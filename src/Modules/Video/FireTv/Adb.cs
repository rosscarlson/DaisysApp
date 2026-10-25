using System.IO;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// A small ADB client over the network (what `adb connect ip:5555` then `adb shell` do), so nothing needs installing:
/// the ADB messages (24-byte header, CNXN / AUTH / OPEN / OKAY / WRTE / CLSE), signing the device's token with our RSA
/// keys, and on a first connection sending our public key, which the TV asks to allow. One shell command at a time.
/// </summary>
internal sealed class AdbClient : IDisposable
{
    private const uint Cnxn = 0x4e584e43, Auth = 0x48545541, Open = 0x4e45504f, Okay = 0x59414b4f, Clse = 0x45534c43,
        Wrte = 0x45545257, Stls = 0x534c5453;
    private const uint Version = 0x01000001, MaxData = 256 * 1024;
    private const uint AuthToken = 1, AuthSignature = 2, AuthPublicKey = 3;

    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim gate = new(1, 1);
    private uint maxData = 4096, nextId = 1;

    /// <summary>The device's CNXN banner: "device::ro.product.model=AFTKM;…".</summary>
    public string Banner { get; private set; } = "";
    public string Address { get; }
    public bool Closed { get; private set; }

    private AdbClient(TcpClient tcp, string address)
    {
        this.tcp = tcp;
        stream = tcp.GetStream();
        Address = address;
    }

