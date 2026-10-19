using System.Globalization;
using System.IO;
using System.Text.Json;
using DaisysApp.Logging;
using static DaisysApp.Applets.Gaming.IRacing.IRacingApi;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// Reads an export made in the browser (iracing_export.js, pasted into the console on members-ng.iracing.com: it uses
/// the site's own sign-in, so no OAuth client is needed) into the races kept: <c>iracing_raw.json</c> (everything:
/// every official and hosted session as iRacing's search gives it, iRating and safety rating before and after each
/// race and qualifying, and the rating graphs), or <c>races_all.csv</c> with <c>irating_history.csv</c> and
/// <c>sr_history.csv</c> beside it. Sessions already kept are updated; the rest are added.
/// </summary>
internal static class RaceImport
{
    public sealed record Result(int Added, int Updated, int Races, int Practices, int Rated);

    private static readonly Dictionary<int, string> Categories = new()
    {
        [1] = "Oval", [2] = "Road", [3] = "Dirt Oval", [4] = "Dirt Road", [5] = "Sports Car", [6] = "Formula Car",
    };

    /// <summary>The export script, shipped beside the module, for Settings' Copy the export script.</summary>
    public static string ScriptPath => Path.Combine(Path.GetDirectoryName(typeof(RaceImport).Assembly.Location) ?? "", "iracing_export.js");

    public static Result Import(RaceData data, string path)
    {
        var result = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? FromCsv(data, path) : FromJson(data, path);
        data.FromZero = true; // what's kept counts from 0, as iRacing does
        data.Imported = DateTime.UtcNow;
        if (data.Licenses.Count == 0) data.Licenses = LicencesFromCharts(data);
        data.Prepare();
        data.Save();
        Log.Here.Info($"iRacing: imported {Path.GetFileName(path)}: {result.Added} sessions added, {result.Updated} updated ({result.Races} races, {result.Rated} with ratings)");
        return result;
    }

    // ---------------------------------------------------------------- iracing_raw.json

    private static Result FromJson(RaceData data, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("series_sessions", out _))
            throw new IRacingException(T("That isn't an iRacing export: iracing_raw.json, from the export script, has the sessions in it."));

        var member = Obj(root, "member");
        long custId = (long)(Num(member, "cust_id") ?? Num(root, "member_cust_id") ?? data.CustId);
        if (data.CustId != 0 && custId != 0 && custId != data.CustId)
            throw new IRacingException(F("This export is of member {0}, but the races kept are member {1}'s. Delete them first (Settings → Gaming → iRacing) to import someone else's.", custId, data.CustId));
        data.CustId = custId;
        if (Str(member, "name") is { Length: > 0 } name) data.Name = name;
        if (Date(member, "member_since") is { } since) data.MemberSince = since;

        var ratings = new Dictionary<long, JsonElement>();
        foreach (var key in new[] { "per_race_ratings", "per_race_rating_changes" })
            if (root.TryGetProperty(key, out var r) && r.ValueKind == JsonValueKind.Object)
                foreach (var p in r.EnumerateObject())
                    if (long.TryParse(p.Name, out long id)) ratings[id] = p.Value;

        var incoming = new List<Race>();
        foreach (var (key, hosted) in new[] { ("series_sessions", false), ("hosted_sessions", true) })
            foreach (var row in Arr(root, key))
            {
                if (ToRaceOf(row, hosted) is not { } race) continue;
                if (ratings.TryGetValue(race.SubsessionId, out var x))
                {
                    race.OldIRating = Positive(Int(x, "oi", -1));
                    race.NewIRating = Positive(Int(x, "ni", -1));
                    race.OldSubLevel = Int(x, "os", -1);
                    race.NewSubLevel = Int(x, "ns", -1);
                }
                incoming.Add(race);
                // your name, from a race you won or a session you hosted
                if (data.Name.Length == 0)
                {
                    if ((long)(Num(row, "winner_group_id") ?? 0) == custId && Str(row, "winner_name") is { Length: > 0 } w) data.Name = w;
                    else if ((long)(Num(Obj(row, "host"), "cust_id") ?? 0) == custId && Str(Obj(row, "host"), "display_name") is { Length: > 0 } h) data.Name = h;
                }
            }

