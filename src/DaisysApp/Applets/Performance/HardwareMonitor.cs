using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// CPU temperature and power from LibreHardwareMonitor, if it's running with its web server on (Options → Remote Web
/// Server → Run). Windows doesn't give programs the CPU's sensors without a driver; LibreHardwareMonitor has one, and
/// this reads its numbers from http://localhost:8085/data.json. Checked every 2 seconds; if it isn't there, again
/// every 30 seconds.
/// </summary>
internal sealed partial class HardwareMonitor : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMilliseconds(800) };
    private long nextTry;
    private (double Temp, double Power) last = (double.NaN, double.NaN);

    public string Address { get; set; } = "http://localhost:8085";

    /// <summary>True while LibreHardwareMonitor is answering.</summary>
    public bool Connected { get; private set; }

    [GeneratedRegex(@"^/(intelcpu|amdcpu)/\d+/temperature/", RegexOptions.IgnoreCase)]
    private static partial Regex CpuTemperature();

    [GeneratedRegex(@"^/(intelcpu|amdcpu)/\d+/power/", RegexOptions.IgnoreCase)]
    private static partial Regex CpuPower();

    /// <summary>The CPU package temperature (°C) and power (W), or NaN. Call from a background thread.</summary>
    public (double CpuTemp, double CpuPower) Read()
    {
        long now = Environment.TickCount64;
        if (now < nextTry) return last;
        try
        {
            string json = http.GetStringAsync(Address.TrimEnd('/') + "/data.json").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var sensors = new List<(string Id, string Text, double Value)>();
            Collect(doc.RootElement, sensors);
            last = (Pick(sensors, CpuTemperature(), "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Package", "Core Max", "Core Average"),
                    Pick(sensors, CpuPower(), "CPU Package", "Package"));
            Connected = true;
            nextTry = now + 2000;
        }
        catch
        {
            last = (double.NaN, double.NaN);
            Connected = false;
            nextTry = now + 30000;
        }
        return last;
    }

    private static double Pick(List<(string Id, string Text, double Value)> sensors, Regex id, params string[] preferred)
    {
        var cpu = sensors.Where(s => id.IsMatch(s.Id)).ToList();
        foreach (string name in preferred)
            if (cpu.FirstOrDefault(s => s.Text.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Id: not null } hit) return hit.Value;
        return double.NaN;
    }

    private static void Collect(JsonElement node, List<(string, string, double)> into)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("SensorId", out var id) && id.ValueKind == JsonValueKind.String &&
            node.TryGetProperty("Text", out var text) && node.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.String &&
            ParseNumber(value.GetString()) is double v)
            into.Add((id.GetString()!, text.GetString() ?? "", v));
        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var c in children.EnumerateArray()) Collect(c, into);
    }

    /// <summary>"43.0 °C" or "43,0 °C" (LibreHardwareMonitor formats in its own language) → 43.</summary>
    private static double? ParseNumber(string? s)
    {
        if (s == null) return null;
        var m = Regex.Match(s, @"-?\d+(?:[.,]\d+)?");
        return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    public void Dispose() => http.Dispose();
}
