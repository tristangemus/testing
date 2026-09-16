namespace ClipForge.Core;

public enum HotkeyAction { SaveReplay, ToggleRecording, ToggleBuffer }

/// <summary>
/// Registers system-wide hotkeys against a hidden message window, so they fire while a
/// fullscreen game has focus.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private int _nextId = 0xC10F;

    public event EventHandler<HotkeyAction>? Triggered;
    /// <summary>Hotkeys Windows refused, usually because another app already owns them.</summary>
    public List<string> Conflicts { get; } = new();

    public HotkeyManager() => CreateHandle(new CreateParams
    {
        Caption = "ClipForgeHotkeys",
        X = 0, Y = 0, Height = 0, Width = 0,
        Style = 0,
        Parent = new IntPtr(-3) // HWND_MESSAGE
    });

    public void Rebind(Settings settings)
    {
        UnregisterAll();
        Conflicts.Clear();
        Register(settings.HotkeySaveReplay, HotkeyAction.SaveReplay);
        Register(settings.HotkeyToggleRecord, HotkeyAction.ToggleRecording);
        Register(settings.HotkeyToggleBuffer, HotkeyAction.ToggleBuffer);
    }

    private void Register(string gesture, HotkeyAction action)
    {
        if (!TryParse(gesture, out var modifiers, out var key)) return;

        var id = _nextId++;
        if (NativeMethods.RegisterHotKey(Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key))
        {
            _registered[id] = action;
        }
        else
        {
            Conflicts.Add(gesture);
            Log.Warn($"Hotkey {gesture} is already in use by another application");
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY &&
            _registered.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            Triggered?.Invoke(this, action);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Parses gestures such as "Ctrl+Shift+S" or "Alt+F9".</summary>
    public static bool TryParse(string gesture, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        foreach (var rawPart in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                case "win" or "windows": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed)) return false;
                    key = parsed;
                    break;
            }
        }
        return key != Keys.None;
    }

    public static string Describe(Keys keyData)
    {
        var parts = new List<string>();
        if (keyData.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keyData.HasFlag(Keys.Shift)) parts.Add("Shift");
        if (keyData.HasFlag(Keys.Alt)) parts.Add("Alt");

        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.None) return "";

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
