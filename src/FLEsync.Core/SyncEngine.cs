using System.IO.Enumeration;
using System.Text.Json;

namespace FLEsync.Core;

public sealed class SyncResult
{
    public int CopiedToTarget { get; set; }
    public int CopiedToSource { get; set; }
    public int Deleted { get; set; }
    public int Conflicts { get; set; }
    public int Errors { get; set; }
    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public TimeSpan Duration { get; set; }

    public bool Success => !Aborted && Errors == 0;

    public override string ToString() => Aborted
        ? $"Afgebroken: {AbortReason}"
        : $"{CopiedToTarget} naar doel, {CopiedToSource} naar bron, {Deleted} verwijderd, " +
          $"{Conflicts} conflict(en), {Errors} fout(en) in {Duration.TotalSeconds:0.0}s";
}

/// <summary>
/// Synchroniseert twee mappen. Bij tweerichtings-sync wordt een staat van de vorige run bijgehouden
/// zodat verwijderingen correct worden doorgevoerd. Bij conflicten wint de nieuwste versie en wordt
/// de oudere bewaard als "(conflict ...)"-kopie.
/// </summary>
public sealed class SyncEngine
{
    private const string TempSuffix = ".flesync.tmp";
    private static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2); // FAT/SMB-precisie
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly SyncJob _settings;
    private readonly string _source;
    private readonly string _target;
    private readonly string _tag;

    public SyncEngine(SyncJob settings)
    {
        _settings = settings;
        _tag = $"[{settings.DisplayName}] ";
        _source = Normalize(settings.SourcePath);
        _target = Normalize(settings.TargetPath);
    }

    /// <summary>
    /// Voert een synchronisatie uit. Gebruikt een lock-bestand per job zodat app en service
    /// dezelfde job niet tegelijk syncen.
    /// </summary>
    public static SyncResult RunExclusive(SyncJob settings, string runBy, CancellationToken ct = default)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        var lockPath = SettingsStore.LockFile(settings.Id);
        var tag = $"[{settings.DisplayName}] ";
        FileStream? lockFile;
        try
        {
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return new SyncResult { Aborted = true, AbortReason = "Deze synchronisatie loopt al." };
        }

        using (lockFile)
        {
            SyncLog.Info(tag + $"Synchronisatie gestart ({runBy}): {settings.SourcePath} <-> {settings.TargetPath} [{settings.Describe()}]");
            SyncResult result;
            try
            {
                result = new SyncEngine(settings).Run(ct);
            }
            catch (OperationCanceledException)
            {
                result = new SyncResult { Aborted = true, AbortReason = "Geannuleerd." };
            }
            catch (Exception ex)
            {
                SyncLog.Error(tag + ex);
                result = new SyncResult { Aborted = true, AbortReason = ex.Message };
            }

            if (result.Success) SyncLog.Info(tag + "Synchronisatie klaar: " + result);
            else SyncLog.Error(tag + "Synchronisatie klaar met problemen: " + result);

            new SyncStatus
            {
                LastRun = DateTime.Now,
                Success = result.Success,
                Summary = result.ToString(),
                RunBy = runBy
            }.Save(settings.Id);
            return result;
        }
    }

    public SyncResult Run(CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var result = new SyncResult();

        if (!Validate(result)) return result;

        if (_settings.Mode == SyncMode.OneWay)
            RunOneWay(result, ct);
        else
            RunTwoWay(result, ct);

        result.Duration = DateTime.UtcNow - started;
        return result;
    }

    // ---------------------------------------------------------------- validatie

    private bool Validate(SyncResult result)
    {
        if (!_settings.IsConfigured)
            return Abort(result, "Bron- en doellocatie moeten beide ingesteld zijn.");

        if (PathComparer.Equals(_source, _target))
            return Abort(result, "Bron en doel zijn dezelfde map.");

        if (IsNested(_source, _target) || IsNested(_target, _source))
            return Abort(result, "Bron en doel mogen niet in elkaar liggen.");

        if (!Directory.Exists(_source))
        {
            if (_settings.Mode == SyncMode.TwoWay && !HasState())
            {
                Directory.CreateDirectory(_source);
            }
            else
            {
                return Abort(result, $"Bronlocatie niet bereikbaar: {_source}");
            }
        }

        if (!Directory.Exists(_target))
        {
            if (HasState() && _settings.Mode == SyncMode.TwoWay)
                return Abort(result, $"Doellocatie niet bereikbaar: {_target}");
            try { Directory.CreateDirectory(_target); }
            catch (Exception ex) { return Abort(result, $"Doellocatie kan niet worden aangemaakt: {ex.Message}"); }
        }
        return true;
    }

    private static bool Abort(SyncResult result, string reason)
    {
        result.Aborted = true;
        result.AbortReason = reason;
        return false;
    }

    // ---------------------------------------------------------------- one-way

    private void RunOneWay(SyncResult result, CancellationToken ct)
    {
        var src = Scan(_source);
        var dst = Scan(_target);

        if (_settings.MirrorDeletes && src.Files.Count == 0 && src.Dirs.Count == 0 && dst.Files.Count > 0)
        {
            Abort(result, "Bron is leeg terwijl het doel bestanden bevat; spiegelen afgebroken als veiligheidsmaatregel.");
            return;
        }

        foreach (var dir in src.Dirs.OrderBy(d => d.Length))
            if (!dst.Dirs.Contains(dir))
                Try(result, $"map aanmaken {dir}", () => Directory.CreateDirectory(Path.Combine(_target, dir)));

        foreach (var (rel, a) in src.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (dst.Files.TryGetValue(rel, out var b) && Same(a, b)) continue;
            if (Try(result, $"kopiëren {rel}", () => CopyFile(_source, _target, rel)))
                result.CopiedToTarget++;
        }

        if (!_settings.MirrorDeletes) return;

        foreach (var rel in dst.Files.Keys.Where(k => !src.Files.ContainsKey(k)).ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (Try(result, $"verwijderen {rel}", () => DeleteFile(Path.Combine(_target, rel))))
                result.Deleted++;
        }

        foreach (var dir in dst.Dirs.Where(d => !src.Dirs.Contains(d)).OrderByDescending(d => d.Length))
            Try(result, $"map verwijderen {dir}", () => DeleteDirIfEmpty(Path.Combine(_target, dir)));
    }

    // ---------------------------------------------------------------- two-way

    private void RunTwoWay(SyncResult result, CancellationToken ct)
    {
        var state = LoadState();
        var a = Scan(_source);
        var b = Scan(_target);

        // Veiligheid: als een kant ineens helemaal leeg is terwijl er eerder bestanden waren,
        // is die locatie waarschijnlijk niet goed bereikbaar. Dan niets verwijderen.
        if (state.Files.Count > 0 && (a.Files.Count == 0 || b.Files.Count == 0))
        {
            var empty = a.Files.Count == 0 ? _source : _target;
            Abort(result, $"Locatie {empty} is onverwacht leeg; afgebroken om massaal verwijderen te voorkomen.");
            return;
        }

        var failed = new HashSet<string>(PathComparer);
        var allFiles = new HashSet<string>(a.Files.Keys, PathComparer);
        allFiles.UnionWith(b.Files.Keys);

        foreach (var rel in allFiles.OrderBy(p => p, PathComparer))
        {
            ct.ThrowIfCancellationRequested();
            a.Files.TryGetValue(rel, out var fa);
            b.Files.TryGetValue(rel, out var fb);
            state.Files.TryGetValue(rel, out var fs);

            bool ok = true;
            if (fa != null && fb != null)
            {
                if (Same(fa, fb)) continue;
                bool changedA = fs == null || !Same(fa, fs);
                bool changedB = fs == null || !Same(fb, fs);

                if (changedA && !changedB)
                {
                    ok = Try(result, $"kopiëren naar doel {rel}", () => CopyFile(_source, _target, rel));
                    if (ok) result.CopiedToTarget++;
                }
                else if (changedB && !changedA)
                {
                    ok = Try(result, $"kopiëren naar bron {rel}", () => CopyFile(_target, _source, rel));
                    if (ok) result.CopiedToSource++;
                }
                else
                {
                    // Conflict: beide gewijzigd. Nieuwste wint, oudere blijft bewaard als conflict-kopie.
                    bool aNewer = fa.LastWriteUtc >= fb.LastWriteUtc;
                    var (winRoot, loseRoot) = aNewer ? (_source, _target) : (_target, _source);
                    ok = Try(result, $"conflict oplossen {rel}", () =>
                    {
                        PreserveConflict(Path.Combine(loseRoot, rel));
                        CopyFile(winRoot, loseRoot, rel);
                    });
                    if (ok)
                    {
                        result.Conflicts++;
                        if (aNewer) result.CopiedToTarget++; else result.CopiedToSource++;
                        SyncLog.Warn(_tag + $"Conflict in {rel}: nieuwste versie ({(aNewer ? "bron" : "doel")}) behouden, oudere als conflict-kopie bewaard.");
                    }
                }
            }
            else if (fa != null)
            {
                ok = HandleOneSide(result, rel, fa, fs, _source, _target, toTarget: true);
            }
            else if (fb != null)
            {
                ok = HandleOneSide(result, rel, fb, fs, _target, _source, toTarget: false);
            }

            if (!ok) failed.Add(rel);
        }

        // Mappen
        SyncDirectories(result, a.Dirs, b.Dirs, state.Dirs, _source, _target);
        SyncDirectories(result, b.Dirs, a.Dirs, state.Dirs, _target, _source);

        // Nieuwe staat opbouwen op basis van de werkelijke situatie na de sync.
        var a2 = Scan(_source);
        var b2 = Scan(_target);
        var newState = new SyncStateData { SourcePath = _source, TargetPath = _target };
        foreach (var (rel, fa2) in a2.Files)
            if (b2.Files.TryGetValue(rel, out var fb2) && Same(fa2, fb2))
                newState.Files[rel] = fa2;
        foreach (var rel in failed)
            if (!newState.Files.ContainsKey(rel) && state.Files.TryGetValue(rel, out var old))
                newState.Files[rel] = old;
        foreach (var d in a2.Dirs)
            if (b2.Dirs.Contains(d)) newState.Dirs.Add(d);

        SaveState(newState);
    }

    /// <summary>Bestand bestaat alleen aan één kant: nieuw (kopiëren) of aan de andere kant verwijderd.</summary>
    private bool HandleOneSide(SyncResult result, string rel, FileEntry present, FileEntry? stateEntry,
        string presentRoot, string otherRoot, bool toTarget)
    {
        if (stateEntry != null && Same(present, stateEntry))
        {
            // Was eerder gesynchroniseerd en is aan de andere kant verwijderd -> hier ook verwijderen.
            var ok = Try(result, $"verwijderen {rel}", () => DeleteFile(Path.Combine(presentRoot, rel)));
            if (ok) result.Deleted++;
            return ok;
        }

        // Nieuw bestand, of gewijzigd terwijl de andere kant het verwijderde (wijziging wint).
        var copied = Try(result, $"kopiëren {rel}", () => CopyFile(presentRoot, otherRoot, rel));
        if (copied)
        {
            if (toTarget) result.CopiedToTarget++; else result.CopiedToSource++;
        }
        return copied;
    }

    private void SyncDirectories(SyncResult result, HashSet<string> here, HashSet<string> there,
        HashSet<string> stateDirs, string hereRoot, string thereRoot)
    {
        foreach (var dir in here.Where(d => !there.Contains(d)).OrderByDescending(d => d.Length))
        {
            var herePath = Path.Combine(hereRoot, dir);
            var therePath = Path.Combine(thereRoot, dir);
            if (Directory.Exists(therePath) || !Directory.Exists(herePath)) continue;

            if (stateDirs.Contains(dir))
                Try(result, $"map verwijderen {dir}", () => DeleteDirIfEmpty(herePath));
            else
                Try(result, $"map aanmaken {dir}", () => Directory.CreateDirectory(therePath));
        }
    }

    // ---------------------------------------------------------------- bestandsoperaties

    private static void CopyFile(string fromRoot, string toRoot, string rel)
    {
        var from = Path.Combine(fromRoot, rel);
        var to = Path.Combine(toRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);

        var tmp = to + TempSuffix;
        File.Copy(from, tmp, overwrite: true);
        var info = new FileInfo(from);
        File.SetLastWriteTimeUtc(tmp, info.LastWriteTimeUtc);

        if (File.Exists(to))
        {
            var attr = File.GetAttributes(to);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(to, attr & ~FileAttributes.ReadOnly);
        }
        File.Move(tmp, to, overwrite: true);
        File.SetAttributes(to, info.Attributes & ~FileAttributes.Directory);
    }

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }

    private static void DeleteDirIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }

    private static void PreserveConflict(string path)
    {
        if (!File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var conflict = Path.Combine(dir, $"{name} (conflict {Environment.MachineName} {DateTime.Now:yyyyMMdd-HHmmss}){ext}");
        File.Copy(path, conflict, overwrite: true);
    }

    private bool Try(SyncResult result, string what, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors++;
            SyncLog.Warn(_tag + $"Mislukt: {what} - {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------- scannen

    private sealed class Snapshot
    {
        public Dictionary<string, FileEntry> Files { get; } = new(PathComparer);
        public HashSet<string> Dirs { get; } = new(PathComparer);
    }

    private Snapshot Scan(string root)
    {
        var snap = new Snapshot();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SyncLog.Warn(_tag + $"Map niet leesbaar: {dir} - {ex.Message}");
                continue;
            }

            foreach (var entry in entries)
            {
                var rel = Path.GetRelativePath(root, entry.FullName);
                if (IsExcluded(rel, entry.Name)) continue;

                if (entry is DirectoryInfo)
                {
                    snap.Dirs.Add(rel);
                    pending.Push(entry.FullName);
                }
                else if (entry is FileInfo fi)
                {
                    snap.Files[rel] = new FileEntry(fi.Length, fi.LastWriteTimeUtc);
                }
            }
        }
        return snap;
    }

    private bool IsExcluded(string rel, string name)
    {
        if (name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var pattern in _settings.ExcludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var p = pattern.Trim();
            var subject = p.Contains('\\') || p.Contains('/') ? rel.Replace('/', '\\') : name;
            if (FileSystemName.MatchesSimpleExpression(p.Replace('/', '\\'), subject, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static bool Same(FileEntry x, FileEntry y) =>
        x.Size == y.Size && (x.LastWriteUtc - y.LastWriteUtc).Duration() <= TimeTolerance;

    // ---------------------------------------------------------------- staat

    private bool HasState() => LoadState().Files.Count > 0;

    private SyncStateData LoadState()
    {
        try
        {
            var file = SettingsStore.StateFile(_settings.Id);
            if (!File.Exists(file) && _settings.Id == 1 && File.Exists(SettingsStore.LegacyStateFile))
                file = SettingsStore.LegacyStateFile; // overname van versie 1.0
            if (File.Exists(file))
            {
                var s = JsonSerializer.Deserialize<SyncStateData>(File.ReadAllText(file), SettingsStore.JsonOptions);
                if (s != null && PathComparer.Equals(s.SourcePath, _source) && PathComparer.Equals(s.TargetPath, _target))
                    return s.WithComparer();
            }
        }
        catch (Exception ex)
        {
            SyncLog.Warn(_tag + "Staatbestand onleesbaar, wordt opnieuw opgebouwd: " + ex.Message);
        }
        return new SyncStateData { SourcePath = _source, TargetPath = _target };
    }

    private void SaveState(SyncStateData state)
    {
        var file = SettingsStore.StateFile(_settings.Id);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, SettingsStore.JsonOptions));
        File.Move(tmp, file, overwrite: true);
    }

    // ---------------------------------------------------------------- helpers

    private static string Normalize(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    private static bool IsNested(string parent, string child) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public sealed record FileEntry(long Size, DateTime LastWriteUtc);

public sealed class SyncStateData
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public Dictionary<string, FileEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Dirs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal SyncStateData WithComparer() => new()
    {
        SourcePath = SourcePath,
        TargetPath = TargetPath,
        Files = new Dictionary<string, FileEntry>(Files, StringComparer.OrdinalIgnoreCase),
        Dirs = new HashSet<string>(Dirs, StringComparer.OrdinalIgnoreCase)
    };
}
