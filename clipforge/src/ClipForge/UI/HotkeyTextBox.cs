using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>Click, then press a combination to rebind. Shows the gesture as text.</summary>
public sealed class HotkeyTextBox : TextBox
{
    private string _gesture = "";

    public HotkeyTextBox(string initial)
    {
        _gesture = initial;
        Text = string.IsNullOrEmpty(initial) ? "Not set" : initial;
        ReadOnly = true;
        Cursor = Cursors.Hand;
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        BorderStyle = BorderStyle.FixedSingle;
        TextAlign = HorizontalAlignment.Center;
        Width = 170;
    }

    public string Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            Text = string.IsNullOrEmpty(value) ? "Not set" : value;
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        Text = "Press keys...";
        BackColor = Theme.Accent;
        base.OnEnter(e);
    }

    protected override void OnLeave(EventArgs e)
    {
        Text = string.IsNullOrEmpty(_gesture) ? "Not set" : _gesture;
        BackColor = Theme.SurfaceAlt;
        base.OnLeave(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            Gesture = "";
            Parent?.SelectNextControl(this, true, true, true, true);
            return true;
        }

        var described = HotkeyManager.Describe(keyData);
        if (string.IsNullOrEmpty(described)) return true;   // swallow lone modifiers

        // A bare key would fire inside games while typing; require at least one modifier.
        if (!described.Contains('+'))
        {
            Text = "Add Ctrl / Alt / Shift";
            return true;
        }

        Gesture = described;
        Parent?.SelectNextControl(this, true, true, true, true);
        return true;
    }
}
