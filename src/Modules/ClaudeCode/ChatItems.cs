using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>Claude's text: a read-only rich text box (selectable, copyable), filled as it streams in, Markdown when it's done.</summary>
internal sealed class AssistantText : RichTextBox
{
    private readonly System.Text.StringBuilder text = new();
    private readonly Run live = new();

    public AssistantText()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        Margin = new Thickness(0, 4, 40, 10);
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(SelectionBrushProperty, "AccentBrush");
        Document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI"), FontSize = 15.5, LineHeight = 24 };
        Document.Blocks.Add(new Paragraph(live) { Margin = new Thickness(0) });
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    public string Text => text.ToString();

    /// <summary>More of it, as it streams.</summary>
    public void Append(string more)
    {
        text.Append(more);
        live.Text = text.ToString();
    }

    /// <summary>The whole of it, shown as Markdown.</summary>
    public void Finish(string? whole = null)
    {
        if (whole != null) { text.Clear(); text.Append(whole); }
        Document.Blocks.Clear();
        foreach (var b in Markdown.Blocks(text.ToString(), this)) Document.Blocks.Add(b);
    }
}

/// <summary>Markdown, the parts Claude uses: headings, lists, code blocks, tables (monospaced), `code`, **bold**, *italic*, links.</summary>
internal static class Markdown
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static IEnumerable<Block> Blocks(string md, FrameworkElement theme)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();
        var codeBack = theme.TryFindResource("RoomBrush") as Brush ?? Brushes.Black;
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
                yield return new Paragraph(new Run(string.Join("\n", code))) { FontFamily = Mono, FontSize = 14, LineHeight = 20, Background = codeBack, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 2, 0, 12) };
                continue;
            }
            if (line.TrimStart().StartsWith('|'))
            {
                foreach (var b in Flush()) yield return b;
                var table = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                    if (!Regex.IsMatch(lines[i], @"^\s*\|[\s:\-|]+\|\s*$")) table.Add(lines[i].Trim());
                i--;
                yield return new Paragraph(new Run(string.Join("\n", table))) { FontFamily = Mono, FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 0, 0, 12) };
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                foreach (var b in Flush()) yield return b;
                var h = new Paragraph { FontWeight = FontWeights.SemiBold, FontSize = heading.Groups[1].Length <= 2 ? 20 : 17.5, Margin = new Thickness(0, 8, 0, 8) };
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
            if (m.Groups[1].Success) yield return new Run(v[1..^1]) { FontFamily = Mono, FontSize = 14 };
            else if (m.Groups[2].Success) yield return new Bold(new Run(v[2..^2]));
            else if (m.Groups[3].Success)
            {
                var lm = Regex.Match(v, @"\[([^\]]+)\]\(([^)]+)\)");
                var link = new Hyperlink(new Run(lm.Groups[1].Value)) { ToolTip = lm.Groups[2].Value };
                if (Uri.TryCreate(lm.Groups[2].Value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                    link.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { } };
                yield return link;
            }
            else yield return new Italic(new Run(v[1..^1]));
            at = m.Index + m.Length;
        }
        if (at < text.Length) yield return new Run(text[at..]);
    }
}

/// <summary>What you sent: a bubble at the right.</summary>
internal sealed class UserBubble : Border
{
    public UserBubble(string text, bool queued = false)
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        MaxWidth = 760;
        Margin = new Thickness(60, 8, 0, 14);
        Padding = new Thickness(14, 10, 14, 10);
        CornerRadius = new CornerRadius(12, 12, 2, 12);
        SetResourceReference(BackgroundProperty, "RowSelectedBrush");
        var t = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 15.5 };
        t.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        t.Style = null;
        Child = t;
        if (queued) ToolTip = T("Sent while Claude was busy: it reads it as soon as it can");
    }
}

/// <summary>A small line in the chat: a turn's summary, an error, a note.</summary>
internal sealed class NoteLine : TextBlock
{
    public NoteLine(string text, bool error = false, string? icon = null)
    {
        Text = (icon != null ? icon + "  " : "") + text;
        TextWrapping = TextWrapping.Wrap;
        FontSize = 13.5;
        Margin = new Thickness(0, 4, 0, 14);
        SetResourceReference(ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }
}

/// <summary>"Thinking · 315 tokens": while Claude thinks (its thoughts aren't shown, only how much).</summary>
internal sealed class ThinkingChip : Border
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
        text.Foreground = Palette.Brush(Palette.Of(SessionState.Thinking));
        Child = text;
        Show(false);
    }

    public void Tokens(int n) { tokens = n; Show(false); }

    public void Done()
    {
        // (a moment's thought, too short to count: not worth a line)
        if (tokens == 0 && (DateTime.UtcNow - start).TotalSeconds < 1.5) Visibility = Visibility.Collapsed;
        Show(true);
    }

    private void Show(bool done) => text.Text = done
        ? F("Thought for {0:0.0} s · {1} tokens", (DateTime.UtcNow - start).TotalSeconds, Palette.Tokens(tokens))
        : tokens > 0 ? F("Thinking… {0} tokens", Palette.Tokens(tokens)) : T("Thinking…");
}

/// <summary>
/// One tool call: its kind's colour and icon, what it does in a line (the file, the command, the search), its status
/// and time; click to open its details (an edit as red and green lines, the command, what came back). A to-do list is
/// shown as a checklist. While Claude asks to use it, Allow / Allow all this session / Deny.
/// </summary>
internal sealed class ToolCard : Border
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
    private readonly StackPanel subSteps = new();
    private readonly Border ask = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6) };
    private int partialChars;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public ToolCard(string id, string tool)
    {
        Id = id;
        Tool = tool;
        var colour = Palette.Of(Palette.KindOf(tool));
        Margin = new Thickness(0, 4, 40, 8);
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
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(icon);
        head.Children.Add(name);
        head.Children.Add(status);
        head.Children.Add(summary);
        head.MouseLeftButtonUp += (_, _) => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        head.ToolTip = T("Click for the details");

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(always);
        stack.Children.Add(subSteps);
        stack.Children.Add(ask);
        stack.Children.Add(details);
        Child = stack;
        status.Text = T("preparing…");
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
        summary.Text = Summary();
        summary.ToolTip = Summary(Tool, Input);
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
                always.Children.Add(Code("> " + (S("command") ?? ""), null, 4));
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
        status.Text = (error ? "\u2717" : "\u2713") + (timed ? "  " + F("{0:0.0} s", s) : "");
        status.Foreground = Palette.Brush(error ? Palette.Of(SessionState.Error) : Palette.Of(SessionState.Idle));
        if (text.Length > 0 && Tool != "TodoWrite")
        {
            var r = Code(Limit(text, 25), error ? Palette.Of(SessionState.Error) : null);
            r.Margin = new Thickness(0, 6, 0, 0);
            details.Children.Add(new TextBlock { Text = error ? T("Failed:") : T("Result:"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7 });
            details.Children.Add(r);
            if (error) details.Visibility = Visibility.Visible;
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
    public void Ask(string question, Action<bool, bool> answer)
    {
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
            b.Click += (_, _) => { ask.Visibility = Visibility.Collapsed; status.Text = allow ? T("running…") : T("denied"); answer(allow, all); };
            return b;
        }
        buttons.Children.Add(B(T("Allow"), true, false, "AccentButton"));
        buttons.Children.Add(B(T("Allow all this session"), true, true));
        buttons.Children.Add(B(T("Deny"), false, false));
        stack.Children.Add(buttons);
        ask.Child = stack;
        ask.Visibility = Visibility.Visible;
        status.Text = T("waiting for you");
        BringIntoView();
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
