using Vortice.DirectInput;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// The game's force feedback, passed from the vJoy wheel to the real one, the way Forza EmuWheel does it (ported from
/// it, so it feels the same): the wheel is opened exclusively with its auto-centre spring off, four effects are made on
/// its force-feedback axis (a constant force, a sine wave, a spring and a damper), and each packet the game sends vJoy
/// updates them, scaled by EmuWheel's tuning (<see cref="EmuWheelFfb"/>). As in EmuWheel, the forces are applied when an
/// effect report or an effect operation arrives, with the latest values of each kind; Forza's engine and gear-shift
/// vibrations are told apart by the gain it gives them (1 and 32). On top: overall <see cref="FfbSettings.Strength"/>
/// and <see cref="FfbSettings.Reverse"/>.
/// </summary>
internal sealed class ForceFeedback : IDisposable
{
    private readonly IDirectInputDevice8 dev;
    private readonly FfbSettings settings;
    private readonly Func<double> steering;
    // (the effects are DirectInput calls into the wheel's driver: under the one DirectInput lock, since 3.24.6)
    private readonly object gate = GameControllers.Gate;
    private readonly int[] axes;
    private readonly IDirectInputEffect constant, sine, spring, damper;
    private volatile bool stopped;

    // the latest of each report, and the last seen (a repeat is ignored, as in EmuWheel)
    private VJoy.EffectReport effect, lastEffect;
    private VJoy.ConstantReport constReport, lastConst;
    private VJoy.PeriodicReport periodic, lastPeriodic;
    private VJoy.ConditionReport condition, lastCondition;
    private VJoy.OperationReport op;
    private byte lastGain;
    private bool engineBug, gearShiftBug;

    /// <summary>What it's doing, for the tab.</summary>
    public string Device { get; }
    public DateTime? LastPacket { get; private set; }

    private ForceFeedback(IDirectInputDevice8 dev, string name, FfbSettings settings, Func<double> steering, int axis)
    {
        this.dev = dev;
        this.settings = settings;
        this.steering = steering;
        Device = name;
        axes = new[] { axis };
        constant = dev.CreateEffect(EffectGuid.ConstantForce, Base(new ConstantForce()));
        sine = dev.CreateEffect(EffectGuid.Sine, Base(new PeriodicForce()));
        spring = dev.CreateEffect(EffectGuid.Spring, Base(new ConditionSet { Conditions = new Vortice.DirectInput.Condition[1] }));
        damper = dev.CreateEffect(EffectGuid.Damper, Base(new ConditionSet { Conditions = new Vortice.DirectInput.Condition[1] }));
    }

