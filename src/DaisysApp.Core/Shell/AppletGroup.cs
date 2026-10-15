using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Logging;
using DaisysApp.Settings;

namespace DaisysApp.Shell;

/// <summary>
/// One tab that holds several tools, each on its own sub-tab (Video: Resizer, Mini Mirror and Gaming). Each tool is an
/// <see cref="IApplet"/> without an [Applet] attribute of its own (so it isn't a tab by itself), with its own code,
/// settings file and Settings page as before; the group starts, saves and stops them all, hands key presses to the
/// one showing, and puts their Settings pages and tray menus under its own. The sub-tab showing is remembered in
/// &lt;Id&gt;.json.
/// </summary>
public abstract class AppletGroup : IApplet
{
    /// <summary>One tool in the group: an id (for the remembered sub-tab), its title and icon, and the tool.</summary>
    public sealed record Tool(string Id, string Title, string Icon, IApplet Applet);

    /// <summary>Which sub-tab was showing. Saved in %APPDATA%\DaisysApp\&lt;group id&gt;.json.</summary>
    public sealed class GroupSettings
    {
        public string? Tool { get; set; }
    }

    private readonly List<Tool> tools = new();
    private readonly GroupSettings settings;
    private readonly SubTabs view = new();
    private SubTabs? settingsView;
    private readonly string fileName;
    private readonly string? wanted; // the sub-tab to open on, from last time

    protected AppletGroup()
    {
        fileName = AppletAttribute.Of(GetType()).Id;
        settings = JsonStore.Load<GroupSettings>(fileName);
        wanted = settings.Tool;
        view.Selected += id => settings.Tool = id;
    }

    private Logger GroupLog => Log.For(Log.NameOf(GetType().Assembly));

    /// <summary>Adds a tool (in the derived class's constructor, in sub-tab order).</summary>
    protected void Add(string id, string title, string icon, Func<IApplet> create)
    {
        var sw = Stopwatch.StartNew();
        IApplet applet;
        try { applet = create(); }
        catch (Exception ex)
        {
            GroupLog.Error($"{id} couldn't be created", ex);
            ErrorLog.Write($"{id} create", ex);
            var text = new TextBlock { Text = F("{0} couldn't load: {1}", title, (ex.InnerException ?? ex).Message), TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
            view.Add(id, title, icon, text);
            return;
        }
        GroupLog.Info($"{id} created in {sw.ElapsedMilliseconds} ms");
        tools.Add(new Tool(id, title, icon, applet));
        view.Add(id, title, icon, applet.View);
        if (wanted == id) view.Select(id);
        if (applet.SettingsView is { } page)
        {
            settingsView ??= new SubTabs();
            settingsView.Add(id, title, icon, page);
        }
    }

    public IReadOnlyList<Tool> Tools => tools;

    public FrameworkElement View => view;

    /// <summary>The tools' Settings pages as sub-tabs (null if none has one).</summary>
    public FrameworkElement? SettingsView => settingsView;

    private IApplet? Showing => tools.FirstOrDefault(t => t.Id == view.Current)?.Applet;

    /// <summary>Shows a tool's sub-tab.</summary>
    public void Select(string id) => view.Select(id);

    /// <summary>The group's tool with that id, or null (also for one whose sub-tab is hidden).</summary>
    public IApplet? ToolFor(string id) =>
        hidden.Contains(id) ? null : tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Applet;

    private readonly HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Shows or hides a tool's sub-tab on the tab, for a tool switched off by a setting of its own (Network's Zabbix). Its
    /// page under Settings stays, where it can be switched back on.
    /// </summary>
    protected void ShowTool(string id, bool shown)
    {
        if (shown) hidden.Remove(id); else hidden.Add(id);
        view.SetShown(id, shown);
    }

    private void Each(string what, Action<IApplet> action)
    {
        foreach (var t in tools)
        {
            try { action(t.Applet); }
            catch (Exception ex)
            {
                GroupLog.Error($"{t.Id}: {what} failed", ex);
                ErrorLog.Write($"{t.Id} {what}", ex);
            }
        }
    }

    public void Start()
    {
        foreach (var t in tools)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                t.Applet.Start();
                GroupLog.Info($"{t.Id} started in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                GroupLog.Error($"{t.Id} couldn't start", ex);
                ErrorLog.Write($"{t.Id}.Start", ex);
            }
        }
    }

    public void OnPreviewKeyDown(KeyEventArgs e) => Showing?.OnPreviewKeyDown(e);

    public void OnWindowHidden() => Each("window hidden", a => a.OnWindowHidden());

    public void SaveSettings()
    {
        JsonStore.Save(fileName, settings);
        Each("saving settings", a => a.SaveSettings());
    }

    public bool NeedsOnboarding => tools.Any(t => t.Applet.NeedsOnboarding);

    public void RunOnboarding(Window owner)
    {
        foreach (var t in tools)
            if (t.Applet.NeedsOnboarding) t.Applet.RunOnboarding(owner);
    }

    /// <summary>The tools' setup wizard pages, one after another (null if none has one).</summary>
    public FrameworkElement? SetupPage => Pages(tools.Select(t => t.Applet.SetupPage));

    /// <summary>Some tool has a setting it can't do without missing.</summary>
    public bool SetupRequired => tools.Any(t => { try { return t.Applet.SetupRequired; } catch { return false; } });

    /// <summary>The pages of the tools that need setting up, one after another.</summary>
    public FrameworkElement? RequiredSetupPage => Pages(tools.Select(t => t.Applet.RequiredSetupPage));

    private static FrameworkElement? Pages(IEnumerable<FrameworkElement?> all)
    {
        var pages = all.OfType<FrameworkElement>().ToList();
        if (pages.Count <= 1) return pages.FirstOrDefault();
        var panel = new StackPanel();
        foreach (var p in pages)
        {
            p.Margin = new Thickness(0, 0, 0, 18);
            panel.Children.Add(p);
        }
        return panel;
    }

    /// <summary>Each tool's tray items: as they are if only one tool has some, else a submenu per tool.</summary>
    public IReadOnlyList<AppletMenuItem>? TrayMenu
    {
        get
        {
            var menus = new List<(Tool Tool, IReadOnlyList<AppletMenuItem> Items)>();
            foreach (var t in tools)
            {
                IReadOnlyList<AppletMenuItem>? items = null;
                try { items = t.Applet.TrayMenu; } catch { }
                if (items is { Count: > 0 }) menus.Add((t, items));
            }
            if (menus.Count == 0) return null;
            if (menus.Count == 1) return menus[0].Items;
            return menus.Select(m => new AppletMenuItem(m.Tool.Title, Children: m.Items)).ToList();
        }
    }

    public void Dispose()
    {
        try { JsonStore.Save(fileName, settings); } catch { }
        Each("shutting down", a => a.Dispose());
    }
}
