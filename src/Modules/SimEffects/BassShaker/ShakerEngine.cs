using System.Collections.Concurrent;
using NAudio.CoreAudioApi;

namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The Shaker running: one thread reads the profile's telemetry, works out its effects
/// (<see cref="EffectRunner"/>) and keeps an output open on each device (<see cref="ShakerOutput"/>), which make the
/// sound on their own audio threads. It runs while it's on, or for a moment when a Test button is pressed while it's
/// off. Changes from the UI (<see cref="Apply"/>) are picked up by the thread at its next turn.
/// </summary>
public sealed class ShakerEngine : IDisposable
{
    private readonly ShakerSettings settings;
    private readonly object gate = new();
    private readonly EffectRunner runner = new();
    private readonly ConcurrentDictionary<string, ShakerOutput> outputs = new();
    private readonly ConcurrentDictionary<string, string> problems = new();
    private volatile EffectSignal[] signals = Array.Empty<EffectSignal>();
    private volatile ShakerDevice[] devices = Array.Empty<ShakerDevice>();
    private volatile ShakerProfile? profile;
    private volatile bool running, stopping, outputsDirty = true;
    private DateTime testUntil;
    private ITelemetrySource? source;
    private (Game, int)? sourceKey;
    private DateTime lastFrameAt = DateTime.MinValue;
    private bool lastLive;

    /// <summary>What it's doing, for the status line.</summary>
    public string Status { get; private set; } = "";
    /// <summary>Telemetry came in the last second.</summary>
    public bool Receiving => DateTime.UtcNow - lastFrameAt < TimeSpan.FromSeconds(1);

    public ShakerEngine(ShakerSettings settings) => this.settings = settings;

    public EffectSignal[] Signals => signals;

    /// <summary>Takes in the settings as they are now (on the UI thread): the profile, its effects, the devices, on/off.</summary>
    public void Apply(ShakerProfile? p)
    {
        profile = p;
        var old = signals;
        signals = p?.Effects.Select(e => old.FirstOrDefault(s => s.Effect == e) ?? new EffectSignal(e, EffectKinds.Of(e.Kind))).ToArray() ?? Array.Empty<EffectSignal>();
        foreach (var s in signals) s.Route();
        devices = settings.Devices.ToArray();
        outputsDirty = true;
        if (settings.Enabled) Run();
    }

    /// <summary>Which devices each effect plays on changed (on the UI thread).</summary>
    public void RoutingChanged()
    {
        foreach (var s in signals) s.Route();
    }

    public void Test(ShakerEffect e)
    {
        var s = signals.FirstOrDefault(x => x.Effect == e);
        if (s == null) return;
        s.TestLevel = (float)Math.Clamp(settings.TestLevel, 0.1, 1);
        s.TestUntil = DateTime.UtcNow.AddSeconds(1.5);
        Keep(s.TestUntil);
    }

