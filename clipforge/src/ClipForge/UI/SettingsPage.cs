using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>All persisted options, grouped into cards.</summary>
public sealed class SettingsPage : UserControl
{
    private readonly AppController _app;
    private readonly Panel _scroll;

    private ComboBox _resolution = null!;
    private ComboBox _encoder = null!;
    private NumericUpDown _fps = null!;
    private NumericUpDown _bitrate = null!;
    private CheckBox _cursor = null!;
    private CheckBox _desktopDuplication = null!;

    private CheckBox _systemAudio = null!;
    private NumericUpDown _systemVolume = null!;
    private CheckBox _microphone = null!;
    private ComboBox _micDevice = null!;
    private NumericUpDown _micVolume = null!;
    private NumericUpDown _audioBitrate = null!;

    private CheckBox _bufferEnabled = null!;
    private NumericUpDown _bufferLength = null!;
    private CheckBox _autoArm = null!;

    private HotkeyTextBox _hotkeySave = null!;
    private HotkeyTextBox _hotkeyRecord = null!;
    private HotkeyTextBox _hotkeyBuffer = null!;

    private TextBox _clipFolder = null!;
    private CheckBox _startWithWindows = null!;
    private CheckBox _startMinimized = null!;
    private CheckBox _minimizeToTray = null!;
    private CheckBox _toasts = null!;

    private Label _ffmpegStatus = null!;
    private ProgressBar _downloadProgress = null!;
    private FlatButton _downloadButton = null!;

