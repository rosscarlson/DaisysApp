using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Claude's text: a read-only rich text box (selectable, copyable), filled as it streams in, Markdown when it's done.
/// It's what Claude tells you, so it stands out: an accent bar and a tint behind it, at the left edge, while the tool
/// cards and notes are indented under it. When it was written is in small type at its bottom right.
/// </summary>
internal sealed class AssistantText : RichTextBox, ILooked
{
    /// <summary>How far the tool cards and notes are indented, so Claude's text (and the thinking chips) stand out at the left.</summary>
    public const double Indent = 30;

    private readonly System.Text.StringBuilder text = new();
    private readonly Run live = new();
    private bool finished;

    public AssistantText()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(4, 0, 0, 0);
        SetResourceReference(BorderBrushProperty, "AccentBrush");
        Padding = new Thickness(16, 12, 16, 0);
        Margin = new Thickness(0, 6, 40, 12);
        SetResourceReference(SelectionBrushProperty, "AccentBrush");
        Document = new FlowDocument { PagePadding = new Thickness(0) };
        Document.Blocks.Add(new Paragraph(live) { Margin = new Thickness(0, 0, 0, 12) });
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalAlignment = HorizontalAlignment.Left;
        ApplyLook();
    }

    /// <summary>The font, size, line spacing, colour and width from the look (and the Markdown again, if it's done).</summary>
    public void ApplyLook()
    {
        Look.Paint(this, ForegroundProperty, Look.A.TextColor, "TextBrush");
        // (a tint of the accent colour behind it)
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Color.FromRgb(0x4C, 0xC2, 0xFF);
        Background = Palette.Brush(accent, 0x1C);
        Document.FontFamily = Look.Font;
        Document.FontSize = Look.Size;
        Document.LineHeight = Look.LineHeight;
        MaxWidth = Look.MaxWidth;
        if (finished) Finish();
    }

    public string Text => text.ToString();

    /// <summary>When it was written (now, or the transcript's time when it's read back; null: not known).</summary>
    public DateTime? When { get; set; } = DateTime.Now;

    /// <summary>A transcript line's time ("timestamp", UTC), as local time.</summary>
    public static DateTime? TimeOf(string? timestamp) =>
        DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : null;

    /// <summary>More of it, as it streams.</summary>
    public void Append(string more)
    {
        text.Append(more);
        live.Text = text.ToString();
    }

    /// <summary>The whole of it, shown as Markdown.</summary>
    public void Finish(string? whole = null)
    {
        finished = true;
        if (whole != null) { text.Clear(); text.Append(whole); }
        Document.Blocks.Clear();
        foreach (var b in Markdown.Blocks(text.ToString(), this)) Document.Blocks.Add(b);
        if (When is not { } when) return;
        // the time, small, at the bottom right (the date too if it wasn't today)
        if (Document.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        var stamp = new Paragraph(new Run(when.Date == DateTime.Today ? when.ToString("t") : when.ToString("d") + " " + when.ToString("t")))
        {
            TextAlignment = TextAlignment.Right, FontFamily = Look.Font, FontSize = Math.Max(9, Look.Size * 0.72), LineHeight = double.NaN,
            Margin = new Thickness(0, 2, 0, 6), ToolTip = when.ToString("F"),
        };
        stamp.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
        Document.Blocks.Add(stamp);
    }
}

