using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Joy 2 Key: controller buttons, sticks and POV hats pressing keys, clicking or moving the mouse, running programs and
/// switching profiles, with a profile per game (like JoyToKey, whose profiles it can import). See README.md.
/// </summary>
[Applet("Joy2Key", "Joy 2 Key", "", Order = 55,
    Description = "Turns controller buttons and sticks into key presses, macros and mouse moves, with a profile per game")]
public sealed class Joy2KeyApplet : IApplet
{
    private readonly Joy2KeySettings settings = Joy2KeySettings.Load();
    private readonly Joy2KeyEngine engine;
    private readonly Joy2KeyView view;

    public Joy2KeyApplet()
    {
        var profiles = ProfileStore.LoadAll();
        // before 0.17.2 the profile showing and the one in use could differ; now they're one: the one showing
        if (settings.Showing is { } showing && !showing.Equals(settings.Active, StringComparison.OrdinalIgnoreCase)
            && profiles.Any(p => p.Name.Equals(showing, StringComparison.OrdinalIgnoreCase)))
            settings.Active = showing;
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
        settings.Save();
        engine.Dispose();
    }
}