    public void TestDevice(ShakerDevice d)
    {
        var until = DateTime.UtcNow.AddSeconds(1.2);
        Keep(until.AddSeconds(0.3));
        // (the output may not be open yet: tried for a moment as the thread opens it)
        Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                if (outputs.TryGetValue(d.Key, out var o)) { o.TestLevel = (float)Math.Clamp(settings.TestLevel, 0.1, 1); o.TestUntil = until; return; }
                await Task.Delay(50);
            }
        });
    }

    private void Keep(DateTime until)
    {
        lock (gate) if (until > testUntil) testUntil = until;
        Run();
    }

    /// <summary>The loudest it's been on a device since this was asked last (for its meter), 0..1.</summary>
    public float TakePeak(string key)
    {
        if (!outputs.TryGetValue(key, out var o)) return 0;
        float p = o.Peak;
        o.Peak = 0;
        return p;
    }

    /// <summary>Why a device isn't playing, if it isn't.</summary>
    public string? Problem(string key) => problems.TryGetValue(key, out var p) ? p : null;

    private void Run()
    {
        lock (gate)
        {
            if (running || stopping) return;
            running = true;
        }
        new Thread(Loop) { IsBackground = true, Name = "DaisysApp-BassShaker", Priority = ThreadPriority.AboveNormal }.Start();
    }

    private bool Wanted()
    {
        lock (gate) return !stopping && (settings.Enabled || DateTime.UtcNow < testUntil);
    }

    private void Loop()
    {
        var frame = new TelemetryFrame();
        var last = DateTime.UtcNow;
        var nextCheck = DateTime.MinValue;
        Logging.Log.Here.Info("Shaker: started");
        try
        {
            while (Wanted())
            {
                if (outputsDirty || DateTime.UtcNow >= nextCheck)
                {
                    outputsDirty = false;
                    nextCheck = DateTime.UtcNow.AddSeconds(3);
                    Outputs();
                }
                var p = profile;
                bool listen = settings.Enabled && p != null;
                Source(listen ? (p!.Game, p.Port) : null);

                bool got = source != null ? source.Read(frame, 50) : Sleep();
                var now = DateTime.UtcNow;
                float dt = (float)Math.Min(0.25, (now - last).TotalSeconds);
                last = now;
                var sig = signals;
                if (got)
                {
                    lastFrameAt = now;
                    if (frame.Live != lastLive) Logging.Log.Here.Info(frame.Live ? "Shaker: driving" : "Shaker: not driving (menu, pause or replay)");
                    lastLive = frame.Live;
                    runner.Process(frame, sig, frame.Dt > 0 ? frame.Dt : dt);
                }
                else if (now - lastFrameAt > TimeSpan.FromMilliseconds(300)) runner.Silence(sig, dt);
                Status = Describe(p, listen, frame);
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Shaker", ex); }
        finally
        {
            Source(null);
            foreach (var o in outputs.Values) o.Dispose();
            outputs.Clear();
            problems.Clear();
            foreach (var s in signals) { s.Everywhere(0); s.Level = 0; }
            Status = T("Off");
            lock (gate) running = false;
            Logging.Log.Here.Info("Shaker: stopped");
            // (switched on again, or a test, while it was stopping)
            if (!stopping && (settings.Enabled || DateTime.UtcNow < testUntil)) Run();
        }
    }

    private static bool Sleep()
    {
        Thread.Sleep(16);
        return false;
    }

    private string Describe(ShakerProfile? p, bool listen, TelemetryFrame f)
    {
        if (!listen) return settings.Enabled ? T("No profile") : T("Testing");
        string game = Games.Title(p!.Game);
        if (source?.Problem is { } problem) return problem;
        if (Receiving) return f.Live ? F("{0}: driving", game) : F("{0}: not driving", game);
        return Games.UsesPort(p.Game) ? F("Waiting for {0} on port {1}", game, p.Port) : F("Waiting for {0}", game);
    }

    private void Source((Game, int)? wanted)
    {
        if (wanted == sourceKey) return;
        source?.Dispose();
        source = null;
        sourceKey = wanted;
        lastFrameAt = DateTime.MinValue;
        if (wanted is { } w)
        {
            source = TelemetryHub.Open(w.Item1, w.Item2);
            Logging.Log.Here.Info($"Shaker: listening for {w.Item1}{(Games.UsesPort(w.Item1) ? $" on UDP {w.Item2}" : "")}");
        }
    }

    /// <summary>An output for every device that's set up and plugged in; none for those removed.</summary>
    private void Outputs()
    {
        var want = devices;
        foreach (var (key, o) in outputs)
        {
            var d = want.FirstOrDefault(x => x.Key == key);
            if (d == null || o.Failed || d.DeviceId != o.DeviceId || Changed(d, o))
            {
                outputs.TryRemove(key, out _);
                o.Dispose();
            }
        }
        foreach (var k in problems.Keys) if (want.All(d => d.Key != k)) problems.TryRemove(k, out _);
        using var enumerator = new MMDeviceEnumerator();
        foreach (var d in want)
        {
            if (outputs.ContainsKey(d.Key)) continue;
            try
            {
                var mm = enumerator.GetDevice(d.DeviceId);
                if (mm.State != DeviceState.Active) throw new InvalidOperationException("not plugged in");
                outputs[d.Key] = new ShakerOutput(d, settings.Master, () => signals, mm);
                channelsOf[d.Key] = (d.Channels, d.Position);
                problems.TryRemove(d.Key, out _);
                Logging.Log.Here.Info($"Shaker: playing on {d.Name} ({d.Position}, {d.Channels}, {d.MinHz}-{d.MaxHz} Hz)");
            }
            catch (Exception ex)
            {
                if (problems.TryAdd(d.Key, T("Not connected"))) Logging.Log.Here.Warn($"Shaker: {d.Name} can't play: {ex.Message}");
            }
        }
    }

    // a device's channels changing needs a new output (the others are read as it plays)
    private readonly ConcurrentDictionary<string, (Channels, Position)> channelsOf = new();
    private bool Changed(ShakerDevice d, ShakerOutput o) => channelsOf.TryGetValue(d.Key, out var c) && c != (d.Channels, d.Position);

    public void Dispose()
    {
        lock (gate) stopping = true;
        for (int i = 0; i < 40 && running; i++) Thread.Sleep(25);
    }
}
