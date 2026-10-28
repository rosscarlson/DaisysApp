using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Windows file sharing (SMB, port 445), as nmap's smb-os-discovery and smb2-security-mode do: the newest SMB version it
/// speaks, whether it insists on signing, and from the start of a login (NTLM's challenge, sent before any password)
/// the computer's name, its domain and Windows' version and build. Then whether it still speaks SMB 1.
/// </summary>
internal static class Smb
{
    public static async Task ProbeAsync(IPAddress ip, ServiceInfo info, CancellationToken ct)
    {
        info.Protocol = "SMB";
        info.DeviceHints.Add(("computer", T("File sharing")));
        using (var client = await ConnectAsync(ip, ct))
        {
            var s = client.GetStream();
            var negotiate = await SendAsync(s, Header(0, 0).Concat(NegotiateBody()).ToArray(), ct);
            if (negotiate == null || negotiate.Length < 64 + 65 || negotiate[0] != 0xFE) { info.Add(T("SMB 2/3"), T("No answer")); }
            else
            {
                int mode = negotiate[64 + 2] | negotiate[64 + 3] << 8, dialect = negotiate[64 + 4] | negotiate[64 + 5] << 8;
                info.Add(T("Version agreed"), "SMB " + Dialect(dialect));
                bool signing = (mode & 2) != 0;
                info.Add(T("Signing"), signing ? T("Required") : T("Not required"));
                if (!signing) info.Findings.Add(new Finding(Severity.Medium, T("SMB signing not required"), T("Lets someone on the network relay a login to it (NTLM relay)."), "https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/overview-server-message-block-signing"));
                long time = BitConverter.ToInt64(negotiate, 64 + 40);
                if (time > 0) info.Add(T("Its clock"), DateTime.FromFileTimeUtc(time).ToLocalTime().ToString("g"));

                var setup = await SendAsync(s, Header(1, 1).Concat(SessionSetupBody(Spnego(NtlmNegotiate()))).ToArray(), ct);
                if (setup != null) Challenge(setup, info);
            }
        }
        // SMB 1: asked separately, as a new connection that only offers it
        try
        {
            using var client = await ConnectAsync(ip, ct);
            var r = await SendAsync(client.GetStream(), Smb1Negotiate(), ct);
            bool smb1 = r != null && r.Length > 9 && r[0] == 0xFF && r[1] == (byte)'S' && r[4] == 0x72 && r[5] == 0 && r[6] == 0 && r[7] == 0 && r[8] == 0;
            info.Add("SMB 1", smb1 ? T("On") : T("Off"));
            if (smb1) info.Findings.Add(new Finding(Severity.High, T("SMB 1 is on"), T("The old version WannaCry spread through: switch it off."), "https://learn.microsoft.com/en-us/windows-server/storage/file-server/troubleshoot/detect-enable-and-disable-smbv1-v2-v3"));
        }
        catch (Exception ex) when (ex is SocketException or IOException) { info.Add("SMB 1", T("Off")); }
    }

    private static string Dialect(int d) => d switch
    {
        0x0202 => "2.0.2",
        0x0210 => "2.1",
        0x0300 => "3.0",
        0x0302 => "3.0.2",
        0x0311 => "3.1.1",
        _ => $"0x{d:X4}",
    };

    private static async Task<TcpClient> ConnectAsync(IPAddress ip, CancellationToken ct)
    {
        var client = new TcpClient(ip.AddressFamily);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(4000);
        try { await client.ConnectAsync(ip, 445, wait.Token); }
        catch { client.Dispose(); throw; }
        return client;
    }

