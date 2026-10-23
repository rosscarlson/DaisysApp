namespace DaisysApp.Applets.SimEffects.BassShaker;

public enum EffectKind { Engine, RoadFeel, Curbs, OffRoad, Impacts, Crashes, GearShift, WheelSlip, Abs, RevLimiter }

/// <summary>How an effect sounds: a tone that follows the telemetry, rumbling noise, a tone switched on and off at a
/// rate (rumble strips, ABS, the limiter), or knocks (one short burst each time something happens).</summary>
public enum Shape { Tone, Noise, Pulsed, Knock }

/// <summary>One of an effect's settings: which field, its label, its range and how it's shown.</summary>
public sealed record EffectParam(string Label, Func<ShakerEffect, double> Get, Action<ShakerEffect, double> Set, double Min, double Max, double Step, Func<double, string> Show, string? Tip = null)
{
    /// <summary>Which of the Level Wizard's up / down shortcuts moves it (None: only its slider).</summary>
    public Knob Knob { get; init; }
}

/// <summary>The settings the Level Wizard has shortcuts for.</summary>
public enum Knob { None, Frequency, Sensitivity, Threshold, Length }

/// <summary>What each kind of effect is: its name, sound, settings, and the games that don't give what it needs.</summary>
public sealed record EffectInfo(EffectKind Kind, string Title, string Tip, Shape Shape, EffectParam[] Params, Game[] NotIn);

public static class EffectKinds
{
    private static EffectParam Hz(string? label = null) => new(label ?? T("Frequency"), e => e.Frequency, (e, v) => e.Frequency = v, 5, 150, 1, v => $"{v:0} Hz") { Knob = Knob.Frequency };
    private static readonly EffectParam HzHigh = new(T("Redline Hz"), e => e.FrequencyHigh, (e, v) => e.FrequencyHigh = v, 5, 150, 1, v => $"{v:0} Hz");
    private static readonly EffectParam Idle = new(T("Idle Level"), e => e.IdleLevel, (e, v) => e.IdleLevel = v, 0, 1, 0.05, v => $"{v * 100:0}%");
    private static readonly EffectParam Sens = new(T("Sensitivity"), e => e.Sensitivity, (e, v) => e.Sensitivity = v, 0.1, 5, 0.1, v => $"{v:0.0}×") { Knob = Knob.Sensitivity };
    private static readonly EffectParam Length = new(T("Length"), e => e.LengthMs, (e, v) => e.LengthMs = v, 20, 800, 10, v => $"{v:0} ms") { Knob = Knob.Length };
    private static EffectParam Threshold(double min, double max, double step, Func<double, string> show, string tip) =>
        new(T("Threshold"), e => e.Threshold, (e, v) => e.Threshold = v, min, max, step, show, tip) { Knob = Knob.Threshold };

    public static readonly EffectInfo[] All =
    {
        new(EffectKind.Engine, T("Engine"), T("The engine's RPM and throttle"), Shape.Tone, new[] { Hz(T("Idle Hz")), HzHigh, Idle }, Array.Empty<Game>()),
        new(EffectKind.RoadFeel, T("Road Feel"), T("The road's texture through the suspension"), Shape.Noise,
            new[] { Hz(), Sens, Threshold(0, 0.5, 0.005, v => $"{v:0.000} m/s", T("Suspension speed below this is left out")) }, Array.Empty<Game>()),
        new(EffectKind.Curbs, T("Curbs"), T("Curbs and rumble strips"), Shape.Pulsed, new[] { Hz(), Sens }, Array.Empty<Game>()),
        new(EffectKind.OffRoad, T("Off-Road"), T("Grass, gravel, dirt and sand"), Shape.Noise, new[] { Hz(), Sens }, Array.Empty<Game>()),
        new(EffectKind.Impacts, T("Impacts"), T("Bumps, potholes and landings"), Shape.Knock,
            new[] { Hz(), Sens, Threshold(0.05, 3, 0.05, v => $"{v:0.00} m/s", T("How fast the suspension has to move")), Length }, Array.Empty<Game>()),
        new(EffectKind.Crashes, T("Crashes"), T("Hitting walls, cars and things"), Shape.Knock,
            new[] { Hz(), Sens, Threshold(1, 20, 0.5, v => $"{v:0.0} g", T("How hard the hit has to be")), Length }, Array.Empty<Game>()),
        new(EffectKind.GearShift, T("Gear Shift"), T("A knock at each gear change"), Shape.Knock, new[] { Hz(), Sens, Length }, Array.Empty<Game>()),
        new(EffectKind.WheelSlip, T("Wheel Slip"), T("Tyres losing grip"), Shape.Tone,
            new[] { Hz(), Sens, Threshold(0, 0.9, 0.05, v => $"{v * 100:0}%", T("How far past grip it starts")) }, new[] { Game.IRacing }),
        new(EffectKind.Abs, T("ABS"), T("ABS working (Forza: a front wheel locking)"), Shape.Pulsed, new[] { Hz(), Sens }, Array.Empty<Game>()),
        new(EffectKind.RevLimiter, T("Rev Limiter"), T("At the redline"), Shape.Pulsed,
            new[] { Hz(), Sens, Threshold(0.8, 1, 0.01, v => $"{v * 100:0}%", T("Share of the redline it starts at")) }, Array.Empty<Game>()),
    };