        // the rating graphs: "1_<category>" iRating, "3_<category>" licence and safety rating
        if (root.TryGetProperty("chart_data", out var charts) && charts.ValueKind == JsonValueKind.Object)
            foreach (var c in charts.EnumerateObject())
            {
                var parts = c.Name.Split('_');
                if (parts.Length != 2 || !int.TryParse(parts[1], out int cat) || !Categories.TryGetValue(cat, out var catName)) continue;
                var points = RaceData.Points(c.Value.ValueKind == JsonValueKind.Array ? Wrap(c.Value) : c.Value);
                if (points.Count == 0) continue;
                if (parts[0] == "1") data.IRating[catName] = points;
                else if (parts[0] == "3") data.Safety[catName] = points.Select(p => p with { Value = p.Value / 1000 }).ToList();
            }

        return Merge(data, incoming, replace: true);
    }

    private static Race? ToRaceOf(JsonElement row, bool hosted) => RaceData.ToRace(row, hosted);

    private static JsonElement Wrap(JsonElement array)
    {
        using var doc = JsonDocument.Parse("{\"data\":" + array.GetRawText() + "}");
        return doc.RootElement.Clone();
    }

    private static int Positive(int v) => v > 0 ? v : -1;

    // ---------------------------------------------------------------- races_all.csv (+ the two histories)

    private static Result FromCsv(RaceData data, string path)
    {
        var rows = ReadCsv(path);
        if (rows.Count == 0 || !rows[0].ContainsKey("subsession_id"))
            throw new IRacingException(T("That isn't an iRacing export: races_all.csv, from the export script, has a subsession_id column."));
        var incoming = new List<Race>();
        foreach (var r in rows)
        {
            if (!long.TryParse(r.GetValueOrDefault("subsession_id"), out long id)) continue;
            string type = r.GetValueOrDefault("session_type") ?? "";
            bool hosted = type == "Hosted";
            int start = Pos(r, "start_pos");
            incoming.Add(new Race
            {
                SubsessionId = id,
                Start = DateTime.TryParse(r.GetValueOrDefault("date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue,
                Hosted = hosted,
                Session = hosted ? (start >= 0 ? Sessions.Race : Sessions.Practice)
                    : type switch { "Practice" => Sessions.Practice, "Qualify" => Sessions.Qualify, "Time Trial" => Sessions.TimeTrial, _ => Sessions.Race },
                Official = !hosted && r.GetValueOrDefault("official") == "true",
                Category = r.GetValueOrDefault("license_category") ?? "",
                Series = r.GetValueOrDefault("series") ?? "",
                SeasonYear = Number(r, "season_year"),
                SeasonQuarter = Number(r, "season_quarter"),
                Week = Number(r, "race_week"),
                Track = r.GetValueOrDefault("track") ?? "",
                Config = r.GetValueOrDefault("config") ?? "",
                Car = r.GetValueOrDefault("car") ?? "",
                CarClass = r.GetValueOrDefault("car_class") ?? "",
                Drivers = Number(r, "num_drivers"),
                Sof = Math.Max(0, Number(r, "sof")),
                StartRaw = start,
                FinishRaw = Pos(r, "finish_pos"),
                FinishInClassRaw = Pos(r, "finish_pos_class"),
                Laps = Math.Max(0, Number(r, "laps_complete")),
                LapsLed = Math.Max(0, Number(r, "laps_led")),
                Incidents = Math.Max(0, Number(r, "incidents")),
                Points = Math.Max(0, Number(r, "champ_points")),
                OldIRating = Positive(Number(r, "irating_before", -1)),
                NewIRating = Positive(Number(r, "irating_after", -1)),
                OldSubLevel = SubLevel(r.GetValueOrDefault("sr_before")),
                NewSubLevel = SubLevel(r.GetValueOrDefault("sr_after")),
            });
        }

        string folder = Path.GetDirectoryName(path) ?? "";
        string ir = Path.Combine(folder, "irating_history.csv"), sr = Path.Combine(folder, "sr_history.csv");
        if (File.Exists(ir))
            foreach (var g in ReadCsv(ir).GroupBy(x => CategoryName(x.GetValueOrDefault("category"))))
                data.IRating[g.Key] = History(g, x => double.TryParse(x.GetValueOrDefault("irating"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null);
        if (File.Exists(sr))
            foreach (var g in ReadCsv(sr).GroupBy(x => CategoryName(x.GetValueOrDefault("category"))))
                data.Safety[g.Key] = History(g, x => double.TryParse(x.GetValueOrDefault("raw_value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v / 1000 : null);

        // (the CSV has less in it than what a sync or the JSON gave: sessions already kept only get its ratings)
        return Merge(data, incoming, replace: false);
    }

    private static List<ChartPoint> History(IEnumerable<Dictionary<string, string>> rows, Func<Dictionary<string, string>, double?> value) =>
        rows.Select(x => DateTime.TryParse(x.GetValueOrDefault("date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) && value(x) is { } v ? new ChartPoint(d, v) : null)
            .Where(p => p != null).Select(p => p!).OrderBy(p => p.When).ToList();

    private static string CategoryName(string? c) => c switch { "Formula" => "Formula Car", null => "", _ => c };

    /// <summary>A 1-based position in the CSV as iRacing counts it (from 0); -1 when blank.</summary>
    private static int Pos(Dictionary<string, string> r, string key) => int.TryParse(r.GetValueOrDefault(key), out int p) && p > 0 ? p - 1 : -1;

    private static int Number(Dictionary<string, string> r, string key, int fallback = 0) =>
        int.TryParse(r.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    /// <summary>"B 3.45" → 345.</summary>
    private static int SubLevel(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return -1;
        var last = s.Trim().Split(' ')[^1];
        return double.TryParse(last, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (int)Math.Round(v * 100) : -1;
    }

    /// <summary>A CSV file's rows by column name (quoted fields, "" inside them, and a byte-order mark allowed).</summary>
    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        string text = File.ReadAllText(path).TrimStart('﻿');
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) records.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); records.Add(row); }
        if (records.Count == 0) return new();
        var head = records[0];
        return records.Skip(1).Select(r => head.Select((h, i) => (h, v: i < r.Count ? r[i] : "")).ToDictionary(x => x.h, x => x.v)).ToList();
    }

    // ---------------------------------------------------------------- putting it together

    private static Result Merge(RaceData data, List<Race> incoming, bool replace)
    {
        var byId = data.Races.ToDictionary(r => r.SubsessionId);
        int added = 0, updated = 0;
        foreach (var race in incoming)
        {
            if (byId.TryGetValue(race.SubsessionId, out var old))
            {
                updated++;
                if (replace)
                {
                    // keep what only a sync knows
                    if (race.TrackId == 0) race.TrackId = old.TrackId;
                    if (race.CarId == 0) race.CarId = old.CarId;
                    if (!race.HasIRating && old.HasIRating) { race.OldIRating = old.OldIRating; race.NewIRating = old.NewIRating; }
                    byId[race.SubsessionId] = race;
                }
                else if (race.HasIRating)
                {
                    old.OldIRating = race.OldIRating; old.NewIRating = race.NewIRating;
                    old.OldSubLevel = race.OldSubLevel; old.NewSubLevel = race.NewSubLevel;
                }
            }
            else { byId[race.SubsessionId] = race; added++; }
        }
        data.Races = byId.Values.ToList();
        if (data.MemberSince == null && data.Races.Count > 0) data.MemberSince = data.Races.Min(r => r.Start).Date;

        // a sync carries on from the newest session imported
        var newestOfficial = incoming.Where(r => !r.Hosted).Select(r => (DateTime?)r.Start).Max();
        var newestHosted = incoming.Where(r => r.Hosted).Select(r => (DateTime?)r.Start).Max();
        if (newestOfficial is { } o && (data.OfficialThrough == null || o > data.OfficialThrough)) data.OfficialThrough = o;
        if (newestHosted is { } h && (data.HostedThrough == null || h > data.HostedThrough)) data.HostedThrough = h;

        return new Result(added, updated, incoming.Count(r => r.IsRace), incoming.Count(r => r.Session == Sessions.Practice), incoming.Count(r => r.HasIRating));
    }

    /// <summary>Your licences as the rating graphs end (an export has no licence list of its own).</summary>
    private static List<License> LicencesFromCharts(RaceData data)
    {
        string[] colours = { "", "fc0706", "fc8a27", "feec04", "33cc00", "0153db", "000000" };
        var list = new List<License>();
        foreach (var cat in Categories.Values)
        {
            var sr = data.Safety.GetValueOrDefault(cat)?.LastOrDefault();
            var ir = data.IRating.GetValueOrDefault(cat)?.LastOrDefault();
            if (sr == null && ir == null) continue;
            int cls = sr == null ? 0 : (int)Math.Floor(sr.Value + 1e-9);
            list.Add(new License
            {
                Category = cat,
                Class = cls > 0 ? (cls == 1 ? "Rookie" : "Class " + Sessions.Class(cls)) : "",
                SafetyRating = sr == null ? 0 : (sr.Value - cls) * 10,
                IRating = ir == null ? 0 : (int)ir.Value,
                Color = cls is > 0 and < 7 ? colours[cls] : "",
            });
        }
        return list;
    }
}
