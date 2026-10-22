using System.Text.Json.Nodes;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// The shared notes (the tabs), on the PC hosting them: every PC's notepad (this one's too) joins ({op: "join", cid}),
/// gets the notes, and sends its changes, each made on a version of one note and numbered by that notepad; one made on
/// that note's latest is applied and sent to every PC (to the one that made it too, as its answer), one made on an
/// older version is turned back ("stale") for that PC to redo on the changes it has got since. The last number applied
/// from each notepad is kept, and told to it when it joins again, so a change whose answer was lost with the
/// connection isn't put in twice. Any PC can add a note ("new"), close one ("close") or rename one ("rename");
/// there's always one, and each has a name ("Notes 1", "Notes 2"… unless named). Callable from any thread.
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

    /// <summary>One note (a tab): its id, its name, the text and its version.</summary>
    public sealed class Note
    {
        public string Id = "", Name = "";
        public List<Ch> Doc = new();
        public int V;
        public JsonObject ToJson() => new() { ["id"] = Id, ["name"] = Name, ["v"] = V, ["doc"] = NoteText.ToJson(Doc) };
    }

    private readonly object gate = new();
    private readonly List<Note> notes;
    private readonly List<Peer> peers = new();
    private readonly Dictionary<string, int> seqs;
    private bool closed;

    /// <summary>The notes changed (raised on the thread that changed them).</summary>
    public event Action? Changed;
    /// <summary>A PC joined or left.</summary>
    public event Action? PeersChanged;

    public NoteServer(IEnumerable<Note> notes, Dictionary<string, int> seqs)
    {
        this.notes = notes.Where(n => n.Id.Length > 0).GroupBy(n => n.Id).Select(g => g.First()).ToList();
        if (this.notes.Count == 0) this.notes.Add(new Note { Id = NotepadSettings.FirstNote });
        // (before 3.1.3 a note could have no name: its first line showed)
        foreach (var n in this.notes) if (n.Name.Length == 0) n.Name = FreeName(this.notes.Select(x => x.Name));
        this.seqs = new Dictionary<string, int>(seqs);
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>"Notes n", with the lowest n not taken.</summary>
    public static string FreeName(IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        int i = 1;
        while (used.Contains(F("Notes {0}", i))) i++;
        return F("Notes {0}", i);
    }

    /// <summary>A name as typed: one line, at most 60 characters.</summary>
    public static string CleanName(string name)
    {
        name = string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length > 60 ? name[..60] : name;
    }

    public List<Note> Snapshot() { lock (gate) return notes.Select(n => new Note { Id = n.Id, Name = n.Name, Doc = n.Doc.ToList(), V = n.V }).ToList(); }
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
            var all = new JsonArray();
            foreach (var n in notes) all.Add(n.ToJson());
            p.Send(new JsonObject { ["op"] = "state", ["you"] = p.Id, ["seq"] = seqs.GetValueOrDefault(cid, -1), ["notes"] = all });
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

    private void SendAll(JsonObject m)
    {
        foreach (var p in peers) p.Send((JsonObject)m.DeepClone());
    }

    /// <summary>What a notepad sends: its join, a change ({op: "edit", note, base, seq, parts}), a new note or a close.</summary>
    public void Handle(Peer from, JsonObject m)
    {
        switch (NoteText.Str(m, "op"))
        {
            case "join": Join(from, NoteText.Str(m, "cid")); break;
            case "edit": Edit(from, m); break;
            case "new": Add(from, m); break;
            case "close": Remove(from, NoteText.Str(m, "id")); break;
            case "rename": Rename(from, NoteText.Str(m, "id"), NoteText.Str(m, "name")); break;
        }
    }

    /// <summary>A note renamed ({op: "rename", id, name}), on every PC.</summary>
    private void Rename(Peer from, string id, string name)
    {
        name = CleanName(name);
        if (name.Length == 0) return;
        lock (gate)
        {
            if (closed || !peers.Contains(from) || notes.FirstOrDefault(n => n.Id == id) is not { } note || note.Name == name) return;
            note.Name = name;
            SendAll(new JsonObject { ["op"] = "rename", ["id"] = id, ["name"] = name });
        }
        Changed?.Invoke();
    }

    /// <summary>A change from one of them.</summary>
    public void Edit(Peer from, JsonObject m)
    {
        if (NoteText.ChangeOf(m["parts"]) is not { } c) return;
        lock (gate)
        {
            if (closed || !peers.Contains(from)) return;
            // (3.0.5's notepads have one note: the first)
            string id = NoteText.Str(m, "note");
            var note = id.Length == 0 ? notes.FirstOrDefault() : notes.FirstOrDefault(n => n.Id == id);
            // (closed meanwhile: its close has gone to that PC already, which drops the change)
            if (note == null) return;
            if (NoteText.Int(m, "base") != note.V || c.From != note.Doc.Count)
            {
                from.Send(new JsonObject { ["op"] = "stale" });
                return;
            }
            note.Doc = NoteText.Apply(note.Doc, c);
            note.V++;
            if (from.Cid.Length > 0 && NoteText.Int(m, "seq") is int seq) seqs[from.Cid] = seq;
            SendAll(new JsonObject { ["op"] = "edit", ["note"] = note.Id, ["v"] = note.V, ["id"] = from.Id, ["by"] = from.Name, ["parts"] = NoteText.ToJson(c) });
        }
        Changed?.Invoke();
    }

    /// <summary>A new note ({op: "new", id, name, doc}), the id made by the PC adding it (so it can use it at once).</summary>
    private void Add(Peer from, JsonObject m)
    {
        string id = NoteText.Str(m, "id");
        if (id.Length == 0) return;
        lock (gate)
        {
            if (closed || !peers.Contains(from) || notes.Any(n => n.Id == id)) return;
            var note = new Note { Id = id, Name = CleanName(NoteText.Str(m, "name")), Doc = NoteText.FromJson(m["doc"]) };
            if (note.Name.Length == 0) note.Name = FreeName(notes.Select(n => n.Name));
            notes.Add(note);
            var added = note.ToJson();
            added["op"] = "new";
            SendAll(added);
        }
        Changed?.Invoke();
    }

    /// <summary>A note closed; the last one is followed by a new empty one.</summary>
    private void Remove(Peer from, string id)
    {
        lock (gate)
        {
            if (closed || !peers.Contains(from) || notes.RemoveAll(n => n.Id == id) == 0) return;
            SendAll(new JsonObject { ["op"] = "close", ["id"] = id });
            if (notes.Count == 0)
            {
                var note = new Note { Id = NewId(), Name = FreeName(Array.Empty<string>()) };
                notes.Add(note);
                var added = note.ToJson();
                added["op"] = "new";
                SendAll(added);
            }
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
