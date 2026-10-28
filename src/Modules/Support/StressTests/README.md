# Support → Stress Tests

*A sub-module of [Support](../README.md) (since 3.22.0): tests that score a PC (CPU, memory, storage, GPU and video)
and load it hard enough to show whether anything breaks, with every run logged, kept, exported and compared with
another PC's. Switch it off in Settings → General → Modules (under Support).*

## The tests

Tick the tests in the top card (by group; **Benchmark** ticks every scored test, **All** adds All at Once), choose how,
and **Start**. Each test runs for the time chosen, one after another; **Stop** ends the run (what finished is kept).

| Group | Test | What it does | Its number |
|---|---|---|---|
| CPU | Single-Core | Integer hashing over a table, a patch of the Mandelbrot set in doubles and a sort, in small units, on one thread | Mops/s |
| CPU | Multi-Core | The same on every logical processor (with its scaling over one thread) | Mops/s |
| CPU | Vector (AVX) | Multiply-adds on 256-bit registers (AVX2 + FMA, or AVX, or SSE) on every logical processor: the most heat a CPU makes | GFLOPS |
| Memory | Bandwidth | Every core reading, writing (past the caches) and copying its own 64 MB buffers | GB/s (the three averaged) |
| Memory | Latency | A chain of random reads, each needing the last, in 32 KB, 256 KB, 8 MB and 512 MB (memory itself) | ns (512 MB) |
| Memory | Integrity | A share of the free memory (Memory to test) filled with patterns by every thread (each word from its address, inverted, walking ones, checkerboards) and read back | GB/s written and checked |
| Storage | Storage | A test file on the drive chosen (Storage file), written and read in 1 MB blocks four at a time, then read in random 4 KB pieces eight at a time, past Windows' cache; deleted after | MB/s (sequential read and write averaged; the score counts random reads as much) |
| GPU | 3D | 1,600 lit, spinning spheres (about 2.4 million triangles a frame) with a heavy pixel shader, at 1920×1080 with 4× MSAA, as fast as the card goes; a picture shows beside the live graph | fps |
| GPU | Compute | A million compute-shader threads of multiply-adds | GFLOPS |
| GPU | VRAM | A share of the free video memory (VRAM to test) taken in 256 MB blocks, filled with patterns and checked by compute shaders | GB/s (writing and checking averaged) |
| Video | Encode | 1080p frames already on the GPU through each hardware encoder it has (H.264, HEVC, AV1), the time shared | fps (H.264) |
| Video | Decode | A 1080p 20 Mbit/s H.264 clip (made by the encoder first) through the hardware decoder, over and over | fps |
| Stress | All at Once | CPU Vector (all threads but two), Memory Integrity (two threads, for its errors) and GPU 3D together: how much of what the CPU and GPU did alone each held (needs those tests earlier in the run) | % (the lowest held) |

**Errors.** Every test that can checks its work, so a run finds instability, not only speed: a CPU unit whose
checksum differs from the one worked out before the clock started, a memory or VRAM word that comes back different,
a storage block that holds the wrong data, a GPU compute batch that differs from the first, the graphics driver
resetting (device lost), a decoder error. Errors are counted per test, shown in red, and logged as errors. Tests also
note the CPU's clock falling during a test (throttling), a software encoder used, a part of All at Once that held less
than 85%.

**Scores.** Each test's number against a reference PC (an i7-8086K, 4 × 16 GB DDR4 at 2133, a GTX 1080 Ti and a WD
SN720 NVMe), which scores 1,000 in each: twice as fast is 2,000. Latency counts the other way (half the time is
2,000). The overall score and each group's are geometric means (one weak test pulls them down, as in most suites).
With Repeat, a test's results are averaged over its loops, with their spread. All at Once has no score.

**Options:** Each test (10 s to an hour), Repeat (once to 10×, or until stopped), Logging, the graphics card (when
there are several), the drive and the size of the storage test's file, how much free memory and video memory to fill,
and Stop at the first error.

