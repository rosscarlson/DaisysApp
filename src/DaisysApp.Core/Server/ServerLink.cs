using System.Runtime.InteropServices;
using DaisysApp.Settings;
using DaisysServer.Protocol;

namespace DaisysApp.Server;

/// <summary>Saved in %APPDATA%\DaisysApp\DaisysServer.json: where Daisy's Server is, its key's fingerprint (kept from the
/// first connection), who signs in there (the password kept encrypted for this Windows user), and this install's
/// device id (never shown: what it shares anonymously is its own by it).</summary>
public sealed class ServerLinkSettings
{
    public const int DefaultPort = 38900;

    public string Host { get; set; } = "";
    public int Port { get; set; } = DefaultPort;
    public string ServerKey { get; set; } = "";
    public string User { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
    public string Device { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Password { get => Dpapi.Unprotect(PasswordProtected); set => PasswordProtected = Dpapi.Protect(value); }

    /// <summary>Has a server (an address).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Complete => Host.Length > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SignedIn => User.Length > 0 && PasswordProtected.Length > 0;
}

/// <summary>
/// (since 4.18.0) A connection to a server that speaks Daisy's Server's protocol (its PROTOCOL.md), with its own
/// settings file: Daisy's Server (<see cref="ServerLink"/>), or another, like ForzaLeaderBoard for SimEffects → Analysis'
/// Share Session. Made when a module first opens a channel, to the server with the key it expects (a built-in server's
/// pinned, else kept from the first connection), signed in as the user set up or anonymous (only modules that allow it:
/// sessions), and made again by the next channel opened after it's lost. Every connection sends this install's one
/// device id (<see cref="ServerLink.Device"/>), so what it shares anonymously is its own on any of them.
/// </summary>
public sealed class ServerConnection
{
    /// <summary>A server built in: its address, port and key's fingerprint, used while no other is set up.</summary>
    public sealed record BuiltIn(string Host, int Port, string Key);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string file;
    private readonly BuiltIn? builtIn;
    private ServerClient? client;
    private ServerLinkSettings? settings;
    /// <summary>(since 4.17.28) Every connection try, whichever module asks: after a failure (or a connection the server
    /// closed within 30 s) the next waits 5 s, doubled each time up to 5 minutes; 30 tries in 10 minutes stop them until
    /// Connect, Forget or a new key (4.17.26's modules tried ~1,000 times a second and used up the PC's ports).</summary>
    private readonly Shared.Network.RetryGuard guard;

    /// <param name="title">Its name in the log and in problems ("Daisy's Server").</param>
    /// <param name="file">Its settings file in %APPDATA%\DaisysApp (no ".json").</param>
    /// <param name="builtIn">The server used while no other is set up, its key pinned (never trusted on first use).</param>
    public ServerConnection(string title, string file, BuiltIn? builtIn = null)
    {
        Title = title;
        this.file = file;
        this.builtIn = builtIn;
        guard = new(title, Logging.Log.App, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), alertAfter: 10, budget: 30, notify: true);
    }

    public string Title { get; }

    public ServerLinkSettings Settings => settings ??= JsonStore.Load<ServerLinkSettings>(file);

    /// <summary>The server built in, if there's one.</summary>
    public BuiltIn? Default => builtIn;

    /// <summary>The built-in server is the one used (no other address set up).</summary>
    public bool UsesBuiltIn => builtIn != null && Settings.Host.Length == 0;

    /// <summary>Where it connects: the address set up, else the built-in one's.</summary>
    public string Host => UsesBuiltIn ? builtIn!.Host : Settings.Host;
    public int Port => UsesBuiltIn ? builtIn!.Port : Settings.Port;
    /// <summary>The key's fingerprint it expects: the built-in one's, else kept from the first connection ("" until then).</summary>
    public string Key => UsesBuiltIn ? builtIn!.Key : Settings.ServerKey;

    /// <summary>Has a server (an address, or a built-in one).</summary>
    public bool Configured => Host.Length > 0;

    /// <summary>Signs in as a user (set up with a user and password): for the modules that need one.</summary>
    public bool SignedIn => Settings.SignedIn;

    /// <summary>Connected now.</summary>
    public bool Connected => client is { IsClosed: false };

    /// <summary>The server's name (as it calls itself, once connected), else its address.</summary>
    public string Name => client is { IsClosed: false } c && c.Server.Length > 0 ? c.Server : Host;

    /// <summary>Why the last try failed, or "".</summary>
    public string Problem { get; private set; } = "";

    /// <summary>Connected, lost, a try failed, or set up again (on any thread): for showing the state.</summary>
    public event Action? Changed;

    /// <summary>Connected, set up again or forgotten (on any thread): when a module waiting to try again should. Not
    /// raised by a failed try, so modules' tries don't wake each other in a loop (4.17.26 did, through Changed).</summary>
    public event Action? Retry;

    /// <summary>Opens a channel to one of the server's modules (connecting first if need be). Throws ServerException,
    /// worded for the user.</summary>
    public async Task<ServerChannel> OpenAsync(string module, string from, CancellationToken ct)
    {
        var c = await ClientAsync(ct);
        return await c.OpenAsync(module, from, ct);
    }

    private async Task<ServerClient> ClientAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (client is { IsClosed: false } c) return c;
            var s = Settings;
            if (!Configured) throw new ServerException(F("{0} isn't set up (Settings → General)", Title));
            if (!guard.Begin())
            {
                // (nothing goes out: too soon after a failure, or stopped)
                string held = guard.Holding ?? T("waiting");
                if (guard.Paused && Problem != held) { Problem = held; Changed?.Invoke(); }
                throw new ServerException(Problem.Length > 0 && !guard.Paused ? Problem : held);
            }
            string host = Host;
            int port = Port;
            try
            {
                c = await ServerClient.ConnectAsync(host, port, Key,
                    s.SignedIn ? s.User : null, s.SignedIn ? s.Password : null, Environment.MachineName, AppPaths.ShortName, ServerLink.Device(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                guard.Failed(ex.Message);
                Problem = ex.Message;
                Changed?.Invoke();
                throw;
            }
            if (!UsesBuiltIn && s.ServerKey.Length == 0)
            {
                // trusted on first use: only this server's let in from now on
                s.ServerKey = c.ServerKey;
                Save();
            }
            long opened = Environment.TickCount64;
            c.Closed += why =>
            {
                Logging.Log.App.Info($"{Title}: disconnected ({why})");
                // (a server that lets the app in and closes it again at once counts as a failure, so it isn't connected to in a loop)
                if (Environment.TickCount64 - opened < 30_000) guard.Failed(why);
                Problem = why;
                Changed?.Invoke();
            };
            client = c;
            Problem = "";
            Logging.Log.App.Info($"{Title}: connected to {c.Server} ({host}:{port}) {(c.Anonymous ? "anonymously" : $"as {c.User}")}");
            Changed?.Invoke();
            Retry?.Invoke();
            // (the waits start again once it's stayed connected 30 s)
            _ = Task.Delay(30_000).ContinueWith(_ => { if (!c.IsClosed) guard.Succeeded(); }, TaskScheduler.Default);
            return c;
        }
        finally { gate.Release(); }
    }

    private void Save() => JsonStore.Save(file, Settings);

    /// <summary>Tries the given details (no user and password: anonymous; no address: the built-in server, where there's
    /// one); kept (and the connection made again with them) if they work. <paramref name="key"/>: the fingerprint another
    /// server must have (empty: the one it had, else trusted on first use). A problem, or null.</summary>
    public async Task<string?> SetUpAsync(string host, int port, string user, string password, string key = "", CancellationToken ct = default)
    {
        host = host.Trim();
        user = user.Trim();
        key = key.Trim();
        if ((user.Length == 0) != (password.Length == 0)) return T("Give a user and a password, or neither");
        // (the built-in server's address typed: the built-in server)
        if (builtIn != null && host.Equals(builtIn.Host, StringComparison.OrdinalIgnoreCase) && port == builtIn.Port) host = "";
        if (host.Length == 0 && builtIn == null) return T("Give the server's address");
        var s = Settings;
        // the built-in server has its key; another is trusted on first use (unless it's given); the same one has to have the key it had
        string expect = host.Length == 0 ? builtIn!.Key : key.Length > 0 ? key : host == s.Host && port == s.Port ? s.ServerKey : "";
        ServerClient test;
        try
        {
            test = await ServerClient.ConnectAsync(host.Length > 0 ? host : builtIn!.Host, host.Length > 0 ? port : builtIn!.Port, expect,
                user.Length > 0 ? user : null, password, Environment.MachineName, AppPaths.ShortName, ServerLink.Device(), ct);
        }
        catch (ServerException ex) { return ex.Message; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ex.Message; }
        string seen = test.ServerKey;
        test.Dispose();
        s.Host = host;
        s.Port = host.Length > 0 ? port : ServerLinkSettings.DefaultPort;
        s.ServerKey = host.Length > 0 ? seen : "";
        s.User = user;
        s.Password = password;
        Save();
        guard.Reset();
        Disconnect();
        Logging.Log.App.Info($"{Title}: set up {Host}:{Port} {(user.Length > 0 ? $"as {user}" : "anonymously")}");
        return null;
    }

    /// <summary>Forgets the server's key (after it was reinstalled): the next connection trusts the one it has (not a
    /// built-in server's, which is pinned).</summary>
    public void ForgetKey()
    {
        Settings.ServerKey = "";
        Save();
        guard.Reset();
        Disconnect();
        Logging.Log.App.Info($"{Title}: its key forgotten");
    }

    /// <summary>Changes the user's password there (their data key wrapped again: nothing else changes). A problem, or null.</summary>
    public async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        if (newPassword.Length < 8) return T("A password is at least 8 characters");
        try
        {
            var c = await ClientAsync(CancellationToken.None);
            await c.ChangePasswordAsync(oldPassword, newPassword);
        }
        catch (ServerException ex) { return ex.Message; }
        Settings.Password = newPassword;
        Save();
        Logging.Log.App.Info($"{Title}: password changed");
        return null;
    }

