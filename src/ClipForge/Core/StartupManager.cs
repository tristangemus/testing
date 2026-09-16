using Microsoft.Win32;

namespace ClipForge.Core;

/// <summary>Toggles the per-user "run at sign-in" registry entry.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipForge";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains("ClipForge", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read startup entry: " + ex.Message);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
                key.SetValue(ValueName, $"\"{AppPaths.ExecutablePath}\" --minimized");
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName);
        }
        catch (Exception ex)
        {
            Log.Error("Could not update startup entry", ex);
        }
    }
}
