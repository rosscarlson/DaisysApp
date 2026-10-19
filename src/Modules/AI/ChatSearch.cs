using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Searching the conversations (Ctrl+F, or 🔍 beside the session tabs): what you said, the replies, or both, in this
/// session or in every one on this PC (Claude Code's transcripts, and the agent's conversations). It reads the whole
/// conversation, not just what the tab shows. Click a result to go to it: in its tab (opened if it isn't), scrolled
/// to and lit up; one older than what the tab shows opens out here.
/// </summary>
internal sealed class ChatSearch : UserControl
{
    /// <summary>A message found: its session, who said it, when, and its words. SameAfter: how many later messages of
    /// the session say exactly the same (to find the right one in the tab).</summary>
    public sealed record Hit(string SessionId, string Title, string Folder, string? Provider, bool Mine, DateTime? When, string Text, int SameAfter);

    /// <summary>The tab showing (for "This session").</summary>
    private readonly Func<SessionInfo?> current;
    private readonly AiSettings settings;
    private readonly TextBox query = new() { MinWidth = 260, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
    private readonly ComboBox who = new() { Margin = new Thickness(8, 0, 0, 0), MinWidth = 150 };
    private readonly ComboBox scope = new() { Margin = new Thickness(8, 0, 0, 0), MinWidth = 140 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 13 };
    private readonly StackPanel results = new();
    private readonly ScrollViewer resultsScroll;
    private readonly DispatcherTimer wait = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? running;

    /// <summary>Go to this message (AiView: its tab, opened if need be). False: the tab doesn't show it.</summary>
    public event Func<Hit, bool>? Go;

    public ChatSearch(AiSettings settings, Func<SessionInfo?> current)
    {
        this.settings = settings;
        this.current = current;
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 0, 0, 10);

        foreach (var (tag, name) in new[] { ("all", T("Everything")), ("mine", T("What I said")), ("replies", T("The replies")) })
            who.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
        who.SelectedIndex = 0;
        foreach (var (tag, name) in new[] { ("this", T("This session")), ("every", T("All sessions")) })
            scope.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
        scope.SelectedIndex = 0;
        query.ToolTip = T("Words to look for in the conversation (any case). Enter searches at once, Esc closes.");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var close = new Button { Content = "✕", FontSize = 10, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Close the search (Esc)"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => Hide();

        var row = new DockPanel();
        var label = new TextBlock { Text = T("Search"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(status, Dock.Right);
        DockPanel.SetDock(scope, Dock.Right);
        DockPanel.SetDock(who, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(close);
        row.Children.Add(status);
        row.Children.Add(scope);
        row.Children.Add(who);
        row.Children.Add(query);

        resultsScroll = new ScrollViewer { Content = results, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(resultsScroll);
        var card = new Border { Child = stack };
        card.SetResourceReference(StyleProperty, "Card");
        Content = card;

        wait.Tick += (_, _) => { wait.Stop(); _ = SearchAsync(); };
        query.TextChanged += (_, _) => { wait.Stop(); wait.Start(); };
        who.SelectionChanged += (_, _) => _ = SearchAsync();
        scope.SelectionChanged += (_, _) => _ = SearchAsync();
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; wait.Stop(); _ = SearchAsync(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Hide(); }
        };
    }

    public bool Showing => Visibility == Visibility.Visible;

    /// <summary>Opens it (or, open, closes it), the box ready to type in.</summary>
    public void Toggle()
    {
        if (Showing && query.IsKeyboardFocusWithin) { Hide(); return; }
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => { query.Focus(); query.SelectAll(); }, DispatcherPriority.Input);
        if (query.Text.Trim().Length > 0) _ = SearchAsync();
    }

    public void Hide()
    {
        running?.Cancel();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Another tab is showing: "This session" means that one now.</summary>
    public void SessionChanged()
    {
        if (Showing && Chosen(scope) == "this") _ = SearchAsync();
    }

    private static string Chosen(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    // ---------------------------------------------------------------- searching

    private const int MaxShown = 200;

    private async Task SearchAsync()
    {
        running?.Cancel();
        string text = query.Text.Trim();
        results.Children.Clear();
        resultsScroll.Visibility = Visibility.Collapsed;
        if (text.Length < 2) { status.Text = text.Length == 0 ? "" : T("Type a little more…"); return; }
        var cts = running = new CancellationTokenSource();
        string whose = Chosen(who);
        bool every = Chosen(scope) == "every";
        var tab = current();
        if (!every && tab is not { Started: true }) { status.Text = T("Nothing's been said in this session yet."); return; }
        var open = settings.Sessions.ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);
        var providers = settings.Providers.ToList();
        status.Text = T("Searching…");
        List<Hit> found;
        int total;
        try
        {
            (found, total) = await Task.Run(() =>
            {
                var hits = new List<(Hit Hit, int Order)>();
                foreach (var file in every ? AllFiles() : OneFile(tab!))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    foreach (var h in Read(file))
                        if ((whose == "all" || (whose == "mine") == h.Mine) && h.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                            hits.Add((h, hits.Count));
                }
                // newest first (a provider's conversation keeps no times: its last first)
                return (hits.OrderByDescending(h => h.Hit.When ?? DateTime.MinValue).ThenByDescending(h => h.Order).Select(h => h.Hit).ToList(), hits.Count);
            }, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            status.Text = F("The search didn't work: {0}", ex.Message);
            return;
        }
        if (cts.IsCancellationRequested) return;
        status.Text = total == 0 ? T("Nothing found") : total > MaxShown ? F("{0} found (the newest {1} shown)", total, MaxShown) : F("{0} found", total);
        foreach (var h in found.Take(MaxShown))
        {
            // a tab's own name, if it's open; the provider by name
            var shown = open.TryGetValue(h.SessionId, out var name) && name.Length > 0 ? h with { Title = name } : h;
            if (shown.Provider != null) shown = shown with { Provider = providers.FirstOrDefault(p => p.Name == shown.Provider)?.Id ?? shown.Provider };
            results.Children.Add(Row(shown, text, every, providers));
        }
        resultsScroll.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        resultsScroll.ScrollToTop();
    }

    private FrameworkElement Row(Hit h, string text, bool every, List<Provider> providers)
    {
        string speaker = h.Mine ? T("You") : h.Provider == null ? "Claude" : providers.FirstOrDefault(p => p.Id == h.Provider)?.Name ?? h.Provider;
        var head = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        head.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        head.Inlines.Add(new Run(speaker) { FontWeight = FontWeights.SemiBold, Foreground = h.Mine ? (Brush)FindResource("AccentBrush") : Palette.Brush(Palette.Of(SessionState.Writing)) });
        if (h.When is { } when) head.Inlines.Add(new Run("  ·  " + when.ToString("g")));
        if (every) head.Inlines.Add(new Run("  ·  " + h.Title));
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 2, 0, 0) };
        Highlight(body, Snippet(h.Text, text), text);
        var full = new TextBox { Text = h.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13.5 };
        var older = new TextBlock { Text = T("Older than what its tab shows: here it is in full."), FontSize = 12.5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        older.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var s = new StackPanel();
        s.Children.Add(head);
        s.Children.Add(body);
        s.Children.Add(older);
        s.Children.Add(full);
        var row = new Border { Child = s, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = T("Go to it in its tab") };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "RowHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (full.IsMouseOver) return;
            bool shown = Go?.Invoke(h) ?? false;
            older.Visibility = full.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        };
        return row;
    }

    /// <summary>The words around the first match, on one line.</summary>
    private static string Snippet(string text, string match)
    {
        string flat = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        int at = flat.IndexOf(match, StringComparison.OrdinalIgnoreCase);
        int start = Math.Max(0, at - 70);
        int end = Math.Min(flat.Length, Math.Max(at, 0) + match.Length + 180);
        return (start > 0 ? "…" : "") + flat[start..end] + (end < flat.Length ? "…" : "");
    }

    /// <summary>The text with each match marked.</summary>
    private void Highlight(TextBlock block, string text, string match)
    {
        int at = 0;
        while (true)
        {
            int i = text.IndexOf(match, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0) { block.Inlines.Add(new Run(text[at..])); break; }
            if (i > at) block.Inlines.Add(new Run(text[at..i]));
            block.Inlines.Add(new Run(text.Substring(i, match.Length)) { FontWeight = FontWeights.SemiBold, Background = Palette.Brush(Palette.Of(SessionState.Waiting), 0x55) });
            at = i + match.Length;
        }
    }

    // ---------------------------------------------------------------- the conversations

    private static IEnumerable<string> OneFile(SessionInfo s)
    {
        string? path = s.Provider.Length == 0 ? ClaudeCli.TranscriptOf(s) : Path.Combine(ApiAgent.Folder, s.Id + ".json");
        if (path != null && File.Exists(path)) yield return path;
    }

    /// <summary>Every conversation on this PC: Claude Code's sessions (each folder's, not its agents'), and the agent's.</summary>
    private static IEnumerable<string> AllFiles()
    {
        var files = new List<FileInfo>();
        try
        {
            if (Directory.Exists(ClaudeCli.ProjectsFolder))
                foreach (var dir in Directory.EnumerateDirectories(ClaudeCli.ProjectsFolder))
                    files.AddRange(new DirectoryInfo(dir).EnumerateFiles("*.jsonl"));
            if (Directory.Exists(ApiAgent.Folder)) files.AddRange(new DirectoryInfo(ApiAgent.Folder).EnumerateFiles("*.json"));
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Searching the conversations: " + ex.Message); }
        return files.OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName);
    }

    // each file's messages, read once (again when it changes)
    private static readonly Dictionary<string, (DateTime Written, long Length, List<Hit> Hits)> cache = new(StringComparer.OrdinalIgnoreCase);

    private static List<Hit> Read(string path)
    {
        var info = new FileInfo(path);
        lock (cache)
            if (cache.TryGetValue(path, out var c) && c.Written == info.LastWriteTimeUtc && c.Length == info.Length) return c.Hits;
        List<Hit> hits;
        try { hits = path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? ReadClaude(path) : ReadAgent(path, info.LastWriteTime); }
        catch (Exception ex) { Logging.Log.Here.Warn($"Searching {path}: {ex.Message}"); hits = new(); }
        lock (cache) cache[path] = (info.LastWriteTimeUtc, info.Length, hits);
        return hits;
    }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A Claude Code transcript: what you wrote and Claude's text (not the tools, their results, its agents' work).</summary>
    private static List<Hit> ReadClaude(string path)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        string? title = null, folder = null, first = null;
        var said = new List<(bool Mine, DateTime? When, string Text)>();
        using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // (most lines are tool results: only the ones that can hold what's wanted are read)
                bool user = line.Contains("\"type\":\"user\""), assistant = line.Contains("\"type\":\"assistant\""), named = line.Contains("\"custom-title\"");
                if (!user && !assistant && !named && folder != null) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    folder ??= Str(r, "cwd");
                    string type = Str(r, "type") ?? "";
                    if (type == "custom-title") { title = Str(r, "customTitle") ?? title; continue; }
                    if (type is not ("user" or "assistant")) continue;
                    if (r.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) continue;
                    if (r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                    DateTime? when = DateTime.TryParse(Str(r, "timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : null;
                    if (!r.TryGetProperty("message", out var m) || !m.TryGetProperty("content", out var content)) continue;
                    bool mine = type == "user";
                    void Add(string? text)
                    {
                        if (text is not { Length: > 0 } || (mine && !SessionView.Visible(text))) return;
                        said.Add((mine, when, text));
                        if (mine) first ??= text;
                    }
                    if (content.ValueKind == JsonValueKind.String) Add(content.GetString());
                    else if (content.ValueKind == JsonValueKind.Array)
                        foreach (var b in content.EnumerateArray())
                            if (Str(b, "type") == "text") Add(Str(b, "text"));
                }
                catch { }
            }
        }
        return Hits(id, title ?? Short(first) ?? id, folder ?? "", null, said);
    }

    /// <summary>A conversation the agent kept (a provider's): what you wrote and its replies' text.</summary>
    private static List<Hit> ReadAgent(string path, DateTime written)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        var said = new List<(bool Mine, DateTime? When, string Text)>();
        if (r.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            foreach (var m in messages.EnumerateArray())
            {
                string role = Str(m, "role") ?? "";
                if (role is not ("user" or "assistant") || Str(m, "content") is not { Length: > 0 } text) continue;
                // (the conversation keeps no times: the last time it was saved)
                said.Add((role == "user", null, text));
            }
        if (said.Count > 0) said[^1] = said[^1] with { When = written };
        string? first = said.FirstOrDefault(s => s.Mine).Text;
        return Hits(Path.GetFileNameWithoutExtension(path), Short(first) ?? Str(r, "provider") ?? "", Str(r, "folder") ?? "", Str(r, "provider") ?? "", said);
    }

    private static List<Hit> Hits(string id, string title, string folder, string? provider, List<(bool Mine, DateTime? When, string Text)> said)
    {
        var hits = new List<Hit>(said.Count);
        var seen = new Dictionary<(bool, string), int>();
        // from the end: how many later messages say the same
        for (int i = said.Count - 1; i >= 0; i--)
        {
            var (mine, when, text) = said[i];
            var key = (mine, text.Trim());
            int after = seen.GetValueOrDefault(key);
            seen[key] = after + 1;
            hits.Add(new Hit(id, title, folder, provider, mine, when, text, after));
        }
        hits.Reverse();
        return hits;
    }

    private static string? Short(string? text)
    {
        if (text == null) return null;
        text = text.Replace('\n', ' ').Trim();
        return text.Length > 60 ? text[..60] + "…" : text;
    }
}
