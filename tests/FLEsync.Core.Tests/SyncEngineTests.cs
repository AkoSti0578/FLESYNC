using FLEsync.Core;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FLEsync.Core.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flesync-test-" + Guid.NewGuid().ToString("N"));
    private string A => Path.Combine(_root, "A");
    private string B => Path.Combine(_root, "B");

    public SyncEngineTests()
    {
        Directory.CreateDirectory(A);
        Directory.CreateDirectory(B);
        SettingsStore.DataDirectory = Path.Combine(_root, "data");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SyncJob Settings(SyncMode mode, bool mirror = false) =>
        new() { Id = 1, SourcePath = A, TargetPath = B, Mode = mode, MirrorDeletes = mirror };

    private static SyncResult Run(SyncJob s) => SyncEngine.RunExclusive(s, "test");

    private static void Write(string path, string content, DateTime? time = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, time ?? DateTime.UtcNow);
    }

    [Fact]
    public void TwoWay_CopiesNewFilesBothDirections()
    {
        Write(Path.Combine(A, "a.txt"), "from A");
        Write(Path.Combine(B, "sub", "b.txt"), "from B");
        Directory.CreateDirectory(Path.Combine(A, "empty"));

        var r = Run(Settings(SyncMode.TwoWay));

        Assert.True(r.Success, r.ToString());
        Assert.Equal("from A", File.ReadAllText(Path.Combine(B, "a.txt")));
        Assert.Equal("from B", File.ReadAllText(Path.Combine(A, "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(B, "empty")));
    }

    [Fact]
    public void TwoWay_PropagatesDeletesAndModifications()
    {
        Write(Path.Combine(A, "keep.txt"), "v1", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(A, "gone.txt"), "x", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(A, "dir", "f.txt"), "x", DateTime.UtcNow.AddHours(-1));
        Assert.True(Run(Settings(SyncMode.TwoWay)).Success);

        File.Delete(Path.Combine(B, "gone.txt"));
        Directory.Delete(Path.Combine(B, "dir"), true);
        Write(Path.Combine(B, "keep.txt"), "v2 from B");

        var r = Run(Settings(SyncMode.TwoWay));

        Assert.True(r.Success, r.ToString());
        Assert.False(File.Exists(Path.Combine(A, "gone.txt")));
        Assert.False(Directory.Exists(Path.Combine(A, "dir")));
        Assert.Equal("v2 from B", File.ReadAllText(Path.Combine(A, "keep.txt")));
    }

    [Fact]
    public void TwoWay_ConflictKeepsNewestAndPreservesOlder()
    {
        Write(Path.Combine(A, "doc.txt"), "base", DateTime.UtcNow.AddHours(-2));
        Assert.True(Run(Settings(SyncMode.TwoWay)).Success);

        Write(Path.Combine(A, "doc.txt"), "older edit A", DateTime.UtcNow.AddMinutes(-30));
        Write(Path.Combine(B, "doc.txt"), "newer edit B!", DateTime.UtcNow);

        var r = Run(Settings(SyncMode.TwoWay));

        Assert.Equal(1, r.Conflicts);
        Assert.Equal("newer edit B!", File.ReadAllText(Path.Combine(A, "doc.txt")));
        Assert.Contains(Directory.GetFiles(A), f => Path.GetFileName(f).StartsWith("doc (conflict"));
    }

    [Fact]
    public void TwoWay_AbortsWhenOneSideSuddenlyEmpty()
    {
        Write(Path.Combine(A, "a.txt"), "1");
        Write(Path.Combine(A, "b.txt"), "2");
        Assert.True(Run(Settings(SyncMode.TwoWay)).Success);

        foreach (var f in Directory.GetFiles(B)) File.Delete(f);

        var r = Run(Settings(SyncMode.TwoWay));

        Assert.True(r.Aborted);
        Assert.True(File.Exists(Path.Combine(A, "a.txt")));
    }

    [Fact]
    public void OneWay_DoesNotTouchExtraTargetFilesWithoutMirror()
    {
        Write(Path.Combine(A, "a.txt"), "A");
        Write(Path.Combine(B, "extra.txt"), "B only");

        var r = Run(Settings(SyncMode.OneWay));

        Assert.True(r.Success, r.ToString());
        Assert.True(File.Exists(Path.Combine(B, "a.txt")));
        Assert.True(File.Exists(Path.Combine(B, "extra.txt")));
        Assert.False(File.Exists(Path.Combine(A, "extra.txt")));
    }

    [Fact]
    public void OneWay_MirrorRemovesExtraTargetFiles()
    {
        Write(Path.Combine(A, "a.txt"), "A");
        Write(Path.Combine(B, "old", "extra.txt"), "B only");

        var r = Run(Settings(SyncMode.OneWay, mirror: true));

        Assert.True(r.Success, r.ToString());
        Assert.True(File.Exists(Path.Combine(B, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(B, "old")));
    }

    [Fact]
    public void ExcludedFilesAreSkipped()
    {
        Write(Path.Combine(A, "a.tmp"), "tmp");
        Write(Path.Combine(A, "a.txt"), "ok");

        Run(Settings(SyncMode.OneWay));

        Assert.False(File.Exists(Path.Combine(B, "a.tmp")));
        Assert.True(File.Exists(Path.Combine(B, "a.txt")));
    }

    [Fact]
    public void RejectsNestedLocations()
    {
        var s = new SyncJob { SourcePath = A, TargetPath = Path.Combine(A, "inner"), Mode = SyncMode.OneWay };
        Assert.True(Run(s).Aborted);
    }

    [Fact]
    public void Schedule_IntervalAndDaily()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        var s = new SyncJob { Schedule = ScheduleType.Interval, IntervalMinutes = 30 };
        Assert.Equal(now, s.GetNextRun(null, now));
        Assert.Equal(now.AddMinutes(30), s.GetNextRun(now, now));

        s = new SyncJob { Schedule = ScheduleType.Daily, DailyTime = "02:30" };
        Assert.Equal(new DateTime(2026, 1, 2, 2, 30, 0), s.GetNextRun(null, now));
        Assert.Equal(new DateTime(2026, 1, 1, 2, 30, 0), s.GetNextRun(null, new DateTime(2026, 1, 1, 1, 0, 0)));
        Assert.Equal(new DateTime(2026, 1, 2, 2, 30, 0), s.GetNextRun(new DateTime(2026, 1, 1, 2, 30, 5), now));
    }

    [Fact]
    public void TwoJobs_AreIndependent()
    {
        var c = Path.Combine(_root, "C");
        var d = Path.Combine(_root, "D");
        Directory.CreateDirectory(c);
        Directory.CreateDirectory(d);

        var job1 = Settings(SyncMode.TwoWay);
        var job2 = new SyncJob { Id = 2, SourcePath = c, TargetPath = d, Mode = SyncMode.TwoWay };

        Write(Path.Combine(A, "one.txt"), "1", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(c, "two.txt"), "2", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(c, "stays.txt"), "s", DateTime.UtcNow.AddHours(-1));
        Assert.True(Run(job1).Success);
        Assert.True(Run(job2).Success);

        Assert.True(File.Exists(Path.Combine(B, "one.txt")));
        Assert.True(File.Exists(Path.Combine(d, "two.txt")));
        Assert.False(File.Exists(Path.Combine(B, "two.txt")));

        // Verwijderen in job 2 werkt met de eigen staat en raakt job 1 niet.
        File.Delete(Path.Combine(d, "two.txt"));
        Assert.True(Run(job2).Success);
        Assert.False(File.Exists(Path.Combine(c, "two.txt")));
        Assert.True(File.Exists(Path.Combine(A, "one.txt")));

        Assert.True(File.Exists(SettingsStore.StateFile(1)));
        Assert.True(File.Exists(SettingsStore.StateFile(2)));
        Assert.NotNull(SyncStatus.Load(2).LastRun);
    }

    [Fact]
    public void Settings_SaveLoadRoundTripWithTwoJobs()
    {
        var settings = new SyncSettings().Normalize();
        settings.Job(1).SourcePath = A;
        settings.Job(1).TargetPath = B;
        settings.Job(2).Name = "Archief";
        settings.Job(2).Enabled = true;
        settings.Job(2).Mode = SyncMode.OneWay;
        settings.Job(2).Schedule = ScheduleType.Daily;
        settings.Job(2).DailyTime = "23:15";
        SettingsStore.Save(settings);

        var loaded = SettingsStore.Load();
        Assert.Equal(SyncSettings.JobCount, loaded.Jobs.Count);
        Assert.Equal(A, loaded.Job(1).SourcePath);
        Assert.Equal("Archief", loaded.Job(2).DisplayName);
        Assert.Equal(SyncMode.OneWay, loaded.Job(2).Mode);
        Assert.Equal(new TimeSpan(23, 15, 0), loaded.Job(2).GetDailyTime());
        Assert.False(loaded.Job(2).IsActive); // geen locaties
    }

    [Fact]
    public void Settings_LegacySingleSyncFileBecomesJob1()
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        File.WriteAllText(SettingsStore.SettingsFile,
            $$"""{ "SourcePath": {{System.Text.Json.JsonSerializer.Serialize(A)}}, "TargetPath": {{System.Text.Json.JsonSerializer.Serialize(B)}}, "Mode": "OneWay", "IntervalMinutes": 42 }""");

        var loaded = SettingsStore.Load();

        Assert.Equal(2, loaded.Jobs.Count);
        Assert.True(loaded.Job(1).IsActive);
        Assert.Equal(SyncMode.OneWay, loaded.Job(1).Mode);
        Assert.Equal(42, loaded.Job(1).IntervalMinutes);
        Assert.False(loaded.Job(2).Enabled);
    }

    private void SeedAndSync(SyncJob job)
    {
        Write(Path.Combine(A, "a.txt"), "a", DateTime.UtcNow.AddHours(-1));
        Write(Path.Combine(A, "sub", "b.txt"), "b", DateTime.UtcNow.AddHours(-1));
        Assert.True(Run(job).Success);
        Assert.True(File.Exists(Path.Combine(B, "sub", "b.txt")));
    }

    private void EmptyTarget()
    {
        foreach (var e in Directory.EnumerateFileSystemEntries(B))
            if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e);
    }

    [Theory]
    [InlineData(SyncMode.TwoWay)]
    [InlineData(SyncMode.OneWay)]
    public void EmptyTarget_EmptiesSource_WhenOptionEnabled(SyncMode mode)
    {
        var job = Settings(mode);
        job.EmptySourceWhenTargetEmpty = true;
        SeedAndSync(job);
        Write(Path.Combine(A, "skip.tmp"), "excluded");

        EmptyTarget();
        var r = Run(job);

        Assert.True(r.Success, r.ToString());
        Assert.True(r.SourceEmptied);
        Assert.False(File.Exists(Path.Combine(A, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(A, "sub")));
        Assert.True(File.Exists(Path.Combine(A, "skip.tmp"))); // uitgesloten blijft staan

        // Daarna gewoon verder: nieuw bestand in A gaat weer naar B.
        Write(Path.Combine(A, "new.txt"), "n");
        Assert.True(Run(job).Success);
        Assert.True(File.Exists(Path.Combine(B, "new.txt")));
    }

    [Fact]
    public void EmptyTarget_TwoWayWithoutOption_StillAborts()
    {
        var job = Settings(SyncMode.TwoWay);
        SeedAndSync(job);
        EmptyTarget();

        var r = Run(job);

        Assert.True(r.Aborted);
        Assert.True(File.Exists(Path.Combine(A, "a.txt")));
    }

    [Fact]
    public void EmptyTarget_OneWayWithoutOption_RecopiesFiles()
    {
        var job = Settings(SyncMode.OneWay);
        SeedAndSync(job);
        EmptyTarget();

        Assert.True(Run(job).Success);
        Assert.True(File.Exists(Path.Combine(A, "a.txt")));
        Assert.True(File.Exists(Path.Combine(B, "a.txt")));
    }

    [Fact]
    public void EmptyTarget_FirstSync_NeverEmptiesSource()
    {
        var job = Settings(SyncMode.OneWay);
        job.EmptySourceWhenTargetEmpty = true;
        Write(Path.Combine(A, "a.txt"), "a");

        var r = Run(job);

        Assert.False(r.SourceEmptied);
        Assert.True(File.Exists(Path.Combine(A, "a.txt")));
        Assert.True(File.Exists(Path.Combine(B, "a.txt")));
    }

    [Fact]
    public void MissingTarget_WithOption_AbortsInsteadOfEmptyingSource()
    {
        var job = Settings(SyncMode.OneWay);
        job.EmptySourceWhenTargetEmpty = true;
        SeedAndSync(job);
        Directory.Delete(B, true); // bv. share niet bereikbaar

        var r = Run(job);

        Assert.True(r.Aborted);
        Assert.True(File.Exists(Path.Combine(A, "a.txt")));
    }
}
