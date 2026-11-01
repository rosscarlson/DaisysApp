using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using DaisysApp.Applets.SimEffects.Analysis;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.InputOverlay;

/// <summary>The kinds of overlay: Inputs, Dash, and Analysis' blocks (by their ids).</summary>
public static class OverlayKinds
{
    public const string Inputs = "Inputs", Dash = "Dash";

    /// <summary>Every kind, in the order New offers them (the blocks only while Analysis is on).</summary>
    public static IEnumerable<string> All(bool analysis) =>
        new[] { Inputs, Dash }.Concat(analysis ? Blocks.All.Select(b => b.Id) : Enumerable.Empty<string>());

    public static string Name(string id) => id switch
    {
        Inputs => T("Inputs"), Dash => T("Dash"), _ => Blocks.ById(id)?.Title ?? id,
    };
}

/// <summary>
/// SimEffects → Overlays (since 3.33.0; the Input Overlay before, ForzaWheel's until 3.17.0): widgets over the game,
/// each made, edited and deleted in the tab's list: <b>Inputs</b> (the pedals' and wheel's last few seconds, and the
/// speed), the <b>Dash</b> (a tach, the gear and speed, TCS and ABS), and Analysis' blocks (Tires, Performance, Timing,
/// Position, Leaderboard; their widgets were made on the Analysis tab before, and still read from there). They show
/// while the tool is on (kept per SimEffects profile, as its shortcut and title bar bubble say) and the profile plays,
/// each one that's ticked. Keeps InputOverlay.json (the blocks' places stay in Analysis.json). See README.md.
/// </summary>
public sealed class InputOverlayApplet : IApplet
{
    private readonly OverlaySettings settings = OverlaySettings.Load();
    private readonly OverlayReader reader;
    private readonly InputOverlayView view;
    private readonly Hotkey hotkey, dashKey;
    private readonly Dictionary<string, Hotkey> blockKeys = new();
    // a hidden window of its own, which DirectInput reads controllers in the background for
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp Input Overlay") { Width = 0, Height = 0, WindowStyle = 0 });
    private OverlayWindow? overlay;
    private DashWindow? dash;
    private readonly Dictionary<string, (Widget Window, BlockView Body)> blocks = new();
    private AnalysisApplet? analysis;

