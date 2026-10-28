using System.Windows;
using System.Windows.Threading;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared.Widgets;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// SimEffects → Analysis (since 3.23.1): the profile's game's car, tyres and race as blocks (Vehicle, Tires, Laps), each
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
    private long frames, used, lastFrameAt;
    private bool live;

    public AnalysisApplet()
    {
        tracker = new LapTracker(settings, store);
        SimProfiles.Register(new ToolScope
        {
            Id = "Analysis", Title = T("Analysis"), Settings = settings, Props = AnalysisSettings.Scoped,
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = ProfileLoaded,
            OnAtFirst = p => p.Game == Game.ForzaHorizon,
        });
        tracker.Recording = settings.On;
        tracker.NewTrack += t => view!.AskName(t);
        view = new AnalysisView(this);
        timer.Tick += (_, _) => Tick();
        view.IsVisibleChanged += (_, _) => Reading();
    }

    internal AnalysisSettings Settings => settings;
    internal LapStore Store => store;
    internal LapTracker Tracker => tracker;

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
        Reading();
        view.ShowOnOff();
    }

    /// <summary>What the blocks show now.</summary>
    internal Reading Now()
    {
        var f = Environment.TickCount64 - lastFrameAt < 3000 ? shown : null;
        string? track = tracker.Track is { } t ? settings.TrackName(t.Id) : null;
        float pb = f is { HasRace: true } ? store.Best(tracker.Track?.Id, f.CarOrdinal, f.CarClass) : 0;
        return new Reading(f, settings, track, pb);
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
        bool want = settings.On || view.IsVisible || widgets.Count > 0;
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
                latest.CopyTo(shown);
                lastFrameAt = Environment.TickCount64;
                live = shown.Live;
                tracker.Feed(shown);
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
        var body = new BlockView(block, place.Hidden, settings.Save, title: true);
        var window = new Widget(place, settings.Save, () => RemoveWidget(block), firstSize: new Size(280, 60 + block.Rows.Length * 20))
        {
            Title = F("Analysis: {0}", block.Title), MinWidth = 120, Body = body,
        };
        window.EditingChanged += () => body.Editing = window.Editing;
        widgets[block.Id] = (window, body);
        window.Show();
        body.Update(Now());
        if (edit) window.BeginEdit();
        Reading();
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
    public FrameworkElement? SettingsView => null;

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
        foreach (var (window, _) in widgets.Values) window.Close();
        widgets.Clear();
        quit = true;
        wake.Set();
        settings.Save();
    }
}
