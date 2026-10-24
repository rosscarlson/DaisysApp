using System.Text.Json.Nodes;

namespace DaisysApp.Shell;

/// <summary>One connection to another PC's Daisy's App, encrypted, carrying JSON messages (the AI module's RemoteWire).</summary>
public interface IRemoteChannel : IDisposable
{
    /// <summary>The other PC's name.</summary>
    string Peer { get; }
    /// <summary>Sends a message (queued: they go in order).</summary>
    void Post(JsonNode message);
    /// <summary>The next message, or null when the other end has closed.</summary>
    Task<JsonObject?> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Other modules' connections on the AI module's remote control, so every module shares its one port and passphrase.
/// The AI module fills in the transport (<see cref="Connect"/>, <see cref="Pcs"/>, <see cref="Serving"/>); a module
/// serves a channel by name (<see cref="Serve"/>), and a PC connecting asks for it with {op: "channel", name}: the
/// connection is then the module's until it closes.
/// </summary>
public static class RemoteChannels
{
    private static readonly Dictionary<string, Func<IRemoteChannel, CancellationToken, Task>> handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The other PCs set up in Settings → AI → Remote control (their names there); null without the AI module.</summary>
    public static Func<IReadOnlyList<string>>? Pcs { get; set; }

    /// <summary>Connects to one of <see cref="Pcs"/> and opens a channel there (throws with a message for the user).</summary>
    public static Func<string, string, CancellationToken, Task<IRemoteChannel>>? Connect { get; set; }

    /// <summary>Adds another PC to <see cref="Pcs"/> (the AI module's Add a PC window: name, address, port, passphrase),
    /// owned by the given window; its name, or null when cancelled.</summary>
    public static Func<System.Windows.Window?, string?>? AddPc { get; set; }

    /// <summary>Whether this PC's remote control is listening.</summary>
    public static Func<bool>? Serving { get; set; }

    /// <summary>Raised when <see cref="Pcs"/> or <see cref="Serving"/> changes (on the UI thread).</summary>
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>Handles a channel's connections from other PCs (on a background thread; it owns the channel).</summary>
    public static void Serve(string name, Func<IRemoteChannel, CancellationToken, Task> handler)
    {
        lock (handlers) handlers[name] = handler;
    }

    public static Func<IRemoteChannel, CancellationToken, Task>? Handler(string name)
    {
        lock (handlers) return handlers.GetValueOrDefault(name);
    }
}
