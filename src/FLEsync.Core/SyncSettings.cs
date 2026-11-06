using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FLEsync.Core;

/// <summary>Richting van de synchronisatie.</summary>
public enum SyncMode
{
    /// <summary>Beide locaties worden gelijk gehouden (wijzigingen in beide richtingen).</summary>
    TwoWay,
    /// <summary>Alleen van bron (A) naar doel (B).</summary>
    OneWay
}

/// <summary>Wanneer de service synchroniseert.</summary>
public enum ScheduleType
{
    /// <summary>Elke X minuten.</summary>
    Interval,
    /// <summary>Elke dag op een vast tijdstip.</summary>
    Daily
}

/// <summary>Eén synchronisatie: een bron/doel-paar met eigen richting en schema.</summary>
public sealed class SyncJob
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public SyncMode Mode { get; set; } = SyncMode.TwoWay;

    /// <summary>Alleen bij one-way: verwijder bestanden in het doel die niet (meer) in de bron staan.</summary>
    public bool MirrorDeletes { get; set; }

    /// <summary>
    /// Als het doel (B) leeg is terwijl er bij de vorige sync wel bestanden waren, wordt de bron (A)
    /// ook leeggemaakt. Werkt bij tweerichtings en one-way. Standaard uit.
    /// </summary>
    public bool EmptySourceWhenTargetEmpty { get; set; }

    public ScheduleType Schedule { get; set; } = ScheduleType.Interval;

    /// <summary>Interval in minuten bij <see cref="ScheduleType.Interval"/>.</summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>Tijdstip (HH:mm) bij <see cref="ScheduleType.Daily"/>.</summary>
    public string DailyTime { get; set; } = "02:00";

    /// <summary>Bestandspatronen die worden overgeslagen (bijv. *.tmp, ~$*).</summary>
    public List<string> ExcludePatterns { get; set; } = new() { "*.tmp", "~$*", "Thumbs.db", "desktop.ini" };

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Synchronisatie {Id}" : Name.Trim();

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(TargetPath);

    /// <summary>Ingeschakeld én volledig ingesteld.</summary>
    [JsonIgnore]
    public bool IsActive => Enabled && IsConfigured;

    public TimeSpan GetDailyTime() =>
        TimeSpan.TryParse(DailyTime, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t
            : new TimeSpan(2, 0, 0);

    /// <summary>Berekent het volgende synchronisatiemoment na <paramref name="lastRun"/>.</summary>
    public DateTime GetNextRun(DateTime? lastRun, DateTime now)
    {
        if (Schedule == ScheduleType.Interval)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes));
            return lastRun is null ? now : lastRun.Value + interval;
        }

        var time = GetDailyTime();
        var todayRun = now.Date + time;
        if (lastRun is null)
            return now >= todayRun ? todayRun.AddDays(1) : todayRun;

        var next = lastRun.Value.Date + time;
        if (next <= lastRun.Value) next = next.AddDays(1);
        return next;
    }

    public string Describe()
    {
        var mode = Mode == SyncMode.TwoWay ? "tweerichtings" : (MirrorDeletes ? "one-way (spiegelen)" : "one-way");
        var when = Schedule == ScheduleType.Interval
            ? $"elke {IntervalMinutes} min"
            : $"dagelijks om {GetDailyTime():hh\\:mm}";
        return EmptySourceWhenTargetEmpty ? $"{mode}, {when}, bron leeg als doel leeg" : $"{mode}, {when}";
    }
}

public sealed class SyncSettings
{
    /// <summary>Aantal synchronisaties dat FLEsync beheert.</summary>
    public const int JobCount = 2;

    public List<SyncJob> Jobs { get; set; } = new();

    /// <summary>Zorgt dat er precies <see cref="JobCount"/> jobs met Id 1..N zijn.</summary>
    public SyncSettings Normalize()
    {
        var jobs = new List<SyncJob>();
        for (int id = 1; id <= JobCount; id++)
        {
            var job = Jobs.FirstOrDefault(j => j.Id == id) ?? new SyncJob { Id = id, Enabled = id == 1 };
            job.Id = id;
            jobs.Add(job);
        }
        Jobs = jobs;
        return this;
    }

    public SyncJob Job(int id) => Jobs.First(j => j.Id == id);
}

/// <summary>Leest en schrijft de instellingen in %ProgramData%\FLEsync zodat app en service dezelfde configuratie delen.</summary>
public static class SettingsStore
{
    public const string ServiceName = "FLEsync";

    /// <summary>%ProgramData%\FLEsync (te overschrijven met omgevingsvariabele FLESYNC_DATA_DIR, o.a. voor tests).</summary>
    public static string DataDirectory { get; set; } =
        Environment.GetEnvironmentVariable("FLESYNC_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FLEsync");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string StateFile(int jobId) => Path.Combine(DataDirectory, $"state-{jobId}.json");
    public static string StatusFile(int jobId) => Path.Combine(DataDirectory, $"status-{jobId}.json");
    public static string LockFile(int jobId) => Path.Combine(DataDirectory, $"sync-{jobId}.lock");

    // Bestanden van versie 1.0 (één synchronisatie); worden automatisch overgenomen als job 1.
    internal static string LegacyStateFile => Path.Combine(DataDirectory, "state.json");
    internal static string LegacyStatusFile => Path.Combine(DataDirectory, "status.json");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static SyncSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var node = JsonNode.Parse(File.ReadAllText(SettingsFile));
                if (node is JsonObject obj && !obj.ContainsKey(nameof(SyncSettings.Jobs)))
                {
                    // Oud formaat: de instellingen zelf waren één synchronisatie.
                    var legacy = obj.Deserialize<SyncJob>(JsonOptions) ?? new SyncJob();
                    legacy.Id = 1;
                    legacy.Enabled = true;
                    return new SyncSettings { Jobs = { legacy } }.Normalize();
                }
                return (node.Deserialize<SyncSettings>(JsonOptions) ?? new SyncSettings()).Normalize();
            }
        }
        catch
        {
            // Corrupte of half geschreven configuratie: val terug op standaardwaarden.
        }
        return new SyncSettings().Normalize();
    }

    public static void Save(SyncSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var tmp = SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(tmp, SettingsFile, overwrite: true);
    }

    public static DateTime GetSettingsTimestamp() =>
        File.Exists(SettingsFile) ? File.GetLastWriteTimeUtc(SettingsFile) : DateTime.MinValue;
}
