using System.Windows.Forms;

namespace DaisysApp.Shell;

/// <summary>
/// WPF has no built-in tray icon, so this wraps System.Windows.Forms.NotifyIcon —
/// the standard, well-supported way to put an icon in the tray from a WPF app.
/// The menu is rebuilt each time it opens, so applets' own items (<see cref="IApplet.TrayMenu"/>) are always current.
/// While a green bubble shows in the title bar (<see cref="TitleBadges"/>), the flower's white petals turn that green.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly System.Drawing.Icon? white;
    private System.Drawing.Icon? green;
    private bool isGreen;
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

        white = LoadIcon();
        icon = new NotifyIcon
        {
            Icon = white,
            Text = AppPaths.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        TitleBadges.Changed += OnBadges;
        ShowBadges();
    }

    // ---- green while a green title bar bubble shows ----

    /// <summary>The title bar's green (Shell/TitleBadgeStrip: a bubble with no colour of its own).</summary>
    private static readonly System.Drawing.Color Green = System.Drawing.Color.FromArgb(0x2E, 0xA0, 0x43);

    private void OnBadges() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(ShowBadges);

    private void ShowBadges()
    {
        bool on = TitleBadges.All.Any(b => b.Color == null);
        if (on == isGreen || white == null) return;
        isGreen = on;
        if (on) green ??= Recolour(white);
        icon.Icon = on ? green : white;
    }

    /// <summary>The icon with its white petals (and their soft edges, into the blue and the yellow centre) green.</summary>
    private static System.Drawing.Icon Recolour(System.Drawing.Icon source)
    {
        using var bmp = source.ToBitmap();
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.A == 0) continue;
                // how white it is: along white–blue the red channel says, along white–yellow the blue one
                double t = Math.Min(Math.Clamp((p.R - 40) / 215.0, 0, 1), Math.Clamp((p.B - 61) / 194.0, 0, 1));
                if (t <= 0) continue;
                int Mix(int c, int g) => Math.Clamp((int)Math.Round(c + t * (g - 255)), 0, 255);
                bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(p.A, Mix(p.R, Green.R), Mix(p.G, Green.G), Mix(p.B, Green.B)));
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

    public void Dispose()
    {
        TitleBadges.Changed -= OnBadges;
        icon.Visible = false;
        icon.Dispose();
        green?.Dispose();
        white?.Dispose();
    }
}
