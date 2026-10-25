using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.AmbiLight;

/// <summary>
/// SimEffects → AmbiLight: what's on the displays, on Govee lights behind them (as Philips' Ambilight). A loop on its
/// own thread, while on or while the tab shows: every display's small picture (<see cref="ScreenSampler"/>), each
/// zone's average colour, made more colourful, dark ones black, smoothed; then each segment its zone's colour, sent
/// to the light (<see cref="GoveeLan"/>) while on. See README.md.
/// </summary>
public sealed class AmbiLightApplet : IApplet
{
    private readonly AmbiSettings settings = AmbiSettings.Load();
    private readonly ScreenSampler sampler = new();
    private readonly GoveeLan lan = new();
    private readonly AmbiLightView view;
    private readonly Thread thread;
    private volatile bool closing, previewing;
    private readonly object gate = new();
    private readonly Dictionary<AmbiLight, double[]> smooth = new();
    private readonly Dictionary<AmbiLight, (byte R, byte G, byte B)[]> shown = new();
    private readonly Dictionary<AmbiLight, Streamed> streaming = new();
    private long testUntil;
    private int frames;
    private long countFrom = Environment.TickCount64;

    /// <summary>What was last sent to a light, and when.</summary>
    private sealed class Streamed
    {
        public byte[] Last = Array.Empty<byte>();
        public long SentAt, OnAt, AskedAt;
        public int Brightness = -1;
    }

    public AmbiLightApplet()
    {
        foreach (var l in settings.Lights) EnsureZones(l);
        view = new AmbiLightView(this);
        thread = new Thread(Loop) { IsBackground = true, Name = "AmbiLight" };
    }

