using System.Windows.Forms;

namespace DaisysApp.Shell;

/// <summary>
/// WPF has no built-in tray icon, so this wraps System.Windows.Forms.NotifyIcon —
/// the standard, well-supported way to put an icon in the tray from a WPF app.
/// The menu is rebuilt each time it opens, so applets' own items (<see cref="IApplet.TrayMenu"/>) are always current.
/// Its background follows the title bar's bubbles (<see cref="TitleBadges"/>): red while a red one shows, else a
/// bubble's own colour (the AI's Coding: orange, purple…) while one shows, else green while a green one does, else the
/// usual blue (the flower stays white; the logo itself is green since 3.32.0, so the tray's blue is made from it and green
/// still says something is running).
/// Since 4.10.7 the flower's centre turns red, a little larger, fading over a second or so, while something in the app
/// records (<see cref="ScreenRecordings.Recording"/>: Video → Recording, Analysis' race videos; not the virtual webcam),
/// whatever the background's colour.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly System.Drawing.Icon? blue;
    private readonly Dictionary<string, System.Drawing.Icon> recoloured = new(StringComparer.OrdinalIgnoreCase);
    private string shown = "";
    /// <summary>How far the centre has turned red (0 to <see cref="Steps"/>), and the timer that fades it.</summary>
    private int fade;
    private const int Steps = 12;
    private readonly System.Windows.Threading.DispatcherTimer fader = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string, System.Drawing.Icon> centred = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The flower's yellow centre in the logo: where, how big, its colour (null: none found).</summary>
    private readonly (float X, float Y, float R, System.Drawing.Color C)? centre;
    private readonly IReadOnlyList<IApplet> applets;

    public event Action? OpenRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? ExitRequested;

    public TrayIcon(IReadOnlyList<IApplet> applets)
    {
        this.applets = applets;
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => Rebuild(menu);
        Rebuild(menu);

        // (the logo's green background in the tray's usual blue)
        blue = LoadIcon() is { } logo ? Recolour(logo, System.Drawing.Color.FromArgb(0, 95, 184)) : null;
        if (blue != null) centre = FindCentre(blue);
        icon = new NotifyIcon
        {
            Icon = blue,
            Text = AppPaths.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        TitleBadges.Changed += OnBadges;
        fader.Tick += (_, _) => Fade();
        ScreenRecordings.RecordingChanged += OnRecording;
        ShowBadges();
        if (ScreenRecordings.Recording) fader.Start();
    }

    // ---- the title bar's colour: red, a bubble's own, green, or the usual blue ----

    /// <summary>The title bar's green (Shell/TitleBadgeStrip: green when a bubble has none of its own).</summary>
    private const string Green = "#2EA043";

    private void OnBadges() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(ShowBadges);

    private void ShowBadges()
    {
        // (a change queued just before the app closed: the icon is gone)
        if (blue == null || disposed) return;
        var all = TitleBadges.All;
        // (amber, the AI's "near the limit", leaves it as it is)
        string now = all.Any(TitleBadges.IsRed) ? TitleBadges.Red
            : all.FirstOrDefault(b => b.Color != null && !string.Equals(b.Color, TitleBadges.Amber, StringComparison.OrdinalIgnoreCase))?.Color
              ?? (all.Any(b => b.Color == null) ? Green : "");
        if (now == shown) return;
        shown = now;
        Show();
    }

    /// <summary>The background's colour (<see cref="shown"/>) with the centre as far turned as <see cref="fade"/>.</summary>
    private void Show()
    {
        if (blue == null || disposed) return;
        var bg = blue;
        if (shown != "" && TryParse(shown, out var to) && !recoloured.TryGetValue(shown, out bg)) recoloured[shown] = bg = Recolour(blue, to);
        if (fade == 0 || centre == null) { icon.Icon = bg; return; }
        string key = shown + "|" + fade;
        if (!centred.TryGetValue(key, out var ic)) centred[key] = ic = Centre(bg, centre.Value, fade / (double)Steps);
        icon.Icon = ic;
    }

    // ---- recording: the centre red ----

    private void OnRecording() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) fader.Start(); });

    /// <summary>A step towards red while recording, back to yellow when not.</summary>
    private void Fade()
    {
        int target = ScreenRecordings.Recording ? Steps : 0;
        if (fade == target || disposed) { fader.Stop(); return; }
        fade += fade < target ? 1 : -1;
        Show();
    }

    /// <summary>The middle and size of the logo's yellow centre, and its colour.</summary>
    private static (float, float, float, System.Drawing.Color)? FindCentre(System.Drawing.Icon source)
    {
        using var bmp = source.ToBitmap();
        double sx = 0, sy = 0, n = 0, r = 0, g = 0, b = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                // (yellow: red and green high, blue well under them)
                if (p.A < 128 || p.R < 150 || p.G < 100 || p.B > Math.Min(p.R, p.G) - 60) continue;
                sx += x; sy += y; n++; r += p.R; g += p.G; b += p.B;
            }
        if (n == 0) return null;
        return ((float)(sx / n + 0.5), (float)(sy / n + 0.5), (float)Math.Sqrt(n / Math.PI) + 0.5f,
            System.Drawing.Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n)));
    }

    /// <summary>An icon with its centre drawn <paramref name="k"/> of the way from yellow to red, and up to a third larger.</summary>
    private static System.Drawing.Icon Centre(System.Drawing.Icon source, (float X, float Y, float R, System.Drawing.Color C) c, double k)
    {
        var red = System.Drawing.Color.FromArgb(230, 30, 40);
        int Mix(int a, int b) => (int)Math.Round(a + (b - a) * k);
        // (drawn on a 32-bit copy: GDI+ won't draw on every bitmap an icon gives)
        using var from = source.ToBitmap();
        using var bmp = new System.Drawing.Bitmap(from.Width, from.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var gfx = System.Drawing.Graphics.FromImage(bmp))
        using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(Mix(c.C.R, red.R), Mix(c.C.G, red.G), Mix(c.C.B, red.B))))
        {
            gfx.DrawImage(from, 0, 0, from.Width, from.Height);
            gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float radius = c.R * (float)(1 + 0.35 * k);
            gfx.FillEllipse(brush, c.X - radius, c.Y - radius, radius * 2, radius * 2);
        }
        nint h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private static bool TryParse(string hex, out System.Drawing.Color c)
    {
        c = default;
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
        c = System.Drawing.Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return true;
    }

    /// <summary>The logo with its green background (and its soft edges into the petals) in another colour; the flower
    /// stays white and yellow.</summary>
    private static System.Drawing.Icon Recolour(System.Drawing.Icon source, System.Drawing.Color to)
    {
        using var bmp = source.ToBitmap();
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                // (green, or green fading to white; white and the yellow centre are left)
                if (p.A == 0 || p.G <= p.R) continue;
                // how far from white, by the red channel (the deepest green's is 22): keeps the gradient and the edges
                double k = Math.Clamp((255 - p.R) / 233.0, 0, 1);
                int Mix(int c) => Math.Clamp((int)Math.Round(255 - k * (255 - c)), 0, 255);
                bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(p.A, Mix(to.R), Mix(to.G), Mix(to.B)));
            }
        nint h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(nint h);

    private void Rebuild(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(F("Open {0}", AppPaths.DisplayName), null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(T("Check for updates"), null, (_, _) => CheckUpdatesRequested?.Invoke());

        // each applet that offers tray items gets a submenu named after it; a tab of several tools (Video) puts each
        // tool's at the top level instead, named after the tool, rather than a submenu inside a submenu
        foreach (var applet in applets)
        {
            var menus = applet is AppletGroup group
                ? group.Tools.Select(t => (t.Title, Items: Items(t.Applet)))
                : [(TabNames.For(applet.Meta.Id, Any(applet.Meta.Title)), Items(applet))];
            foreach (var (title, items) in menus)
            {
                if (items == null || items.Count == 0) continue;
                menu.Items.Add(new ToolStripSeparator());
                var sub = new ToolStripMenuItem(title);
                foreach (var item in items) sub.DropDownItems.Add(ToMenuItem(item));
                menu.Items.Add(sub);
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Exit"), null, (_, _) => ExitRequested?.Invoke());
    }

    private static IReadOnlyList<AppletMenuItem>? Items(IApplet applet)
    {
        try { return applet.TrayMenu; }
        catch { return null; }
    }

    private static ToolStripItem ToMenuItem(AppletMenuItem item)
    {
        if (item.IsSeparator) return new ToolStripSeparator();
        var menuItem = new ToolStripMenuItem(item.Text)
        {
            Enabled = item.Enabled,
            ShortcutKeyDisplayString = item.Hint is { } hint ? Shared.Hotkeys.ControllerButtons.Describe(hint) : null,
            Checked = item.Checked,
        };
        if (item.Click is { } click) menuItem.Click += (_, _) => click();
        if (item.Children != null)
            foreach (var child in item.Children) menuItem.DropDownItems.Add(ToMenuItem(child));
        return menuItem;
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/DaisysApp.ico"));
            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        }
        catch
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
    }

    public void ShowBalloon(string title, string text) => icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    private bool disposed;

    public void Dispose()
    {
        disposed = true;
        TitleBadges.Changed -= OnBadges;
        ScreenRecordings.RecordingChanged -= OnRecording;
        fader.Stop();
        icon.Visible = false;
        icon.Dispose();
        foreach (var ic in recoloured.Values) ic.Dispose();
        foreach (var ic in centred.Values) ic.Dispose();
        blue?.Dispose();
    }
}