    /// <summary>Sends one message (with its 4-byte length in front) and reads the answer (without it).</summary>
    private static async Task<byte[]?> SendAsync(NetworkStream s, byte[] message, CancellationToken ct)
    {
        var framed = new byte[4 + message.Length];
        framed[1] = (byte)(message.Length >> 16);
        framed[2] = (byte)(message.Length >> 8);
        framed[3] = (byte)message.Length;
        message.CopyTo(framed, 4);
        await s.WriteAsync(framed, ct);
        var reply = await ServiceProbe.ReadAsync(s, ct, 65536, 4000, b => b.Count >= 4 && b.Count >= 4 + (b[1] << 16 | b[2] << 8 | b[3]));
        return reply.Length > 4 ? reply[4..] : null;
    }

    private static byte[] Header(ushort command, ulong messageId)
    {
        var h = new byte[64];
        h[0] = 0xFE; h[1] = (byte)'S'; h[2] = (byte)'M'; h[3] = (byte)'B';
        h[4] = 64;
        BitConverter.GetBytes(command).CopyTo(h, 12);
        h[14] = 1; // credits asked for
        BitConverter.GetBytes(messageId).CopyTo(h, 24);
        return h;
    }

    private static byte[] NegotiateBody()
    {
        ushort[] dialects = [0x0202, 0x0210, 0x0300, 0x0302];
        var b = new byte[36 + dialects.Length * 2];
        b[0] = 36;
        b[2] = (byte)dialects.Length;
        b[4] = 1; // signing enabled
        Guid.NewGuid().ToByteArray().CopyTo(b, 12);
        for (int i = 0; i < dialects.Length; i++) BitConverter.GetBytes(dialects[i]).CopyTo(b, 36 + i * 2);
        return b;
    }

    private static byte[] SessionSetupBody(byte[] token)
    {
        var b = new byte[24 + token.Length];
        b[0] = 25;
        b[3] = 1; // signing enabled
        BitConverter.GetBytes((ushort)(64 + 24)).CopyTo(b, 12);
        BitConverter.GetBytes((ushort)token.Length).CopyTo(b, 14);
        token.CopyTo(b, 24);
        return b;
    }

    private static byte[] NtlmNegotiate()
    {
        // Unicode, request target, NTLM, always sign, extended session security, target info, version, 128-bit, 56-bit
        uint flags = 0x1 | 0x4 | 0x200 | 0x8000 | 0x80000 | 0x800000 | 0x2000000 | 0x20000000 | 0x80000000;
        var b = new byte[40];
        Encoding.ASCII.GetBytes("NTLMSSP\0").CopyTo(b, 0);
        b[8] = 1;
        BitConverter.GetBytes(flags).CopyTo(b, 12);
        b[32] = 10; b[39] = 15; // version 10.0, NTLM revision 15
        return b;
    }

    /// <summary>The NTLM token wrapped as SPNEGO's first message (what Windows sends).</summary>
    private static byte[] Spnego(byte[] ntlm)
    {
        byte[] ntlmOid = [0x06, 0x0A, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x02, 0x02, 0x0A];
        var mechTypes = Der(0xA0, Der(0x30, ntlmOid));
        var mechToken = Der(0xA2, Der(0x04, ntlm));
        var init = Der(0xA0, Der(0x30, mechTypes.Concat(mechToken).ToArray()));
        byte[] spnegoOid = [0x06, 0x06, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x02];
        return Der(0x60, spnegoOid.Concat(init).ToArray());
    }

    public static byte[] Der(byte tag, byte[] content)
    {
        var b = new List<byte> { tag };
        if (content.Length < 128) b.Add((byte)content.Length);
        else if (content.Length < 256) { b.Add(0x81); b.Add((byte)content.Length); }
        else { b.Add(0x82); b.Add((byte)(content.Length >> 8)); b.Add((byte)content.Length); }
        b.AddRange(content);
        return b.ToArray();
    }

