namespace DaisysApp.Applets.SimEffects.Arduino;

/// <summary>8×8 pictures of what an 8×8 matrix shows: the gears (R, N, 1–9) and a dash. A row a byte, bit 7 on the left.</summary>
public static class Glyphs
{
    private static readonly Dictionary<string, string[]> pictures = new()
    {
        ["R"] = new[] { "..####..", "..#...#.", "..#...#.", "..####..", "..#.#...", "..#..#..", "..#...#.", "........" },
        ["N"] = new[] { ".#....#.", ".##...#.", ".#.#..#.", ".#..#.#.", ".#...##.", ".#....#.", ".#....#.", "........" },
        ["P"] = new[] { "..####..", "..#...#.", "..#...#.", "..####..", "..#.....", "..#.....", "..#.....", "........" },
        ["1"] = new[] { "...##...", "..###...", "...##...", "...##...", "...##...", "...##...", "..####..", "........" },
        ["2"] = new[] { "..####..", ".##..##.", ".....##.", "....##..", "...##...", "..##....", ".######.", "........" },
        ["3"] = new[] { "..####..", ".##..##.", ".....##.", "...###..", ".....##.", ".##..##.", "..####..", "........" },
        ["4"] = new[] { "....##..", "...###..", "..#.##..", ".#..##..", ".######.", "....##..", "....##..", "........" },
        ["5"] = new[] { ".######.", ".##.....", ".#####..", ".....##.", ".....##.", ".##..##.", "..####..", "........" },
        ["6"] = new[] { "..####..", ".##.....", ".#####..", ".##..##.", ".##..##.", ".##..##.", "..####..", "........" },
        ["7"] = new[] { ".######.", ".....##.", "....##..", "...##...", "...##...", "...##...", "...##...", "........" },
        ["8"] = new[] { "..####..", ".##..##.", ".##..##.", "..####..", ".##..##.", ".##..##.", "..####..", "........" },
        ["9"] = new[] { "..####..", ".##..##.", ".##..##.", "..#####.", ".....##.", "....##..", "..###...", "........" },
        ["-"] = new[] { "........", "........", "........", ".######.", "........", "........", "........", "........" },
        ["full"] = new[] { "########", "########", "########", "########", "########", "########", "########", "########" },
    };

    private static readonly Dictionary<string, byte[]> rows = pictures.ToDictionary(p => p.Key, p => p.Value.Select(Row).ToArray());

    private static byte Row(string s)
    {
        byte b = 0;
        for (int i = 0; i < 8; i++) if (s[i] == '#') b |= (byte)(0x80 >> i);
        return b;
    }

    /// <summary>The picture for a gear's text ("R", "N", "3"); a dash for anything else. Ten and up show their last digit.</summary>
    public static byte[] Of(string text)
    {
        if (rows.TryGetValue(text, out var r)) return r;
        if (text.Length > 1 && rows.TryGetValue(text[^1..], out r)) return r;
        return rows["-"];
    }

    /// <summary>The text for a gear (-1 reverse, 0 neutral).</summary>
    public static string Gear(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();
}
