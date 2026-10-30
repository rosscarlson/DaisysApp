using System.Runtime.InteropServices;
using DaisysApp.Settings;
using DaisysServer.Protocol;

namespace DaisysApp.Server;

/// <summary>Saved in %APPDATA%\DaisysApp\DaisysServer.json: where Daisy's Server is and who signs in there (the token
/// and password kept encrypted for this Windows user).</summary>
public sealed class ServerLinkSettings
{
    public const int DefaultPort = 38900;
    public string Host { get; set; } = "";
    public int Port { get; set; } = DefaultPort;
    public string User { get; set; } = "";
    public string TokenProtected { get; set; } = "";
    public string PasswordProtected { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Token { get => Dpapi.Unprotect(TokenProtected); set => TokenProtected = Dpapi.Protect(value); }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Password { get => Dpapi.Unprotect(PasswordProtected); set => PasswordProtected = Dpapi.Protect(value); }

    public bool Complete => Host.Length > 0 && TokenProtected.Length > 0 && User.Length > 0 && PasswordProtected.Length > 0;
}

/// <summary>
/// Daisy's App's one connection to Daisy's Server (D:\OneDrive\Code\Windows\DaisysServer), shared by every module
/// that uses it: made when a module first opens a channel, signed in as the user set up here, and made again by the
/// next channel opened after it's lost. A module opens a channel to its counterpart on the server
/// (<see cref="OpenAsync"/>) and seals what it stores there with the user's key (<c>ServerChannel.DataKey</c>), so the
/// server keeps it without being able to read it. The protocol is the server's PROTOCOL.md.
/// </summary>
public static class ServerLink
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static ServerClient? client;
    private static ServerLinkSettings? settings;

    public static ServerLinkSettings Settings => settings ??= JsonStore.Load<ServerLinkSettings>("DaisysServer");

    /// <summary>Set up (an address, a token, a user and a password).</summary>
    public static bool Configured => Settings.Complete;

    /// <summary>Connected now.</summary>
    public static bool Connected => client is { IsClosed: false };

    /// <summary>The server's name (as it calls itself, once connected), else its address.</summary>
    public static string Name => client is { IsClosed: false } c && c.Server.Length > 0 ? c.Server : Settings.Host;

    /// <summary>Why the last try failed, or "".</summary>
    public static string Problem { get; private set; } = "";

    /// <summary>Connected, lost, or set up again (on any thread).</summary>
    public static event Action? Changed;

    /// <summary>Opens a channel to one of the server's modules (connecting first if need be). Throws ServerException,
    /// worded for the user.</summary>
    public static async Task<ServerChannel> OpenAsync(string module, string from, CancellationToken ct)
    {
        var c = await ClientAsync(ct);
        return await c.OpenAsync(module, from, ct);
    }

    private static async Task<ServerClient> ClientAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (client is { IsClosed: false } c) return c;
            var s = Settings;
            if (!s.Complete) throw new ServerException(T("Daisy's Server isn't set up (Settings → Tools → Notepad)"));
            try
            {
                c = await ServerClient.ConnectAsync(s.Host, s.Port, s.Token, s.User, s.Password, Environment.MachineName, $"{AppPaths.ShortName}", ct);
            }
            catch (ServerException ex)
            {
                Problem = ex.Message;
                Changed?.Invoke();
                throw;
            }
            c.Closed += why =>
            {
                Logging.Log.App.Info($"Daisy's Server: disconnected ({why})");
                Problem = why;
                Changed?.Invoke();
            };
            client = c;
            Problem = "";
            Logging.Log.App.Info($"Daisy's Server: connected to {c.Server} ({s.Host}:{s.Port}) as {c.User}");
            Changed?.Invoke();
            return c;
        }
        finally { gate.Release(); }
    }

    /// <summary>Tries the given details; kept (and the connection made again with them) if they work. A problem, or null.</summary>
    public static async Task<string?> SetUpAsync(string host, int port, string token, string user, string password, CancellationToken ct = default)
    {
        ServerClient test;
        try { test = await ServerClient.ConnectAsync(host.Trim(), port, token.Trim(), user.Trim(), password, Environment.MachineName, AppPaths.ShortName, ct); }
        catch (ServerException ex) { return ex.Message; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ex.Message; }
        test.Dispose();
        var s = Settings;
        s.Host = host.Trim();
        s.Port = port;
        s.Token = token.Trim();
        s.User = user.Trim();
        s.Password = password;
        JsonStore.Save("DaisysServer", s);
        Disconnect();
        Logging.Log.App.Info($"Daisy's Server: set up {s.Host}:{s.Port} as {s.User}");
        return null;
    }

    /// <summary>Changes the user's password there (their data key wrapped again: nothing else changes). A problem, or null.</summary>
    public static async Task<string?> ChangePasswordAsync(string oldPassword, string newPassword)
    {
        if (newPassword.Length < 8) return T("A password is at least 8 characters");
        try
        {
            var c = await ClientAsync(CancellationToken.None);
            await c.ChangePasswordAsync(oldPassword, newPassword);
        }
        catch (ServerException ex) { return ex.Message; }
        Settings.Password = newPassword;
        JsonStore.Save("DaisysServer", Settings);
        Logging.Log.App.Info("Daisy's Server: password changed");
        return null;
    }

    /// <summary>Forgets the details here (the server keeps the user's data).</summary>
    public static void Forget()
    {
        settings = new ServerLinkSettings();
        JsonStore.Save("DaisysServer", settings);
        Disconnect();
    }

    /// <summary>Closes the connection (its channels end; modules connect again when they next open one).</summary>
    public static void Disconnect()
    {
        var c = client;
        client = null;
        c?.Dispose();
        Problem = "";
        Changed?.Invoke();
    }
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