**While it runs:** the test and what it's doing, its live number and graph, the test's and the run's progress, the
errors so far, the 3D test's picture, and the sensors once a second: CPU load and clock (Windows' counters), CPU
temperature and power (from [LibreHardwareMonitor](../../Hardware/Sensors/README.md) when it's running), GPU load,
clock, temperature and power (NVIDIA's library on NVIDIA cards), memory in use. A green **Stress Test** bubble shows in
the title bar (click: this tab). Each test's sensor summary (average clock, highest temperature and power) is kept
with its result.

## Logging

Everything goes to the Support log (`Support-<date>.log`, in **Tools → Logs**, source Support):

- **Standard:** the run's start (tests, length, the PC), each test's start and result (its numbers, device and
  sensors), errors (the first 20 of each test as errors), notes, what Windows logged, and the end (scores, errors).
- **Maximum:** also the board, BIOS, power plan, each memory module, GPU and drive, the app's memory; a line every
  second with the test, its live number and every sensor (so the last line before a crash shows how far it got and how
  hot it was); each pass and batch of a test; every error.

**A run that didn't finish.** While a run goes, `logs\stresstests\running.json` holds it as far as it's got and the
test it's on. If the app or Windows stops (a crash, a freeze, a blue screen, a power cut), the next start finds it,
keeps it as a run marked *Stopped during <test>*, logs an error and shows a notice.

**What Windows logged.** At the end of a run (and for one that didn't finish) Windows' System log is read for the
run's time: unexpected restarts (Kernel-Power 41, EventLog 6008), hardware errors (WHEA-Logger), the graphics driver
resetting (Display 4101, nvlddmkm, amdkmdag, igfx), disk and file system errors, low memory, and the app's own crashes.
They're kept with the run and shown under its results.

## Results, runs and export

**Results** shows the run picked (the newest after a run): the score and each group's, *No errors* or how many, and a
row per test: score, bar, number, details (each measurement and the device), errors, sensors and notes. **Runs** lists
every run (newest first): when, the PC, score, errors, how long, its status and tests; pick one to show it, **Delete**
(Confirm?) or open the folder. Runs are kept in `%LOCALAPPDATA%\DaisysApp\logs\stresstests\` (a JSON file each;
Settings → General → Storage: *Stress test runs*).

**Export…** saves a run as a JSON file (the same as the ones kept: the PC's details and every result) to compare on
another PC.

## Compare

**Compare…** opens a window with **A** and **B** each picked from this PC's runs, or from a run exported on another PC
(**Import…**, remembered for next time); ⇄ swaps them. The report:

- the two scores, big, and how much faster or slower B is overall;
- **System:** PC, CPU, cores and threads, base clock, memory (size, type, speed, modules), GPU, VRAM, GPU driver, other
  GPUs, the drive tested and every drive, motherboard, BIOS, Windows, power plan, test length, app version; what
  differs is in amber;
- **Scores:** overall and each group, with bars;
- **Tests:** each test's score, with bars;
- **Details:** each test's device, number and every measurement, errors, and its CPU and GPU clock, temperature and
  power;
- **Stability:** errors, a run that didn't finish and what Windows logged.

The last column is B against A in percent, green when B is better and red when worse (for latency, lower is better).
**Copy** puts the report on the clipboard as text; **Save Report…** saves it as a web page and opens it.

## Settings

`%APPDATA%\DaisysApp\StressTests.json`: the tests ticked, the options, the runs last compared and the files imported.

## Code

Namespace `DaisysApp.Applets.Support.StressTests`.

| File | What it does |
|---|---|
| `StressApplet.cs` | The sub-module (Support's `AppletGroup` adds it as `StressTests`) |
| `StressSettings.cs` | `StressTests.json` |
| `Model.cs` | `Catalog` (the tests, groups, references, the geometric mean), `StressRun` (a run: its PC, results, events; read and written as JSON; `Averaged`, `ComputeScores`), `TestResult`, `Metric`, `SensorSummary`, `RunEvent` |
| `SystemSpec.cs` | The PC: Windows, CPU (registry, cores), board, BIOS and memory modules (`Smbios`: the firmware's table), GPUs and drivers (`Graphics`: DXGI and the display adapters' registry keys), drives (`DiskInfo`), power plan; `Mem` (free memory) |
| `Monitor.cs` | The sensors once a second (`PdhQuery`, `Nvml`, `SensorHub` from DaisysApp.Shared) and each test's `SensorSummary` |
| `TestContext.cs` | `IStressTest`; what a test runs with and reports through (length, stop, live number and trace, errors, notes, details for Maximum logging, the 3D picture); `Part` for All at Once |
| `Runner.cs` | The run: each test on its own thread, the marker file and a run that didn't finish, scores, logging, Windows' events, the runs kept |
| `StressView.cs` | The tab: tests and options, the live card, Results, Runs, Export |
| `CompareWindow.cs` | Compare: A and B, Import…, the report drawn, Copy and Save Report… |
| `Report.cs` | The comparison's sections and rows, as text and as a web page |
| `Tests/CpuTests.cs` | `CpuWork` (the checked units, threads), Single-Core, Multi-Core, Vector |
| `Tests/MemoryTests.cs` | Bandwidth, Latency, Integrity |
| `Tests/StorageTest.cs` | Storage (unbuffered, overlapped, every 4 KB stamped and checked) |
| `Tests/Gpu.cs` | `GpuDevice` (Direct3D 11 on the card picked), 3D, Compute, VRAM (shaders compiled at run time) |
| `Tests/VideoTests.cs` | `Media` (Media Foundation: hardware encoders and decoders, NV12 frames, encoding through the sink writer), Encode, Decode |
| `Tests/AllAtOnce.cs` | All at Once |
