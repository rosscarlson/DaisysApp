namespace DaisysApp.Applets.Gaming.ForzaAnalysis;

/// <summary>Something worth a look at one moment of a session, marked on the trace.</summary>
internal enum MarkerKind { SnapRelease, Reapply, Spike, SlowApply, Stab, Hesitation, Overlap }

internal sealed record Marker(int At, MarkerKind Kind, string Text);

/// <summary>
/// One brake zone: from the foot leaving the floor to the pedal being let go, its parts timed (application, hold,
/// release) and scored. Indexes are samples (<see cref="SessionData.Hz"/> a second).
/// </summary>
internal sealed class BrakeZone
{
    public int Number { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public int PeakAt { get; init; }
    public int ReleaseFrom { get; init; }
    public double Peak { get; init; }
    public double Hold { get; init; }
    public double ApplyMs { get; init; }
    public double ApplyRate { get; init; }
    public double Overshoot { get; init; }
    public double HoldS { get; init; }
    public int Wobbles { get; init; }
    public double ReleaseS { get; init; }
    public double ReleaseSmooth { get; init; }
    public int Reapplies { get; init; }
    public double SnapFrom { get; init; }
    public double TrailPct { get; init; }
    public double OverlapS { get; init; }
    public double CoastS { get; init; } = double.NaN;
    public double PinnedS { get; init; }
    public double ApplyScore { get; init; }
    public double ReleaseScore { get; init; }
    public List<string> Notes { get; } = new();

    public double At => Start * SessionData.Dt;
    public double LengthS => (End - Start) * SessionData.Dt;
    public string NotesText => string.Join("; ", Notes);

    // for the table
    public string Clock => SessionChart.Clock(At);
    public double PeakPct => Peak * 100;
    public double ReleaseSmoothPct => ReleaseSmooth * 100;
    public double? Trail => double.IsNaN(TrailPct) ? null : TrailPct;
    public double? Coast => double.IsNaN(CoastS) ? null : CoastS;
}

/// <summary>One throttle application (a rise of a quarter or more) or release (a drop of as much).</summary>
internal sealed class ThrottleMove
{
    public int Number { get; init; }
    public bool Up { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public double From { get; init; }
    public double To { get; init; }
    public double Ms { get; init; }
    public double Rate { get; init; }
    public double Smooth { get; init; }
    public int Hesitations { get; init; }
    public bool Stab { get; set; }
    public double Score { get; set; }
    public List<string> Notes { get; } = new();

    public double At => Start * SessionData.Dt;
    public string NotesText => string.Join("; ", Notes);

    // for the table
    public string Clock => SessionChart.Clock(At);
    public string Way => Up ? T("On") : T("Off");
    public double FromPct => From * 100;
    public double ToPct => To * 100;
    public double SmoothPct => Smooth * 100;
}

/// <summary>
/// A session analysed for how smoothly the pedals are used, as a driving coach (or Racing Labs) would look at it: brake
/// zones (how fast and firm the application is, a spike and back off, how steady the hold, how gradual and clean the
/// release, trail braking into the turn, overlap with the throttle, coasting after), throttle applications and
/// releases (progressive or stabbed, hesitations), steering corrections, the time split (full throttle, part throttle,
/// braking, coasting, both), consistency between zones, and tips from all that. Scores are 0–100.
/// </summary>
internal sealed class PedalAnalysis
{
    /// <summary>Bumped when the analysis changes, so cached summaries are worked out again.</summary>
    public const int Version = 1;

    private const double On = 0.05, Off = 0.02, Foot = 0.005;
    private const int Hz = SessionData.Hz;

    public SessionData Data { get; }
    public double[] SmoothBrake { get; }
    public double[] SmoothThrottle { get; }
    public List<BrakeZone> Brakes { get; } = new();
    public List<ThrottleMove> Throttle { get; } = new();
    public List<Marker> Markers { get; } = new();
    public List<string> Tips { get; } = new();

    public bool HasBrake { get; }
    public bool HasThrottle { get; }
    public bool HasSteer { get; }

