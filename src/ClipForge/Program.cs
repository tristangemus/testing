using ClipForge.Core;
using ClipForge.UI;

namespace ClipForge;

internal static class Program
{
    /// <summary>Named so a second launch can find the first instance instead of double-recording.</summary>
    private const string InstanceMutexName = @"Local\ClipForge.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "ClipForge is already running. Look for its icon in the system tray.",
                "ClipForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);

        AppPaths.EnsureAll();
        AppPaths.PruneTemp();
        Log.Info($"ClipForge {AppPaths.Version} starting");

        var settings = Settings.Load();
        var startMinimized = settings.StartMinimized ||
                             args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // Keep the registry entry in step with the saved preference.
        if (StartupManager.IsEnabled() != settings.StartWithWindows)
            StartupManager.SetEnabled(settings.StartWithWindows);

        using var controller = new AppController(settings);
        var window = new MainForm(controller, startMinimized);

        // Discover ffmpeg once the window exists; Shown guarantees a realised handle.
        var started = false;
        window.Shown += async (_, _) =>
        {
            if (started) return;
            started = true;

            await controller.InitializeAsync();

            if (FFmpegManager.Resolve(controller.Settings) is null)
            {
                using var setup = new FirstRunForm(controller);
                setup.ShowDialog(window);
                await controller.InitializeAsync();
            }

            if (!controller.Settings.FirstRunCompleted)
            {
                controller.Settings.FirstRunCompleted = true;
                controller.Settings.Save();
            }
        };

        Application.Run(window);
        Log.Info("ClipForge exiting");
    }

    private static void Crash(Exception? exception)
    {
        Log.Error("Unhandled exception", exception);
        MessageBox.Show(
            "ClipForge hit an unexpected error:\n\n" + (exception?.Message ?? "unknown") +
            "\n\nDetails were written to:\n" + AppPaths.LogFile,
            "ClipForge", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
