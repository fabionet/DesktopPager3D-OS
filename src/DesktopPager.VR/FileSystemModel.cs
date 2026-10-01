namespace DesktopPager.VR;

/// <summary>Una voce della stanza: disco/cartella (contenitore) o file.</summary>
internal sealed record Entry(string Name, string FullPath, bool IsContainer);

/// <summary>
/// Lettura del filesystem, gemella (semplificata) di quella della Vista 3D
/// Game su WPF: null = "Questo PC" (dischi + cartelle note), altrimenti il
/// contenuto della cartella. Cartelle prima, poi file, con un tetto massimo.
/// </summary>
internal static class FileSystemModel
{
    public const int MaxEntries = 48;

    public static List<Entry> Read(string? path)
    {
        var list = new List<Entry>();
        try
        {
            if (path is null)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady)
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disco" : d.VolumeLabel;
                    list.Add(new Entry($"{label} ({d.Name.TrimEnd('\\')})", d.RootDirectory.FullName, true));
                }

                foreach (var sf in new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyVideos
                })
                {
                    var p = Environment.GetFolderPath(sf);
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                    {
                        list.Add(new Entry(Path.GetFileName(p.TrimEnd('\\')), p, true));
                    }
                }
            }
            else
            {
                foreach (var dir in Directory.EnumerateDirectories(path).Take(MaxEntries))
                {
                    list.Add(new Entry(Path.GetFileName(dir), dir, true));
                }

                foreach (var file in Directory.EnumerateFiles(path).Take(MaxEntries - list.Count))
                {
                    list.Add(new Entry(Path.GetFileName(file), file, false));
                }
            }
        }
        catch
        {
            // percorso non accessibile: stanza vuota
        }

        return list.Take(MaxEntries).ToList();
    }

    /// <summary>Cartella genitore, o null se già alla radice/"Questo PC".</summary>
    public static string? Parent(string? current)
    {
        if (current is null)
        {
            return null;
        }

        return Directory.GetParent(current.TrimEnd('\\'))?.FullName;
    }
}