    /// <summary>
    /// Force feedback on the wheel the reader has open (exclusively, which force feedback needs), or null if it has no
    /// force-feedback axis or the effects can't be made.
    /// </summary>
    public static ForceFeedback? Create(GameControllers.Reader reader, string name, FfbSettings settings, Func<double> steering)
    {
        var dev = reader.Device;
        lock (GameControllers.Gate)
        try
        {
            int? actuator = dev.GetObjects().Where(o => (o.ObjectId.Flags & DeviceObjectTypeFlags.ForceFeedbackActuator) != 0)
                .Select(o => (int?)(int)o.ObjectId).FirstOrDefault();
            if (actuator == null)
            {
                Logging.Log.Here.Warn($"ForzaWheel: {name} has no force-feedback axis");
                return null;
            }
            var ffb = new ForceFeedback(dev, name, settings, steering, actuator.Value);
            Logging.Log.Here.Info($"ForzaWheel: force feedback on {name}");
            return ffb;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"ForzaWheel: couldn't make force-feedback effects on {name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The parameters every effect is made with (EmuWheel's: forever, full gain, on the force-feedback axis).</summary>
    private EffectParameters Base(TypeSpecificParameters specific)
    {
        var p = new EffectParameters
        {
            Duration = -1, Flags = EffectFlags.ObjectIds | EffectFlags.Cartesian, Gain = 10000, StartDelay = 0, SamplePeriod = 0,
            TriggerButton = -1, TriggerRepeatInterval = 0, Parameters = specific,
        };
        p.SetAxes(axes, new[] { (int)effect.Direction });
        return p;
    }

    /// <summary>One packet from the game (on vJoy's thread).</summary>
    public void OnPacket(IntPtr packet)
    {
        if (stopped || !settings.Enabled) return;
        lock (gate)
        {
            try { Handle(packet); }
            catch (Exception ex) { Logging.Log.Here.Debug(() => "ForzaWheel force feedback: " + ex.Message); }
        }
    }

    private void Handle(IntPtr p)
    {
        if (!VJoy.TryType(p, out var type)) return;
        LastPacket = DateTime.Now;
        switch (type)
        {
            case VJoy.FfbPacket.EffectReport:
                if (!VJoy.TryEffect(p, out effect)) break;
                if (Same(effect, lastEffect)) return;
                lastEffect = effect;
                break;
            case VJoy.FfbPacket.Condition:
                if (!VJoy.TryCondition(p, out condition)) break;
                if (Same(condition, lastCondition)) return;
                lastCondition = condition;
                break;
            case VJoy.FfbPacket.Periodic:
                if (!VJoy.TryPeriodic(p, out periodic)) break;
                if (Same(periodic, lastPeriodic)) return;
                lastPeriodic = periodic;
                break;
            case VJoy.FfbPacket.Constant:
                if (!VJoy.TryConstant(p, out constReport)) break;
                if (Same(constReport, lastConst)) return;
                lastConst = constReport;
                break;
            case VJoy.FfbPacket.EffectOperation:
                VJoy.TryOperation(p, out op);
                break;
            case VJoy.FfbPacket.Gain:
                if (VJoy.TryGain(p, out var gain))
                {
                    if (gain == lastGain) return;
                    lastGain = gain;
                }
                break;
            case VJoy.FfbPacket.Control:
                if (VJoy.TryControl(p, out var control)) Control(control);
                return;
            case VJoy.FfbPacket.NewEffect or VJoy.FfbPacket.Sample or VJoy.FfbPacket.BlockFree or VJoy.FfbPacket.BlockLoad or VJoy.FfbPacket.Pool:
                return;
        }
        if (effect.Type == VJoy.FfbEffect.None) return;
        // as EmuWheel: forces are applied on an effect report or operation, with the latest of each kind of value
        if (type is not (VJoy.FfbPacket.EffectOperation or VJoy.FfbPacket.EffectReport)) return;
        if (op.Operation == VJoy.FfbOperation.Stop)
            (effect.Type switch { VJoy.FfbEffect.Constant => constant, VJoy.FfbEffect.Sine => sine, VJoy.FfbEffect.Damper => damper, VJoy.FfbEffect.Spring => spring, _ => null })?.Stop();
        switch (effect.Type)
        {
            case VJoy.FfbEffect.Sine: SendPeriodic(); break;
            case VJoy.FfbEffect.Constant: SendConstant(); break;
            case VJoy.FfbEffect.Damper or VJoy.FfbEffect.Spring: SendCondition(); break;
        }
        op = default;
    }

    private void Control(VJoy.FfbControl c)
    {
        var cmd = c switch
        {
            VJoy.FfbControl.Reset => ForceFeedbackCommand.Reset,
            VJoy.FfbControl.StopAll or VJoy.FfbControl.DisableActuators => ForceFeedbackCommand.StopAll,
            VJoy.FfbControl.Pause => ForceFeedbackCommand.Pause,
            VJoy.FfbControl.Continue => ForceFeedbackCommand.Continue,
            _ => ForceFeedbackCommand.None,
        };
        if (cmd != ForceFeedbackCommand.None) dev.SendForceFeedbackCommand(cmd);
    }

    // ---------------------------------------------------------------- the effects, as EmuWheel sends them

    private void SendConstant()
    {
        var t = settings.Tuning.Const;
        // the game's forces are -10000 to 10000: anything else is a misread, and is dropped rather than sent to the wheel
        if (constReport.Magnitude is < -10000 or > 10000) return;
        int num = (int)Math.Round(constReport.Magnitude * t.Magnitude * settings.Strength);
        int max = (int)Math.Round(t.MaximumForce * 10000);
        if (Math.Abs(num) > max) num = max * Math.Sign(num);
        if (Math.Abs(num) < (int)(t.MinimumForce * 10000)) num = (int)(t.MinimumForce * 10000) * Math.Sign(num);
        if (Math.Abs(num) > 10000) num = 10000 * Math.Sign(num);
        // weaker near the centre: scaled by how far the wheel is turned, never below MinimumCoefficient (off by default)
        if (Math.Abs(num) > t.FilterThreshold * 10000)
        {
            double s = steering();
            if (!double.IsNaN(s)) num = (int)(num * Math.Max(t.MinimumCoefficient, Math.Abs(s * 2 - 1)));
        }
        Play(constant, new ConstantForce { Magnitude = settings.Reverse ? num : -num }, startOnce: true);
    }

    private void SendPeriodic()
    {
        var t = settings.Tuning.Sine;
        var force = new PeriodicForce
        {
            Offset = periodic.Offset,
            Period = (int)Math.Round(periodic.Period * 1000 / Math.Max(0.01f, t.Frequency)),
            Phase = (int)(t.Phase * 35999),
        };
        int num = (int)Math.Round(effect.Gain / 255.0 * 10000 * t.Magnitude);
        int max = (int)Math.Round(t.MaximumForce * 10000);
        if (num > max) num = max;
        int min = (int)Math.Round(t.MinimumForce * 10000);
        if (num < min && num > 0) num = min;
        // Forza marks the gear-shift jolt with gain 32 and the engine with gain 1 (and 0 right after)
        if (effect.Gain == 32 || (gearShiftBug && effect.Gain == 0))
        {
            gearShiftBug = effect.Gain == 32;
            num = (int)Math.Round(t.GearShiftVibrations.Strength * 10000);
            force.Period = (int)Math.Round(periodic.Period * 1000 / Math.Max(0.01f, t.GearShiftVibrations.Frequency));
        }
        if (effect.Gain == 1 || (engineBug && effect.Gain == 0))
        {
            engineBug = effect.Gain == 1;
            num = (int)Math.Round(t.EngineVibrations.Strength * 10000);
            force.Period = (int)Math.Round(periodic.Period * 1000 / Math.Max(0.01f, t.EngineVibrations.Frequency));
        }
        force.Magnitude = Math.Min(10000, (int)(num * settings.Strength));
        Play(sine, force, startOnce: false);
    }

    private void SendCondition()
    {
        bool isDamper = effect.Type == VJoy.FfbEffect.Damper;
        var t = isDamper ? settings.Tuning.Damper : settings.Tuning.Spring;
        var c = new Vortice.DirectInput.Condition
        {
            DeadBand = condition.DeadBand,
            Offset = condition.CenterPointOffset,
            PositiveCoefficient = (int)Math.Round(condition.PosCoeff * t.Coefficient),
            NegativeCoefficient = (int)Math.Round(condition.NegCoeff * t.Coefficient),
            PositiveSaturation = Math.Min(10000, (int)Math.Round(condition.PosSatur * t.Saturation)),
            NegativeSaturation = Math.Min(10000, (int)Math.Round(condition.NegSatur * t.Saturation)),
        };
        Play(isDamper ? damper : spring, new ConditionSet { Conditions = new[] { c } }, startOnce: false);
    }

    private void Play(IDirectInputEffect e, TypeSpecificParameters specific, bool startOnce)
    {
        var p = Base(specific);
        e.SetParameters(p, EffectParameterFlags.TypeSpecificParameters);
        if (e.Status != EffectStatus.Playing)
        {
            if (startOnce) e.Start(1, EffectPlayFlags.NoDownload);
            else e.Start();
        }
    }

    /// <summary>
    /// Test FFB: a pull left, a pull right, then a shake, sent to a vJoy device (opened as a game opens it), so they come
    /// back through vJoy to the wheel as the game's forces do. <paramref name="step"/> is told each (on this thread).
    /// Null, or why it couldn't.
    /// </summary>
    public static string? Test(Guid vjoy, IntPtr window, Action<string> step)
    {
        using var reader = GameControllers.Open(vjoy, window, forceFeedback: true);
        if (reader == null) return T("The vJoy device couldn't be opened (is the game holding it? Close it to test).");
        var dev = reader.Device;
        int? axis;
        lock (GameControllers.Gate)
            axis = dev.GetObjects().Where(o => (o.ObjectId.Flags & DeviceObjectTypeFlags.ForceFeedbackActuator) != 0)
                .Select(o => (int?)(int)o.ObjectId).FirstOrDefault();
        if (axis == null) return T("The vJoy device has no force feedback: tick Enable Effects in Configure vJoy.");
        EffectParameters P(TypeSpecificParameters specific, int gain)
        {
            var p = new EffectParameters { Duration = -1, Flags = EffectFlags.ObjectIds | EffectFlags.Cartesian, Gain = gain, TriggerButton = -1, Parameters = specific };
            p.SetAxes(new[] { axis.Value }, new[] { 1 });
            return p;
        }
        // (vJoy's calls aren't under the DirectInput lock: the forces come back on vJoy's thread, which takes it)
        IDirectInputEffect? pull = null, shake = null;
        try
        {
            // as the game's: + pulls left once passed on (ForceFeedback sends the wheel the other sign), - right
            foreach (var (text, push) in new[] { (T("Pulling left…"), 3000), (T("Pulling right…"), -3000) })
            {
                step(text);
                if (pull == null) { pull = dev.CreateEffect(EffectGuid.ConstantForce, P(new ConstantForce { Magnitude = push }, 10000)); pull.Start(); }
                else pull.SetParameters(P(new ConstantForce { Magnitude = push }, 10000), EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start);
                Thread.Sleep(800);
            }
            // (the effect stopped, the last force would be sent again: 0 first)
            pull?.SetParameters(P(new ConstantForce { Magnitude = 0 }, 10000), EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.Start);
            pull?.Stop();
            step(T("Shaking…"));
            // (a sine's strength is its gain, as EmuWheel reads it: 40%, 20 times a second)
            shake = dev.CreateEffect(EffectGuid.Sine, P(new PeriodicForce { Magnitude = 10000, Period = 50_000 }, 4000));
            shake.Start();
            Thread.Sleep(1200);
            shake.SetParameters(P(new PeriodicForce { Magnitude = 10000, Period = 50_000 }, 0), EffectParameterFlags.Gain | EffectParameterFlags.Start);
            shake.Stop();
            return null;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn("ForzaWheel: Test FFB: " + ex.Message);
            return F("The forces couldn't be sent to the vJoy device: {0}", ex.Message);
        }
        finally
        {
            try { dev.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll); } catch { }
            try { pull?.Dispose(); } catch { }
            try { shake?.Dispose(); } catch { }
        }
    }

    private static bool Same<TStruct>(TStruct a, TStruct b) where TStruct : struct => a.Equals(b);

    public void Dispose()
    {
        stopped = true;
        lock (gate)
        {
            try { dev.SendForceFeedbackCommand(ForceFeedbackCommand.StopAll); } catch { }
            foreach (var e in new[] { constant, sine, spring, damper })
                try { e.Dispose(); } catch { }
        }
        Logging.Log.Here.Info($"ForzaWheel: force feedback off ({Device})");
    }
}
