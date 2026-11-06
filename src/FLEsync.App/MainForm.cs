using System.ServiceProcess;
using FLEsync.Core;

namespace FLEsync.App;

internal sealed class MainForm : Form
{
    // Locaties
    private readonly TextBox _source = NewTextBox();
    private readonly TextBox _target = NewTextBox();
    private readonly Label _sourceLabel = FieldLabel("Locatie A (bron)");
    private readonly Label _targetLabel = FieldLabel("Locatie B (doel)");

    // Richting
    private readonly RadioButton _twoWay = Radio("Tweerichtings – A en B gelijk houden (A ⇄ B)");
    private readonly RadioButton _oneWay = Radio("One-way – alleen van A naar B (A → B)");
    private readonly CheckBox _mirror = new() { Text = "Bestanden die uit A verdwijnen ook in B verwijderen (spiegelen)", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(22, 2, 0, 2) };
    private readonly TextBox _exclude = NewTextBox();

    // Planning
    private readonly RadioButton _interval = Radio("Elke");
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 10080, Value = 15, Width = 70, Font = Theme.Base, TextAlign = HorizontalAlignment.Right };
    private readonly RadioButton _daily = Radio("Dagelijks om");
    private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80, Font = Theme.Base };
    private readonly Label _nextRun = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 8, 0, 0) };

    // Acties / status
    private readonly Button _save = Theme.SecondaryButton("Opslaan");
    private readonly Button _syncNow = Theme.PrimaryButton("Nu synchroniseren");
    private readonly Button _installService = Theme.AccentButton("Als service instellen");
    private readonly Button _startStop = Theme.SecondaryButton("Service stoppen");
    private readonly Button _uninstall = Theme.DangerButton("Service verwijderen");
    private readonly StatusPill _servicePill = new();
    private readonly Label _lastRun = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 0) };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Mono, ForeColor = Theme.Text, IntegralHeight = false, HorizontalScrollbar = true };

    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private bool _syncRunning;
    private bool _dirty;
    private string _lastLogSignature = "";

    public MainForm()
    {
        Text = "FLEsync – Normec Group";
        Icon = Theme.AppIcon();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 760);
        Size = new Size(960, 860);
        BackColor = Theme.Background;
        Font = Theme.Base;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();
        LoadSettings();
        WireEvents();
        RefreshStatus();
        _refresh.Start();
    }

    // =================================================================== layout

    private void BuildLayout()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 8),
            AutoScroll = true
        };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // locaties
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // richting + planning
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // acties
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // logboek

        body.Controls.Add(BuildLocationsCard(), 0, 0);
        body.Controls.Add(BuildOptionsRow(), 0, 1);
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
        var label = new Label
        {
            Text = $"FLEsync {version?.ToString(3)}  ·  © Normec Group  ·  Logboek: {SettingsStore.LogDirectory}",
            ForeColor = Theme.TextMuted,
            Font = Theme.Subtitle,
            AutoSize = true,
            Location = new Point(24, 5)
        };
        footer.Controls.Add(label);
        return footer;
    }

    private Control BuildLocationsCard()
    {
        var card = new Card("Locaties") { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, BackColor = Theme.Card };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browseSource = Theme.SecondaryButton("Bladeren…");
        var browseTarget = Theme.SecondaryButton("Bladeren…");
        browseSource.Margin = browseTarget.Margin = new Padding(8, 3, 0, 3);
        browseSource.Click += (_, _) => Browse(_source);
        browseTarget.Click += (_, _) => Browse(_target);

        grid.Controls.Add(_sourceLabel, 0, 0);
        grid.Controls.Add(_source, 1, 0);
        grid.Controls.Add(browseSource, 2, 0);
        grid.Controls.Add(_targetLabel, 0, 1);
        grid.Controls.Add(_target, 1, 1);
        grid.Controls.Add(browseTarget, 2, 1);

        var hint = new Label
        {
            Text = "Lokale mappen of netwerklocaties (bij voorkeur UNC, bijv. \\\\server\\share\\map – een service ziet geen gekoppelde stationsletters).",
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 0)
        };
        grid.Controls.Add(hint, 0, 2);
        grid.SetColumnSpan(hint, 3);

        card.Controls.Add(grid);
        return card;
    }

    private Control BuildOptionsRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        // Richting
        var dirCard = new Card("Synchronisatie") { AutoSize = true, Margin = new Padding(0, 0, 7, 14) };
        var dir = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Card };
        dir.Controls.Add(_twoWay);
        dir.Controls.Add(_oneWay);
        dir.Controls.Add(_mirror);
        dir.Controls.Add(new Label { Text = "Uitsluiten (gescheiden door ;)", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 2) });
        _exclude.Width = 360;
        dir.Controls.Add(_exclude);
        dirCard.Controls.Add(dir);

        // Planning
        var planCard = new Card("Synchronisatietijd") { AutoSize = true, Margin = new Padding(7, 0, 0, 14) };
        var plan = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, BackColor = Theme.Card };
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _minutes.Margin = new Padding(4, 4, 4, 4);
        _time.Margin = new Padding(4, 4, 4, 4);
        plan.Controls.Add(_interval, 0, 0);
        plan.Controls.Add(_minutes, 1, 0);
        plan.Controls.Add(new Label { Text = "minuten", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 8, 0, 0) }, 2, 0);
        plan.Controls.Add(_daily, 0, 1);
        plan.Controls.Add(_time, 1, 1);
        plan.Controls.Add(new Label { Text = "uur", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 8, 0, 0) }, 2, 1);
        plan.Controls.Add(_nextRun, 0, 2);
        plan.SetColumnSpan(_nextRun, 3);
        planCard.Controls.Add(plan);

        row.Controls.Add(dirCard, 0, 0);
        row.Controls.Add(planCard, 1, 0);
        return row;
    }

    private Control BuildActionsCard()
    {
        var card = new Card("Acties") { AutoSize = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = Theme.Card };
        flow.Controls.Add(_save);
        flow.Controls.Add(_syncNow);
        flow.Controls.Add(_installService);
        flow.Controls.Add(_startStop);
        flow.Controls.Add(_uninstall);
        flow.SetFlowBreak(_uninstall, true);
        flow.Controls.Add(_lastRun);
        card.Controls.Add(flow);
        return card;
    }

    private Control BuildLogCard()
    {
        var card = new Card("Logboek") { MinimumSize = new Size(0, 180) };
        var holder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        _log.BackColor = ColorTranslator.FromHtml("#F8FAFC");
        holder.Controls.Add(_log);
        card.Controls.Add(holder);
        return card;
    }

    // =================================================================== gedrag

    private void WireEvents()
    {
        foreach (var c in new Control[] { _source, _target, _exclude, _minutes, _time })
            c.TextChanged += (_, _) => MarkDirty();
        _minutes.ValueChanged += (_, _) => MarkDirty();
        _time.ValueChanged += (_, _) => MarkDirty();
        foreach (var r in new[] { _twoWay, _oneWay, _interval, _daily })
            r.CheckedChanged += (_, _) => { UpdateEnabledState(); MarkDirty(); };
        _mirror.CheckedChanged += (_, _) => MarkDirty();

        _save.Click += (_, _) => Save(showMessage: true);
        _syncNow.Click += async (_, _) => await SyncNowAsync();
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

    private void LoadSettings()
    {
        var s = SettingsStore.Load();
        _source.Text = s.SourcePath;
        _target.Text = s.TargetPath;
        _twoWay.Checked = s.Mode == SyncMode.TwoWay;
        _oneWay.Checked = s.Mode == SyncMode.OneWay;
        _mirror.Checked = s.MirrorDeletes;
        _exclude.Text = string.Join("; ", s.ExcludePatterns);
        _interval.Checked = s.Schedule == ScheduleType.Interval;
        _daily.Checked = s.Schedule == ScheduleType.Daily;
        _minutes.Value = Math.Clamp(s.IntervalMinutes, (int)_minutes.Minimum, (int)_minutes.Maximum);
        _time.Value = DateTime.Today + s.GetDailyTime();
        UpdateEnabledState();
        _dirty = false;
    }

    private SyncSettings ReadForm() => new()
    {
        SourcePath = _source.Text.Trim(),
        TargetPath = _target.Text.Trim(),
        Mode = _oneWay.Checked ? SyncMode.OneWay : SyncMode.TwoWay,
        MirrorDeletes = _oneWay.Checked && _mirror.Checked,
        Schedule = _daily.Checked ? ScheduleType.Daily : ScheduleType.Interval,
        IntervalMinutes = (int)_minutes.Value,
        DailyTime = _time.Value.ToString("HH:mm"),
        ExcludePatterns = _exclude.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
    };

    private bool Validate(SyncSettings s)
    {
        _errors.Clear();
        bool ok = true;
        if (string.IsNullOrWhiteSpace(s.SourcePath)) { _errors.SetError(_source, "Kies locatie A."); ok = false; }
        if (string.IsNullOrWhiteSpace(s.TargetPath)) { _errors.SetError(_target, "Kies locatie B."); ok = false; }
        if (!ok) return false;

        if (string.Equals(Path.GetFullPath(s.SourcePath).TrimEnd('\\'), Path.GetFullPath(s.TargetPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            _errors.SetError(_target, "Locatie B moet verschillen van locatie A.");
            return false;
        }
        if (!Directory.Exists(s.SourcePath))
            _errors.SetError(_source, "Map bestaat (nu) niet of is niet bereikbaar.");
        return true;
    }

    private bool Save(bool showMessage)
    {
        var s = ReadForm();
        if (!Validate(s)) return false;
        try
        {
            SettingsStore.Save(s);
            _dirty = false;
            SyncLog.Info($"Instellingen opgeslagen: {s.SourcePath} / {s.TargetPath} [{s.Describe()}]");
            WarnAboutMappedDrives(s);
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

    private void WarnAboutMappedDrives(SyncSettings s)
    {
        foreach (var path in new[] { s.SourcePath, s.TargetPath })
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (root is { Length: >= 2 } && root[1] == ':' && new DriveInfo(root).DriveType == DriveType.Network)
                {
                    MessageBox.Show(this,
                        $"'{path}' staat op een gekoppelde netwerkschijf. Een Windows-service ziet geen gekoppelde " +
                        "stationsletters; gebruik bij voorkeur het UNC-pad (\\\\server\\share\\...).",
                        "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch { }
        }
    }

    private async Task SyncNowAsync()
    {
        if (_syncRunning) return;
        if (!Save(showMessage: false)) return;

        var s = SettingsStore.Load();
        _syncRunning = true;
        _syncNow.Enabled = false;
        _syncNow.Text = "Bezig met synchroniseren…";
        UseWaitCursor = true;
        try
        {
            var result = await Task.Run(() => SyncEngine.RunExclusive(s, $"handmatig door {Environment.UserName}"));
            RefreshStatus();
            MessageBox.Show(this, result.ToString(), "FLEsync – synchronisatie",
                MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _syncRunning = false;
            _syncNow.Text = "Nu synchroniseren";
            _syncNow.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private void InstallService()
    {
        if (!Save(showMessage: false)) return;
        var wasInstalled = ServiceManager.IsInstalled;
        var action = wasInstalled ? ServiceManager.ArgRestart : ServiceManager.ArgInstall;
        if (ServiceManager.RunElevated(action, out var error))
        {
            var s = SettingsStore.Load();
            MessageBox.Show(this,
                $"FLEsync draait nu als Windows-service en start automatisch met Windows.\n\nSchema: {s.Describe()}",
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
                break;
            case ServiceControllerStatus.Running:
                _servicePill.SetStatus("Service: actief", Theme.Success);
                break;
            case ServiceControllerStatus.Stopped:
                _servicePill.SetStatus("Service: gestopt", Theme.Danger);
                break;
            default:
                _servicePill.SetStatus("Service: " + status.ToString()!.ToLowerInvariant(), Theme.Warning);
                break;
        }

        var configured = ReadForm().IsConfigured;
        _installService.Text = status == null ? "Als service instellen" : "Service bijwerken && herstarten";
        _installService.Enabled = configured;
        _startStop.Visible = _uninstall.Visible = status != null;
        _startStop.Text = status == ServiceControllerStatus.Running ? "Service stoppen" : "Service starten";
        _syncNow.Enabled = configured && !_syncRunning;

        var last = SyncStatus.Load();
        _lastRun.Text = last.LastRun is null
            ? "Nog niet gesynchroniseerd."
            : $"Laatste sync: {last.LastRun:dd-MM-yyyy HH:mm} ({last.RunBy}) – {(last.Success ? "✓" : "⚠")} {last.Summary}";
        _lastRun.ForeColor = last.LastRun is null || last.Success ? Theme.TextMuted : Theme.Danger;

        UpdateNextRun(last.LastRun, status);
        RefreshLog();
    }

    private void UpdateNextRun(DateTime? lastRun, ServiceControllerStatus? status)
    {
        var s = ReadForm();
        if (status != ServiceControllerStatus.Running)
        {
            _nextRun.Text = "Automatisch synchroniseren werkt zodra de service actief is.";
            return;
        }
        var next = s.GetNextRun(lastRun, DateTime.Now);
        if (next < DateTime.Now) next = DateTime.Now;
        _nextRun.Text = $"Volgende synchronisatie: {next:dd-MM-yyyy HH:mm}";
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

    private void UpdateEnabledState()
    {
        _mirror.Enabled = _oneWay.Checked;
        _minutes.Enabled = _interval.Checked;
        _time.Enabled = _daily.Checked;
        _sourceLabel.Text = _oneWay.Checked ? "Locatie A (bron)" : "Locatie A";
        _targetLabel.Text = _oneWay.Checked ? "Locatie B (doel)" : "Locatie B";
    }

    private void MarkDirty() => _dirty = true;

    private void Browse(TextBox box)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Kies een map om te synchroniseren",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    // =================================================================== factory helpers

    private static TextBox NewTextBox() => new()
    {
        Dock = DockStyle.Fill,
        Font = Theme.Base,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 5, 3, 5)
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Bold,
        ForeColor = Theme.Text,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 12, 0)
    };

    private static RadioButton Radio(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        Margin = new Padding(0, 4, 0, 4)
    };
}
