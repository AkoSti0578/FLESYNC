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

    private SyncSettings Settings(SyncMode mode, bool mirror = false) =>
        new() { SourcePath = A, TargetPath = B, Mode = mode, MirrorDeletes = mirror };

    private SyncResult Run(SyncSettings s) => SyncEngine.RunExclusive(s, "test");

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
        var s = new SyncSettings { SourcePath = A, TargetPath = Path.Combine(A, "inner"), Mode = SyncMode.OneWay };
        Assert.True(Run(s).Aborted);
    }

    [Fact]
    public void Schedule_IntervalAndDaily()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0);
        var s = new SyncSettings { Schedule = ScheduleType.Interval, IntervalMinutes = 30 };
        Assert.Equal(now, s.GetNextRun(null, now));
        Assert.Equal(now.AddMinutes(30), s.GetNextRun(now, now));

        s = new SyncSettings { Schedule = ScheduleType.Daily, DailyTime = "02:30" };
        Assert.Equal(new DateTime(2026, 1, 2, 2, 30, 0), s.GetNextRun(null, now));
        Assert.Equal(new DateTime(2026, 1, 1, 2, 30, 0), s.GetNextRun(null, new DateTime(2026, 1, 1, 1, 0, 0)));
        Assert.Equal(new DateTime(2026, 1, 2, 2, 30, 0), s.GetNextRun(new DateTime(2026, 1, 1, 2, 30, 5), now));
    }
}
