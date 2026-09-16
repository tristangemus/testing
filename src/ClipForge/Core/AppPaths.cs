using System.Reflection;

namespace ClipForge.Core;

/// <summary>Well-known locations used by the app. Everything mutable lives under LOCALAPPDATA.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipForge");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "clipforge.log");
    public static string ToolsDir => Path.Combine(DataDir, "tools");
    public static string BufferDir => Path.Combine(DataDir, "buffer");
    public static string ThumbnailDir => Path.Combine(DataDir, "thumbnails");
    public static string TempDir => Path.Combine(DataDir, "temp");

    public static string DefaultClipFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClipForge");

    /// <summary>Path of the running executable, valid for both single-file and folder deployments.</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClipForge.exe");

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public static void EnsureAll()
    {
        foreach (var dir in new[] { DataDir, ToolsDir, BufferDir, ThumbnailDir, TempDir })
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Drops scratch files a previous crash left behind. Anything from the last day is kept so a
    /// recoverable recording is not thrown away.
    /// </summary>
    public static void PruneTemp()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var path in Directory.GetFiles(TempDir))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            foreach (var path in Directory.GetDirectories(TempDir))
            {
                if (Directory.GetLastWriteTimeUtc(path) < cutoff) Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Temp prune failed: " + ex.Message);
        }
    }
}
