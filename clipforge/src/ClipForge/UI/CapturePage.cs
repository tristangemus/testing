using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>Live capture control: status, source selection and the big action buttons.</summary>
public sealed class CapturePage : UserControl
{
    private readonly AppController _app;
    private readonly StatusPill _pill;
    private readonly Label _detail;
    private readonly Label _detected;
    private readonly Label _elapsed;
    private readonly FlatButton _record;
    private readonly FlatButton _buffer;
    private readonly FlatButton _save;
    private readonly RadioButton _monitorMode;
    private readonly RadioButton _windowMode;
    private readonly ComboBox _monitors;
    private readonly ComboBox _windows;
    private readonly System.Windows.Forms.Timer _ticker = new();
    private bool _suppressSourceEvents;

    public CapturePage(AppController app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        AutoScroll = true;

        var title = Theme.Title("Capture");
        title.Location = new Point(0, 6);
        Controls.Add(title);

        // ---- status card ----
        var status = Theme.Card();
        status.Location = new Point(0, 48);
        status.Size = new Size(760, 150);
        status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _pill = new StatusPill { Location = new Point(18, 18), Width = 300 };
        status.Controls.Add(_pill);

        _elapsed = new Label
        {
            Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text,
            AutoSize = true, BackColor = Color.Transparent, Location = new Point(330, 22)
        };
        status.Controls.Add(_elapsed);

        _detail = Theme.Muted("");
        _detail.Location = new Point(20, 58);
        status.Controls.Add(_detail);

        _detected = Theme.Muted("");
        _detected.Location = new Point(20, 78);
        status.Controls.Add(_detected);

        _record = new FlatButton
        {
            Text = "Start recording", Style = FlatButton.Variant.Accent,
            Size = new Size(160, 38), Location = new Point(18, 100)
        };
        _record.Click += async (_, _) => await _app.ToggleRecordingAsync();

        _buffer = new FlatButton { Text = "Arm replay buffer", Size = new Size(160, 38), Location = new Point(188, 100) };
        _buffer.Click += async (_, _) => await _app.ToggleBufferAsync();

        _save = new FlatButton { Text = "Save replay now", Size = new Size(150, 38), Location = new Point(358, 100) };
        _save.Click += async (_, _) => await _app.SaveReplayAsync();

        status.Controls.AddRange(new Control[] { _record, _buffer, _save });
        Controls.Add(status);

        // ---- source card ----
        var source = Theme.Card();
        source.Location = new Point(0, 214);
        source.Size = new Size(760, 190);
        source.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var sourceTitle = Theme.Body("Capture source");
        sourceTitle.Font = Theme.Bold;
        sourceTitle.Location = new Point(18, 16);
        source.Controls.Add(sourceTitle);

        _monitorMode = new RadioButton
        {
            Text = "Whole display", Location = new Point(18, 46), AutoSize = true,
            ForeColor = Theme.Text, Font = Theme.Base, BackColor = Color.Transparent,
            Checked = _app.Settings.CaptureMode == CaptureMode.Monitor
        };
        _windowMode = new RadioButton
        {
            Text = "Single window", Location = new Point(18, 100), AutoSize = true,
            ForeColor = Theme.Text, Font = Theme.Base, BackColor = Color.Transparent,
            Checked = _app.Settings.CaptureMode == CaptureMode.Window
        };

        _monitors = Theme.Combo(400);
        _monitors.Location = new Point(40, 70);
        _monitors.DrawItem += Theme.DrawComboItem;

        _windows = Theme.Combo(400);
        _windows.Location = new Point(40, 124);
        _windows.DrawItem += Theme.DrawComboItem;

        var refreshWindows = new FlatButton { Text = "Refresh list", Width = 110, Location = new Point(452, 123) };
        refreshWindows.Click += (_, _) => LoadWindows();

        var note = Theme.Muted("Display capture uses GPU Desktop Duplication when available - " +
                               "the cheapest option while a game is running.");
        note.Location = new Point(40, 160);
        source.Controls.Add(note);

        _monitorMode.CheckedChanged += (_, _) => OnSourceChanged();
        _windowMode.CheckedChanged += (_, _) => OnSourceChanged();
        _monitors.SelectedIndexChanged += (_, _) => OnSourceChanged();
        _windows.SelectedIndexChanged += (_, _) => OnSourceChanged();

        source.Controls.AddRange(new Control[]
        {
            _monitorMode, _monitors, _windowMode, _windows, refreshWindows
        });
        Controls.Add(source);

        // ---- hotkey reminder ----
        var hint = Theme.Card();
        hint.Location = new Point(0, 420);
        hint.Size = new Size(760, 96);
        hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var hintTitle = Theme.Body("Hotkeys");
        hintTitle.Font = Theme.Bold;
        hintTitle.Location = new Point(18, 14);
        hint.Controls.Add(hintTitle);

        var hotkeyList = Theme.Muted("");
        hotkeyList.Location = new Point(18, 40);
        hotkeyList.Name = "hotkeys";
        hint.Controls.Add(hotkeyList);
        Controls.Add(hint);

        LoadMonitors();
        LoadWindows();

        _ticker.Interval = 500;
        _ticker.Tick += (_, _) => UpdateElapsed();
        _ticker.Start();

        _app.StatusChanged += (_, _) => UpdateStatus();
        _app.SettingsChanged += (_, _) => { LoadMonitors(); UpdateStatus(); };
        UpdateStatus();
    }

