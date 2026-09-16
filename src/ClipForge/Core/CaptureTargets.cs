using System.Diagnostics;

namespace ClipForge.Core;

public sealed record MonitorInfo(int Index, string DeviceName, Rectangle Bounds, bool IsPrimary)
{
    public override string ToString() =>
        $"Display {Index + 1} - {Bounds.Width}x{Bounds.Height}{(IsPrimary ? " (primary)" : "")}";
}

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, Size Size)
{
    public override string ToString() => $"{Title}  [{ProcessName}]";
}

/// <summary>Enumerates capture sources and guesses when the user is in a game.</summary>
public static class CaptureTargets
{
    /// <summary>Windows that are never interesting as a capture target.</summary>
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "shellexperiencehost", "startmenuexperiencehost",
        "textinputhost", "applicationframehost", "systemsettings", "lockapp",
        "clipforge", "dwm", "sihost", "widgets", "widgetboard"
    };

    /// <summary>Processes that are commonly fullscreen but are not games.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "zen",
        "code", "devenv", "rider64", "idea64", "pycharm64", "sublime_text", "notepad++",
        "vlc", "mpc-hc64", "mpv", "potplayermini64", "spotify", "discord", "slack", "teams",
        "obs64", "obs32", "streamlabs obs", "xsplit.core", "clipforge",
        "explorer", "powerpnt", "winword", "excel", "acrobat", "photoshop", "illustrator",
        "windowsterminal", "wt", "conhost", "cmd", "powershell", "pwsh"
    };

    /// <summary>Launchers whose presence strongly suggests the foreground app is a game.</summary>
    private static readonly HashSet<string> GameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "steamapps", "\\steam\\", "epic games", "riot games", "battle.net", "gog galaxy",
        "ubisoft", "ea games", "origin games", "xboxgames", "windowsapps\\", "rockstar games",
        "minecraft", "roblox"
    };

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
            monitors.Add(new MonitorInfo(i, screens[i].DeviceName, screens[i].Bounds, screens[i].Primary));
        return monitors;
    }

    public static List<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsAltTabWindow(hWnd)) return true;

            var title = NativeMethods.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            var processName = GetProcessName(hWnd);
            if (ShellProcesses.Contains(processName)) return true;

            if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return true;
            if (rect.Width < 160 || rect.Height < 120) return true;

            windows.Add(new WindowInfo(hWnd, title, processName, new Size(rect.Width, rect.Height)));
            return true;
        }, IntPtr.Zero);

        return windows
            .GroupBy(w => w.Title)
            .Select(g => g.First())
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string GetProcessName(IntPtr hWnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // Process exited or access denied.
            return "";
        }
    }

    /// <summary>
    /// Best-effort "is the user in a game right now" check, used to auto-arm the replay buffer.
    /// Requires the foreground window to cover a whole display and not be a known non-game app.
    /// </summary>
    public static WindowInfo? DetectForegroundGame()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;
        if (!NativeMethods.IsAltTabWindow(hWnd)) return null;
        if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return null;

        var processName = GetProcessName(hWnd);
        if (string.IsNullOrEmpty(processName)) return null;
        if (ShellProcesses.Contains(processName) || NotGames.Contains(processName)) return null;

        var bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
        var screen = Screen.FromHandle(hWnd);
        // Allow a couple of pixels of slack for borderless windows.
        var coversScreen = bounds.Width >= screen.Bounds.Width - 2 &&
                           bounds.Height >= screen.Bounds.Height - 2;

        var executablePath = GetProcessPath(hWnd);
        var launcherHint = executablePath is not null &&
                           GameHints.Any(h => executablePath.Contains(h, StringComparison.OrdinalIgnoreCase));

        if (!coversScreen && !launcherHint) return null;

        var title = NativeMethods.GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title)) title = processName;
        return new WindowInfo(hWnd, title, processName, new Size(rect.Width, rect.Height));
    }

    private static string? GetProcessPath(IntPtr hWnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return null;
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            // MainModule throws for elevated or 32/64-bit mismatched processes.
            return null;
        }
    }
}
