using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DaisysApp.Settings;
using Microsoft.Win32;

namespace DaisysApp.Applets.AudioLevel.Eq;

/// <summary>
/// Speaker EQ through Equalizer APO (free; it has to be installed, and enabled for the device with its Configurator).
/// The filters are kept in SpeakerEq.json in the settings folder and written out as DaisysApp-SpeakerEQ.txt in
/// Equalizer APO's config folder, which its config.txt includes. Equalizer APO applies them itself, so they stay
/// applied without Daisy's App running.
/// </summary>
public sealed partial class EqualizerApoEq : IEqControl
{
    public const string IncludeFile = "DaisysApp-SpeakerEQ.txt";
    private const string StoreName = "SpeakerEq";

    private readonly string configFolder;
    private readonly string deviceGuid;
    private readonly int channels;

    private EqualizerApoEq(string configFolder, string deviceGuid, int channels)
    {
        this.configFolder = configFolder;
        this.deviceGuid = deviceGuid;
        this.channels = channels;
    }

    /// <summary>Equalizer APO's config folder, or null if it isn't installed.</summary>
    public static string? ConfigFolder()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO");
            string? folder = key?.GetValue("ConfigPath") as string;
            if (folder == null && key?.GetValue("InstallPath") is string install) folder = Path.Combine(install, "config");
            return folder != null && Directory.Exists(folder) ? folder : null;
        }
        catch { return null; }
    }

    /// <summary>For an output device (endpoint id "{0.0.0.00000000}.{guid}"), or null if Equalizer APO isn't installed.</summary>
    public static EqualizerApoEq? TryCreate(string deviceId, int channels)
    {
        var folder = ConfigFolder();
        var m = GuidPattern().Match(deviceId);
        return folder == null || !m.Success ? null : new EqualizerApoEq(folder, m.Value.ToLowerInvariant(), channels);
    }

    [GeneratedRegex(@"\{[0-9a-fA-F\-]{36}\}$")]
    private static partial Regex GuidPattern();

    public string Description => F("Equalizer APO ({0} in {1}; stays applied without Daisy's App running)", IncludeFile, configFolder);

    public EqLimits Limits(double maxBoostDb) => new(10, MinQ: 0.3, MaxQ: 20, MinHz: 20, MaxHz: 20000, MaxCutDb: 15, MaxBoostDb: maxBoostDb);

    public bool CanControl(int channel) => channel >= 0 && channel < channels;

    /// <summary>Every device's filters: device GUID → channel → filters.</summary>
    public sealed class Store
    {
        public Dictionary<string, Dictionary<int, List<EqBand>>> Devices { get; set; } = new();
    }

    private static Store Load() => JsonStore.Load<Store>(StoreName);

    public IReadOnlyList<EqBand> Get(int channel) =>
        Load().Devices.TryGetValue(deviceGuid, out var d) && d.TryGetValue(channel, out var bands) ? bands : [];

    public void Set(int channel, IReadOnlyList<EqBand> bands)
    {
        var store = Load();
        if (!store.Devices.TryGetValue(deviceGuid, out var d)) store.Devices[deviceGuid] = d = new();
        if (bands.Count == 0) d.Remove(channel); else d[channel] = bands.ToList();
        if (d.Count == 0) store.Devices.Remove(deviceGuid);
        Write(store);
    }

    public object Snapshot() =>
        Load().Devices.TryGetValue(deviceGuid, out var d) ? d.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()) : new Dictionary<int, List<EqBand>>();

    public void Restore(object snapshot)
    {
        if (snapshot is not Dictionary<int, List<EqBand>> saved) return;
        var store = Load();
        if (saved.Count == 0) store.Devices.Remove(deviceGuid); else store.Devices[deviceGuid] = saved;
        Write(store);
    }

    public string? Warning => null;

    private void Write(Store store)
    {
        JsonStore.Save(StoreName, store);
        try
        {
            File.WriteAllText(Path.Combine(configFolder, IncludeFile), Render(store), new UTF8Encoding(false));
            EnsureIncluded();
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                F("Windows won't let Daisy's App write to Equalizer APO's config folder ({0}). Give your account permission to change that folder (right-click it → Properties → Security), then try again.", configFolder));
        }
    }

    /// <summary>Adds "Include: DaisysApp-SpeakerEQ.txt" to the end of config.txt if it isn't there.</summary>
    private void EnsureIncluded()
    {
        string config = Path.Combine(configFolder, "config.txt");
        string text = File.Exists(config) ? File.ReadAllText(config) : "";
        if (text.Contains("Include: " + IncludeFile, StringComparison.OrdinalIgnoreCase)) return;
        string add = (text.Length > 0 && !text.EndsWith('\n') ? "\r\n" : "")
                     + "\r\n# Speaker EQ from Daisy's App (Audio Tools → EQ Wizard)\r\nInclude: " + IncludeFile + "\r\n";
        File.AppendAllText(config, add, new UTF8Encoding(false));
    }

    /// <summary>The include file: for each device, a preamp for the biggest boost, then each channel's filters.</summary>
    public static string Render(Store store)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Written by Daisy's App (Audio Tools → EQ Wizard). It's rewritten each time, so changes made here are lost.");
        foreach (var (guid, chans) in store.Devices)
        {
            sb.AppendLine();
            sb.AppendLine("Device: " + guid);
            sb.AppendLine("Channel: all");
            double boost = chans.Values.Select(bs => Response.Grid.Max(f => EqBand.ResponseDb(bs, f))).DefaultIfEmpty(0).Max();
            if (boost > 0.05) sb.AppendLine($"Preamp: {(-Math.Ceiling(boost * 2) / 2).ToString("0.0", inv)} dB");
            foreach (var (ch, bands) in chans.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"Channel: {ch + 1}");
                foreach (var b in bands)
                    sb.AppendLine(string.Format(inv, "Filter: ON PK Fc {0:0.#} Hz Gain {1:0.0} dB Q {2:0.00}", b.Hz, b.GainDb, b.Q));
            }
            sb.AppendLine("Channel: all");
        }
        sb.AppendLine();
        sb.AppendLine("Device: all");
        return sb.ToString();
    }

    public void Dispose() { }
}
