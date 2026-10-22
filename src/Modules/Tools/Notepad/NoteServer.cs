using System.Text.Json.Nodes;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// The shared note, on the PC hosting it: every PC's notepad (this one's too) joins it ({op: "join", cid}), gets the
/// note, and sends its changes, each made on a version of the note and numbered by that notepad; one made on the
/// latest is applied and sent to every PC (to the one that made it too, as its answer), one made on an older version
/// is turned back ("stale") for that PC to redo on the changes it has got since. The last number applied from each
/// notepad is kept, and told to it when it joins again, so a change whose answer was lost with the connection isn't
/// put in twice. Callable from any thread.
/// </summary>
internal sealed class NoteServer
{
    /// <summary>A notepad joined to it: this PC's, or another PC's over remote control.</summary>
    public abstract class Peer
    {
        private static int ids;
        public int Id { get; } = Interlocked.Increment(ref ids);
        public abstract string Name { get; }
        /// <summary>Its notepad's id (from its join).</summary>
        public string Cid { get; set; } = "";
        public abstract void Send(JsonObject m);
        public virtual void Close() { }
    }

    private readonly object gate = new();
    private List<Ch> doc;
    private readonly List<Peer> peers = new();
    private readonly Dictionary<string, int> seqs;
    private int version;
    private bool closed;

    /// <summary>The note changed (raised on the thread that changed it).</summary>
    public event Action? Changed;
    /// <summary>A PC joined or left.</summary>
    public event Action? PeersChanged;

    public NoteServer(List<Ch> doc, Dictionary<string, int> seqs)
    {
        this.doc = doc;
        this.seqs = new Dictionary<string, int>(seqs);
    }

    public List<Ch> Snapshot() { lock (gate) return doc.ToList(); }
    public Dictionary<string, int> Seqs() { lock (gate) return new Dictionary<string, int>(seqs); }
    public IReadOnlyList<string> Who { get { lock (gate) return peers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); } }

    public void Join(Peer p, string cid)
    {
        lock (gate)
        {
            if (closed) { p.Close(); return; }
            if (peers.Contains(p)) return;
            p.Cid = cid;
            peers.Add(p);
            p.Send(new JsonObject { ["op"] = "state", ["v"] = version, ["you"] = p.Id, ["seq"] = seqs.GetValueOrDefault(cid, -1), ["doc"] = NoteText.ToJson(doc) });
            SendWho();
        }
        PeersChanged?.Invoke();
    }

    public void Leave(Peer p)
    {
        lock (gate)
        {
            if (!peers.Remove(p)) return;
            SendWho();
        }
        PeersChanged?.Invoke();
    }

    private void SendWho()
    {
        var who = new JsonArray();
        foreach (var n in peers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)) who.Add(n);
        foreach (var p in peers) p.Send(new JsonObject { ["op"] = "who", ["pcs"] = who.DeepClone() });
    }

    /// <summary>What a notepad sends: its join, or a change ({op: "edit", base, seq, parts}).</summary>
    public void Handle(Peer from, JsonObject m)
    {
        switch (NoteText.Str(m, "op"))
        {
            case "join": Join(from, NoteText.Str(m, "cid")); break;
            case "edit": Edit(from, m); break;
        }
    }

    /// <summary>A change from one of them.</summary>
    public void Edit(Peer from, JsonObject m)
    {
        if (NoteText.ChangeOf(m["parts"]) is not { } c) return;
        lock (gate)
        {
            if (closed || !peers.Contains(from)) return;
            if (NoteText.Int(m, "base") != version || c.From != doc.Count)
            {
                from.Send(new JsonObject { ["op"] = "stale" });
                return;
            }
            doc = NoteText.Apply(doc, c);
            version++;
            if (from.Cid.Length > 0 && NoteText.Int(m, "seq") is int seq) seqs[from.Cid] = seq;
            var edit = new JsonObject { ["op"] = "edit", ["v"] = version, ["id"] = from.Id, ["by"] = from.Name, ["parts"] = NoteText.ToJson(c) };
            foreach (var p in peers) p.Send((JsonObject)edit.DeepClone());
        }
        Changed?.Invoke();
    }

    /// <summary>No longer hosting: every PC is let go (theirs try again, and hear it isn't hosted here).</summary>
    public void Close()
    {
        List<Peer> all;
        lock (gate)
        {
            closed = true;
            all = peers.ToList();
            peers.Clear();
        }
        foreach (var p in all) p.Close();
    }

    /// <summary>Another PC's notepad, on its channel, until it goes.</summary>
    public async Task ServeAsync(IRemoteChannel channel, CancellationToken ct)
    {
        var peer = new ChannelPeer(channel);
        Logging.Log.Here.Info($"Notepad: {channel.Peer} joined");
        try
        {
            while (await channel.ReadAsync(ct) is { } m) Handle(peer, m);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally
        {
            Leave(peer);
            Logging.Log.Here.Info($"Notepad: {channel.Peer} left");
        }
    }

    private sealed class ChannelPeer(IRemoteChannel channel) : Peer
    {
        public override string Name => channel.Peer;
        public override void Send(JsonObject m) => channel.Post(m);
        public override void Close() => channel.Dispose();
    }
}
