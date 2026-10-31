using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Everything public about an address: where it is and who runs it (ip-api.com: place, provider, organisation, network,
/// whether it's a mobile, proxy or hosting address) and who it's registered to (RDAP, the registries' own WHOIS:
/// the network's name and range, the owner, the abuse contact, dates). Private addresses have none of these.
/// </summary>
internal sealed class IpDetails
{
    public string Ip = "";
    public readonly List<Fact> Location = new(), Network = new(), Registration = new();
    public double Lat = double.NaN, Lon = double.NaN;
    public string Place = "";
    public string? Asn;
    public string? Error;

    private static readonly HttpClient http = MakeClient();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.ShortName}/{typeof(IpDetails).Assembly.GetName().Version?.ToString(3)}");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/rdap+json, application/json");
        return h;
    }

    public static async Task<IpDetails> LookupAsync(string ip, CancellationToken ct = default)
    {
        var d = new IpDetails { Ip = ip };
        await Task.WhenAll(GeoAsync(d, ct), RdapAsync(d, ct));
        return d;
    }

    private static async Task GeoAsync(IpDetails d, CancellationToken ct)
    {
        try
        {
            const string fields = "status,message,continent,country,countryCode,regionName,city,zip,lat,lon,timezone,isp,org,as,asname,mobile,proxy,hosting,query";
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"http://ip-api.com/json/{d.Ip}?fields={fields}", ct));
            var e = doc.RootElement;
            string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            if (S("status") != "success") { d.Error = S("message"); return; }
            d.Lat = e.GetProperty("lat").GetDouble();
            d.Lon = e.GetProperty("lon").GetDouble();
            d.Place = string.Join(", ", new[] { S("city"), S("regionName"), S("country") }.Where(s => s.Length > 0).Distinct());
            d.Location.Add(new Fact(T("Place"), d.Place));
            if (S("zip").Length > 0) d.Location.Add(new Fact(T("Postcode"), S("zip")));
            d.Location.Add(new Fact(T("Continent"), S("continent")));
            d.Location.Add(new Fact(T("Coordinates"), $"{d.Lat:0.###}, {d.Lon:0.###}", $"https://www.openstreetmap.org/?mlat={d.Lat:0.#####}&mlon={d.Lon:0.#####}#map=10/{d.Lat:0.#####}/{d.Lon:0.#####}".Replace(',', '.')));
            d.Location.Add(new Fact(T("Time zone"), S("timezone")));
            d.Network.Add(new Fact(T("Provider"), S("isp")));
            if (S("org").Length > 0 && S("org") != S("isp")) d.Network.Add(new Fact(T("Organisation"), S("org")));
            string asText = S("as");
            if (asText.StartsWith("AS"))
            {
                d.Asn = asText.Split(' ')[0];
                d.Network.Add(new Fact(T("Network (AS)"), asText + (S("asname").Length > 0 ? $" ({S("asname")})" : ""), $"https://bgp.he.net/{d.Asn}"));
            }
            var kinds = new List<string>();
            if (B("mobile")) kinds.Add(T("mobile network"));
            if (B("proxy")) kinds.Add(T("proxy, VPN or Tor"));
            if (B("hosting")) kinds.Add(T("hosting / data centre"));
            d.Network.Add(new Fact(T("Kind"), kinds.Count > 0 ? string.Join(", ", kinds) : T("ordinary connection")));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { d.Error ??= ex.Message; }
    }

    /// <summary>RDAP (rdap.org sends the question to the registry that has the address: ARIN, RIPE, APNIC, LACNIC or AFRINIC).</summary>
    private static async Task RdapAsync(IpDetails d, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://rdap.org/ip/{d.Ip}", ct));
            var e = doc.RootElement;
            string S(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            void Add(string label, string value, string? url = null) { if (value.Length > 0) d.Registration.Add(new Fact(label, value, url)); }
            Add(T("Network name"), S(e, "name"));
            Add(T("Range"), S(e, "startAddress").Length > 0 ? $"{S(e, "startAddress")} – {S(e, "endAddress")}" : "");
            if (e.TryGetProperty("cidr0_cidrs", out var cidrs) && cidrs.ValueKind == JsonValueKind.Array)
                Add("CIDR", string.Join(", ", cidrs.EnumerateArray().Select(c => $"{S(c, "v4prefix")}{S(c, "v6prefix")}/{(c.TryGetProperty("length", out var l) ? l.GetInt32() : 0)}")));
            Add(T("Kind"), S(e, "type"));
            Add(T("Country"), S(e, "country"));
            Add(T("Handle"), S(e, "handle"));
            if (e.TryGetProperty("entities", out var entities))
                foreach (var (role, name, email) in Entities(entities))
                {
                    string label = role switch
                    {
                        "registrant" => T("Registered to"),
                        "abuse" => T("Abuse contact"),
                        "administrative" => T("Admin contact"),
                        "technical" => T("Technical contact"),
                        _ => "",
                    };
                    if (label.Length == 0 || d.Registration.Any(f => f.Label == label)) continue;
                    Add(label, email.Length > 0 && name.Length > 0 ? $"{name} ({email})" : name + email, email.Length > 0 ? "mailto:" + email : null);
                }
            if (e.TryGetProperty("events", out var events))
                foreach (var ev in events.EnumerateArray())
                {
                    string action = S(ev, "eventAction"), date = S(ev, "eventDate");
                    if (!DateTime.TryParse(date, out var when)) continue;
                    if (action == "registration") Add(T("Registered"), when.ToString("d"));
                    else if (action == "last changed") Add(T("Last changed"), when.ToString("d"));
                }
            if (e.TryGetProperty("port43", out var whois) && whois.GetString() is { Length: > 0 } server) Add(T("Registry"), server);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.Log.Here.Debug(() => $"RDAP {d.Ip}: {ex.Message}"); }
    }

    /// <summary>Each contact's roles, name and email (from its vCard), through nested entities too.</summary>
    private static IEnumerable<(string Role, string Name, string Email)> Entities(JsonElement list)
    {
        foreach (var en in list.EnumerateArray())
        {
            string name = "", email = "";
            if (en.TryGetProperty("vcardArray", out var vc) && vc.ValueKind == JsonValueKind.Array && vc.GetArrayLength() > 1)
                foreach (var prop in vc[1].EnumerateArray())
                {
                    if (prop.GetArrayLength() < 4 || prop[3].ValueKind != JsonValueKind.String) continue;
                    string key = prop[0].GetString() ?? "";
                    if (key == "fn") name = prop[3].GetString() ?? "";
                    else if (key == "email" && email.Length == 0) email = prop[3].GetString() ?? "";
                }
            if (en.TryGetProperty("roles", out var roles))
                foreach (var r in roles.EnumerateArray()) yield return (r.GetString() ?? "", name, email);
            if (en.TryGetProperty("entities", out var inner))
                foreach (var x in Entities(inner)) yield return x;
        }
    }

    /// <summary>Pages with more about an address: its network's routes, reputation and what's been seen on it.</summary>
    public static IEnumerable<(string Text, string Url)> Links(string ip, string? asn)
    {
        yield return ("Hurricane Electric BGP", $"https://bgp.he.net/ip/{ip}");
        if (asn != null) yield return (F("{0} at PeeringDB", asn), $"https://www.peeringdb.com/search?q={asn[2..]}");
        yield return ("ipinfo.io", $"https://ipinfo.io/{ip}");
        yield return ("AbuseIPDB", $"https://www.abuseipdb.com/check/{ip}");
        yield return ("Shodan", $"https://www.shodan.io/host/{ip}");
        yield return ("Censys", $"https://search.censys.io/hosts/{ip}");
        yield return ("VirusTotal", $"https://www.virustotal.com/gui/ip-address/{ip}");
        yield return ("WHOIS (ARIN)", $"https://search.arin.net/rdap/?query={ip}");
    }
}