    private void LoadMonitors()
    {
        _suppressSourceEvents = true;
        try
        {
            LoadMonitorsCore();
        }
        finally { _suppressSourceEvents = false; }
    }

    private void LoadMonitorsCore()
    {
        _monitors.Items.Clear();
        foreach (var monitor in CaptureTargets.GetMonitors()) _monitors.Items.Add(monitor);
        if (_monitors.Items.Count == 0) return;

        var index = Math.Clamp(_app.Settings.MonitorIndex, 0, _monitors.Items.Count - 1);
        _monitors.SelectedIndex = index;
    }

    private void LoadWindows()
    {
        _suppressSourceEvents = true;
        try
        {
            LoadWindowsCore();
        }
        finally { _suppressSourceEvents = false; }
    }

    private void LoadWindowsCore()
    {
        _windows.Items.Clear();
        foreach (var window in CaptureTargets.GetWindows()) _windows.Items.Add(window);

        var saved = _app.Settings.WindowTitle;
        if (!string.IsNullOrEmpty(saved))
        {
            for (var i = 0; i < _windows.Items.Count; i++)
            {
                if (_windows.Items[i] is WindowInfo info && info.Title == saved)
                {
                    _windows.SelectedIndex = i;
                    return;
                }
            }
        }
        if (_windows.Items.Count > 0 && _windows.SelectedIndex < 0) _windows.SelectedIndex = 0;
    }

    private async void OnSourceChanged()
    {
        _monitors.Enabled = _monitorMode.Checked;
        _windows.Enabled = _windowMode.Checked;
        if (_suppressSourceEvents) return;

        var updated = _app.Settings.Clone();
        var mode = _windowMode.Checked ? CaptureMode.Window : CaptureMode.Monitor;
        var monitorIndex = _monitors.SelectedItem is MonitorInfo m ? m.Index : 0;
        var windowTitle = _windows.SelectedItem is WindowInfo w ? w.Title : "";

        if (updated.CaptureMode == mode && updated.MonitorIndex == monitorIndex &&
            updated.WindowTitle == windowTitle)
            return;

        updated.CaptureMode = mode;
        updated.MonitorIndex = monitorIndex;
        updated.WindowTitle = windowTitle;
        await _app.ApplySettingsAsync(updated);
    }

    private void UpdateElapsed()
    {
        var started = _app.Recorder.RecordingStartedUtc;
        _elapsed.Text = started is null
            ? ""
            : "REC  " + (DateTime.UtcNow - started.Value).ToString(@"hh\:mm\:ss");
        _elapsed.ForeColor = Theme.Danger;
    }

    private void UpdateStatus()
    {
        var dot = _app.Status switch
        {
            RecorderStatus.Recording or RecorderStatus.BufferingAndRecording => Theme.Danger,
            RecorderStatus.Buffering => Theme.Success,
            RecorderStatus.Error => Theme.Warning,
            _ => Theme.TextDim
        };
        _pill.Set(_app.StatusText, dot);

        _record.Text = _app.Recorder.IsRecording ? "Stop recording" : "Start recording";
        _record.Style = _app.Recorder.IsRecording ? FlatButton.Variant.Danger : FlatButton.Variant.Accent;
        _buffer.Text = _app.Recorder.IsBuffering ? "Disarm replay buffer" : "Arm replay buffer";
        _save.Enabled = _app.Recorder.IsBuffering;

        var encoder = _app.Recorder.EncoderId;
        var resolution = _app.Settings.Resolution == "Native" ? "native resolution" : _app.Settings.Resolution;
        _detail.Text = $"{encoder}  •  {resolution} @ {_app.Settings.Fps} fps  •  " +
                       $"{_app.Settings.VideoBitrateMbps} Mbps  •  buffer {_app.Settings.ReplayLengthSeconds}s";

        _detected.Text = _app.DetectedGame is null
            ? "No game detected in the foreground."
            : "Game detected: " + _app.DetectedGame;
        _detected.ForeColor = _app.DetectedGame is null ? Theme.TextDim : Theme.Success;

        foreach (Control control in Controls)
        {
            var label = control.Controls.Find("hotkeys", false).FirstOrDefault();
            if (label is not null)
                label.Text = $"Save replay:  {_app.Settings.HotkeySaveReplay}       " +
                             $"Start/stop recording:  {_app.Settings.HotkeyToggleRecord}       " +
                             $"Arm/disarm buffer:  {_app.Settings.HotkeyToggleBuffer}";
        }

        _monitors.Enabled = _monitorMode.Checked;
        _windows.Enabled = _windowMode.Checked;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ticker.Dispose();
        base.Dispose(disposing);
    }
}
