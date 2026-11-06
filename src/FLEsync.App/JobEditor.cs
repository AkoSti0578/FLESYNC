using FLEsync.Core;

namespace FLEsync.App;

/// <summary>Bewerkscherm voor één synchronisatie (bron, doel, richting en schema).</summary>
internal sealed class JobEditor : UserControl
{
    public int JobId { get; }

    private readonly CheckBox _enabled = new() { Text = "Deze synchronisatie is actief", AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Navy, Margin = new Padding(0, 6, 24, 0) };
    private readonly TextBox _name = new() { Width = 260, Font = Theme.Base, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(6, 3, 0, 3) };

    // Locaties
    private readonly TextBox _source = NewTextBox();
    private readonly TextBox _target = NewTextBox();
    private readonly Label _sourceLabel = FieldLabel("Locatie A (bron)");
    private readonly Label _targetLabel = FieldLabel("Locatie B (doel)");

    // Richting
    private readonly RadioButton _twoWay = Radio("Tweerichtings – A en B gelijk houden (A ⇄ B)");
    private readonly RadioButton _oneWay = Radio("One-way – alleen van A naar B (A → B)");
    private readonly CheckBox _mirror = new() { Text = "Bestanden die uit A verdwijnen ook in B verwijderen (spiegelen)", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(22, 2, 0, 2) };
    private readonly CheckBox _deliver = new() { Text = "Alleen nieuwe bestanden afleveren (wat in B is opgepakt/verwijderd niet opnieuw kopiëren)", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(22, 2, 0, 2) };
    private readonly CheckBox _deleteProcessed = new() { Text = "Verwerkte bestanden (uit B verdwenen) ook uit A verwijderen", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(44, 2, 0, 2) };
    private readonly LinkLabel _markDelivered = new() { Text = "Huidige bestanden in A markeren als afgeleverd", AutoSize = true, LinkColor = Theme.Blue, ActiveLinkColor = Theme.Accent, Margin = new Padding(44, 4, 0, 0) };
    private readonly LinkLabel _resetDelivered = new() { Text = "Afleverregister wissen (alles opnieuw afleveren)", AutoSize = true, LinkColor = Theme.Blue, ActiveLinkColor = Theme.Accent, Margin = new Padding(44, 2, 0, 2) };
    private readonly CheckBox _emptySource = new() { Text = "Als B (doel) leeg is, ook A (bron) leegmaken", AutoSize = true, ForeColor = Theme.Danger, Margin = new Padding(0, 8, 0, 2) };
    private readonly TextBox _exclude = NewTextBox();

