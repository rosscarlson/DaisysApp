namespace DaisysApp.Applets.Tools.Renamer;

public enum NameMode { Keep, Remove, Fixed, Reverse }
public enum CaseMode { Same, Lower, Upper, Title, Sentence }
public enum CropMode { None, Before, After }
public enum MoveMode { None, CopyFirst, CopyLast, MoveFirst, MoveLast }
public enum MoveTarget { Start, End, Position }
public enum AddPlace { None, Prefix, Suffix }
public enum NumberMode { None, Prefix, Suffix, Both, Insert }
public enum NumberStyle { Decimal, Hex, LowerLetters, UpperLetters, UpperRoman, LowerRoman }
public enum DateKind { Modified, Created, Taken, Now }
public enum ExtensionMode { Same, Lower, Upper, Title, New, Extra, Remove }

/// <summary>
/// Everything the Renamer can do to a name, in the panels Bulk Rename Utility has (numbered as there) and applied in
/// that order: RegEx (1), Name (2), Replace (3), Case (4), Remove (5), Move/Copy (6), Add (7), Auto Date (8), Append
/// Folder Name (9), Numbering (10), Extension (11). Each panel's defaults change nothing. Saved with the settings.
/// </summary>
public sealed class RenameOptions
{
    // (1) RegEx
    public string RegexMatch { get; set; } = "";
    public string RegexReplace { get; set; } = "";
    public bool RegexIncludeExtension { get; set; }
    public bool RegexMatchCase { get; set; }

    // (2) Name
    public NameMode NameMode { get; set; }
    public string FixedName { get; set; } = "";

    // (3) Replace
    public string Replace { get; set; } = "";
    public string ReplaceWith { get; set; } = "";
    public bool ReplaceMatchCase { get; set; }

    // (4) Case
    public CaseMode Case { get; set; }
    /// <summary>Words kept exactly as written here (e.g. "USB; of; DVD"), whatever the case chosen.</summary>
    public string CaseExcept { get; set; } = "";

    // (5) Remove
    public int RemoveFirst { get; set; }
    public int RemoveLast { get; set; }
    /// <summary>Characters from this position to <see cref="RemoveTo"/> (1 is the first; 0 for none).</summary>
    public int RemoveFrom { get; set; }
    public int RemoveTo { get; set; }
    /// <summary>These characters, wherever they are.</summary>
    public string RemoveChars { get; set; } = "";
    /// <summary>These words (separated by spaces), wherever they are.</summary>
    public string RemoveWords { get; set; } = "";
    public CropMode Crop { get; set; }
    public string CropText { get; set; } = "";
    public bool RemoveDigits { get; set; }
    public bool RemoveHighChars { get; set; }
    public bool RemoveTrim { get; set; }
    public bool RemoveDoubleSpaces { get; set; }
    public bool RemoveAccents { get; set; }
    public bool RemoveSymbols { get; set; }
    public bool RemoveLeadingDots { get; set; }

    // (6) Move / Copy
    public MoveMode MoveMode { get; set; }
    public int MoveCount { get; set; } = 1;
    public MoveTarget MoveTo { get; set; }
    public int MovePosition { get; set; } = 1;
    public string MoveSeparator { get; set; } = "";

    // (7) Add
    public string Prefix { get; set; } = "";
    public string Insert { get; set; } = "";
    /// <summary>Where <see cref="Insert"/> goes: 1 is before the first character; a negative one counts from the end.</summary>
    public int InsertAt { get; set; } = 1;
    public string Suffix { get; set; } = "";
    /// <summary>A space before each capital that follows a small letter ("HolidayPhotos" → "Holiday Photos").</summary>
    public bool WordSpace { get; set; }

    // (8) Auto Date
    public AddPlace DatePlace { get; set; }
    public DateKind DateKind { get; set; }
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public string DateSeparator { get; set; } = " ";

    // (9) Append Folder Name
    public AddPlace FolderPlace { get; set; }
    public string FolderSeparator { get; set; } = " ";
    /// <summary>How many folders up: 1 is the folder the file is in.</summary>
    public int FolderLevels { get; set; } = 1;

    // (10) Numbering
    public NumberMode NumberMode { get; set; }
    public long NumberStart { get; set; } = 1;
    public long NumberStep { get; set; } = 1;
    /// <summary>At least this many digits, with zeros in front (0 or 1: as they come).</summary>
    public int NumberPad { get; set; }
    public string NumberSeparator { get; set; } = " ";
    public int NumberAt { get; set; } = 1;
    public NumberStyle NumberStyle { get; set; }
    /// <summary>Start again at <see cref="NumberStart"/> in each folder (with subfolders listed).</summary>
    public bool NumberPerFolder { get; set; }

    // (11) Extension
    public ExtensionMode ExtensionMode { get; set; }
    public string ExtensionText { get; set; } = "";

    public RenameOptions Clone() => (RenameOptions)MemberwiseClone();

    /// <summary>Which panels change something now (for their headers), by number.</summary>
    public IEnumerable<int> ActivePanels()
    {
        if (RegexMatch.Length > 0) yield return 1;
        if (NameMode != NameMode.Keep) yield return 2;
        if (Replace.Length > 0) yield return 3;
        if (Case != CaseMode.Same) yield return 4;
        if (RemoveFirst > 0 || RemoveLast > 0 || RemoveFrom > 0 || RemoveChars.Length > 0 || RemoveWords.Length > 0 || (Crop != CropMode.None && CropText.Length > 0)
            || RemoveDigits || RemoveHighChars || RemoveTrim || RemoveDoubleSpaces || RemoveAccents || RemoveSymbols || RemoveLeadingDots) yield return 5;
        if (MoveMode != MoveMode.None && MoveCount > 0) yield return 6;
        if (Prefix.Length > 0 || Insert.Length > 0 || Suffix.Length > 0 || WordSpace) yield return 7;
        if (DatePlace != AddPlace.None) yield return 8;
        if (FolderPlace != AddPlace.None) yield return 9;
        if (NumberMode != NumberMode.None) yield return 10;
        if (ExtensionMode != ExtensionMode.Same) yield return 11;
    }
}
