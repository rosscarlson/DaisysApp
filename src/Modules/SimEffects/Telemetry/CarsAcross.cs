namespace DaisysApp.Applets.SimEffects.Telemetry;

/// <summary>
/// (since 4.20.1, the Radar) How far each of iRacing's cars is to the side of yours. iRacing doesn't send it: only its
/// spotter's CarLeftRight, once a car overlaps yours, and each car's steering (CarIdxSteer). So a car's move to the side
/// is worked out from how much more or less it steers than the cars of its class do at that place on the lap (learned as
/// they drive, a cell every few metres), and yours from your steering the same way; the spotter sets it right while a car is alongside, and it goes back
/// in line over a few seconds. Where nothing is known yet (the first lap, a sharp corner) a car stays in line. What's
/// shown follows that smoothly. Tuned on a recorded session (2026-11-22, SimEffects' CLAUDE.md).
/// </summary>
internal sealed class CarsAcross
{
    /// <summary>How far beside you the spotter's car is put (a second on that side twice that); a car's length.</summary>
    public const float LaneMetres = 3, CarMetres = 4.8f;

    // the path's curve for a radian of steering (+ left; right is +), and how long a heading and a place to the side last
    private const float Curve = -0.04f, HeadingSeconds = 1, AcrossSeconds = 3;
    // steering past this isn't followed (a sharp corner), nor a car slower than this; the most a car is put out on its own
    private const float SteerMost = 0.4f, SlowestMs = 10, MostAlone = 3.2f;
    // how much of another car's own move counts (yours is told well, theirs poorly: whole, they swerved with nobody moving)
    private const float Theirs = 0.5f;
    // a place on the lap's cell, how many readings before its usual steering counts, how many it's the mean of
    private const float CellMetres = 5;
    private const int Enough = 3, Mean = 30;
    private const float Reach = 80, Overlapping = 8;

    private readonly int cars;
    private readonly float[] pctWas, speed, heading, target, shown, shownSpeed, push, ahead;
    private readonly int[] cellWas, lane;
    private readonly Dictionary<int, (float[] Steer, int[] Count)> usual = new();
    private float length;
    private bool going;

    public CarsAcross(int cars)
    {
        this.cars = cars;
        pctWas = new float[cars];
        speed = new float[cars];
        heading = new float[cars];
        target = new float[cars];
        shown = new float[cars];
        shownSpeed = new float[cars];
        push = new float[cars];
        ahead = new float[cars];
        cellWas = new int[cars];
        lane = new int[cars];
    }

    /// <summary>Metres to the right of yours (− left), as shown.</summary>
    public float Right(int i) => shown[i];
    /// <summary>Metres ahead of yours along the track (− behind), as shown.</summary>
    public float Ahead(int i) => ahead[i] + push[i];
    /// <summary>The spotter says it's alongside.</summary>
    public bool Beside(int i) => lane[i] != 0;

    /// <summary>Where every car is starts again (off track, a jump in the telemetry); the usual steering is kept.</summary>
    public void Reset()
    {
        if (!going) return;
        going = false;
        foreach (var a in new[] { speed, heading, target, shown, shownSpeed, push, ahead }) Array.Clear(a);
        Array.Clear(lane);
    }

