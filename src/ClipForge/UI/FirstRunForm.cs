using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>
/// Shown when ffmpeg is missing. ClipForge does not bundle ffmpeg (it is a separately licensed
/// GPL build), so the first run offers to fetch it.
/// </summary>
public sealed class FirstRunForm : Form
{
    private readonly AppController _app;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly FlatButton _download;
    private readonly FlatButton _locate;
    private readonly FlatButton _skip;

    public FirstRunForm(AppController app)
    {
        _app = app;

        Text = "Set up ClipForge";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Base;
        ClientSize = new Size(560, 280);

        var heading = Theme.Title("One more thing");
        heading.Location = new Point(28, 26);
        Controls.Add(heading);

        var body = new Label
        {
            Text = "ClipForge captures and encodes through ffmpeg, which is not bundled with the app.\n\n" +
                   "Download it now (about 80 MB, once) and ClipForge will keep it in your local app " +
                   "data folder. If you already have an ffmpeg build you prefer, point ClipForge at it " +
                   "instead.",
            Font = Theme.Base,
            ForeColor = Theme.TextDim,
            Location = new Point(30, 66),
            Size = new Size(500, 92)
        };
        Controls.Add(body);

        _status = Theme.Muted("");
        _status.Location = new Point(30, 164);
        _status.MaximumSize = new Size(500, 0);
        Controls.Add(_status);

        _progress = new ProgressBar
        {
            Location = new Point(30, 192),
            Size = new Size(500, 8),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };
        Controls.Add(_progress);

        _download = new FlatButton
        {
            Text = "Download ffmpeg",
            Style = FlatButton.Variant.Accent,
            Size = new Size(160, 38),
            Location = new Point(30, 218)
        };
        _download.Click += async (_, _) => await DownloadAsync();

        _locate = new FlatButton { Text = "I already have it", Size = new Size(150, 38), Location = new Point(200, 218) };
        _locate.Click += (_, _) => Locate();

        _skip = new FlatButton { Text = "Later", Style = FlatButton.Variant.Ghost, Size = new Size(90, 38), Location = new Point(440, 218) };
        _skip.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _download, _locate, _skip });
    }

    private async Task DownloadAsync()
    {
        _download.Enabled = false;
        _locate.Enabled = false;
        _progress.Visible = true;

        var progress = new Progress<(string stage, int percent)>(update =>
        {
            _status.Text = update.stage;
            _progress.Value = Math.Clamp(update.percent, 0, 100);
        });

        try
        {
            if (await FFmpegManager.DownloadAsync(progress, CancellationToken.None))
            {
                _status.Text = "ffmpeg installed. ClipForge is ready.";
                _status.ForeColor = Theme.Success;
                await Task.Delay(700);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _status.Text = "Download failed. Check your connection, or use an existing ffmpeg build.";
            _status.ForeColor = Theme.Warning;
        }
        catch (Exception ex)
        {
            Log.Error("First-run ffmpeg download failed", ex);
            _status.Text = "Download failed: " + ex.Message;
            _status.ForeColor = Theme.Danger;
        }
        finally
        {
            _download.Enabled = true;
            _locate.Enabled = true;
            _progress.Visible = false;
        }
    }

    private void Locate()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|Executables|*.exe"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var settings = _app.Settings;
        settings.FfmpegPath = dialog.FileName;
        settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
