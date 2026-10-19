namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>A track's, car's, series' or season's totals: a row of the tables.</summary>
public sealed class RaceGroup
{
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public int Races { get; set; }
    public int Wins { get; set; }
    public int Podiums { get; set; }
    public int Top5 { get; set; }
    public int Poles { get; set; }
    public int Laps { get; set; }
    public int LapsLed { get; set; }
    public int Incidents { get; set; }
    public int Clean { get; set; }
    public int Best { get; set; }
    public double AvgStart { get; set; }
    public double AvgFinish { get; set; }
    public double AvgGained { get; set; }
    public double Km { get; set; }
    public int IRatingChange { get; set; }
    public double AvgSof { get; set; }
    public DateTime Last { get; set; }
    public double WinPct => Races == 0 ? 0 : 100.0 * Wins / Races;
    public double PodiumPct => Races == 0 ? 0 : 100.0 * Podiums / Races;
    public double IncPerRace => Races == 0 ? 0 : (double)Incidents / Races;
    public double IncPer100Laps => Laps == 0 ? 0 : 100.0 * Incidents / Laps;
    public string LastText => Last == default ? "" : Last.ToLocalTime().ToString("d");
    public string BestText => Best > 0 ? "P" + Best : "–";

    public static List<RaceGroup> Of(IEnumerable<Race> races, Func<Race, string> key, Func<IGrouping<string, Race>, string>? detail = null) =>
        races.Where(r => key(r).Length > 0).GroupBy(key).Select(g =>
        {
            var list = g.ToList();
            var finished = list.Where(r => r.Finish > 0).ToList();
            var started = list.Where(r => r.StartPos > 0).ToList();
            var sof = list.Where(r => r.Sof > 0).ToList();
            return new RaceGroup
            {
                Name = g.Key,
                Detail = detail?.Invoke(g) ?? "",
                Races = list.Count,
                Wins = list.Count(r => r.Win),
                Podiums = list.Count(r => r.Podium),
                Top5 = list.Count(r => r.Top5),
                Poles = list.Count(r => r.Pole),
                Laps = list.Sum(r => r.Laps),
                LapsLed = list.Sum(r => r.LapsLed),
                Incidents = list.Sum(r => r.Incidents),
                Clean = list.Count(r => r.Clean),
                Best = finished.Count == 0 ? 0 : finished.Min(r => r.Finish),
                AvgStart = started.Count == 0 ? 0 : started.Average(r => r.StartPos),
                AvgFinish = finished.Count == 0 ? 0 : finished.Average(r => r.Finish),
                AvgGained = list.Count(r => r.StartPos > 0 && r.Finish > 0) is > 0 and int n ? list.Where(r => r.StartPos > 0 && r.Finish > 0).Sum(r => r.Gained) / (double)n : 0,
                Km = list.Sum(r => r.Km),
                IRatingChange = list.Sum(r => r.IRatingChange),
                AvgSof = sof.Count == 0 ? 0 : sof.Average(r => r.Sof),
                Last = list.Max(r => r.Start),
            };
        }).OrderByDescending(g => g.Races).ToList();
}

/// <summary>A record: the race it was, and what it was.</summary>
public sealed record Record(string Title, string Value, Race Race);

/// <summary>Everything worked out from the races shown (after the filters): totals, rates, streaks, records, and the
/// numbers each graph draws.</summary>
public sealed class RaceStats
{
    public IReadOnlyList<Race> Races { get; }
    public int Count => Races.Count;
    public int Wins, Podiums, Top5, Top10, Poles, FrontRow, Laps, LapsLed, Incidents, Clean, Dnfs, Points, LedRaces;
    public double AvgStart, AvgFinish, AvgGained, AvgIncidents, IncPer100Laps, AvgField, AvgSof, Km, Hours;
    public int Tracks, Configs, Cars, Series, Seasons, Days;
    public int IRatingNet, IRatingBestGain, IRatingWorstLoss;
    public double SafetyNet;
    public int WinStreak, PodiumStreak, CleanStreak, Top5Streak, CurrentCleanStreak, DayStreak;
    public int BusiestDayCount;
    public DateTime BusiestDay;
    public DateTime First, Last;
    public string FavouriteTrack = "", FavouriteCar = "", FavouriteSeries = "", FavouriteDay = "", FavouriteHour = "";
    public List<Record> Records { get; } = new();
    public List<RaceGroup> ByTrack { get; }
    public List<RaceGroup> ByTrackConfig { get; }
    public List<RaceGroup> ByCar { get; }
    public List<RaceGroup> BySeries { get; }
    public List<RaceGroup> BySeason { get; }
    public List<RaceGroup> ByYear { get; }
    public List<RaceGroup> ByCategory { get; }
    public List<RaceGroup> ByClass { get; }
    /// <summary>How many times you finished in each place (index 0 = P1), the last counting that place and below.</summary>
    public int[] Finishes { get; } = new int[21];
    public int[] Starts { get; } = new int[21];
    /// <summary>Places gained (index 0 = −10 or worse … 20 = +10 or better).</summary>
    public int[] Gains { get; } = new int[21];
    /// <summary>Incidents per race (index = incidents, the last 16 or more).</summary>
    public int[] IncidentCounts { get; } = new int[17];
    /// <summary>Races at each hour of each day (local time): [day of week Monday first, hour].</summary>
    public int[,] Week { get; } = new int[7, 24];
    public Dictionary<DateTime, int> PerDay { get; } = new();
    /// <summary>Months, oldest first, with races, wins and laps.</summary>
    public List<(DateTime Month, int Races, int Wins, int Podiums, int Laps)> Months { get; } = new();