    public SettingsPage(AppController app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var header = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Background };
        var title = Theme.Title("Settings");
        title.Location = new Point(0, 6);
        header.Controls.Add(title);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Background };
        var save = new FlatButton
        {
            Text = "Save settings", Style = FlatButton.Variant.Accent,
            Size = new Size(150, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        save.Click += async (_, _) => await SaveAsync();
        var reset = new FlatButton { Text = "Restore defaults", Size = new Size(140, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        reset.Click += (_, _) => RestoreDefaults();
        footer.Controls.Add(save);
        footer.Controls.Add(reset);
        void LayoutFooter()
        {
            save.Location = new Point(footer.Width - save.Width, 10);
            reset.Location = new Point(save.Left - reset.Width - 8, 10);
        }
        footer.Resize += (_, _) => LayoutFooter();
        LayoutFooter();

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(0, 0, 8, 0)
        };

        // Docking is applied from the highest child index down, so the fill panel is added first
        // and the docked edges after it.
        Controls.Add(_scroll);
        Controls.Add(footer);
        Controls.Add(header);

        Build();
        LoadFrom(_app.Settings);
        _ = RefreshFfmpegStatusAsync();
    }

    // ---------------------------------------------------------------- layout helpers

    private int _y = 4;

    private CardPanel Section(string heading, int height)
    {
        var card = new CardPanel
        {
            Location = new Point(0, _y),
            Size = new Size(Math.Max(720, Width - 24), height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(18)
        };
        var label = Theme.Body(heading);
        label.Font = new Font("Segoe UI Semibold", 11f);
        label.Location = new Point(18, 14);
        card.Controls.Add(label);

        _scroll.Controls.Add(card);
        _y += height + 14;
        return card;
    }

    private static void Row(Control parent, string caption, Control field, int y, string? hint = null)
    {
        var label = Theme.Body(caption);
        label.Location = new Point(20, y + 3);
        parent.Controls.Add(label);

        field.Location = new Point(250, y);
        parent.Controls.Add(field);

        if (hint is null) return;
        var note = Theme.Muted(hint);
        note.Location = new Point(250 + field.Width + 12, y + 4);
        parent.Controls.Add(note);
    }

    private void Build()
    {
        // ---- Video ----
        var video = Section("Video", 200);
        _resolution = Theme.Combo(180);
        _resolution.DrawItem += Theme.DrawComboItem;
        _resolution.Items.AddRange(new object[] { "Native", "2160p", "1440p", "1080p", "720p" });
        Row(video, "Resolution", _resolution, 52);

        _fps = Theme.Number(15, 240, 60);
        Row(video, "Frame rate", _fps, 84, "frames per second");

        _bitrate = Theme.Number(2, 150, 20);
        Row(video, "Bitrate", _bitrate, 112, "Mbps - 20 is a good default for 1080p60");

        _encoder = Theme.Combo(260);
        _encoder.DrawItem += Theme.DrawComboItem;
        Row(video, "Encoder", _encoder, 140);

        var detect = new FlatButton { Text = "Detect", Width = 90, Location = new Point(520, 139) };
        detect.Click += async (_, _) => await DetectEncodersAsync();
        video.Controls.Add(detect);

        _cursor = Theme.Check("Capture the mouse cursor", true);
        _cursor.Location = new Point(20, 170);
        video.Controls.Add(_cursor);

        _desktopDuplication = Theme.Check("Prefer GPU Desktop Duplication for display capture", true);
        _desktopDuplication.Location = new Point(250, 170);
        video.Controls.Add(_desktopDuplication);

        // ---- Audio ----
        var audio = Section("Audio", 230);
        _systemAudio = Theme.Check("Record system / game audio", true);
        _systemAudio.Location = new Point(20, 52);
        audio.Controls.Add(_systemAudio);

        _systemVolume = Theme.Number(0, 200, 100);
        Row(audio, "System volume", _systemVolume, 80, "%");

        _microphone = Theme.Check("Record microphone", false);
        _microphone.Location = new Point(20, 112);
        audio.Controls.Add(_microphone);

        _micDevice = Theme.Combo(320);
        _micDevice.DrawItem += Theme.DrawComboItem;
        Row(audio, "Microphone", _micDevice, 138);

        _micVolume = Theme.Number(0, 200, 100);
        Row(audio, "Microphone volume", _micVolume, 166, "%");

        _audioBitrate = Theme.Number(64, 320, 160);
        Row(audio, "Audio bitrate", _audioBitrate, 194, "kbps");

        // ---- Replay buffer ----
        var buffer = Section("Instant replay", 150);
        _bufferEnabled = Theme.Check("Enable the replay buffer", true);
        _bufferEnabled.Location = new Point(20, 52);
        buffer.Controls.Add(_bufferEnabled);

        _bufferLength = Theme.Number(10, 600, 60);
        Row(buffer, "Keep the last", _bufferLength, 80, "seconds");

        _autoArm = Theme.Check("Arm automatically when a game is detected", true);
        _autoArm.Location = new Point(20, 112);
        buffer.Controls.Add(_autoArm);

        // ---- Hotkeys ----
        var hotkeys = Section("Hotkeys", 170);
        _hotkeySave = new HotkeyTextBox("");
        Row(hotkeys, "Save replay", _hotkeySave, 52, "click, then press a combination");
        _hotkeyRecord = new HotkeyTextBox("");
        Row(hotkeys, "Start / stop recording", _hotkeyRecord, 84);
        _hotkeyBuffer = new HotkeyTextBox("");
        Row(hotkeys, "Arm / disarm buffer", _hotkeyBuffer, 116);

        // ---- Output ----
        var output = Section("Clips folder", 110);
        _clipFolder = Theme.Input("", 400);
        Row(output, "Save clips to", _clipFolder, 52);

        var browse = new FlatButton { Text = "Browse", Width = 90, Location = new Point(662, 51) };
        browse.Click += (_, _) => BrowseFolder();
        output.Controls.Add(browse);

        // ---- Application ----
        var application = Section("Application", 150);
        _startWithWindows = Theme.Check("Start ClipForge when I sign in to Windows", false);
        _startWithWindows.Location = new Point(20, 52);
        _startMinimized = Theme.Check("Start minimised to the system tray", false);
        _startMinimized.Location = new Point(20, 78);
        _minimizeToTray = Theme.Check("Closing the window keeps ClipForge running in the tray", true);
        _minimizeToTray.Location = new Point(20, 104);
        _toasts = Theme.Check("Show on-screen notifications", true);
        _toasts.Location = new Point(430, 52);
        application.Controls.AddRange(new Control[]
        {
            _startWithWindows, _startMinimized, _minimizeToTray, _toasts
        });

        // ---- ffmpeg ----
        var tools = Section("Capture backend (ffmpeg)", 170);
        _ffmpegStatus = Theme.Muted("Checking...");
        _ffmpegStatus.Location = new Point(20, 52);
        _ffmpegStatus.MaximumSize = new Size(700, 0);
        tools.Controls.Add(_ffmpegStatus);

        _downloadButton = new FlatButton
        {
            Text = "Download ffmpeg", Style = FlatButton.Variant.Accent,
            Width = 150, Location = new Point(20, 88)
        };
        _downloadButton.Click += async (_, _) => await DownloadFfmpegAsync();
        tools.Controls.Add(_downloadButton);

        var locate = new FlatButton { Text = "Use my own build", Width = 150, Location = new Point(180, 88) };
        locate.Click += (_, _) => LocateFfmpeg();
        tools.Controls.Add(locate);

        var openLog = new FlatButton { Text = "Open log", Width = 100, Location = new Point(340, 88) };
        openLog.Click += (_, _) => AppController.OpenFolder(AppPaths.DataDir);
        tools.Controls.Add(openLog);

        _downloadProgress = new ProgressBar
        {
            Location = new Point(20, 130), Size = new Size(600, 8),
            Style = ProgressBarStyle.Continuous, Visible = false
        };
        tools.Controls.Add(_downloadProgress);
    }

    // ---------------------------------------------------------------- state

    private void LoadFrom(Settings settings)
    {
        _resolution.SelectedItem = settings.Resolution;
        if (_resolution.SelectedIndex < 0) _resolution.SelectedIndex = 3;
        _fps.Value = settings.Fps;
        _bitrate.Value = settings.VideoBitrateMbps;
        _cursor.Checked = settings.CaptureCursor;
        _desktopDuplication.Checked = settings.PreferDesktopDuplication;

        PopulateEncoders(settings.Encoder);

        _systemAudio.Checked = settings.CaptureSystemAudio;
        _systemVolume.Value = settings.SystemVolumePercent;
        _microphone.Checked = settings.CaptureMicrophone;
        _micVolume.Value = settings.MicVolumePercent;
        _audioBitrate.Value = settings.AudioBitrateKbps;

        _micDevice.Items.Clear();
        _micDevice.Items.Add(new AudioDeviceInfo("", "System default"));
        foreach (var device in AudioEngine.GetInputDevices()) _micDevice.Items.Add(device);
        _micDevice.SelectedIndex = 0;
        for (var i = 0; i < _micDevice.Items.Count; i++)
        {
            if (_micDevice.Items[i] is AudioDeviceInfo info && info.Id == settings.MicDeviceId)
            {
                _micDevice.SelectedIndex = i;
                break;
            }
        }

        _bufferEnabled.Checked = settings.ReplayBufferEnabled;
        _bufferLength.Value = settings.ReplayLengthSeconds;
        _autoArm.Checked = settings.AutoStartBufferOnGame;

        _hotkeySave.Gesture = settings.HotkeySaveReplay;
        _hotkeyRecord.Gesture = settings.HotkeyToggleRecord;
        _hotkeyBuffer.Gesture = settings.HotkeyToggleBuffer;

        _clipFolder.Text = settings.ClipFolder;
        _startWithWindows.Checked = settings.StartWithWindows;
        _startMinimized.Checked = settings.StartMinimized;
        _minimizeToTray.Checked = settings.MinimizeToTray;
        _toasts.Checked = settings.ShowOverlayToasts;
    }

    private void PopulateEncoders(string selected)
    {
        if (_encoder.Items.Count == 0)
        {
            _encoder.Items.Add(new EncoderOption("auto", "Automatic (best available)", "auto"));
            _encoder.Items.Add(new EncoderOption("h264_nvenc", "H.264 - NVIDIA NVENC (GPU)", "NVIDIA"));
            _encoder.Items.Add(new EncoderOption("hevc_nvenc", "HEVC - NVIDIA NVENC (GPU)", "NVIDIA"));
            _encoder.Items.Add(new EncoderOption("h264_amf", "H.264 - AMD AMF (GPU)", "AMD"));
            _encoder.Items.Add(new EncoderOption("h264_qsv", "H.264 - Intel Quick Sync", "Intel"));
            _encoder.Items.Add(new EncoderOption("libx264", "H.264 - x264 (CPU)", "CPU"));
        }
        SelectEncoder(selected);
    }

    private void SelectEncoder(string id)
    {
        for (var i = 0; i < _encoder.Items.Count; i++)
        {
            if (_encoder.Items[i] is EncoderOption option &&
                option.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                _encoder.SelectedIndex = i;
                return;
            }
        }
        _encoder.SelectedIndex = 0;
    }

    private Settings Collect()
    {
        var settings = _app.Settings.Clone();
        settings.Resolution = _resolution.SelectedItem?.ToString() ?? "1080p";
        settings.Fps = (int)_fps.Value;
        settings.VideoBitrateMbps = (int)_bitrate.Value;
        settings.CaptureCursor = _cursor.Checked;
        settings.PreferDesktopDuplication = _desktopDuplication.Checked;
        settings.Encoder = _encoder.SelectedItem is EncoderOption option ? option.Id : "auto";

        settings.CaptureSystemAudio = _systemAudio.Checked;
        settings.SystemVolumePercent = (int)_systemVolume.Value;
        settings.CaptureMicrophone = _microphone.Checked;
        settings.MicVolumePercent = (int)_micVolume.Value;
        settings.MicDeviceId = _micDevice.SelectedItem is AudioDeviceInfo device ? device.Id : "";
        settings.AudioBitrateKbps = (int)_audioBitrate.Value;

        settings.ReplayBufferEnabled = _bufferEnabled.Checked;
        settings.ReplayLengthSeconds = (int)_bufferLength.Value;
        settings.AutoStartBufferOnGame = _autoArm.Checked;

        settings.HotkeySaveReplay = _hotkeySave.Gesture;
        settings.HotkeyToggleRecord = _hotkeyRecord.Gesture;
        settings.HotkeyToggleBuffer = _hotkeyBuffer.Gesture;

        settings.ClipFolder = string.IsNullOrWhiteSpace(_clipFolder.Text)
            ? AppPaths.DefaultClipFolder
            : _clipFolder.Text.Trim();

        settings.StartWithWindows = _startWithWindows.Checked;
        settings.StartMinimized = _startMinimized.Checked;
        settings.MinimizeToTray = _minimizeToTray.Checked;
        settings.ShowOverlayToasts = _toasts.Checked;
        return settings;
    }

    private async Task SaveAsync()
    {
        var settings = Collect();
        try
        {
            Directory.CreateDirectory(settings.ClipFolder);
        }
        catch (Exception ex)
        {
            MessageBox.Show("That clips folder cannot be used: " + ex.Message, "ClipForge",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _app.ApplySettingsAsync(settings);

        if (_app.Hotkeys.Conflicts.Count > 0)
            MessageBox.Show(
                "These hotkeys are already registered by another application and will not fire:\n\n" +
                string.Join("\n", _app.Hotkeys.Conflicts),
                "Hotkey conflict", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            _app.Notify("Settings saved", "Capture settings applied", Theme.Success);
    }

    private void RestoreDefaults()
    {
        var confirm = MessageBox.Show("Reset every setting to its default value?", "ClipForge",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var defaults = new Settings { FirstRunCompleted = true, FfmpegPath = _app.Settings.FfmpegPath };
        LoadFrom(defaults);
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where ClipForge saves clips",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_clipFolder.Text) ? _clipFolder.Text : AppPaths.DefaultClipFolder
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _clipFolder.Text = dialog.SelectedPath;
    }

    private void LocateFfmpeg()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|Executables|*.exe"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var settings = _app.Settings.Clone();
        settings.FfmpegPath = dialog.FileName;
        _ = _app.ApplySettingsAsync(settings);
        _ = RefreshFfmpegStatusAsync();
    }

    private async Task DetectEncodersAsync()
    {
        var ffmpeg = FFmpegManager.Resolve(_app.Settings);
        if (ffmpeg is null)
        {
            MessageBox.Show("Download ffmpeg first.", "ClipForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _ffmpegStatus.Text = "Testing encoders - this takes a few seconds...";
        var working = await FFmpegManager.DetectEncodersAsync(ffmpeg);

        var current = _encoder.SelectedItem is EncoderOption option ? option.Id : "auto";
        _encoder.Items.Clear();
        _encoder.Items.Add(new EncoderOption("auto", "Automatic (best available)", "auto"));
        foreach (var encoder in working) _encoder.Items.Add(encoder);
        SelectEncoder(current);

        _ffmpegStatus.Text = $"Working encoders: {string.Join(", ", working.Select(w => w.Id))}";
    }

    private async Task DownloadFfmpegAsync()
    {
        _downloadButton.Enabled = false;
        _downloadProgress.Visible = true;
        _downloadProgress.Value = 0;

        var progress = new Progress<(string stage, int percent)>(update =>
        {
            _ffmpegStatus.Text = update.stage;
            _downloadProgress.Value = Math.Clamp(update.percent, 0, 100);
        });

        try
        {
            var ok = await FFmpegManager.DownloadAsync(progress, CancellationToken.None);
            if (ok)
            {
                await _app.Recorder.InitializeAsync();
                _app.Notify("ffmpeg installed", "ClipForge is ready to record", Theme.Success);
            }
            else
            {
                MessageBox.Show(
                    "The download did not succeed. Check your internet connection, or install ffmpeg " +
                    "yourself and point ClipForge at it with \"Use my own build\".",
                    "ClipForge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg download failed", ex);
            MessageBox.Show("Download failed: " + ex.Message, "ClipForge",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _downloadButton.Enabled = true;
            _downloadProgress.Visible = false;
            await RefreshFfmpegStatusAsync();
        }
    }

    private async Task RefreshFfmpegStatusAsync()
    {
        var ffmpeg = FFmpegManager.Resolve(_app.Settings);
        if (ffmpeg is null)
        {
            _ffmpegStatus.Text = "ffmpeg was not found. ClipForge needs it to capture - " +
                                 "click Download ffmpeg (about 80 MB, one time).";
            _ffmpegStatus.ForeColor = Theme.Warning;
            _downloadButton.Visible = true;
            return;
        }

        _ffmpegStatus.ForeColor = Theme.TextDim;
        _ffmpegStatus.Text = "Found: " + ffmpeg;
        try
        {
            var version = await FFmpegManager.GetVersionAsync(ffmpeg);
            var dda = await FFmpegManager.SupportsDdagrabAsync(ffmpeg);
            _ffmpegStatus.Text = $"{ffmpeg}\nVersion {version}  •  " +
                                 $"Desktop Duplication {(dda ? "supported" : "not available in this build")}";
        }
        catch (Exception ex)
        {
            Log.Warn("ffmpeg probe failed: " + ex.Message);
        }
    }
}
