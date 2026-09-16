using System.Reflection;

namespace ClipForge.UI;

/// <summary>Loads the embedded application icon once and shares it.</summary>
public static class AppIcon
{
    private static Icon? _cached;

    public static Icon Value
    {
        get
        {
            if (_cached is not null) return _cached;
            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("ClipForge.app.ico");
                _cached = stream is not null ? new Icon(stream) : SystemIcons.Application;
            }
            catch (Exception)
            {
                _cached = SystemIcons.Application;
            }
            return _cached;
        }
    }
}
