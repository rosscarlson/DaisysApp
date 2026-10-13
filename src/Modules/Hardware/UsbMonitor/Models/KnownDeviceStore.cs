using System.Windows.Threading;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Hardware.UsbMonitor.Models;

/// <summary>
/// Every USB device seen, for the Devices sub-tab and for working out what a misbehaving device is: when one shows a
/// problem (and often little else, as a device that fails its USB handshake can't say what it is), it's matched against
/// the devices known to have worked by where it's plugged in and anything else it still reports (<see cref="Analyse"/>).
/// Fed by the USB Monitor on the UI thread: Windows' own records at start (devices it remembers, plugged in or not), then
/// every event. Saved in UsbDevices.json.
/// </summary>
public sealed class KnownDeviceStore
{
    private const string FileName = "UsbDevices";

    public sealed class Data
    {
        public List<KnownDevice> Devices { get; set; } = new();
    }

    private static KnownDeviceStore? instance;
    public static KnownDeviceStore Instance => instance ??= new KnownDeviceStore();

    private readonly Dictionary<string, KnownDevice> byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool dirty;

    private KnownDeviceStore()
    {
        foreach (var d in JsonStore.Load<Data>(FileName).Devices ?? new())
            if (!string.IsNullOrEmpty(d.InstanceId)) byId[d.InstanceId] = d;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
    }

    /// <summary>Raised on the UI thread when anything changed.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<KnownDevice> Devices => byId.Values;

