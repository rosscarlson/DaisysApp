using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DaisysApp.Applets.Performance;

/// <summary>A row of the process table, updated in place so sorting and the selection survive each refresh.</summary>
public sealed class ProcessRow(int pid, string name) : INotifyPropertyChanged
{
    private double cpu, memoryMB, gpu, vramMB, ioMBps;
    private int threads;
    private string gpuEngine = "", gpuName = "";
    private bool isApp;

    public int Pid { get; } = pid;
    public string Name { get; } = name;

    public double Cpu { get => cpu; private set => Set(ref cpu, value); }
    public double MemoryMB { get => memoryMB; private set => Set(ref memoryMB, value); }
    public double Gpu { get => gpu; private set => Set(ref gpu, value); }
    public double VramMB { get => vramMB; private set => Set(ref vramMB, value); }
    public double IoMBps { get => ioMBps; private set => Set(ref ioMBps, value); }
    public int Threads { get => threads; private set => Set(ref threads, value); }
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

    public string Group => isApp ? "Apps" : "Background processes";
    public int GroupOrder => isApp ? 0 : 1;

    public void Update(ProcessSample s)
    {
        Cpu = Math.Round(s.Cpu, 1);
        MemoryMB = Math.Round(s.MemoryMB, 1);
        Gpu = Math.Round(s.Gpu, 1);
        VramMB = Math.Round(s.VramMB, 1);
        IoMBps = Math.Round(s.IoMBps, 2);
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
