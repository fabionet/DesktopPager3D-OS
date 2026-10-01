using Microsoft.Win32;

namespace DesktopPager.VR;

/// <summary>
/// Rilevamento leggero di un runtime OpenXR, senza inizializzare StereoKit.
/// Serve alla tray per offrire la voce "Apri in VR" solo quando ha senso: su un
/// PC senza visore (es. la Intel HD 3000 di casa) la voce resta nascosta e il
/// modulo VR non viene mai lanciato. Il runtime attivo è registrato da Windows
/// in HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime (Quest via Link, SteamVR,
/// WMR scrivono qui).
/// </summary>
public static class OpenXrRuntime
{
    public static bool IsAvailable() => ActiveRuntimePath() is { Length: > 0 };

    /// <summary>Percorso del manifest del runtime OpenXR attivo, o null.</summary>
    public static string? ActiveRuntimePath()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
            if (key?.GetValue("ActiveRuntime") is string path && File.Exists(path))
            {
                return path;
            }
        }
        catch
        {
            // registro non leggibile: trattiamo come "nessun runtime"
        }

        return null;
    }
}
