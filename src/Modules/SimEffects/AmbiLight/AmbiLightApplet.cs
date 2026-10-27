using System.Windows;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Logging;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>
/// SimEffects → AmbiLight: what's on the displays, on Govee lights behind them (as Philips' Ambilight). A loop on its
/// own thread, while on or while the tab shows: every display's small picture (<see cref="ScreenSampler"/>), each
/// LED's average colour over its own spot in its zone (<see cref="Parts"/>), made more colourful, dark ones black, smoothed; sent
/// to the light (<see cref="GoveeLan"/>) while on. A light is also streamed to, while off, for its Test (all red),
/// Show Segments, and the zone being dragged on the tab (red); a stream that ends puts the light back as it was (its
/// own scene, and off again if it was off or didn't say). See README.md.
/// </summary>
public sealed class AmbiLightApplet : IApplet
{
    private readonly AmbiSettings settings = AmbiSettings.Load();
    private readonly ScreenSampler sampler = new();
    private readonly GoveeLan lan = new();
    private readonly AmbiLightView view;
    private readonly Hotkey hotkey;
    private readonly Thread thread;
    private volatile bool closing, previewing;
    private readonly object gate = new();
    private readonly Dictionary<AmbiLight, double[]> smooth = new();
    private readonly Dictionary<AmbiLight, (byte R, byte G, byte B)[]> shown = new(), ledsShown = new();
    private readonly Dictionary<AmbiLight, Streamed> streaming = new();
    private readonly Dictionary<AmbiLight, long> redUntil = new(), patternUntil = new(), ledsUntil = new();
    private (AmbiLight? Light, int Zone, long Until) held;
    private int frames;
    private long countFrom = Environment.TickCount64;

