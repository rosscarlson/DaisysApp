using System.Windows;
using System.Windows.Interop;
using DaisysApp.Shell;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// ForzaWheel: Forza EmuWheel's three parts in one place. The emulator (a wheel, pedals, shifter, handbrake and button
/// boxes as one vJoy controller), Hush (mutes the real devices for Forza, as EmuWheel's Hush.exe) and the binding tool
/// (what drives what, saved as JSON). It checks the vJoy device is set up as Forza needs and can set it up. A module of
/// its own; in Daisy's App a sub-tab of Gaming with Joy2Key (../Joy2Key/GamingTab.cs). See README.md.
/// </summary>
[Applet("ForzaWheel", "ForzaWheel", "", Order = 56, Group = "GamingTools", GroupOrder = 20,
    Description = "Your wheel, pedals, shifter and button boxes as one controller for Forza")]
public sealed class ForzaWheelApplet : IApplet
{
    /// <summary>The tool was WheelEmu until 0.58: the Gaming tab's remembered sub-tab follows its new id.</summary>
    static ForzaWheelApplet()
    {
        try
        {
            var path = DaisysApp.Settings.JsonStore.PathFor("GamingTools");
            if (System.IO.File.Exists(path) && System.IO.File.ReadAllText(path) is { } text && text.Contains("\"WheelEmu\""))
                System.IO.File.WriteAllText(path, text.Replace("\"WheelEmu\"", "\"ForzaWheel\""));
        }
        catch { }
    }

    private readonly ForzaWheelSettings settings = ForzaWheelSettings.Load();
    private readonly WheelEmulator emulator;
    private readonly ForzaWheelView view;
    private readonly Hotkey hotkey;
    // a hidden window of its own, which DirectInput reads controllers in the background for
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp ForzaWheel") { Width = 0, Height = 0, WindowStyle = 0 });

    public ForzaWheelApplet()
    {
        emulator = new WheelEmulator(settings) { RecordSessions = settings.Overlay.Record };
        emulator.SetWindow(window.Handle);
        // the start / stop shortcut: a key combination or a controller button, from anywhere (in the game too)
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "ForzaWheel.Toggle", Module = T("ForzaWheel"), Name = T("Start / stop the emulator"),
            Get = () => settings.ToggleHotkey, Set = v => { settings.ToggleHotkey = v; settings.Save(); },
        }, () =>
        {
            if (emulator.Running) emulator.Stop(); else emulator.Start();
            view?.Refresh();
        });
        view = new ForzaWheelView(settings, emulator, window.Handle, hotkey);
        // Auto Hush: starting the emulator mutes the bound devices, stopping it unmutes them (from the tab, the tray or
        // at launch alike)
        emulator.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(AutoHush);
        emulator.StateChanged += ShowBadge;
        emulator.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(Remember);
        view.BadgeChanged += ShowBadge;
        gameTimer.Tick += async (_, _) => await CheckGames();
    }

    private bool remembered;

    /// <summary>Keeps whether the emulator runs (started by hand, not by a game), so the next launch puts it back as it
    /// was: closing the app for an update leaves it running afterwards, and stopped stays stopped.</summary>
    private void Remember()
    {
        bool running = emulator.Running;
        if (running == remembered || closing) return;
        remembered = running;
        if (!running) startedByGame = false;
        bool keep = running && !startedByGame;
        if (keep == settings.WasRunning) return;
        settings.WasRunning = keep;
        settings.Save();
    }

    // ---- games that start it ----

    private readonly System.Windows.Threading.DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool gameRunning, startedByGame, checking;

    /// <summary>A game on the list starting starts the emulator; the last one closing stops it, if a game started it
    /// (one started or stopped by hand in between is left as it is).</summary>
    private async Task CheckGames()
    {
        if (checking || closing) return;
        var games = settings.Games.ToArray();
        if (games.Length == 0 && !gameRunning) return;
        checking = true;
        try
        {
            string? game = await Task.Run(() => RunningGame(games));
            if (closing) return;
            bool now = game != null;
            if (now == gameRunning) return;
            gameRunning = now;
            view.GameRunning = now;
            view.ShowOverlay();
            if (now)
            {
                Logging.Log.Here.Info($"ForzaWheel: {game} is running");
                if (emulator.Running) return;
                startedByGame = true;
                if (!emulator.Start()) startedByGame = false;
            }
            else
            {
                Logging.Log.Here.Info("ForzaWheel: no game on the list is running");
                if (startedByGame && emulator.Running) emulator.Stop();
                startedByGame = false;
            }
            view.Refresh();
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ForzaWheel games", ex); }
        finally { checking = false; }
    }

    private static string? RunningGame(string[] games)
    {
        static string Bare(string s)
        {
            s = System.IO.Path.GetFileName(s.Trim());
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        }
        var wanted = games.Select(Bare).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? found = null;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { if (found == null && wanted.Contains(p.ProcessName)) found = p.ProcessName + ".exe"; } catch { }
            p.Dispose();
        }
        return found;
    }

    /// <summary>The title bar's ForzaWheel bubble: while the emulator runs (unless switched off).</summary>
    private void ShowBadge() =>
        TitleBadges.Set("ForzaWheel", emulator.Running && settings.TitleBadge ? "ForzaWheel" : null, T("The ForzaWheel emulator is running"));

    private bool wasRunning, closing;

    private async void AutoHush()
    {
        bool running = emulator.Running;
        if (running == wasRunning || closing) return;
        wasRunning = running;
        if (!settings.AutoHush) return;
        try
        {
            // the bound devices that are plugged in, by their GameInput key
            var bound = settings.Config.Devices.Select(d => d.Device).ToHashSet();
            var keys = GameControllers.List().Where(c => bound.Contains(c.Instance)).Select(c => c.GameInputKey()).Distinct().ToList();
            Elevated.Outcome? result = null;
            if (running)
            {
                var mute = keys.Where(k => !GameInputRegistry.IsMuted(k)).ToList();
                bool register = !GameInputRegistry.VJoyIsWheel();
                if (mute.Count > 0 || register) result = await Elevated.RunAsync(GameInputRegistry.MuteCommands(mute, register));
            }
            else
            {
                var unmute = keys.Where(GameInputRegistry.IsMuted).ToList();
                if (unmute.Count > 0) result = await GameInputRegistry.UnmuteAsync(unmute);
            }
            if (result is { Ok: false }) Logging.Log.Here.Warn("ForzaWheel Auto Hush: " + result.Output);
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ForzaWheel Auto Hush", ex); }
        view.ShowHush();
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        view.ShowOverlay();
        // as it was when the app closed (or always, with Start it when Daisy's App starts)
        if (settings.StartWithApp || settings.WasRunning) emulator.Start();
        remembered = emulator.Running;
        gameTimer.Start();
        _ = CheckGames();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Wheel emulator running"), () => { if (emulator.Running) emulator.Stop(); else emulator.Start(); view.Refresh(); }) { Checked = emulator.Running },
    };

    public void Dispose()
    {
        closing = true; // closing the app leaves them muted rather than asking for permission on the way out (and
                        // the emulator remembered as running)
        gameTimer.Stop();
        view.CloseOverlay();
        hotkey.Dispose();
        settings.Save();
        emulator.Dispose();
        window.Dispose();
    }
}
