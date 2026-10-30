using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DaisysApp.Shared.Network;

/// <summary>One ping's answer: its time in ms (NaN if none), Windows' status, and whether the name wasn't found.
/// <see cref="IPStatus.Unknown"/> with <see cref="NotFound"/> false: the ping itself failed.</summary>
public readonly record struct PingResult(double Ms, IPStatus Status, bool NotFound)
{
    public bool Answered => !double.IsNaN(Ms);
}

/// <summary>
/// Pings for every module (Hardware → Performance's Network tests, Support → Ping): an address is pinged once for all
/// of them. A caller gets an answer taken less than <c>maxAgeMs</c> ago if there is one, joins a ping to that address
/// still waiting, or else sends one. (Since Core 0.48: before, both modules pinged the router, 1.1.1.1 and 8.8.8.8
/// every second, each on its own.)
/// </summary>
public static class Pings
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, (DateTime At, PingResult Result)> last = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<PingResult>> waiting = new(StringComparer.OrdinalIgnoreCase);

    public static Task<PingResult> SendAsync(string address, int timeoutMs, int maxAgeMs)
    {
        address = address.Trim();
        lock (gate)
        {
            if (last.TryGetValue(address, out var l) && (DateTime.UtcNow - l.At).TotalMilliseconds < maxAgeMs) return Task.FromResult(l.Result);
            if (waiting.TryGetValue(address, out var w)) return w;
            var task = PingAsync(address, timeoutMs);
            if (!task.IsCompleted) waiting[address] = task; // (one that failed at once has removed itself already)
            return task;
        }
    }

    private static async Task<PingResult> PingAsync(string address, int timeoutMs)
    {
        PingResult result;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, timeoutMs).ConfigureAwait(false);
            result = reply.Status == IPStatus.Success
                ? new(Math.Max(reply.RoundtripTime, 0.5), IPStatus.Success, false) // under a millisecond reads as 0
                : new(double.NaN, reply.Status, false);
        }
        catch (PingException ex) when (ex.InnerException is SocketException) { result = new(double.NaN, IPStatus.Unknown, true); }
        catch (Exception) { result = new(double.NaN, IPStatus.Unknown, false); }
        lock (gate)
        {
            last[address] = (DateTime.UtcNow, result);
            waiting.Remove(address);
            if (last.Count > 200) last.Clear(); // (addresses no longer pinged)
        }
        return result;
    }
}
