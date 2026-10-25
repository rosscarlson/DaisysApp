using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// The Status card's list: a line per thing done, with its time and a mark (working, done, failed, note), what the TV
/// answered under it in small mono text, and bold section lines ("Apply · 12 changes") with their summary. A working
/// line is finished in place (<see cref="Line.Done"/>). Right-click copies it all.
/// </summary>
internal sealed class StatusLog : Border
{
    public enum Mark { Working, Ok, Failed, Note }

    private readonly StackPanel lines = new();
    private readonly ScrollViewer scroll;
    private readonly List<Line> all = new();

    public StatusLog()
    {
        scroll = new ScrollViewer { Content = lines, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 };
        Child = scroll;
        var copy = new MenuItem { Header = T("Copy") };
        copy.Click += (_, _) => { try { Clipboard.SetText(Text()); } catch { } };
        var clear = new MenuItem { Header = T("Clear") };
        clear.Click += (_, _) => { lines.Children.Clear(); all.Clear(); };
        ContextMenu = new ContextMenu { Items = { copy, clear } };
        Background = Brushes.Transparent;
    }

    /// <summary>A section heading (a bold line with a rule above it, after the first).</summary>
    public void Section(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, lines.Children.Count == 0 ? 0 : 10, 0, 3), TextWrapping = TextWrapping.Wrap };
        lines.Children.Add(t);
        all.Add(new Line(this, null, t, null, null, text));
        scroll.ScrollToEnd();
    }

    /// <summary>A line; <paramref name="said"/> is what the TV answered (shown small under it).</summary>
    public Line Add(string text, Mark mark = Mark.Note, string? said = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var time = new TextBlock { Text = DateTime.Now.ToString("H:mm:ss"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        time.SetResourceReference(StyleProperty, "SecondaryText");
        var icon = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        Grid.SetColumn(icon, 1);
        var body = new StackPanel();
        Grid.SetColumn(body, 2);
        var main = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 2) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        body.Children.Add(main);
        body.Children.Add(detail);
        grid.Children.Add(time);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        lines.Children.Add(grid);
        var line = new Line(this, icon, main, detail, time, text);
        line.Set(mark, text, said);
        all.Add(line);
        scroll.ScrollToEnd();
        return line;
    }

    public Line Working(string text) => Add(text, Mark.Working);
    public void Ok(string text, string? said = null) => Add(text, Mark.Ok, said);
    public void Failed(string text, string? said = null) => Add(text, Mark.Failed, said);
    public void Note(string text, string? said = null) => Add(text, Mark.Note, said);

    /// <summary>Everything as plain text (Copy).</summary>
    public string Text()
    {
        var b = new StringBuilder();
        foreach (var l in all) b.AppendLine(l.AsText());
        return b.ToString();
    }

    /// <summary>One line, finished in place when its work ends.</summary>
    public sealed class Line
    {
        private readonly StatusLog log;
        private readonly TextBlock? icon, detail, time;
        private readonly TextBlock main;
        private string text, said = "";
        private Mark mark;

        internal Line(StatusLog log, TextBlock? icon, TextBlock main, TextBlock? detail, TextBlock? time, string text)
        {
            this.log = log;
            this.icon = icon;
            this.main = main;
            this.detail = detail;
            this.time = time;
            this.text = text;
        }

        public void Done(bool ok, string? text = null, string? said = null)
        {
            // (the time it finished)
            if (time != null) time.Text = DateTime.Now.ToString("H:mm:ss");
            Set(ok ? Mark.Ok : Mark.Failed, text ?? this.text, said);
        }

        internal void Set(Mark mark, string text, string? said)
        {
            this.mark = mark;
            this.text = text;
            this.said = Tidy(said);
            main.Text = text;
            if (icon == null || detail == null) return;
            (icon.Text, var brush) = mark switch
            {
                Mark.Working => ("", "AccentBrush"),
                Mark.Ok => ("", "SuccessBrush"),
                Mark.Failed => ("", "ErrorTextBrush"),
                _ => ("", "TextSecondaryBrush"),
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
            detail.Text = this.said;
            detail.Visibility = this.said.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            log.scroll.ScrollToEnd();
        }

        internal string AsText()
        {
            if (time == null) return "== " + text;
            var m = mark switch { Mark.Working => "…", Mark.Ok => "✓", Mark.Failed => "✗", _ => "·" };
            return $"{time.Text}  {m} {text}" + (said.Length > 0 ? "\n            " + said.Replace("\n", "\n            ") : "");
        }

        /// <summary>The TV's answer, without blank lines, at most 6 lines.</summary>
        private static string Tidy(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var parts = s.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            return string.Join("\n", parts.Take(6)) + (parts.Count > 6 ? "\n…" : "");
        }
    }
}
