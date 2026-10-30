using System.Text;
using System.Text.Json.Nodes;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>A character's formatting.</summary>
[Flags]
internal enum Fmt : byte { None = 0, Bold = 1, Italic = 2, Underline = 4 }

/// <summary>One character of the note: its formatting, and the PC that typed it.</summary>
internal readonly record struct Ch(char C, Fmt F, string A);

/// <summary>A piece of a change: keep <see cref="Keep"/> characters, remove <see cref="Remove"/>, or put <see cref="Put"/> in.</summary>
internal sealed record Part(int Keep, int Remove, IReadOnlyList<Ch>? Put)
{
    public static Part Retain(int n) => new(n, 0, null);
    public static Part Delete(int n) => new(0, n, null);
    public static Part Insert(IReadOnlyList<Ch> chars) => new(0, 0, chars);
}

/// <summary>
/// A change to the note, from start to end: what's kept, removed and put in (as in shared editors' "operational
/// transformation"). Only what changed is in it, never text that stayed the same, so two changes made at once can
/// both be applied without anything coming twice.
/// </summary>
internal sealed class Change
{
    public List<Part> Parts { get; } = new();
    /// <summary>The length of the note it's made on.</summary>
    public int From => Parts.Sum(p => p.Keep + p.Remove);

    public Change Retain(int n)
    {
        if (n <= 0) return this;
        if (Parts.Count > 0 && Parts[^1].Keep > 0) Parts[^1] = Part.Retain(Parts[^1].Keep + n); else Parts.Add(Part.Retain(n));
        return this;
    }

    public Change Delete(int n)
    {
        if (n <= 0) return this;
        if (Parts.Count > 0 && Parts[^1].Remove > 0) Parts[^1] = Part.Delete(Parts[^1].Remove + n); else Parts.Add(Part.Delete(n));
        return this;
    }

    public Change Insert(IReadOnlyList<Ch> chars)
    {
        if (chars.Count == 0) return this;
        if (Parts.Count > 0 && Parts[^1].Put is { } put) Parts[^1] = Part.Insert(put.Concat(chars).ToList());
        // (an insert goes before a delete at the same place: the same change, in one order)
        else if (Parts.Count > 0 && Parts[^1].Remove > 0)
        {
            var del = Parts[^1];
            Parts.RemoveAt(Parts.Count - 1);
            Insert(chars);
            Parts.Add(del);
        }
        else Parts.Add(Part.Insert(chars.ToList()));
        return this;
    }
}

/// <summary>
/// The shared note as characters, and its changes: what changed between two versions (a diff, Myers'), a change
/// applied, and a change made on one version moved onto another made at the same time, so text typed on two PCs at
/// once all stays, once.
/// </summary>
internal static class NoteText
{
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

    /// <summary>Above this many characters changed, the diff is one remove and one insert (it's quick, and still right).</summary>
    private const int MaxEdits = 500;

    /// <summary>What turns <paramref name="a"/> into <paramref name="b"/>; null if they're the same.</summary>
    public static Change? Diff(IReadOnlyList<Ch> a, IReadOnlyList<Ch> b, bool authors = true)
    {
        bool Same(Ch x, Ch y) => x.C == y.C && x.F == y.F && (!authors || x.A == y.A);
        int start = 0, n = Math.Min(a.Count, b.Count);
        while (start < n && Same(a[start], b[start])) start++;
        int end = 0;
        while (end < n - start && Same(a[a.Count - 1 - end], b[b.Count - 1 - end])) end++;
        int la = a.Count - start - end, lb = b.Count - start - end;
        if (la == 0 && lb == 0) return null;
        var c = new Change().Retain(start);
        Middle(a, start, la, b, start, lb, Same, c);
        return c.Retain(end);
    }