/// <summary>An address's public details in cards: names, a map of where it is, its network, its registration and links.</summary>
internal sealed class IpDetailsView : StackPanel
{
    private readonly NetUi.Fields names = new(), location = new(), network = new(), registration = new();
    private readonly MapView map = new() { Height = 260, Margin = new Thickness(0, 8, 0, 0) };
    private readonly WrapPanel links = new();
    private readonly TextBlock status = Ui.Secondary(T("Looking up…"), wrap: true);

    public IpDetailsView(string ip, string? name = null)
    {
        Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("Names")), names, status } }, new Thickness(0, 12, 0, 0)));
        bool isPrivate = IPAddress.TryParse(ip, out var a) && NetInfo.IsPrivate(a);
        if (isPrivate) status.Text = T("A private address: it has no public records.");
        else
        {
            var left = Ui.Card(new StackPanel { Children = { Ui.Header(T("Location")), location, map } }, new Thickness(0, 12, 6, 0));
            var right = new StackPanel { Children = { Ui.Card(new StackPanel { Children = { Ui.Header(T("Network")), network } }, new Thickness(6, 12, 0, 0)),
                Ui.Card(new StackPanel { Children = { Ui.Header(T("Registration")), registration } }, new Thickness(6, 12, 0, 0)) } };
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.Children.Add(left);
            Grid.SetColumn(right, 1);
            columns.Children.Add(right);
            Children.Add(columns);
            Children.Add(Ui.Card(new StackPanel { Children = { Ui.Header(T("More about it")), links } }, new Thickness(0, 12, 0, 0)));
        }
        _ = FillAsync(ip, name, isPrivate);
    }

    private async Task FillAsync(string ip, string? name, bool isPrivate)
    {
        names.Add(T("Address"), ip);
        name ??= await Names.ReverseAsync(ip);
        names.Add(T("Name (reverse)"), name ?? T("none"));
        if (name != null)
        {
            // does the name lead back to the address? (a name that doesn't is only a label)
            try
            {
                var back = await System.Net.Dns.GetHostAddressesAsync(name);
                names.Add(T("Name leads to"), back.Length == 0 ? T("nothing") : string.Join(", ", back.Select(b => b.ToString())),
                    back.Any(b => b.ToString() == ip) ? T("Matches: the name is really this address's") : T("Doesn't match: the name is only a label"));
            }
            catch { names.Add(T("Name leads to"), T("nothing")); }
        }
        if (isPrivate) return;
        var d = await IpDetails.LookupAsync(ip);
        status.Text = d.Error != null && d.Location.Count == 0 && d.Registration.Count == 0 ? F("Couldn't look it up: {0}", d.Error) : "";
        status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Fill(location, d.Location);
        Fill(network, d.Network);
        Fill(registration, d.Registration);
        if (!double.IsNaN(d.Lat)) map.Show([new MapPoint(ip, d.Lat, d.Lon, d.Place)], true, zoom: 9);
        foreach (var (text, url) in IpDetails.Links(ip, d.Asn)) links.Children.Add(Ui.Link(text, url));
    }

    /// <summary>Facts as rows (a fact with a link opens it on a click of its value).</summary>
    public static void Fill(NetUi.Fields fields, IEnumerable<Fact> facts)
    {
        foreach (var f in facts)
        {
            var box = fields.Add(f.Label, f.Value);
            if (box != null && f.Url != null) Linked(box, f.Url);
        }
    }

    /// <summary>Makes selectable text a link too: the accent colour, and a click that doesn't select anything opens it.</summary>
    public static void Linked(TextBox box, string url)
    {
        box.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        box.Cursor = System.Windows.Input.Cursors.Hand;
        box.ToolTip = url;
        box.PreviewMouseLeftButtonUp += (_, _) => { if (box.SelectionLength == 0) Ui.Open(url); };
    }
}