    public InputOverlayApplet()
    {
        reader = new OverlayReader(settings, window.Handle);
        if (settings.Overlays == null) FromBefore();
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "InputOverlay.OnOff", Module = T("Overlays"), Name = T("On / off"),
            Get = () => settings.OnOffKey, Set = v => { settings.OnOffKey = v; settings.Save(); },
        }, () => SetEnabled(!settings.Enabled));
        dashKey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Overlays.Dash", Module = T("Overlays"), Target = T("Dash"), Name = T("Overlay on / off"),
            Get = () => settings.Dash.Key, Set = v => { settings.Dash.Key = v; settings.Save(); },
        }, () => Toggle(OverlayKinds.Dash));
        SimProfiles.Register(new ToolScope
        {
            Id = "InputOverlay", Title = T("Overlays"), Settings = settings, Props = OverlaySettings.Scoped,
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = ProfileLoaded, OnWithGames = settings.SwitchOnWithGame,
        });
        view = new InputOverlayView(this);
        SimProfiles.Changed += ShowBadge;
    }

    /// <summary>
    /// The first time since 3.33.0: the list is Inputs and the Analysis blocks' widgets that were showing. Those showed
    /// while Analysis was on; now while Overlays is: on in each profile Analysis was on in, without Inputs where the
    /// Input Overlay was off there.
    /// </summary>
    private void FromBefore()
    {
        var shown = new List<string>();
        try
        {
            var old = AnalysisSettings.Load();
            shown = Blocks.All.Where(b => old.Widgets.TryGetValue(b.Id, out var w) && w.Show).Select(b => b.Id).ToList();
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Overlays: Analysis' widgets", ex); }
        settings.Overlays = new List<string> { OverlayKinds.Inputs };
        settings.Overlays.AddRange(shown);
        static bool On(JsonObject? o) => o?["Enabled"] is JsonValue v && v.TryGetValue(out bool b) && b;
        if (shown.Count > 0)
            foreach (var p in SimProfiles.All)
            {
                if (!p.Tools.TryGetValue("InputOverlay", out var mine) || On(mine)) continue;
                if (!On(p.Tools.TryGetValue("Analysis", out var a) ? a : null)) continue;
                mine["Enabled"] = true;
                mine["Inputs"] = false;
            }
        settings.Save();
        Logging.Log.Here.Info($"Overlays: made from the Input Overlay and {shown.Count} of Analysis' widgets");
    }

    internal OverlaySettings Settings => settings;
    internal OverlayReader Reader => reader;
    internal Hotkey Hotkey => hotkey;
    internal IntPtr Window => window.Handle;
    internal OverlayWindow? Overlay => overlay;
    internal AnalysisApplet? Analysis => analysis;

    /// <summary>An overlay's on / off shortcut (none for Inputs: the tool's own).</summary>
    internal Hotkey? KeyOf(string id) => id == OverlayKinds.Dash ? dashKey : blockKeys.GetValueOrDefault(id);

    /// <summary>On or off (ON / OFF, the shortcut, the tray, the Profiles tab): kept in the profile in use.</summary>
    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        Logging.Log.Here.Info($"Overlays: switched {(on ? "on" : "off")}");
        settings.Save();
        ShowOverlays();
        ShowBadge();
        view.ShowOnOff();
    }

    /// <summary>Another SimEffects profile is in use, or it plays or stops: its settings shown, the overlays as it has them.</summary>
    private void ProfileLoaded()
    {
        reader.Changed();
        ShowOverlays();
        ShowBadge();
        view.Render();
    }

    /// <summary>The source or an axis changed: saved, read again, drawn again.</summary>
    internal void Changed()
    {
        settings.Save();
        reader.Changed();
        ShowBadge();
        view.Render();
    }

    internal void ShowBadge() =>
        TitleBadges.Set("InputOverlay", settings.On ? F("Overlay: {0}", SimProfiles.Active.Name) : null, T("The overlays are on"));

    // ---------------------------------------------------------------- the list

    internal IReadOnlyList<string> Made => settings.Overlays ??= new List<string>();

    /// <summary>The kinds not made yet, that New offers.</summary>
    internal IEnumerable<string> CanMake => OverlayKinds.All(analysis != null).Where(k => !Made.Contains(k));

    /// <summary>Ticked in the list: it shows while the tool's on and the profile plays.</summary>
    internal bool IsTicked(string id) => id switch
    {
        OverlayKinds.Inputs => settings.Inputs,
        OverlayKinds.Dash => settings.Dash.Show,
        _ => analysis?.Settings.Widgets.TryGetValue(id, out var w) == true && w.Show,
    };

    internal void SetTicked(string id, bool on)
    {
        if (id == OverlayKinds.Inputs) settings.Inputs = on;
        else if (id == OverlayKinds.Dash) settings.Dash.Show = on;
        else if (analysis != null) { analysis.Settings.Widget(id).Show = on; analysis.Settings.Save(); }
        settings.Save();
        Logging.Log.Here.Info($"Overlays: {id} {(on ? "ticked" : "unticked")}");
        ShowOverlays();
        view.ShowList();
    }

    private void Toggle(string id)
    {
        if (!Made.Contains(id)) settings.Overlays!.Add(id);
        SetTicked(id, !IsTicked(id));
        if (IsTicked(id) && !settings.On)
            DaisysApp.Shell.Notifications.Show(T("Overlays"), F("{0}: shows while Overlays is on and the profile plays", OverlayKinds.Name(id)));
    }

    /// <summary>New: made, ticked, and shown to be placed.</summary>
    internal void Create(string id)
    {
        if (!Made.Contains(id)) settings.Overlays!.Add(id);
        SetTicked(id, true);
        Edit(id);
    }

    /// <summary>Delete: out of the list and closed (its place is kept, for when it's made again).</summary>
    internal void Delete(string id)
    {
        settings.Overlays!.Remove(id);
        SetTicked(id, false);
        Close(id);
        ShowOverlays();
        view.ShowList();
    }

    /// <summary>Edit: shown (even while off) with its edit strip, to drag and stretch until Done.</summary>
    internal void Edit(string id)
    {
        if (WindowOf(id) is { Editing: true } editing) { editing.EndEdit(); return; }
        var w = Open(id);
        if (w == null) return;
        w.Showing = settings.On && IsTicked(id);
        w.BeginEdit();
        Reading();
    }

    internal bool IsEditing(string id) => WindowOf(id)?.Editing == true;

    /// <summary>Its look changed on the tab: made again.</summary>
    internal void Redraw(string id)
    {
        if (id == OverlayKinds.Inputs) { overlay?.Changed(); return; }
        if (id == OverlayKinds.Dash) { dash?.Changed(); return; }
        if (!blocks.ContainsKey(id)) return;
        Close(id);
        ShowOverlays();
    }

    internal void ResetPlace()
    {
        Close(OverlayKinds.Inputs);
        settings.X = settings.Y = 80;
        settings.Width = settings.Height = 0;
        settings.Save();
        ShowOverlays();
    }

    // ---------------------------------------------------------------- the windows

    private Widget? WindowOf(string id) => id switch
    {
        OverlayKinds.Inputs => overlay,
        OverlayKinds.Dash => dash,
        _ => blocks.TryGetValue(id, out var b) ? b.Window : null,
    };

    /// <summary>Its window, made if it isn't (hidden until <see cref="Widget.Showing"/>).</summary>
    private Widget? Open(string id)
    {
        if (WindowOf(id) is { } w) return w;
        Widget made;
        if (id == OverlayKinds.Inputs) made = overlay = new OverlayWindow(settings, reader, settings.Save, () => SetTicked(id, false));
        else if (id == OverlayKinds.Dash) made = dash = new DashWindow(settings, reader, settings.Save, () => SetTicked(id, false));
        else if (analysis != null && Blocks.ById(id) is { } block) made = MakeBlock(block, analysis);
        else return null;
        made.Showing = false;
        made.EditingChanged += () =>
        {
            view.ShowList();
            Reading();
            if (!made.Editing) Application.Current?.Dispatcher.BeginInvoke(ShowOverlays);
        };
        return made;
    }

    private Widget MakeBlock(Block block, AnalysisApplet a)
    {
        var place = a.Settings.Widget(block.Id);
        var body = new BlockView(block, place.Hidden, a.Settings.Save);
        var title = Ui.Header(block.Title);
        title.Margin = new Thickness(0, 0, 0, 2);
        // (its own card: the title can be hidden and the background made see-through; Timing and Position start without
        // a title, their captions say what they are and the room goes to the numbers)
        var (window, _) = OverlayFrame.Make(place, a.Settings.Save, () => SetTicked(block.Id, false), block.WidgetSize, body, title,
            titleAtFirst: block.Kind is not (BlockKind.Timing or BlockKind.Big));
        window.Title = F("Overlay: {0}", block.Title);
        window.MinWidth = 120;
        window.EditingChanged += () => body.Editing = window.Editing;
        blocks[block.Id] = (window, body);
        body.Update(a.Now());
        return window;
    }

    private void Close(string id)
    {
        if (id == OverlayKinds.Inputs) { overlay?.Close(); overlay = null; }
        else if (id == OverlayKinds.Dash) { dash?.Close(); dash = null; }
        else if (blocks.Remove(id, out var b)) b.Window.Close();
    }

    /// <summary>Each overlay made and ticked open (showing while the tool's on and the profile plays), the rest closed
    /// (but one being edited).</summary>
    internal void ShowOverlays()
    {
        foreach (var id in OverlayKinds.All(true))
        {
            bool want = Made.Contains(id) && IsTicked(id);
            var w = WindowOf(id) ?? (want ? Open(id) : null);
            if (w == null) continue;
            if (!want && !w.Editing) { Close(id); continue; }
            w.Showing = want && settings.On;
        }
        reader.Recording = overlay is { IsVisible: true };
        Reading();
        view.ShowList();
    }

    /// <summary>The reader runs while the tab or an overlay shows; Analysis reads while one of its blocks does.</summary>
    internal void Reading()
    {
        bool Shown(Widget? w) => w != null && (w.IsVisible || w.Editing);
        reader.Wanted = view.IsVisible || Shown(overlay) || Shown(dash);
        analysis?.Reading();
    }

    private bool BlocksShowing() => blocks.Values.Any(b => b.Window.IsVisible || b.Window.Editing);

    private void Tick(Reading r)
    {
        foreach (var (_, body) in blocks.Values) body.Update(r);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        // (Analysis is made by now, if it's switched on: its blocks are overlays here)
        analysis = AnalysisApplet.Current;
        if (analysis != null)
        {
            var a = analysis;
            a.Wanted = BlocksShowing;
            a.Ticked += Tick;
            // (since 3.27.1, on the Analysis tab until 3.33.0) each block's overlay on / off from a shortcut
            foreach (var b in Blocks.All)
            {
                var block = b;
                blockKeys[b.Id] = Hotkeys.Add(new HotkeySpec
                {
                    Id = "Analysis.Widget." + b.Id, Module = T("Overlays"), Target = b.Title, Name = T("Overlay on / off"),
                    Get = () => a.Settings.Widgets.TryGetValue(block.Id, out var w) ? w.Key : null,
                    Set = v => { a.Settings.Widget(block.Id).Key = v; a.Settings.Save(); },
                }, () => Toggle(block.Id));
            }
        }
        ShowOverlays();
        ShowBadge();
        view.Render();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Overlays on"), () => SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        SimProfiles.Changed -= ShowBadge;
        hotkey.Dispose();
        dashKey.Dispose();
        foreach (var k in blockKeys.Values) k.Dispose();
        if (analysis != null)
        {
            analysis.Ticked -= Tick;
            analysis.Wanted = null;
        }
        foreach (var id in OverlayKinds.All(true)) Close(id);
        TitleBadges.Set("InputOverlay", null);
        view.Stop();
        reader.Dispose();
        settings.Save();
        window.Dispose();
    }
}