    public RaceStats(IReadOnlyList<Race> races)
    {
        Races = races;
        ByTrack = RaceGroup.Of(races, r => r.TrackName, g => P(g.Select(r => r.Config).Distinct().Count(), "{0} layout", "{0} layouts"));
        ByTrackConfig = RaceGroup.Of(races, r => r.TrackFull);
        ByCar = RaceGroup.Of(races, r => r.Car, g => string.Join(", ", g.Select(r => r.CarClass).Where(c => c.Length > 0).Distinct().Take(2)));
        BySeries = RaceGroup.Of(races, r => r.Series, g => g.First().Category);
        BySeason = RaceGroup.Of(races, r => r.Season).OrderByDescending(g => g.Name).ToList();
        ByYear = RaceGroup.Of(races, r => r.Local.Year.ToString()).OrderByDescending(g => g.Name).ToList();
        ByCategory = RaceGroup.Of(races, r => r.Category.Length > 0 ? r.Category : T("Other"));
        ByClass = RaceGroup.Of(races, r => r.CarClass);
        if (races.Count == 0) return;

        var finished = races.Where(r => r.Finish > 0).ToList();
        var started = races.Where(r => r.StartPos > 0).ToList();
        Wins = races.Count(r => r.Win);
        Podiums = races.Count(r => r.Podium);
        Top5 = races.Count(r => r.Top5);
        Top10 = races.Count(r => r.Finish is >= 1 and <= 10);
        Poles = races.Count(r => r.Pole);
        FrontRow = races.Count(r => r.StartPos is 1 or 2);
        Laps = races.Sum(r => r.Laps);
        LapsLed = races.Sum(r => r.LapsLed);
        LedRaces = races.Count(r => r.LapsLed > 0);
        Incidents = races.Sum(r => r.Incidents);
        Clean = races.Count(r => r.Clean);
        Dnfs = races.Count(r => r.Dnf);
        Points = races.Sum(r => r.Points);
        AvgStart = started.Count > 0 ? started.Average(r => r.StartPos) : 0;
        AvgFinish = finished.Count > 0 ? finished.Average(r => r.Finish) : 0;
        var both = races.Where(r => r.StartPos > 0 && r.Finish > 0).ToList();
        AvgGained = both.Count > 0 ? both.Average(r => r.Gained) : 0;
        AvgIncidents = (double)Incidents / races.Count;
        IncPer100Laps = Laps > 0 ? 100.0 * Incidents / Laps : 0;
        AvgField = races.Average(r => r.Drivers);
        var sof = races.Where(r => r.Sof > 0).ToList();
        AvgSof = sof.Count > 0 ? sof.Average(r => r.Sof) : 0;
        Km = races.Sum(r => r.Km);
        Hours = races.Where(r => r.End > r.Start && r.End - r.Start < TimeSpan.FromHours(30)).Sum(r => (r.End - r.Start).TotalHours);
        Tracks = ByTrack.Count;
        Configs = ByTrackConfig.Count;
        Cars = ByCar.Count;
        Series = BySeries.Count;
        Seasons = BySeason.Count;
        First = races.Min(r => r.Start);
        Last = races.Max(r => r.Start);

        var rated = races.Where(r => r.HasIRating).ToList();
        IRatingNet = rated.Sum(r => r.IRatingChange);
        IRatingBestGain = rated.Count > 0 ? rated.Max(r => r.IRatingChange) : 0;
        IRatingWorstLoss = rated.Count > 0 ? rated.Min(r => r.IRatingChange) : 0;
        SafetyNet = races.Where(r => r.OldSubLevel >= 0 && r.NewSubLevel >= 0).Sum(r => r.SafetyChange);

        // streaks (in order)
        int Longest(Func<Race, bool> test)
        {
            int best = 0, run = 0;
            foreach (var r in races) { run = test(r) ? run + 1 : 0; best = Math.Max(best, run); }
            return best;
        }
        WinStreak = Longest(r => r.Win);
        PodiumStreak = Longest(r => r.Podium);
        Top5Streak = Longest(r => r.Top5);
        CleanStreak = Longest(r => r.Clean);
        for (int i = races.Count - 1; i >= 0 && races[i].Clean; i--) CurrentCleanStreak++;

        // when
        foreach (var r in races)
        {
            var l = r.Local;
            int dow = ((int)l.DayOfWeek + 6) % 7;
            Week[dow, l.Hour]++;
            PerDay[l.Date] = PerDay.GetValueOrDefault(l.Date) + 1;
            if (r.Finish > 0) Finishes[Math.Min(20, r.Finish - 1)]++;
            if (r.StartPos > 0) Starts[Math.Min(20, r.StartPos - 1)]++;
            if (r.StartPos > 0 && r.Finish > 0) Gains[Math.Clamp(r.Gained, -10, 10) + 10]++;
            IncidentCounts[Math.Min(16, r.Incidents)]++;
        }
        Days = PerDay.Count;
        var busiest = PerDay.MaxBy(p => p.Value);
        BusiestDay = busiest.Key;
        BusiestDayCount = busiest.Value;
        int dayRun = 0;
        DateTime? prev = null;
        foreach (var d in PerDay.Keys.OrderBy(d => d))
        {
            dayRun = prev is { } p && (d - p).TotalDays == 1 ? dayRun + 1 : 1;
            DayStreak = Math.Max(DayStreak, dayRun);
            prev = d;
        }
        var days = Enumerable.Range(0, 7).Select(d => (d, n: Enumerable.Range(0, 24).Sum(h => Week[d, h]))).MaxBy(x => x.n);
        FavouriteDay = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)((days.d + 1) % 7));
        var hours = Enumerable.Range(0, 24).Select(h => (h, n: Enumerable.Range(0, 7).Sum(d => Week[d, h]))).MaxBy(x => x.n);
        FavouriteHour = new DateTime(2000, 1, 1, hours.h, 0, 0).ToString("t");
        FavouriteTrack = ByTrack.MaxBy(g => g.Laps)?.Name ?? "";
        FavouriteCar = ByCar.FirstOrDefault()?.Name ?? "";
        FavouriteSeries = BySeries.FirstOrDefault()?.Name ?? "";

        var firstMonth = new DateTime(First.ToLocalTime().Year, First.ToLocalTime().Month, 1);
        var lastMonth = new DateTime(Last.ToLocalTime().Year, Last.ToLocalTime().Month, 1);
        var byMonth = races.GroupBy(r => new DateTime(r.Local.Year, r.Local.Month, 1)).ToDictionary(g => g.Key, g => g.ToList());
        for (var m = firstMonth; m <= lastMonth; m = m.AddMonths(1))
        {
            var list = byMonth.GetValueOrDefault(m) ?? new();
            Months.Add((m, list.Count, list.Count(r => r.Win), list.Count(r => r.Podium), list.Sum(r => r.Laps)));
        }

        // records
        void Add(string title, Race? r, Func<Race, string> value) { if (r != null) Records.Add(new Record(title, value(r), r)); }
        var wins = races.Where(r => r.Win).ToList();
        Add(T("First race"), races[0], r => r.Series);
        Add(T("First win"), wins.FirstOrDefault(), r => r.Series);
        Add(T("Toughest win"), wins.Where(r => r.Sof > 0).MaxBy(r => r.Sof), r => F("SoF {0}", r.Sof));
        Add(T("Best finish against the strongest field"), finished.Where(r => r.Sof > 0).OrderByDescending(r => r.Sof / Math.Sqrt(r.Finish)).FirstOrDefault(), r => F("P{0} at SoF {1}", r.Finish, r.Sof));
        Add(T("Biggest charge"), both.MaxBy(r => r.Gained), r => F("P{0} → P{1} (+{2})", r.StartPos, r.Finish, r.Gained));
        Add(T("Biggest drop"), both.MinBy(r => r.Gained), r => F("P{0} → P{1} ({2})", r.StartPos, r.Finish, r.Gained));
        Add(T("Most laps led"), races.Where(r => r.LapsLed > 0).MaxBy(r => r.LapsLed), r => P(r.LapsLed, "{0} lap", "{0} laps"));
        Add(T("Longest race"), races.MaxBy(r => r.Laps), r => P(r.Laps, "{0} lap", "{0} laps"));
        Add(T("Biggest field"), races.MaxBy(r => r.Drivers), r => P(r.Drivers, "{0} driver", "{0} drivers"));
        Add(T("Strongest field"), races.Where(r => r.Sof > 0).MaxBy(r => r.Sof), r => F("SoF {0}, P{1}", r.Sof, r.Finish));
        Add(T("Best iRating gain"), rated.MaxBy(r => r.IRatingChange), r => F("+{0}", r.IRatingChange));
        Add(T("Worst iRating loss"), rated.MinBy(r => r.IRatingChange), r => F("{0}", r.IRatingChange));
        Add(T("Most incidents"), races.MaxBy(r => r.Incidents), r => F("{0}x", r.Incidents));
        Add(T("Latest win"), wins.LastOrDefault(), r => r.Series);
        Add(T("Latest race"), races[^1], r => r.Finish > 0 ? "P" + r.Finish : "");
    }
}
