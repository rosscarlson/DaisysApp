using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DaisysApp.Applets.Hardware.Performance;

/// <summary>
/// A row of the process table, updated in place so sorting and the selection survive each refresh. Processes sharing a
/// name (a browser's, one a tab) are one row of their own: a group (<see cref="IsGroup"/>) showing their totals, with
/// an arrow that opens it to show each of them under it (<see cref="Level"/> 1), the main one marked.
/// </summary>
public sealed class ProcessRow(int pid, string name) : INotifyPropertyChanged
{
    private int pidValue = pid;
    private bool expanded;
    private int level, count;
    private bool isMain;

    /// <summary>A name's processes together (their totals), rather than one process.</summary>
    public bool IsGroup { get; init; }

    /// <summary>A group's processes (the main one first).</summary>
    public List<ProcessRow> Members { get; set; } = new();

    /// <summary>A group: open, showing its processes under it.</summary>
    public bool Expanded
    {
        get => expanded;
        set { Set(ref expanded, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Arrow))); }
    }

    /// <summary>0 at the top, 1 a group's process (indented).</summary>
    public int Level
    {
        get => level;
        set { Set(ref level, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Indent))); }
    }

    /// <summary>A group's main process (the one that isn't another's of the same name).</summary>
    public bool IsMain
    {
        get => isMain;
        set { Set(ref isMain, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameWeight))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameTip))); }
    }

    public int Count
    {
        get => count;
        set { Set(ref count, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText))); }
    }

    /// <summary>The arrow: right when closed, down when open (Segoe Fluent Icons); none for a single process.</summary>
    public string Arrow => IsGroup ? (expanded ? "\uE70D" : "\uE76C") : "";
    public System.Windows.Thickness Indent => new(level * 22, 0, 0, 0);
    public string CountText => IsGroup ? $"({count})" : "";
    /// <summary>A group's main process in bold (it's the one the group's PID is).</summary>
    public System.Windows.FontWeight NameWeight => isMain ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
    public string? NameTip => isMain ? T("The main process (the others were started by it)") : null;
    /// <summary>The arrow's tooltip (a group's only).</summary>
    public string? ArrowTip => IsGroup ? T("Show or hide its processes") : null;

    /// <summary>A group's totals from its processes: CPU, memory, GPU, VRAM, disk, network and threads added up; the GPU engine
    /// of the busiest; the worst warning.</summary>
    public void Total()
    {
        Cpu = Math.Round(Members.Sum(m => m.Cpu), 1);
        MemoryMB = Math.Round(Members.Sum(m => m.MemoryMB), 1);
        Gpu = Math.Round(Math.Min(100, Members.Sum(m => m.Gpu)), 1);
        VramMB = Math.Round(Members.Sum(m => m.VramMB), 1);
        IoMBps = Math.Round(Members.Sum(m => m.IoMBps), 2);
        SetNet(Members.Sum(m => m.Net), Members.Sum(m => m.NetMbps));
        Threads = Members.Sum(m => m.Threads);
        var busiest = Members.OrderByDescending(m => m.Gpu).FirstOrDefault();
        GpuEngine = busiest?.Gpu > 0 ? busiest.GpuEngine : "";
        GpuName = busiest?.GpuName ?? "";
        Severity = Members.Count == 0 ? 0 : Members.Max(m => m.Severity);
        Count = Members.Count;
        Pid = Members.FirstOrDefault(m => m.IsMain)?.Pid ?? Members.FirstOrDefault()?.Pid ?? 0;
    }

    private double cpu, memoryMB, gpu, vramMB, ioMBps, net = double.NaN, netMbps = double.NaN;
    private int threads;
    private string gpuEngine = "", gpuName = "";
    private bool isApp;
    private int severity;

    /// <summary>0 = fine, 1 = over an orange level, 2 = over a red one (Performance → gear).</summary>
    public int Severity { get => severity; set => Set(ref severity, value); }

    /// <summary>The process's id (a group's: its main process's).</summary>
    public int Pid { get => pidValue; private set => Set(ref pidValue, value); }
    public string Name { get; } = name;

    public double Cpu { get => cpu; private set => Set(ref cpu, value); }
    public double MemoryMB { get => memoryMB; private set => Set(ref memoryMB, value); }
    public double Gpu { get => gpu; private set => Set(ref gpu, value); }
    public double VramMB { get => vramMB; private set => Set(ref vramMB, value); }
    public double IoMBps { get => ioMBps; private set => Set(ref ioMBps, value); }
    public int Threads { get => threads; private set => Set(ref threads, value); }

    /// <summary>Its share of all network traffic, in % (NaN: not available); <see cref="NetText"/> shows it.</summary>
    public double Net { get => net; private set => Set(ref net, value); }
    public double NetMbps { get => netMbps; private set => Set(ref netMbps, value); }
    public string NetText => double.IsNaN(net) ? "–" : $"{net:0.0} %";
    public string? NetTip => double.IsNaN(netMbps) ? T("Needs permission to read Windows' network trace: Allow… under the list") : F("{0:0.00} Mbit/s sent and received", netMbps);

    private void SetNet(double share, double mbps)
    {
        share = double.IsNaN(share) ? share : Math.Round(Math.Min(100, share), 1);
        mbps = double.IsNaN(mbps) ? mbps : Math.Round(mbps, 2);
        if (!share.Equals(net)) { Net = share; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetText))); }
        if (!mbps.Equals(netMbps)) { NetMbps = mbps; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetTip))); }
    }
    public string GpuEngine { get => gpuEngine; private set => Set(ref gpuEngine, value); }

    /// <summary>The graphics card's name, for the GPU engine column's tooltip.</summary>
    public string GpuName { get => gpuName; set => Set(ref gpuName, value); }

    /// <summary>Has a window of its own (or shares a name with a process that does), like Task Manager's Apps.</summary>
    public bool IsApp
    {
        get => isApp;
        set
        {
            if (isApp == value) return;
            Set(ref isApp, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Group)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupOrder)));
        }
    }

    public string Group => isApp ? T("Apps") : T("Background processes");
    public int GroupOrder => isApp ? 0 : 1;

    public void Update(ProcessSample s)
    {
        Cpu = Math.Round(s.Cpu, 1);
        MemoryMB = Math.Round(s.MemoryMB, 1);
        Gpu = Math.Round(s.Gpu, 1);
        VramMB = Math.Round(s.VramMB, 1);
        IoMBps = Math.Round(s.IoMBps, 2);
        SetNet(s.Net, s.NetMbps);
        Threads = s.Threads;
        GpuEngine = s.GpuEngine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
