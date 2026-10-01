using System.Diagnostics;
using StereoKit;

namespace DesktopPager.VR;

/// <summary>
/// Versione VR della "Vista 3D Game": la stanza (pavimento, pareti, porte delle
/// cartelle, pannelli dei file) è ricostruita nello scene-graph di StereoKit.
/// La testa è tracciata dal visore; ci si sposta con lo stick sinistro (avanti/
/// laterale rispetto a dove si guarda) e si gira a scatti con lo stick destro.
/// Si punta un banco col raggio del controller destro e lo si apre col grilletto;
/// pulsante X/A torna alla cartella superiore, Esc (o simulatore) esce.
/// </summary>
internal sealed class VrShell
{
    // Dimensioni della stanza, in metri (misura reale nel visore).
    private const float WallX = 2.8f;      // pareti laterali / porte delle cartelle
    private const float FileLaneX = 1.5f;  // corsia dei file
    private const float SpacingZ = 2.4f;   // passo delle schiere lungo il corridoio
    private const float EyeStartZ = 3.2f;  // da dove si parte, guardando dentro (-z)
    private const float MoveSpeed = 2.2f;  // m/s con lo stick
    private const float SnapDeg = 30f;     // gradi per scatto di rotazione

    private sealed class Booth
    {
        public required Entry Entry;
        public Vec3 Pos;
        public bool IsFolder;
    }

    private readonly Mesh _cube = Mesh.GenerateCube(Vec3.One);
    private readonly Material _mat = Material.Default;

    private readonly List<Booth> _booths = new();
    private string? _current;   // null = "Questo PC"

    private Vec3 _playerPos = new(0, 0, EyeStartZ);
    private float _yawDeg;
    private bool _snapArmed = true;
    private bool _triggerPrev;

    private float _roomHalfW;
    private float _roomBackZ;
    private float _roomFrontZ;

    public VrShell()
    {
        Navigate(null);
    }

    // --- ciclo per fotogramma ---------------------------------------------

    public void Step()
    {
        HandleLocomotion();
        Renderer.CameraRoot = Matrix.TR(_playerPos, Quat.FromAngles(0, _yawDeg, 0));

        DrawRoom();
        var hover = PickBooth(out _);
        DrawBooths(hover);
        HandleActions(hover);

        if (Input.Key(Key.Esc).IsJustActive())
        {
            SK.Quit();
        }
    }

    private void HandleLocomotion()
    {
        var dt = Time.Stepf;

        // stick sinistro: avanti/indietro e strafe rispetto a dove guarda la testa
        var move = Input.Controller(Handed.Left).stick;
        var fwd = Input.Head.Forward;
        fwd.y = 0;
        if (fwd.Length > 0.001f)
        {
            fwd = fwd.Normalized;
        }

        var right = new Vec3(fwd.z, 0, -fwd.x);
        _playerPos += (fwd * move.y + right * move.x) * (MoveSpeed * dt);

        // stick destro: rotazione a scatti (comfort VR)
        var turn = Input.Controller(Handed.Right).stick.x;
        if (_snapArmed && MathF.Abs(turn) > 0.7f)
        {
            _yawDeg -= MathF.Sign(turn) * SnapDeg;
            _snapArmed = false;
        }
        else if (MathF.Abs(turn) < 0.3f)
        {
            _snapArmed = true;
        }
    }

    /// <summary>Banco puntato dal raggio del controller destro, o -1.</summary>
    private int PickBooth(out Ray ray)
    {
        var aim = Input.Controller(Handed.Right).aim;
        ray = new Ray(aim.position, aim.Forward);

        var best = 0.9f; // distanza perpendicolare massima, in metri
        var hit = -1;
        for (var i = 0; i < _booths.Count; i++)
        {
            var v = _booths[i].Pos - ray.position;
            var t = Vec3.Dot(v, ray.direction);
            if (t < 0.2f || t > 30f)
            {
                continue;
            }

            var perp = (v - ray.direction * t).Length;
            if (perp < best)
            {
                best = perp;
                hit = i;
            }
        }

        return hit;
    }

    private void HandleActions(int hover)
    {
        // grilletto destro: fronte di salita -> apri il banco puntato
        var trigger = Input.Controller(Handed.Right).trigger > 0.6f;
        if (trigger && !_triggerPrev && hover >= 0)
        {
            Activate(_booths[hover]);
        }

        _triggerPrev = trigger;

        // X / A: torna alla cartella superiore
        if (Input.Controller(Handed.Left).x1.IsJustActive() ||
            Input.Controller(Handed.Right).x1.IsJustActive())
        {
            Navigate(FileSystemModel.Parent(_current));
        }
    }

