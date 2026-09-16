using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ClipForge.Core;

public sealed record EncoderOption(string Id, string DisplayName, string Vendor)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Locates (or downloads) the ffmpeg/ffprobe binaries the capture engine drives, and probes
/// which hardware encoders actually work on this machine.
/// </summary>
public static class FFmpegManager
{
    // GPL builds: needed for ddagrab (D3D11 desktop duplication) plus NVENC/AMF/QSV.
    private static readonly string[] DownloadMirrors =
    {
        "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    };

    public static string BundledFfmpeg => Path.Combine(AppPaths.ToolsDir, "ffmpeg.exe");
    public static string BundledFfprobe => Path.Combine(AppPaths.ToolsDir, "ffprobe.exe");

    /// <summary>Resolves the ffmpeg executable: explicit setting, then app tools dir, then PATH.</summary>
    public static string? Resolve(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath) && File.Exists(settings.FfmpegPath))
            return settings.FfmpegPath;
        if (File.Exists(BundledFfmpeg)) return BundledFfmpeg;

        var next = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(next)) return next;

        return FindOnPath("ffmpeg.exe");
    }

    public static string? ResolveProbe(Settings settings)
    {
        var ffmpeg = Resolve(settings);
        if (ffmpeg is not null)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? ".", "ffprobe.exe");
            if (File.Exists(sibling)) return sibling;
        }
        if (File.Exists(BundledFfprobe)) return BundledFfprobe;
        return FindOnPath("ffprobe.exe");
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }
        return null;
    }

    public static async Task<bool> DownloadAsync(
        IProgress<(string stage, int percent)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.ToolsDir);
        var zipPath = Path.Combine(AppPaths.TempDir, "ffmpeg-download.zip");
        Directory.CreateDirectory(AppPaths.TempDir);

        foreach (var url in DownloadMirrors)
        {
            try
            {
                progress.Report(("Contacting " + new Uri(url).Host, 0));
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipForge/" + AppPaths.Version);

                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? 0L;
                    await using var input = await response.Content.ReadAsStreamAsync(ct);
                    await using var output = File.Create(zipPath);

                    var buffer = new byte[128 * 1024];
                    long read = 0;
                    int n;
                    while ((n = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, n), ct);
                        read += n;
                        var pct = total > 0 ? (int)(read * 90 / total) : 45;
                        progress.Report(($"Downloading ffmpeg ({read / 1048576} MB)", pct));
                    }
                }

                progress.Report(("Extracting", 92));
                ExtractBinaries(zipPath);
                progress.Report(("Done", 100));
                return File.Exists(BundledFfmpeg);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"ffmpeg download failed from {url}: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* temp file */ }
            }
        }
        return false;
    }

    private static void ExtractBinaries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var wanted in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), wanted, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            var dest = Path.Combine(AppPaths.ToolsDir, wanted);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    public static async Task<string> GetVersionAsync(string ffmpeg)
    {
        var output = await RunAsync(ffmpeg, "-hide_banner -version", TimeSpan.FromSeconds(15));
        var first = output.Split('\n').FirstOrDefault() ?? "";
        var match = Regex.Match(first, @"ffmpeg version (\S+)");
        return match.Success ? match.Groups[1].Value : first.Trim();
    }

    public static async Task<bool> SupportsDdagrabAsync(string ffmpeg)
    {
        var output = await RunAsync(ffmpeg, "-hide_banner -filters", TimeSpan.FromSeconds(20));
        return output.Contains("ddagrab", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Candidate encoders, best first. Each is verified by actually encoding a few frames, because a
    /// build advertising h264_nvenc still fails on a machine without the matching GPU/driver.
    /// </summary>
    public static async Task<List<EncoderOption>> DetectEncodersAsync(string ffmpeg)
    {
        var listed = await RunAsync(ffmpeg, "-hide_banner -encoders", TimeSpan.FromSeconds(20));
        var candidates = new List<EncoderOption>
        {
            new("h264_nvenc", "H.264 - NVIDIA NVENC (GPU)", "NVIDIA"),
            new("hevc_nvenc", "HEVC - NVIDIA NVENC (GPU)", "NVIDIA"),
            new("h264_amf",   "H.264 - AMD AMF (GPU)",      "AMD"),
            new("hevc_amf",   "HEVC - AMD AMF (GPU)",       "AMD"),
            new("h264_qsv",   "H.264 - Intel Quick Sync",   "Intel"),
            new("hevc_qsv",   "HEVC - Intel Quick Sync",    "Intel"),
            new("libx264",    "H.264 - x264 (CPU)",         "CPU")
        };

        var working = new List<EncoderOption>();
        foreach (var option in candidates)
        {
            if (!listed.Contains(option.Id, StringComparison.Ordinal)) continue;
            if (await EncoderWorksAsync(ffmpeg, option.Id)) working.Add(option);
        }

        if (working.Count == 0)
            working.Add(new EncoderOption("libx264", "H.264 - x264 (CPU)", "CPU"));
        return working;
    }

    private static async Task<bool> EncoderWorksAsync(string ffmpeg, string encoder)
    {
        var args = $"-hide_banner -loglevel error -f lavfi -i testsrc=size=320x240:rate=15:duration=0.4 " +
                   $"-c:v {encoder} -pix_fmt yuv420p -f null -";
        try
        {
            using var process = StartHidden(ffmpeg, args);
            var completed = await WaitAsync(process, TimeSpan.FromSeconds(25));
            var ok = completed && process.ExitCode == 0;
            Log.Info($"Encoder probe {encoder}: {(ok ? "available" : "unavailable")}");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn($"Encoder probe {encoder} threw: {ex.Message}");
            return false;
        }
    }

    public static async Task<double> GetDurationSecondsAsync(string ffprobe, string mediaFile)
    {
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 " +
                   $"\"{mediaFile}\"";
        var output = await RunAsync(ffprobe, args, TimeSpan.FromSeconds(30));
        return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    }

    /// <summary>Starts a process from a pre-split argument list, so titles and paths need no quoting.</summary>
    public static Process StartHidden(string exe, IEnumerable<string> arguments, bool redirectStdIn = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdIn,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    public static Process StartHidden(string exe, string arguments, bool redirectStdIn = false)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdIn,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    public static async Task<string> RunAsync(string exe, string arguments, TimeSpan timeout)
    {
        using var process = StartHidden(exe, arguments);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!await WaitAsync(process, timeout)) return "";
        return await stdout + await stderr;
    }

    private static async Task<bool> WaitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return false;
        }
    }
}
