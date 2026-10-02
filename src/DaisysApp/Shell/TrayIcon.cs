using System.Windows.Forms;

namespace DaisysApp.Shell;

/// <summary>
/// WPF has no built-in tray icon, so this wraps System.Windows.Forms.NotifyIcon —
/// the standard, well-supported way to put an icon in the tray from a WPF app.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon icon;

    public event Action? OpenRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add($"Open {AppPaths.DisplayName}", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Check for updates", null, (_, _) => CheckUpdatesRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

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