    /// <summary>
    /// Connects and signs in. <paramref name="asking"/> is called when the TV shows "Allow USB debugging?" (our key is
    /// new to it); then this waits up to 2 minutes for the answer.
    /// </summary>
    public static async Task<AdbClient> ConnectAsync(string host, int port, Action asking, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        using (var reach = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            reach.CancelAfter(TimeSpan.FromSeconds(4));
            try { await tcp.ConnectAsync(host, port, reach.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
                tcp.Dispose();
                if (ct.IsCancellationRequested) throw;
                throw new AdbException(F("Nothing answers on {0}:{1}. Turn on ADB Debugging on the TV (Settings → My Fire TV → Developer Options).", host, port));
            }
        }
        var c = new AdbClient(tcp, $"{host}:{port}");
        try
        {
            await c.HandshakeAsync(asking, ct);
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    private async Task HandshakeAsync(Action asking, CancellationToken ct)
    {
        using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
        time.CancelAfter(TimeSpan.FromSeconds(10));
        await SendAsync(Cnxn, Version, MaxData, Encoding.ASCII.GetBytes("host::\0"), time.Token);
        var keys = AdbKeys.All();
        int tried = 0;
        bool sentPublic = false;
        try
        {
            while (true)
            {
                var m = await ReadAsync(time.Token);
                if (m.Command == Cnxn)
                {
                    maxData = Math.Clamp(m.Arg1, 4096, MaxData);
                    Banner = Encoding.UTF8.GetString(m.Data).TrimEnd('\0');
                    return;
                }
                if (m.Command == Stls)
                    throw new AdbException(T("This device wants Wireless Debugging (pairing), which isn't supported: use ADB Debugging."));
                if (m.Command != Auth || m.Arg0 != AuthToken) continue;
                if (tried < keys.Count)
                {
                    var sig = keys[tried++].SignHash(m.Data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
                    await SendAsync(Auth, AuthSignature, 0, sig, time.Token);
                }
                else if (!sentPublic)
                {
                    sentPublic = true;
                    await SendAsync(Auth, AuthPublicKey, 0, AdbKeys.PublicKey(keys[0], $"{AppPaths.ShortName}@{Environment.MachineName}"), time.Token);
                    time.CancelAfter(TimeSpan.FromMinutes(2));
                    asking();
                }
                else throw new AdbException(T("The TV didn't allow this PC."));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AdbException(sentPublic ? T("No answer on the TV in time: connect again and pick Allow.") : T("The device didn't answer as ADB does."));
        }
        catch (IOException)
        {
            // (a TV that says Deny drops the connection)
            throw new AdbException(sentPublic ? T("The TV didn't allow this PC.") : T("The device closed the connection."));
        }
    }

    /// <summary>Runs a shell command and returns what it printed (stdout and stderr together).</summary>
    public async Task<string> ShellAsync(string command, CancellationToken ct = default, int seconds = 60)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
            time.CancelAfter(TimeSpan.FromSeconds(seconds));
            uint local = nextId++, remote = 0;
            await SendAsync(Open, local, 0, Encoding.UTF8.GetBytes("shell:" + command + "\0"), time.Token);
            var output = new MemoryStream();
            while (true)
            {
                var m = await ReadAsync(time.Token);
                if (m.Arg1 != local) continue;
                if (m.Command == Okay) remote = m.Arg0;
                else if (m.Command == Wrte)
                {
                    output.Write(m.Data);
                    remote = m.Arg0;
                    await SendAsync(Okay, local, remote, Array.Empty<byte>(), time.Token);
                }
                else if (m.Command == Clse)
                {
                    if (remote == 0) throw new AdbException(T("The device refused the command."));
                    await SendAsync(Clse, local, remote, Array.Empty<byte>(), time.Token);
                    return Encoding.UTF8.GetString(output.ToArray()).Replace("\r\n", "\n");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            Dispose();
            throw new AdbException(T("Lost the connection to the TV."));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Dispose();
            throw new AdbException(T("The TV stopped answering."));
        }
        finally { gate.Release(); }
    }

    private readonly record struct Message(uint Command, uint Arg0, uint Arg1, byte[] Data);

    private async Task SendAsync(uint command, uint arg0, uint arg1, byte[] data, CancellationToken ct)
    {
        if (data.Length > maxData && command != Cnxn) throw new AdbException("Message too long");
        var b = new byte[24 + data.Length];
        uint sum = 0;
        foreach (var x in data) sum += x;
        BitConverter.TryWriteBytes(b.AsSpan(0), command);
        BitConverter.TryWriteBytes(b.AsSpan(4), arg0);
        BitConverter.TryWriteBytes(b.AsSpan(8), arg1);
        BitConverter.TryWriteBytes(b.AsSpan(12), (uint)data.Length);
        BitConverter.TryWriteBytes(b.AsSpan(16), sum);
        BitConverter.TryWriteBytes(b.AsSpan(20), command ^ 0xffffffff);
        data.CopyTo(b, 24);
        await stream.WriteAsync(b, ct);
    }

    private async Task<Message> ReadAsync(CancellationToken ct)
    {
        var h = new byte[24];
        await stream.ReadExactlyAsync(h, ct);
        uint command = BitConverter.ToUInt32(h, 0), length = BitConverter.ToUInt32(h, 12), magic = BitConverter.ToUInt32(h, 20);
        if ((command ^ 0xffffffff) != magic || length > MaxData) throw new AdbException(T("The device didn't answer as ADB does."));
        var data = new byte[length];
        if (length > 0) await stream.ReadExactlyAsync(data, ct);
        return new Message(command, BitConverter.ToUInt32(h, 4), BitConverter.ToUInt32(h, 8), data);
    }

    public void Dispose()
    {
        if (Closed) return;
        Closed = true;
        try { tcp.Dispose(); } catch { }
    }
}

internal sealed class AdbException(string message) : Exception(message);

/// <summary>
/// The RSA keys ADB signs in with: our own (made once, %APPDATA%\DaisysApp\FireTv\adbkey, a PKCS#8 PEM as adb's), then
/// adb's own (%USERPROFILE%\.android\adbkey) if this PC has one, since the TV may know it already.
/// </summary>
internal static class AdbKeys
{
    private static readonly object locker = new();
    private static RSA? own;

    public static string OwnPath => Path.Combine(AppPaths.SettingsFolder, "FireTv", "adbkey");

    public static List<RSA> All()
    {
        var keys = new List<RSA> { Own() };
        try
        {
            var adb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "adbkey");
            if (File.Exists(adb))
            {
                var r = RSA.Create();
                r.ImportFromPem(File.ReadAllText(adb));
                if (r.KeySize == 2048) keys.Add(r);
            }
        }
        catch { }
        return keys;
    }

    private static RSA Own()
    {
        lock (locker)
        {
            if (own != null) return own;
            var r = RSA.Create();
            try
            {
                r.ImportFromPem(File.ReadAllText(OwnPath));
                if (r.KeySize == 2048) return own = r;
            }
            catch { }
            r = RSA.Create(2048);
            Directory.CreateDirectory(Path.GetDirectoryName(OwnPath)!);
            File.WriteAllText(OwnPath, r.ExportPkcs8PrivateKeyPem());
            Logging.Log.Here.Info("Made this PC's ADB key");
            return own = r;
        }
    }

    /// <summary>
    /// The public key as adbd wants it: base64 of Android's RSAPublicKey struct (modulus size in words, n0inv = -1/n mod
    /// 2^32, modulus and R² mod n as little-endian words, exponent), then " name" and a nul.
    /// </summary>
    public static byte[] PublicKey(RSA rsa, string name)
    {
        var p = rsa.ExportParameters(false);
        const int words = 64;
        var n = new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true);
        uint n0 = (uint)(n & uint.MaxValue), inv = n0;
        for (int i = 0; i < 5; i++) inv *= 2 - n0 * inv;
        var rr = BigInteger.ModPow(2, words * 32 * 2, n);
        var b = new byte[4 + 4 + words * 4 * 2 + 4];
        BitConverter.TryWriteBytes(b.AsSpan(0), (uint)words);
        BitConverter.TryWriteBytes(b.AsSpan(4), 0u - inv);
        n.TryWriteBytes(b.AsSpan(8, words * 4), out _, isUnsigned: true, isBigEndian: false);
        rr.TryWriteBytes(b.AsSpan(8 + words * 4, words * 4), out _, isUnsigned: true, isBigEndian: false);
        uint e = 0;
        foreach (var x in p.Exponent!) e = (e << 8) | x;
        BitConverter.TryWriteBytes(b.AsSpan(8 + words * 8), e);
        return Encoding.ASCII.GetBytes(Convert.ToBase64String(b) + " " + name + "\0");
    }
}
