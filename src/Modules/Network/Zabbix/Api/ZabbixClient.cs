using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaisysApp.Applets.Network.Zabbix.Api;

/// <summary>A message from the Zabbix server (or about reaching it), fit to show the user.</summary>
public sealed class ZabbixException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Zabbix's JSON-RPC API (api_jsonrpc.php), read-only. Asks the server's version first (no sign-in needed) and signs in
/// the way that version wants: an API token or a session from a user name and password, sent in an Authorization
/// header from Zabbix 6.4 on and in the request's "auth" field before (7.2 no longer takes the field). The calls used
/// here exist from Zabbix 5.0 on; each version's differences (where the availability is, what the host groups are
/// called) are handled in each call.
/// </summary>
public sealed class ZabbixClient : IDisposable
{
    private readonly HttpClient http;
    private string url;
    private string? auth;
    private int id;

    public Version Version { get; private set; } = new(0, 0);
    /// <summary>The API's URL that answered (a different path from the one set, when that one wasn't it).</summary>
    public string Url => url;
    /// <summary>The path that answered, when it isn't the one in the settings.</summary>
    public string? FoundPath { get; private set; }

    private ZabbixClient(ZabbixSettings s)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (s.IgnoreCertificate) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DaisysApp/" + (typeof(ZabbixClient).Assembly.GetName().Version?.ToString(3) ?? "1"));
        url = s.ApiUrl();
    }

    /// <summary>Finds the API (the path set, else the usual ones), checks the version and signs in.</summary>
    public static async Task<ZabbixClient> ConnectAsync(ZabbixSettings s, CancellationToken ct = default)
    {
        if (!s.HasServer) throw new ZabbixException(T("No Zabbix server is set (Settings → Network → Zabbix)."));
        var c = new ZabbixClient(s);
        try
        {
            await c.FindAsync(s, ct);
            if (s.UseToken)
            {
                string token = s.Token.Trim();
                if (token.Length == 0) throw new ZabbixException(T("No API token is set."));
                if (c.Version < new Version(5, 4)) throw new ZabbixException(F("Zabbix {0} has no API tokens (they came in 5.4): use a user name and password.", c.Version));
                c.auth = token;
            }
            else
            {
                if (s.User.Trim().Length == 0) throw new ZabbixException(T("No user name is set."));
                var login = new JsonObject { [c.Version >= new Version(5, 4) ? "username" : "user"] = s.User.Trim(), ["password"] = s.Password };
                var session = await c.CallAsync("user.login", login, ct, signedIn: false);
                c.auth = session.GetValue<string>();
            }
            // a cheap call that needs the sign-in, so a wrong token shows here and not later
            await c.CallAsync("host.get", new JsonObject { ["output"] = new JsonArray("hostid"), ["limit"] = 1 }, ct);
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    private async Task FindAsync(ZabbixSettings s, CancellationToken ct)
    {
        var paths = new List<string> { s.Path ?? "" };
        foreach (var p in new[] { "/zabbix", "/", "/zabbix/ui" })
            if (!paths.Any(x => x.Trim('/').Equals(p.Trim('/'), StringComparison.OrdinalIgnoreCase))) paths.Add(p);
        ZabbixException? first = null;
        foreach (var p in paths)
        {
            url = s.ApiUrl(p);
            try
            {
                var v = await CallAsync("apiinfo.version", new JsonArray(), ct, signedIn: false);
                Version = ParseVersion(v.GetValue<string>());
                if (!p.Trim('/').Equals((s.Path ?? "").Trim('/'), StringComparison.OrdinalIgnoreCase)) FoundPath = "/" + p.Trim('/');
                return;
            }
            catch (ZabbixException ex) when (ex.InnerException is NotZabbixException)
            {
                first ??= new ZabbixException(F("There's no Zabbix API at {0}.", s.ApiUrl()), ex);
            }
            catch (ZabbixException ex)
            {
                // reached the server but it failed for another reason (refused, timed out, certificate): no point trying other paths
                throw new ZabbixException(ex.Message, ex.InnerException);
            }
        }
        throw first ?? new ZabbixException(F("There's no Zabbix API at {0}.", s.ApiUrl()));
    }

    private static Version ParseVersion(string s)
    {
        var parts = s.Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0).ToArray();
        return new Version(parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2));
    }

    private sealed class NotZabbixException(string message) : Exception(message);

    private bool HeaderAuth => Version >= new Version(6, 4);

    /// <summary>One API call; its "result", or a <see cref="ZabbixException"/> with the server's message.</summary>
    public async Task<JsonNode> CallAsync(string method, JsonNode parameters, CancellationToken ct = default, bool signedIn = true)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters, ["id"] = Interlocked.Increment(ref id) };
        if (signedIn && auth != null && !HeaderAuth) body["auth"] = auth;
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        if (signedIn && auth != null && HeaderAuth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ZabbixException(F("{0} didn't answer in time.", HostOf(url)), ex); }
        catch (HttpRequestException ex) { throw new ZabbixException(F("Couldn't reach {0}: {1}", HostOf(url), Innermost(ex)), ex); }
        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
            if (json is not JsonObject obj || !obj.ContainsKey("jsonrpc"))
                throw new ZabbixException(F("{0} answered {1:D} {2}, not as Zabbix's API.", url, (int)response.StatusCode, response.ReasonPhrase), new NotZabbixException(text.Length > 200 ? text[..200] : text));
            if (obj["error"] is JsonObject error)
            {
                string message = error["message"]?.ToString() ?? T("Error");
                string data = error["data"]?.ToString() ?? "";
                throw new ZabbixException(data.Length > 0 && data != message ? $"{message} {data}" : message);
            }
            return obj["result"] ?? throw new ZabbixException(T("The server's answer had no result."));
        }
    }

    private static string HostOf(string u) => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Authority : u;

    private static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    // ---------------------------------------------------------------- reading

    private static string S(JsonNode? n) => n?.ToString() ?? "";
    private static int I(JsonNode? n) => int.TryParse(S(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    private static long L(JsonNode? n) => long.TryParse(S(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;
    private static double D(JsonNode? n) => double.TryParse(S(n), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    private static DateTime? Clock(JsonNode? n) => L(n) is long c && c > 0 ? DateTimeOffset.FromUnixTimeSeconds(c).LocalDateTime : null;
    private static JsonArray Array(params string[] values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    private static Availability Avail(JsonNode? n) => I(n) switch { 1 => Availability.Available, 2 => Availability.Unavailable, _ => Availability.Unknown };

    /// <summary>Every host, with its interfaces and groups.</summary>
    public async Task<List<ZHost>> HostsAsync(CancellationToken ct = default)
    {
        bool oldAvailability = Version < new Version(5, 2); // before 5.2 it was the host's, not each interface's
        var output = Array("hostid", "host", "name", "status", "maintenance_status", "description");
        if (oldAvailability) foreach (var f in new[] { "available", "error", "snmp_available", "snmp_error", "ipmi_available", "ipmi_error", "jmx_available", "jmx_error" }) output.Add(f);
        var interfaceFields = Array("type", "main", "ip", "dns", "useip", "port");
        if (!oldAvailability) { interfaceFields.Add("available"); interfaceFields.Add("error"); }
        var p = new JsonObject
        {
            ["output"] = output,
            ["selectInterfaces"] = interfaceFields,
            [Version >= new Version(6, 2) ? "selectHostGroups" : "selectGroups"] = Array("name"),
            ["sortfield"] = "name",
        };
        var result = await CallAsync("host.get", p, ct);
        var hosts = new List<ZHost>();
        foreach (var h in result.AsArray().OfType<JsonObject>())
        {
            var interfaces = new List<ZInterface>();
            foreach (var i in (h["interfaces"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                int type = I(i["type"]);
                string prefix = type switch { 2 => "snmp_", 3 => "ipmi_", 4 => "jmx_", _ => "" };
                interfaces.Add(new ZInterface
                {
                    Type = type, Main = I(i["main"]) == 1, Ip = S(i["ip"]), Dns = S(i["dns"]), UseIp = I(i["useip"]) == 1, Port = S(i["port"]),
                    Available = Avail(oldAvailability ? h[prefix + "available"] : i["available"]),
                    Error = S(oldAvailability ? h[prefix + "error"] : i["error"]),
                });
            }
            var groups = (h["hostgroups"] as JsonArray ?? h["groups"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(g => S(g["name"])).ToList();
            hosts.Add(new ZHost
            {
                HostId = S(h["hostid"]), Host = S(h["host"]), Name = S(h["name"]), Description = S(h["description"]),
                Monitored = I(h["status"]) == 0, InMaintenance = I(h["maintenance_status"]) == 1,
                Interfaces = interfaces, Groups = groups,
            });
        }
        return hosts;
    }

    private static readonly string[] ItemFields = { "itemid", "hostid", "name", "key_", "value_type", "units", "lastvalue", "lastclock", "state", "error", "description", "delay" };

    private static ZItem ReadItem(JsonObject i) => new()
    {
        ItemId = S(i["itemid"]), HostId = S(i["hostid"]), Name = S(i["name"]), Key = S(i["key_"]), ValueType = I(i["value_type"]), Units = S(i["units"]),
        LastValue = S(i["lastvalue"]), LastClock = Clock(i["lastclock"]), NotSupported = I(i["state"]) == 1, Error = S(i["error"]),
        Description = S(i["description"]), Delay = S(i["delay"]),
    };

    /// <summary>A host's enabled items, with their latest values.</summary>
    public async Task<List<ZItem>> ItemsAsync(string hostId, CancellationToken ct = default)
    {
        var p = new JsonObject
        {
            ["output"] = Array(ItemFields),
            ["hostids"] = Array(hostId),
            ["filter"] = new JsonObject { ["status"] = 0 },
            ["sortfield"] = "name",
        };
        return (await CallAsync("item.get", p, ct)).AsArray().OfType<JsonObject>().Select(ReadItem).ToList();
    }

    /// <summary>The items whose key starts with any of <paramref name="keyStarts"/>, on every host (for the host boxes).</summary>
    public async Task<List<ZItem>> ItemsByKeyAsync(IEnumerable<string> keyStarts, CancellationToken ct = default)
    {
        var p = new JsonObject
        {
            ["output"] = Array(ItemFields),
            ["filter"] = new JsonObject { ["status"] = 0 },
            ["search"] = new JsonObject { ["key_"] = Array(keyStarts.ToArray()) },
            ["searchByAny"] = true,
            ["startSearch"] = true,
        };
        return (await CallAsync("item.get", p, ct)).AsArray().OfType<JsonObject>().Select(ReadItem).ToList();
    }

    /// <summary>Every trigger in the problem state on a monitored host (not ones that depend on another that's firing).</summary>
    public async Task<List<ZProblem>> ProblemsAsync(CancellationToken ct = default)
    {
        var p = new JsonObject
        {
            ["output"] = Array("triggerid", "description", "priority", "lastchange"),
            ["filter"] = new JsonObject { ["value"] = 1 },
            ["monitored"] = true,
            ["skipDependent"] = true,
            ["expandDescription"] = true,
            ["selectHosts"] = Array("hostid"),
            ["sortfield"] = "priority",
            ["sortorder"] = "DESC",
        };
        return (await CallAsync("trigger.get", p, ct)).AsArray().OfType<JsonObject>().Select(t => new ZProblem
        {
            TriggerId = S(t["triggerid"]), Description = S(t["description"]), Severity = I(t["priority"]),
            Since = Clock(t["lastchange"]) ?? DateTime.MinValue,
            HostIds = (t["hosts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(h => S(h["hostid"])).ToList(),
        }).ToList();
    }

    /// <summary>One reading: its time, and the value (or for trends the average, with the lowest and highest).</summary>
    public readonly record struct Point(DateTime Time, double Value, double Min, double Max);

    /// <summary>Every value of numeric items between two times, by item id, oldest first.</summary>
    public async Task<Dictionary<string, List<Point>>> HistoryAsync(IEnumerable<ZItem> items, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<Point>>();
        foreach (var byType in items.Where(i => i.IsNumeric).GroupBy(i => i.ValueType))
        {
            var p = new JsonObject
            {
                ["output"] = Array("itemid", "clock", "value"),
                ["history"] = byType.Key,
                ["itemids"] = Array(byType.Select(i => i.ItemId).ToArray()),
                ["time_from"] = Unix(from),
                ["time_till"] = Unix(to),
                ["sortfield"] = "clock",
                ["sortorder"] = "ASC",
            };
            foreach (var r in (await CallAsync("history.get", p, ct)).AsArray().OfType<JsonObject>())
            {
                double v = D(r["value"]);
                Add(result, S(r["itemid"]), new Point(Clock(r["clock"]) ?? from, v, v, v));
            }
        }
        foreach (var list in result.Values) list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    /// <summary>Hourly averages (with each hour's lowest and highest) of numeric items, by item id, oldest first.</summary>
    public async Task<Dictionary<string, List<Point>>> TrendsAsync(IEnumerable<ZItem> items, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<Point>>();
        var ids = items.Where(i => i.IsNumeric).Select(i => i.ItemId).ToArray();
        if (ids.Length == 0) return result;
        var p = new JsonObject
        {
            ["output"] = Array("itemid", "clock", "value_min", "value_avg", "value_max"),
            ["itemids"] = Array(ids),
            ["time_from"] = Unix(from),
            ["time_till"] = Unix(to),
        };
        foreach (var r in (await CallAsync("trend.get", p, ct)).AsArray().OfType<JsonObject>())
            Add(result, S(r["itemid"]), new Point(Clock(r["clock"]) ?? from, D(r["value_avg"]), D(r["value_min"]), D(r["value_max"])));
        foreach (var list in result.Values) list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    private static void Add(Dictionary<string, List<Point>> d, string id, Point p)
    {
        if (!d.TryGetValue(id, out var list)) d[id] = list = new();
        list.Add(p);
    }

    private static long Unix(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();

    /// <summary>Signs out a session made with a password (an API token stays as it is).</summary>
    public async Task LogoutAsync(bool session)
    {
        if (!session || auth == null) return;
        try { await CallAsync("user.logout", new JsonArray()); } catch { }
    }

    public void Dispose() => http.Dispose();
}
