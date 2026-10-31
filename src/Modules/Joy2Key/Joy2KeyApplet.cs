using System.Windows;
using DaisysApp.Shell;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Gaming.Joy2Key;

/// <summary>
/// Joy2Key: controller buttons, sticks and POV hats pressing keys, clicking or moving the mouse, running programs and
/// switching profiles, with a profile per game (like JoyToKey, whose profiles it can import). A module of its own (and an
/// app of its own, Joy2Key.exe); in Daisy's App a sub-tab of Gaming with ForzaWheel (Core's Shell/TabGroups.cs). See README.md.
/// </summary>
[Applet("Joy2Key", "Button2Key", "", Order = 55, Group = "GamingTools", GroupOrder = 10,
    Description = "Controller buttons and sticks to keys, macros and mouse moves, a profile per game")]
public sealed class Joy2KeyApplet : IApplet
{
    private readonly Joy2KeySettings settings = Joy2KeySettings.Load();
    private readonly Joy2KeyEngine engine;
    private readonly Joy2KeyView view;
    private readonly Hotkey hotkey;

    // a hidden window of its own, which DirectInput reads controllers in the background for (Joysticks)
    private readonly System.Windows.Interop.HwndSource window = new(new System.Windows.Interop.HwndSourceParameters("DaisysApp Joy2Key") { Width = 0, Height = 0, WindowStyle = 0 });

    public Joy2KeyApplet()
    {
        Joysticks.Window = window.Handle;
        var profiles = ProfileStore.LoadAll();
        // before 0.17.2 the profile showing and the one in use could differ; now they're one: the one showing
        if (settings.Showing is { } showing && !showing.Equals(settings.Active, StringComparison.OrdinalIgnoreCase)
            && profiles.Any(p => p.Name.Equals(showing, StringComparison.OrdinalIgnoreCase)))
            settings.Active = showing;
        if (!profiles.Any(p => p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase))) settings.Active = profiles[0].Name;
        engine = new Joy2KeyEngine(settings);
        engine.SetProfiles(profiles);
        // the on / off shortcut, from anywhere (in the game too), as the On / Off button
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Joy2Key.OnOff", Module = T("Button2Key"), Name = T("On / off"),
            Get = () => settings.Hotkey, Set = v => { settings.Hotkey = v; settings.Save(); },
        }, () => { engine.Turn(!settings.Enabled); view!.Changed(); });
        view = new Joy2KeyView(settings, engine, profiles, hotkey);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        engine.Enabled = settings.Enabled;
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu
    {
        get
        {
            var items = new List<AppletMenuItem>
            {
                new(T("On"), () => { engine.Turn(!settings.Enabled); view.Changed(); }) { Checked = settings.Enabled, Hint = settings.Hotkey },
                AppletMenuItem.Separator,
            };
            foreach (var p in view.Profiles)
            {
                string name = p.Name;
                items.Add(new(name, () => { settings.Active = settings.Showing = name; settings.Save(); engine.Refresh(); view.Changed(); })
                {
                    Checked = name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase),
                    Hint = name.Equals(engine.Current, StringComparison.OrdinalIgnoreCase) && settings.Enabled ? T("in use") : null,
                });
            }
            return items;
        }
    }

    public void Dispose()
    {
        // on only because a game is running: off at the next start (on again if the game is still running then)
        if (engine.OnByGame) settings.Enabled = false;
        settings.Save();
        hotkey.Dispose();
        engine.Dispose();
        Joysticks.Close();
        window.Dispose();
    }
}
