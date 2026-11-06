using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using FLEsync.Core;

namespace FLEsync.App;

/// <summary>
/// Installeert en beheert de FLEsync Windows-service. Handelingen die beheerdersrechten vereisen
/// worden uitgevoerd door FLEsync.exe opnieuw te starten met UAC-verhoging en een opdrachtregelargument.
/// </summary>
internal static class ServiceManager
{
    public const string ArgInstall = "--install-service";
    public const string ArgUninstall = "--uninstall-service";
    public const string ArgStart = "--start-service";
    public const string ArgStop = "--stop-service";
    public const string ArgRestart = "--restart-service";

    public static string ServiceExePath => Path.Combine(AppContext.BaseDirectory, "FLEsync.Service.exe");

    public static ServiceControllerStatus? GetStatus()
    {
        try
        {
            using var sc = new ServiceController(SettingsStore.ServiceName);
            return sc.Status;
        }
        catch (InvalidOperationException)
        {
            return null; // niet geïnstalleerd
        }
    }

    public static bool IsInstalled => GetStatus() != null;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Voert een service-actie uit met verhoogde rechten. Retourneert true bij succes.</summary>
    public static bool RunElevated(string argument, out string error)
    {
        error = "";
        if (IsAdministrator())
        {
            var code = Execute(argument);
            if (code != 0) error = $"Actie mislukt (code {code}). Zie het logboek voor details.";
            return code == 0;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FLEsync.exe"),
                Arguments = argument,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0) error = $"Actie mislukt (code {p.ExitCode}). Zie het logboek voor details.";
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "Geannuleerd: beheerdersrechten zijn nodig om de service te beheren.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Wordt uitgevoerd in het verhoogde proces. Retourneert een exitcode.</summary>
    public static int Execute(string argument)
    {
        try
        {
            switch (argument)
            {
                case ArgInstall: Install(); break;
                case ArgUninstall: Uninstall(); break;
                case ArgStart: Start(); break;
                case ArgStop: Stop(); break;
                case ArgRestart: Stop(); Start(); break;
                default: return 2;
            }
            return 0;
        }
        catch (Exception ex)
        {
            SyncLog.Error($"Service-actie {argument} mislukt: {ex.Message}");
            return 1;
        }
    }

    private static void Install()
    {
        if (!File.Exists(ServiceExePath))
            throw new FileNotFoundException("FLEsync.Service.exe niet gevonden", ServiceExePath);

        if (!IsInstalled)
        {
            Sc($"create {SettingsStore.ServiceName} binPath= \"\\\"{ServiceExePath}\\\"\" start= delayed-auto DisplayName= \"FLEsync Synchronisatie\"");
            Sc($"description {SettingsStore.ServiceName} \"Normec Group FLEsync - synchroniseert bestandslocaties volgens schema.\"");
            // Herstart automatisch na een crash (na 1 min, 1 min, 5 min).
            Sc($"failure {SettingsStore.ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000");
            SyncLog.Info("Windows-service FLEsync geïnstalleerd.");
        }
        Start();
    }

    private static void Uninstall()
    {
        if (!IsInstalled) return;
        Stop();
        Sc($"delete {SettingsStore.ServiceName}");
        SyncLog.Info("Windows-service FLEsync verwijderd.");
    }

    private static void Start()
    {
        using var sc = new ServiceController(SettingsStore.ServiceName);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    private static void Stop()
    {
        using var sc = new ServiceController(SettingsStore.ServiceName);
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
    }

    private static void Sc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"sc.exe {args.Split(' ')[0]} gaf code {p.ExitCode}: {output.Trim()}");
    }
}