    /// <summary>
    /// What Windows knows at start: a device whose status can be read is plugged in; one that can't is one Windows
    /// remembers from before (with its last connect and disconnect times). Everything plugged in is marked as such, and
    /// anything not in the list as unplugged.
    /// </summary>
    public void ObserveBaseline(IReadOnlyList<DeviceInfo> devices)
    {
        var now = DateTime.Now;
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in devices)
        {
            bool isNew = !byId.TryGetValue(info.InstanceId, out var d);
            d ??= new KnownDevice { InstanceId = info.InstanceId, FirstSeen = info.LastArrival ?? now, FromWindows = !info.StatusKnown };
            d.Info = Merge(d.Info, info);
            if (info.LastArrival is { } a && (d.LastConnected == null || a > d.LastConnected)) d.LastConnected = a;
            if (info.LastRemoval is { } r && (d.LastDisconnected == null || r > d.LastDisconnected)) d.LastDisconnected = r;
            if (info.StatusKnown)
            {
                present.Add(info.InstanceId);
                d.Connected = true;
                d.FromWindows = false;
                if (info.ProblemCode == 0) Working(d, info, now);
                else Problem(d, info, now);
            }
            else
            {
                d.Connected = false;
                // remembered by Windows with a driver installed: it worked here once
                if (d.LastGood == null && !string.IsNullOrEmpty(info.Service) && !string.IsNullOrEmpty(info.VendorId) && info.VendorId != "0000")
                {
                    d.LastGood = info.Clone();
                    d.LastWorking ??= info.LastRemoval ?? info.LastArrival;
                }
            }
            if (isNew) byId[d.InstanceId] = d;
        }
        foreach (var d in byId.Values)
            if (d.Connected && !present.Contains(d.InstanceId)) d.Connected = false;
        MarkChanged();
    }

    /// <summary>One event from the USB Monitor.</summary>
    public void Observe(DeviceRecord record)
    {
        var info = record.Info;
        if (string.IsNullOrEmpty(info.InstanceId)) return;
        bool isNew = !byId.TryGetValue(info.InstanceId, out var d);
        d ??= new KnownDevice { InstanceId = info.InstanceId, FirstSeen = record.Timestamp };
        d.Info = Merge(d.Info, info);
        d.Events++;
        d.FromWindows = false;
        switch (record.EventType)
        {
            case DeviceEventType.Arrived:
                d.Connected = true;
                d.LastConnected = record.Timestamp;
                break;
            case DeviceEventType.Removed:
                d.Connected = false;
                d.LastDisconnected = record.Timestamp;
                break;
            default:
                if (!info.StatusKnown && d.Connected) { d.Connected = false; d.LastDisconnected = record.Timestamp; }
                else if (info.StatusKnown && !d.Connected) { d.Connected = true; d.LastConnected = record.Timestamp; }
                break;
        }
        if (info.StatusKnown && info.ProblemCode == 0) Working(d, info, record.Timestamp);
        else if (info.ProblemCode != 0) Problem(d, info, record.Timestamp);
        if (isNew) byId[d.InstanceId] = d;
        MarkChanged();
    }

    public void Forget(KnownDevice d)
    {
        if (byId.Remove(d.InstanceId)) MarkChanged();
    }

    private static void Working(KnownDevice d, DeviceInfo info, DateTime when)
    {
        d.LastWorking = when;
        d.LastGood = info.Clone();
    }

    private static void Problem(KnownDevice d, DeviceInfo info, DateTime when)
    {
        d.LastProblem = when;
        d.LastProblemCode = info.ProblemCode;
    }

    /// <summary>The newer reading, with anything it couldn't read kept from before (a removal often says almost nothing).</summary>
    private static DeviceInfo Merge(DeviceInfo old, DeviceInfo fresh)
    {
        var m = fresh.Clone();
        foreach (var p in typeof(DeviceInfo).GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(string) && p.Name != nameof(DeviceInfo.Status)))
            if (string.IsNullOrEmpty((string?)p.GetValue(m)) && p.GetValue(old) is string v && v.Length > 0) p.SetValue(m, v);
        m.LastArrival ??= old.LastArrival;
        m.LastRemoval ??= old.LastRemoval;
        return m;
    }

    private void MarkChanged()
    {
        dirty = true;
        if (!saveTimer.IsEnabled) saveTimer.Start();
        Changed?.Invoke();
    }

    public void Save()
    {
        if (!dirty) return;
        dirty = false;
        JsonStore.Save(FileName, new Data { Devices = byId.Values.OrderBy(d => d.FirstSeen).ToList() });
    }

    // ---------------------------------------------------------------- what a problem device most likely is

    /// <summary>A known, working device a problem device may be, how sure (0–100), and why.</summary>
    public sealed record Candidate(KnownDevice Device, int Score, IReadOnlyList<string> Reasons);

    /// <summary>
    /// The known devices that worked and that <paramref name="info"/> most likely is, best first: the same device
    /// instance, the same physical port (its location path), the same port number on the same hub, the same
    /// vendor and product (when the device still reports them), the same serial number or container.
    /// </summary>
    public IReadOnlyList<Candidate> Analyse(DeviceInfo info)
    {
        var list = new List<Candidate>();
        foreach (var d in byId.Values)
        {
            if (d.LastGood is not { } good) continue;
            int score = 0;
            var why = new List<string>();
            void Add(int points, string reason) { score += points; why.Add(reason); }
            static bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            static bool RealId(string? id) => !string.IsNullOrEmpty(id) && id != "0000";

            if (Same(d.InstanceId, info.InstanceId)) Add(60, T("the same device"));
            if (Same(good.LocationPaths, info.LocationPaths))
                Add(Same(good.ParentInstanceId, info.ParentInstanceId) ? 60 : 55, F("plugged into the same port ({0})", info.LocationPaths!.Split(" ; ")[0]));
            else if (Same(good.LocationInformation, info.LocationInformation))
            {
                if (Same(good.ParentInstanceId, info.ParentInstanceId)) Add(45, F("the same port on the same hub ({0})", info.LocationInformation));
                else Add(15, F("the same port number ({0}), on another hub", info.LocationInformation));
            }
            if (RealId(info.VendorId) && Same(good.VendorId, info.VendorId))
            {
                if (Same(good.ProductId, info.ProductId)) Add(35, F("the same vendor and product ({0}:{1})", info.VendorId, info.ProductId));
                else Add(10, F("the same vendor ({0})", info.VendorId));
            }
            if (Same(good.SerialNumber, info.SerialNumber)) Add(40, F("the same serial number ({0})", info.SerialNumber));
            if (Same(good.ContainerId, info.ContainerId) && info.ContainerId != "{00000000-0000-0000-ffff-ffffffffffff}") Add(25, T("the same physical device (container)"));
            if (score >= 30) list.Add(new Candidate(d, Math.Min(100, score), why));
        }
        return list.OrderByDescending(c => c.Score).ThenByDescending(c => c.Device.LastWorking).Take(3).ToList();
    }

    /// <summary>The analysis as text, for the log, the details page and the log file; and the likeliest device's name.</summary>
    public (string Text, string? Likely) Describe(DeviceInfo info)
    {
        var parts = new List<string>();
        parts.Add(info.ProblemCode != 0
            ? F("Code {0}: {1}.", info.ProblemCode, info.ProblemText)
            : T("It appeared but Windows couldn't read its status."));
        var candidates = Analyse(info);
        if (candidates.Count == 0)
            parts.Add(T("It doesn't match any device known to have worked here (Daisy's App learns each device as it sees it work)."));
        else
            foreach (var (c, i) in candidates.Select((c, i) => (c, i)))
                parts.Add((i == 0 ? T("Most likely: ") : T("Or: ")) +
                    F("{0}{1}, last working {2} (match {3}%: {4}).",
                        c.Device.Name,
                        c.Device.LastGood is { VendorId: { } v, ProductId: { } p } ? $" [{v}:{p}]" : "",
                        c.Device.LastWorking?.ToString("yyyy-MM-dd HH:mm") ?? T("unknown"),
                        c.Score, string.Join(", ", c.Reasons)));
        return (string.Join(" ", parts), candidates.FirstOrDefault()?.Device.Name);
    }
}