    public static EffectInfo Of(EffectKind k) => All[(int)k];

    /// <summary>A new effect of a kind, with the settings that suit the game.</summary>
    public static ShakerEffect Create(EffectKind kind, Game game)
    {
        bool forza = Games.UsesPort(game);
        var e = new ShakerEffect { Kind = kind };
        switch (kind)
        {
            case EffectKind.Engine: e.Frequency = 30; e.FrequencyHigh = 65; e.IdleLevel = 0.25; e.Volume = 0.45; break;
            case EffectKind.RoadFeel:
                e.Frequency = 45; e.Volume = 0.5;
                e.Threshold = game switch { Game.ForzaHorizon => 0.05, Game.IRacing => 0.03, _ => 0.04 };
                break;
            case EffectKind.Curbs: e.Frequency = 35; e.Volume = 0.7; break;
            case EffectKind.OffRoad: e.Frequency = 25; e.Volume = game == Game.ForzaHorizon ? 0.5 : 0.35; break;
            case EffectKind.Impacts:
                e.Frequency = 30; e.LengthMs = 120; e.Volume = game == Game.ForzaHorizon ? 0.9 : 0.75;
                e.Threshold = game switch { Game.ForzaHorizon => 0.8, Game.IRacing => 0.5, _ => 0.6 };
                break;
            case EffectKind.Crashes: e.Frequency = 22; e.LengthMs = 350; e.Volume = 1; e.Threshold = forza ? 4 : 3.5; break;
            case EffectKind.GearShift: e.Frequency = 40; e.LengthMs = 80; e.Volume = 0.6; break;
            case EffectKind.WheelSlip: e.Frequency = 55; e.Threshold = 0.1; e.Volume = 0.4; break;
            case EffectKind.Abs: e.Frequency = 50; e.Volume = 0.5; break;
            case EffectKind.RevLimiter: e.Frequency = 45; e.Threshold = 0.97; e.Volume = 0.5; break;
        }
        return e;
    }
}

/// <summary>
/// An effect as it's playing: what the telemetry asks of it right now, read by every device's output on its own
/// audio thread. Tones and noise follow <see cref="Amp"/> and <see cref="Freq"/> (smoothed there); a knock is
/// <see cref="Knocks"/> going up by one, with its strength and length.
/// </summary>
public sealed class EffectSignal(ShakerEffect effect, EffectInfo info)
{
    public readonly ShakerEffect Effect = effect;
    public readonly EffectInfo Info = info;
    public volatile float Amp, Freq, GateHz;
    public volatile int Knocks;
    public volatile float KnockAmp, KnockSeconds;
    /// <summary>How much goes to each device (by its key; 0 for one it's off on, 1 for any not listed), set on the
    /// UI thread by <see cref="Route"/>.</summary>
    public volatile Dictionary<string, float> Gains = new();
    /// <summary>For the meters: how hard it's shaking now, 0..1, after its own volume.</summary>
    public volatile float Level;
    /// <summary>Its Test button: plays it by itself until then.</summary>
    public DateTime TestUntil;

    public float Gain => Effect.Muted ? 0 : (float)Effect.Volume;

    public float GainOn(string device) => Gains.TryGetValue(device, out float g) ? g : 1;

    /// <summary>Takes in its devices and their levels (on the UI thread).</summary>
    public void Route()
    {
        var g = Effect.Levels.ToDictionary(kv => kv.Key, kv => (float)Math.Clamp(kv.Value, 0, 1));
        foreach (var k in Effect.Off) g[k] = 0;
        Gains = g;
    }
}

/// <summary>Works out each effect from a frame of telemetry, on the engine's thread.</summary>
internal sealed class EffectRunner
{
    private int lastGear = int.MinValue;
    private readonly Dictionary<EffectSignal, double> cooldown = new();
    private double clock;

