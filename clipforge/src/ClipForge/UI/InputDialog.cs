namespace ClipForge.UI;

/// <summary>Simple themed single-line prompt.</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _input;

    public string Value => _input.Text.Trim();

    public InputDialog(string title, string prompt, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        ClientSize = new Size(420, 150);

        var label = Theme.Body(prompt);
        label.Location = new Point(20, 20);
        Controls.Add(label);

        _input = Theme.Input(initial, 380);
        _input.Location = new Point(20, 50);
        _input.SelectAll();
        Controls.Add(_input);

        var ok = new FlatButton { Text = "Save", Style = FlatButton.Variant.Accent, Width = 100, Location = new Point(300, 96) };
        var cancel = new FlatButton { Text = "Cancel", Width = 100, Location = new Point(190, 96) };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }
}
