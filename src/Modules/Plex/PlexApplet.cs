using System.Windows;
using DaisysApp.Applets.Plex.Views;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Plex;

/// <summary>
/// The Plex tab: a Plex Media Server's admin tools. Now Playing (who's watching what, how, and stopping a stream; with
/// Tautulli's recent plays), Server (its state, problems found, libraries, maintenance, and on the server's own PC its
/// logs, caches and a restart), Database (integrity check, repair, backups and a backup scheduled in Windows' Task
/// Scheduler, restore), Posters (a 4K / HDR / Dolby Vision banner on many posters at once, and off again) and Tautulli
/// (its statistics, history and users). Set up in the setup wizard: this PC's Plex, a plex.tv sign-in, or an address and
/// token; Tautulli's address and API key (found on this PC when it's here).
/// </summary>
[Applet("Plex", "Plex", "\uE8B2", Order = 48,
    Description = "Your Plex server: who's watching, its database checked, repaired and backed up on a schedule, poster banners, and Tautulli's stats")]
public sealed class PlexApplet : AppletGroup
{
    public const string Id = "Plex";

    private readonly PlexSettings settings = PlexSettings.Load();
    private readonly PlexService service;
    private bool ownerMade;

    public PlexApplet()
    {
        service = new PlexService(settings);
        Add("Activity", T("Now Playing"), "\uE768", () => MakeTool(new ActivityView(service)));
        Add("Server", T("Server"), "\uE7F4", () => MakeTool(new ServerView(service)));
        Add("Database", T("Database"), "\uE8F1", () => MakeTool(new DatabaseView(service)));
        Add("Posters", T("Posters"), "\uEB9F", () => MakeTool(new PostersView(service)));
        Add("Tautulli", T("Tautulli"), "\uE9D9", () => MakeTool(new TautulliView(service)));
    }

    /// <summary>A tool; the first one made also has the connection's Settings page and setup step (any can be off).</summary>
    private PlexTool MakeTool(FrameworkElement view)
    {
        bool owner = !ownerMade;
        ownerMade = true;
        return new PlexTool(service, view, owner);
    }
}

/// <summary>One of the Plex tab's sub-tabs.</summary>
internal sealed class PlexTool(PlexService service, FrameworkElement view, bool owner) : IApplet
{
    private bool setupShown;
    private PlexSettings S => service.Settings;

    public FrameworkElement View => view;

    public FrameworkElement? SettingsView { get; } = owner ? new PlexSettingsView(service) : null;

    /// <summary>The first tool connects (once, for all of them).</summary>
    public void Start()
    {
        if (owner) service.Start();
    }

    public FrameworkElement? SetupPage
    {
        get
        {
            if (!owner) return null;
            setupShown = true;
            return new SetupPanel(service);
        }
    }

    public bool SetupRequired => owner && !S.Configured && !S.SetupSkipped;

    public bool NeedsOnboarding => owner && !S.Configured && !S.SetupSkipped && !setupShown;

    public void RunOnboarding(Window owner)
    {
        setupShown = true;
        SetupPanel.ShowWindow(service, owner);
    }

    public void SaveSettings() => S.Save();

    public void Dispose()
    {
        if (view is IDisposable d) d.Dispose();
    }
}