    public void Process(TelemetryFrame f, EffectSignal[] signals, float dt)
    {
        clock += dt;
        bool live = f.Live;
        float speedGate = Math.Clamp(f.SpeedMs / 8f, 0, 1);
        float rough = 0, peak = 0;
        for (int w = 0; w < 4; w++)
        {
            float v = Math.Abs(f.SuspVel[w]);
            rough += v / 4;
            peak = Math.Max(peak, v);
        }
        float horizontalG = MathF.Sqrt(f.AccelX * f.AccelX + f.AccelZ * f.AccelZ);
        bool shifted = lastGear != int.MinValue && f.Gear != lastGear && f.SpeedMs > 1;
        lastGear = f.Gear;

        foreach (var s in signals)
        {
            var e = s.Effect;
            float sens = (float)e.Sensitivity, thr = (float)e.Threshold;
            float amp = 0, freq = (float)e.Frequency, gate = 0;
            if (Testing(s, freq)) continue;
            if (live)
            {
                switch (e.Kind)
                {
                    case EffectKind.Engine:
                        if (f.Rpm > 50)
                        {
                            float idle = (float)e.IdleLevel, r = f.RpmFraction;
                            amp = idle + (1 - idle) * (0.35f * r + 0.65f * f.Throttle);
                            freq = (float)(e.Frequency + (e.FrequencyHigh - e.Frequency) * r);
                        }
                        break;
                    case EffectKind.RoadFeel:
                        amp = Math.Clamp((rough - thr) * 4 * sens, 0, 1) * speedGate;
                        break;
                    case EffectKind.Curbs:
                        amp = Math.Clamp(f.Kerb * sens, 0, 1) * Math.Clamp(f.SpeedMs / 4f, 0, 1);
                        // a bump about every metre and a bit
                        gate = Math.Clamp(f.SpeedMs / 1.2f, 4, 20);
                        break;
                    case EffectKind.OffRoad:
                        amp = Math.Clamp(f.OffRoad * sens, 0, 1) * speedGate;
                        break;
                    case EffectKind.Impacts:
                        if (peak > thr && Ready(s, e.LengthMs)) Knock(s, Math.Clamp(0.35f + (peak - thr) / (2 * thr), 0, 1) * sens, e.LengthMs);
                        break;
                    case EffectKind.Crashes:
                        if (horizontalG > thr && Ready(s, e.LengthMs * 1.5)) Knock(s, Math.Clamp(0.4f + (horizontalG - thr) / (2 * thr), 0, 1) * sens, e.LengthMs);
                        break;
                    case EffectKind.GearShift:
                        if (shifted && Ready(s, e.LengthMs)) Knock(s, 0.8f * sens, e.LengthMs);
                        break;
                    case EffectKind.WheelSlip:
                        amp = thr < 1 ? Math.Clamp((f.Slip - thr) / (1 - thr) * sens, 0, 1) * Math.Clamp(f.SpeedMs / 3f, 0, 1) : 0;
                        break;
                    case EffectKind.Abs:
                        amp = Math.Clamp(f.Abs * sens * 0.8f, 0, 1);
                        gate = 14;
                        break;
                    case EffectKind.RevLimiter:
                        amp = f.MaxRpm > 0 && f.Rpm >= f.MaxRpm * thr ? Math.Clamp(0.8f * sens, 0, 1) : 0;
                        gate = 10;
                        break;
                }
            }
            s.Amp = Math.Min(amp, 1);
            s.Freq = freq;
            s.GateHz = gate;
        }
        Meter(signals, dt);
    }

    /// <summary>No telemetry (the game closed or paused): everything stops, knocks ring out.</summary>
    public void Silence(EffectSignal[] signals, float dt)
    {
        clock += dt;
        lastGear = int.MinValue;
        foreach (var s in signals)
        {
            if (Testing(s, (float)s.Effect.Frequency)) continue;
            s.Amp = 0;
        }
        Meter(signals, dt);
    }

    private bool Testing(EffectSignal s, float freq)
    {
        if (DateTime.UtcNow >= s.TestUntil) return false;
        s.Freq = freq;
        s.GateHz = s.Info.Kind switch { EffectKind.Abs => 14, EffectKind.RevLimiter => 10, EffectKind.Curbs => 10, _ => 0 };
        if (s.Info.Shape == Shape.Knock)
        {
            s.Amp = 0;
            if (Ready(s, 450)) Knock(s, 0.9f, s.Effect.LengthMs);
        }
        else s.Amp = 0.8f;
        return true;
    }

    private bool Ready(EffectSignal s, double ms)
    {
        if (cooldown.TryGetValue(s, out double at) && clock < at) return false;
        cooldown[s] = clock + ms / 1000;
        return true;
    }

    private static void Knock(EffectSignal s, float amp, double ms)
    {
        s.KnockAmp = Math.Clamp(amp, 0, 1);
        s.KnockSeconds = (float)(ms / 1000);
        s.Knocks++;
        s.Level = Math.Max(s.Level, s.KnockAmp * s.Gain);
    }

    /// <summary>The meters: up at once, down over about a quarter of a second.</summary>
    private static void Meter(EffectSignal[] signals, float dt)
    {
        float fall = Math.Clamp(dt * 4, 0, 1);
        foreach (var s in signals)
        {
            float now = s.Amp * (s.GateHz > 0 ? 0.85f : 1) * s.Gain;
            s.Level = now >= s.Level ? now : s.Level - (s.Level - now) * fall;
        }
    }
}
