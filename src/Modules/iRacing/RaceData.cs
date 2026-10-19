using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Logging;
using DaisysApp.Settings;
using static DaisysApp.Applets.Gaming.IRacing.IRacingApi;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>One session of yours (a row of iRacing's results search: a race, qualifying, practice or time trial), as kept on this PC.</summary>
public sealed class Race
{
    public long SubsessionId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool Hosted { get; set; }
    /// <summary>Race, Qualify, Practice or Time Trial (a hosted session with a race in it is a Race).</summary>
    public string Session { get; set; } = Sessions.Race;
    public bool Official { get; set; }
    public string Category { get; set; } = "";
    public int SeriesId { get; set; }
    public string Series { get; set; } = "";
    public int SeasonYear { get; set; }
    public int SeasonQuarter { get; set; }
    public int Week { get; set; }
    public int TrackId { get; set; }
    public string Track { get; set; } = "";
    public string Config { get; set; } = "";
    public int CarId { get; set; }
    public string Car { get; set; } = "";
    public string CarClass { get; set; } = "";
    public int Drivers { get; set; }
    public int Sof { get; set; }
    /// <summary>As iRacing gives them (counted from 0 or 1: see <see cref="RaceData.FromZero"/>).</summary>
    public int StartRaw { get; set; } = -1;
    public int FinishRaw { get; set; } = -1;
    public int FinishInClassRaw { get; set; } = -1;
    public int Laps { get; set; }
    public int LeaderLaps { get; set; } = -1;
    public int LapsLed { get; set; }
    public int Incidents { get; set; }
    public int Points { get; set; }
    public int OldIRating { get; set; } = -1;
    public int NewIRating { get; set; } = -1;
    public int OldSubLevel { get; set; } = -1;
    public int NewSubLevel { get; set; } = -1;
    public int Cautions { get; set; } = -1;
    public int LeadChanges { get; set; } = -1;
    public string Winner { get; set; } = "";

    // ---- worked out when loaded (positions from 1)

    [JsonIgnore] public int StartPos { get; set; }
    [JsonIgnore] public int Finish { get; set; }
    [JsonIgnore] public int FinishInClass { get; set; }
    /// <summary>The track's length in km (from iRacing's track list), 0 if not known.</summary>
    [JsonIgnore] public double TrackKm { get; set; }

    [JsonIgnore] public DateTime Local => Start.ToLocalTime();
    /// <summary>The track without iRacing's "[Retired]" or "[Legacy]" (its older versions count with it).</summary>
    [JsonIgnore] public string TrackName => Unversioned(Track);
    [JsonIgnore] public string TrackFull => Config.Length == 0 || Config == "N/A" ? Track : Track + " – " + Config;
    [JsonIgnore] public int Gained => StartPos > 0 && Finish > 0 ? StartPos - Finish : 0;
    [JsonIgnore] public int IRatingChange => OldIRating > 0 && NewIRating > 0 ? NewIRating - OldIRating : 0;
    [JsonIgnore] public bool HasIRating => OldIRating > 0 && NewIRating > 0;
    [JsonIgnore] public double SafetyChange => OldSubLevel >= 0 && NewSubLevel >= 0 ? (NewSubLevel - OldSubLevel) / 100.0 : 0;
    [JsonIgnore] public bool Win => Finish == 1;
    [JsonIgnore] public bool Podium => Finish is >= 1 and <= 3;
    [JsonIgnore] public bool Top5 => Finish is >= 1 and <= 5;
    [JsonIgnore] public bool Pole => StartPos == 1;
    [JsonIgnore] public bool Clean => Incidents == 0;
    /// <summary>Didn't finish on the lead lap's distance by more than a lap.</summary>
    [JsonIgnore] public bool Dnf => LeaderLaps > 0 && Laps < LeaderLaps - 1;
    [JsonIgnore] public double Km => TrackKm * Laps;
    [JsonIgnore] public string Kind => Hosted ? T("Hosted") : Official ? T("Official") : T("Unofficial");
    [JsonIgnore] public string Season => SeasonYear > 0 ? $"{SeasonYear} S{SeasonQuarter}" : "";
    [JsonIgnore] public bool IsRace => Session == Sessions.Race;

    private static string Unversioned(string s)
    {
        foreach (var p in new[] { "[Retired] ", "[Legacy] " })
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return s[p.Length..].Trim();
        return s;
    }
    [JsonIgnore] public string SessionText => Sessions.Text(Session);
}

/// <summary>The kinds of session.</summary>
public static class Sessions
{
    public const string Race = "Race", Qualify = "Qualify", Practice = "Practice", TimeTrial = "Time Trial";

    public static string Text(string s) => s switch
    {
        Race => T("Race"),
        Qualify => T("Qualifying"),
        Practice => T("Practice"),
        TimeTrial => T("Time trial"),
        _ => s,
    };

    /// <summary>A licence class from its number in iRacing's records (1 Rookie, 2 D, 3 C, 4 B, 5 A, 6 Pro).</summary>
    public static string Class(int n) => n switch { 1 => "R", 2 => "D", 3 => "C", 4 => "B", 5 => "A", 6 => "P", _ => "" };

    /// <summary>A licence and safety rating as the graphs keep it: the class's number and a tenth of the rating (A 3.10 is 5.31).</summary>
    public static string Licence(double v)
    {
        int c = (int)Math.Floor(v + 1e-9);
        return $"{Class(c)} {(v - c) * 10:0.00}".Trim();
    }
}

public sealed class License
{
    public string Category { get; set; } = "";
    public string Class { get; set; } = "";
    public double SafetyRating { get; set; }
    public int IRating { get; set; }
    public int TTRating { get; set; }
    public double Cpi { get; set; }
    public string Color { get; set; } = "";
}

/// <summary>A category's totals as iRacing counts them (stats/member_career, member_yearly).</summary>
public sealed class CareerStat
{
    public string Category { get; set; } = "";
    public int Year { get; set; }
    public int Starts { get; set; }
    public int Wins { get; set; }
    public int Top5 { get; set; }
    public int Poles { get; set; }
    public double AvgStart { get; set; }
    public double AvgFinish { get; set; }
    public int Laps { get; set; }
    public int LapsLed { get; set; }
    public double AvgIncidents { get; set; }
    public double AvgPoints { get; set; }
    public double WinPct { get; set; }
    public double Top5Pct { get; set; }
    public double LapsLedPct { get; set; }
}

public sealed record ChartPoint(DateTime When, double Value);

/// <summary>
/// Everything fetched from iRacing, kept in %LOCALAPPDATA%\DaisysApp\iRacing\data.json so it's there at once and only
/// what's new is fetched: you, your licences, iRacing's own career and yearly totals, your iRating and safety rating
/// over time per category, and every race (official series and, if wanted, hosted).
/// </summary>
public sealed class RaceData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long CustId { get; set; }
    public string Name { get; set; } = "";
    public DateTime? MemberSince { get; set; }
    public string Club { get; set; } = "";
    public List<License> Licenses { get; set; } = new();
    public List<CareerStat> Career { get; set; } = new();
    public List<CareerStat> Yearly { get; set; } = new();
    public Dictionary<string, List<ChartPoint>> IRating { get; set; } = new();
    public Dictionary<string, List<ChartPoint>> Safety { get; set; } = new();
    public List<Race> Races { get; set; } = new();
    /// <summary>Track id → length in km.</summary>
    public Dictionary<int, double> TrackKm { get; set; } = new();
    public Dictionary<int, string> CarNames { get; set; } = new();
    /// <summary>Races searched up to here (official, hosted).</summary>
    public DateTime? OfficialThrough { get; set; }
    public DateTime? HostedThrough { get; set; }
    public DateTime? Synced { get; set; }
    /// <summary>When an export was last imported (RaceImport).</summary>
    public DateTime? Imported { get; set; }
    /// <summary>iRacing's positions count from 0 (the winner is 0); null = not known yet.</summary>
    public bool? FromZero { get; set; }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.ShortName, "iRacing");
    private static string FilePath => Path.Combine(Folder, "data.json");

    public static RaceData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonSerializer.Deserialize<RaceData>(File.ReadAllText(FilePath), JsonStore.Options) ?? new();
                if (d.Version == CurrentVersion) { d.Prepare(); return d; }
            }
        }
        catch (Exception ex) { Log.Here.Warn("iRacing: the saved races couldn't be read", ex); }
        return new RaceData();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonStore.Options));
        File.Move(tmp, FilePath, true);
    }

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }

    /// <summary>Positions from 1, track lengths and car names filled in.</summary>
    public void Prepare()
    {
        FromZero ??= GuessFromZero();
        int off = FromZero == true ? 1 : 0;
        foreach (var r in Races)
        {
            r.StartPos = r.StartRaw >= 0 ? r.StartRaw + off : 0;
            r.Finish = r.FinishRaw >= 0 ? r.FinishRaw + off : 0;
            r.FinishInClass = r.FinishInClassRaw >= 0 ? r.FinishInClassRaw + off : r.Finish;
            r.TrackKm = TrackKm.GetValueOrDefault(r.TrackId);
            if (r.Car.Length == 0 && CarNames.TryGetValue(r.CarId, out var car)) r.Car = car;
        }
        // a new list (the tab may be reading the old one while a sync runs)
        Races = Races.OrderBy(r => r.Start).ToList();
    }

    /// <summary>A 0 anywhere means they count from 0; otherwise iRacing's own count of wins says which.</summary>
    private bool? GuessFromZero()
    {
        if (Races.Count == 0) return null;
        if (Races.Any(r => r.FinishRaw == 0 || r.StartRaw == 0)) return true;
        int wins = Career.Sum(c => c.Wins);
        int ones = Races.Count(r => r.FinishRaw == 1 && !r.Hosted);
        if (wins == 0) return ones > 0;
        return false;
    }

    // ---------------------------------------------------------------- fetching

    private static readonly (int Id, string Name)[] Categories =
    {
        (1, "Oval"), (2, "Road"), (3, "Dirt Oval"), (4, "Dirt Road"), (5, "Sports Car"), (6, "Formula Car"),
    };

    /// <summary>Fetches what's new: you, the totals and ratings (all again, they're small) and the races since the last
    /// time (all of them, from when you joined, the first time). Progress: what, and how far (0–1).</summary>
    public async Task SyncAsync(IRacingApi api, bool hosted, Action<string, double> progress, CancellationToken cancel)
    {
        progress(T("Signing in…"), 0);
        await api.SignInAsync(cancel);

        progress(T("Reading your profile…"), 0.02);
        var info = await api.GetAsync("/member/info", cancel);
        CustId = (long)(Num(info, "cust_id") ?? 0);
        if (CustId == 0) throw new IRacingException(T("iRacing's answer had no member id."));
        Name = Str(info, "display_name") ?? "";
        MemberSince = Date(info, "member_since");
        Club = Str(info, "club_name") ?? "";
        var licences = Obj(info, "licenses");
        IEnumerable<JsonElement> list = licences.ValueKind == JsonValueKind.Object
            ? licences.EnumerateObject().Select(p => p.Value)
            : licences.ValueKind == JsonValueKind.Array ? licences.EnumerateArray() : Enumerable.Empty<JsonElement>();
        Licenses = list.Select(l => new License
        {
            Category = Str(l, "category_name") ?? Str(l, "category") ?? "",
            Class = Str(l, "group_name") ?? "",
            SafetyRating = Num(l, "safety_rating") ?? 0,
            IRating = Int(l, "irating"),
            TTRating = Int(l, "tt_rating"),
            Cpi = Num(l, "cpi") ?? 0,
            Color = Str(l, "color") ?? "",
        }).Where(l => l.Category.Length > 0).ToList();

        progress(T("Reading your career totals…"), 0.04);
        Career = Stats(await api.GetAsync($"/stats/member_career?cust_id={CustId}", cancel));
        Yearly = Stats(await api.GetAsync($"/stats/member_yearly?cust_id={CustId}", cancel));

        // new dictionaries, put in place when done (the tab may be drawing the old ones)
        var iRating = new Dictionary<string, List<ChartPoint>>(IRating);
        var safety = new Dictionary<string, List<ChartPoint>>(Safety);
        for (int i = 0; i < Categories.Length; i++)
        {
            var (id, name) = Categories[i];
            progress(F("Reading your ratings: {0}…", T(name)), 0.06 + 0.06 * i / Categories.Length);
            try
            {
                var ir = Points(await api.GetAsync($"/member/chart_data?cust_id={CustId}&category_id={id}&chart_type=1", cancel));
                if (ir.Count > 0) iRating[name] = ir; else iRating.Remove(name);
                var sr = Points(await api.GetAsync($"/member/chart_data?cust_id={CustId}&category_id={id}&chart_type=3", cancel));
                // (as 1.00–4.99 for Rookie, or a class and its rating as hundreds: 4.99 or 499, say)
                double div = sr.Count == 0 ? 1 : sr.Max(p => p.Value) > 2000 ? 1000 : sr.Max(p => p.Value) > 20 ? 100 : 1;
                if (sr.Count > 0) safety[name] = sr.Select(p => p with { Value = p.Value / div }).ToList(); else safety.Remove(name);
            }
            catch (IRacingException ex) { Log.Here.Warn($"iRacing: {name} ratings: {ex.Message}"); }
        }
        IRating = iRating;
        Safety = safety;

        var byId = Races.ToDictionary(r => r.SubsessionId);
        var since = MemberSince ?? new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.UtcNow.AddHours(1);

        async Task Search(string kind, DateTime? through, Func<DateTime, DateTime, string> path, bool isHosted, double from, double to, Action<DateTime> done)
        {
            var begin = through is { } t ? t.AddDays(-2) : since;
            if (begin < since) begin = since;
            double total = Math.Max(1, (now - begin).TotalDays);
            while (begin < now)
            {
                cancel.ThrowIfCancellationRequested();
                var end = begin.AddDays(89) < now ? begin.AddDays(89) : now;
                progress(F("{0}: {1:d} to {2:d}… ({3} races so far)", kind, begin.ToLocalTime(), end.ToLocalTime(), byId.Count), from + (to - from) * (begin - (now.AddDays(-total))).TotalDays / total);
                foreach (var row in await api.SearchAsync(path(begin, end), cancel))
                    if (ToRace(row, isHosted) is { } race) byId[race.SubsessionId] = race;
                begin = end;
                done(end.AddHours(-1));
            }
        }

        static string Iso(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm") + "Z";
        await Search(T("Official sessions"), OfficialThrough,
            (b, e) => $"/results/search_series?cust_id={CustId}&start_range_begin={Iso(b)}&start_range_end={Iso(e)}",
            false, 0.12, hosted ? 0.7 : 0.94, d => OfficialThrough = d);
        if (hosted)
        {
            try
            {
                await Search(T("Hosted sessions"), HostedThrough,
                    (b, e) => $"/results/search_hosted?cust_id={CustId}&start_range_begin={Iso(b)}&start_range_end={Iso(e)}",
                    true, 0.7, 0.94, d => HostedThrough = d);
            }
            catch (IRacingException ex) { Log.Here.Warn("iRacing: hosted races: " + ex.Message); }
        }
        Races = byId.Values.ToList();

        // car names and track lengths, when there's one not known yet
        if (Races.Any(r => r.CarId > 0 && r.Car.Length == 0 && !CarNames.ContainsKey(r.CarId)) || CarNames.Count == 0)
        {
            progress(T("Reading the cars…"), 0.95);
            try
            {
                var cars = await api.GetAsync("/car/get", cancel);
                if (cars.ValueKind == JsonValueKind.Array)
                    foreach (var c in cars.EnumerateArray())
                        if (Int(c, "car_id") is > 0 and int id && Str(c, "car_name") is { } n) CarNames[id] = n;
            }
            catch (IRacingException ex) { Log.Here.Warn("iRacing: cars: " + ex.Message); }
        }
        if (Races.Any(r => r.TrackId > 0 && !TrackKm.ContainsKey(r.TrackId)))
        {
            progress(T("Reading the tracks…"), 0.97);
            try
            {
                var tracks = await api.GetAsync("/track/get", cancel);
                if (tracks.ValueKind == JsonValueKind.Array)
                    foreach (var t in tracks.EnumerateArray())
                        if (Int(t, "track_id") is > 0 and int id && Num(t, "track_config_length") is > 0 and double miles) TrackKm[id] = miles * 1.609344;
            }
            catch (IRacingException ex) { Log.Here.Warn("iRacing: tracks: " + ex.Message); }
        }

        Synced = DateTime.UtcNow;
        FromZero = null;
        Prepare();
        Save();
        Log.Here.Info($"iRacing: synced, {Races.Count} races ({Races.Count(r => r.Hosted)} hosted)");
        progress(P(Races.Count, "{0} race", "{0} races"), 1);
    }

    private static List<CareerStat> Stats(JsonElement e) => Arr(e, "stats").Select(s => new CareerStat
    {
        Category = Str(s, "category") ?? "",
        Year = Int(s, "year"),
        Starts = Int(s, "starts"),
        Wins = Int(s, "wins"),
        Top5 = Int(s, "top5"),
        Poles = Int(s, "poles"),
        AvgStart = Num(s, "avg_start_position") ?? 0,
        AvgFinish = Num(s, "avg_finish_position") ?? 0,
        Laps = Int(s, "laps"),
        LapsLed = Int(s, "laps_led"),
        AvgIncidents = Num(s, "avg_incidents") ?? 0,
        AvgPoints = Num(s, "avg_points") ?? 0,
        WinPct = Num(s, "win_percentage") ?? 0,
        Top5Pct = Num(s, "top5_percentage") ?? 0,
        LapsLedPct = Num(s, "laps_led_percentage") ?? 0,
    }).ToList();

    internal static List<ChartPoint> Points(JsonElement e) =>
        Arr(e, "data").Select(p => Date(p, "when") is { } w && Num(p, "value") is { } v ? new ChartPoint(w, v) : null)
            .Where(p => p != null).Select(p => p!).OrderBy(p => p.When).ToList();

    /// <summary>A licence category's name from its number (hosted sessions give only that).</summary>
    internal static string? CategoryOf(int id) => Categories.FirstOrDefault(c => c.Id == id).Name;

    internal static Race? ToRace(JsonElement r, bool hosted)
    {
        long id = (long)(Num(r, "subsession_id") ?? 0);
        if (id == 0) return null;
        // a row that isn't about you (no position of yours in it)
        if (Num(r, "finish_position") == null) return null;
        var track = Obj(r, "track");
        // a hosted session is a race if it had one (laps or minutes), otherwise practice (a track day)
        string session = hosted
            ? (Int(r, "race_laps") > 0 || Int(r, "race_length") > 0 || Int(r, "starting_position", -1) >= 0 ? Sessions.Race : Sessions.Practice)
            : Str(r, "event_type_name") switch
            {
                "Practice" => Sessions.Practice,
                "Qualify" => Sessions.Qualify,
                "Time Trial" => Sessions.TimeTrial,
                _ => Num(r, "event_type") switch { 2 => Sessions.Practice, 3 => Sessions.Qualify, 4 => Sessions.TimeTrial, _ => Sessions.Race },
            };
        var firstCar = Arr(r, "cars").FirstOrDefault();
        return new Race
        {
            SubsessionId = id,
            Start = Date(r, "start_time") ?? Date(r, "launch_at") ?? DateTime.MinValue,
            End = Date(r, "end_time") ?? DateTime.MinValue,
            Hosted = hosted,
            Session = session,
            Official = !hosted && Bool(r, "official_session"),
            Category = Str(r, "license_category") ?? CategoryOf(Int(r, "license_category_id")) ?? Str(track, "category") ?? "",
            SeriesId = Int(r, "series_id"),
            Series = Str(r, "series_name") ?? (hosted ? Str(r, "session_name") ?? T("Hosted") : ""),
            SeasonYear = Int(r, "season_year"),
            SeasonQuarter = Int(r, "season_quarter"),
            Week = Int(r, "race_week_num", -1) + 1,
            TrackId = Int(track, "track_id"),
            Track = Str(track, "track_name") ?? "",
            Config = Str(track, "config_name") ?? "",
            CarId = Int(r, "car_id", Int(firstCar, "car_id")),
            Car = Str(r, "car_name") ?? Str(firstCar, "car_name") ?? "",
            CarClass = Str(r, "car_class_name") ?? Str(r, "car_class_short_name") ?? Str(firstCar, "car_class_name") ?? "",
            Drivers = Int(r, "num_drivers"),
            Sof = Math.Max(0, Int(r, "event_strength_of_field")),
            StartRaw = Int(r, "starting_position", -1),
            FinishRaw = Int(r, "finish_position", -1),
            FinishInClassRaw = Int(r, "finish_position_in_class", -1),
            Laps = Math.Max(0, Int(r, "laps_complete")),
            LeaderLaps = Int(r, "event_laps_complete", -1),
            LapsLed = Math.Max(0, Int(r, "laps_led", Int(r, "laps_lead"))),
            Incidents = Math.Max(0, Int(r, "incidents")),
            Points = Math.Max(0, Int(r, "champ_points")),
            OldIRating = Int(r, "oldi_rating", -1),
            NewIRating = Int(r, "newi_rating", -1),
            OldSubLevel = Int(r, "old_sub_level", -1),
            NewSubLevel = Int(r, "new_sub_level", -1),
            Cautions = Int(r, "num_cautions", -1),
            LeadChanges = Int(r, "num_lead_changes", -1),
            Winner = Str(r, "winner_name") ?? "",
        };
    }
}
