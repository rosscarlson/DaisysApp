using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Everything you've said (the search bar's 💬 button): this session's messages at the top, and every session's in
/// the same project (folder) below, oldest first. Ctrl+F finds words in both, with how many there are, next / previous,
/// Match case and Whole word.
/// </summary>
internal sealed class SaidWindow : Window
{
    private sealed record Said(DateTime? When, string Session, string Text);

    private sealed class Pane
    {
        public readonly TextBlock Head = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        public readonly FlowDocumentScrollViewer Viewer = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false };
        public List<Said> Messages = new();
        public readonly List<Run> Found = new();
        public string Title = "";
    }

    private readonly Pane session = new(), project = new();
    private readonly TextBox find = new() { MinWidth = 260, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
    private readonly CheckBox matchCase = new() { Content = T("Match case"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox wholeWord = new() { Content = T("Whole word"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock count = new() { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly DispatcherTimer wait = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<Run> all = new();
    private int at = -1;
    private Brush mark = Brushes.Transparent, current = Brushes.Transparent;

    /// <param name="sentForYou">What the tab sends by itself (the session notes' message, carrying on after a limit):
    /// left out, as you didn't type it.</param>
    public SaidWindow(SessionInfo tab, IReadOnlyList<SessionInfo> open, IReadOnlyList<string> sentForYou)
    {
        Title = F("Everything you've said — {0}", tab.Name.Length > 0 ? tab.Name : tab.Folder);
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1100, area.Width * 0.8);
        Height = area.Height * 0.85;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        mark = Palette.Brush(Palette.Of(SessionState.Waiting), 0x55);
        current = Palette.Brush(Palette.Of(SessionState.Waiting), 0xCC);

        // the find line
        var up = Small("\uE70E", T("Previous (Shift+Enter)"), () => Step(-1));
        var down = Small("\uE70D", T("Next (Enter, F3)"), () => Step(1));
        var label = new TextBlock { Text = T("Find"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var e in new UIElement[] { label, find, up, down, matchCase, wholeWord, count }) line.Children.Add(e);
        AutomationName(find, T("Find"));

        wait.Tick += (_, _) => { wait.Stop(); Search(); };
        find.TextChanged += (_, _) => { wait.Stop(); wait.Start(); };
        matchCase.Click += (_, _) => Search();
        wholeWord.Click += (_, _) => Search();
        find.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; if (wait.IsEnabled) { wait.Stop(); Search(); } else Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); }
            else if (e.Key == Key.Escape && find.Text.Length > 0) { e.Handled = true; find.Clear(); }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; find.Focus(); find.SelectAll(); }
            else if (e.Key == Key.F3) { e.Handled = true; Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); }
            else if (e.Key == Key.Escape && !find.IsKeyboardFocused) Close();
        };

        // this session on top, the project below, a splitter between
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 80 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 80 });
        Grid.SetRow(line, 0);
        grid.Children.Add(line);
        var splitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, ResizeDirection = GridResizeDirection.Rows };
        splitter.SetResourceReference(BackgroundProperty, "CardBorderBrush");
        Grid.SetRow(splitter, 2);
        grid.Children.Add(splitter);
        foreach (var (pane, row) in new[] { (session, 1), (project, 3) })
        {
            var dock = new DockPanel();
            DockPanel.SetDock(pane.Head, Dock.Top);
            dock.Children.Add(pane.Head);
            dock.Children.Add(pane.Viewer);
            var card = new Border { Child = dock, Margin = new Thickness(0) };
            card.SetResourceReference(StyleProperty, "Card");
            Grid.SetRow(card, row);
            grid.Children.Add(card);
        }
        Content = new Border { Padding = new Thickness(16), Child = grid };

        session.Head.Text = project.Head.Text = T("Reading…");
        Loaded += async (_, _) =>
        {
            find.Focus();
            var names = open.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            try
            {
                var (mine, theirs, sessions) = await Task.Run(() => Load(tab, names, sentForYou));
                session.Messages = mine;
                project.Messages = theirs;
                session.Title = T("This session");
                project.Title = F("The whole project ({0}, {1} sessions)", ShortFolder(tab.Folder), sessions);
            }
            catch (Exception ex)
            {
                session.Title = project.Title = F("Couldn't read the sessions: {0}", ex.Message);
            }
            Search();
            foreach (var p in new[] { session, project }) ScrollToEnd(p);
        };
    }

    // ---------------------------------------------------------------- reading

    /// <summary>What you said in this session, and in every session of its project (Claude Code's, then the agent's).</summary>
    private static (List<Said> Session, List<Said> Project, int Sessions) Load(SessionInfo tab, Dictionary<string, string> names, IReadOnlyList<string> sentForYou)
    {
        var files = new List<string>();
        string projectDir = tab.Provider.Length == 0 && ClaudeCli.TranscriptOf(tab) is { } t ? Path.GetDirectoryName(t)! : ClaudeCli.ProjectFolderFor(tab.Folder);
        if (Directory.Exists(projectDir)) files.AddRange(Directory.EnumerateFiles(projectDir, "*.jsonl"));
        if (Directory.Exists(ApiAgent.Folder)) files.AddRange(Directory.EnumerateFiles(ApiAgent.Folder, "*.json"));

        string folder = Norm(tab.Folder);
        var mine = new List<Said>();
        var everything = new List<Said>();
        var sessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            bool claude = file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
            foreach (var h in ChatSearch.Read(file))
            {
                if (!h.Mine || sentForYou.Any(x => x.Trim().Length > 0 && h.Text.Trim() == x.Trim())) continue;
                if (!claude && Norm(h.Folder) != folder) break; // another project's conversation
                string name = names.TryGetValue(h.SessionId, out var n) && n.Length > 0 ? n : h.Title;
                var said = new Said(h.When, name, h.Text.Trim());
                everything.Add(said);
                sessions.Add(h.SessionId);
                if (h.SessionId.Equals(tab.Id, StringComparison.OrdinalIgnoreCase)) mine.Add(said);
            }
        }
        // oldest first (a provider's conversation keeps no times: where it was read)
        static List<Said> Ordered(List<Said> l) => l.Select((s, i) => (s, i)).OrderBy(x => x.s.When ?? DateTime.MaxValue).ThenBy(x => x.i).Select(x => x.s).ToList();
        return (Ordered(mine), Ordered(everything), sessions.Count);
    }

    private static string Norm(string f) => f.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    private static string ShortFolder(string f) => Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : f;

    // ---------------------------------------------------------------- finding

    private void Search()
    {
        string text = find.Text;
        Regex? re = null;
        if (text.Length > 0)
        {
            string pattern = Regex.Escape(text);
            if (wholeWord.IsChecked == true) pattern = @"(?<!\w)" + pattern + @"(?!\w)";
            re = new Regex(pattern, matchCase.IsChecked == true ? RegexOptions.CultureInvariant : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        all.Clear();
        foreach (var p in new[] { session, project })
        {
            double offset = p.Viewer.Document != null ? ScrollerOf(p.Viewer)?.VerticalOffset ?? 0 : 0;
            p.Found.Clear();
            p.Viewer.Document = Build(p, re);
            int words = p.Messages.Sum(m => Words(m.Text));
            p.Head.Text = $"{p.Title}  ·  " + F("{0} messages, {1} words", p.Messages.Count.ToString("N0"), words.ToString("N0"))
                + (re != null ? "  ·  " + F("{0} found", p.Found.Count.ToString("N0")) : "");
            all.AddRange(p.Found);
            if (re == null) Dispatcher.BeginInvoke(() => ScrollerOf(p.Viewer)?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        at = -1;
        if (re == null) { count.Text = ""; return; }
        if (all.Count == 0) { count.Text = T("Nothing found"); return; }
        Step(1);
    }

    private FlowDocument Build(Pane p, Regex? re)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(4, 0, 12, 0), FontSize = 13.5, FontFamily = FontFamily };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        if (p.Messages.Count == 0)
        {
            var none = new Paragraph(new Run(T("Nothing said yet.")));
            none.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
            doc.Blocks.Add(none);
            return doc;
        }
        bool showSession = p == project;
        foreach (var m in p.Messages)
        {
            var head = new Paragraph { Margin = new Thickness(0, 10, 0, 2), FontSize = 12 };
            head.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
            head.Inlines.Add(new Run(m.When?.ToString("g") ?? ""));
            if (showSession) head.Inlines.Add(new Run((m.When != null ? "  ·  " : "") + m.Session));
            doc.Blocks.Add(head);
            var body = new Paragraph { Margin = new Thickness(0) };
            if (re == null) body.Inlines.Add(new Run(m.Text));
            else
            {
                int from = 0;
                foreach (Match x in re.Matches(m.Text))
                {
                    if (x.Index > from) body.Inlines.Add(new Run(m.Text[from..x.Index]));
                    var hit = new Run(x.Value) { Background = mark, FontWeight = FontWeights.SemiBold };
                    body.Inlines.Add(hit);
                    p.Found.Add(hit);
                    from = x.Index + x.Length;
                }
                if (from < m.Text.Length) body.Inlines.Add(new Run(m.Text[from..]));
            }
            doc.Blocks.Add(body);
        }
        return doc;
    }

    private void Step(int by)
    {
        if (all.Count == 0) return;
        if (at >= 0 && at < all.Count) all[at].Background = mark;
        at = ((at < 0 && by < 0 ? 0 : at + by) % all.Count + all.Count) % all.Count;
        var run = all[at];
        run.Background = current;
        // (once it's laid out: a third of the way down its pane)
        var pane = session.Found.Contains(run) ? session : project;
        Dispatcher.BeginInvoke(() =>
        {
            if (ScrollerOf(pane.Viewer) is not { } scroller) return;
            var rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) return;
            // (measured from what the pane shows now)
            scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + rect.Top - scroller.ViewportHeight / 3));
        }, DispatcherPriority.Loaded);
        int inSession = session.Found.Count;
        count.Text = F("{0} of {1}", at + 1, all.Count) + "  ·  " + F("{0} in this session, {1} in the project", inSession, project.Found.Count);
    }

    private static int Words(string text) => Regex.Matches(text, @"\S+").Count;

    private static void ScrollToEnd(Pane p) =>
        p.Viewer.Dispatcher.BeginInvoke(() => ScrollerOf(p.Viewer)?.ScrollToEnd(), DispatcherPriority.Loaded);

    private static ScrollViewer? ScrollerOf(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (ScrollerOf(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    private static Button Small(string glyph, string tip, Action click)
    {
        var t = new TextBlock { Text = glyph, FontSize = 12 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = t, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip, MinHeight = 0 };
        b.Click += (_, _) => click();
        return b;
    }

    private static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);
}
