using System.Text.Json.Nodes;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>Saved in %APPDATA%\DaisysApp\Notepad.json.</summary>
public sealed class NotepadSettings
{
    private const string FileName = "Notepad";

    /// <summary>The PC hosting the note (its name in Settings → AI → Remote control); "" = this PC.</summary>
    public string Host { get; set; } = "";

    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 15;
    public bool Wrap { get; set; } = true;
    /// <summary>Text typed on other PCs shown in their colour.</summary>
    public bool Colours { get; set; } = true;

    /// <summary>The note, when this PC hosts it (runs, as NoteText.ToJson), and the last change it applied from each notepad.</summary>
    public JsonArray HostDoc { get; set; } = new();
    public Dictionary<string, int> HostSeqs { get; set; } = new();

    /// <summary>This notepad's id for the host, and the number of its last change.</summary>
    public string ClientId { get; set; } = "";
    public int Seq { get; set; }
    /// <summary>The change sent and not yet answered (null: none), and its number.</summary>
    public JsonArray? Pending { get; set; }
    public int PendingSeq { get; set; }

    /// <summary>The note as it is here, and as it last was on <see cref="KnownHost"/> (to send what was typed while it was away).</summary>
    public JsonArray Doc { get; set; } = new();
    public JsonArray Known { get; set; } = new();
    public string? KnownHost { get; set; }

    public static NotepadSettings Load() => JsonStore.Load<NotepadSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
