using System.Text;

namespace ClipForge.Core;

/// <summary>Minimal rolling file logger. Never throws: logging must not break capture.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var file = AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    var old = file + ".1";
                    File.Delete(old);
                    File.Move(file, old);
                }
                File.AppendAllText(file,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging is best-effort by design.
        }
    }
}
