using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The Claude Account box's list of accounts (since 3.11.1): a line each with its usage in columns (5H, W, days until
/// the week resets), coloured by its state (green, amber near a limit, red at it, with how long until it's back under
/// the name); click a name to use that account (since 3.15.0; it was a drop-down), the one in use bold on a tint; the
/// arrow opens its windows (bars, resets). Claude Code's own sign-in's usage is PlanUsage's; another's is what its
/// sessions report, or "/usage" asked in its folder (no model is asked) when the list first shows, then every few
/// minutes while it shows. A new tab's Claude Account is this list too: the readings are shared by every copy.
/// </summary>
internal sealed class AccountsUsage : StackPanel
{
    private readonly AiSettings settings;
    private readonly PlanUsage usage;
    private readonly Func<string?> findExe;
    // another account's last reading, and when (one for every copy of the list)
    private static readonly Dictionary<string, (PlanUsage.Elsewhere Plan, DateTime At)> others = new();
    private static bool reading;
    private static event Action? OthersChanged;
    private readonly HashSet<string> open = new();
    private readonly DispatcherTimer poll = new();
    private readonly DispatcherTimer minute = new() { Interval = TimeSpan.FromMinutes(1) };
    private string? showing;

    /// <summary>A name clicked: that account's id, to use it.</summary>
    public event Action<string>? Picked;