    public double ActiveS { get; private set; }
    public double FullThrottlePct { get; private set; }
    public double PartThrottlePct { get; private set; }
    public double BrakingPct { get; private set; }
    public double CoastPct { get; private set; }
    public double OverlapPct { get; private set; }

    public double BrakeApplyScore { get; private set; } = double.NaN;
    public double BrakeReleaseScore { get; private set; } = double.NaN;
    public double ThrottleApplyScore { get; private set; } = double.NaN;
    public double ThrottleReleaseScore { get; private set; } = double.NaN;
    public double SteeringScore { get; private set; } = double.NaN;
    public double ConsistencyScore { get; private set; } = double.NaN;
    public double OverallScore { get; private set; } = double.NaN;

    public double SteerCorrectionsPerMin { get; private set; }
    public double ThrottleToBrakeS { get; private set; } = double.NaN;
    public int Stabs { get; private set; }

    public PedalAnalysis(SessionData data)
    {
        Data = data;
        HasBrake = data.Brake.Any(v => !double.IsNaN(v));
        HasThrottle = data.Throttle.Any(v => !double.IsNaN(v));
        HasSteer = data.Steer.Any(v => !double.IsNaN(v));
        SmoothBrake = Smooth(data.Brake, 3);
        SmoothThrottle = Smooth(data.Throttle, 3);
        TimeSplit();
        if (HasBrake) FindBrakeZones();
        if (HasThrottle) FindThrottleMoves();
        if (HasSteer) SteeringCorrections();
        Score();
        MakeTips();
        Markers.Sort((a, b) => a.At.CompareTo(b.At));
    }

    // ---------------------------------------------------------------- the time split