    /// <summary>No user here any more (anonymous from now on; the server set up stays).</summary>
    public void SignOut()
    {
        Settings.User = "";
        Settings.Password = "";
        Save();
        guard.Reset();
        Disconnect();
        Logging.Log.App.Info($"{Title}: signed out");
    }

    /// <summary>Forgets the details here (the server keeps the user's data; this install's id stays): the built-in
    /// server again, anonymous, where there's one.</summary>
    public void Forget()
    {
        settings = new ServerLinkSettings { Device = Settings.Device };
        Save();
        guard.Reset();
        Disconnect();
    }

    /// <summary>Closes the connection (its channels end; modules connect again when they next open one).</summary>
    public void Disconnect()
    {
        var c = client;
        client = null;
        c?.Dispose();
        Problem = "";
        Changed?.Invoke();
        Retry?.Invoke();
    }
}

/// <summary>
/// Daisy's App's one connection to Daisy's Server (D:\OneDrive\Code\Windows\DaisysServer), shared by every module
/// that uses it (set up in Settings → General; since 4.18.0 a <see cref="ServerConnection"/>). A module opens a channel
/// to its counterpart on the server (<see cref="Keep"/>, or <see cref="OpenAsync"/> for one try) and seals what it
/// stores there with the user's key (<c>ServerChannel.DataKey</c>), so the server keeps it without being able to read
/// it. The protocol is the server's PROTOCOL.md.
/// </summary>
public static class ServerLink
{
    public static ServerConnection Connection { get; } = new("Daisy's Server", "DaisysServer");

