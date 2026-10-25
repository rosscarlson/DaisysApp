using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// My prompts (the search bar's button beside 💬): the session as a chat of only your prompts, each with the AI's
/// final reply to it (its last text before your next prompt), so the way the work went reads at a glance. The tab's
/// own messages are left out: the session notes' turn (and its reply), and carrying on after a limit (its reply counts
/// as the prompt's); Context sent mid-turn joins the prompt it was about.
/// </summary>
internal sealed class PromptsWindow : Window
{
    private sealed class Exchange
    {
        public readonly List<string> Prompts = new();
        public string Reply = "";
        public DateTime? When, ReplyWhen;
    }

    private const string ContextHead = "More context for what you're working on now:\n\n";
    private const string ContextTail = "\n\nTake it into account and carry on with the task.";

    private readonly StackPanel chat = new() { Margin = new Thickness(0, 0, 12, 0) };
    private readonly ScrollViewer scroller;
    private readonly TextBlock head = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

    /// <param name="notes">The session notes' messages (left out with their replies).</param>
    /// <param name="carryOn">Carrying on after a limit or a pause (left out; the reply stays the prompt's).</param>
    public PromptsWindow(SessionInfo tab, IReadOnlyList<string> notes, IReadOnlyList<string> carryOn)
    {
        Title = F("My prompts — {0}", tab.Name.Length > 0 ? tab.Name : tab.Folder);
        var area = SystemParameters.WorkArea;
        Width = Math.Min(900, area.Width * 0.6);
        Height = area.Height * 0.8;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

        scroller = new ScrollViewer { Content = chat, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.Background = Look.Brush(Look.A.ChatBackground) ?? Brushes.Transparent;
        head.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(scroller);
        Content = new Border { Padding = new Thickness(16), Child = dock };

        head.Text = T("Reading…");
        Loaded += async (_, _) =>
        {
            List<Exchange> all;
            try { all = await Task.Run(() => Load(tab, notes, carryOn)); }
            catch (Exception ex) { head.Text = F("Couldn't read the session: {0}", ex.Message); return; }
            head.Text = all.Count == 0 ? (tab.Remote is { Length: > 0 } ? T("This session's conversation is on the other PC.") : T("Nothing said yet."))
                : P(all.Count, "{0} prompt", "{0} prompts");
            foreach (var x in all) Show(x);
            _ = Dispatcher.BeginInvoke(() => scroller.ScrollToEnd(), DispatcherPriority.Loaded);
        };
    }

    // ---------------------------------------------------------------- reading

    private static List<Exchange> Load(SessionInfo tab, IReadOnlyList<string> notes, IReadOnlyList<string> carryOn)
    {
        var list = new List<Exchange>();
        if (tab.Remote is { Length: > 0 }) return list;
        static bool In(IReadOnlyList<string> l, string t) => l.Any(x => x.Trim().Length > 0 && x.Trim() == t);
        Exchange? now = null;
        bool inNotes = false;
        foreach (var file in ChatSearch.OneFile(tab))
            foreach (var h in ChatSearch.Read(file))
            {
                string text = h.Text.Trim();
                if (h.Mine)
                {
                    if (In(notes, text)) { inNotes = true; continue; }
                    if (In(carryOn, text) || text.StartsWith("[Request interrupted")) { inNotes = false; continue; }
                    inNotes = false;
                    if (text.StartsWith(ContextHead.Trim()) && now != null)
                    {
                        now.Prompts.Add(text[ContextHead.Trim().Length..].Replace(ContextTail.Trim(), "").Trim());
                        continue;
                    }
                    text = SessionView.WithoutReadFirst(text).Trim();
                    if (text.Length == 0) continue;
                    now = new Exchange { When = h.When };
                    now.Prompts.Add(text);
                    list.Add(now);
                }
                // (Claude's answer to a message of Claude Code's own, not yours)
                else if (!inNotes && now != null && text != "No response requested.")
                {
                    now.Reply = text;
                    now.ReplyWhen = h.When;
                }
            }
        return list;
    }

    // ---------------------------------------------------------------- showing

    private void Show(Exchange x)
    {
        if (x.When is { } when)
        {
            var time = new TextBlock
            {
                Text = when.Date == DateTime.Today ? when.ToString("t") : when.ToString("d") + " " + when.ToString("t"),
                FontSize = Math.Max(9, Look.Size * 0.72), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 4, -6),
            };
            time.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            chat.Children.Add(time);
        }
        foreach (var p in x.Prompts) chat.Children.Add(new UserBubble(p));
        if (x.Reply.Length == 0)
        {
            chat.Children.Add(new NoteLine(T("No reply")));
            return;
        }
        var reply = new AssistantText { When = x.ReplyWhen };
        reply.Finish(x.Reply);
        chat.Children.Add(reply);
    }
}
