using System.Text.Json;

namespace FLEsync.Core;

/// <summary>Eenvoudige dagelijkse logbestanden in %ProgramData%\FLEsync\logs.</summary>
public static class SyncLog
{
    private static readonly object Gate = new();

    /// <summary>Wordt aangeroepen voor elke logregel (gebruikt door de UI).</summary>
    public static event Action<string>? LineWritten;

    public static string CurrentLogFile =>
        Path.Combine(SettingsStore.LogDirectory, $"flesync-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("FOUT", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.LogDirectory);
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
            }
            catch
            {
                // Loggen mag de synchronisatie nooit laten falen.
            }
        }
        LineWritten?.Invoke(line);
    }

    /// <summary>Verwijdert logbestanden ouder dan <paramref name="days"/> dagen.</summary>
    public static void Cleanup(int days = 30)
    {
        try
        {
            if (!Directory.Exists(SettingsStore.LogDirectory)) return;
            foreach (var file in Directory.GetFiles(SettingsStore.LogDirectory, "flesync-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-days))
                    File.Delete(file);
        }
        catch { }
    }

    public static IReadOnlyList<string> ReadTail(int maxLines = 200)
    {
        try
        {
            if (!File.Exists(CurrentLogFile)) return Array.Empty<string>();
            using var fs = new FileStream(CurrentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } l)
            {
                lines.Enqueue(l);
                if (lines.Count > maxLines) lines.Dequeue();
            }
            return lines.ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>Resultaat van de laatste synchronisatie van een job, gedeeld tussen service en app.</summary>
public sealed class SyncStatus
{
    public DateTime? LastRun { get; set; }
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public string RunBy { get; set; } = "";

    public static SyncStatus Load(int jobId)
    {
        try
        {
            var file = SettingsStore.StatusFile(jobId);
            if (!File.Exists(file) && jobId == 1 && File.Exists(SettingsStore.LegacyStatusFile))
                file = SettingsStore.LegacyStatusFile;
            if (File.Exists(file))
                return JsonSerializer.Deserialize<SyncStatus>(File.ReadAllText(file), SettingsStore.JsonOptions) ?? new();
        }
        catch { }
        return new SyncStatus();
    }

    public void Save(int jobId)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            var file = SettingsStore.StatusFile(jobId);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, SettingsStore.JsonOptions));
            File.Move(tmp, file, overwrite: true);
        }
        catch { }
    }
}
