using System.Diagnostics;
using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>One clip in the library grid: thumbnail, metadata and row of actions.</summary>
public sealed class ClipCard : CardPanel
{
    private readonly ClipInfo _clip;
    private readonly PictureBox _thumbnail;
    private readonly Label _name;

    public event EventHandler? Changed;

    public ClipCard(ClipInfo clip)
    {
        _clip = clip;
        Size = new Size(300, 252);
        Margin = new Padding(0, 0, 16, 16);
        Padding = new Padding(10);
        CornerRadius = 12;

        _thumbnail = new PictureBox
        {
            Size = new Size(280, 140),
            Location = new Point(10, 10),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(0x10, 0x10, 0x18),
            Cursor = Cursors.Hand
        };
        _thumbnail.Click += (_, _) => Play();
        Controls.Add(_thumbnail);
        LoadThumbnail();

        _name = new Label
        {
            Text = clip.Name,
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            Location = new Point(12, 158),
            Size = new Size(276, 18),
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };
        Controls.Add(_name);

        var meta = new Label
        {
            Text = $"{clip.DurationText}   •   {clip.SizeText}   •   {clip.CreatedUtc.ToLocalTime():dd MMM HH:mm}",
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            Location = new Point(12, 178),
            Size = new Size(276, 16),
            BackColor = Color.Transparent
        };
        Controls.Add(meta);

        var play = new FlatButton { Text = "Play", Style = FlatButton.Variant.Accent, Size = new Size(70, 30), Location = new Point(12, 204) };
        var folder = new FlatButton { Text = "Folder", Size = new Size(72, 30), Location = new Point(88, 204) };
        var rename = new FlatButton { Text = "Rename", Size = new Size(76, 30), Location = new Point(166, 204) };
        var delete = new FlatButton { Text = "Delete", Style = FlatButton.Variant.Danger, Size = new Size(46, 30), Location = new Point(248, 204) };

        play.Click += (_, _) => Play();
        folder.Click += (_, _) => RevealInExplorer();
        rename.Click += (_, _) => Rename();
        delete.Click += (_, _) => Delete();

        Controls.AddRange(new Control[] { play, folder, rename, delete });
    }

    private void LoadThumbnail()
    {
        try
        {
            if (_clip.ThumbnailPath is not null && File.Exists(_clip.ThumbnailPath))
            {
                // Read through memory so the cache file is never locked by the PictureBox.
                var bytes = File.ReadAllBytes(_clip.ThumbnailPath);
                using var stream = new MemoryStream(bytes);
                _thumbnail.Image = Image.FromStream(stream);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Thumbnail load failed for {_clip.Path}: {ex.Message}");
        }
        _thumbnail.Image = null;
    }

    private void Play()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_clip.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open the clip: " + ex.Message, "ClipForge",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RevealInExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_clip.Path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open Explorer: " + ex.Message);
        }
    }

    private void Rename()
    {
        using var dialog = new InputDialog("Rename clip", "New name for this clip:", _clip.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (ClipLibrary.Rename(_clip, dialog.Value, out var error))
        {
            _clip.Name = dialog.Value;
            _name.Text = dialog.Value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            MessageBox.Show(error, "Rename failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Delete()
    {
        var confirm = MessageBox.Show(
            $"Delete \"{_clip.Name}\"?\n\nThis removes the file from disk.",
            "Delete clip", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        // Release the bitmap first or the file stays locked on some systems.
        _thumbnail.Image?.Dispose();
        _thumbnail.Image = null;

        if (ClipLibrary.Delete(_clip)) Changed?.Invoke(this, EventArgs.Empty);
        else MessageBox.Show("Could not delete the clip. It may be open in another program.",
            "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _thumbnail.Image?.Dispose();
        base.Dispose(disposing);
    }
}