    internal AmbiSettings Settings => settings;
    internal ScreenSampler Sampler => sampler;
    internal GoveeLan Lan => lan;
    /// <summary>Pictures a second the loop managed, over the last second or so.</summary>
    internal double Rate { get; private set; }
    internal bool Testing => Environment.TickCount64 < Interlocked.Read(ref testUntil);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        if (settings.Enabled) lan.Listen();
        thread.Start();
        if (settings.Enabled) _ = FindMovedAsync();
    }

    /// <summary>The tab shows: the pictures are taken for it even while off.</summary>
    internal bool Previewing { set => previewing = value; }

    internal void SetEnabled(bool on)
    {
        settings.Enabled = on;
        settings.Save();
        Log.Here.Info($"AmbiLight: switched {(on ? "on" : "off")}");
        if (on)
        {
            lan.Listen();
            _ = FindMovedAsync();
        }
    }

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

    /// <summary>A zone for each group of segments: laid out again when the count changed.</summary>
    internal void EnsureZones(AmbiLight light, bool again = false)
    {
        if (!again && light.Zones.Count == light.ZoneCount) return;
        var zones = Layouts.Make(light.Layout, light.ZoneCount, sampler.Displays.Select(d => d.Bounds).ToList());
        lock (gate) light.Zones = zones;
    }

    /// <summary>A light changed (segments, zones, stretch…): saved, and sent in full next time.</summary>
    internal void Changed(AmbiLight? light = null)
    {
        settings.Save();
        lock (gate)
        {
            if (light != null) { smooth.Remove(light); shown.Remove(light); }
            foreach (var s in streaming.Values) s.Last = Array.Empty<byte>();
        }
    }

    internal void Removed(AmbiLight light)
    {
        lock (gate)
        {
            if (streaming.Remove(light)) lan.Stream(light.Ip, false);
            smooth.Remove(light);
            shown.Remove(light);
        }
        settings.Save();
    }

    /// <summary>Each segment a colour for 10 s: the first white, then red, green, blue over and over, to count them and
    /// see which end is first.</summary>
    internal void Test() => Interlocked.Exchange(ref testUntil, Environment.TickCount64 + 10000);

    /// <summary>Each zone's colour now (for the tab), or null before the first picture.</summary>
    internal (byte R, byte G, byte B)[]? ZoneColours(AmbiLight light)
    {
        lock (gate) return shown.TryGetValue(light, out var c) ? c : null;
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
                bool testing = Testing, on = settings.Enabled || testing;
                if (!on) StopAll();
                if (on || previewing)
                {
                    sampler.Capture();
                    foreach (var light in settings.Lights.ToArray()) Light(light, on, testing);
                    Count();
                }
            }
            catch (Exception ex) { Log.Here.Warn("AmbiLight: the loop", ex); Thread.Sleep(1000); }
            // to a schedule, so Windows' 15.6 ms sleeps even out
            int fps = settings.Enabled || Testing ? Math.Clamp(settings.Fps, 10, 60) : previewing ? 10 : 5;
            long now = Environment.TickCount64;
            next = Math.Max(next + 1000 / fps, now - 100);
            Thread.Sleep((int)Math.Max(1, next - now));
        }
        StopAll();
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

    private void Light(AmbiLight light, bool on, bool testing)
    {
        AmbiZone[] zones;
        lock (gate) zones = light.Zones.ToArray();
        int n = zones.Length;
        if (n == 0) return;
        double keep = Math.Clamp(settings.Smoothing, 0, 0.95), sat = Math.Clamp(settings.Saturation, 0, 3);
        int black = Math.Clamp(settings.BlackLevel, 0, 60);
        double[] state;
        lock (gate)
        {
            if (!smooth.TryGetValue(light, out state!) || state.Length != n * 3) smooth[light] = state = new double[n * 3];
        }
        var colours = new (byte R, byte G, byte B)[n];
        for (int z = 0; z < n; z++)
        {
            var (r, g, b) = zones[z].W > 0 && zones[z].H > 0 ? sampler.Average(zones[z]) : (0, 0, 0);
            // more colourful: away from its grey
            double grey = 0.299 * r + 0.587 * g + 0.114 * b;
            r = grey + (r - grey) * sat;
            g = grey + (g - grey) * sat;
            b = grey + (b - grey) * sat;
            if (Math.Max(r, Math.Max(g, b)) < black) r = g = b = 0;
            state[z * 3] = state[z * 3] * keep + r * (1 - keep);
            state[z * 3 + 1] = state[z * 3 + 1] * keep + g * (1 - keep);
            state[z * 3 + 2] = state[z * 3 + 2] * keep + b * (1 - keep);
            colours[z] = (Byte(state[z * 3]), Byte(state[z * 3 + 1]), Byte(state[z * 3 + 2]));
        }

        int segments = Math.Clamp(light.Segments, 1, 255);
        var rgb = new byte[segments * 3];
        for (int s = 0; s < segments; s++)
        {
            var c = testing ? TestColour(s) : colours[Math.Min(n - 1, light.ZoneOf(s))];
            rgb[s * 3] = c.R;
            rgb[s * 3 + 1] = c.G;
            rgb[s * 3 + 2] = c.B;
        }
        if (testing)
            for (int z = 0; z < n; z++)
            {
                int first = Enumerable.Range(0, segments).FirstOrDefault(s => light.ZoneOf(s) == z);
                colours[z] = TestColour(first);
            }
        lock (gate) shown[light] = colours;
        if (on && !string.IsNullOrEmpty(light.Ip)) Send(light, rgb);
    }

    private static (byte R, byte G, byte B) TestColour(int segment) => segment == 0 ? ((byte)255, (byte)255, (byte)255) : (segment % 3) switch
    {
        1 => ((byte)255, (byte)0, (byte)0),
        2 => ((byte)0, (byte)255, (byte)0),
        _ => ((byte)0, (byte)0, (byte)255),
    };

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Into streaming first (on, its brightness); then the colours when they change, and at least each
    /// second (it falls back to its own scene when nothing comes); asked how it is every 10 s.</summary>
    private void Send(AmbiLight light, byte[] rgb)
    {
        long now = Environment.TickCount64;
        Streamed s;
        lock (gate)
        {
            if (!streaming.TryGetValue(light, out s!))
            {
                streaming[light] = s = new Streamed();
                Log.Here.Info($"AmbiLight: streaming to {light.Describe}, {light.Segments} segments in {light.ZoneCount} zones{(light.Stretch ? ", stretched" : "")}");
                lan.Power(light.Ip, true);
            }
        }
        if (now - s.OnAt > 30000)
        {
            lan.Stream(light.Ip, true);
            s.OnAt = now;
        }
        if (s.Brightness != light.Brightness)
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

    /// <summary>Every light back to its own scene.</summary>
    private void StopAll()
    {
        lock (gate)
        {
            if (streaming.Count == 0) return;
            foreach (var light in streaming.Keys)
            {
                lan.Stream(light.Ip, false);
                Log.Here.Info($"AmbiLight: stopped streaming to {light.Describe}");
            }
            streaming.Clear();
        }
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        closing = true;
        view.Stop();
        if (!thread.IsAlive || !thread.Join(2000)) StopAll();
        sampler.Dispose();
        lan.Dispose();
        settings.Save();
    }
}
