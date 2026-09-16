using System.Diagnostics;
using ClipForge.Core;

namespace ClipForge.UI;

/// <summary>
/// Single owner of app state: settings, the recorder, hotkeys and notifications. Engine events can
/// arrive on background threads, so everything is re-posted to the UI thread before it is raised.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly Control _marshal = new();
    private readonly System.Windows.Forms.Timer _gameWatcher = new();
    private readonly ToastOverlay _toast = new();

    private bool _bufferAutoStarted;
    private string? _lastDetectedGame;
    private int _gameAbsentTicks;
    private bool _busy;

    public Settings Settings { get; private set; }
    public RecorderEngine Recorder { get; }
    public HotkeyManager Hotkeys { get; } = new();

    public string StatusText { get; private set; } = "Starting...";
    public RecorderStatus Status { get; private set; } = RecorderStatus.Idle;
    public string? DetectedGame => _lastDetectedGame;

    public event EventHandler? StatusChanged;
    public event EventHandler? ClipsChanged;
    public event EventHandler? SettingsChanged;

    public AppController(Settings settings)
    {
        Settings = settings;

        // Force handle creation on the UI thread now: engine callbacks arrive on ffmpeg/audio
        // threads and need a reliable BeginInvoke target, which exists before Application.Run.
        _ = _marshal.Handle;

        Recorder = new RecorderEngine(settings);

        Recorder.StatusChanged += (_, e) => Post(() =>
        {
            Status = e.Status;
            StatusText = e.Message;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        });

        Recorder.ClipSaved += (_, e) => Post(() =>
        {
            ClipsChanged?.Invoke(this, EventArgs.Empty);
            Notify(e.FromReplayBuffer ? "Replay saved" : "Recording saved",
                $"{e.Clip.Name}  •  {e.Clip.DurationText}", Theme.Success);
        });

        Recorder.Failed += (_, message) => Post(() =>
        {
            Status = RecorderStatus.Error;
            StatusText = message;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            Notify("ClipForge", message, Theme.Danger);
        });

        Hotkeys.Triggered += OnHotkey;

        _gameWatcher.Interval = 3000;
        _gameWatcher.Tick += OnGameWatcherTick;
    }

    public async Task InitializeAsync()
    {
        AppPaths.EnsureAll();
        Hotkeys.Rebind(Settings);

        if (Hotkeys.Conflicts.Count > 0)
            Notify("Hotkey unavailable",
                "Already used by another app: " + string.Join(", ", Hotkeys.Conflicts), Theme.Warning);

        var ready = await Recorder.InitializeAsync();
        if (ready && Settings.ReplayBufferEnabled && !Settings.AutoStartBufferOnGame)
            await Recorder.StartBufferAsync();

        _gameWatcher.Start();
        RaiseStatus(ready ? StatusText : "ffmpeg is missing - open Settings");
    }

    private void RaiseStatus(string text)
    {
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Post(Action action)
    {
        try
        {
            if (_marshal.IsHandleCreated && _marshal.InvokeRequired) _marshal.BeginInvoke(action);
            else action();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not marshal to the UI thread: " + ex.Message);
        }
    }

    public void Notify(string title, string detail, Color accent)
    {
        if (!Settings.ShowOverlayToasts) return;
        try { _toast.Show(title, detail, accent); }
        catch (Exception ex) { Log.Warn("Toast failed: " + ex.Message); }
    }

    // ---------------------------------------------------------------- actions

    private async void OnHotkey(object? sender, HotkeyAction action)
    {
        try
        {
            switch (action)
            {
                case HotkeyAction.SaveReplay: await SaveReplayAsync(); break;
                case HotkeyAction.ToggleRecording: await ToggleRecordingAsync(); break;
                case HotkeyAction.ToggleBuffer: await ToggleBufferAsync(); break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey handler failed", ex);
        }
    }

    public async Task SaveReplayAsync()
    {
        if (_busy) return;
        if (!Recorder.IsBuffering)
        {
            Notify("Replay buffer is off", "Press " + Settings.HotkeyToggleBuffer + " to arm it", Theme.Warning);
            return;
        }

        _busy = true;
        try
        {
            Notify("Saving replay...", $"Last {Settings.ReplayLengthSeconds} seconds", Theme.Accent);
            await Recorder.SaveReplayAsync();
        }
        finally { _busy = false; }
    }

    public async Task ToggleRecordingAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Recorder.IsRecording)
            {
                await Recorder.StopRecordingAsync();
            }
            else if (await Recorder.StartRecordingAsync())
            {
                Notify("Recording started", "Press " + Settings.HotkeyToggleRecord + " to stop", Theme.Danger);
            }
        }
        finally { _busy = false; }
    }

    public async Task ToggleBufferAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Recorder.IsBuffering)
            {
                Recorder.StopBuffer();
                _bufferAutoStarted = false;
                Notify("Replay buffer off", "No longer capturing in the background", Theme.TextDim);
            }
            else if (await Recorder.StartBufferAsync())
            {
                Notify("Replay buffer armed",
                    $"Press {Settings.HotkeySaveReplay} to keep the last {Settings.ReplayLengthSeconds}s",
                    Theme.Success);
            }
        }
        finally { _busy = false; }
    }

    // ---------------------------------------------------------------- settings

    public async Task ApplySettingsAsync(Settings updated)
    {
        var needsRestart =
            updated.Fps != Settings.Fps ||
            updated.Resolution != Settings.Resolution ||
            updated.Encoder != Settings.Encoder ||
            updated.VideoBitrateMbps != Settings.VideoBitrateMbps ||
            updated.CaptureMode != Settings.CaptureMode ||
            updated.MonitorIndex != Settings.MonitorIndex ||
            updated.WindowTitle != Settings.WindowTitle ||
            updated.ReplayLengthSeconds != Settings.ReplayLengthSeconds ||
            updated.CaptureSystemAudio != Settings.CaptureSystemAudio ||
            updated.CaptureMicrophone != Settings.CaptureMicrophone ||
            updated.MicDeviceId != Settings.MicDeviceId ||
            updated.CaptureCursor != Settings.CaptureCursor ||
            updated.PreferDesktopDuplication != Settings.PreferDesktopDuplication ||
            updated.FfmpegPath != Settings.FfmpegPath;

        var wasBuffering = Recorder.IsBuffering;

        Settings = updated;
        Settings.Save();
        Recorder.ApplySettings(updated);
        Hotkeys.Rebind(updated);
        StartupManager.SetEnabled(updated.StartWithWindows);

        if (needsRestart)
        {
            if (wasBuffering) Recorder.StopBuffer();
            await Recorder.InitializeAsync();
            if (wasBuffering && updated.ReplayBufferEnabled) await Recorder.StartBufferAsync();
        }

        if (!updated.ReplayBufferEnabled && Recorder.IsBuffering) Recorder.StopBuffer();

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- game auto-arm

    private async void OnGameWatcherTick(object? sender, EventArgs e)
    {
        try
        {
            var game = CaptureTargets.DetectForegroundGame();
            _lastDetectedGame = game?.Title;

            if (!Settings.AutoStartBufferOnGame || !Settings.ReplayBufferEnabled) return;

            if (game is not null)
            {
                _gameAbsentTicks = 0;
                if (!Recorder.IsBuffering && !_busy)
                {
                    _bufferAutoStarted = true;
                    if (await Recorder.StartBufferAsync())
                        Notify("Game detected: " + game.Title,
                            $"Replay buffer armed - press {Settings.HotkeySaveReplay} to clip", Theme.Success);
                }
            }
            else if (_bufferAutoStarted && Recorder.IsBuffering && !Recorder.IsRecording)
            {
                // Wait a few polls before disarming: alt-tabbing out should not kill the buffer.
                if (++_gameAbsentTicks >= 5)
                {
                    _gameAbsentTicks = 0;
                    _bufferAutoStarted = false;
                    Recorder.StopBuffer();
                }
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warn("Game watcher tick failed: " + ex.Message);
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open folder: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _gameWatcher.Stop();
        _gameWatcher.Dispose();
        Hotkeys.Dispose();
        Recorder.Dispose();
        _toast.Dispose();
        _marshal.Dispose();
    }
}
