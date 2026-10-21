using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Holds the module together, on the UI thread: the ButtScreens and the settings (BigButts, the server's), the
/// <see cref="PressEngine"/> and <see cref="ButtServer"/> (which get a fresh copy after every change), the editors and
/// pop-outs open, the title bar bubble, and the BigButt in use (a game's while it runs, if auto-switching is on).
/// </summary>
internal sealed class ButtHub : IButtHost, IDisposable
{
    private const int GameCheckMs = 2000;
    private static readonly Logging.Logger log = Logging.Log.Here;

    private readonly Dispatcher ui;
    private readonly Dictionary<string, EditorWindow> editors = new();
    private readonly Dictionary<string, PopOutWindow> popOuts = new();
    private readonly DispatcherTimer gameTimer = new() { Interval = TimeSpan.FromMilliseconds(GameCheckMs) };
    private bool checking;
    private string? lastGame;
    private bool gameChecked;

    public RemoteSettings Settings { get; }
    public List<ButtScreen> Screens { get; }
    public PressEngine Engine { get; } = new();
    public ButtServer Server { get; }

    /// <summary>Raised when ButtScreens, BigButts or the one in use change (the tab redraws).</summary>
    public event Action? Updated;

    public ButtHub(Dispatcher ui)
    {
        this.ui = ui;
        Settings = RemoteSettings.Load();
        Screens = ScreenStore.LoadAll();
        if (!Settings.BigButts.Any(b => b.Name.Equals(BigButt.DefaultName, StringComparison.OrdinalIgnoreCase)))
            Settings.BigButts.Insert(0, new BigButt { Name = BigButt.DefaultName, Screens = Screens.Select(s => s.Id).ToList() });
        if (Active == null) Settings.Active = BigButt.DefaultName;
        Server = new ButtServer(Settings, Engine, this, ui);
        Server.StatusChanged += ShowBadge;
        Engine.Enabled = Settings.Enabled;
        Engine.ShowScreen += (source, name) => ui.BeginInvoke(() => ShowScreen(source, name));
        gameTimer.Tick += (_, _) => CheckGames();
    }

    public void Start()
    {
        Publish();
        Server.Apply();
        gameTimer.Start();
        foreach (var (id, place) in Settings.PopOuts.ToList())
            if (place.Open && Screens.FirstOrDefault(s => s.Id == id) is { } s) PopOut(s);
    }

    public BigButt? Active => Settings.BigButts.FirstOrDefault(b => b.Name.Equals(Settings.Active, StringComparison.OrdinalIgnoreCase));

    public BigButt Default => Settings.BigButts.First(b => b.Name.Equals(BigButt.DefaultName, StringComparison.OrdinalIgnoreCase));

    public void Save() => Settings.Save();

    /// <summary>Hands the engine and the server the ButtScreens and BigButts as they are now.</summary>
    public void Publish()
    {
        Engine.SetScreens(Screens);
        Server.Publish(Screens, Settings, Settings.Active);
        Updated?.Invoke();
        ShowBadge();
    }

    // ---- on / off, the server ----

    public void SetEnabled(bool on)
    {
        Settings.Enabled = on;
        Settings.Save();
        Engine.Enabled = on;
        Publish();
    }

    /// <summary>The title bar bubble: while a device is connected (unless switched off).</summary>
    public void ShowBadge()
    {
        var clients = Server.Clients;
        TitleBadges.Set("Butt2KeyRemote", Settings.TitleBadge && Settings.Enabled && clients.Count > 0 ? T("Button2Key Remote") : null,
            clients.Count > 0 ? F("Button2Key Remote: {0} connected", string.Join(", ", clients.Select(c => c.Device))) : null);
    }

    public void DeviceSeen(SeenDevice device)
    {
        Settings.Devices.RemoveAll(d => d.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
        Settings.Devices.Insert(0, device);
        if (Settings.Devices.Count > 12) Settings.Devices.RemoveRange(12, Settings.Devices.Count - 12);
        Settings.Save();
        Updated?.Invoke();
    }

    // ---- ButtScreens ----

    public ButtScreen Add(string name, ButtScreen? copyOf = null)
    {
        var s = copyOf?.Clone() ?? new ButtScreen();
        s.Id = new ButtScreen().Id;
        s.Name = ScreenStore.FreeName(name, Screens);
        s.Version = 0;
        if (copyOf != null) foreach (var b in s.Buttons) b.Id = new ButtButton().Id;
        Screens.Add(s);
        Screens.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        if (!Default.Screens.Contains(s.Id)) Default.Screens.Add(s.Id);
        Settings.Save();
        ScreenStore.Save(s);
        log.Info($"ButtScreen added: {s.Name}");
        Publish();
        return s;
    }

    /// <summary>A ButtScreen changed (the editor): saved, and everything that shows it told.</summary>
    public void Changed(ButtScreen s)
    {
        ScreenStore.Save(s);
        if (popOuts.TryGetValue(s.Id, out var w)) w.Redraw();
        Publish();
    }

    /// <summary>A ButtScreen's new name: buttons that showed it by its old name show it by the new one.</summary>
    public void Renamed(ButtScreen s, string old)
    {
        foreach (var other in Screens)
        {
            bool changed = false;
            foreach (var b in other.Buttons)
                if (b.Action is { Kind: ActionKind.Profile } a && string.Equals(a.Profile, old, StringComparison.CurrentCultureIgnoreCase)) { a.Profile = s.Name; changed = true; }
            if (changed && other != s) ScreenStore.Save(other);
        }
        Screens.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        if (popOuts.TryGetValue(s.Id, out var w)) w.Redraw();
        Publish();
    }

    public void Delete(ButtScreen s)
    {
        if (editors.TryGetValue(s.Id, out var e)) e.Close();
        if (popOuts.TryGetValue(s.Id, out var w)) w.Close();
        Settings.PopOuts.Remove(s.Id);
        foreach (var b in Settings.BigButts) b.Screens.Remove(s.Id);
        Screens.Remove(s);
        ScreenStore.Delete(s);
        ScreenStore.TidyImages(Screens);
        Settings.Save();
        log.Info($"ButtScreen deleted: {s.Name}");
        Publish();
    }

    public void Edit(ButtScreen s)
    {
        if (editors.TryGetValue(s.Id, out var open)) { open.Activate(); return; }
        var w = new EditorWindow(this, s);
        editors[s.Id] = w;
        w.Closed += (_, _) => { editors.Remove(s.Id); ScreenStore.TidyImages(Screens); };
        w.Show();
    }

    // ---- pop-outs ----

    public bool IsPoppedOut(ButtScreen s) => popOuts.ContainsKey(s.Id);

    public void PopOut(ButtScreen s)
    {
        if (popOuts.TryGetValue(s.Id, out var open)) { open.Show(); return; }
        if (!Settings.PopOuts.TryGetValue(s.Id, out var place)) Settings.PopOuts[s.Id] = place = new PopOutPlace();
        place.Open = true;
        Settings.Save();
        var w = new PopOutWindow(this, s, place);
        popOuts[s.Id] = w;
        w.Closed += (_, _) =>
        {
            // (a pop-out that switched to another ButtScreen is filed under that one)
            var shown = w.Screen;
            if (popOuts.TryGetValue(shown.Id, out var x) && x == w) popOuts.Remove(shown.Id);
            if (!closing && Settings.PopOuts.TryGetValue(shown.Id, out var p)) p.Open = false;
            Settings.Save();
            Updated?.Invoke();
        };
        w.Show();
        Updated?.Invoke();
    }

    public void ClosePopOut(ButtScreen s)
    {
        if (popOuts.TryGetValue(s.Id, out var w)) w.Close();
    }

    /// <summary>A pop-out now shows another ButtScreen (a Show a ButtScreen button): it's filed under that one, in the same place.</summary>
    public void Moved(PopOutWindow w, ButtScreen from, ButtScreen to)
    {
        popOuts.Remove(from.Id);
        if (popOuts.TryGetValue(to.Id, out var other) && other != w) other.Close();
        popOuts[to.Id] = w;
        var place = Settings.PopOuts.TryGetValue(from.Id, out var p) ? p : new PopOutPlace();
        Settings.PopOuts.Remove(from.Id);
        Settings.PopOuts[to.Id] = place;
        Settings.Save();
        Updated?.Invoke();
    }

    private void ShowScreen(string source, string name)
    {
        if (source.StartsWith("popout:"))
        {
            var target = Screens.FirstOrDefault(s => s.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            var w = popOuts.Values.FirstOrDefault(p => p.Source == source);
            if (target != null && w != null) w.Switch(target);
        }
        else Server.ShowScreen(source, name);
    }

    // ---- BigButts ----

    /// <summary>Makes a BigButt the one in use (picked in the tab, or by a device).</summary>
    public void UseBigButt(string name)
    {
        var b = Settings.BigButts.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (b == null || b.Name == Settings.Active) return;
        Settings.Active = b.Name;
        Settings.Save();
        log.Info($"BigButt in use: {b.Name}");
        Publish();
    }

    /// <summary>
    /// A game starting makes its BigButt the one in use; the game closing switches to Default. In between, one picked
    /// by hand stays (as Button2Key does with profiles). The running programs are read off the UI thread.
    /// </summary>
    private async void CheckGames()
    {
        if (checking || !Settings.AutoSwitch) return;
        checking = true;
        try
        {
            var lists = Settings.BigButts.Where(b => b.Programs.Count > 0).Select(b => (b.Name, Programs: b.Programs.ToList())).ToList();
            string? game = lists.Count == 0 ? null : await Task.Run(() => RunningGame(lists));
            if (gameChecked && string.Equals(game, lastGame, StringComparison.OrdinalIgnoreCase)) return;
            gameChecked = true;
            lastGame = game;
            log.Info(game != null ? $"Game running: BigButt {game}" : "No BigButt's game running: Default");
            UseBigButt(game ?? BigButt.DefaultName);
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Button2Key Remote: looking for games", ex); }
        finally { checking = false; }
    }

    private static string? RunningGame(List<(string Name, List<string> Programs)> lists)
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { running.Add(p.ProcessName); } catch { }
            p.Dispose();
        }
        var matching = lists.Where(l => l.Programs.Any(g => running.Contains(Bare(g)))).ToList();
        if (matching.Count == 0) return null;
        string? front = null;
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out int pid);
            using var p = Process.GetProcessById(pid);
            front = p.ProcessName;
        }
        catch { }
        return (front != null ? matching.FirstOrDefault(l => l.Programs.Any(g => Bare(g).Equals(front, StringComparison.OrdinalIgnoreCase))) : default).Name ?? matching[0].Name;
    }

    private static string Bare(string s)
    {
        s = System.IO.Path.GetFileName(s.Trim());
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(nint hwnd, out int pid);

    private bool closing;

    public void Dispose()
    {
        closing = true;
        gameTimer.Stop();
        foreach (var w in editors.Values.ToList()) w.Close();
        foreach (var w in popOuts.Values.ToList()) w.Close();
        Settings.Save();
        Server.Dispose();
        Engine.Dispose();
        TitleBadges.Set("Butt2KeyRemote", null);
    }
}
