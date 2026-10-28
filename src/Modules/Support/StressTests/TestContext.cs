using System.Diagnostics;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>A test: runs on its own thread until <see cref="TestContext.TimeUp"/>, filling in the result.</summary>
internal interface IStressTest
{
    void Run(TestContext c);
}

/// <summary>
/// What a test runs with (its length, the settings, the stop button) and reports through: its live value and step for
/// the page, its errors and, at Maximum logging, its details for the log.
/// </summary>
internal sealed class TestContext
{
    private readonly Stopwatch clock = new();
    private readonly object gate = new();
    private long lastTrace;
    private int errorsLogged;

    public TestInfo Info { get; }
    public TestResult Result { get; }
    public TimeSpan Length { get; }
    public CancellationToken Token { get; }
    public StressSettings Settings { get; }
    public StressLogging Logging { get; }
    /// <summary>The run's earlier results (Multi-Core's scaling over Single-Core, All at Once's comparisons).</summary>
    public IReadOnlyList<TestResult> Earlier { get; }
    /// <summary>Threads to use (0 = the test's own choice); All at Once shares the CPU between its parts.</summary>
    public int Threads { get; init; }
    /// <summary>The share of free memory to fill (All at Once uses less).</summary>
    public int MemoryPercent { get; init; }

    private static readonly Logger log = Log.Here;

    public TestContext(TestInfo info, TimeSpan length, StressSettings settings, IReadOnlyList<TestResult> earlier, CancellationToken token, int loop)
    {
        Info = info;
        Length = length;
        Settings = settings;
        Logging = settings.Logging;
        Earlier = earlier;
        Token = token;
        MemoryPercent = settings.MemoryPercent;
        Result = new TestResult { Id = info.Id, Name = info.Name, Group = info.Group, Unit = info.Unit, Loop = loop };
    }

    /// <summary>A part of a test (All at Once's), sharing its stop button and length.</summary>
    public TestContext Part(TestInfo info, int threads, int memoryPercent) =>
        new(info, Length, Settings, Earlier, Token, Result.Loop) { Threads = threads, MemoryPercent = memoryPercent, Parent = this };

    public TestContext? Parent { get; private init; }

    public bool Stopping => Token.IsCancellationRequested;

    /// <summary>Starts the test's clock (after its preparation, which isn't timed).</summary>
    public void Begin()
    {
        clock.Restart();
        lastTrace = 0;
    }

    public TimeSpan Elapsed => clock.Elapsed;

    /// <summary>The time's up (or Stop was pressed).</summary>
    public bool TimeUp => Token.IsCancellationRequested || clock.IsRunning && clock.Elapsed >= Length;

    /// <summary>Of the test's time, 0–1.</summary>
    public double Progress => clock.IsRunning ? Math.Clamp(clock.Elapsed.TotalSeconds / Length.TotalSeconds, 0, 1) : 0;

    /// <summary>What it's doing ("Writing", "Checking"), for the page.</summary>
    public string Step { get; set; } = "";

    /// <summary>The live value (in <see cref="TestInfo.Unit"/>, or the test's own unit while it's preparing).</summary>
    public double Live { get; private set; } = double.NaN;
    public string LiveUnit { get; private set; } = "";

    /// <summary>Sets the live value; kept once a second for the test's graph.</summary>
    public void Report(double value, string? unit = null)
    {
        Live = value;
        LiveUnit = unit ?? Info.Unit;
        long s = (long)clock.Elapsed.TotalSeconds;
        if (clock.IsRunning && s > lastTrace && Parent == null)
        {
            lastTrace = s;
            lock (gate) Result.Trace.Add(Math.Round(value, 3));
        }
    }

    /// <summary>A frame of the GPU's 3D test for the page (BGRA, width, height), now and then.</summary>
    public Action<byte[], int, int>? Preview { get; set; }

    public void ShowFrame(byte[] bgra, int w, int h) => (Preview ?? Parent?.Preview)?.Invoke(bgra, w, h);

    /// <summary>A detail for the log, written at Maximum logging only.</summary>
    public void Detail(string text)
    {
        if (Logging == StressLogging.Maximum) log.Info($"Stress Tests: {Info.Name}: {text}");
    }

    /// <summary>A note shown with the result (and logged).</summary>
    public void Note(string text)
    {
        lock (gate) if (!Result.Notes.Contains(text)) Result.Notes.Add(text);
        log.Warn($"Stress Tests: {Info.Name}: {text}");
    }

    /// <summary>A wrong result: counted, the first ones logged as errors (all of them at Maximum).</summary>
    public void Error(string text, int count = 1)
    {
        int logged;
        lock (gate)
        {
            Result.Errors += count;
            logged = ++errorsLogged;
            if (Result.Notes.Count(n => n.StartsWith('✕')) < 10) Result.Notes.Add("✕ " + text);
        }
        if (logged <= 20 || Logging == StressLogging.Maximum) log.Error($"Stress Tests: {Info.Name}: {text}");
        else if (logged == 21) log.Error($"Stress Tests: {Info.Name}: more errors (not each logged at Standard logging)");
        Parent?.AddErrors(count);
    }

    private void AddErrors(int count)
    {
        lock (gate) Result.Errors += count;
    }

    public int Errors { get { lock (gate) return Result.Errors; } }

    public void Add(string name, double value, string unit, bool lowerIsBetter = false)
    {
        lock (gate) Result.Metrics.Add(new Metric(name, Math.Round(value, 3), unit, lowerIsBetter));
    }
}
