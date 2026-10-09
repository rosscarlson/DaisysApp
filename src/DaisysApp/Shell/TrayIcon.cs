using System.Windows.Forms;

namespace DaisysApp.Shell;

/// <summary>
/// WPF has no built-in tray icon, so this wraps System.Windows.Forms.NotifyIcon —
/// the standard, well-supported way to put an icon in the tray from a WPF app.
/// The menu is rebuilt each time it opens, so applets' own items (<see cref="IApplet.TrayMenu"/>) are always current.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;
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

        icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppPaths.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    private void Rebuild(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(F("Open {0}", AppPaths.DisplayName), null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(T("Check for updates"), null, (_, _) => CheckUpdatesRequested?.Invoke());

        // each applet that offers tray items gets a submenu named after it
        foreach (var applet in applets)
        {
            IReadOnlyList<AppletMenuItem>? items;
            try { items = applet.TrayMenu; }
            catch { items = null; }
            if (items == null || items.Count == 0) continue;
            menu.Items.Add(new ToolStripSeparator());
            var sub = new ToolStripMenuItem(TabNames.For(applet.Meta.Id, Any(applet.Meta.Title)));
            foreach (var item in items) sub.DropDownItems.Add(ToMenuItem(item));
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Exit"), null, (_, _) => ExitRequested?.Invoke());
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
        icon.Visible = false;
        icon.Dispose();
    }
}