/// <summary>Markdown, the parts Claude uses: headings, lists, code blocks, tables (monospaced), `code`, **bold**, *italic*, links.</summary>
internal static class Markdown
{
    public static IEnumerable<Block> Blocks(string md, FrameworkElement theme, double size = 0)
    {
        double body = size > 0 ? size : Look.Size;
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();
        var codeBack = Look.Brush(Look.A.CodeBackground) ?? theme.TryFindResource("RoomBrush") as Brush ?? Brushes.Black;
        var codeText = Look.Brush(Look.A.CodeText);
        var Mono = Look.Mono;
        IEnumerable<Block> Flush()
        {
            if (para.Count == 0) yield break;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < para.Count; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                foreach (var inline in Inlines(para[i])) p.Inlines.Add(inline);
            }
            para.Clear();
            yield return p;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("```"))
            {
                foreach (var b in Flush()) yield return b;
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                yield return new Paragraph(new Run(string.Join("\n", code))) { FontFamily = Mono, FontSize = Look.CodeSize, LineHeight = Look.CodeLineHeight, Background = codeBack, Foreground = codeText, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 2, 0, 12) };
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                foreach (var b in Flush()) yield return b;
                var table = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                    if (!Regex.IsMatch(lines[i], @"^\s*\|[\s:\-|]+\|\s*$")) table.Add(lines[i].Trim());
                i--;
                yield return new Paragraph(new Run(string.Join("\n", table))) { FontFamily = Mono, FontSize = Look.CodeSize * 0.96, LineHeight = Look.CodeLineHeight, Foreground = codeText, Margin = new Thickness(0, 0, 0, 12) };
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                foreach (var b in Flush()) yield return b;
                var h = new Paragraph { FontWeight = FontWeights.SemiBold, FontSize = heading.Groups[1].Length <= 2 ? body * 1.3 : body * 1.13, Margin = new Thickness(0, 8, 0, 8) };
                foreach (var inline in Inlines(heading.Groups[2].Value)) h.Inlines.Add(inline);
                yield return h;
                continue;
            }
            var item = Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
            if (item.Success)
            {
                foreach (var b in Flush()) yield return b;
                bool numbered = char.IsDigit(item.Groups[2].Value[0]);
                var li = new Paragraph { Margin = new Thickness(18 + item.Groups[1].Length * 8, 0, 0, 5), TextIndent = -14 };
                li.Inlines.Add(new Run(numbered ? item.Groups[2].Value + " " : "\u2022  "));
                foreach (var inline in Inlines(item.Groups[3].Value)) li.Inlines.Add(inline);
                yield return li;
                if (i + 1 < lines.Length && lines[i + 1].Trim().Length == 0 && i + 2 < lines.Length && !Regex.IsMatch(lines[i + 2], @"^\s*([-*+]|\d+[.)])\s")) li.Margin = new Thickness(li.Margin.Left, 0, 0, 12);
                continue;
            }
            if (line.Trim().Length == 0) { foreach (var b in Flush()) yield return b; continue; }
            if (Regex.IsMatch(line, @"^\s*(---|\*\*\*)\s*$")) { foreach (var b in Flush()) yield return b; continue; }
            para.Add(line);
        }
        foreach (var b in Flush()) yield return b;
    }

    private static IEnumerable<Inline> Inlines(string text)
    {
        // `code`, **bold**, *italic* / _italic_, [text](url)
        var pattern = new Regex(@"(`[^`]+`)|(\*\*[^*]+\*\*)|(\[[^\]]+\]\([^)\s]+\))|((?<![\w*])\*[^*\s][^*]*\*(?!\w))");
        int at = 0;
        foreach (Match m in pattern.Matches(text))
        {
            if (m.Index > at) yield return new Run(text[at..m.Index]);
            string v = m.Value;
            if (m.Groups[1].Success) yield return new Run(v[1..^1]) { FontFamily = Look.Mono, FontSize = Look.CodeSize };
            else if (m.Groups[2].Success) yield return new Bold(new Run(v[2..^2]));
            else if (m.Groups[3].Success)
            {
                var lm = Regex.Match(v, @"\[([^\]]+)\]\(([^)]+)\)");
                var link = new Hyperlink(new Run(lm.Groups[1].Value)) { ToolTip = lm.Groups[2].Value };
                if (Uri.TryCreate(lm.Groups[2].Value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    // (kept when it's copied, or pasted into the box)
                    link.NavigateUri = uri;
                    link.RequestNavigate += (_, e) => e.Handled = true;
                }
                if (uri != null && (uri.Scheme == "https" || uri.Scheme == "http"))
                    link.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { } };
                yield return link;
            }
            else yield return new Italic(new Run(v[1..^1]));
            at = m.Index + m.Length;
        }
        if (at < text.Length) yield return new Run(text[at..]);
    }
}

/// <summary>What you sent: a bubble at the right, its Markdown drawn as Claude's is (so formatting pasted into the box
/// shows as it was), and any images under it.</summary>
internal sealed class UserBubble : Border, ILooked
{
    private readonly RichTextBox box;
    private readonly string text;

