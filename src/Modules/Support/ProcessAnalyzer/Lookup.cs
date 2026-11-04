using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DaisysApp.Logging;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// Looking a program up: its file's SHA-256 asked of VirusTotal (with the user's free API key; only the hash is sent,
/// never the file), or VirusTotal's page and a web search opened in the browser. The free key allows 4 lookups a
/// minute, so the automatic ones (unknown programs that aren't signed) wait their turn.
/// </summary>
internal static class Lookup
{
    private static readonly HttpClient http = Http.Client(20);
    private static readonly SemaphoreSlim turn = new(1, 1);
    private static DateTime last = DateTime.MinValue;

    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static void OpenVirusTotal(string sha256) => Ui.Open($"https://www.virustotal.com/gui/file/{sha256}");

    public static void Search(string name, string? company) =>
        Ui.Open("https://www.bing.com/search?q=" + Uri.EscapeDataString($"\"{name}.exe\" {company} process"));

    /// <summary>VirusTotal's verdict on the file (stored on its entry): (flagged, engines), (0, 0) when VirusTotal
    /// hasn't seen it; throws with the reason worded for the user.</summary>
    public static async Task<(int Malicious, int Engines)> VirusTotalAsync(string key, string path, string name)
    {
        string hash = await Task.Run(() => Sha256(path));
        await turn.WaitAsync();
        try
        {
            var wait = last + TimeSpan.FromSeconds(16) - DateTime.Now;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            last = DateTime.Now;
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{hash}");
            req.Headers.Add("x-apikey", key);
            using var res = await http.SendAsync(req);
            (int, int) result;
            if (res.StatusCode == HttpStatusCode.NotFound) result = (0, 0);
            else if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException(T("VirusTotal didn't accept the API key"));
            else if ((int)res.StatusCode == 429) throw new InvalidOperationException(T("VirusTotal: too many lookups, try again in a minute"));
            else
            {
                res.EnsureSuccessStatusCode();
                var stats = JsonNode.Parse(await res.Content.ReadAsStringAsync())?["data"]?["attributes"]?["last_analysis_stats"];
                int Get(string k) => stats?[k]?.GetValue<int>() ?? 0;
                result = (Get("malicious") + Get("suspicious"), Get("malicious") + Get("suspicious") + Get("undetected") + Get("harmless"));
            }
            var e = (ProcessDb.Db.Get(name) ?? new KnownProcess { Name = name }).Copy();
            e.Sha256 = hash;
            e.VtMalicious = result.Item1;
            e.VtEngines = result.Item2;
            e.VtChecked = DateTime.UtcNow;
            ProcessDb.Db.Set(e);
            Log.Here.Info($"Process Analyzer: VirusTotal {name} ({hash}): {(result.Item2 == 0 ? "not known" : $"{result.Item1} of {result.Item2}")}");
            return result;
        }
        finally { turn.Release(); }
    }
}
