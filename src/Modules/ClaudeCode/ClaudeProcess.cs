using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>
/// One running Claude Code session: "claude -p" with streaming JSON in and out, as the Agent SDK drives it. It stays
/// running between turns (each message is a line on its stdin), and asks this app before using a tool
/// (--permission-prompt-tool stdio: a can_use_tool control request, answered with allow or deny). Everything it prints
/// is raised as <see cref="Message"/>, on the UI thread. It signs in as the CLI is signed in (the user's own account).
/// </summary>
internal sealed class ClaudeProcess : IDisposable
{
    private readonly Process process;
    private readonly Dispatcher ui;
    private readonly StringBuilder errors = new();
    private readonly object writeGate = new();
    private int requests;
    private bool disposed;

    /// <summary>A line of its output (stream_event, assistant, user, system, result, control_request…).</summary>
    public event Action<JsonElement>? Message;

    /// <summary>It ended (crashed, signed out, closed): the last of what it wrote to stderr.</summary>
    public event Action<string>? Exited;

    public bool Running => !disposed && !process.HasExited;

    private ClaudeProcess(Process process, Dispatcher ui)
    {
        this.process = process;
        this.ui = ui;
    }

    /// <summary>Starts it for this session: a new one with its id and name, or the saved one resumed.</summary>
    public static ClaudeProcess Start(string exe, SessionInfo s, Dispatcher ui)
    {
        var args = new List<string>
        {
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--permission-prompt-tool", "stdio",
        };
        if (s.Teleport is { Length: > 0 } remote) args.AddRange(new[] { "--teleport", remote });
        else if (s.Started) args.AddRange(new[] { "--resume", s.Id });
        else args.AddRange(new[] { "--session-id", s.Id });
        if (s.Name.Length > 0) args.AddRange(new[] { "--name", s.Name });
        if (s.Model.Length > 0) args.AddRange(new[] { "--model", s.Model });
        if (s.Mode.Length > 0) args.AddRange(new[] { "--permission-mode", s.Mode });
        var info = ClaudeCli.StartInfo(exe, args, Directory.Exists(s.Folder) ? s.Folder : null);
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardInputEncoding = new UTF8Encoding(false);
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        var cp = new ClaudeProcess(p, ui);
        p.Start();
        Job.Add(p);
        Logging.Log.Here.Info($"Claude Code session {s.Id} started ({(s.Teleport != null ? "teleported from " + s.Teleport : s.Started ? "resumed" : "new")}) in {s.Folder}, process {p.Id}");
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (cp.errors) { cp.errors.AppendLine(e.Data); if (cp.errors.Length > 8000) cp.errors.Remove(0, cp.errors.Length - 8000); } };
        p.BeginErrorReadLine();
        p.Exited += (_, _) =>
        {
            string tail;
            lock (cp.errors) tail = cp.errors.ToString().Trim();
            Logging.Log.Here.Info($"Claude Code process {p.Id} ended ({SafeExit(p)}){(tail.Length > 0 ? ": " + tail : "")}");
            if (!cp.disposed) ui.BeginInvoke(() => cp.Exited?.Invoke(tail));
        };
        new Thread(cp.Read) { IsBackground = true, Name = "Claude Code reader" }.Start();
        cp.Control("initialize", null);
        return cp;
    }

    /// <summary>
    /// A Windows job holding every Claude Code process (and what they start: shells, builds), closed with the app: if the
    /// app crashes or is ended, they end with it instead of running on unseen.
    /// </summary>
    private static class Job
    {
        private static readonly IntPtr handle = Create();

        private static IntPtr Create()
        {
            var h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return h;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            int size = System.Runtime.InteropServices.Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(h, 9, ptr, (uint)size); // JobObjectExtendedLimitInformation
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
            return h;
        }

        public static void Add(Process p)
        {
            try { if (handle != IntPtr.Zero) AssignProcessToJobObject(handle, p.Handle); }
            catch (Exception ex) { Logging.Log.Here.Warn("Claude Code job: " + ex.Message); }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }

    private static string SafeExit(Process p) { try { return "exit code " + p.ExitCode; } catch { return "ended"; } }

    private void Read()
    {
        try
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonElement e;
                try { using var doc = JsonDocument.Parse(line); e = doc.RootElement.Clone(); }
                catch { continue; }
                ui.BeginInvoke(() => { if (!disposed) Message?.Invoke(e); });
            }
        }
        catch (Exception ex) when (!disposed) { Logging.Log.Here.Warn("Claude Code output: " + ex.Message); }
        catch { }
    }

    private void Write(object message)
    {
        if (!Running) return;
        string json = JsonSerializer.Serialize(message);
        lock (writeGate)
        {
            try
            {
                process.StandardInput.WriteLine(json);
                process.StandardInput.Flush();
            }
            catch (Exception ex) { Logging.Log.Here.Warn("Claude Code input: " + ex.Message); }
        }
    }

    /// <summary>A message from the user (a turn, or a slash command such as /rename or /compact).</summary>
    public void Send(string text) =>
        Write(new { type = "user", message = new { role = "user", content = text } });

    /// <summary>A control request: initialize, interrupt, set_permission_mode {mode}, set_model {model}.</summary>
    public void Control(string subtype, Dictionary<string, object?>? fields)
    {
        var request = new Dictionary<string, object?> { ["subtype"] = subtype };
        if (fields != null) foreach (var (k, v) in fields) request[k] = v;
        Write(new { type = "control_request", request_id = "app-" + Interlocked.Increment(ref requests), request });
    }

    /// <summary>Stops what it's doing now (as Esc does in the CLI).</summary>
    public void Interrupt() => Control("interrupt", null);

    /// <summary>Answers a can_use_tool request: allowed (with the input as asked), or denied with a reason.</summary>
    public void Answer(string requestId, bool allow, JsonElement input, string? reason = null)
    {
        object response = allow
            ? new { behavior = "allow", updatedInput = input }
            : new { behavior = "deny", message = reason ?? "The user said no." };
        Write(new { type = "control_response", response = new { subtype = "success", request_id = requestId, response } });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { process.StandardInput.Close(); } catch { }
        try { if (!process.WaitForExit(1500)) process.Kill(true); } catch { }
        process.Dispose();
    }
}
