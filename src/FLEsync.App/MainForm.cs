using System.ServiceProcess;
using FLEsync.Core;

namespace FLEsync.App;

internal sealed class MainForm : Form
{
    private readonly List<JobEditor> _editors = new();
    private readonly List<JobTab> _tabs = new();
    private readonly HashSet<int> _syncing = new();

    // Acties / status
    private readonly Button _save = Theme.SecondaryButton("Opslaan");
    private readonly Button _syncAll = Theme.PrimaryButton("Alles nu synchroniseren");
    private readonly Button _installService = Theme.AccentButton("Als service instellen");
    private readonly Button _startStop = Theme.SecondaryButton("Service stoppen");
    private readonly Button _uninstall = Theme.DangerButton("Service verwijderen");
    private readonly StatusPill _servicePill = new();
    private readonly Label _serviceHint = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 0) };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Mono, ForeColor = Theme.Text, IntegralHeight = false, HorizontalScrollbar = true };

    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };
    private bool _dirty;
    private string _lastLogSignature = "";

    public MainForm()
    {
        Text = "FLEsync – Normec Group";
        Icon = Theme.AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 820);
        Size = new Size(1000, 960);
        BackColor = Theme.Background;
        Font = Theme.Base;
        AutoScaleMode = AutoScaleMode.Dpi;

        for (int id = 1; id <= SyncSettings.JobCount; id++)
        {
            var editor = new JobEditor(id) { Visible = id == 1 };
            editor.Changed += (_, _) => { _dirty = true; UpdateTabs(); };
            editor.SyncNowRequested += async (s, _) => await SyncAsync(new[] { ((JobEditor)s!).JobId });
            _editors.Add(editor);
        }

        BuildLayout();
        LoadSettings();
        WireEvents();
        RefreshStatus();
        _refresh.Start();
    }

    private JobEditor Editor(int id) => _editors[id - 1];

    // =================================================================== layout

    private void BuildLayout()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(24, 16, 24, 8),
            AutoScroll = true
        };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // tabs
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // job-editor
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // acties
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // logboek

        body.Controls.Add(BuildTabs(), 0, 0);
        body.Controls.Add(BuildJobHost(), 0, 1);
        body.Controls.Add(BuildActionsCard(), 0, 2);
        body.Controls.Add(BuildLogCard(), 0, 3);

        Controls.Add(body);
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Navy };
        header.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, 0, header.Height - 4, header.Width, 4);
        };

        var title = new Label { Text = "FLEsync", Font = Theme.Logo, ForeColor = Color.White, AutoSize = true, Location = new Point(24, 12), BackColor = Color.Transparent };
        var subtitle = new Label { Text = "Bestandssynchronisatie  ·  Normec Group", Font = Theme.Subtitle, ForeColor = ColorTranslator.FromHtml("#B9C7D8"), AutoSize = true, Location = new Point(27, 52), BackColor = Color.Transparent };

        _servicePill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _servicePill.Location = new Point(header.Width - _servicePill.Width - 24, 26);
        header.Resize += (_, _) => _servicePill.Left = header.Width - _servicePill.Width - 24;

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_servicePill);
        return header;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Background };
        var version = typeof(MainForm).Assembly.GetName().Version;
        footer.Controls.Add(new Label
        {
            Text = $"FLEsync {version?.ToString(3)}  ·  © Normec Group  ·  Logboek: {SettingsStore.LogDirectory}",
            ForeColor = Theme.TextMuted,
            Font = Theme.Subtitle,
            AutoSize = true,
            Location = new Point(24, 5)
        });
        return footer;
    }

    private Control BuildTabs()
    {
        var strip = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        for (int id = 1; id <= SyncSettings.JobCount; id++)
        {
            var jobId = id;
            var tab = new JobTab { Selected = id == 1 };
            tab.Click += (_, _) => SelectJob(jobId);
            _tabs.Add(tab);
            strip.Controls.Add(tab);
        }
        return strip;
    }

    private Control BuildJobHost()
    {
        var host = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        foreach (var editor in _editors) host.Controls.Add(editor);
        return host;
    }

    private Control BuildActionsCard()
    {
        var card = new Card("Service en acties") { AutoSize = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = Theme.Card };
        flow.Controls.Add(_save);
        flow.Controls.Add(_syncAll);
        flow.Controls.Add(_installService);
        flow.Controls.Add(_startStop);
        flow.Controls.Add(_uninstall);
        flow.SetFlowBreak(_uninstall, true);
        flow.Controls.Add(_serviceHint);
        card.Controls.Add(flow);
        return card;
    }

    private Control BuildLogCard()
    {
        var card = new Card("Logboek") { MinimumSize = new Size(0, 160) };
        var holder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        _log.BackColor = ColorTranslator.FromHtml("#F8FAFC");
        holder.Controls.Add(_log);
        card.Controls.Add(holder);
        return card;
    }

    // =================================================================== gedrag

    private void WireEvents()
    {
        _save.Click += (_, _) => Save(showMessage: true);
        _syncAll.Click += async (_, _) => await SyncAsync(_editors.Select(e => e.ReadJob()).Where(j => j.IsActive).Select(j => j.Id).ToArray());
        _installService.Click += (_, _) => InstallService();
        _startStop.Click += (_, _) => ToggleService();
        _uninstall.Click += (_, _) => UninstallService();
        _refresh.Tick += (_, _) => RefreshStatus();

        FormClosing += (_, e) =>
        {
            if (_dirty && MessageBox.Show(this, "Er zijn niet-opgeslagen wijzigingen. Toch afsluiten?", "FLEsync",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                e.Cancel = true;
        };
    }

    private void SelectJob(int id)
    {
        SuspendLayout();
        foreach (var editor in _editors) editor.Visible = editor.JobId == id;
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Selected = i + 1 == id;
        ResumeLayout(true);
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load();
        foreach (var editor in _editors) editor.LoadJob(settings.Job(editor.JobId));
        _dirty = false;
        UpdateTabs();
    }

    private SyncSettings ReadForm() => new SyncSettings { Jobs = _editors.Select(e => e.ReadJob()).ToList() }.Normalize();

    private void UpdateTabs()
    {
        var running = ServiceManager.GetStatus() == ServiceControllerStatus.Running;
        foreach (var editor in _editors)
        {
            var job = editor.ReadJob();
            var tab = _tabs[job.Id - 1];
            tab.Text = job.DisplayName;
            if (!job.Enabled) tab.SetSubtitle("Uitgeschakeld", Theme.TextMuted);
            else if (!job.IsConfigured) tab.SetSubtitle("Nog niet ingesteld", Theme.Warning);
            else tab.SetSubtitle(job.Describe(), running ? Theme.Success : Theme.Warning);
        }
    }

    private bool Save(bool showMessage)
    {
        foreach (var editor in _editors)
        {
            if (editor.ValidateJob(out var message)) continue;
            SelectJob(editor.JobId);
            MessageBox.Show(this, message, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var settings = ReadForm();
        if (!ConfirmOverlap(settings)) return false;

        try
        {
            SettingsStore.Save(settings);
            _dirty = false;
            foreach (var job in settings.Jobs)
                SyncLog.Info($"[{job.DisplayName}] Instellingen opgeslagen: " +
                             (job.IsActive ? $"{job.SourcePath} / {job.TargetPath} [{job.Describe()}]" : job.Enabled ? "niet ingesteld" : "uitgeschakeld"));
            WarnAboutMappedDrives(settings);
            RefreshStatus();
            if (showMessage)
            {
                var msg = ServiceManager.GetStatus() == ServiceControllerStatus.Running
                    ? "Instellingen opgeslagen. De service gebruikt de nieuwe instellingen automatisch."
                    : "Instellingen opgeslagen.";
                MessageBox.Show(this, msg, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Opslaan mislukt: " + ex.Message, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Waarschuwt als twee synchronisaties dezelfde of geneste mappen gebruiken.</summary>
    private bool ConfirmOverlap(SyncSettings settings)
    {
        var active = settings.Jobs.Where(j => j.IsActive).ToList();
        for (int i = 0; i < active.Count; i++)
        for (int k = i + 1; k < active.Count; k++)
        {
            foreach (var p in new[] { active[i].SourcePath, active[i].TargetPath })
            foreach (var q in new[] { active[k].SourcePath, active[k].TargetPath })
            {
                if (!Overlaps(p, q)) continue;
                return MessageBox.Show(this,
                    $"{active[i].DisplayName} en {active[k].DisplayName} gebruiken dezelfde of in elkaar liggende mappen:\n\n{p}\n{q}\n\n" +
                    "Dit kan leiden tot dubbel werk of onverwachte resultaten. Toch opslaan?",
                    "FLEsync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            }
        }
        return true;
    }

    private static bool Overlaps(string a, string b)
    {
        try
        {
            var x = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)) + Path.DirectorySeparatorChar;
            var y = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)) + Path.DirectorySeparatorChar;
            return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void WarnAboutMappedDrives(SyncSettings settings)
    {
        foreach (var job in settings.Jobs.Where(j => j.IsActive))
        foreach (var path in new[] { job.SourcePath, job.TargetPath })
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (root is { Length: >= 2 } && root[1] == ':' && new DriveInfo(root).DriveType == DriveType.Network)
                {
                    MessageBox.Show(this,
                        $"{job.DisplayName}: '{path}' staat op een gekoppelde netwerkschijf. Een Windows-service ziet geen " +
                        "gekoppelde stationsletters; gebruik bij voorkeur het UNC-pad (\\\\server\\share\\...).",
                        "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch { }
        }
    }

    private async Task SyncAsync(int[] jobIds)
    {
        if (!Save(showMessage: false)) return;
        var settings = SettingsStore.Load();
        var jobs = jobIds.Where(id => !_syncing.Contains(id)).Select(settings.Job).Where(j => j.IsConfigured).ToList();
        if (jobs.Count == 0)
        {
            MessageBox.Show(this, "Er is geen ingestelde synchronisatie om uit te voeren.", "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var j in jobs) _syncing.Add(j.Id);
        RefreshStatus();
        try
        {
            var user = Environment.UserName;
            var results = await Task.WhenAll(jobs.Select(j =>
                Task.Run(() => (Job: j, Result: SyncEngine.RunExclusive(j, $"handmatig door {user}")))));

            RefreshStatus();
            var text = string.Join("\n\n", results.Select(r => $"{r.Job.DisplayName}:\n{r.Result}"));
            MessageBox.Show(this, text, "FLEsync – synchronisatie", MessageBoxButtons.OK,
                results.All(r => r.Result.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            foreach (var j in jobs) _syncing.Remove(j.Id);
            RefreshStatus();
        }
    }

    private void InstallService()
    {
        if (!Save(showMessage: false)) return;
        var settings = SettingsStore.Load();
        if (!settings.Jobs.Any(j => j.IsActive))
        {
            MessageBox.Show(this, "Stel eerst minimaal één synchronisatie in.", "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var action = ServiceManager.IsInstalled ? ServiceManager.ArgRestart : ServiceManager.ArgInstall;
        if (ServiceManager.RunElevated(action, out var error))
        {
            var summary = string.Join("\n", settings.Jobs.Select(j =>
                $"• {j.DisplayName}: {(j.IsActive ? j.Describe() : "uitgeschakeld")}"));
            MessageBox.Show(this,
                $"FLEsync draait nu als Windows-service en start automatisch met Windows.\n\n{summary}",
                "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, error, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshStatus();
    }

    private void ToggleService()
    {
        var running = ServiceManager.GetStatus() == ServiceControllerStatus.Running;
        if (!ServiceManager.RunElevated(running ? ServiceManager.ArgStop : ServiceManager.ArgStart, out var error))
            MessageBox.Show(this, error, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        RefreshStatus();
    }

    private void UninstallService()
    {
        if (MessageBox.Show(this, "De FLEsync-service stoppen en verwijderen? Er wordt dan niet meer automatisch gesynchroniseerd.",
                "FLEsync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        if (!ServiceManager.RunElevated(ServiceManager.ArgUninstall, out var error))
            MessageBox.Show(this, error, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = ServiceManager.GetStatus();
        switch (status)
        {
            case null:
                _servicePill.SetStatus("Service: niet ingesteld", Theme.TextMuted);
                _serviceHint.Text = "De service synchroniseert op de achtergrond volgens het schema van elke synchronisatie.";
                break;
            case ServiceControllerStatus.Running:
                _servicePill.SetStatus("Service: actief", Theme.Success);
                _serviceHint.Text = "De service draait. Opgeslagen wijzigingen worden automatisch overgenomen.";
                break;
            case ServiceControllerStatus.Stopped:
                _servicePill.SetStatus("Service: gestopt", Theme.Danger);
                _serviceHint.Text = "De service is gestopt; er wordt niet automatisch gesynchroniseerd.";
                break;
            default:
                _servicePill.SetStatus("Service: " + status.ToString()!.ToLowerInvariant(), Theme.Warning);
                break;
        }

        var settings = ReadForm();
        var anyActive = settings.Jobs.Any(j => j.IsActive);
        _installService.Text = status == null ? "Als service instellen" : "Service bijwerken && herstarten";
        _installService.Enabled = anyActive;
        _startStop.Visible = _uninstall.Visible = status != null;
        _startStop.Text = status == ServiceControllerStatus.Running ? "Service stoppen" : "Service starten";
        _syncAll.Enabled = anyActive && _syncing.Count == 0;

        var serviceRunning = status == ServiceControllerStatus.Running;
        foreach (var editor in _editors)
            editor.ShowStatus(SyncStatus.Load(editor.JobId), serviceRunning, _syncing.Contains(editor.JobId));

        UpdateTabs();
        RefreshLog();
    }

    private void RefreshLog()
    {
        var lines = SyncLog.ReadTail(300);
        var signature = lines.Count + "|" + (lines.Count > 0 ? lines[^1] : "");
        if (signature == _lastLogSignature) return;
        _lastLogSignature = signature;

        _log.BeginUpdate();
        _log.Items.Clear();
        foreach (var l in lines) _log.Items.Add(l);
        if (_log.Items.Count > 0) _log.TopIndex = _log.Items.Count - 1;
        _log.EndUpdate();
    }
}