    /// <summary>NTLM's challenge: the computer's and domain's names, and Windows' version.</summary>
    private static void Challenge(byte[] r, ServiceInfo info)
    {
        int at = IndexOf(r, Encoding.ASCII.GetBytes("NTLMSSP\0"));
        if (at < 0 || r.Length < at + 56 || r[at + 8] != 2) return;
        var m = r.AsSpan(at).ToArray();
        int infoLen = BitConverter.ToUInt16(m, 40), infoOffset = BitConverter.ToInt32(m, 44);
        uint flags = BitConverter.ToUInt32(m, 20);
        if ((flags & 0x2000000) != 0 && m.Length >= 56)
        {
            int major = m[48], minor = m[49], build = BitConverter.ToUInt16(m, 50);
            if (build == 0)
            {
                // Samba says 6.1 with no build: Linux (a NAS, a router), not Windows 7
                info.Product = "Samba";
                info.Add(T("Software"), T("Samba (Linux, a NAS or a router)"));
                info.OsHints.Add("Linux (Samba)");
            }
            else
            {
                string name = WindowsName(major, minor, build);
                info.Add(T("Windows"), $"{name} ({major}.{minor}.{build})");
                info.OsHints.Add(name);
            }
        }
        for (int p = infoOffset; p + 4 <= Math.Min(m.Length, infoOffset + infoLen);)
        {
            int id = BitConverter.ToUInt16(m, p), len = BitConverter.ToUInt16(m, p + 2);
            if (id == 0 || p + 4 + len > m.Length) break;
            string v = Encoding.Unicode.GetString(m, p + 4, len);
            switch (id)
            {
                case 1: info.Add(T("Computer name"), v); break;
                case 2: info.Add(T("Domain / workgroup"), v); break;
                case 3: info.Add(T("Full name"), v); break;
                case 4: info.Add(T("DNS domain"), v); break;
                case 5: info.Add(T("Forest"), v); break;
            }
            p += 4 + len;
        }
    }

    /// <summary>Windows' name for a version and build (a server's builds are the same as a desktop's).</summary>
    public static string WindowsName(int major, int minor, int build) => (major, minor) switch
    {
        (10, 0) when build >= 26100 => "Windows 11 24H2+ / Server 2025",
        (10, 0) when build >= 22631 => "Windows 11 23H2",
        (10, 0) when build >= 22621 => "Windows 11 22H2",
        (10, 0) when build >= 22000 => "Windows 11 21H2",
        (10, 0) when build >= 20348 => "Windows Server 2022",
        (10, 0) when build >= 19041 => "Windows 10 20H1+",
        (10, 0) when build >= 17763 => "Windows 10 1809 / Server 2019",
        (10, 0) when build >= 14393 => "Windows 10 1607 / Server 2016",
        (10, 0) => "Windows 10",
        (6, 3) => "Windows 8.1 / Server 2012 R2",
        (6, 2) => "Windows 8 / Server 2012",
        (6, 1) => "Windows 7 / Server 2008 R2",
        (6, 0) => "Windows Vista / Server 2008",
        (5, _) => "Windows XP / Server 2003",
        _ => "Windows",
    };

    private static byte[] Smb1Negotiate()
    {
        var dialect = Encoding.ASCII.GetBytes("\x02NT LM 0.12\0");
        var b = new byte[32 + 3 + dialect.Length];
        b[0] = 0xFF; b[1] = (byte)'S'; b[2] = (byte)'M'; b[3] = (byte)'B';
        b[4] = 0x72;       // negotiate
        b[9] = 0x18;       // flags: case-insensitive, canonical names
        b[10] = 0x01; b[11] = 0xC8; // flags 2: long names, NT status, Unicode
        b[32] = 0;         // no words
        BitConverter.GetBytes((ushort)dialect.Length).CopyTo(b, 33);
        dialect.CopyTo(b, 35);
        return b;
    }

    private static int IndexOf(byte[] data, byte[] find)
    {
        for (int i = 0; i + find.Length <= data.Length; i++)
            if (data.AsSpan(i, find.Length).SequenceEqual(find)) return i;
        return -1;
    }
}
