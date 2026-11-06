using FLEsync.Core;

namespace FLEsync.Service;

/// <summary>
/// Achtergrondservice: leest periodiek de instellingen uit %ProgramData%\FLEsync\settings.json
/// en voert de synchronisatie uit volgens het ingestelde schema. Wijzigingen in de instellingen
/// worden automatisch opgepikt zonder herstart.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SyncLog.Info("FLEsync-service gestart.");
        SyncLog.Cleanup();

        var settingsStamp = DateTime.MinValue;
        var settings = new SyncSettings();
        DateTime? lastRun = null;
        DateTime nextRun = DateTime.MaxValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var stamp = SettingsStore.GetSettingsTimestamp();
            if (stamp != settingsStamp)
            {
                settingsStamp = stamp;
                settings = SettingsStore.Load();
                // Laatst bekende run gebruiken zodat een herstart niet direct een sync forceert bij dagelijks schema.
                lastRun ??= SyncStatus.Load().LastRun;
                nextRun = settings.IsConfigured ? settings.GetNextRun(lastRun, DateTime.Now) : DateTime.MaxValue;
                SyncLog.Info(settings.IsConfigured
                    ? $"Instellingen geladen ({settings.Describe()}). Volgende sync: {nextRun:dd-MM-yyyy HH:mm}"
                    : "Nog geen locaties ingesteld; service wacht op configuratie.");
            }

            if (settings.IsConfigured && DateTime.Now >= nextRun)
            {
                await Task.Run(() => SyncEngine.RunExclusive(settings, "service", stoppingToken), stoppingToken)
                    .ContinueWith(_ => { }, TaskScheduler.Default);
                lastRun = DateTime.Now;
                nextRun = settings.GetNextRun(lastRun, DateTime.Now);
                SyncLog.Info($"Volgende sync: {nextRun:dd-MM-yyyy HH:mm}");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        SyncLog.Info("FLEsync-service gestopt.");
    }
}