    public static ServerLinkSettings Settings => Connection.Settings;

    /// <summary>Has a server (an address).</summary>
    public static bool Configured => Connection.Configured;

    /// <summary>Signs in as a user (set up with a user and password): for the modules that need one.</summary>
    public static bool SignedIn => Connection.SignedIn;

    /// <summary>Connected now.</summary>
    public static bool Connected => Connection.Connected;

    /// <summary>The server's name (as it calls itself, once connected), else its address.</summary>
    public static string Name => Connection.Name;

    /// <summary>Why the last try failed, or "".</summary>
    public static string Problem => Connection.Problem;

    /// <summary>Connected, lost, a try failed, or set up again (on any thread): for showing the state.</summary>
    public static event Action? Changed { add => Connection.Changed += value; remove => Connection.Changed -= value; }

    /// <summary>Connected, set up again or forgotten (on any thread): when a module waiting to try again should.</summary>
    public static event Action? Retry { add => Connection.Retry += value; remove => Connection.Retry -= value; }

    /// <summary>A channel to one of the server's modules, kept open (see <see cref="KeptChannel"/>): hook up its events,
    /// then <see cref="KeptChannel.Start"/> is called here once they can be. Dispose it to close it. Opened only while
    /// signed in, unless the server's module allows <paramref name="anonymous"/> ones.</summary>
    public static KeptChannel Keep(string module, string from, Action<KeptChannel> hookUp, bool anonymous = false)
    {
        var k = new KeptChannel(module, from, anonymous);
        hookUp(k);
        k.Start();
        return k;
    }

