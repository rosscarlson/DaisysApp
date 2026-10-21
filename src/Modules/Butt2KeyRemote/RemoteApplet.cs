using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Button2Key Remote: ButtScreens (screens of buttons laid out on a grid) for a phone, tablet or PC, or popped out on
/// this one, whose buttons do what Button2Key's inputs do; BigButts (sets of ButtScreens) switched by the game running;
/// and the server devices connect to. A module of its own; in Daisy's App a sub-tab of Gaming (GamingTab.cs). See README.md.
/// </summary>
[Applet("Butt2KeyRemote", "Button2Key Remote", "", Order = 59, Group = "GamingTools", GroupOrder = 15,
    Description = "Screens of buttons on your phone, tablet or PC (or popped out here) that press keys, play macros and more")]
public sealed class RemoteApplet : IApplet
{
    private readonly ButtHub hub;
    private readonly RemoteView view;

    public RemoteApplet()
    {
        hub = new ButtHub(Application.Current.Dispatcher);
        view = new RemoteView(hub);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => hub.Start();

    public void SaveSettings() => hub.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu
    {
        get
        {
            var items = new List<AppletMenuItem>
            {
                new(T("On"), () => hub.SetEnabled(!hub.Settings.Enabled)) { Checked = hub.Settings.Enabled },
                new(T("Let devices connect"), () => { hub.Settings.Serve = !hub.Settings.Serve; hub.Save(); hub.Server.Apply(); hub.Publish(); }) { Checked = hub.Settings.Serve },
                AppletMenuItem.Separator,
            };
            var bigButts = hub.Settings.BigButts.Select(b => new AppletMenuItem(b.Name, () => hub.UseBigButt(b.Name)) { Checked = b == hub.Active }).ToList();
            items.Add(new AppletMenuItem(T("BigButt in use"), null, bigButts));
            var screens = hub.Screens.Select(s => new AppletMenuItem(s.Name, () => { if (hub.IsPoppedOut(s)) hub.ClosePopOut(s); else hub.PopOut(s); }) { Checked = hub.IsPoppedOut(s) }).ToList();
            if (screens.Count > 0) items.Add(new AppletMenuItem(T("Pop out"), null, screens));
            return items;
        }
    }

    public void Dispose() => hub.Dispose();
}
