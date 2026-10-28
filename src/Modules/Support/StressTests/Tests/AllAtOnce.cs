namespace DaisysApp.Applets.Support.StressTests.Tests;

/// <summary>
/// Stress → All at Once: the CPU's vector test, the memory integrity test and the GPU's 3D test together, the most a
/// PC's power supply and cooling get asked for. What each managed is compared with what it did on its own earlier in
/// the run (its "held" share): a part that drops a lot is throttling, or short of power.
/// </summary>
internal sealed class AllAtOnce : IStressTest
{
    public void Run(TestContext c)
    {
        int cpuThreads = Math.Max(1, Environment.ProcessorCount - 2);
        var parts = new (TestInfo Info, IStressTest Test, TestContext Context)[]
        {
            (Catalog.Find("CpuVector")!, new CpuVector(), c.Part(Catalog.Find("CpuVector")!, cpuThreads, 0)),
            (Catalog.Find("MemIntegrity")!, new MemIntegrity(), c.Part(Catalog.Find("MemIntegrity")!, 2, Math.Min(30, c.Settings.MemoryPercent))),
            (Catalog.Find("Gpu3D")!, new Gpu3D(), c.Part(Catalog.Find("Gpu3D")!, 0, 0)),
        };
        c.Step = T("CPU, memory and GPU together");
        c.Begin();
        var threads = parts.Select(p => new Thread(() =>
        {
            try { p.Test.Run(p.Context); }
            catch (Exception ex)
            {
                p.Context.Result.Failed = ex.Message;
                c.Note(F("{0} stopped: {1}", p.Info.Name, ex.Message));
            }
        }) { IsBackground = true, Name = $"StressTests-AllAtOnce-{p.Info.Id}" }).ToList();
        threads.ForEach(t => t.Start());
        while (threads.Any(t => t.IsAlive))
        {
            Thread.Sleep(250);
            c.Step = string.Join(" · ", parts.Select(p => $"{p.Info.Name} {Metric.Format(p.Context.Live, p.Info.Unit)}"));
            // the live graph follows the CPU
            if (!double.IsNaN(parts[0].Context.Live)) c.Report(parts[0].Context.Live, parts[0].Info.Unit);
        }

        var held = new List<double>();
        var devices = new List<string>();
        foreach (var (info, _, part) in parts)
        {
            var r = part.Result;
            if (r.Device != null) devices.Add(r.Device);
            foreach (var n in r.Notes) if (!c.Result.Notes.Contains(n)) c.Result.Notes.Add(n);
            if (r.Failed != null) { c.Note(F("{0}: {1}", info.Name, r.Failed)); continue; }
            c.Add(info.Name, r.Value, info.Unit);
            // (memory runs on two threads here: its speed isn't comparable with its own run, only its errors are)
            if (info.Id != "MemIntegrity" && c.Earlier.LastOrDefault(e => e.Id == info.Id && e.Value > 0) is { } alone)
            {
                double share = r.Value / alone.Value * 100;
                // the CPU had fewer threads here than on its own
                if (info.Id == "CpuVector") share *= Environment.ProcessorCount / (double)cpuThreads;
                held.Add(share);
                c.Add(F("{0} held", info.Name), share, "%");
            }
        }
        c.Result.Device = string.Join(" · ", devices.Distinct());
        c.Result.Value = held.Count > 0 ? held.Min() : double.NaN;
        if (held.Count == 0) c.Note(T("Run the CPU Vector and GPU 3D tests too to see how much each held"));
        else if (held.Min() < 85) c.Note(F("A part held only {0:0}% of what it did on its own: throttling or short of power", held.Min()));
    }
}
