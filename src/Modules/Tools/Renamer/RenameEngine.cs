using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace DaisysApp.Applets.Tools.Renamer;

/// <summary>What the preview found for one name.</summary>
public enum RenameStatus { NotPicked, Same, Ok, Conflict, Invalid }

/// <summary>One file or folder in the list: as it is, ticked or not, and its new name.</summary>
internal sealed class RenameItem : INotifyPropertyChanged
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    /// <summary>The folder it's in, from the listed folder ("" for the folder itself, "Trip\Day 1" for one under it).</summary>
    public required string Folder { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }

    private bool picked = true;
    /// <summary>Renamed (the tick in the list).</summary>
    public bool Picked { get => picked; set { if (picked == value) return; picked = value; Changed(nameof(Picked)); PickedChanged?.Invoke(); } }

    public event Action? PickedChanged;

    public string NewName { get; private set; } = "";
    public RenameStatus Status { get; private set; } = RenameStatus.Same;
    public string? Note { get; private set; }
    public Brush? NewBrush { get; private set; }

    public string Icon => IsDir ? "" : "";
    public string SizeText => IsDir ? "" : Sizes.Bytes(Size);
    public string ModifiedText => Modified.ToString("g");
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    /// <summary>The photo's "date taken" (read once, when Auto Date needs it); null when it has none.</summary>
    public DateTime? Taken;
    public bool TakenRead;

    public void Show(string newName, RenameStatus status, string? note, Brush? brush)
    {
        NewName = newName;
        Status = status;
        Note = note;
        NewBrush = brush;
        Changed(nameof(NewName)); Changed(nameof(Note)); Changed(nameof(NewBrush)); Changed(nameof(Status));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>Works out new names: the panels in Bulk Rename Utility's order, then whether each can be used.</summary>
internal static class RenameEngine
{
    /// <summary>One item's new name. <paramref name="number"/> is its place among the ticked ones (or in its folder).</summary>
    public static string NewName(RenameItem item, RenameOptions o, long number, Regex? regex)
    {
        string stem = item.IsDir ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        string ext = item.IsDir ? "" : Path.GetExtension(item.Name); // ".jpg"
        if (!item.IsDir && stem.Length == 0) { stem = ext; ext = ""; } // ".gitignore"

        // (1) RegEx
        if (regex != null)
        {
            if (o.RegexIncludeExtension && !item.IsDir)
            {
                string all = regex.Replace(stem + ext, o.RegexReplace);
                int dot = all.LastIndexOf('.');
                (stem, ext) = dot > 0 ? (all[..dot], all[dot..]) : (all, "");
            }
            else stem = regex.Replace(stem, o.RegexReplace);
        }

        // (2) Name
        stem = o.NameMode switch
        {
            NameMode.Remove => "",
            NameMode.Fixed => o.FixedName,
            NameMode.Reverse => new string(stem.Reverse().ToArray()),
            _ => stem,
        };

        // (3) Replace
        if (o.Replace.Length > 0)
            stem = stem.Replace(o.Replace, o.ReplaceWith, o.ReplaceMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        // (4) Case
        stem = ChangeCase(stem, o.Case, o.CaseExcept);

        // (5) Remove
        stem = Remove(stem, o);

        // (6) Move / Copy
        if (o.MoveMode != MoveMode.None && o.MoveCount > 0 && stem.Length > 0)
        {
            int n = Math.Min(o.MoveCount, stem.Length);
            bool first = o.MoveMode is MoveMode.CopyFirst or MoveMode.MoveFirst;
            string piece = first ? stem[..n] : stem[^n..];
            if (o.MoveMode is MoveMode.MoveFirst or MoveMode.MoveLast) stem = first ? stem[n..] : stem[..^n];
            stem = o.MoveTo switch
            {
                MoveTarget.Start => piece + o.MoveSeparator + stem,
                MoveTarget.End => stem + o.MoveSeparator + piece,
                _ => InsertAt(stem, o.MovePosition, o.MoveSeparator + piece + o.MoveSeparator),
            };
        }

        // (7) Add
        if (o.Insert.Length > 0) stem = InsertAt(stem, o.InsertAt, o.Insert);
        stem = o.Prefix + stem + o.Suffix;
        if (o.WordSpace) stem = Regex.Replace(stem, @"(?<=\p{Ll})(?=\p{Lu})", " ");

        // (8) Auto Date
        if (o.DatePlace != AddPlace.None)
        {
            DateTime when = o.DateKind switch
            {
                DateKind.Created => item.Created,
                DateKind.Taken => TakenOf(item) ?? item.Modified,
                DateKind.Now => DateTime.Now,
                _ => item.Modified,
            };
            string date;
            try { date = when.ToString(o.DateFormat.Length > 0 ? o.DateFormat : "yyyy-MM-dd", CultureInfo.CurrentCulture); }
            catch (FormatException) { date = when.ToString("yyyy-MM-dd"); }
            stem = o.DatePlace == AddPlace.Prefix ? date + o.DateSeparator + stem : stem + o.DateSeparator + date;
        }

        // (9) Append Folder Name
        if (o.FolderPlace != AddPlace.None)
        {
            var names = new List<string>();
            var dir = new DirectoryInfo(item.Directory);
            for (int i = 0; i < Math.Max(1, o.FolderLevels) && dir != null && dir.Parent != null; i++, dir = dir.Parent) names.Insert(0, dir.Name);
            string folder = string.Join(o.FolderSeparator, names);
            if (folder.Length > 0) stem = o.FolderPlace == AddPlace.Prefix ? folder + o.FolderSeparator + stem : stem + o.FolderSeparator + folder;
        }

        // (10) Numbering
        if (o.NumberMode != NumberMode.None)
        {
            string num = Number(o.NumberStart + number * o.NumberStep, o.NumberStyle, o.NumberPad);
            string sep = o.NumberSeparator;
            stem = o.NumberMode switch
            {
                NumberMode.Prefix => num + sep + stem,
                NumberMode.Suffix => stem + sep + num,
                NumberMode.Both => num + sep + stem + sep + num,
                _ => InsertAt(stem, o.NumberAt, num),
            };
        }

        // (11) Extension
        if (!item.IsDir)
        {
            string bare = ext.TrimStart('.');
            ext = o.ExtensionMode switch
            {
                ExtensionMode.Lower => ext.ToLowerInvariant(),
                ExtensionMode.Upper => ext.ToUpperInvariant(),
                ExtensionMode.Title => bare.Length > 0 ? "." + char.ToUpperInvariant(bare[0]) + bare[1..].ToLowerInvariant() : ext,
                ExtensionMode.New => o.ExtensionText.Trim().TrimStart('.') is { Length: > 0 } t ? "." + t : "",
                ExtensionMode.Extra => o.ExtensionText.Trim().TrimStart('.') is { Length: > 0 } x ? ext + "." + x : ext,
                ExtensionMode.Remove => "",
                _ => ext,
            };
        }
        return stem + ext;
    }

    /// <summary>The panel 1 pattern, or null when it's empty. Throws <see cref="ArgumentException"/> when it isn't a valid one.</summary>
    public static Regex? Pattern(RenameOptions o) =>
        o.RegexMatch.Length == 0 ? null
            : new Regex(o.RegexMatch, (o.RegexMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static string InsertAt(string s, int position, string text)
    {
        int at = position > 0 ? position - 1 : s.Length + position + 1; // 1: before the first; −1: after the last
        return s.Insert(Math.Clamp(at, 0, s.Length), text);
    }

    // ---------------------------------------------------------------- (4) case

    public static string ChangeCase(string s, CaseMode mode, string except)
    {
        var ti = CultureInfo.CurrentCulture.TextInfo;
        string r = mode switch
        {
            CaseMode.Lower => s.ToLower(CultureInfo.CurrentCulture),
            CaseMode.Upper => s.ToUpper(CultureInfo.CurrentCulture),
            CaseMode.Title => Title(s.ToLower(CultureInfo.CurrentCulture), ti),
            CaseMode.Sentence => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..].ToLower(CultureInfo.CurrentCulture),
            _ => s,
        };
        if (mode == CaseMode.Same) return r;
        foreach (var word in except.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            r = Regex.Replace(r, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])", word.Replace("$", "$$"), RegexOptions.IgnoreCase);
        return r;
    }

    // each word's first letter (after a space, _, -, ., a bracket…; not after an apostrophe: "it's")
    private static string Title(string s, TextInfo ti)
    {
        var sb = new StringBuilder(s);
        bool start = true;
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (char.IsLetter(c)) { if (start) sb[i] = ti.ToUpper(c); start = false; }
            else if (char.IsDigit(c)) start = false;
            else start = c is not ('\'' or '’');
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- (5) remove

    private static string Remove(string s, RenameOptions o)
    {
        if (o.RemoveFirst > 0) s = o.RemoveFirst >= s.Length ? "" : s[o.RemoveFirst..];
        if (o.RemoveLast > 0) s = o.RemoveLast >= s.Length ? "" : s[..^o.RemoveLast];
        if (o.RemoveFrom > 0 && o.RemoveFrom <= s.Length)
        {
            int to = Math.Clamp(o.RemoveTo <= 0 ? o.RemoveFrom : o.RemoveTo, o.RemoveFrom, s.Length);
            s = s.Remove(o.RemoveFrom - 1, to - o.RemoveFrom + 1);
        }
        if (o.RemoveChars.Length > 0) s = new string(s.Where(c => o.RemoveChars.IndexOf(c) < 0).ToArray());
        foreach (var w in o.RemoveWords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(w) + @"(?![\p{L}\p{N}])", "", RegexOptions.IgnoreCase);
        if (o.Crop != CropMode.None && o.CropText.Length > 0)
        {
            int at = s.IndexOf(o.CropText, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) s = o.Crop == CropMode.Before ? s[at..] : s[..(at + o.CropText.Length)];
        }
        if (o.RemoveDigits) s = new string(s.Where(c => !char.IsDigit(c)).ToArray());
        if (o.RemoveHighChars) s = new string(s.Where(c => c < 128).ToArray());
        if (o.RemoveAccents) s = StripAccents(s);
        if (o.RemoveSymbols) s = new string(s.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        if (o.RemoveDoubleSpaces) s = Regex.Replace(s, " {2,}", " ");
        if (o.RemoveTrim) s = s.Trim();
        if (o.RemoveLeadingDots) s = s.TrimStart('.');
        return s;
    }

    private static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ---------------------------------------------------------------- (10) numbers

    public static string Number(long n, NumberStyle style, int pad)
    {
        string s = style switch
        {
            NumberStyle.Hex => n.ToString("X"),
            NumberStyle.LowerLetters => Letters(n).ToLowerInvariant(),
            NumberStyle.UpperLetters => Letters(n),
            NumberStyle.UpperRoman => Roman(n),
            NumberStyle.LowerRoman => Roman(n).ToLowerInvariant(),
            _ => Math.Abs(n).ToString(CultureInfo.InvariantCulture),
        };
        if (style is NumberStyle.Decimal or NumberStyle.Hex && pad > 1) s = s.PadLeft(pad, '0');
        return style == NumberStyle.Decimal && n < 0 ? "-" + s : s;
    }

    // 1 a, 26 z, 27 aa (as spreadsheet columns)
    private static string Letters(long n)
    {
        if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        while (n > 0) { n--; sb.Insert(0, (char)('A' + n % 26)); n /= 26; }
        return sb.ToString();
    }

    private static string Roman(long n)
    {
        if (n <= 0 || n >= 4000) return n.ToString(CultureInfo.InvariantCulture);
        (int V, string S)[] map = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var sb = new StringBuilder();
        foreach (var (v, sym) in map) while (n >= v) { sb.Append(sym); n -= v; }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- (8) a photo's date taken

    private static DateTime? TakenOf(RenameItem item)
    {
        if (item.TakenRead) return item.Taken;
        item.TakenRead = true;
        try
        {
            string ext = Path.GetExtension(item.Path).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".tif" or ".tiff" or ".png" or ".heic" or ".jxr" or ".wdp")) return null;
            using var f = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(f, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
            if (frame.Metadata is System.Windows.Media.Imaging.BitmapMetadata m && m.DateTaken is { Length: > 0 } t && DateTime.TryParse(t, out var d)) item.Taken = d;
        }
        catch { }
        return item.Taken;
    }

    // ---------------------------------------------------------------- can it be used?

    private static readonly char[] Bad = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why Windows won't take this name, or null.</summary>
    public static string? Invalid(string name)
    {
        if (name.Length == 0) return T("The new name is empty");
        if (name.IndexOfAny(Bad) >= 0) return F("A name can't have any of these: {0}", "\\ / : * ? \" < > |");
        if (name.EndsWith(' ') || name.EndsWith('.')) return T("A name can't end with a space or a dot");
        if (name.Length > 255) return T("The new name is longer than 255 characters");
        if (Reserved.Contains(Path.GetFileNameWithoutExtension(name))) return F("{0} is a name Windows keeps for itself", Path.GetFileNameWithoutExtension(name));
        return null;
    }
}