    /// <summary>The next reading: each car's place on the lap (0–1; − not in the world), steering, class, whether it's on
    /// the track, whether it can be near yours (not in the pits; off for those too far too, after), and the spotter's
    /// CarLeftRight. Another track's length starts it all again.</summary>
    public void Step(int me, float trackLength, float dt, float[] pct, float[] steer, int[] carClass, bool[] onTrack, bool[] counts, int leftRight)
    {
        if (trackLength != length)
        {
            Reset();
            usual.Clear();
            length = trackLength;
        }
        if (dt <= 0) Reset();
        if (!going)
        {
            Array.Copy(pct, pctWas, cars);
            Array.Fill(cellWas, -1);
            going = true;
            dt = 0;
        }
        // yours first: the others are beside it
        float mine = Sideways(me, dt, pct, steer, carClass, onTrack) ?? 0;
        for (int i = 0; i < cars; i++)
        {
            if (i == me) continue;
            float? move = Sideways(i, dt, pct, steer, carClass, onTrack);
            ahead[i] = Along(pct[i] - pct[me]);
            if (!counts[i] || pct[i] < 0 || Math.Abs(ahead[i]) > Reach)
            {
                target[i] = shown[i] = shownSpeed[i] = push[i] = 0;
                counts[i] = false;
                continue;
            }
            float most = Math.Max(MostAlone, Math.Abs(target[i]));
            target[i] = Math.Clamp((target[i] + (move is { } m ? (m * Theirs - mine) * dt : 0)) * MathF.Exp(-dt / AcrossSeconds), -most, most);
        }
        Spotter(me, dt, counts, leftRight);
        Array.Copy(pct, pctWas, cars);
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / 0.02f));
        for (int i = 0; i < cars; i++)
        {
            if (i == me || !counts[i]) continue;
            // as a mass on a spring (no overshoot), quicker while it has to get out from behind or ahead of yours
            float w = lane[i] != 0 && Math.Abs(shown[i]) < 2 ? 9 : 5, t = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                shownSpeed[i] += (w * w * (target[i] - shown[i]) - 2 * w * shownSpeed[i]) * t;
                shown[i] += shownSpeed[i] * t;
            }
            // (one in line can't be where yours is: its place on the lap is a little out)
            float want = 0, clear = CarMetres * 0.8f * (1 - Math.Clamp(Math.Abs(shown[i]) / 2, 0, 1));
            if (lane[i] == 0 && Math.Abs(ahead[i]) < clear) want = (ahead[i] < 0 ? -clear : clear) - ahead[i];
            push[i] += (want - push[i]) * Math.Min(1, dt * 8);
        }
    }

    /// <summary>A difference of places on the lap, the shorter way round, in metres.</summary>
    private float Along(float d) => (d > 0.5f ? d - 1 : d < -0.5f ? d + 1 : d) * length;

    /// <summary>How fast a car moves to the right of the usual line (m/s), or null where it can't be told; learns the
    /// usual steering as it goes.</summary>
    private float? Sideways(int i, float dt, float[] pct, float[] steer, int[] carClass, bool[] onTrack)
    {
        if (dt <= 0) return null;
        float p = pct[i], v = Along(p - pctWas[i]) / dt, s = steer[i];
        // (not in the world, or put somewhere else: from there)
        if (p < 0 || pctWas[i] < 0 || Math.Abs(v) > 110 || !float.IsFinite(s))
        {
            speed[i] = heading[i] = 0;
            cellWas[i] = -1;
            return null;
        }
        speed[i] += (Math.Max(-5, v) - speed[i]) * (1 - MathF.Exp(-dt / 0.075f));
        bool driving = onTrack[i] && speed[i] > SlowestMs;
        if (!usual.TryGetValue(carClass[i], out var at))
        {
            int n = (int)(length / CellMetres) + 1;
            usual[carClass[i]] = at = (new float[n], new int[n]);
        }
        int cell = Math.Clamp((int)(p * length / CellMetres), 0, at.Steer.Length - 1);
        float u = at.Steer[cell];
        bool told = driving && at.Count[cell] >= Enough && Math.Abs(u) < SteerMost && Math.Abs(s) < SteerMost + 0.1f;
        heading[i] = (heading[i] + (told ? Curve * (s - u) * speed[i] * dt : 0)) * MathF.Exp(-dt / (told ? HeadingSeconds : 0.3f));
        if (driving && cell != cellWas[i])
        {
            cellWas[i] = cell;
            at.Count[cell] = Math.Min(Mean, at.Count[cell] + 1);
            at.Steer[cell] += (s - u) / at.Count[cell];
        }
        return told ? heading[i] * speed[i] : null;
    }

    /// <summary>The spotter's word: the cars overlapping that fit it best (the nearest, and on that side already) go to
    /// its lanes.</summary>
    private void Spotter(int me, float dt, bool[] counts, int leftRight)
    {
        Array.Clear(lane);
        // irsdk_CarLeftRight: 0 off, 1 clear, 2 a car left, 3 right, 4 both, 5 two left, 6 two right
        int left = leftRight switch { 2 or 4 => 1, 5 => 2, _ => 0 }, right = leftRight switch { 3 or 4 => 1, 6 => 2, _ => 0 };
        int l = 0, r = 0;
        while (l < left || r < right)
        {
            int best = -1, side = 0;
            float cost = float.MaxValue;
            for (int i = 0; i < cars; i++)
            {
                if (i == me || !counts[i] || lane[i] != 0 || Math.Abs(ahead[i]) > Overlapping) continue;
                float c = Math.Abs(ahead[i]);
                if (l < left && c + target[i] < cost) (best, side, cost) = (i, -1, c + target[i]);
                if (r < right && c - target[i] < cost) (best, side, cost) = (i, 1, c - target[i]);
            }
            if (best < 0) break;
            lane[best] = side < 0 ? -++l : ++r;
            target[best] += (lane[best] * LaneMetres - target[best]) * (1 - MathF.Exp(-dt * 5));
        }
    }
}
