using FLEsync.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Verhoogde aanroep vanuit de UI voor service-beheer (installeren/starten/stoppen/verwijderen).
        if (args.Length == 1 && args[0].StartsWith("--", StringComparison.Ordinal))
            return ServiceManager.Execute(args[0]);

        using var mutex = new Mutex(true, @"Local\FLEsync.App", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("FLEsync is al geopend.", "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
