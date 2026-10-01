using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DesktopPager.Tray.DesktopEffects;

namespace DesktopPager.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DesktopPageManager _pageManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly AutostartService _autostartService;
    private readonly HotkeyWindow _window;
    private readonly BarForm _bar;
    private readonly DesktopEffectsService _effects;

    public TrayApplicationContext()
    {
        var pagerService = new DesktopIconPagerService();
        _pageManager = new DesktopPageManager(pagerService);
        _pageManager.RefreshState();
        _autostartService = new AutostartService();

        _hotkeyManager = new GlobalHotkeyManager();
        _window = new HotkeyWindow(_hotkeyManager, _pageManager, UpdateTooltip, RestartExplorer, RotateScreen);
        var hotkeysRegistered = _hotkeyManager.Register(_window.Handle);

        // effetti desktop 3D (cubo di paginazione + finestre gelatina)
        _effects = new DesktopEffectsService(dir =>
        {
            if (dir > 0) _pageManager.NextPage(); else _pageManager.PreviousPage();
            UpdateTooltip();
        });

        // barra a scomparsa stile Windows, visibile all'avvio
        _bar = new BarForm { Effects = _effects };
        _bar.Show();

        var trayMenu = BarMenuStyle.New();
        var barItem = new ToolStripMenuItem("Barra a scomparsa") { Checked = true, CheckOnClick = true };
        barItem.CheckedChanged += (_, _) => { if (barItem.Checked) _bar.Show(); else _bar.Hide(); };
        trayMenu.Items.Add(barItem);
        var replaceItem = new ToolStripMenuItem("Sostituisci la barra di Windows") { CheckOnClick = true };
        replaceItem.Click += (_, _) =>
        {
            _bar.ToggleReplaceTaskbar();
            replaceItem.Checked = _bar.ReplaceTaskbarEnabled;
        };
        trayMenu.Items.Add(replaceItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Pagina avanti (Ctrl+Alt+PgGiù)", null, (_, _) => ChangePage(_pageManager.NextPage));
        trayMenu.Items.Add("Pagina indietro (Ctrl+Alt+PgSu)", null, (_, _) => ChangePage(_pageManager.PreviousPage));
        trayMenu.Items.Add("Prima pagina (Ctrl+Alt+Home)", null, (_, _) => ChangePage(_pageManager.GoToMainPage));
        var rotateMenu = new ToolStripMenuItem("Ruota schermo");
        rotateMenu.DropDownItems.Add("Sottosopra (Ctrl+Alt+Su)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation180));
        rotateMenu.DropDownItems.Add("Normale (Ctrl+Alt+Giù)", null, (_, _) => RotateScreen(ScreenRotationService.OrientationDefault));
        rotateMenu.DropDownItems.Add("Barra a sinistra (Ctrl+Alt+Sinistra)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation90));
        rotateMenu.DropDownItems.Add("Barra a destra (Ctrl+Alt+Destra)", null, (_, _) => RotateScreen(ScreenRotationService.Orientation270));
        rotateMenu.DropDownItems.Add(new ToolStripSeparator());
        rotateMenu.DropDownItems.Add("Emergenza: ripristina (Ctrl+Alt+Shift+^)", null, (_, _) => RotateScreen(ScreenRotationService.OrientationDefault));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(rotateMenu);
        trayMenu.Items.Add("Riavvia Explorer (Ctrl+Alt+Fine)", null, (_, _) => RestartExplorer());
        trayMenu.Items.Add(new ToolStripSeparator());
        var effectsMenu = new ToolStripMenuItem("Effetti desktop 3D");
        var cubeItem = new ToolStripMenuItem("Cubo del desktop (Ctrl+tasto destro trascina)")
        {
            Checked = _effects.CubeEnabled,
            CheckOnClick = true
        };
        cubeItem.Click += (_, _) => _effects.CubeEnabled = cubeItem.Checked;
        var wobbleItem = new ToolStripMenuItem("Finestre gelatina (allo spostamento)")
        {
            Checked = _effects.WobbleEnabled,
            CheckOnClick = true
        };
        wobbleItem.Click += (_, _) => _effects.WobbleEnabled = wobbleItem.Checked;
        effectsMenu.DropDownItems.Add(cubeItem);
        effectsMenu.DropDownItems.Add(wobbleItem);
        trayMenu.Items.Add(effectsMenu);

        // Modalità VR della Vista 3D Game: DI DEFAULT SPENTA. Da accesa, aprire
        // la Vista 3D Game lancia il modulo VR (visore) invece della finestra 3D.
        var vrItem = new ToolStripMenuItem("Vista 3D Game in VR (visore)")
        {
            Checked = VrMode.Enabled, // false all'avvio
            CheckOnClick = true,
            ToolTipText = "Richiede un visore OpenXR (es. Meta Quest via Link). " +
                          "Da spenta, la Vista 3D Game resta su schermo."
        };
        vrItem.Click += (_, _) => VrMode.Enabled = vrItem.Checked;
        trayMenu.Items.Add(vrItem);

        trayMenu.Items.Add(BuildTaskbarMenu());
        trayMenu.Items.Add(BuildColorMenu());
        var autostartItem = new ToolStripMenuItem("Avvio automatico con Windows")
        {
            Checked = _autostartService.IsEnabled()
        };
        autostartItem.Click += (_, _) =>
        {
            var targetEnabled = !autostartItem.Checked;
            if (_autostartService.SetEnabled(targetEnabled))
            {
                autostartItem.Checked = targetEnabled;
            }
            else
            {
                MessageBox.Show(
                    "Impossibile aggiornare l'impostazione di avvio automatico.",
                    "DesktopPager3D-OS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        trayMenu.Items.Add(autostartItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Esci", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = ResolveIcon(),
            Visible = true,
            Text = "DesktopPager3D-OS",
            ContextMenuStrip = trayMenu
        };
        UpdateTooltip();
        if (!hotkeysRegistered)
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                "DesktopPager3D-OS",
                "Impossibile registrare una o più hotkey globali. Verifica conflitti con altre applicazioni.",
                ToolTipIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        _effects.Dispose();
        _bar.Close();
        _bar.Dispose();
        // lascia il desktop come l'abbiamo trovato (scroll a zero, stile originale)
        _pageManager.GoToMainPage();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _hotkeyManager.Dispose();
        _window.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>
    /// Sottomenu della barra di Windows: come sfogliare l'elenco applicazioni
    /// quando non ci sta in una riga sola, e se colorarla come la nostra barra.
    /// </summary>
    private ToolStripMenuItem BuildTaskbarMenu()
    {
        var menu = new ToolStripMenuItem("Barra di Windows");

        // Attenzione ai nomi: entrambe le modalità sfogliano le stesse pagine
        // VERTICALI, cambia solo quale rotellina usi. Non promettere una
        // disposizione orizzontale delle icone: la barra di Windows non ce l'ha
        // (verificato: WS_HSCROLL assente e SB_HORZ con corsa zero). Explorer
        // manda le icone a capo in righe e impila le righe; per metterle in fila
        // orizzontale bisognerebbe rifargli l'impaginazione a ogni cambiamento.
        var modes = new (string Text, string Tip, TaskbarScrollMode Mode)[]
        {
            ("Sfoglia girando la rotellina",
                "Gira la rotellina sopra i pulsanti delle applicazioni per cambiare pagina.",
                TaskbarScrollMode.Wheel),
            ("Sfoglia inclinando la rotellina (tilt)",
                "Come sopra, ma inclinando la rotellina di lato invece di girarla: sfoglia " +
                "sempre le stesse pagine verticali. Richiede un mouse con la rotellina inclinabile.",
                TaskbarScrollMode.TiltWheel),
            ("Disattivato: freccette di Windows",
                "Torna al metodo di Windows: si cambia pagina solo con le freccette.",
                TaskbarScrollMode.Off)
        };

        var items = new List<ToolStripMenuItem>();

        void Refresh()
        {
            foreach (var it in items)
            {
                it.Checked = it.Tag is TaskbarScrollMode m && m == _effects.TaskbarScroll;
            }
        }

        foreach (var (text, tip, mode) in modes)
        {
            var item = new ToolStripMenuItem(text) { Tag = mode, ToolTipText = tip };
            item.Click += (_, _) => { _effects.TaskbarScroll = mode; Refresh(); };
            items.Add(item);
            menu.DropDownItems.Add(item);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());
        var tintItem = new ToolStripMenuItem("Coloral&a come la barra a linguetta")
        {
            Checked = _effects.TaskbarTint,
            CheckOnClick = true
        };
        tintItem.Click += (_, _) => _effects.TaskbarTint = tintItem.Checked;
        menu.DropDownItems.Add(tintItem);

        menu.DropDownOpening += (_, _) => { Refresh(); tintItem.Checked = _effects.TaskbarTint; };
        Refresh();
        return menu;
    }

    /// <summary>
    /// Sottomenu del colore: cambia il colore base da cui derivano barra, menu
    /// e menu Start. La scelta viene ricordata tra un avvio e l'altro.
    /// </summary>
    private static ToolStripMenuItem BuildColorMenu()
    {
        var menu = new ToolStripMenuItem("Colore della barra e dei menu");
        var items = new List<ToolStripMenuItem>();

        void Refresh()
        {
            foreach (var it in items)
            {
                it.Checked = it.Tag is Color c && c.ToArgb() == BarStyle.Base.ToArgb();
            }
        }

        foreach (var (name, color) in BarStyle.Presets)
        {
            var item = new ToolStripMenuItem(name)
            {
                Tag = color,
                Image = Swatch(color)
            };
            item.Click += (_, _) => { BarStyle.Base = color; Refresh(); };
            items.Add(item);
            menu.DropDownItems.Add(item);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("Personalizzato…", null, (_, _) =>
        {
            using var dlg = new ColorDialog { Color = BarStyle.Base, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                BarStyle.Base = dlg.Color;
                Refresh();
            }
        });

        menu.DropDownOpening += (_, _) => Refresh();
        Refresh();
        return menu;
    }

    /// <summary>Quadratino del colore da mostrare nel menu.</summary>
    private static Bitmap Swatch(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var b = new SolidBrush(color);
        g.FillRectangle(b, 1, 1, 14, 14);
        using var p = new Pen(Color.FromArgb(160, 255, 255, 255));
        g.DrawRectangle(p, 1, 1, 13, 13);
        return bmp;
    }

    private static Icon ResolveIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopPager.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private bool ChangePage(Func<bool> action)
    {
        var ok = action();
        UpdateTooltip();
        return ok;
    }

    private void RotateScreen(int orientation)
    {
        if (!ScreenRotationService.Rotate(orientation))
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                "DesktopPager3D-OS",
                "Rotazione non riuscita: il driver video potrebbe non supportarla.",
                ToolTipIcon.Warning);
        }
    }

    private void RestartExplorer()
    {
        if (!ExplorerService.Restart())
        {
            MessageBox.Show(
                "Impossibile riavviare Explorer.",
                "DesktopPager3D-OS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _pageManager.RefreshState();
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text = $"DesktopPager3D-OS - Pagina {_pageManager.CurrentPage}/{_pageManager.TotalPages}";
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly DesktopPageManager _pageManager;
        private readonly Action _onPageChanged;
        private readonly Action _restartExplorer;
        private readonly Action<int> _rotateScreen;

        public HotkeyWindow(GlobalHotkeyManager hotkeyManager, DesktopPageManager pageManager, Action onPageChanged, Action restartExplorer, Action<int> rotateScreen)
        {
            _hotkeyManager = hotkeyManager;
            _pageManager = pageManager;
            _onPageChanged = onPageChanged;
            _restartExplorer = restartExplorer;
            _rotateScreen = rotateScreen;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_hotkeyManager.HandleMessage(m, _pageManager, _restartExplorer, _rotateScreen))
            {
                _onPageChanged();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
