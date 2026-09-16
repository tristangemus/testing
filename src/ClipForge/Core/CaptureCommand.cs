using System.Globalization;

namespace ClipForge.Core;

/// <summary>Builds ffmpeg argument lists for the two capture backends.</summary>
public static class CaptureCommand
{
    /// <summary>
    /// D3D11 Desktop Duplication. Far cheaper than GDI on fullscreen games and the only backend
    /// that reliably reaches high frame rates, but it can only capture a whole display.
    /// </summary>
    public const string BackendDesktopDuplication = "ddagrab";

    /// <summary>GDI capture. Slower, but works on any ffmpeg build and can target a single window.</summary>
    public const string BackendGdi = "gdigrab";

    public static string ChooseBackend(Settings settings, bool ddagrabAvailable) =>
        settings.CaptureMode == CaptureMode.Monitor && settings.PreferDesktopDuplication && ddagrabAvailable
            ? BackendDesktopDuplication
            : BackendGdi;

    public static List<string> BuildVideoInput(Settings settings, string backend)
    {
        var args = new List<string>();
        var fps = settings.Fps.ToString(CultureInfo.InvariantCulture);
        var drawMouse = settings.CaptureCursor ? "1" : "0";

        if (backend == BackendDesktopDuplication)
        {
            args.Add("-f"); args.Add("lavfi");
            args.Add("-i");
            args.Add($"ddagrab=output_idx={settings.MonitorIndex}:framerate={fps}:draw_mouse={drawMouse}");
            return args;
        }

        args.Add("-f"); args.Add("gdigrab");
        args.Add("-framerate"); args.Add(fps);
        args.Add("-draw_mouse"); args.Add(drawMouse);
        args.Add("-thread_queue_size"); args.Add("1024");

        if (settings.CaptureMode == CaptureMode.Window && !string.IsNullOrWhiteSpace(settings.WindowTitle))
        {
            args.Add("-i");
            args.Add("title=" + settings.WindowTitle);
        }
        else
        {
            var monitors = CaptureTargets.GetMonitors();
            var monitor = monitors.FirstOrDefault(m => m.Index == settings.MonitorIndex) ?? monitors.FirstOrDefault();
            if (monitor is not null)
            {
                args.Add("-offset_x"); args.Add(monitor.Bounds.X.ToString(CultureInfo.InvariantCulture));
                args.Add("-offset_y"); args.Add(monitor.Bounds.Y.ToString(CultureInfo.InvariantCulture));
                args.Add("-video_size");
                args.Add($"{monitor.Bounds.Width}x{monitor.Bounds.Height}");
            }
            args.Add("-i"); args.Add("desktop");
        }
        return args;
    }

    public static List<string> BuildAudioInput()
    {
        return new List<string>
        {
            "-f", "s16le",
            "-ar", AudioEngine.SampleRate.ToString(CultureInfo.InvariantCulture),
            "-ac", AudioEngine.Channels.ToString(CultureInfo.InvariantCulture),
            "-thread_queue_size", "1024",
            "-i", "pipe:0"
        };
    }

    public static string BuildVideoFilter(Settings settings, string backend)
    {
        var stages = new List<string>();

        // ddagrab hands back GPU surfaces; pull them into system memory before scaling.
        if (backend == BackendDesktopDuplication)
        {
            stages.Add("hwdownload");
            stages.Add("format=bgra");
        }

        var target = settings.TargetSize();
        if (target is { } size)
        {
            // Fit inside the target and letterbox, so an ultrawide source is never distorted.
            stages.Add($"scale={size.Width}:{size.Height}:force_original_aspect_ratio=decrease:flags=bicubic");
            stages.Add($"pad={size.Width}:{size.Height}:(ow-iw)/2:(oh-ih)/2:color=black");
        }
        else
        {
            // H.264 requires even dimensions; odd-sized windows otherwise fail to encode.
            stages.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        }

        stages.Add("format=yuv420p");
        return string.Join(",", stages);
    }

    public static List<string> BuildEncoderArgs(Settings settings, string encoderId)
    {
        var bitrate = settings.VideoBitrateMbps + "M";
        var bufsize = settings.VideoBitrateMbps * 2 + "M";
        var gop = (settings.Fps * 2).ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "-c:v", encoderId };

        if (encoderId.Contains("nvenc", StringComparison.Ordinal))
        {
            args.AddRange(new[]
            {
                "-preset", "p5", "-tune", "hq", "-rc", "cbr",
                "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", bufsize,
                "-g", gop, "-bf", "2", "-rc-lookahead", "20", "-spatial-aq", "1"
            });
        }
        else if (encoderId.Contains("amf", StringComparison.Ordinal))
        {
            args.AddRange(new[]
            {
                "-quality", "balanced", "-rc", "cbr",
                "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", bufsize, "-g", gop
            });
        }
        else if (encoderId.Contains("qsv", StringComparison.Ordinal))
        {
            args.AddRange(new[]
            {
                "-preset", "medium",
                "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", bufsize, "-g", gop
            });
        }
        else
        {
            // x264: veryfast keeps a CPU encode viable alongside a running game.
            args.AddRange(new[]
            {
                "-preset", "veryfast", "-profile:v", "high",
                "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", bufsize,
                "-g", gop, "-x264-params", "nal-hrd=cbr"
            });
        }
        return args;
    }

    public static List<string> BuildAudioEncoderArgs(Settings settings) => new()
    {
        "-c:a", "aac",
        "-b:a", settings.AudioBitrateKbps + "k",
        "-ar", AudioEngine.SampleRate.ToString(CultureInfo.InvariantCulture),
        "-ac", AudioEngine.Channels.ToString(CultureInfo.InvariantCulture)
    };
}
