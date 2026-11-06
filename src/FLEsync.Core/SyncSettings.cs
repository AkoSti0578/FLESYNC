using System.Text.Json;
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

public sealed class SyncSettings
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public SyncMode Mode { get; set; } = SyncMode.TwoWay;

    /// <summary>Alleen bij one-way: verwijder bestanden in het doel die niet (meer) in de bron staan.</summary>
    public bool MirrorDeletes { get; set; }

    public ScheduleType Schedule { get; set; } = ScheduleType.Interval;

    /// <summary>Interval in minuten bij <see cref="ScheduleType.Interval"/>.</summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>Tijdstip (HH:mm) bij <see cref="ScheduleType.Daily"/>.</summary>
    public string DailyTime { get; set; } = "02:00";

    /// <summary>Bestandspatronen die worden overgeslagen (bijv. *.tmp, ~$*).</summary>
    public List<string> ExcludePatterns { get; set; } = new() { "*.tmp", "~$*", "Thumbs.db", "desktop.ini" };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(TargetPath);

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
        return $"{mode}, {when}";
    }
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
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string StatusFile => Path.Combine(DataDirectory, "status.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

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
                return JsonSerializer.Deserialize<SyncSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new();
        }
        catch
        {
            // Corrupte of half geschreven configuratie: val terug op standaardwaarden.
        }
        return new SyncSettings();
    }

    public static void Save(SyncSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var tmp = SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, SettingsFile, overwrite: true);
    }

    public static DateTime GetSettingsTimestamp() =>
        File.Exists(SettingsFile) ? File.GetLastWriteTimeUtc(SettingsFile) : DateTime.MinValue;
}
