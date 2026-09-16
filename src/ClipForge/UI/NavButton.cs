using System.Drawing.Drawing2D;

namespace ClipForge.UI;

/// <summary>Sidebar navigation entry with an accent bar when active.</summary>
public sealed class NavButton : Control
{
    private bool _hovered;
    private bool _active;

    public NavButton(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(186, 40);
        Font = Theme.Bold;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        if (_active || _hovered)
        {
            using var path = Theme.RoundedRect(ClientRectangle, 8);
            using var brush = new SolidBrush(_active ? Theme.SurfaceHover : Theme.SurfaceAlt);
            g.FillPath(brush, path);
        }

        if (_active)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, 0, 10, 3, Height - 20);
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(18, 0, Width - 24, Height),
            _active ? Theme.Text : Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
