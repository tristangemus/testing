using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipForge.Core;

public enum CaptureMode { Monitor, Window }

public sealed class Settings
{
    // ---- Video ----
    public CaptureMode CaptureMode { get; set; } = CaptureMode.Monitor;
    public int MonitorIndex { get; set; }
    public string WindowTitle { get; set; } = "";
    public int Fps { get; set; } = 60;
    /// <summary>"Native", "2160p", "1440p", "1080p", "720p".</summary>
    public string Resolution { get; set; } = "1080p";
    /// <summary>"auto" or a concrete ffmpeg encoder name such as h264_nvenc.</summary>
    public string Encoder { get; set; } = "auto";
    public int VideoBitrateMbps { get; set; } = 20;
    public bool CaptureCursor { get; set; } = true;
    /// <summary>Use D3D11 Desktop Duplication (ddagrab) when the ffmpeg build supports it.</summary>
    public bool PreferDesktopDuplication { get; set; } = true;

    // ---- Audio ----
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; }
    public string MicDeviceId { get; set; } = "";
    public int SystemVolumePercent { get; set; } = 100;
    public int MicVolumePercent { get; set; } = 100;
    public int AudioBitrateKbps { get; set; } = 160;

    // ---- Replay buffer ----
    public bool ReplayBufferEnabled { get; set; } = true;
    public int ReplayLengthSeconds { get; set; } = 60;
    public bool AutoStartBufferOnGame { get; set; } = true;

    // ---- Output ----
    public string ClipFolder { get; set; } = AppPaths.DefaultClipFolder;

    // ---- Hotkeys ----
    public string HotkeySaveReplay { get; set; } = "Ctrl+Shift+S";
    public string HotkeyToggleRecord { get; set; } = "Ctrl+Shift+R";
    public string HotkeyToggleBuffer { get; set; } = "Ctrl+Shift+B";

    // ---- Application ----
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowOverlayToasts { get; set; } = true;
    public string FfmpegPath { get; set; } = "";
    public bool FirstRunCompleted { get; set; }

    [JsonIgnore]
    public int ReplayBufferDiskBudgetMb =>
        Math.Max(256, (VideoBitrateMbps + 1) * ReplayLengthSeconds / 8 * 2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                if (loaded is not null) return loaded.Normalized();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings, falling back to defaults", ex);
        }
        return new Settings().Normalized();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            // Write-then-replace so a crash mid-write cannot corrupt the file.
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Shallow copy. Callers edit a clone so the controller can diff old against new and decide
    /// whether the capture pipeline has to restart.
    /// </summary>
    public Settings Clone() => (Settings)MemberwiseClone();

    private Settings Normalized()
    {
        Fps = Math.Clamp(Fps, 15, 240);
        VideoBitrateMbps = Math.Clamp(VideoBitrateMbps, 2, 150);
        AudioBitrateKbps = Math.Clamp(AudioBitrateKbps, 64, 320);
        ReplayLengthSeconds = Math.Clamp(ReplayLengthSeconds, 10, 600);
        SystemVolumePercent = Math.Clamp(SystemVolumePercent, 0, 200);
        MicVolumePercent = Math.Clamp(MicVolumePercent, 0, 200);
        if (string.IsNullOrWhiteSpace(ClipFolder)) ClipFolder = AppPaths.DefaultClipFolder;
        return this;
    }

    /// <summary>Target frame size for the encoder, or null to keep the source resolution.</summary>
    public Size? TargetSize() => Resolution switch
    {
        "2160p" => new Size(3840, 2160),
        "1440p" => new Size(2560, 1440),
        "1080p" => new Size(1920, 1080),
        "720p" => new Size(1280, 720),
        _ => null
    };
}
