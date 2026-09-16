using System.Drawing.Drawing2D;

namespace ClipForge.UI;

/// <summary>Dark palette and control styling helpers shared by every page.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x14, 0x14, 0x1C);
    public static readonly Color Surface = Color.FromArgb(0x1C, 0x1C, 0x27);
    public static readonly Color SurfaceAlt = Color.FromArgb(0x23, 0x23, 0x31);
    public static readonly Color SurfaceHover = Color.FromArgb(0x2B, 0x2B, 0x3B);
    public static readonly Color Border = Color.FromArgb(0x31, 0x31, 0x42);
    public static readonly Color Text = Color.FromArgb(0xEC, 0xEC, 0xF2);
    public static readonly Color TextDim = Color.FromArgb(0x9A, 0x9A, 0xAE);
    public static readonly Color Accent = Color.FromArgb(0x6C, 0x4C, 0xF1);
    public static readonly Color AccentHover = Color.FromArgb(0x81, 0x63, 0xFF);
    public static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);
    public static readonly Color Success = Color.FromArgb(0x30, 0xA4, 0x6C);
    public static readonly Color Warning = Color.FromArgb(0xF5, 0xA5, 0x24);

    public static Font Base { get; } = new("Segoe UI", 9.75f);
    public static Font Bold { get; } = new("Segoe UI Semibold", 9.75f);
    public static Font Heading { get; } = new("Segoe UI Semibold", 15f);
    public static Font Small { get; } = new("Segoe UI", 8.25f);
    public static Font Mono { get; } = new("Consolas", 9f);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        // Inset by one pixel so the antialiased outline stays inside the control.
        var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Title(string text) => new()
    {
        Text = text, Font = Heading, ForeColor = Text, AutoSize = true, BackColor = Color.Transparent
    };

    public static Label Body(string text) => new()
    {
        Text = text, Font = Base, ForeColor = Text, AutoSize = true, BackColor = Color.Transparent
    };

    public static Label Muted(string text) => new()
    {
        Text = text, Font = Small, ForeColor = TextDim, AutoSize = true, BackColor = Color.Transparent
    };

    public static CheckBox Check(string text, bool value) => new()
    {
        Text = text, Checked = value, Font = Base, ForeColor = Text,
        BackColor = Color.Transparent, AutoSize = true, FlatStyle = FlatStyle.Flat
    };

    public static ComboBox Combo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = SurfaceAlt,
        ForeColor = Text,
        Font = Base,
        Width = width,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 20
    };

    /// <summary>Owner-draws combo items; the system renderer ignores our dark colours otherwise.</summary>
    public static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        e.DrawBackground();

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var background = new SolidBrush(selected ? Accent : SurfaceAlt);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (e.Index >= 0 && e.Index < combo.Items.Count)
        {
            var text = combo.Items[e.Index]?.ToString() ?? "";
            TextRenderer.DrawText(e.Graphics, text, Base, e.Bounds, Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        e.DrawFocusRectangle();
    }

    public static NumericUpDown Number(int min, int max, int value, int width = 90)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min, Maximum = max,
            Value = Math.Clamp(value, min, max),
            BackColor = SurfaceAlt, ForeColor = Text, Font = Base,
            BorderStyle = BorderStyle.None, Width = width, TextAlign = HorizontalAlignment.Center
        };
        return numeric;
    }

    public static TextBox Input(string text, int width) => new()
    {
        Text = text, BackColor = SurfaceAlt, ForeColor = Text, Font = Base,
        BorderStyle = BorderStyle.FixedSingle, Width = width
    };

    public static Panel Card(int padding = 18) => new CardPanel { Padding = new Padding(padding) };

    public static Panel Divider() => new()
    {
        Height = 1, BackColor = Border, Dock = DockStyle.Top, Margin = new Padding(0, 10, 0, 10)
    };
}

/// <summary>Rounded, bordered surface used to group related settings.</summary>
public class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 10;
    public Color FillColor { get; set; } = Theme.Surface;
    public Color BorderColor { get; set; } = Theme.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Transparent-looking background: paint the parent colour, then the rounded card on top.
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(ClientRectangle, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        using var pen = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

/// <summary>Flat, rounded button with accent/secondary/danger variants.</summary>
public sealed class FlatButton : Button
{
    public enum Variant { Accent, Secondary, Danger, Ghost }

    private bool _hovered;
    private bool _pressed;

    public Variant Style { get; set; } = Variant.Secondary;
    public int CornerRadius { get; set; } = 8;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Bold;
        Height = 34;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var (fill, border, foreground) = Resolve();
        using var path = Theme.RoundedRect(ClientRectangle, CornerRadius);

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }
        if (border.A > 0)
        {
            using var pen = new Pen(border);
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private (Color fill, Color border, Color foreground) Resolve()
    {
        if (!Enabled)
            return (Theme.SurfaceAlt, Theme.Border, Theme.TextDim);

        return Style switch
        {
            Variant.Accent => (
                _pressed ? Theme.Accent : _hovered ? Theme.AccentHover : Theme.Accent,
                Color.Transparent, Color.White),
            Variant.Danger => (
                _hovered ? Theme.Danger : Color.FromArgb(0x3A, 0x1E, 0x24),
                Theme.Danger, _hovered ? Color.White : Theme.Danger),
            Variant.Ghost => (
                _hovered ? Theme.SurfaceHover : Color.Transparent,
                Color.Transparent, Theme.TextDim),
            _ => (
                _hovered ? Theme.SurfaceHover : Theme.SurfaceAlt,
                Theme.Border, Theme.Text)
        };
    }
}

/// <summary>Coloured status pill, e.g. "Replay buffer armed".</summary>
public sealed class StatusPill : Control
{
    private Color _dot = Theme.TextDim;

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Font = Theme.Bold;
        ForeColor = Theme.Text;
        Height = 30;
        Width = 240;
    }

    public void Set(string text, Color dotColor)
    {
        Text = text;
        _dot = dotColor;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(ClientRectangle, Height / 2);
        using var fill = new SolidBrush(Theme.SurfaceAlt);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);

        var dotSize = 9;
        using var dotBrush = new SolidBrush(_dot);
        e.Graphics.FillEllipse(dotBrush, 12, (Height - dotSize) / 2, dotSize, dotSize);

        var textRect = new Rectangle(30, 0, Width - 38, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
