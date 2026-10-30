using System.Text.Json.Nodes;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>Saved in %APPDATA%\DaisysApp\Notepad.json.</summary>
public sealed class NotepadSettings
{
    private const string FileName = "Notepad";
    /// <summary>The id of the one note before 3.0.6's tabs (it carries on as the first tab).</summary>
    public const string FirstNote = "main";

    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 15;
    public bool Wrap { get; set; } = true;

    /// <summary>In its own window (opened again so at the next start), and where that window was.</summary>
    public bool PoppedOut { get; set; }
    public double[]? PopBounds { get; set; }

    /// <summary>This notepad's id for the server, and the number of its last change.</summary>
    public string ClientId { get; set; } = "";
    public int Seq { get; set; }
    /// <summary>The change sent and not yet answered (null: none), its note and its number.</summary>
    public JsonArray? Pending { get; set; }
    public string PendingNote { get; set; } = "";
    public int PendingSeq { get; set; }

    /// <summary>The notes (tabs) as they are here, the one showing, and the server and user they were last in step with.</summary>
    public List<SheetState> Sheets { get; set; } = new();
    public string Selected { get; set; } = "";
    public string? KnownHost { get; set; }

    /// <summary>Default app (Settings → Tools → Notepad): the file types picked, and each one's app before (to go back to).</summary>
    public List<string> DefaultTypes { get; set; } = new();
    public Dictionary<string, string> TypesBefore { get; set; } = new();

    /// <summary>Before 3.0.6: the one note (here, and as last known on the host). Read once into the first tab.</summary>
    public JsonArray? Doc { get; set; }
    public JsonArray? Known { get; set; }

    /// <summary>A tab: the note here, as the server last had it (null: added here, not on the server yet) and its version
    /// there, and the file it was opened from on this PC (with how to save it back).</summary>
    public sealed class SheetState
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public JsonArray Doc { get; set; } = new();
        public JsonArray? Known { get; set; }
        public int Version { get; set; }
        public string? Path { get; set; }
        public int CodePage { get; set; } = 65001;
        public bool Bom { get; set; }
        public bool Crlf { get; set; } = true;
        /// <summary>The text as opened or last saved (a hash), to show it's changed.</summary>
        public string Saved { get; set; } = "";
    }

    public static NotepadSettings Load()
    {
        var s = JsonStore.Load<NotepadSettings>(FileName);
        if (s.Doc != null)
        {
            if (s.Sheets.Count == 0 && (s.Doc.Count > 0 || s.KnownHost != null))
                s.Sheets.Add(new SheetState { Id = FirstNote, Doc = s.Doc, Known = s.KnownHost != null ? s.Known ?? new JsonArray() : null });
            // (the change on its way was to the one note)
            if (s.Pending != null && s.PendingNote.Length == 0) s.PendingNote = FirstNote;
            s.Doc = s.Known = null;
        }
        return s;
    }

    public void Save() => JsonStore.Save(FileName, this);
}
