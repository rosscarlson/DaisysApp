using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>A TCP port tried: open (it accepted), closed (refused) or no answer (a firewall dropped it).</summary>
internal enum PortState { Open, Closed, Filtered }

/// <summary>What a port said: its state, how long it took to accept, and its banner (what it sent first, if anything).</summary>
internal sealed record PortResult(int Port, PortState State, double Ms, string Banner, string RawBanner);

/// <summary>Connects to TCP ports, as Port Scan and Host Scan do, and reads what a service says first.</summary>
internal static class PortProbe
{
    /// <summary>Connects to a port: open (with what it says first, if it says something within a second), refused or no answer.</summary>
    public static async Task<PortResult> ProbeAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct, int bannerMs = 1000)
    {
        using var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(timeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            // (timed off the UI thread: waiting for it there would add whatever it's busy with)
            await socket.ConnectAsync(new IPEndPoint(ip, port), wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(port, PortState.Filtered, double.NaN, "", ""); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused) { return new(port, PortState.Closed, double.NaN, "", ""); }
        catch (SocketException) { return new(port, PortState.Filtered, double.NaN, "", ""); }
        double ms = sw.Elapsed.TotalMilliseconds;
        string raw = "";
        try
        {
            using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
            read.CancelAfter(bannerMs);
            var buffer = new byte[2048];
            int n = await socket.ReceiveAsync(buffer, SocketFlags.None, read.Token).ConfigureAwait(false);
            raw = Printable(Encoding.Latin1.GetString(buffer, 0, n), keepLines: true);
        }
        catch { /* most services wait for the client to speak first */ }
        return new(port, PortState.Open, ms, FirstLine(raw), raw);
    }

    /// <summary>The first line, cut to 120 characters.</summary>
    public static string FirstLine(string text)
    {
        string first = text.Split('\n')[0].Trim();
        return first.Length > 120 ? first[..120] + "…" : first;
    }

    /// <summary>Printable characters only (SSH, for one, carries on in binary after its first line; Telnet sends options).</summary>
    public static string Printable(string text, bool keepLines = false) =>
        new string(text.Where(c => c is >= ' ' and < (char)127 || keepLines && c == '\n').ToArray()).Trim();
}
