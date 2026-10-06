using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Performance;

/// <summary>One LibreHardwareMonitor sensor reading. Min and max are since LibreHardwareMonitor started.</summary>
public sealed record HwSensor(string Key, string Hardware, string Kind, string Type, string Name, double Value, double Min, double Max, string Unit)
{
    public string Text(double v) => double.IsNaN(v) ? "—" : FormatValue(v, Unit);

    public static string FormatValue(double v, string unit)
    {
        string format = unit switch { "V" => "0.000", "°C" or "W" or "%" or "GB" => "0.0", _ => Math.Abs(v) >= 100 ? "#,0" : "0.#" };
        return v.ToString(format, CultureInfo.CurrentCulture) + (unit == "%" ? "%" : unit.Length > 0 ? " " + unit : "");
    }
}

/// <summary>Everything LibreHardwareMonitor reported at one time.</summary>
public sealed record HwSnapshot(DateTime Time, IReadOnlyList<HwSensor> Sensors);

/// <summary>
/// Hardware sensors from LibreHardwareMonitor, if it's running with its web server on (Options → Remote Web Server →
/// Run). Windows doesn't give programs the CPU's, motherboard's or fans' sensors without a driver; LibreHardwareMonitor
/// has one, and this reads all its numbers from &lt;address&gt;/data.json. Checked every 2 seconds; if it isn't there,
/// again every 15 seconds.
/// </summary>
internal sealed partial class HardwareMonitor : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMilliseconds(1500) };
    private long nextTry;

    public const string DefaultAddress = "http://localhost:8085";
    public const string DownloadPage = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest";

    public string Address { get; set; } = DefaultAddress;

    /// <summary>True while LibreHardwareMonitor is answering.</summary>
    public bool Connected { get; private set; }

    /// <summary>The latest readings (null when it isn't answering).</summary>
    public HwSnapshot? Latest { get; private set; }

    /// <summary>Whether LibreHardwareMonitor is running at all (then only its web server is missing).</summary>
    public static bool ProcessRunning()
    {
        try
        {
            var p = Process.GetProcessesByName("LibreHardwareMonitor");
            foreach (var x in p) x.Dispose();
            return p.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>Reads it if it's time (call once a second from a background thread); returns a new snapshot, or null.</summary>
    public HwSnapshot? Poll()
    {
        long now = Environment.TickCount64;
        if (now < nextTry) return null;
        try
        {
            string json = http.GetStringAsync(Address.TrimEnd('/') + "/data.json").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var sensors = new List<HwSensor>();
            var seen = new HashSet<string>();
            Collect(doc.RootElement, depth: 0, hardware: "", kind: "", sensors, seen);
            Latest = new HwSnapshot(DateTime.Now, sensors);
            Connected = true;
            nextTry = now + 2000;
            return Latest;
        }
        catch
        {
            Latest = null;
            Connected = false;
            nextTry = now + 15000;
            return null;
        }
    }

    /// <summary>Asks again right away (after the address changed, or from "Check again").</summary>
    public void RetryNow() => nextTry = 0;

    // The tree is: root → computer → hardware (→ sub-hardware, e.g. the motherboard's sensor chip) → type group → sensor.
    private static void Collect(JsonElement node, int depth, string hardware, string kind, List<HwSensor> into, HashSet<string> seen)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        string text = node.TryGetProperty("Text", out var t) ? t.GetString() ?? "" : "";
        if (node.TryGetProperty("HardwareId", out _) && depth == 2)
        {
            hardware = text.Trim();
            kind = node.TryGetProperty("ImageURL", out var img) ? KindOf(img.GetString()) : "";
        }

        if (node.TryGetProperty("SensorId", out var id) && id.ValueKind == JsonValueKind.String)
        {
            string type = node.TryGetProperty("Type", out var ty) ? ty.GetString() ?? "" : "";
            var (value, unit) = Parse(Str(node, "Value"));
            if (!double.IsNaN(value))
            {
                // a few sensors share an id (e.g. two GPU loads): the name makes the key unique
                string key = $"{id.GetString()}|{text}";
                if (seen.Add(key))
                    into.Add(new HwSensor(key, hardware, kind, type, text, value, Parse(Str(node, "Min")).Value, Parse(Str(node, "Max")).Value, unit));
            }
        }

        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var c in children.EnumerateArray()) Collect(c, depth + 1, hardware, kind, into, seen);
    }

    private static string KindOf(string? image) => image switch
    {
        null => "",
        _ when image.Contains("cpu") => "cpu",
        _ when image.Contains("nvidia") || image.Contains("ati") || image.Contains("amd") || image.Contains("intel") => "gpu",
        _ when image.Contains("mainboard") => "board",
        _ when image.Contains("ram") => "memory",
        _ when image.Contains("hdd") => "drive",
        _ => "",
    };

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    [GeneratedRegex(@"^\s*(-?\d+(?:[.,]\d+)?)\s*(.*)$")]
    private static partial Regex NumberAndUnit();

    /// <summary>"43.0 °C" or "43,0 °C" (it formats in its own language) → (43, "°C").</summary>
    private static (double Value, string Unit) Parse(string s)
    {
        var m = NumberAndUnit().Match(s);
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? (v, m.Groups[2].Value.Trim())
            : (double.NaN, "");
    }

    // ---------------------------------------------------------------- the few sensors the tiles use

    [GeneratedRegex(@"^/(intelcpu|amdcpu)/\d+/temperature/", RegexOptions.IgnoreCase)]
    private static partial Regex CpuTemperatureId();

    [GeneratedRegex(@"^/(intelcpu|amdcpu)/\d+/power/", RegexOptions.IgnoreCase)]
    private static partial Regex CpuPowerId();

    [GeneratedRegex(@"^/gpu-[a-z]+/\d+/temperature/", RegexOptions.IgnoreCase)]
    private static partial Regex GpuTemperatureId();

    [GeneratedRegex(@"^/gpu-[a-z]+/\d+/power/", RegexOptions.IgnoreCase)]
    private static partial Regex GpuPowerId();

    [GeneratedRegex(@"^/gpu-[a-z]+/\d+/control/", RegexOptions.IgnoreCase)]
    private static partial Regex GpuFanId();

    public static double CpuTemp(HwSnapshot s) => Pick(s, CpuTemperatureId(), "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Package", "Core Max", "Core Average");
    public static double CpuPower(HwSnapshot s) => Pick(s, CpuPowerId(), "CPU Package", "Package");
    public static double GpuTemp(HwSnapshot s) => Pick(s, GpuTemperatureId(), "GPU Core", "GPU Temperature", "GPU Package");
    public static double GpuPower(HwSnapshot s) => Pick(s, GpuPowerId(), "GPU Package", "GPU Power", "GPU Board Power");
    public static double GpuFan(HwSnapshot s) => Pick(s, GpuFanId(), "GPU Fan", "GPU Fan 1");

    private static double Pick(HwSnapshot s, Regex id, params string[] preferred)
    {
        var matching = s.Sensors.Where(x => id.IsMatch(x.Key)).ToList();
        foreach (string name in preferred)
            if (matching.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit.Value;
        return double.NaN;
    }

    public void Dispose() => http.Dispose();
}