    private void TimeSplit()
    {
        int n = Data.Count;
        var b = SmoothBrake;
        var t = SmoothThrottle;
        var s = Data.Steer;
        // active: within 3 s of something moving (time parked in a menu doesn't count)
        var moving = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            bool m = b[i] > Off || t[i] > Off || (i > 0 && !double.IsNaN(s[i]) && !double.IsNaN(s[i - 1]) && Math.Abs(s[i] - s[i - 1]) > 0.0005);
            moving[i + 1] = moving[i] + (m ? 1 : 0);
        }
        int full = 0, part = 0, brake = 0, coast = 0, both = 0, active = 0;
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - 3 * Hz), hi = Math.Min(n, i + 3 * Hz);
            if (moving[hi] - moving[lo] == 0) continue;
            active++;
            bool tb = t[i] > On, bb = b[i] > On;
            if (tb && bb) both++;
            else if (bb) brake++;
            else if (t[i] >= 0.97) full++;
            else if (tb) part++;
            else coast++;
        }
        ActiveS = active * SessionData.Dt;
        double Pct(int x) => active == 0 ? 0 : 100.0 * x / active;
        FullThrottlePct = Pct(full);
        PartThrottlePct = Pct(part);
        BrakingPct = Pct(brake);
        CoastPct = Pct(coast);
        OverlapPct = Pct(both);
    }

    // ---------------------------------------------------------------- brake zones

    private void FindBrakeZones()
    {
        var b = SmoothBrake;
        int n = b.Length;
        int i = 0;
        while (i < n)
        {
            if (!(b[i] > On)) { i++; continue; }
            int s = i;
            while (s > 0 && b[s - 1] > Foot && b[s - 1] <= b[s] + 0.002) s--;
            int e = i;
            while (true)
            {
                while (e + 1 < n && b[e + 1] > Off) e++;
                // let go for less than 0.15 s and pressed again: the same zone (a re-application)
                int again = -1;
                for (int k = e + 1; k < Math.Min(n, e + 1 + Hz * 15 / 100); k++) if (b[k] > On) { again = k; break; }
                if (again < 0) break;
                e = again;
            }
            while (e + 1 < n && b[e + 1] > Foot && b[e + 1] <= b[e] + 0.002) e++;
            i = e + 1;
            double peak = 0;
            int peakAt = s;
            for (int k = s; k <= e; k++) if (b[k] > peak) { peak = b[k]; peakAt = k; }
            if (peak < 0.08 || e - s < Hz * 15 / 100) continue;
            Brakes.Add(Zone(Brakes.Count + 1, s, e, peak, peakAt));
        }
    }

    private BrakeZone Zone(int number, int s, int e, double peak, int peakAt)
    {
        var b = SmoothBrake;
        var thr = Data.Throttle;
        var steer = Data.Steer;
        int i10 = s, i90 = s;
        while (i10 < peakAt && b[i10] < 0.1 * peak) i10++;
        i90 = i10;
        while (i90 < peakAt && b[i90] < 0.9 * peak) i90++;
        double applyMs = (i90 - i10) * 1000.0 / Hz;
        double applyRate = 0.8 * peak * 100 / Math.Max(SessionData.Dt, (i90 - i10) * SessionData.Dt);

        // the level held: the upper part of what follows the application (a spike at the start isn't it)
        var after = new List<double>();
        for (int k = i90; k <= e; k++) after.Add(b[k]);
        after.Sort();
        double hold = after.Count == 0 ? peak : after[Math.Min(after.Count - 1, (int)(after.Count * 0.8))];
        int r0 = e;
        while (r0 > i90 && b[r0] < 0.9 * hold) r0--;
        double holdS = (r0 - i90) * SessionData.Dt;
        double overshoot = peakAt <= i90 + Hz / 2 && holdS >= 0.3 ? Math.Max(0, peak - hold) : 0;

        var (applyNet, applyTv, _, applyDowns) = Travel(b, s, i90, 0.01, 0.03);
        var (_, _, holdUps, holdDowns) = Travel(b, i90, r0, 0.03, 0.03);
        var (relNet, relTv, reapplies, _) = Travel(b, r0, e, 0.01, 0.03);
        double applySmooth = applyTv <= 0 ? 1 : Math.Clamp(Math.Abs(applyNet) / applyTv, 0, 1);
        double releaseSmooth = relTv <= 0 ? 1 : Math.Clamp(Math.Abs(relNet) / relTv, 0, 1);
        double releaseS = (e - r0) * SessionData.Dt;
        double snapFrom = b[Math.Max(r0, e - Hz / 10)];

        int trail = 0, overlap = 0, pinned = 0, steerKnown = 0;
        for (int k = s; k <= e; k++)
        {
            if (thr[k] > On) overlap++;
            if (b[k] >= 0.98) pinned++;
            if (k >= r0 && !double.IsNaN(steer[k])) { steerKnown++; if (Math.Abs(steer[k] - 0.5) * 2 >= 0.05) trail++; }
        }
        double coast = double.NaN;
        for (int k = e + 1; k < Math.Min(Data.Count, e + 1 + 5 * Hz); k++)
        {
            if (thr[k] > On) { coast = (k - e) * SessionData.Dt; break; }
            if (b[k] > On) break;
        }

        // scores: the application firm and in one movement; the release gradual, in one movement, and not let go at the end
        double applyScore = 100 * applySmooth * applySmooth;
        if (peak >= 0.5 && applyMs > 300) applyScore *= 1 - Math.Min(0.4, (applyMs - 300) / 1000);
        if (overshoot > 0.08) applyScore *= 1 - Math.Min(0.3, overshoot);
        double releaseScore = 100 * releaseSmooth * releaseSmooth;
        releaseScore *= 1 - 0.6 * Math.Clamp((snapFrom - 0.15) / 0.6, 0, 1);
        if (releaseS < 0.15 && peak > 0.3) releaseScore *= 0.7;

        var z = new BrakeZone
        {
            Number = number,
            Start = s,
            End = e,
            PeakAt = peakAt,
            ReleaseFrom = r0,
            Peak = peak,
            Hold = hold,
            ApplyMs = applyMs,
            ApplyRate = applyRate,
            Overshoot = overshoot,
            HoldS = holdS,
            Wobbles = holdUps + holdDowns,
            ReleaseS = releaseS,
            ReleaseSmooth = releaseSmooth,
            Reapplies = reapplies,
            SnapFrom = snapFrom,
            TrailPct = steerKnown == 0 ? double.NaN : 100.0 * trail / steerKnown,
            OverlapS = overlap * SessionData.Dt,
            CoastS = coast,
            PinnedS = pinned * SessionData.Dt,
            ApplyScore = Math.Clamp(applyScore, 0, 100),
            ReleaseScore = Math.Clamp(releaseScore, 0, 100),
        };
        if (snapFrom > 0.3)
        {
            z.Notes.Add(F("let go from {0:0}%", snapFrom * 100));
            Markers.Add(new Marker(Math.Max(r0, e - Hz / 10), MarkerKind.SnapRelease, F("Brake zone {0}: let go from {1:0}% in under 0.1 s", number, snapFrom * 100)));
        }
        if (reapplies > 0)
        {
            z.Notes.Add(P(reapplies, "pressed again while releasing", "pressed again {0} times while releasing"));
            Markers.Add(new Marker(r0, MarkerKind.Reapply, F("Brake zone {0}: pressed again while releasing", number)));
        }
        if (overshoot > 0.08)
        {
            z.Notes.Add(F("spiked to {0:0}%, held {1:0}%", peak * 100, hold * 100));
            Markers.Add(new Marker(peakAt, MarkerKind.Spike, F("Brake zone {0}: spiked to {1:0}% then backed off to {2:0}%", number, peak * 100, hold * 100)));
        }
        if (peak >= 0.5 && applyMs > 300)
        {
            z.Notes.Add(F("slow to build ({0:0.00} s)", applyMs / 1000));
            Markers.Add(new Marker(i10, MarkerKind.SlowApply, F("Brake zone {0}: {1:0.00} s to build the pressure", number, applyMs / 1000)));
        }
        if (z.Wobbles >= 3) z.Notes.Add(F("pressure varied {0} times while holding", z.Wobbles));
        if (z.OverlapS >= 0.2)
        {
            z.Notes.Add(F("throttle on too ({0:0.0} s)", z.OverlapS));
            Markers.Add(new Marker(s, MarkerKind.Overlap, F("Brake zone {0}: throttle and brake together for {1:0.0} s", number, z.OverlapS)));
        }
        if (z.PinnedS >= 0.3) z.Notes.Add(F("at 100% for {0:0.0} s", z.PinnedS));
        return z;
    }

    // ---------------------------------------------------------------- the throttle

    private void FindThrottleMoves()
    {
        var t = SmoothThrottle;
        var turns = ZigZag(t, 0.08);
        int up = 0, down = 0;
        for (int k = 0; k + 1 < turns.Count; k++)
        {
            int a = turns[k], p = turns[k + 1];
            double from = t[a], to = t[p];
            if (Math.Abs(to - from) < 0.25) continue;
            bool rising = to > from;
            double span = to - from;
            // the move's own part: 10% to 90% of the way, and where it got (95%)
            int i10 = a, i90, reach;
            while (i10 < p && (t[i10] - from) / span < 0.1) i10++;
            i90 = i10;
            while (i90 < p && (t[i90] - from) / span < 0.9) i90++;
            reach = i90;
            while (reach < p && (t[reach] - from) / span < 0.95) reach++;
            double ms = (i90 - i10) * 1000.0 / Hz;
            var (net, tv, ups, downs) = Travel(t, a, reach, 0.01, 0.03);
            double smooth = tv <= 0 ? 1 : Math.Clamp(Math.Abs(net) / tv, 0, 1);
            var m = new ThrottleMove
            {
                Number = rising ? ++up : ++down,
                Up = rising,
                Start = a,
                End = reach,
                From = from,
                To = to,
                Ms = ms,
                Rate = Math.Abs(span) * 0.8 * 100 / Math.Max(SessionData.Dt, ms / 1000),
                Smooth = smooth,
                Hesitations = rising ? downs : ups,
                Score = 100 * smooth * smooth,
            };
            if (rising && to >= 0.97 && from < 0.1 && ms < 100) { m.Notes.Add(T("straight to full")); m.Score *= 0.9; }
            if (!rising && to > 0.1) m.Notes.Add(F("part lift to {0:0}%", to * 100));
            if (m.Hesitations > 0)
            {
                m.Notes.Add(rising ? P(m.Hesitations, "lifted on the way", "lifted {0} times on the way") : P(m.Hesitations, "pressed again on the way", "pressed again {0} times on the way"));
                if (rising) Markers.Add(new Marker(a, MarkerKind.Hesitation, F("Throttle {0}: lifted while applying it", m.Number)));
            }
            Throttle.Add(m);
        }
        // a stab: on by a third or more in 0.15 s and off again by a fifth within 0.3 s
        for (int k = 0; k + 1 < Throttle.Count; k++)
        {
            var a = Throttle[k];
            var b = Throttle[k + 1];
            if (!a.Up || b.Up) continue;
            if (a.To - a.From >= 0.3 && (a.End - a.Start) <= Hz * 15 / 100 && b.Start - a.End <= Hz * 3 / 10 && a.To - b.To >= 0.2)
            {
                a.Stab = true;
                a.Score *= 0.6;
                a.Notes.Add(T("stab: on and straight off again"));
                Stabs++;
                Markers.Add(new Marker(a.Start, MarkerKind.Stab, F("Throttle {0}: a stab, on and straight off again", a.Number)));
            }
        }
        // throttle off to brake on, where the brake follows within a second
        if (HasBrake)
        {
            var gaps = new List<double>();
            foreach (var z in Brakes)
            {
                int k = z.Start;
                while (k > 0 && k > z.Start - Hz && SmoothThrottle[k] <= On) k--;
                if (SmoothThrottle[k] > On && z.Start - k < Hz) gaps.Add((z.Start - k) * SessionData.Dt);
            }
            if (gaps.Count > 0) ThrottleToBrakeS = gaps.Average();
        }
    }

    // ---------------------------------------------------------------- steering

    private void SteeringCorrections()
    {
        var s = Smooth(Data.Steer, 5);
        var turns = ZigZag(s, 0.004);
        // a correction: a turn back of under 13° of Forza's 450° wheel, inside a second
        int corrections = 0;
        for (int k = 1; k + 1 < turns.Count; k++)
        {
            double leg = Math.Abs(s[turns[k + 1]] - s[turns[k]]);
            if (leg < 0.03 && turns[k + 1] - turns[k] < Hz) corrections++;
        }
        SteerCorrectionsPerMin = ActiveS < 1 ? 0 : corrections / (ActiveS / 60);
    }

    // ---------------------------------------------------------------- scores and tips

    private void Score()
    {
        var big = Brakes.Where(z => z.Peak >= 0.15).ToList();
        if (big.Count > 0)
        {
            BrakeApplyScore = Weighted(big, z => z.ApplyScore, z => z.Peak);
            BrakeReleaseScore = Weighted(big, z => z.ReleaseScore, z => z.Peak);
        }
        var ups = Throttle.Where(m => m.Up).ToList();
        var downs = Throttle.Where(m => !m.Up).ToList();
        if (ups.Count > 0) ThrottleApplyScore = Weighted(ups, m => m.Score, m => m.To - m.From);
        if (downs.Count > 0) ThrottleReleaseScore = Weighted(downs, m => m.Score, m => m.From - m.To);
        if (HasSteer && ActiveS >= 30) SteeringScore = 100 * Math.Clamp(1 - (SteerCorrectionsPerMin - 15) / 60, 0, 1);

        var firm = Brakes.Where(z => z.Peak >= 0.3).ToList();
        if (firm.Count >= 4)
        {
            double Cv(Func<BrakeZone, double> of)
            {
                var x = firm.Select(of).ToList();
                double mean = x.Average();
                return mean <= 0 ? 0 : Math.Sqrt(x.Average(v => (v - mean) * (v - mean))) / mean;
            }
            double cv = (Cv(z => z.Peak) + Cv(z => Math.Max(50, z.ApplyMs)) + Cv(z => Math.Max(0.1, z.ReleaseS))) / 3;
            ConsistencyScore = 100 * Math.Clamp(1 - cv, 0, 1);
        }

        double sum = 0, weights = 0;
        void Add(double score, double weight) { if (!double.IsNaN(score)) { sum += score * weight; weights += weight; } }
        Add(BrakeApplyScore, 1);
        Add(BrakeReleaseScore, 1.5);
        Add(ThrottleApplyScore, 1);
        Add(ThrottleReleaseScore, 0.5);
        Add(SteeringScore, 0.5);
        if (weights > 0) OverallScore = sum / weights;
    }

    private static double Weighted<TItem>(List<TItem> items, Func<TItem, double> score, Func<TItem, double> weight)
    {
        double w = items.Sum(weight);
        return w <= 0 ? items.Average(score) : items.Sum(i => score(i) * weight(i)) / w;
    }

    private void MakeTips()
    {
        var firm = Brakes.Where(z => z.Peak >= 0.15).ToList();
        int zones = firm.Count;
        if (!HasBrake) Tips.Add(T("No brake in this session: bind the brake in ForzaWheel to have it analysed."));
        if (!HasThrottle) Tips.Add(T("No throttle in this session: bind the throttle in ForzaWheel to have it analysed."));
        if (zones == 0 && HasBrake) Tips.Add(T("No brake zones found yet: drive a few corners with the overlay showing."));
        if (zones > 0)
        {
            int snaps = firm.Count(z => z.SnapFrom > 0.3);
            if (snaps * 4 >= zones)
                Tips.Add(F("In {0} of {1} brake zones you let go of the brake in one go (from 30% or more in under 0.1 s). Ease off over half a second or more as you turn in: it keeps weight on the front tyres, so the car turns (trail braking).", snaps, zones));
            int reapply = firm.Count(z => z.Reapplies > 0);
            if (reapply > 0)
                Tips.Add(F("In {0} brake zones you pressed the brake again while releasing it. Commit to one smooth release; if you keep running out of room, brake a little earlier instead.", reapply));
            int spikes = firm.Count(z => z.Overshoot > 0.08);
            if (spikes > 0)
                Tips.Add(F("In {0} brake zones you spiked the brake and then backed off. Build the pressure to the level you want and hold it there: a spike can lock the wheels or upset the car.", spikes));
            int slow = firm.Count(z => z.Peak >= 0.5 && z.ApplyMs > 300);
            if (slow * 4 >= zones && slow > 0)
                Tips.Add(F("In {0} brake zones the pressure took over 0.3 s to build. At the braking point press firmly and quickly to the level you need: the most braking is at the start, while the car is fastest.", slow));
            var trail = firm.Where(z => !double.IsNaN(z.TrailPct)).Select(z => z.TrailPct).ToList();
            if (trail.Count >= 3 && trail.Average() < 20)
                Tips.Add(T("You mostly finish braking before you turn (little trail braking). Try carrying some brake into the turn-in and releasing it as the steering goes on."));
            var coast = firm.Where(z => !double.IsNaN(z.CoastS)).Select(z => z.CoastS).ToList();
            if (coast.Count >= 3 && coast.Average() > 0.8)
                Tips.Add(F("After letting go of the brake you coast for {0:0.0} s on average before the throttle. Start feeding the throttle in sooner, gently, once the car has turned.", coast.Average()));
            double pinned = firm.Sum(z => z.PinnedS);
            if (pinned >= 2)
                Tips.Add(F("The brake was at 100% for {0:0.0} s in all. Without ABS that locks the wheels; with it, 80–90% usually stops as well and leaves room to trail off.", pinned));
            if (BrakeReleaseScore >= 85 && zones >= 3) Tips.Add(T("Your brake releases are smooth and gradual: nice."));
        }
        double overlap = Brakes.Sum(z => z.OverlapS);
        if (overlap >= 1)
            Tips.Add(F("Throttle and brake were pressed together for {0:0.0} s. Unless you mean to (left-foot braking to balance the car), keep them apart.", overlap));
        if (Stabs > 0)
            Tips.Add(F("{0} throttle stabs (on and straight off again). Squeeze the throttle on out of corners; if the car steps out, you went to it too soon or too hard.", Stabs));
        int hes = Throttle.Count(m => m.Up && m.Hesitations > 0);
        int apps = Throttle.Count(m => m.Up);
        if (apps > 0 && hes * 5 >= apps && hes >= 2)
            Tips.Add(F("You lifted on the way back onto the throttle {0} times out of {1}. Wait a moment longer, then apply it once, progressively, to the end.", hes, apps));
        if (!double.IsNaN(SteeringScore) && SteeringScore < 60)
            Tips.Add(F("{0:0} small steering corrections a minute: look further ahead and make one steady input for each corner.", SteerCorrectionsPerMin));
        if (!double.IsNaN(ThrottleApplyScore) && ThrottleApplyScore >= 85 && apps >= 3) Tips.Add(T("Your throttle applications are smooth and progressive: nice."));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A centred moving average over <paramref name="width"/> samples (NaN taken as 0 for the pedals' sake; all NaN stays NaN).</summary>
    public static double[] Smooth(double[] x, int width)
    {
        int n = x.Length, h = width / 2;
        var o = new double[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            int c = 0;
            for (int k = Math.Max(0, i - h); k <= Math.Min(n - 1, i + h); k++) if (!double.IsNaN(x[k])) { sum += x[k]; c++; }
            o[i] = c == 0 ? 0 : sum / c;
            any |= c > 0;
        }
        if (!any) Array.Fill(o, double.NaN);
        return o;
    }

    /// <summary>
    /// The travel from <paramref name="a"/> to <paramref name="b"/> with jitter of under <paramref name="deadband"/>
    /// ignored: the net change, the total moved (net / total is 1 for a move in one direction), and how many times it
    /// rose and fell by more than <paramref name="turn"/> (a rise or fall that goes on counts once).
    /// </summary>
    private static (double Net, double Total, int Ups, int Downs) Travel(double[] x, int a, int b, double deadband, double turn)
    {
        if (b <= a) return (0, 0, 0, 0);
        double reference = x[a], total = 0;
        for (int k = a + 1; k <= b; k++)
        {
            if (Math.Abs(x[k] - reference) < deadband) continue;
            total += Math.Abs(x[k] - reference);
            reference = x[k];
        }
        return (x[b] - x[a], total, CountAgainst(x, a, b, turn, up: true), CountAgainst(x, a, b, turn, up: false));
    }

    /// <summary>How many times a series moved against its way by more than <paramref name="turn"/>: up while going down, or down while going up.</summary>
    private static int CountAgainst(double[] x, int a, int b, double turn, bool up)
    {
        // up = count rises of more than 'turn' from a running low; otherwise falls from a running high
        int count = 0;
        double extreme = x[a];
        for (int k = a + 1; k <= b; k++)
        {
            double v = x[k];
            if (up)
            {
                if (v < extreme) extreme = v;
                else if (v - extreme > turn) { count++; extreme = v; while (k + 1 <= b && x[k + 1] >= extreme) { k++; extreme = x[k]; } }
            }
            else
            {
                if (v > extreme) extreme = v;
                else if (extreme - v > turn) { count++; extreme = v; while (k + 1 <= b && x[k + 1] <= extreme) { k++; extreme = x[k]; } }
            }
        }
        return count;
    }

    /// <summary>The turning points of a series that reverses by at least <paramref name="threshold"/>: first and last sample included.</summary>
    private static List<int> ZigZag(double[] x, double threshold)
    {
        var turns = new List<int> { 0 };
        int n = x.Length;
        if (n == 0) return turns;
        int dir = 0, cand = 0, lo = 0, hi = 0;
        for (int i = 1; i < n; i++)
        {
            double v = x[i];
            if (double.IsNaN(v)) continue;
            if (dir == 0)
            {
                if (v <= x[lo]) lo = i;
                if (v >= x[hi]) hi = i;
                if (x[hi] - x[lo] >= threshold)
                {
                    dir = hi > lo ? 1 : -1;
                    turns[0] = dir > 0 ? lo : hi;
                    cand = dir > 0 ? hi : lo;
                }
                continue;
            }
            if (dir > 0)
            {
                if (v >= x[cand]) cand = i;
                else if (x[cand] - v >= threshold) { turns.Add(cand); dir = -1; cand = i; }
            }
            else
            {
                if (v <= x[cand]) cand = i;
                else if (v - x[cand] >= threshold) { turns.Add(cand); dir = 1; cand = i; }
            }
        }
        if (cand != turns[^1]) turns.Add(cand);
        return turns;
    }
}