    /// <summary>Opens a channel to one of the server's modules (connecting first if need be). Throws ServerException,
    /// worded for the user.</summary>
    public static Task<ServerChannel> OpenAsync(string module, string from, CancellationToken ct) => Connection.OpenAsync(module, from, ct);

    /// <summary>This install's id, made once and kept in Daisy's Server's settings (never shown or logged): every
    /// server connection sends it, so what it shares anonymously is its own.</summary>
    public static string Device()
    {
        var s = Settings;
        if (s.Device.Length == 0)
        {
            s.Device = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            JsonStore.Save("DaisysServer", s);
        }
        return s.Device;
    }

    /// <summary>Tries the given details (no user and password: anonymous); kept if they work. A problem, or null.</summary>
    public static Task<string?> SetUpAsync(string host, int port, string user, string password, CancellationToken ct = default) =>
        Connection.SetUpAsync(host, port, user, password, "", ct);

    /// <summary>Forgets the server's key (after it was reinstalled): the next connection trusts the one it has.</summary>
    public static void ForgetKey() => Connection.ForgetKey();

    /// <summary>Changes the user's password there. A problem, or null.</summary>
    public static Task<string?> ChangePasswordAsync(string oldPassword, string newPassword) => Connection.ChangePasswordAsync(oldPassword, newPassword);

    /// <summary>Forgets the details here (the server keeps the user's data; this install's id stays).</summary>
    public static void Forget() => Connection.Forget();

    /// <summary>Closes the connection (its channels end; modules connect again when they next open one).</summary>
    public static void Disconnect() => Connection.Disconnect();
}

/// <summary>Windows' DPAPI for this user (CryptProtectData), so Core needs no package for it.</summary>
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const int UiForbidden = 1;

    public static string Protect(string value) => value.Length == 0 ? "" : Convert.ToBase64String(Run(System.Text.Encoding.UTF8.GetBytes(value), true));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return System.Text.Encoding.UTF8.GetString(Run(Convert.FromBase64String(value), false)); }
        catch { return ""; }
    }

    private static byte[] Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var output = new Blob();
            bool ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var r = new byte[output.Size];
                Marshal.Copy(output.Data, r, 0, output.Size);
                return r;
            }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
