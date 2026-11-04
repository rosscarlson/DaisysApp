using System.Buffers.Binary;
using System.IO;

namespace DaisysApp.Shared.Network;

/// <summary>
/// Messages over a stream as frames: a 4-byte little-endian length, then that many bytes. Button2Key Remote's
/// connection uses it (since Core 0.55); Core's Server/Wire frames the same way but keeps its own copy, as it's kept
/// identical to Daisy's Server's.
/// </summary>
public static class Frames
{
    /// <summary>Writes one frame.</summary>
    public static async Task WriteAsync(Stream s, ReadOnlyMemory<byte> body, CancellationToken ct = default)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, body.Length);
        await s.WriteAsync(head, ct);
        await s.WriteAsync(body, ct);
    }

    /// <summary>The next frame, or null when the other end has closed; an IOException for one over <paramref name="max"/> bytes.</summary>
    public static async Task<byte[]?> ReadAsync(Stream s, int max, CancellationToken ct = default)
    {
        var head = new byte[4];
        try { await s.ReadExactlyAsync(head, ct); }
        catch (EndOfStreamException) { return null; }
        int n = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (n < 0 || n > max) throw new IOException("a frame too big");
        var body = new byte[n];
        await s.ReadExactlyAsync(body, ct);
        return body;
    }
}
