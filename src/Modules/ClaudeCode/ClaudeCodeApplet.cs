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

    public ClaudeCodeApplet() => view = new ClaudeCodeView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;
    public void Start() { }
    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => view.Working > 0
        ? new[] { new AppletMenuItem(P(view.Working, "{0} session working", "{0} sessions working")) { Enabled = false } }
        : null;

    public void Dispose()
    {
        view.CloseAll();
        settings.Save();
    }
}
