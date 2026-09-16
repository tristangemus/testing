using ClipForge.Core;

namespace ClipForge.UI;

public sealed class MainForm : Form
{
    private readonly AppController _app;
    private readonly Panel _content;
    private readonly List<(NavButton button, Func<UserControl> factory)> _nav = new();
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayBuffer;
    private readonly ToolStripMenuItem _trayRecord;
    private readonly Label _sidebarStatus;

    private bool _reallyExit;
    private readonly bool _startMinimized;

    public MainForm(AppController app, bool startMinimized)
    {
        _app = app;

        Text = "ClipForge";
        Icon = AppIcon.Value;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        MinimumSize = new Size(1000, 660);
        ClientSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        // ---- sidebar ----
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 216, BackColor = Theme.Surface };

        var logo = new PictureBox
        {
            Image = AppIcon.Value.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(30, 30),
            Location = new Point(18, 22),
            BackColor = Color.Transparent
        };
        sidebar.Controls.Add(logo);

        var wordmark = new Label
        {
            Text = "ClipForge",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(56, 25),
            BackColor = Color.Transparent
        };
        sidebar.Controls.Add(wordmark);

        AddNav(sidebar, "Clips", 82, () => new ClipsPage(_app));
        AddNav(sidebar, "Capture", 128, () => new CapturePage(_app));
        AddNav(sidebar, "Settings", 174, () => new SettingsPage(_app));

        _sidebarStatus = new Label
        {
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(18, 0, 12, 12),
            TextAlign = ContentAlignment.BottomLeft,
            BackColor = Color.Transparent
        };
        sidebar.Controls.Add(_sidebarStatus);

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(26, 18, 26, 18)
        };

        Controls.Add(_content);
        Controls.Add(sidebar);

        // ---- tray ----
        _trayBuffer = new ToolStripMenuItem("Arm replay buffer", null, async (_, _) => await _app.ToggleBufferAsync());
        _trayRecord = new ToolStripMenuItem("Start recording", null, async (_, _) => await _app.ToggleRecordingAsync());

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem("Open ClipForge", null, (_, _) => RestoreFromTray()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayRecord);
        menu.Items.Add(_trayBuffer);
        menu.Items.Add(new ToolStripMenuItem("Save replay now", null, async (_, _) => await _app.SaveReplayAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open clips folder", null,
            (_, _) => AppController.OpenFolder(_app.Settings.ClipFolder)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => { _reallyExit = true; Close(); }));

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Text = "ClipForge",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        _app.StatusChanged += (_, _) => UpdateChrome();

        _startMinimized = startMinimized;
        ShowPage("Capture");
        UpdateChrome();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_startMinimized) return;

        // Only safe once the handle exists, which is why this is not done in the constructor.
        WindowState = FormWindowState.Minimized;
        HideToTray();
    }

    private void AddNav(Control parent, string label, int y, Func<UserControl> factory)
    {
        var button = new NavButton(label) { Location = new Point(15, y) };
        button.Click += (_, _) => ShowPage(label);
        parent.Controls.Add(button);
        _nav.Add((button, factory));
    }

    private void ShowPage(string label)
    {
        if (!_pages.TryGetValue(label, out var page))
        {
            var entry = _nav.FirstOrDefault(n => n.button.Text == label);
            if (entry.factory is null) return;
            page = entry.factory();
            _pages[label] = page;
        }

        _content.SuspendLayout();
        foreach (Control control in _content.Controls) control.Visible = false;
        if (!_content.Controls.Contains(page)) _content.Controls.Add(page);
        page.Visible = true;
        page.BringToFront();
        _content.ResumeLayout();

        foreach (var (button, _) in _nav) button.Active = button.Text == label;

        if (page is ClipsPage clips) clips.Reload();
    }

    private void UpdateChrome()
    {
        var recording = _app.Recorder.IsRecording;
        var buffering = _app.Recorder.IsBuffering;

        _trayRecord.Text = recording ? "Stop recording" : "Start recording";
        _trayBuffer.Text = buffering ? "Disarm replay buffer" : "Arm replay buffer";

        var state = recording ? "Recording" : buffering ? "Replay buffer armed" : "Idle";
        _tray.Text = ("ClipForge - " + state).Length > 63
            ? "ClipForge"
            : "ClipForge - " + state;

        _sidebarStatus.Text = $"{state}\nv{AppPaths.Version}";
        _sidebarStatus.ForeColor = recording ? Theme.Danger : buffering ? Theme.Success : Theme.TextDim;
    }

    private void RestoreFromTray()
    {
        base.Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        BringToFront();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && _app.Settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            _app.Notify("Still recording in the background",
                "ClipForge is in the system tray. Right-click its icon to exit.", Theme.Accent);
            return;
        }

        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _app.Settings.MinimizeToTray) HideToTray();
    }
}
