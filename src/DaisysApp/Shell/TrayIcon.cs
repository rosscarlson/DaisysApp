using System.Windows.Forms;

namespace DaisysApp.Shell;

/// <summary>
/// WPF has no built-in tray icon, so this wraps System.Windows.Forms.NotifyIcon —
/// the standard, well-supported way to put an icon in the tray from a WPF app.
/// The menu is rebuilt each time it opens, so applets' own items (<see cref="IApplet.TrayMenu"/>) are always current.
/// Its background follows the title bar's bubbles (<see cref="TitleBadges"/>): red while a red one shows, else green
/// while a green one does, else the usual blue (the flower stays white).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly System.Drawing.Icon? blue;
    private System.Drawing.Icon? green, red;
    private string shown = "";
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

        blue = LoadIcon();
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
        ShowBadges();
    }

    // ---- the title bar's colour: red, green, or the usual blue ----

    /// <summary>The title bar's bubble colours (Shell/TitleBadgeStrip: green when a bubble has none of its own).</summary>
    private static readonly System.Drawing.Color Green = System.Drawing.Color.FromArgb(0x2E, 0xA0, 0x43);
    private static readonly System.Drawing.Color Red = System.Drawing.Color.FromArgb(0xF8, 0x51, 0x49);

    private void OnBadges() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(ShowBadges);

    private void ShowBadges()
    {
        // (a change queued just before the app closed: the icon is gone)
        if (blue == null || disposed) return;
        var all = TitleBadges.All;
        // (amber, the AI's "near the limit", leaves it blue)
        string now = all.Any(TitleBadges.IsRed) ? "red"
            : all.Any(b => b.Color == null) ? "green" : "";
        if (now == shown) return;
        shown = now;
        icon.Icon = now switch
        {
            "red" => red ??= Recolour(blue, Red),
            "green" => green ??= Recolour(blue, Green),
            _ => blue,
        };
    }

    /// <summary>The icon with its blue background (and its soft edges into the petals) in another colour; the flower
    /// stays white and yellow.</summary>
    private static System.Drawing.Icon Recolour(System.Drawing.Icon source, System.Drawing.Color to)
    {
        using var bmp = source.ToBitmap();
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                // (blue, or blue fading to white; white and the yellow centre are left)
                if (p.A == 0 || p.B <= p.R) continue;
                // how far from white, by the red channel (the deepest blue's is 17): keeps the gradient and the edges
                double k = Math.Clamp((255 - p.R) / 238.0, 0, 1);
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
            ShortcutKeyDisplayString = item.Hint,
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
        icon.Visible = false;
        icon.Dispose();
        green?.Dispose();
        red?.Dispose();
        blue?.Dispose();
    }
}