    // Planning
    private readonly RadioButton _interval = Radio("Elke");
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 10080, Value = 15, Width = 70, Font = Theme.Base, TextAlign = HorizontalAlignment.Right };
    private readonly RadioButton _daily = Radio("Dagelijks om");
    private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80, Font = Theme.Base };
    private readonly Label _nextRun = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 8, 0, 0) };

    // Status
    private readonly Label _lastRun = new() { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 8, 0, 0) };
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    private readonly List<Control> _detailControls = new();

    /// <summary>Wordt aangeroepen als de gebruiker iets wijzigt.</summary>
    public event EventHandler? Changed;

    /// <summary>Gebruiker klikt "Deze nu synchroniseren".</summary>
    public event EventHandler? SyncNowRequested;

    public Button SyncNowButton { get; } = Theme.PrimaryButton("Deze nu synchroniseren");

    public JobEditor(int jobId)
    {
        JobId = jobId;
        Dock = DockStyle.Top;
        BackColor = Theme.Background;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.Base;

        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty };
        stack.Controls.Add(BuildTopRow());
        stack.Controls.Add(BuildLocationsCard());
        stack.Controls.Add(BuildOptionsRow());
        Controls.Add(stack);

        Wire();
    }

    // =================================================================== layout

    private Control BuildTopRow()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        row.Controls.Add(_enabled);
        row.Controls.Add(new Label { Text = "Naam", AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Text, Margin = new Padding(0, 7, 0, 0) });
        _name.PlaceholderText = $"Synchronisatie {JobId}";
        row.Controls.Add(_name);
        return row;
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

        var status = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Card, Margin = new Padding(0, 10, 0, 0) };
        SyncNowButton.Margin = new Padding(0, 0, 16, 0);
        status.Controls.Add(SyncNowButton);
        status.Controls.Add(_lastRun);
        grid.Controls.Add(status, 0, 3);
        grid.SetColumnSpan(status, 3);

        _detailControls.AddRange(new Control[] { _source, _target, browseSource, browseTarget });
        card.Controls.Add(grid);
        return card;
    }

    private Control BuildOptionsRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        // Richting
        var dirCard = new Card("Richting") { AutoSize = true, Margin = new Padding(0, 0, 7, 14) };
        var dir = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Card };
        // Lange teksten laten afbreken zodat ze bij een smal venster niet wegvallen.
        foreach (var c in new Control[] { _twoWay, _oneWay, _mirror, _deliver, _deleteProcessed, _emptySource })
            c.MaximumSize = new Size(430, 0);
        dir.Controls.Add(_twoWay);
        dir.Controls.Add(_oneWay);
        dir.Controls.Add(_mirror);
        dir.Controls.Add(_deliver);
        dir.Controls.Add(_deleteProcessed);
        dir.Controls.Add(_markDelivered);
        dir.Controls.Add(_resetDelivered);
        dir.Controls.Add(_emptySource);
        dir.Controls.Add(new Label { Text = "Uitsluiten (gescheiden door ;)", AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 10, 0, 2) });
        _exclude.Dock = DockStyle.None;
        _exclude.Width = 360;
        dir.Controls.Add(_exclude);
        dirCard.Controls.Add(dir);

        // Planning
        var planCard = new Card("Synchronisatietijd") { AutoSize = true, Margin = new Padding(7, 0, 0, 14) };
        var plan = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, BackColor = Theme.Card };
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        plan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _minutes.Margin = new Padding(4);
        _time.Margin = new Padding(4);
        plan.Controls.Add(_interval, 0, 0);
        plan.Controls.Add(_minutes, 1, 0);
        plan.Controls.Add(new Label { Text = "minuten", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 8, 0, 0) }, 2, 0);
        plan.Controls.Add(_daily, 0, 1);
        plan.Controls.Add(_time, 1, 1);
        plan.Controls.Add(new Label { Text = "uur", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 8, 0, 0) }, 2, 1);
        plan.Controls.Add(_nextRun, 0, 2);
        plan.SetColumnSpan(_nextRun, 3);
        planCard.Controls.Add(plan);

        _detailControls.AddRange(new Control[] { _twoWay, _oneWay, _emptySource, _exclude, _interval, _daily });

        row.Controls.Add(dirCard, 0, 0);
        row.Controls.Add(planCard, 1, 0);
        return row;
    }

    // =================================================================== gedrag

    private void Wire()
    {
        foreach (var c in new Control[] { _name, _source, _target, _exclude })
            c.TextChanged += (_, _) => OnChanged();
        _minutes.ValueChanged += (_, _) => OnChanged();
        _time.ValueChanged += (_, _) => OnChanged();
        foreach (var r in new[] { _twoWay, _oneWay, _interval, _daily })
            r.CheckedChanged += (_, _) => { UpdateEnabledState(); OnChanged(); };
        _mirror.CheckedChanged += (_, _) => OnChanged();
        _deliver.CheckedChanged += (_, _) => { UpdateEnabledState(); OnChanged(); };
        _deleteProcessed.CheckedChanged += (_, _) => OnChanged();
        _markDelivered.LinkClicked += (_, _) => MarkDelivered();
        _resetDelivered.LinkClicked += (_, _) => ResetDelivered();
        _emptySource.CheckedChanged += (_, _) =>
        {
            if (!_loading && _emptySource.Checked && MessageBox.Show(FindForm(),
                    "Met deze instelling wordt locatie A (bron) volledig leeggemaakt zodra locatie B (doel) leeg is.\n\n" +
                    "Dit gebeurt alleen als B bereikbaar is en bij de vorige synchronisatie wél bestanden bevatte; " +
                    "uitgesloten bestanden blijven staan. Verwijderde bestanden komen niet in de prullenbak.\n\n" +
                    "Weet je zeker dat je dit wilt inschakelen?",
                    "FLEsync – bron leegmaken", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                _loading = true;
                _emptySource.Checked = false;
                _loading = false;
                return;
            }
            OnChanged();
        };
        _enabled.CheckedChanged += (_, _) => { UpdateEnabledState(); OnChanged(); };
        SyncNowButton.Click += (_, _) => SyncNowRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool _loading;

    private void OnChanged()
    {
        if (!_loading) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void LoadJob(SyncJob job)
    {
        _loading = true;
        _enabled.Checked = job.Enabled;
        _name.Text = job.Name;
        _source.Text = job.SourcePath;
        _target.Text = job.TargetPath;
        _twoWay.Checked = job.Mode == SyncMode.TwoWay;
        _oneWay.Checked = job.Mode == SyncMode.OneWay;
        _mirror.Checked = job.MirrorDeletes;
        _emptySource.Checked = job.EmptySourceWhenTargetEmpty;
        _deliver.Checked = job.DeliverNewOnly;
        _deleteProcessed.Checked = job.DeleteProcessedFromSource;
        _exclude.Text = string.Join("; ", job.ExcludePatterns);
        _interval.Checked = job.Schedule == ScheduleType.Interval;
        _daily.Checked = job.Schedule == ScheduleType.Daily;
        _minutes.Value = Math.Clamp(job.IntervalMinutes, (int)_minutes.Minimum, (int)_minutes.Maximum);
        _time.Value = DateTime.Today + job.GetDailyTime();
        UpdateEnabledState();
        _loading = false;
    }

    public SyncJob ReadJob() => new()
    {
        Id = JobId,
        Enabled = _enabled.Checked,
        Name = _name.Text.Trim(),
        SourcePath = _source.Text.Trim(),
        TargetPath = _target.Text.Trim(),
        Mode = _oneWay.Checked ? SyncMode.OneWay : SyncMode.TwoWay,
        MirrorDeletes = _oneWay.Checked && _mirror.Checked && !_deliver.Checked,
        DeliverNewOnly = _oneWay.Checked && _deliver.Checked,
        DeleteProcessedFromSource = _oneWay.Checked && _deliver.Checked && _deleteProcessed.Checked,
        EmptySourceWhenTargetEmpty = _emptySource.Checked,
        Schedule = _daily.Checked ? ScheduleType.Daily : ScheduleType.Interval,
        IntervalMinutes = (int)_minutes.Value,
        DailyTime = _time.Value.ToString("HH:mm"),
        ExcludePatterns = _exclude.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
    };

    /// <summary>Controleert de invoer. Een uitgeschakelde, lege synchronisatie is altijd geldig.</summary>
    public bool ValidateJob(out string? message)
    {
        message = null;
        _errors.Clear();
        var job = ReadJob();
        if (!job.Enabled && !job.IsConfigured) return true;

        if (string.IsNullOrWhiteSpace(job.SourcePath)) _errors.SetError(_source, "Kies locatie A.");
        if (string.IsNullOrWhiteSpace(job.TargetPath)) _errors.SetError(_target, "Kies locatie B.");
        if (!job.IsConfigured)
        {
            message = $"{job.DisplayName}: kies zowel locatie A als locatie B (of zet de synchronisatie uit).";
            return false;
        }

        try
        {
            if (PathsEqual(job.SourcePath, job.TargetPath))
            {
                _errors.SetError(_target, "Locatie B moet verschillen van locatie A.");
                message = $"{job.DisplayName}: locatie B moet verschillen van locatie A.";
                return false;
            }
        }
        catch (Exception ex)
        {
            message = $"{job.DisplayName}: ongeldig pad ({ex.Message}).";
            return false;
        }

        if (!Directory.Exists(job.SourcePath))
            _errors.SetError(_source, "Map bestaat (nu) niet of is niet bereikbaar.");
        return true;
    }

    public void ShowStatus(SyncStatus status, bool serviceRunning, bool syncing)
    {
        var job = ReadJob();
        SyncNowButton.Enabled = job.IsConfigured && !syncing;
        SyncNowButton.Text = syncing ? "Bezig met synchroniseren…" : "Deze nu synchroniseren";

        _lastRun.Text = status.LastRun is null
            ? "Nog niet gesynchroniseerd."
            : $"Laatste sync: {status.LastRun:dd-MM-yyyy HH:mm} ({status.RunBy}) – {(status.Success ? "✓" : "⚠")} {status.Summary}";
        _lastRun.ForeColor = status.LastRun is null || status.Success ? Theme.TextMuted : Theme.Danger;

        if (!job.Enabled)
            _nextRun.Text = "Uitgeschakeld – wordt niet automatisch gesynchroniseerd.";
        else if (!serviceRunning)
            _nextRun.Text = "Automatisch synchroniseren werkt zodra de service actief is.";
        else
        {
            var next = job.GetNextRun(status.LastRun, DateTime.Now);
            if (next < DateTime.Now) next = DateTime.Now;
            _nextRun.Text = $"Volgende synchronisatie: {next:dd-MM-yyyy HH:mm}";
        }
    }

    private void UpdateEnabledState()
    {
        var on = _enabled.Checked;
        foreach (var c in _detailControls) c.Enabled = on;
        _name.Enabled = on;
        var delivery = _oneWay.Checked && _deliver.Checked;
        _mirror.Enabled = on && _oneWay.Checked && !_deliver.Checked;
        _deliver.Enabled = on && _oneWay.Checked;
        _deleteProcessed.Enabled = on && delivery;
        _markDelivered.Visible = _resetDelivered.Visible = delivery;
        _markDelivered.Enabled = _resetDelivered.Enabled = on;
        _emptySource.Enabled = on && !delivery;
        _minutes.Enabled = on && _interval.Checked;
        _time.Enabled = on && _daily.Checked;
        _sourceLabel.Text = _oneWay.Checked ? "Locatie A (bron)" : "Locatie A";
        _targetLabel.Text = _oneWay.Checked ? "Locatie B (doel)" : "Locatie B";
    }

    private void MarkDelivered()
    {
        var job = ReadJob();
        if (!job.IsConfigured) return;
        if (MessageBox.Show(FindForm(),
                "Alle bestanden die nu in locatie A staan worden als 'afgeleverd' gemarkeerd. Alleen bestanden die hierna " +
                "nieuw binnenkomen (of wijzigen) gaan naar B.\n\nDoorgaan?",
                "FLEsync – afleveren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            var count = SyncEngine.MarkAllDelivered(job);
            MessageBox.Show(FindForm(), $"{count} bestand(en) gemarkeerd als afgeleverd.", "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Markeren mislukt: " + ex.Message, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ResetDelivered()
    {
        var job = ReadJob();
        if (!job.IsConfigured) return;
        if (MessageBox.Show(FindForm(),
                "Het afleverregister wordt gewist. Bij de volgende synchronisatie worden ALLE bestanden in A opnieuw " +
                "naar B gekopieerd en dus opnieuw door de machine opgepakt.\n\nDoorgaan?",
                "FLEsync – afleveren", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            SyncEngine.ResetDelivered(job);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Wissen mislukt: " + ex.Message, "FLEsync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Browse(TextBox box)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Kies een map om te synchroniseren",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text;
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    internal static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                      Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                      StringComparison.OrdinalIgnoreCase);

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
