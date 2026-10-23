using DaisysApp.Settings;

namespace DaisysApp.Applets.Tools.Terminal;

/// <summary>Saved in %APPDATA%\DaisysApp\Terminal.json.</summary>
public sealed class TerminalSettings
{
    private const string FileName = "Terminal";

    /// <summary>What + opens (Settings → Tools → Terminal).</summary>
    public ShellKind Default { get; set; } = ShellKind.Cmd;

    /// <summary>The tabs, opened again (each a new shell) at the next start, and the one showing.</summary>
    public List<Session> Sessions { get; set; } = new();
    public string Selected { get; set; } = "";

    public sealed class Session
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ShellKind Kind { get; set; }
    }

    public static TerminalSettings Load() => JsonStore.Load<TerminalSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
