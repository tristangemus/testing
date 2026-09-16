using ClipForge.Core;
using System.Drawing.Drawing2D;

namespace ClipForge.UI;

/// <summary>
/// Small "Clip saved" notification drawn over whatever is on screen. It never takes focus and is
/// click-through, so it cannot steal input from a game.
/// </summary>
public sealed class ToastOverlay : Form
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private string _title = "";
    private string _detail = "";
    private Color _accent = Theme.Accent;
    private int _ticks;

    private const int HoldTicks = 28;   // ~1.9s at 66ms
    private const int FadeTicks = 12;

    public ToastOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x0E, 0x0E, 0x14);
        Size = new Size(340, 84);
        Opacity = 0;
        DoubleBuffered = true;

        _timer.Interval = 66;
        _timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW |
                          NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
            return cp;
        }
    }

    public void Show(string title, string detail, Color accent)
    {
        _title = title;
        _detail = detail;
        _accent = accent;
        _ticks = 0;

        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Right - Width - 24, screen.Bottom - Height - 24);

        Opacity = 0;
        Invalidate();
        if (!Visible) Show();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _ticks++;
        if (_ticks <= 4)
            Opacity = Math.Min(0.96, _ticks / 4.0 * 0.96);
        else if (_ticks > HoldTicks)
            Opacity = Math.Max(0, 0.96 * (1 - (_ticks - HoldTicks) / (double)FadeTicks));

        if (_ticks >= HoldTicks + FadeTicks)
        {
            _timer.Stop();
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Theme.RoundedRect(ClientRectangle, 12);
        using var fill = new SolidBrush(Color.FromArgb(0x1A, 0x1A, 0x24));
        using var pen = new Pen(Color.FromArgb(0x3A, 0x3A, 0x4E));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        using var bar = new SolidBrush(_accent);
        g.FillRectangle(bar, 0, 12, 4, Height - 24);

        TextRenderer.DrawText(g, _title, new Font("Segoe UI Semibold", 11f),
            new Rectangle(18, 16, Width - 32, 24), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(g, _detail, Theme.Small,
            new Rectangle(18, 42, Width - 32, 28), Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
