using StereoKit;

namespace DesktopPager.VR;

/// <summary>
/// Punto d'ingresso del modulo VR opzionale. StereoKit apre la sessione
/// OpenXR: se c'è un visore (es. Meta Quest via Link) la scena va sul visore,
/// altrimenti parte il Simulatore a schermo, utile per lo sviluppo. Questo
/// eseguibile è separato dalla tray: sul PC senza OpenXR non viene mai lanciato.
/// </summary>
internal static class Program
{
    private static void Main()
    {
        var settings = new SKSettings
        {
            appName = "DesktopPager3D-OS VR",
            assetsFolder = "Assets"
        };

        // Se manca del tutto un runtime OpenXR, Initialize fallisce: usciamo in
        // silenzio (la tray non dovrebbe nemmeno aver offerto la voce VR).
        if (!SK.Initialize(settings))
        {
            return;
        }

        var shell = new VrShell();
        SK.Run(shell.Step);
    }
}