    public AccountsUsage(AiSettings settings, PlanUsage usage, Func<string?> findExe)
    {
        this.settings = settings;
        this.usage = usage;
        this.findExe = findExe;
        Margin = new Thickness(0, 0, 0, 4);
        Grid.SetIsSharedSizeScope(this, true);
        // (only while it's in the window: a new tab's copy goes with its tab)
        Loaded += (_, _) => { usage.Changed += Render; settings.AccountsChanged += Render; OthersChanged += Render; Render(); };
        Unloaded += (_, _) => { usage.Changed -= Render; settings.AccountsChanged -= Render; OthersChanged -= Render; };
        poll.Tick += (_, _) => _ = ReadAsync();
        // (the countdowns)
        minute.Tick += (_, _) => Render();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                poll.Interval = TimeSpan.FromMinutes(Math.Clamp(settings.Usage.CheckMinutes, 5, 120));
                poll.Start();
                minute.Start();
                if (others.Count == 0) _ = ReadAsync();
            }
            else { poll.Stop(); minute.Stop(); }
        };
        Render();
    }

    /// <summary>The account in use (null: Claude Code's own), bold in the list.</summary>
    public void Showing(string? id)
    {
        showing = id;
        Render();
    }

    /// <summary>A session on another account reported its usage (its own requests, or its ⟳).</summary>
    public void Report(string id, PlanUsage.Elsewhere plan)
    {
        others[id] = (plan, DateTime.Now);
        OthersChanged?.Invoke();
    }

    /// <summary>"/usage" in each other account's folder, one after another.</summary>
    public async Task ReadAsync()
    {
        if (reading || findExe() is not { } exe) return;
        reading = true;
        try
        {
            foreach (var a in Claude().Where(a => a.Id != settings.ActiveAccount).ToList())
            {
                string? folder;
                try { folder = ClaudeAccounts.FolderFor(settings, a.Id); }
                catch (Exception ex) { Logging.Log.Here.Warn($"Accounts' usage: {a.Email}'s folder: {ex.Message}"); continue; }
                if (folder == null) continue;
                var (windows, problem) = await PlanUsage.ReadAsync(exe, folder);
                if (windows.Count == 0) { Logging.Log.Here.Info($"Accounts' usage: {a.Email}: {problem}"); continue; }
                var was = others.TryGetValue(a.Id, out var w) ? w.Plan : null;
                others[a.Id] = (new PlanUsage.Elsewhere(windows, was?.Blocked == true && windows.Any(x => x.Used >= 0.99), was?.BlockedUntil, a.Label), DateTime.Now);
                OthersChanged?.Invoke();
            }
        }
        finally { reading = false; }
    }

    private List<AiAccount> Claude() => settings.Accounts.Where(a => a.Kind == AccountKinds.Claude).ToList();

    /// <summary>An account's windows, whether it's at its limit and until when, and when it was read (null: not yet).</summary>
    private (List<PlanUsage.Window> Windows, bool Blocked, DateTime? Until, DateTime? At)? Of(AiAccount a)
    {
        if (a.Id == settings.ActiveAccount)
            return usage.Windows.Count == 0 && !usage.Blocked ? null : (usage.Windows, usage.Blocked, usage.BlockedUntil, usage.Checked);
        return others.TryGetValue(a.Id, out var o) ? (o.Plan.Windows, o.Plan.Blocked, o.Plan.BlockedUntil, o.At) : null;
    }

    private void Render()
    {
        Children.Clear();
        var list = Claude();
        Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        string chosen = showing is { } s && list.Any(a => a.Id == s) ? s : settings.ActiveAccount ?? "";
        foreach (var a in list) Row(a, a.Id == chosen);
    }

    private void Row(AiAccount a, bool current)
    {
        var u = Of(a);
        var now = DateTime.Now;
        Color colour;
        // (amber or red: why, under the name)
        string? state = null;
        string tip;
        string fiveH = "–", week = "", days = "";
        if (u is not { } x)
        {
            colour = Colors.Gray;
            tip = T("Not read yet");
        }
        else
        {
            double top = x.Windows.Count == 0 ? 0 : x.Windows.Max(w => w.Used);
            var full = x.Windows.Where(w => w.Used >= 0.99 && w.Resets > now).Select(w => w.Resets).Max();
            DateTime? back = x.Blocked && x.Until > now ? x.Until : full;
            var high = x.Windows.OrderByDescending(w => w.Used).FirstOrDefault();
            if (x.Blocked || top >= 0.99)
            {
                colour = Palette.Of(SessionState.Error);
                state = back is { } b ? F("Back {0}", PlanUsage.Until(b)) : T("Limit reached");
            }
            else if (top * 100 >= settings.Usage.WarnAt)
            {
                colour = Palette.Of(SessionState.Waiting);
                if (high?.Resets is { } r) state = F("Resets {0}", PlanUsage.Until(r));
            }
            else colour = Palette.Of(SessionState.Idle);
            var session = x.Windows.FirstOrDefault(w => w.Key == "session");
            var wk = x.Windows.FirstOrDefault(w => w.Key == "week");
            fiveH = session != null ? F("5H {0:0}%", session.Used * 100) : "";
            week = wk != null ? F("W {0:0}%", wk.Used * 100) : "";
            // (days until the week resets: .1D to 6.9D)
            if (wk?.Resets is { } wr) days = F("{0:#.0}D", Math.Max(0.1, (wr - now).TotalDays));
            tip = string.Join("\n", x.Windows.Select(w => $"{w.Label}: {w.Used * 100:0}%" + (w.Resets is { } r ? " · " + F("resets {0}", PlanUsage.When(r)) : "")));
        }

        bool isOpen = open.Contains(a.Id);
        var weight = current ? FontWeights.SemiBold : FontWeights.Normal;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // (the same widths on every line)
        foreach (var g in new[] { "H", "W", "D" }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "AccountUsage" + g });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // the arrow opens its windows; the rest of the line picks it
        var arrow = new TextBlock { Text = isOpen ? "▾" : "▸", FontSize = 13, Padding = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = T("Its usage") };
        arrow.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!open.Remove(a.Id)) open.Add(a.Id);
            Render();
        };
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Palette.Brush(colour), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = a.Label, FontSize = current ? 14.5 : 13, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Put(grid, arrow, 0);
        Put(grid, dot, 1);
        Put(grid, name, 2);
        int col = 3;
        foreach (var text in new[] { fiveH, week, days })
        {
            var t = new TextBlock { Text = text, FontSize = current ? 13 : 12.5, FontWeight = weight, Margin = new Thickness(10, 0, 0, 0), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Brush(colour) };
            if (col == 5 && text.Length > 0) t.ToolTip = T("Days until the week resets");
            Put(grid, t, col++);
        }
        if (state != null)
        {
            var st = new TextBlock { Text = state, FontSize = 11.5, Foreground = Palette.Brush(colour), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(st, 1);
            Grid.SetColumn(st, 2);
            Grid.SetColumnSpan(st, 4);
            grid.Children.Add(st);
        }
        var line = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(4), Padding = new Thickness(2, 3, 6, 3), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent,
            Cursor = current ? Cursors.Arrow : Cursors.Hand,
            ToolTip = a.Description + (a.Id == settings.ActiveAccount ? "\n" + T("Claude Code's own sign-in on this PC") : "") + "\n" + tip,
        };
        if (current) line.SetResourceReference(Border.BackgroundProperty, "RowSelectedBrush");
        line.MouseLeftButtonUp += (_, _) => { if (!current) Picked?.Invoke(a.Id); };
        System.Windows.Automation.AutomationProperties.SetName(line, a.Label);
        Children.Add(line);
        if (!isOpen) return;

        var details = new StackPanel { Margin = new Thickness(16, 4, 0, 6) };
        if (u is not { } d)
        {
            details.Children.Add(Small(T("Not read yet")));
        }
        else
        {
            foreach (var w in d.Windows)
            {
                var m = new Meter(w.Label) { Margin = new Thickness(0, 0, 0, 6) };
                string left = w.Resets is { } r ? " · " + PlanUsage.Until(r) : "";
                m.Set(new[] { (w.Used, UsagePanel.Colour(w.Used), F("{0:0}% used", w.Used * 100)) }, F("{0:0}%", w.Used * 100) + left,
                    w.Resets is { } rr ? F("Resets {0}", PlanUsage.When(rr)) : null);
                details.Children.Add(m);
                if (w.Resets is { } at) details.Children.Add(Small(F("Resets {0}", PlanUsage.When(at)), top: -4));
                // (Claude Code's own: its pace, from the readings kept)
                if (a.Id == settings.ActiveAccount && w.Key is "session" or "week" && usage.Pace(w) is { PerHour: > 0.001 } p)
                    details.Children.Add(Small(p.Full is { } f ? F("+{0:0.#}% an hour · full {1}", p.PerHour * 100, PlanUsage.When(f)) : F("+{0:0.#}% an hour", p.PerHour * 100)));
            }
            if (d.Blocked) details.Children.Add(Small(d.Until is { } b ? F("Limit reached: back {0}", PlanUsage.When(b)) : T("Limit reached")));
            if (d.At is { } c) details.Children.Add(Small(F("Read {0}", PlanUsage.When(c)), top: 4));
        }
        Children.Add(details);
    }

    private static void Put(Grid grid, UIElement e, int column)
    {
        Grid.SetColumn(e, column);
        grid.Children.Add(e);
    }

    private static TextBlock Small(string text, double top = 0)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }
}
