using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Terminal.Wpf;
using Microsoft.Win32.SafeHandles;

namespace DaisysApp.Applets.Tools.Terminal;

/// <summary>
/// A shell (CMD, PowerShell or WSL) on a Windows pseudo console (ConPTY), connected to a terminal control: what the
/// shell writes goes to the control, and what's typed (or pasted) goes to the shell. When the shell ends with an error,
/// it says so in the terminal and Enter starts it again; ending normally (exit) raises <see cref="Exited"/>.
/// </summary>
internal sealed class ShellConnection : ITerminalConnection, IDisposable
{
    private readonly ShellKind kind;
    private readonly object gate = new();
    private nint console;
    private SafeFileHandle? input, output;
    private FileStream? writer;
    private nint process;
    private uint rows = 25, columns = 80;
    private bool started, ended, disposed;

    public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

    /// <summary>The shell ended normally (exit code 0), raised on a background thread.</summary>
    public event Action? Exited;

    /// <summary>The shell has turned bracketed paste on (bash, zsh…): a paste is wrapped so it isn't run line by line.</summary>
    public bool BracketedPaste { get; private set; }

    public ShellConnection(ShellKind kind) => this.kind = kind;

    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
        }
        Launch();
    }

    private void Launch()
    {
        string command = Shells.CommandLine(kind);
        if (Shells.Missing(kind) is { } missing)
        {
            Write(F("{0} isn't installed on this PC.", missing) + "\r\n");
            ended = true;
            return;
        }
        try
        {
            if (!CreatePipe(out var inRead, out var inWrite, 0, 0) || !CreatePipe(out var outRead, out var outWrite, 0, 0))
                throw new Win32Exception();
            int hr = CreatePseudoConsole(new Coord((short)columns, (short)rows), inRead, outWrite, 0, out var pc);
            // (the pseudo console has its own copies of its ends)
            inRead.Dispose();
            outWrite.Dispose();
            if (hr != 0) throw new Win32Exception(hr);

            var info = new StartupInfoEx();
            info.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            // (no standard handles of ours: a parent with a console would hand them on, and the shell would write there)
            info.StartupInfo.dwFlags = UseStdHandles;
            nint size = 0;
            InitializeProcThreadAttributeList(0, 1, 0, ref size);
            info.AttributeList = Marshal.AllocHGlobal(size);
            try
            {
                if (!InitializeProcThreadAttributeList(info.AttributeList, 1, 0, ref size)) throw new Win32Exception();
                if (!UpdateProcThreadAttribute(info.AttributeList, 0, ProcThreadAttributePseudoConsole, pc, IntPtr.Size, 0, 0)) throw new Win32Exception();
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!CreateProcess(null, new StringBuilder(command), 0, 0, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment, 0, folder, ref info, out var pi))
                    throw new Win32Exception();
                CloseHandle(pi.hThread);
                lock (gate)
                {
                    console = pc;
                    input = inWrite;
                    output = outRead;
                    writer = new FileStream(inWrite, FileAccess.Write, 1, false);
                    process = pi.hProcess;
                    ended = false;
                }
            }
            finally
            {
                DeleteProcThreadAttributeList(info.AttributeList);
                Marshal.FreeHGlobal(info.AttributeList);
            }
            Logging.Log.Here.Info($"Terminal: started {command}");
            new Thread(() => Read(outRead)) { IsBackground = true, Name = "DaisysApp-Terminal-read" }.Start();
            new Thread(() => Wait(process)) { IsBackground = true, Name = "DaisysApp-Terminal-wait" }.Start();
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Terminal: {command} didn't start", ex);
            Write(F("{0} didn't start: {1}", command, ex.Message) + "\r\n");
            ended = true;
        }
    }

    /// <summary>What the shell writes, to the terminal (UTF-8, characters split between reads kept for the next).</summary>
    private void Read(SafeFileHandle from)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16384];
        var chars = new char[16384 + 4];
        try
        {
            using var stream = new FileStream(from, FileAccess.Read, 1, false);
            int n;
            while ((n = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                int c = decoder.GetChars(bytes, 0, n, chars, 0);
                if (c == 0) continue;
                string text = new(chars, 0, c);
                if (text.Contains("\u001b[?2004h")) BracketedPaste = true;
                if (text.Contains("\u001b[?2004l")) BracketedPaste = false;
                Write(text);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    /// <summary>Waits for the shell to end (this thread owns its handle and closes it).</summary>
    private void Wait(nint handle)
    {
        WaitForSingleObject(handle, uint.MaxValue);
        GetExitCodeProcess(handle, out uint code);
        CloseHandle(handle);
        lock (gate)
        {
            if (disposed || handle != process) return;
            process = 0;
            ended = true;
        }
        Logging.Log.Here.Info($"Terminal: {Shells.Name(kind)} ended (exit code {code})");
        Release();
        BracketedPaste = false;
        if (code == 0) Exited?.Invoke();
        else Write("\r\n\u001b[0m" + F("[Ended with exit code {0}. Press Enter to start it again.]", code) + "\r\n");
    }

    private void Write(string text) => TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));

    public void WriteInput(string data)
    {
        if (ended)
        {
            // (Enter after it ended: the shell again, in the same terminal)
            if (data.Contains('\r') && !disposed)
            {
                Write("\r\n");
                Launch();
            }
            return;
        }
        FileStream? w;
        lock (gate) w = writer;
        if (w == null) return;
        try
        {
            var b = Encoding.UTF8.GetBytes(data);
            w.Write(b, 0, b.Length);
            w.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    /// <summary>Text pasted: as typed, but with Windows' line ends as Enter, and wrapped when the shell asked for it.</summary>
    public void Paste(string text)
    {
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        WriteInput(BracketedPaste ? "\u001b[200~" + text + "\u001b[201~" : text);
    }

    public void Resize(uint rows, uint columns)
    {
        if (rows == 0 || columns == 0) return;
        lock (gate)
        {
            this.rows = rows;
            this.columns = columns;
            if (console != 0) ResizePseudoConsole(console, new Coord((short)Math.Min(columns, short.MaxValue), (short)Math.Min(rows, short.MaxValue)));
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        // (closing the pseudo console ends the shell; it can wait for output to be read, so not on the UI thread)
        Task.Run(Release);
    }

    private void Release()
    {
        nint pc;
        FileStream? w;
        SafeFileHandle? inp, outp;
        lock (gate)
        {
            (pc, w, inp, outp) = (console, writer, input, output);
            console = 0;
            writer = null;
            input = output = null;
        }
        try { w?.Dispose(); } catch { }
        inp?.Dispose();
        // (ends the shell, if it's still running: its wait thread then closes its handle)
        if (pc != 0) ClosePseudoConsole(pc);
        outp?.Dispose();
    }

    // ---- Windows

    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint ExtendedStartupInfoPresent = 0x00080000, CreateUnicodeEnvironment = 0x00000400;
    private const int UseStdHandles = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord(short x, short y) { public short X = x, Y = y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, nint attributes, int size);
    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);
    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(nint console, Coord size);
    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(nint console);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previous, nint returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? application, StringBuilder commandLine, nint processAttributes, nint threadAttributes,
        bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfoEx startup, out ProcessInformation info);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint ms);
    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
