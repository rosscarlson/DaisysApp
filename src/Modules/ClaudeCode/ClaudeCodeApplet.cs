using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>
/// Claude Code: the Claude Code CLI (as installed and signed in on this PC, so your own Claude account) run for each
/// session tab, chatting and working on code as it does in a terminal, drawn with live gauges: what it's doing, its
/// speed, the context window, tokens and cost, your plan's usage, the steps of each turn and the files it changed.
/// See README.md.
/// </summary>
[Applet("ClaudeCode", "Claude Code", "", Order = 70,
    Description = "Claude Code sessions in tabs: chat, and let it write and edit code, with live gauges for what it's doing and the tokens it uses")]
public sealed class ClaudeCodeApplet : IApplet
{
    private readonly ClaudeCodeSettings settings = ClaudeCodeSettings.Load();
    private readonly ClaudeCodeView view;
    private readonly PlanUsage usage;

    public ClaudeCodeApplet()
    {
        usage = new PlanUsage(settings, () => ClaudeCli.Find(settings.ClaudePath), Application.Current.Dispatcher);
        view = new ClaudeCodeView(settings, usage);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    /// <summary>The plan's usage is watched from the start (warnings come whether the tab is open or not).</summary>
    public void Start() => usage.Start();
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
        TitleBadges.Set("ClaudeUsage", null);
        view.CloseAll();
        settings.Save();
    }
}
