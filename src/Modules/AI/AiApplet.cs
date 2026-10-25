using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Claude Code: the Claude Code CLI (as installed and signed in on this PC, so your own Claude account) run for each
/// session tab, chatting and working on code as it does in a terminal, drawn with live gauges: what it's doing, its
/// speed, the context window, tokens and cost, your plan's usage, the steps of each turn and the files it changed.
/// See README.md.
/// </summary>
[Applet("AI", "AI", "", Order = 70,
    Description = "AI sessions in tabs: Claude Code, Gemini, OpenAI, local models (Ollama) and more, side by side, to chat and to write and edit code, with live gauges")]
public sealed class AiApplet : IApplet
{
    private readonly AiSettings settings = AiSettings.Load();
    private readonly AiView view;
    private readonly PlanUsage usage;
    private readonly AiSettingsView settingsView;
    private readonly RemoteServer remote;

    public AiApplet()
    {
        Look.Set(settings.Look);
        usage = new PlanUsage(settings, () => ClaudeCli.Find(settings.ClaudePath), Application.Current.Dispatcher);
        view = new AiView(settings, usage);
        remote = new RemoteServer(settings, view, Application.Current.Dispatcher);
        settingsView = new AiSettingsView(settings, remote);
    }

    public FrameworkElement View => view;
    /// <summary>Settings → AI: permission prompts, and how the conversation looks.</summary>
    public FrameworkElement? SettingsView => settingsView;
    /// <summary>The plan's usage is watched from the start (warnings come whether the tab is open or not).</summary>
    public void Start()
    {
        usage.Start();
        // remote control: this PC's sessions for another PC, if it's on
        remote.Apply();
        settings.RemoteChanged += remote.Apply;
        // (the same connection for other modules: Tools → Notepad)
        RemoteChannels.Pcs = () => settings.Remote.Pcs.Select(p => p.Label).ToList();
        RemoteChannels.Serving = () => remote.Listening;
        RemoteChannels.Connect = async (pc, name, ct) =>
        {
            var p = settings.Remote.Pcs.FirstOrDefault(x => x.Label.Equals(pc, StringComparison.OrdinalIgnoreCase))
                ?? throw new RemoteException(F("{0} isn't in Settings → AI → Remote control.", pc));
            var w = await RemoteWire.ConnectAsync(p.Address, p.Port, p.Passphrase, ct);
            w.Post(new System.Text.Json.Nodes.JsonObject { ["op"] = "channel", ["name"] = name });
            return w;
        };
        RemoteChannels.AddPc = owner =>
        {
            var pc = new RemotePc();
            if (new RemotePcWindow(pc, true) { Owner = owner }.ShowDialog() != true) return null;
            settings.Remote.Pcs.Add(pc);
            settings.Save();
            settings.NotifyRemoteChanged();
            Logging.Log.Here.Info($"Remote control: {pc.Label} added (from another module)");
            return pc.Label;
        };
        remote.StatusChanged += RemoteChannels.RaiseChanged;
        settings.RemoteChanged += RemoteChannels.RaiseChanged;
        RemoteChannels.RaiseChanged();
    }
    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu
    {
        get
        {
            var items = new List<AppletMenuItem>();
            foreach (var w in usage.Windows.Take(2))
                items.Add(new AppletMenuItem(F("{0}: {1:0}% used", w.Label, w.Used * 100)) { Enabled = false, Hint = w.Resets is { } r ? F("resets {0}", PlanUsage.Until(r)) : null });
            if (view.Working > 0) items.Add(new AppletMenuItem(P(view.Working, "{0} session working", "{0} sessions working")) { Enabled = false });
            return items.Count > 0 ? items : null;
        }
    }

    public void Dispose()
    {
        usage.Stop();
        remote.Dispose();
        RemoteChannels.Connect = null;
        RemoteChannels.AddPc = null;
        RemoteChannels.Serving = null;
        RemoteChannels.Pcs = null;
        TitleBadges.Set("ClaudeUsage", null);
        TitleBadges.Set("AiCoding", null);
        view.CloseAll();
        settings.Save();
    }
}
