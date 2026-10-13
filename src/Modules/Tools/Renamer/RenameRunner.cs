using System.IO;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Tools.Renamer;

/// <summary>One rename: where it was and where it went.</summary>
public sealed class RenamePair
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool IsDir { get; set; }
}

/// <summary>
/// Renames a batch. Each name first goes to a temporary one and then to its new one, so names can swap (a → b and
/// b → a) and case-only changes work; entries deeper down go first, so renaming a folder never loses the path of
/// something inside it. What worked is returned (for Undo); a failure is noted and the rest go on.
/// </summary>
internal static class RenameRunner
{
    public sealed record Result(List<RenamePair> Done, List<(string Path, string Error)> Failed);

    /// <param name="deepestFirst">True to rename: things in a folder before the folder. False to undo: the folders first.</param>
    public static Result Run(IReadOnlyList<RenamePair> pairs, bool deepestFirst, Action<int>? progress = null)
    {
        var done = new List<RenamePair>();
        var failed = new List<(string, string)>();
        int count = 0;
        static int Depth(string p) => p.Count(c => c == '\\');
        var groups = deepestFirst
            ? pairs.GroupBy(p => Depth(p.From)).OrderByDescending(g => g.Key)
            : pairs.GroupBy(p => Depth(p.From)).OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            // 1. out of the way, under a temporary name beside it
            var moved = new List<(RenamePair Pair, string Temp)>();
            foreach (var p in group)
            {
                string temp = Path.Combine(Path.GetDirectoryName(p.From)!, ".daisyrename-" + Guid.NewGuid().ToString("N")[..12]);
                try
                {
                    Move(p.From, temp, p.IsDir);
                    moved.Add((p, temp));
                }
                catch (Exception ex) { failed.Add((p.From, ex.Message)); Log.Here.Warn($"Rename {p.From}: {ex.Message}"); progress?.Invoke(++count); }
            }
            // 2. to the new names
            foreach (var (p, temp) in moved)
            {
                try
                {
                    if (!SamePath(p.From, p.To) && (File.Exists(p.To) || Directory.Exists(p.To))) throw new IOException(T("Something with the new name is already there"));
                    Move(temp, p.To, p.IsDir);
                    done.Add(p);
                }
                catch (Exception ex)
                {
                    failed.Add((p.From, ex.Message));
                    Log.Here.Warn($"Rename {p.From} → {p.To}: {ex.Message}");
                    try { Move(temp, p.From, p.IsDir); } catch (Exception back) { Log.Here.Error($"Couldn't put back {p.From} (it's {temp})", back); }
                }
                progress?.Invoke(++count);
            }
        }
        return new Result(done, failed);
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Move(string from, string to, bool dir)
    {
        if (dir) Directory.Move(from, to);
        else File.Move(from, to);
    }
}