    private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);

    /// <summary>What was last sent to a light, and when; how it was before (to put it back).</summary>
    private sealed class Streamed
    {
        public byte[] Last = Array.Empty<byte>();
        public long SentAt, OnAt, AskedAt;
        public int Brightness = -1;
        /// <summary>Its answer came (or the wait ended): streaming can start.</summary>
        public volatile bool Ready;
        public bool? WasOn;
        public int WasBrightness = -1;
        /// <summary>Started while AmbiLight was off (a test, a zone being dragged).</summary>
        public bool Borrowed;
    }

    public AmbiLightApplet()
    {
        sampler.Off = new HashSet<string>(settings.DisplaysOff, StringComparer.OrdinalIgnoreCase);
        foreach (var l in settings.Lights)
        {
            // named by their model (3.9.0 called them all "Govee")
            if (l.Name == "Govee" && !string.IsNullOrEmpty(l.Sku)) l.Name = GoveeModels.Name(l.Sku);
            EnsureZones(l);
        }
        // the on / off shortcut: a key combination or a controller button, from anywhere
        hotkey = Hotkeys.Add(new HotkeySpec
        {
            Id = "AmbiLight.OnOff", Module = T("AmbiLight"), Name = T("On / off"),
            Get = () => settings.OnOffKey, Set = v => { settings.OnOffKey = v; settings.Save(); },
        }, () => SetEnabled(!settings.Enabled));
        SimProfiles.Register(new ToolScope
        {
            Id = "AmbiLight", Title = T("AmbiLight"), Settings = settings, Props = AmbiSettings.Scoped,
            IsOn = () => settings.Enabled, SetOn = SetEnabled, Loaded = ProfileLoaded, OnWithGames = settings.SwitchOnWithGame,
        });
        view = new AmbiLightView(this);
        ShowBadge();
        thread = new Thread(Loop) { IsBackground = true, Name = "AmbiLight" };
    }

    internal AmbiSettings Settings => settings;
    internal ScreenSampler Sampler => sampler;
    internal Hotkey Hotkey => hotkey;
    internal GoveeLan Lan => lan;
    /// <summary>Pictures a second the loop managed, over the last second or so.</summary>
    internal double Rate { get; private set; }

    /// <summary>Some light is showing its Test or segments.</summary>
    internal bool Testing
    {
        get
        {
            long now = Environment.TickCount64;
            lock (gate) return redUntil.Values.Any(t => t > now) || patternUntil.Values.Any(t => t > now) || ledsUntil.Values.Any(t => t > now);
        }
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        lan.Listen();
        thread.Start();
        if (settings.Enabled) _ = FindMovedAsync();
    }

    /// <summary>The tab shows: the pictures are taken for it even while off.</summary>
    internal bool Previewing { set => previewing = value; }

    /// <summary>Another SimEffects profile is in use: on or off, and its picture's settings, as it has them.</summary>
    private void ProfileLoaded()
    {
        if (settings.Enabled) _ = FindMovedAsync();
        ShowBadge();
        view.ShowOnOff();
        view.ShowPicture();
    }

    /// <summary>On or off (ON / OFF, the shortcut, the tray, the Profiles tab): kept in the profile in use.</summary>
    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        settings.Save();
        Log.Here.Info($"AmbiLight: switched {(on ? "on" : "off")}");
        if (on) _ = FindMovedAsync();
        ShowBadge();
        view.ShowOnOff();
    }

    /// <summary>The title bar's AmbiLight bubble: while it's on.</summary>
    internal void ShowBadge() =>
        TitleBadges.Set("AmbiLight", settings.Enabled ? "AmbiLight" : null, T("AmbiLight is on"));

    /// <summary>Lights whose address changed (found again by their MAC).</summary>
    private async Task FindMovedAsync()
    {
        if (!settings.Lights.Any(l => !string.IsNullOrEmpty(l.Mac))) return;
        try
        {
            foreach (var f in await lan.ScanAsync())
                foreach (var l in settings.Lights.Where(l => l.Mac == f.Mac && l.Ip != f.Ip))
                {
                    Log.Here.Info($"AmbiLight: {l.Describe} is at {f.Ip} now");
                    l.Ip = f.Ip;
                    settings.Save();
                }
        }
        catch (Exception ex) { Log.Here.Debug($"AmbiLight: scan at start: {ex.Message}"); }
    }

    /// <summary>A zone for each group of segments, on the ticked displays: laid out again when the count changed.</summary>
    internal void EnsureZones(AmbiLight light, bool again = false)
    {
        if (!again && light.Zones.Count == light.ZoneCount) return;
        var zones = Layouts.Make(light.Layout, light.ZoneCount, sampler.Used);
        lock (gate) light.Zones = zones;
    }

    /// <summary>A light changed (segments, zones, stretch…): saved, and sent in full next time.</summary>
    internal void Changed(AmbiLight? light = null)
    {
        settings.Save();
        lock (gate)
        {
            if (light != null) { smooth.Remove(light); shown.Remove(light); ledsShown.Remove(light); }
            foreach (var s in streaming.Values) s.Last = Array.Empty<byte>();
        }
    }

    internal void Removed(AmbiLight light)
    {
        lock (gate)
        {
            if (streaming.Remove(light, out var s)) PutBack(light, s);
            smooth.Remove(light);
            shown.Remove(light);
            ledsShown.Remove(light);
        }
        settings.Save();
    }

    /// <summary>A display ticked or unticked: the lights take colours only from ticked ones, and lay out on them.</summary>
    internal void SetDisplay(string name, bool on)
    {
        var off = new HashSet<string>(sampler.Off, StringComparer.OrdinalIgnoreCase);
        if (on) off.Remove(name); else off.Add(name);
        sampler.Off = off;
        settings.DisplaysOff = off.ToList();
        settings.Save();
        Log.Here.Info($"AmbiLight: display {name} {(on ? "used" : "left out")}");
    }

    /// <summary>Each segment a colour for 10 s: the first white, then red, green, blue over and over, to count them and
    /// see which end is first.</summary>
    internal void ShowSegments(AmbiLight light)
    {
        lock (gate) patternUntil[light] = Environment.TickCount64 + 10000;
    }

    /// <summary>Each LED sent a colour for 10 s (the first white, then red, green, blue): how finely the light shows them.</summary>
    internal void ShowLeds(AmbiLight light)
    {
        lock (gate) ledsUntil[light] = Environment.TickCount64 + 10000;
    }

    /// <summary>The whole light red for 2 s.</summary>
    internal void Test(AmbiLight light)
    {
        lock (gate) redUntil[light] = Environment.TickCount64 + 2000;
    }

    /// <summary>
    /// A light not added yet red for 2 s: how it is asked first; on (if it wasn't), streamed red, then streaming off
    /// (back to its own scene) and off again unless it said it was on.
    /// </summary>
    internal async Task TestFoundAsync(GoveeFound f)
    {
        if (settings.Lights.FirstOrDefault(l => l.Ip == f.Ip) is { } added) { Test(added); return; }
        var was = await lan.AskStatusAsync(f.Ip);
        Log.Here.Info($"AmbiLight: test of {GoveeModels.Name(f.Sku)} at {f.Ip} (it was {(was == null ? "silent" : was.On ? "on" : "off")})");
        if (was?.On != true) lan.Power(f.Ip, true);
        lan.Stream(f.Ip, true);
        for (int i = 0; i < 10; i++)
        {
            lan.Colours(f.Ip, new byte[] { 255, 0, 0 }, stretch: true);
            await Task.Delay(200);
        }
        lan.Stream(f.Ip, false);
        if (was?.On != true) lan.Power(f.Ip, false);
    }

    /// <summary>The zone being dragged on the tab (-1: let go): red on the light, which stays streamed 2 s after.</summary>
    internal void Hold(AmbiLight light, int zone)
    {
        lock (gate) held = (light, zone, Environment.TickCount64 + 2000);
    }

    /// <summary>Each zone's colour now (for the tab), or null before the first picture.</summary>
    internal (byte R, byte G, byte B)[]? ZoneColours(AmbiLight light)
    {
        lock (gate) return shown.TryGetValue(light, out var c) ? c : null;
    }

    /// <summary>Each LED's colour now (in the light's order), for the tab's slices.</summary>
    internal (byte R, byte G, byte B)[]? LedColours(AmbiLight light)
    {
        lock (gate) return ledsShown.TryGetValue(light, out var c) ? c : null;
    }

    internal bool Streaming(AmbiLight light)
    {
        lock (gate) return streaming.ContainsKey(light);
    }

    private void Loop()
    {
        long next = Environment.TickCount64;
        while (!closing)
        {
            try
            {
                var lights = settings.Lights.ToArray();
                long now = Environment.TickCount64;
                var wanted = lights.Where(l => Wants(l, now)).ToHashSet();
                StopUnwanted(wanted);
                if (wanted.Count > 0 || previewing)
                {
                    sampler.Capture();
                    foreach (var light in lights) Light(light, wanted.Contains(light), now);
                    Count();
                }
            }
            catch (Exception ex) { Log.Here.Warn("AmbiLight: the loop", ex); Thread.Sleep(1000); }
            // to a schedule, so Windows' 15.6 ms sleeps even out
            int fps = settings.Enabled || Busy ? Math.Clamp(settings.Fps, 10, 60) : previewing ? 10 : 5;
            long at = Environment.TickCount64;
            next = Math.Max(next + 1000 / fps, at - 100);
            Thread.Sleep((int)Math.Max(1, next - at));
        }
        StopAll();
    }

    /// <summary>Whether a light is streamed to now: AmbiLight on, or its Test, Show Segments or a zone dragged.</summary>
    private bool Wants(AmbiLight light, long now)
    {
        if (settings.Enabled) return true;
        lock (gate)
            return redUntil.TryGetValue(light, out long r) && r > now
                || patternUntil.TryGetValue(light, out long p) && p > now
                || ledsUntil.TryGetValue(light, out long q) && q > now
                || held.Light == light && (held.Zone >= 0 || held.Until > now);
    }

    private bool Busy
    {
        get
        {
            long now = Environment.TickCount64;
            lock (gate) return Testing || held.Light != null && (held.Zone >= 0 || held.Until > now);
        }
    }

    private void Count()
    {
        frames++;
        long now = Environment.TickCount64;
        if (now - countFrom < 1000) return;
        Rate = frames * 1000.0 / (now - countFrom);
        frames = 0;
        countFrom = now;
    }

    private void Light(AmbiLight light, bool on, long now)
    {
        AmbiZone[] zones;
        bool red, testing, showingLeds;
        int heldZone;
        lock (gate)
        {
            zones = light.Zones.ToArray();
            red = redUntil.TryGetValue(light, out long r) && r > now;
            testing = patternUntil.TryGetValue(light, out long p) && p > now;
            showingLeds = ledsUntil.TryGetValue(light, out long q) && q > now;
            heldZone = held.Light == light ? held.Zone : -1;
        }
        int n = zones.Length;
        if (n == 0) return;
        double keep = Math.Clamp(settings.Smoothing, 0, 0.95), sat = Math.Clamp(settings.Saturation, 0, 3);
        int black = Math.Clamp(settings.BlackLevel, 0, 60);
        int leds = light.LedCount;
        double[] state;
        lock (gate)
        {
            if (!smooth.TryGetValue(light, out state!) || state.Length != leds * 3) smooth[light] = state = new double[leds * 3];
        }
        // each LED sent (a virtual LED: Stretch spreads them over the light's own) takes its own spot of its zone (a
        // zone with 10 of them is cut in 10 along its length), so it shows what's there and nothing beside it
        var parts = Parts(light, zones);
        var own = new (byte R, byte G, byte B)[leds];
        var sums = new double[n * 4];
        for (int i = 0; i < leds; i++)
        {
            var part = parts[i];
            var (cr, cg, cb) = part != null && part.W > 0 && part.H > 0 ? sampler.Average(part) : (0, 0, 0);
            // more colourful: away from its grey
            double grey = 0.299 * cr + 0.587 * cg + 0.114 * cb;
            cr = grey + (cr - grey) * sat;
            cg = grey + (cg - grey) * sat;
            cb = grey + (cb - grey) * sat;
            if (Math.Max(cr, Math.Max(cg, cb)) < black) cr = cg = cb = 0;
            state[i * 3] = state[i * 3] * keep + cr * (1 - keep);
            state[i * 3 + 1] = state[i * 3 + 1] * keep + cg * (1 - keep);
            state[i * 3 + 2] = state[i * 3 + 2] * keep + cb * (1 - keep);
            own[i] = (Byte(state[i * 3]), Byte(state[i * 3 + 1]), Byte(state[i * 3 + 2]));
            int z = Math.Min(n - 1, light.ZoneOfLed(i));
            sums[z * 4] += own[i].R; sums[z * 4 + 1] += own[i].G; sums[z * 4 + 2] += own[i].B; sums[z * 4 + 3]++;
        }
        // the tab shows each zone's LEDs together
        var colours = new (byte R, byte G, byte B)[n];
        for (int z = 0; z < n; z++)
            if (sums[z * 4 + 3] > 0)
                colours[z] = (Byte(sums[z * 4] / sums[z * 4 + 3]), Byte(sums[z * 4 + 1] / sums[z * 4 + 3]), Byte(sums[z * 4 + 2] / sums[z * 4 + 3]));

        // a test, the segments or LEDs shown, or the zone being dragged; while AmbiLight is off the rest stay dark
        var rgb = new byte[leds * 3];
        var each = new (byte R, byte G, byte B)[leds];
        var firstShown = new (byte R, byte G, byte B)?[n];
        var firstAt = new int[n];
        for (int i = 0; i < leds; i++)
        {
            int zone = Math.Min(n - 1, light.ZoneOfLed(i));
            var c = red || zone == heldZone ? Red
                : testing ? TestColour(light.SegmentOfLed(i))
                : showingLeds ? TestColour(i)
                : settings.Enabled ? own[i] : default;
            each[i] = settings.Enabled || red || zone == heldZone || testing || showingLeds ? c : own[i];
            rgb[i * 3] = c.R;
            rgb[i * 3 + 1] = c.G;
            rgb[i * 3 + 2] = c.B;
            int at = light.PathOf(i);
            if ((testing || showingLeds) && (firstShown[zone] == null || at < firstAt[zone]))
            {
                firstShown[zone] = c;
                firstAt[zone] = at;
            }
        }
        for (int z = 0; z < n; z++)
            if (red || z == heldZone) colours[z] = Red;
            else if (firstShown[z] is { } f) colours[z] = f;
        lock (gate) { shown[light] = colours; ledsShown[light] = each; }
        if (on && !string.IsNullOrEmpty(light.Ip)) Send(light, rgb);
    }

    private static (byte R, byte G, byte B) TestColour(int segment) => segment == 0 ? ((byte)255, (byte)255, (byte)255) : (segment % 3) switch
    {
        1 => ((byte)255, (byte)0, (byte)0),
        2 => ((byte)0, (byte)255, (byte)0),
        _ => ((byte)0, (byte)0, (byte)255),
    };

    /// <summary>
    /// Each LED's spot on the screen: its zone cut into as many slices as the zone has LEDs, nothing outside it. The
    /// cuts go across the light's path: the way to the next zone (or from the one before) says whether the light runs
    /// across the zone or up / down it, and which end its first LED is at; a zone on its own is cut along its longer
    /// side.
    /// </summary>
    internal static AmbiZone?[] Parts(AmbiLight light, AmbiZone[] zones)
    {
        int n = zones.Length, leds = light.LedCount;
        var parts = new AmbiZone?[leds];
        if (n == 0) return parts;
        // how many LEDs each zone has, and the first one's place along the path
        var count = new int[n];
        var first = Enumerable.Repeat(int.MaxValue, n).ToArray();
        for (int i = 0; i < leds; i++)
        {
            int z = Math.Min(n - 1, light.ZoneOfLed(i));
            count[z]++;
            first[z] = Math.Min(first[z], light.PathOf(i));
        }
        for (int i = 0; i < leds; i++)
        {
            int z = Math.Min(n - 1, light.ZoneOfLed(i));
            var zone = zones[z];
            if (count[z] <= 1) { parts[i] = zone; continue; }
            int k = Math.Clamp(light.PathOf(i) - first[z], 0, count[z] - 1);
            var (along, backwards) = Direction(zones, z);
            int at = backwards ? count[z] - 1 - k : k;
            parts[i] = along
                ? new AmbiZone { X = zone.X + zone.W * at / count[z], Y = zone.Y, W = zone.W / count[z], H = zone.H }
                : new AmbiZone { X = zone.X, Y = zone.Y + zone.H * at / count[z], W = zone.W, H = zone.H / count[z] };
        }
        return parts;
    }

    /// <summary>Whether the path runs across zone <paramref name="z"/> (else up / down it), and whether backwards
    /// (right to left, or bottom to top).</summary>
    internal static (bool Across, bool Backwards) Direction(AmbiZone[] zones, int z)
    {
        var zone = zones[z];
        double cx = zone.X + zone.W / 2, cy = zone.Y + zone.H / 2;
        AmbiZone? other = z + 1 < zones.Length ? zones[z + 1] : z > 0 ? zones[z - 1] : null;
        if (other == null) return (zone.W >= zone.H, false);
        double dx = other.X + other.W / 2 - cx, dy = other.Y + other.H / 2 - cy;
        if (other != zones.ElementAtOrDefault(z + 1)) { dx = -dx; dy = -dy; }
        // two zones on the same spot say nothing: by its shape then
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return (zone.W >= zone.H, false);
        bool across = Math.Abs(dx) >= Math.Abs(dy);
        return (across, across ? dx < 0 : dy < 0);
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Into streaming first (asked how it is, so it can be put back; on; its brightness while AmbiLight is
    /// on); then the colours when they change, and at least each second (it falls back to its own scene when nothing
    /// comes); asked how it is every 10 s.</summary>
    private void Send(AmbiLight light, byte[] rgb)
    {
        long now = Environment.TickCount64;
        Streamed s;
        lock (gate)
        {
            if (!streaming.TryGetValue(light, out s!))
            {
                streaming[light] = s = new Streamed { Borrowed = !settings.Enabled, AskedAt = now };
                var started = s;
                _ = lan.AskStatusAsync(light.Ip, 700).ContinueWith(t =>
                {
                    if (t.Result is { } was) { started.WasOn = was.On; started.WasBrightness = was.Brightness; }
                    started.Ready = true;
                });
            }
        }
        if (!s.Ready) return;
        if (s.OnAt == 0)
        {
            Log.Here.Info($"AmbiLight: streaming to {light.Describe}, {(light.Stretch ? $"{light.LedCount} LEDs stretched over {light.Segments} segments" : $"{light.Segments} segments")} in {light.ZoneCount} zones{(s.Borrowed ? " (while off)" : "")}; it was {(s.WasOn == null ? "silent" : s.WasOn == true ? $"on at {s.WasBrightness}%" : "off")}");
            if (s.WasOn != true) lan.Power(light.Ip, true);
        }
        if (now - s.OnAt > 30000)
        {
            lan.Stream(light.Ip, true);
            s.OnAt = now;
        }
        if (settings.Enabled && s.Brightness != light.Brightness)
        {
            lan.Brightness(light.Ip, light.Brightness);
            s.Brightness = light.Brightness;
        }
        if (now - s.AskedAt > 10000)
        {
            lan.AskStatus(light.Ip);
            s.AskedAt = now;
        }
        if (now - s.SentAt < 1000 && rgb.AsSpan().SequenceEqual(s.Last)) return;
        lan.Colours(light.Ip, rgb, light.Stretch);
        s.Last = rgb;
        s.SentAt = now;
    }

    /// <summary>Lights no longer wanted back as they were.</summary>
    private void StopUnwanted(HashSet<AmbiLight> wanted)
    {
        lock (gate)
            foreach (var light in streaming.Keys.Where(l => !wanted.Contains(l)).ToList())
            {
                PutBack(light, streaming[light]);
                streaming.Remove(light);
            }
    }

    /// <summary>Every light back as it was.</summary>
    private void StopAll() => StopUnwanted(new HashSet<AmbiLight>());

    /// <summary>
    /// Streaming off (Govee goes back to its own scene); its brightness back if it was changed; off again if it was
    /// off, or if it didn't say and was only borrowed (a test, a zone dragged).
    /// </summary>
    private void PutBack(AmbiLight light, Streamed s)
    {
        if (s.OnAt == 0) return; // never started
        lan.Stream(light.Ip, false);
        if (s.Brightness >= 0 && s.WasBrightness > 0 && s.Brightness != s.WasBrightness) lan.Brightness(light.Ip, s.WasBrightness);
        bool off = s.WasOn == false || s.WasOn == null && s.Borrowed;
        if (off) lan.Power(light.Ip, false);
        Log.Here.Info($"AmbiLight: stopped streaming to {light.Describe}{(off ? ", turned off again" : "")}");
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("AmbiLight on"), () => SetEnabled(!settings.Enabled)) { Checked = settings.Enabled },
    };

    public void Dispose()
    {
        closing = true;
        hotkey.Dispose();
        TitleBadges.Set("AmbiLight", null);
        view.Stop();
        if (!thread.IsAlive || !thread.Join(2000)) StopAll();
        sampler.Dispose();
        lan.Dispose();
        settings.Save();
    }
}