    public UserBubble(string text, bool queued = false, IReadOnlyList<ImageData>? images = null)
    {
        this.text = text;
        HorizontalAlignment = HorizontalAlignment.Right;
        MaxWidth = 760;
        Margin = new Thickness(60, 8, 0, 14);
        Padding = new Thickness(14, 10, 14, 10);
        CornerRadius = new CornerRadius(12, 12, 2, 12);
        box = new RichTextBox
        {
            IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Document = new FlowDocument { PagePadding = new Thickness(0) },
        };
        box.SetResourceReference(RichTextBox.SelectionBrushProperty, "AccentBrush");
        var stack = new StackPanel();
        if (text.Trim().Length > 0) stack.Children.Add(box);
        if (images is { Count: > 0 })
        {
            var pics = new WrapPanel();
            foreach (var i in images) pics.Children.Add(i.Thumbnail());
            stack.Children.Add(pics);
        }
        Child = stack;
        if (queued) ToolTip = T("Sent while Claude was busy: it reads it as soon as it can");
        ApplyLook();
    }

    public string Text => text;

    public void ApplyLook()
    {
        Look.Paint(this, BackgroundProperty, Look.A.BubbleBackground, "RowSelectedBrush");
        Look.Paint(box, RichTextBox.ForegroundProperty, Look.A.BubbleText, "TextBrush");
        box.Document.FontFamily = Look.Font;
        box.Document.FontSize = Look.Size;
        box.Document.LineHeight = Look.LineHeight;
        MaxWidth = Math.Min(Look.MaxWidth, 760 * Look.Size / 15.5);
        box.Document.Blocks.Clear();
        foreach (var b in Markdown.Blocks(text, this)) box.Document.Blocks.Add(b);
        if (box.Document.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        // (a rich text box takes all the width it's given: as wide as its longest line instead, as a text box is)
        double widest = 0;
        var face = new Typeface(Look.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            widest = Math.Max(widest, new FormattedText(line, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Look.Size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace);
        box.Width = Math.Min(widest * 1.04 + 12, Math.Max(80, MaxWidth - Padding.Left - Padding.Right));
    }
}

/// <summary>A small line in the chat: a turn's summary, an error, a note.</summary>
internal sealed class NoteLine : TextBlock, ILooked
{
    public string Note { get; private set; }
    public string? Icon { get; }
    public bool Error { get; private set; }

    public NoteLine(string text, bool error = false, string? icon = null)
    {
        Note = text;
        Icon = icon;
        Error = error;
        Text = (icon != null ? icon + "  " : "") + text;
        TextWrapping = TextWrapping.Wrap;
        Margin = new Thickness(AssistantText.Indent, 4, 0, 14);
        SetResourceReference(ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
        ApplyLook();
    }

    /// <summary>Says something else (a step that's finished: "Compacting…" once it's compacted).</summary>
    public void Set(string text, bool error = false)
    {
        Note = text;
        Error = error;
        Text = (Icon != null ? Icon + "  " : "") + text;
        SetResourceReference(ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    public void ApplyLook()
    {
        FontSize = Look.CardSize * 0.96;
        FontFamily = Look.Font;
    }
}

/// <summary>
/// The session notes' turn as one line ("📝 Session notes brought up to date: CLAUDE.md, README.md ▸"): what it did
/// (its cards, text and summary) is under it, closed; click to open it. It goes above the reply it follows, so that
/// reply stays at the bottom, where it's read.
/// </summary>
internal sealed class NotesGroup : StackPanel, ILooked
{
    public const string Icon = "📝";

    private readonly TextBlock head = new() { TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand };
    private readonly StackPanel items = new() { Visibility = Visibility.Collapsed };
    private string line;

    public NotesGroup(string line)
    {
        this.line = line;
        head.Margin = new Thickness(AssistantText.Indent, 4, 0, 14);
        head.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        head.MouseLeftButtonUp += (_, _) => Open(items.Visibility != Visibility.Visible);
        Children.Add(head);
        Children.Add(items);
        ApplyLook();
        Show();
    }

    /// <summary>What the notes' turn adds goes in here.</summary>
    public UIElementCollection Items => items.Children;

    public void Open(bool open = true) { items.Visibility = open ? Visibility.Visible : Visibility.Collapsed; Show(); }

    /// <summary>The turn's over: the line says which files it changed.</summary>
    public void Done()
    {
        var files = items.Children.OfType<ToolCard>().Where(c => c.Changed && c.Input.ValueKind == JsonValueKind.Object && c.Input.TryGetProperty("file_path", out var f) && f.ValueKind == JsonValueKind.String)
            .Select(c => System.IO.Path.GetFileName(c.Input.GetProperty("file_path").GetString() ?? "")).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        line = files.Count > 0 ? F("Session notes brought up to date: {0}", string.Join(", ", files)) : T("Session notes: nothing to change");
        Show();
    }

    private void Show() => head.Text = Icon + "  " + line + "  " + (items.Visibility == Visibility.Visible ? "▾" : "▸");

    public void ApplyLook()
    {
        head.FontSize = Look.CardSize * 0.96;
        head.FontFamily = Look.Font;
        foreach (var i in items.Children.OfType<ILooked>()) i.ApplyLook();
    }
}

/// <summary>"Thinking · 315 tokens": while Claude thinks (its thoughts aren't shown, only how much).</summary>
internal sealed class ThinkingChip : Border, ILooked
{
    private readonly TextBlock text = new() { FontSize = 13.5 };
    private readonly DateTime start = DateTime.UtcNow;
    private int tokens;

    public ThinkingChip()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        Padding = new Thickness(10, 3, 10, 3);
        Margin = new Thickness(0, 2, 0, 8);
        CornerRadius = new CornerRadius(10);
        Background = Palette.Brush(Palette.Of(SessionState.Thinking), 0x30);
        ApplyLook();
        text.Foreground = Palette.Brush(Palette.Of(SessionState.Thinking));
        Child = text;
        Show(false);
    }

    public void ApplyLook() => text.FontSize = Look.CardSize * 0.96;

    public void Tokens(int n) { tokens = n; Show(false); }

    public void Done()
    {
        // (a moment's thought, too short to count: not worth a line)
        if (tokens == 0 && (DateTime.UtcNow - start).TotalSeconds < 1.5) Visibility = Visibility.Collapsed;
        Show(true);
    }

    private void Show(bool done) => text.Text = done
        ? F("Thought for {0} · {1} tokens", Palette.Time((DateTime.UtcNow - start).TotalSeconds), Palette.Tokens(tokens))
        : tokens > 0 ? F("Thinking… {0} tokens", Palette.Tokens(tokens)) : T("Thinking…");
}

/// <summary>
/// One tool call: its kind's colour and icon, what it does in a line (the file, the command, the search), its status
/// and time; click to open its details (an edit as red and green lines, the command, what came back). A to-do list is
/// shown as a checklist. While Claude asks to use it, Allow / Allow all this session / Always allow everything / Deny.
/// </summary>
internal sealed class ToolCard : Border, ILooked
{
    public string Id { get; }
    public string Tool { get; }
    public JsonElement Input { get; private set; }
    public bool Finished { get; private set; }
    public bool Changed => Palette.KindOf(Tool) == Palette.ToolKind.Change;
    public DateTime Started { get; } = DateTime.UtcNow;

    private readonly TextBlock summary = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly StackPanel details = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel always = new();
    private readonly TextBlock chevron = new() { Text = "▸", FontSize = 12, Width = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
    private readonly StackPanel subSteps = new();
    private readonly Border ask = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6) };
    private int partialChars;

    private static FontFamily Mono => Look.Mono;

    public ToolCard(string id, string tool)
    {
        Id = id;
        Tool = tool;
        var colour = Palette.Of(Palette.KindOf(tool));
        Margin = new Thickness(AssistantText.Indent, 4, 40, 8);
        Padding = new Thickness(12, 9, 12, 9);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(3, 0, 0, 0);
        BorderBrush = Palette.Brush(colour);
        SetResourceReference(BackgroundProperty, "RoomBrush");

        var icon = new TextBlock { Text = Palette.IconOf(tool), FontSize = 15, Foreground = Palette.Brush(colour), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var name = new TextBlock { Text = tool, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        DockPanel.SetDock(chevron, Dock.Left);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(chevron);
        head.Children.Add(icon);
        head.Children.Add(name);
        head.Children.Add(status);
        head.Children.Add(summary);
        // starts closed (its title only): click to open or close it
        head.MouseLeftButtonUp += (_, _) => Show(details.Visibility != Visibility.Visible);
        head.ToolTip = T("Click to show or hide the details");

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(always);
        stack.Children.Add(subSteps);
        stack.Children.Add(ask);
        stack.Children.Add(details);
        Child = stack;
        status.Text = T("preparing…");
        ApplyLook();
    }

    /// <summary>Opens or closes the details (the title line always shows).</summary>
    private void Show(bool open)
    {
        details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = open ? "▾" : "▸";
    }

    private bool Runs => Palette.KindOf(Tool) == Palette.ToolKind.Run;

    /// <summary>The look's card size: the whole card drawn that much bigger (or smaller) than its usual 14.</summary>
    public void ApplyLook()
    {
        double k = Look.CardScale;
        LayoutTransform = Math.Abs(k - 1) < 0.01 ? Transform.Identity : new ScaleTransform(k, k);
    }

    /// <summary>Its input arriving, as it streams (the length so far).</summary>
    public void Partial(int chars)
    {
        partialChars += chars;
        if (!Finished) status.Text = F("writing… {0}", Palette.Tokens(partialChars));
    }

    /// <summary>Its whole input: the summary line and the details.</summary>
    public void SetInput(JsonElement input)
    {
        Input = input.Clone();
        if (!Finished) status.Text = T("running…");
        // (the title line: one line, however long the command or text; all of it in the details and the tooltip)
        summary.Text = OneLine(Summary());
        summary.ToolTip = Limit(Summary(Tool, Input), 20);
        details.Children.Clear();
        always.Children.Clear();
        string? S(string n) => Input.ValueKind == JsonValueKind.Object && Input.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (Tool)
        {
            case "Edit":
                details.Children.Add(Diff(S("old_string") ?? "", S("new_string") ?? ""));
                break;
            case "MultiEdit":
                if (Input.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array)
                    foreach (var e in edits.EnumerateArray())
                        details.Children.Add(Diff(e.TryGetProperty("old_string", out var o) ? o.GetString() ?? "" : "", e.TryGetProperty("new_string", out var n) ? n.GetString() ?? "" : ""));
                break;
            case "Write":
                details.Children.Add(Code(Limit(S("content") ?? "", 40), Palette.Of(Palette.ToolKind.Change)));
                break;
            case "Bash":
            case "PowerShell":
                // (in the details, closed: the title shows what it's for)
                details.Children.Add(Code("> " + (S("command") ?? ""), null, 4));
                break;
            case "Task":
            case "Agent":
                details.Children.Add(Code(Limit(S("prompt") ?? "", 30), null));
                break;
            case "TodoWrite":
                if (Input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array) always.Children.Add(Todos(todos));
                break;
            default:
                details.Children.Add(Code(Limit(JsonSerializer.Serialize(Input, new JsonSerializerOptions { WriteIndented = true }), 30), null));
                break;
        }
    }

    /// <summary>What it gave back: done (in the time it took), or failed.</summary>
    public void SetResult(string text, bool error, bool timed = true)
    {
        Finished = true;
        ask.Visibility = Visibility.Collapsed;
        double s = (DateTime.UtcNow - Started).TotalSeconds;
        status.Text = (error ? "\u2717" : "\u2713") + (timed ? "  " + Palette.Time(s) : "");
        status.Foreground = Palette.Brush(error ? Palette.Of(SessionState.Error) : Palette.Of(SessionState.Idle));
        if (text.Length > 0 && Tool != "TodoWrite")
        {
            var r = Code(Limit(text, 25), error ? Palette.Of(SessionState.Error) : null);
            r.Margin = new Thickness(0, 6, 0, 0);
            details.Children.Add(new TextBlock { Text = error ? T("Failed:") : T("Result:"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7 });
            details.Children.Add(r);
            // a failed step opens (not a command's: its ✗ shows, and it opens when clicked)
            if (error && !Runs) Show(true);
        }
    }

    /// <summary>A step of the agent it started (Task): a line under it.</summary>
    public void SubStep(string tool, string what)
    {
        var colour = Palette.Of(Palette.KindOf(tool));
        var line = new TextBlock { FontSize = 13, Margin = new Thickness(24, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        line.Inlines.Add(new Run("\u21B3 " + tool + "  ") { Foreground = Palette.Brush(colour) });
        line.Inlines.Add(new Run(what));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        subSteps.Children.Add(line);
        if (subSteps.Children.Count > 12) subSteps.Children.RemoveAt(0);
    }

    /// <summary>Claude asks to use it: the three buttons; <paramref name="answer"/> gets allow, and for the whole session.</summary>
    /// <summary>It's asking now (its buttons showing).</summary>
    public bool Asking => ask.Visibility == Visibility.Visible;
    private Action<bool, bool>? pending;

    /// <summary>Answers the question showing (permission prompts were switched off).</summary>
    public void AnswerNow(bool allow)
    {
        if (!Asking || pending is not { } answer) return;
        pending = null;
        ask.Visibility = Visibility.Collapsed;
        status.Text = allow ? T("running…") : T("denied");
        answer(allow, false);
    }

    /// <summary>Asks: Allow, Allow all this session, Always allow everything (Settings → AI's switch, every session from
    /// now on: <paramref name="everything"/>), Deny.</summary>
    public void Ask(string question, Action<bool, bool> answer, Action? everything = null)
    {
        pending = answer;
        var colour = Palette.Of(SessionState.Waiting);
        ask.Background = Palette.Brush(colour, 0x26);
        ask.BorderBrush = Palette.Brush(colour);
        ask.BorderThickness = new Thickness(1);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = question, FontSize = 14.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button B(string text, bool allow, bool all, string? style = null)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            if (style != null) b.SetResourceReference(StyleProperty, style);
            b.Click += (_, _) => { pending = null; ask.Visibility = Visibility.Collapsed; status.Text = allow ? T("running…") : T("denied"); answer(allow, all); };
            return b;
        }
        buttons.Children.Add(B(T("Allow"), true, false, "AccentButton"));
        buttons.Children.Add(B(T("Allow all this session"), true, true));
        if (everything != null)
        {
            var b = B(T("Always allow everything"), true, true);
            b.ToolTip = T("Never ask again, in any session, with any AI: it does everything without asking (Settings → AI → Allow everything turns it off)");
            b.Click += (_, _) => everything();
            buttons.Children.Add(b);
        }
        buttons.Children.Add(B(T("Deny"), false, false));
        stack.Children.Add(buttons);
        ask.Child = stack;
        ask.Visibility = Visibility.Visible;
        // a command it asks to run: shown, to decide on
        if (Runs) Show(true);
        status.Text = T("waiting for you");
        BringIntoView();
    }

    private Action<JsonObject?>? questions;

    /// <summary>It's asking you its own questions (AskUserQuestion), not for permission.</summary>
    public bool AskingQuestions => Asking && questions != null;

    /// <summary>
    /// Claude's own questions (AskUserQuestion: each with a short header, the question, 2–4 options and whether more
    /// than one can be picked): an option or your own words for each, then Send. <paramref name="answered"/> gets the
    /// answers, question text → the option's label (several joined with ", "), or null for Skip.
    /// </summary>
    public void AskQuestions(JsonElement input, Action<JsonObject?> answered)
    {
        questions = answered;
        pending = null;
        var colour = Palette.Of(SessionState.Waiting);
        ask.Background = Palette.Brush(colour, 0x26);
        ask.BorderBrush = Palette.Brush(colour);
        ask.BorderThickness = new Thickness(1);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = T("Claude is asking you:"), FontSize = 13, Opacity = 0.8 });
        var send = new Button { Content = T("Send"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        send.SetResourceReference(StyleProperty, "AccentButton");
        var reads = new List<Func<(string Question, string Answer)>>();
        int n = 0;
        var list = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array ? qs.EnumerateArray().ToList() : new();
        foreach (var q in list)
        {
            string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            string text = Str(q, "question");
            bool multi = q.TryGetProperty("multiSelect", out var m) && m.ValueKind == JsonValueKind.True;
            string group = $"q{Id}-{n++}";
            var block = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (Str(q, "header") is { Length: > 0 } header)
                block.Children.Add(new TextBlock { Text = header.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Opacity = 0.75 });
            block.Children.Add(new TextBlock { Text = text, FontSize = 14.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4) });
            var picks = new List<(ToggleButtonLike Box, string Label)>();
            var options = q.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array ? o.EnumerateArray().ToList() : new();
            foreach (var opt in options)
            {
                string label = Str(opt, "label");
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.Wrap });
                if (Str(opt, "description") is { Length: > 0 } d)
                    content.Children.Add(new TextBlock { Text = d, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
                System.Windows.Controls.Primitives.ToggleButton box = multi ? new CheckBox { Content = content } : new RadioButton { Content = content, GroupName = group };
                box.Margin = new Thickness(0, 3, 0, 3);
                block.Children.Add(box);
                picks.Add((new ToggleButtonLike(box), label));
            }
            var other = new TextBox { Margin = new Thickness(0, 4, 0, 0), MinWidth = 240, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, ToolTip = T("Or write your own answer") };
            var otherRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var otherLabel = new TextBlock { Text = T("Other:"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };
            otherRow.Children.Add(otherLabel);
            otherRow.Children.Add(other);
            block.Children.Add(otherRow);
            // (typing your own answer clears a single choice; picking one clears what you typed)
            other.TextChanged += (_, _) => { if (!multi && other.Text.Length > 0) foreach (var p in picks) p.Box.IsChecked = false; Check(); };
            foreach (var p in picks) p.Box.Changed += () => { if (!multi && p.Box.IsChecked) other.Text = ""; Check(); };
            reads.Add(() =>
            {
                var chosen = picks.Where(p => p.Box.IsChecked).Select(p => p.Label).ToList();
                if (other.Text.Trim().Length > 0) chosen.Add(other.Text.Trim());
                return (text, string.Join(", ", chosen));
            });
            stack.Children.Add(block);
        }
        void Check() => send.IsEnabled = reads.Count > 0 && reads.All(r => r().Answer.Length > 0);

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        send.Click += (_, _) =>
        {
            var answers = new JsonObject();
            foreach (var r in reads) { var (q, a) = r(); answers[q] = a; }
            Close(T("answered"));
            answered(answers);
        };
        var skip = new Button { Content = T("Skip"), Padding = new Thickness(12, 4, 12, 4), ToolTip = T("Don't answer: Claude carries on without") };
        skip.Click += (_, _) => { Close(T("skipped")); answered(null); };
        buttons.Children.Add(send);
        buttons.Children.Add(skip);
        stack.Children.Add(buttons);
        ask.Child = stack;
        ask.Visibility = Visibility.Visible;
        Show(true);
        status.Text = T("waiting for you");
        BringIntoView();
    }

    /// <summary>Its questions were answered on the other PC (remote control), or here for the other PC: the answers in place of the buttons.</summary>
    public void QuestionsAnswered(JsonObject? answers)
    {
        if (!AskingQuestions) return;
        Close(answers == null ? T("skipped") : T("answered"));
    }

    private void Close(string how)
    {
        questions = null;
        ask.Visibility = Visibility.Collapsed;
        status.Text = how;
    }

    /// <summary>A check box or radio button's ticked state, and a change to it.</summary>
    private sealed class ToggleButtonLike
    {
        private readonly System.Windows.Controls.Primitives.ToggleButton box;
        public event Action? Changed;
        public ToggleButtonLike(System.Windows.Controls.Primitives.ToggleButton box)
        {
            this.box = box;
            box.Checked += (_, _) => Changed?.Invoke();
            box.Unchecked += (_, _) => Changed?.Invoke();
        }
        public bool IsChecked { get => box.IsChecked == true; set => box.IsChecked = value; }
    }

    /// <summary>The question was answered somewhere else (on the other PC, with remote control): its buttons go.</summary>
    public void Answered(bool allow)
    {
        if (!Asking) return;
        if (questions != null) { Close(allow ? T("answered") : T("skipped")); return; }
        pending = null;
        ask.Visibility = Visibility.Collapsed;
        status.Text = allow ? T("running…") : T("denied");
    }

    /// <summary>It was refused (by the permission mode, or you).</summary>
    public void Denied(string why)
    {
        Finished = true;
        ask.Visibility = Visibility.Collapsed;
        status.Text = "\u2717  " + T("denied");
        status.Foreground = Palette.Brush(Palette.Of(SessionState.Waiting));
        details.Children.Add(new TextBlock { Text = why, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 6, 0, 0) });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The session's folder: paths in it are shown from there.</summary>
    public string? Root { get; set; }

    public string Summary() => Summary(Tool, Input, Root);

    /// <summary>Its first line, with "…" when there's more.</summary>
    private static string OneLine(string text)
    {
        text = text.Trim();
        int nl = text.IndexOfAny(['\r', '\n']);
        return nl < 0 ? text : text[..nl].TrimEnd() + " …";
    }

    public static string Summary(string tool, JsonElement input, string? root = null)
    {
        string? S(string n) => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? Short(v.GetString(), root) : null;
        string? file = S("file_path") ?? S("notebook_path") ?? S("path");
        return tool switch
        {
            "Bash" or "PowerShell" => S("description") ?? S("command") ?? "",
            "Glob" => (S("pattern") ?? "") + (S("path") is { } p ? "  in " + p : ""),
            "Grep" => "\u201C" + (S("pattern") ?? "") + "\u201D" + (S("path") is { } gp ? "  in " + gp : ""),
            "WebFetch" => S("url") ?? "",
            "WebSearch" => S("query") ?? "",
            "Task" or "Agent" => (S("subagent_type") is { } st ? st + ": " : "") + (S("description") ?? ""),
            "TodoWrite" => T("the to-do list"),
            _ => file ?? (input.ValueKind == JsonValueKind.Object ? string.Join("  ", input.EnumerateObject().Take(2).Select(p => p.Value.ToString())) : ""),
        };
    }

    /// <summary>A path inside the session's folder, from there (src\\Foo.cs).</summary>
    private static string? Short(string? path, string? root)
    {
        if (path == null || string.IsNullOrEmpty(root) || path.Length <= root.Length + 1) return path;
        string r = root.TrimEnd('\\', '/') + "\\";
        return path.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? path[r.Length..] : path;
    }

    private static string Limit(string text, int lines)
    {
        var all = text.Replace("\r\n", "\n").Split('\n');
        if (all.Length <= lines) return text.TrimEnd();
        return string.Join("\n", all.Take(lines)) + "\n" + F("… {0} more lines", all.Length - lines);
    }

    private static TextBox Code(string text, Color? colour, int top = 0)
    {
        var t = new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = Mono, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, top, 0, 0), Style = null,
        };
        if (colour is { } c) t.Foreground = Palette.Brush(c); else t.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        return t;
    }

    /// <summary>An edit: the old lines red, the new green (the lines both have, plain).</summary>
    private static FrameworkElement Diff(string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n');
        var b = after.Replace("\r\n", "\n").Split('\n');
        int head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        int tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;
        var box = new TextBlock { FontFamily = Mono, FontSize = 13.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        var red = Palette.Brush(Palette.Of(SessionState.Error), 0x30);
        var green = Palette.Brush(Palette.Of(Palette.ToolKind.Change), 0x30);
        void Line(string prefix, string text, Brush? back)
        {
            if (box.Inlines.Count > 0) box.Inlines.Add(new LineBreak());
            box.Inlines.Add(new Run(prefix + text) { Background = back });
        }
        int shown = 0;
        for (int i = Math.Max(0, head - 2); i < head; i++) Line("  ", a[i], null);
        for (int i = head; i < a.Length - tail && shown++ < 40; i++) Line("- ", a[i], red);
        for (int i = head; i < b.Length - tail && shown++ < 80; i++) Line("+ ", b[i], green);
        for (int i = a.Length - tail; i < Math.Min(a.Length, a.Length - tail + 2); i++) Line("  ", a[i], null);
        return box;
    }

    /// <summary>The to-do list: done ticked, the one being done highlighted, the rest open.</summary>
    private static FrameworkElement Todos(JsonElement todos)
    {
        var stack = new StackPanel { Margin = new Thickness(22, 6, 0, 0) };
        foreach (var t in todos.EnumerateArray())
        {
            string status = t.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            string text = t.TryGetProperty(status == "in_progress" && t.TryGetProperty("activeForm", out _) ? "activeForm" : "content", out var c) ? c.GetString() ?? "" : "";
            var line = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
            line.Inlines.Add(new Run(status == "completed" ? "\u2611  " : status == "in_progress" ? "\u25D0  " : "\u2610  ")
                { Foreground = Palette.Brush(status == "completed" ? Palette.Of(SessionState.Idle) : status == "in_progress" ? Palette.Of(SessionState.Writing) : Palette.Of(SessionState.Offline)) });
            var run = new Run(text);
            if (status == "completed") { run.TextDecorations = TextDecorations.Strikethrough; line.Opacity = 0.65; }
            if (status == "in_progress") run.FontWeight = FontWeights.SemiBold;
            line.Inlines.Add(run);
            stack.Children.Add(line);
        }
        return stack;
    }
}