    private void Activate(Booth booth)
    {
        if (booth.Entry.IsContainer)
        {
            Navigate(booth.Entry.FullPath);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = booth.Entry.FullPath, UseShellExecute = true });
            }
            catch
            {
                // apertura fallita: ignora
            }
        }
    }

    // --- costruzione stanza ------------------------------------------------

    private void Navigate(string? path)
    {
        _current = path;
        _booths.Clear();

        var entries = FileSystemModel.Read(path);
        var folders = new List<Entry>();
        var files = new List<Entry>();
        foreach (var e in entries)
        {
            (e.IsContainer ? folders : files).Add(e);
        }

        for (var j = 0; j < folders.Count; j++)
        {
            var side = j % 2 == 0 ? -1 : 1;
            var z = -1.6f - j / 2 * SpacingZ;
            _booths.Add(new Booth { Entry = folders[j], Pos = new Vec3(side * WallX, 1.2f, z), IsFolder = true });
        }

        for (var j = 0; j < files.Count; j++)
        {
            var side = j % 2 == 0 ? -1 : 1;
            var z = -1.2f - j / 2 * SpacingZ;
            _booths.Add(new Booth { Entry = files[j], Pos = new Vec3(side * FileLaneX, 1.3f, z), IsFolder = false });
        }

        var rows = Math.Max(1, Math.Max((folders.Count + 1) / 2, (files.Count + 1) / 2));
        _roomHalfW = WallX + 0.4f;
        _roomBackZ = -(1.6f + rows * SpacingZ) - 1.0f;
        _roomFrontZ = EyeStartZ + 1.5f;

        _playerPos = new Vec3(0, 0, EyeStartZ);
        _yawDeg = 0;
    }

    // --- disegno -----------------------------------------------------------

    private void DrawRoom()
    {
        var depth = _roomFrontZ - _roomBackZ;
        var midZ = (_roomFrontZ + _roomBackZ) / 2;
        var floor = new Color(0.10f, 0.11f, 0.16f);
        var wall = new Color(0.14f, 0.15f, 0.20f);

        // pavimento e soffitto
        _cube.Draw(_mat, Matrix.TS(new Vec3(0, -0.02f, midZ), new Vec3(_roomHalfW * 2, 0.04f, depth)), floor);
        _cube.Draw(_mat, Matrix.TS(new Vec3(0, 3.0f, midZ), new Vec3(_roomHalfW * 2, 0.04f, depth)), wall);

        // pareti: fondo + due laterali
        _cube.Draw(_mat, Matrix.TS(new Vec3(0, 1.5f, _roomBackZ), new Vec3(_roomHalfW * 2, 3.0f, 0.1f)), wall);
        _cube.Draw(_mat, Matrix.TS(new Vec3(-_roomHalfW, 1.5f, midZ), new Vec3(0.1f, 3.0f, depth)), wall);
        _cube.Draw(_mat, Matrix.TS(new Vec3(_roomHalfW, 1.5f, midZ), new Vec3(0.1f, 3.0f, depth)), wall);

        // insegna del percorso corrente sulla parete di fondo
        var sign = _current is null ? "Questo PC" : Path.GetFileName(_current.TrimEnd('\\'));
        if (string.IsNullOrEmpty(sign))
        {
            sign = _current ?? "";
        }

        Text.Add(sign, Matrix.TRS(new Vec3(0, 2.4f, _roomBackZ + 0.08f), Quat.FromAngles(0, 0, 0), 2f));
    }

    private void DrawBooths(int hover)
    {
        var head = Input.Head.position;
        var folderCol = new Color(0.20f, 0.45f, 0.85f);
        var fileCol = new Color(0.62f, 0.66f, 0.74f);
        var hoverCol = new Color(1.0f, 0.78f, 0.25f);

        for (var i = 0; i < _booths.Count; i++)
        {
            var b = _booths[i];
            var col = i == hover ? hoverCol : (b.IsFolder ? folderCol : fileCol);

            if (b.IsFolder)
            {
                // porta incassata nella parete: pannello alto e stretto
                _cube.Draw(_mat, Matrix.TS(b.Pos, new Vec3(0.16f, 2.0f, 1.2f)), col);
            }
            else
            {
                // pannello del file, quasi piatto
                _cube.Draw(_mat, Matrix.TS(b.Pos, new Vec3(1.0f, 1.0f, 0.06f)), col);
            }

            // etichetta col nome, rivolta verso la testa
            var labelPos = b.Pos + new Vec3(0, 1.25f, 0);
            Text.Add(b.Entry.Name, Matrix.TRS(labelPos, Quat.LookAt(labelPos, head), 0.6f));
        }
    }
}
