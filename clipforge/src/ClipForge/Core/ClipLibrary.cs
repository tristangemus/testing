using System.Security.Cryptography;
using System.Text;

namespace ClipForge.Core;

public sealed class ClipInfo
{
    public required string Path { get; init; }
    public required string Name { get; set; }
    public DateTime CreatedUtc { get; set; }
    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }
    public string? ThumbnailPath { get; set; }

    public string DurationText => DurationSeconds >= 3600
        ? TimeSpan.FromSeconds(DurationSeconds).ToString(@"h\:mm\:ss")
        : TimeSpan.FromSeconds(DurationSeconds).ToString(@"m\:ss");

    public string SizeText => SizeBytes >= 1L << 30
        ? $"{SizeBytes / (double)(1L << 30):0.0} GB"
        : $"{SizeBytes / (double)(1L << 20):0} MB";
}

/// <summary>Enumerates saved clips and manages their thumbnail cache.</summary>
public static class ClipLibrary
{
    private static readonly string[] Extensions = { "*.mp4", "*.mkv", "*.mov" };

    public static List<ClipInfo> Scan(string folder)
    {
        var clips = new List<ClipInfo>();
        try
        {
            if (!Directory.Exists(folder)) return clips;
            foreach (var pattern in Extensions)
            {
                foreach (var path in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(path);
                    clips.Add(new ClipInfo
                    {
                        Path = path,
                        Name = System.IO.Path.GetFileNameWithoutExtension(path),
                        CreatedUtc = info.LastWriteTimeUtc,
                        SizeBytes = info.Length,
                        ThumbnailPath = ExistingThumbnail(path)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Clip scan failed: " + ex.Message);
        }
        return clips.OrderByDescending(c => c.CreatedUtc).ToList();
    }

    private static string ThumbnailFor(string clipPath)
    {
        var hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(clipPath.ToLowerInvariant())))[..16];
        return System.IO.Path.Combine(AppPaths.ThumbnailDir, hash + ".jpg");
    }

    private static string? ExistingThumbnail(string clipPath)
    {
        var path = ThumbnailFor(clipPath);
        return File.Exists(path) ? path : null;
    }

    public static async Task<string?> EnsureThumbnailAsync(string ffmpeg, ClipInfo clip)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ThumbnailDir);
            var target = ThumbnailFor(clip.Path);
            if (File.Exists(target)) return target;

            // Seek a little way in: the first frame of a clip is often a black fade.
            var seek = clip.DurationSeconds > 4 ? clip.DurationSeconds / 4 : 0.5;
            var args = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-ss", seek.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                "-i", clip.Path,
                "-frames:v", "1",
                "-vf", "scale=480:-2",
                "-q:v", "4",
                target
            };

            using var process = FFmpegManager.StartHidden(ffmpeg, args);
            _ = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(cts.Token);
            return File.Exists(target) ? target : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Thumbnail generation failed for {clip.Path}: {ex.Message}");
            return null;
        }
    }

    public static bool Delete(ClipInfo clip)
    {
        try
        {
            if (File.Exists(clip.Path)) File.Delete(clip.Path);
            var thumb = ThumbnailFor(clip.Path);
            if (File.Exists(thumb)) File.Delete(thumb);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete {clip.Path}: {ex.Message}");
            return false;
        }
    }

    public static bool Rename(ClipInfo clip, string newName, out string error)
    {
        error = "";
        try
        {
            var safe = RecorderEngine.SanitizeFileName(newName);
            if (string.IsNullOrWhiteSpace(safe))
            {
                error = "Please enter a name.";
                return false;
            }

            var directory = System.IO.Path.GetDirectoryName(clip.Path)!;
            var destination = System.IO.Path.Combine(directory, safe + System.IO.Path.GetExtension(clip.Path));
            if (string.Equals(destination, clip.Path, StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(destination))
            {
                error = "A clip with that name already exists.";
                return false;
            }

            File.Move(clip.Path, destination);

            // Keep the cached thumbnail by moving it to the new path's hash.
            var oldThumb = ThumbnailFor(clip.Path);
            var newThumb = ThumbnailFor(destination);
            if (File.Exists(oldThumb) && !File.Exists(newThumb)) File.Move(oldThumb, newThumb);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
