using System.Windows;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// SimEffects → Analysis (since 3.23.1): the profile's game's car, tyres and race as blocks (Vehicle, Tires, Laps, and
/// since 3.24.0 Performance, Timing, Position and Lap), each
/// of which can be a widget (Core's <see cref="Widget"/>) with the rows it shows ticked while it's edited; and, while
/// it's on, every lap recorded by track, car and class (<see cref="LapTracker"/>, <see cref="LapStore"/>), with the
/// stats on the tab. Forza Horizon's data for now (Motorsport's has the same, but CarGroup). On / off is kept per
/// SimEffects profile (on in Forza Horizon's at first). See README.md.
/// </summary>
public sealed class AnalysisApplet : IApplet
{
    private readonly AnalysisSettings settings = AnalysisSettings.Load();
    private readonly LapStore store = new();
    private readonly LapTracker tracker;
    private readonly AnalysisView view;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string, (Widget Window, BlockView Body)> widgets = new();
    private readonly object gate = new();
    private readonly TelemetryFrame latest = new(), shown = new();
    private readonly AutoResetEvent wake = new(false);
    private Thread? thread;
    private volatile bool wanted, quit;
    private ITelemetrySource? source;
    private readonly PerfHistory history = new();
    private readonly AnalysisSettingsView settingsView;
    private readonly Hotkey hotkey;
    private readonly Dictionary<string, Hotkey> widgetKeys = new();
    private long frames, used, lastFrameAt;
    private bool live, driven;

