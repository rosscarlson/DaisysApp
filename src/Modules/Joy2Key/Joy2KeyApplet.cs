using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Joy 2 Key: controller buttons, sticks and POV hats pressing keys, clicking or moving the mouse, running programs and
/// switching profiles, with a profile per game (like JoyToKey, whose profiles it can import). See README.md.
/// </summary>
[Applet("Joy2Key", "Joy 2 Key", "", Order = 55,
    Description = "Turns controller buttons and sticks into key presses and mouse moves, with a profile per game (imports JoyToKey's)")]
public sealed class Joy2KeyApplet : IApplet
{
    private readonly Joy2KeySettings settings = Joy2KeySettings.Load();
    private readonly Joy2KeyEngine engine;
    private readonly Joy2KeyView view;

    public Joy2KeyApplet()
    {
        var profiles = ProfileStore.LoadAll();
        if (!profiles.Any(p => p.Name.Equals(settings.Active, StringComparison.OrdinalIgnoreCase))) settings.Active = profiles[0].Name;
        engine = new Joy2KeyEngine(settings);
        engine.SetProfiles(profiles);
        view = new Joy2KeyView(settings, engine, profiles);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => engine.Enabled = settings.Enabled;

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu
    {
        get
        {
            var items = new List<AppletMenuItem>
            {
                new(T("On"), () => { settings.Enabled = !settings.Enabled; settings.Save(); engine.Enabled = settings.Enabled; view.Changed(); }) { Checked = settings.Enabled },
                AppletMenuItem.Separator,
            };
            foreach (var p in view.Profiles)
            {
                string name = p.Name;
                items.Add(new(name, () => { settings.Active = name; settings.Save(); engine.Refresh(); view.Changed(); })
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
        settings.Save();
        engine.Dispose();
    }
}
