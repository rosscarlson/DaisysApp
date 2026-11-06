using System.Windows;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shared.Widgets;
using DaisysApp.Shell;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// SimEffects → Analysis (since 3.23.1): the profile's game's car, tyres and race as blocks (Tires, and since 3.24.0
/// Performance, Timing and Position; since 3.28.2 Leaderboard, iRacing's field; Vehicle and Laps until 3.30.2), each
/// of which is an overlay on SimEffects → Overlays (since 3.33.0; its widget was made here before); and, while
/// it's on, every lap recorded by track, car and class (<see cref="LapTracker"/>, <see cref="LapStore"/>), with the
/// stats on the tab. Forza Horizon's data for now (Motorsport's has the same, but CarGroup). On / off is kept per
/// SimEffects profile (on in Forza Horizon's at first). See README.md.
/// </summary>
public sealed class AnalysisApplet : IApplet
{
    private readonly AnalysisSettings settings = AnalysisSettings.Load();
    private readonly LapStore store = new();
    private readonly LapTracker tracker;
    /// <summary>(since 4.5.0) A session log a race, for the race analysis on the tab.</summary>
    private readonly RaceLog raceLog = new();
    private readonly AnalysisView view;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly object gate = new();
    private readonly TelemetryFrame latest = new(), shown = new();
    private readonly AutoResetEvent wake = new(false);
    private Thread? thread;
    private volatile bool wanted, quit;
    private ITelemetrySource? source;
    private readonly PerfHistory history = new();
    private readonly AnalysisSettingsView settingsView;
    private readonly Hotkey hotkey;
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
        tracker.Recording = raceLog.On = settings.On;
        tracker.Claim = raceLog.Claim;
        tracker.NewTrack += t => DaisysApp.Shell.Notifications.Show(T("Analysis"), F("A new track: {0} (name it in Discovered Tracks)", settings.TrackName(t.Id)));
        view = new AnalysisView(this);
        // (since 3.28.0 the units are on the Profiles tab's Profile card)
        ProfileCard.UnitBoxes = () => AnalysisSettingsView.UnitBoxes(this);
        settingsView = new AnalysisSettingsView(this);
        timer.Tick += (_, _) => Tick();
        view.IsVisibleChanged += (_, _) => Reading();
        Current = this;
    }

    /// <summary>The one made (since 3.33.0 the blocks' widgets are SimEffects → Overlays', which shows them from here);
    /// null while Analysis is switched off.</summary>
    internal static AnalysisApplet? Current { get; private set; }

    /// <summary>What the blocks show, ten times a second while it reads (the Overlays' block widgets).</summary>
    internal event Action<Reading>? Ticked;

    /// <summary>Something else wants it reading (the Overlays' block widgets showing).</summary>
    internal Func<bool>? Wanted { get; set; }

    internal AnalysisSettings Settings => settings;
    internal LapStore Store => store;
    internal LapTracker Tracker => tracker;
    internal Hotkey Hotkey => hotkey;

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
        tracker.Recording = raceLog.On = settings.On;
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
        if (receiving && !shown.HasRace && shown.Field == null) text += " · " + T("no race data from this game");
        return (text, receiving);
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Reads while it records, the tab shows or a widget does.</summary>
    internal void Reading()
    {
        bool want = settings.On || view.IsVisible || Wanted?.Invoke() == true;
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
                    raceLog.Idle();
                    source?.Dispose();
                    source = null;
                    wake.WaitOne(1000);
                    continue;
                }
                source ??= SimData.Open();
                if (!source.Read(frame, 200))
                {
                    raceLog.Idle();
                    continue;
                }
                raceLog.Feed(frame);
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
        Ticked?.Invoke(now);
    }

    // ---------------------------------------------------------------- IApplet

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
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
        if (Current == this) Current = null;
        quit = true;
        wake.Set();
        raceLog.Dispose();
        settings.Save();
    }
}
