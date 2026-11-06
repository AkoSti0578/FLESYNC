using FLEsync.Core;

namespace FLEsync.Service;

/// <summary>
/// Achtergrondservice: leest periodiek de instellingen uit %ProgramData%\FLEsync\settings.json
/// en voert elke ingeschakelde synchronisatie uit volgens haar eigen schema. Synchronisaties lopen
/// onafhankelijk van elkaar (een lange sync houdt de andere niet op). Wijzigingen in de instellingen
/// worden automatisch opgepikt zonder herstart.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private sealed class JobRuntime
    {
        public SyncJob Job = new();
        public DateTime? LastRun;
        public DateTime NextRun = DateTime.MaxValue;
        public Task? Running;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SyncLog.Info("FLEsync-service gestart.");
        SyncLog.Cleanup();

        var settingsStamp = DateTime.MinValue;
        var jobs = new Dictionary<int, JobRuntime>();

        while (!stoppingToken.IsCancellationRequested)
        {
            var stamp = SettingsStore.GetSettingsTimestamp();
            if (stamp != settingsStamp)
            {
                settingsStamp = stamp;
                foreach (var job in SettingsStore.Load().Jobs)
                {
                    if (!jobs.TryGetValue(job.Id, out var rt))
                    {
                        // Laatst bekende run gebruiken zodat een herstart niet direct een sync forceert.
                        rt = new JobRuntime { LastRun = SyncStatus.Load(job.Id).LastRun };
                        jobs[job.Id] = rt;
                    }
                    rt.Job = job;
                    rt.NextRun = job.IsActive ? job.GetNextRun(rt.LastRun, DateTime.Now) : DateTime.MaxValue;
                    SyncLog.Info(job.IsActive
                        ? $"[{job.DisplayName}] Ingesteld ({job.Describe()}). Volgende sync: {Max(rt.NextRun, DateTime.Now):dd-MM-yyyy HH:mm}"
                        : $"[{job.DisplayName}] {(job.Enabled ? "Nog niet ingesteld" : "Uitgeschakeld")}.");
                }
            }

            foreach (var rt in jobs.Values)
            {
                if (rt.Running is { IsCompleted: false }) continue;
                if (!rt.Job.IsActive || DateTime.Now < rt.NextRun) continue;

                var job = rt.Job;
                rt.Running = Task.Run(() =>
                {
                    SyncEngine.RunExclusive(job, "service", stoppingToken);
                    rt.LastRun = DateTime.Now;
                    rt.NextRun = job.GetNextRun(rt.LastRun, DateTime.Now);
                    SyncLog.Info($"[{job.DisplayName}] Volgende sync: {rt.NextRun:dd-MM-yyyy HH:mm}");
                }, stoppingToken);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        var running = jobs.Values.Select(j => j.Running).OfType<Task>().ToArray();
        try { await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(20)); } catch { }
        SyncLog.Info("FLEsync-service gestopt.");
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