    public AnalysisApplet()
    {
        tracker = new LapTracker(settings, store);
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "Analysis.OnOff", Module = T("Analysis"), Name = T("On / off"),
            Get = () => settings.OnOffKey, Set = v => { settings.OnOffKey = v; settings.Save(); },
        }, () => SetEnabled(!settings.Enabled));
        SimProfiles.Register(new ToolScope
        {
            Id = "Analysis", Title = T("Analysis"), Settings = settings, Props = AnalysisSettings.Scoped,
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = ProfileLoaded,
            OnAtFirst = p => p.Game == Game.ForzaHorizon,
        });
        // (since 3.27.1) each block's widget on / off from a shortcut
        foreach (var b in Blocks.All)
        {
            var block = b;
            widgetKeys[b.Id] = Hotkeys.Add(new HotkeySpec
            {
                Id = "Analysis.Widget." + b.Id, Module = T("Analysis"), Target = b.Title, Name = T("Widget on / off"),
                Get = () => settings.Widgets.TryGetValue(block.Id, out var w) ? w.Key : null,
                Set = v => { settings.Widget(block.Id).Key = v; settings.Save(); },
            }, () => ToggleWidget(block));
        }
        tracker.Recording = settings.On;
        tracker.NewTrack += t => DaisysApp.Shell.Notifications.Show(T("Analysis"), F("A new track: {0} (name it in Discovered Tracks)", settings.TrackName(t.Id)));
        view = new AnalysisView(this);
        // (since 3.28.0 the units are on the Profiles tab's Profile card)
        ProfileCard.UnitBoxes = () => AnalysisSettingsView.UnitBoxes(this);
        settingsView = new AnalysisSettingsView(this);
        timer.Tick += (_, _) => Tick();
        view.IsVisibleChanged += (_, _) => Reading();
    }

    internal AnalysisSettings Settings => settings;
    internal LapStore Store => store;
    internal LapTracker Tracker => tracker;
    internal Hotkey Hotkey => hotkey;
    internal Hotkey WidgetKey(Block block) => widgetKeys[block.Id];

    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        Logging.Log.Here.Info($"Analysis: switched {(on ? "on" : "off")}");
        settings.Save();
        Apply();
    }

    private void ProfileLoaded() => Apply();

    private void Apply()
    {
        tracker.Recording = settings.On;
        // (the widgets show while Analysis is on and the profile plays)
        foreach (var (window, _) in widgets.Values) window.Showing = settings.On;
        Reading();
        view.ShowOnOff();
    }

    /// <summary>What the blocks show now: the last frame driving, held while the game is paused or gone.</summary>
    internal Reading Now()
    {
        var f = driven ? shown : null;
        string? track = tracker.Track is { } t ? settings.TrackName(t.Id) : null;
        float pb = 0, session = 0, cls = 0;
        if (f is { HasRace: true })
        {
            // PB: this car on this track (any class it's been built to); PBC: any car of its class there
            pb = store.Best(tracker.Track?.Id, f.CarOrdinal, null);
            cls = store.Best(tracker.Track?.Id, null, f.CarClass);
            session = store.Best(tracker.Track?.Id, f.CarOrdinal, f.CarClass, session: true);
            // (Forza's own best of this race, kept or not)
            if (f.BestLap > 0 && (session == 0 || f.BestLap < session)) session = f.BestLap;
        }
        return new Reading(f, CarNames.Edition, settings, track, pb, session, tracker.Racers, tracker.TotalLaps, history, cls, tracker.Progress);
    }

    private static string GameNow => CarNames.Edition ?? LapStore.OldGame;

    /// <summary>The place the car is, saved as a track with this name (the tab's Tag box, since 3.27.1; the Name button
    /// before); null: no place from the game.</summary>
    internal Track? NameHere(string name) => tracker.NameHere(driven ? shown : null, GameNow, name);

    /// <summary>The track the car is at (its race's, or the one starting nearest it) and how far from its start (null: none,
    /// or no place).</summary>
    internal (Track Track, float Metres)? Here() => tracker.Here(driven ? shown : null, GameNow);

    /// <summary>The track starting nearest the car, and how far (null: none within the radius, or no place).</summary>
    internal (Track Track, float Metres)? Nearest() => tracker.Nearest(driven ? shown : null, GameNow);

    /// <summary>The units changed (the tab or Settings): everything shown again.</summary>
    internal void UnitsChanged()
    {
        settings.Save();
        view.UnitsChanged();
        settingsView.Refresh();
        Tick();
    }

    /// <summary>What the status line says about the game.</summary>
    internal (string Text, bool Receiving) Status()
    {
        bool receiving = Environment.TickCount64 - lastFrameAt < 3000;
        string text = SimData.Status(source, receiving, live);
        if (receiving && !shown.HasRace) text += " · " + T("no race data from this game");
        return (text, receiving);
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Reads while it records, the tab shows or a widget does.</summary>
    internal void Reading()
    {
        bool want = settings.On || view.IsVisible || widgets.Values.Any(w => w.Window.IsVisible);
        wanted = want;
        if (want)
        {
            if (thread == null)
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "DaisysApp-Analysis" };
                thread.Start();
            }
            timer.Start();
        }
        else timer.Stop();
        wake.Set();
    }

    private void Loop()
    {
        var frame = new TelemetryFrame();
        try
        {
            while (!quit)
            {
                if (!wanted)
                {
                    source?.Dispose();
                    source = null;
                    wake.WaitOne(1000);
                    continue;
                }
                source ??= SimData.Open();
                if (!source.Read(frame, 200)) continue;
                lock (gate)
                {
                    frame.CopyTo(latest);
                    frames++;
                }
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Analysis", ex); }
        finally
        {
            source?.Dispose();
            source = null;
        }
    }

    private void Tick()
    {
        lock (gate)
        {
            if (frames != used)
            {
                used = frames;
                lastFrameAt = Environment.TickCount64;
                live = latest.Live;
                // paused, in a menu or a replay: what was shown stays (the game sends zeros then)
                if (live || !driven)
                {
                    latest.CopyTo(shown);
                    if (live) driven = true;
                    if (live && shown.HasRace) history.Add(shown);
                }
                tracker.Feed(latest, CarNames.Edition ?? LapStore.OldGame);
            }
        }
        var now = Now();
        view.Tick(now);
        foreach (var (_, body) in widgets.Values) body.Update(now);
    }

    // ---------------------------------------------------------------- widgets

    /// <summary>A block's widget, made (being edited, so it can be placed) or edited again.</summary>
    internal void MakeWidget(Block block)
    {
        if (widgets.TryGetValue(block.Id, out var w)) { w.Window.BeginEdit(); return; }
        ShowWidget(block, edit: true);
    }

    private void ShowWidget(Block block, bool edit = false)
    {
        var place = settings.Widget(block.Id);
        place.Show = true;
        settings.Save();
        var body = new BlockView(block, place.Hidden, settings.Save);
        var title = Ui.Header(block.Title);
        title.Margin = new Thickness(0, 0, 0, 2);
        // (its own card: the title can be hidden and the background made see-through; Timing and Position start without
        // a title, their captions say what they are and the room goes to the numbers)
        var (window, _) = OverlayFrame.Make(place, settings.Save, () => RemoveWidget(block), block.WidgetSize, body, title,
            titleAtFirst: block.Kind is not (BlockKind.Timing or BlockKind.Big));
        window.Title = F("Analysis: {0}", block.Title);
        window.MinWidth = 120;
        window.EditingChanged += () => { body.Editing = window.Editing; Reading(); };
        widgets[block.Id] = (window, body);
        // (off or stopped: hidden until it's on and the profile plays, unless it's being made)
        window.Showing = settings.On;
        body.Update(Now());
        if (edit) window.BeginEdit();
        Reading();
    }

    /// <summary>A block's shortcut: its widget shown (where it was) or closed (its place and rows kept).</summary>
    private void ToggleWidget(Block block)
    {
        if (widgets.ContainsKey(block.Id)) RemoveWidget(block);
        else
        {
            ShowWidget(block);
            if (!settings.On) DaisysApp.Shell.Notifications.Show(T("Analysis"), F("{0}: shows while Analysis is on and the profile plays", block.Title));
        }
    }

    private void RemoveWidget(Block block)
    {
        settings.Widget(block.Id).Show = false;
        settings.Save();
        if (widgets.Remove(block.Id, out var w)) w.Window.Close();
        Reading();
    }

    // ---------------------------------------------------------------- IApplet

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        foreach (var b in Blocks.All)
            if (settings.Widgets.TryGetValue(b.Id, out var w) && w.Show) ShowWidget(b);
        Apply();
        view.Render();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Record laps"), () => SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        timer.Stop();
        hotkey.Dispose();
        foreach (var k in widgetKeys.Values) k.Dispose();
        foreach (var (window, _) in widgets.Values) window.Close();
        widgets.Clear();
        quit = true;
        wake.Set();
        settings.Save();
    }
}
