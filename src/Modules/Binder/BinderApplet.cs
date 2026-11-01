using System.Windows;
using System.Windows.Interop;
using DaisysApp.Settings;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Binder;

/// <summary>Saved in %APPDATA%\DaisysApp\Binder.json: the titles set up, the one showing, and each one's group last shown.</summary>
public sealed class BinderSettings
{
    public List<TitleSetup> Titles { get; set; } = new();
    public string? Selected { get; set; }
    public Dictionary<string, string> Group { get; set; } = new();

    public static BinderSettings Load() => JsonStore.Load<BinderSettings>("Binder");
    public void Save() => JsonStore.Save("Binder", this);
}

/// <summary>
/// Gaming → Binder (since 3.36.0, a module of its own since 3.36.1, SimEffects' before; from FotG Controls): a game's key, mouse and controller bindings set up outside it, for
/// any game a JSON file describes (<see cref="Titles"/>): the titles set up on the left with their details and the
/// devices, an example device for the title's kind on the right with the bindings under it; bindings copied from
/// another title by what each action does. Written into the game's own files as they change. See Binder/README.md.
/// </summary>
[Applet("Binder", "Binder", "", Order = 59, Group = "GamingTools", GroupOrder = 47,
    Description = "A game's key, mouse and controller bindings set up outside it, copied from another game's")]
public sealed class BinderApplet : IApplet
{
    private readonly BinderSettings settings = BinderSettings.Load();
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp Binder") { Width = 0, Height = 0, WindowStyle = 0 });
    private BinderView? view;

    public FrameworkElement View => view ??= new BinderView(settings, window.Handle);
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        view?.Close();
        settings.Save();
        window.Dispose();
    }
}
