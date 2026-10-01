using System.IO;
using Microsoft.Win32;

namespace DesktopPager.Tray;

/// <summary>
/// Interruttore globale della modalità VR della Vista 3D Game.
/// <para>
/// Di default è <b>spenta</b>: la si accende dalla voce di menu della tray.
/// Quando è accesa, aprire la Vista 3D Game lancia il modulo VR separato
/// (DesktopPager3D-VR, StereoKit/OpenXR) invece della finestra 3D su schermo.
/// </para>
/// </summary>
internal static class VrMode
{
    /// <summary>Acceso solo su richiesta dell'utente. Default: false.</summary>
    public static bool Enabled { get; set; }

    private const string VrExeName = "DesktopPager3D-VR.exe";

    /// <summary>
    /// Cerca l'eseguibile del modulo VR: accanto all'app (build spedita) o, in
    /// sviluppo, dentro src/DesktopPager.VR. Null se non installato.
    /// </summary>
    public static string? FindExecutable()
    {
        var baseDir = AppContext.BaseDirectory;

        foreach (var rel in new[] { VrExeName, Path.Combine("VR", VrExeName) })
        {
            var p = Path.Combine(baseDir, rel);
            if (File.Exists(p))
            {
                return p;
            }
        }

        // in sviluppo l'app gira da src/DesktopPager.Tray/bin/...: risali fino a
        // trovare la cartella del modulo VR e prendi l'exe compilato più recente
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var vrProj = Path.Combine(dir.FullName, "src", "DesktopPager.VR");
            if (Directory.Exists(vrProj))
            {
                return Directory
                    .EnumerateFiles(vrProj, VrExeName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// C'è un runtime OpenXR attivo (visore collegato: Quest via Link, SteamVR,
    /// WMR)? Windows lo registra in HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime.
    /// </summary>
    public static bool OpenXrAvailable()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
            return key?.GetValue("ActiveRuntime") is string path && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