    /// <summary>The middle that differs, as the fewest removes and inserts (Myers' O(ND) diff).</summary>
    private static void Middle(IReadOnlyList<Ch> a, int a0, int n, IReadOnlyList<Ch> b, int b0, int m, Func<Ch, Ch, bool> same, Change c)
    {
        if (n == 0 || m == 0)
        {
            c.Delete(n).Insert(Slice(b, b0, m));
            return;
        }
        int max = Math.Min(n + m, MaxEdits), offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= max && found < 0; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && same(a[a0 + x], b[b0 + y])) { x++; y++; }
                v[offset + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
        }
        if (found < 0)
        {
            c.Delete(n).Insert(Slice(b, b0, m));
            return;
        }
        // back from the end: each step a remove or an insert, after a run kept the same
        var steps = new List<(char Kind, int A, int B)>();
        int cx = n, cy = m;
        for (int d = found; d > 0; d--)
        {
            var pv = trace[d];
            int k = cx - cy;
            int pk = k == -d || (k != d && pv[offset + k - 1] < pv[offset + k + 1]) ? k + 1 : k - 1;
            int px = pv[offset + pk], py = px - pk;
            while (cx > px && cy > py) { steps.Add(('=', cx - 1, cy - 1)); cx--; cy--; }
            if (cx == px) steps.Add(('+', cx, cy - 1)); else steps.Add(('-', cx - 1, cy));
            cx = px;
            cy = py;
        }
        while (cx > 0 && cy > 0) { steps.Add(('=', cx - 1, cy - 1)); cx--; cy--; }
        steps.Reverse();
        foreach (var (kind, _, y) in steps)
            switch (kind)
            {
                case '=': c.Retain(1); break;
                case '-': c.Delete(1); break;
                default: c.Insert(new[] { b[b0 + y] }); break;
            }
    }

    private static List<Ch> Slice(IReadOnlyList<Ch> list, int start, int count)
    {
        var r = new List<Ch>(count);
        for (int i = 0; i < count; i++) r.Add(list[start + i]);
        return r;
    }

    /// <summary>The change applied (it must be made on a note of this length).</summary>
    public static List<Ch> Apply(IReadOnlyList<Ch> doc, Change c)
    {
        var r = new List<Ch>(doc.Count);
        int i = 0;
        foreach (var p in c.Parts)
        {
            if (p.Keep > 0) { for (int k = 0; k < p.Keep && i < doc.Count; k++) r.Add(doc[i++]); }
            else if (p.Remove > 0) i = Math.Min(doc.Count, i + p.Remove);
            else if (p.Put != null) r.AddRange(p.Put);
        }
        while (i < doc.Count) r.Add(doc[i++]);
        return r;
    }

    /// <summary>
    /// <paramref name="a"/> made on the version <paramref name="b"/> was made on, moved to apply after it: what both
    /// removed goes once, what either put in stays (where both put text at one place, a's comes first).
    /// </summary>
    public static Change Transform(Change a, Change b)
    {
        var r = new Change();
        var pa = new Queue<Part>(a.Parts);
        var pb = new Queue<Part>(b.Parts);
        Part? x = Next(pa), y = Next(pb);
        while (x != null || y != null)
        {
            if (x?.Put is { } put) { r.Insert(put); x = Next(pa); continue; }
            if (y?.Put is { } other) { r.Retain(other.Count); y = Next(pb); continue; }
            if (x == null || y == null) break;
            int n = Math.Min(x.Keep + x.Remove, y.Keep + y.Remove);
            if (x.Keep > 0 && y.Keep > 0) r.Retain(n);
            else if (x.Remove > 0 && y.Keep > 0) r.Delete(n);
            // (removed by b: nothing to do; kept by a, removed by b: gone already)
            x = Rest(x, n) ?? Next(pa);
            y = Rest(y, n) ?? Next(pb);
        }
        return r;
    }

    private static Part? Next(Queue<Part> q) => q.Count > 0 ? q.Dequeue() : null;

    private static Part? Rest(Part p, int n) =>
        p.Keep > n ? Part.Retain(p.Keep - n) : p.Remove > n ? Part.Delete(p.Remove - n) : null;

    /// <summary>Where a place in the text (the caret) goes when the change is applied.</summary>
    public static int Map(int at, Change c)
    {
        int i = 0, r = at;
        foreach (var p in c.Parts)
        {
            if (i > at) break;
            if (p.Keep > 0) i += p.Keep;
            else if (p.Remove > 0) { r -= Math.Min(p.Remove, at - i); i += p.Remove; }
            else if (p.Put != null && i < at) r += p.Put.Count;
        }
        return Math.Max(0, r);
    }

    /// <summary>Runs of the same formatting and PC: [{t, f, a}].</summary>
    public static JsonArray ToJson(IReadOnlyList<Ch> doc)
    {
        var runs = new JsonArray();
        int i = 0;
        while (i < doc.Count)
        {
            int j = i;
            var sb = new StringBuilder();
            while (j < doc.Count && doc[j].F == doc[i].F && doc[j].A == doc[i].A) sb.Append(doc[j++].C);
            var run = new JsonObject { ["t"] = sb.ToString(), ["a"] = doc[i].A };
            if (doc[i].F != Fmt.None) run["f"] = (int)doc[i].F;
            runs.Add(run);
            i = j;
        }
        return runs;
    }

    public static List<Ch> FromJson(JsonNode? runs)
    {
        var doc = new List<Ch>();
        if (runs is not JsonArray a) return doc;
        foreach (var run in a.OfType<JsonObject>())
        {
            string t = Str(run, "t").Replace("\r", ""), by = Str(run, "a");
            var f = (Fmt)(run["f"] is JsonValue v && v.TryGetValue<int>(out int n) ? n & 7 : 0);
            foreach (char c in t) doc.Add(new Ch(c, f, by));
        }
        return doc;
    }

    /// <summary>A change as [{k: n}, {d: n}, {i: runs}].</summary>
    public static JsonArray ToJson(Change c)
    {
        var parts = new JsonArray();
        foreach (var p in c.Parts)
            parts.Add(p.Keep > 0 ? new JsonObject { ["k"] = p.Keep } : p.Remove > 0 ? new JsonObject { ["d"] = p.Remove } : new JsonObject { ["i"] = ToJson(p.Put!) });
        return parts;
    }

    public static Change? ChangeOf(JsonNode? parts)
    {
        if (parts is not JsonArray a) return null;
        var c = new Change();
        foreach (var p in a.OfType<JsonObject>())
        {
            if (Int(p, "k") is int k and > 0) c.Retain(k);
            else if (Int(p, "d") is int d and > 0) c.Delete(d);
            else if (p["i"] is JsonArray i) c.Insert(FromJson(i));
            else return null;
        }
        return c;
    }

    public static string Str(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    public static int? Int(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
}
