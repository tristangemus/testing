using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>Library grid of saved clips with search, refresh and folder access.</summary>
public sealed class ClipsPage : UserControl
{
    private readonly AppController _app;
    private readonly FlowLayoutPanel _grid;
    private readonly TextBox _search;
    private readonly Label _summary;
    private readonly Label _empty;
    private List<ClipInfo> _clips = new();

    public ClipsPage(AppController app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;

        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Background };

        var title = Theme.Title("Clips");
        title.Location = new Point(0, 6);
        header.Controls.Add(title);

        _summary = Theme.Muted("");
        _summary.Location = new Point(2, 36);
        header.Controls.Add(_summary);

        _search = Theme.Input("", 220);
        _search.Location = new Point(0, 0);
        _search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _search.PlaceholderText = "Search clips";
        _search.TextChanged += (_, _) => Render();
        header.Controls.Add(_search);

        var refresh = new FlatButton { Text = "Refresh", Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        refresh.Click += (_, _) => Reload();
        header.Controls.Add(refresh);

        var openFolder = new FlatButton
        {
            Text = "Open folder", Width = 110, Style = FlatButton.Variant.Accent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        openFolder.Click += (_, _) => AppController.OpenFolder(_app.Settings.ClipFolder);
        header.Controls.Add(openFolder);

        void LayoutHeader()
        {
            openFolder.Location = new Point(header.Width - openFolder.Width, 4);
            refresh.Location = new Point(openFolder.Left - refresh.Width - 8, 4);
            _search.Location = new Point(refresh.Left - _search.Width - 8, 6);
        }
        header.Resize += (_, _) => LayoutHeader();
        LayoutHeader();

        _grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Background,
            Padding = new Padding(0, 8, 0, 8)
        };

        _empty = new Label
        {
            Text = "No clips yet.\n\nArm the replay buffer on the Capture page, play something,\n" +
                   "then press your save-replay hotkey to keep the last moments.",
            Font = Theme.Base,
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Visible = false
        };

        Controls.Add(_grid);
        Controls.Add(_empty);
        Controls.Add(header);

        _app.ClipsChanged += (_, _) => Reload();
    }

    public void Reload()
    {
        _clips = ClipLibrary.Scan(_app.Settings.ClipFolder);
        _ = BackfillMetadataAsync();
        Render();
    }

    /// <summary>Fills in durations and thumbnails for clips the app has not described yet.</summary>
    private async Task BackfillMetadataAsync()
    {
        var ffmpeg = FFmpegManager.Resolve(_app.Settings);
        var ffprobe = FFmpegManager.ResolveProbe(_app.Settings);
        if (ffmpeg is null) return;

        var pending = _clips.Where(c => c.ThumbnailPath is null || c.DurationSeconds <= 0).Take(40).ToList();
        if (pending.Count == 0) return;

        var changed = false;
        foreach (var clip in pending)
        {
            try
            {
                if (clip.DurationSeconds <= 0 && ffprobe is not null)
                    clip.DurationSeconds = await FFmpegManager.GetDurationSecondsAsync(ffprobe, clip.Path);
                clip.ThumbnailPath ??= await ClipLibrary.EnsureThumbnailAsync(ffmpeg, clip);
                changed = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Metadata backfill failed for {clip.Path}: {ex.Message}");
            }
        }

        if (changed && !IsDisposed && IsHandleCreated)
            BeginInvoke(new Action(Render));
    }

    private void Render()
    {
        var filter = _search.Text.Trim();
        var visible = string.IsNullOrEmpty(filter)
            ? _clips
            : _clips.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        _grid.SuspendLayout();
        foreach (Control control in _grid.Controls) control.Dispose();
        _grid.Controls.Clear();

        foreach (var clip in visible)
        {
            var card = new ClipCard(clip);
            card.Changed += (_, _) => Reload();
            _grid.Controls.Add(card);
        }
        _grid.ResumeLayout();

        var totalBytes = _clips.Sum(c => c.SizeBytes);
        _summary.Text = _clips.Count == 0
            ? _app.Settings.ClipFolder
            : $"{_clips.Count} clip{(_clips.Count == 1 ? "" : "s")}  •  " +
              $"{totalBytes / (double)(1L << 30):0.00} GB  •  {_app.Settings.ClipFolder}";

        _empty.Visible = visible.Count == 0;
        _grid.Visible = visible.Count > 0;
    }
}
