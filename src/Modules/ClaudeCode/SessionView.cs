using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>
/// One session tab. Left: the conversation (your messages, Claude's text as it streams, each tool call as a card with
/// its permission prompt) and the box to write in. Right: the dashboard, live while it works: the status orb and what
/// it's doing, its speed, the context window filling, the tokens by kind and the cost, your plan's usage, the turn's
/// activity ribbon, the files it changed, and the session's folder, model and permission mode.
/// </summary>
internal sealed class SessionView : UserControl
{
    public SessionInfo Info { get; }
    private readonly ClaudeCodeSettings settings;
    private readonly Func<string?> findExe;
    private readonly PlanUsage usage;
    private ClaudeProcess? proc;
    private DateTime limitNoted;

    /// <summary>The state changed (for the tab's dot).</summary>
    public event Action? StateChanged;
    /// <summary>Claude Code renamed the session (or confirmed a rename).</summary>
    public event Action? Renamed;

    public SessionState State { get; private set; } = SessionState.Offline;
    private string doing = "";
    public bool Busy => State is SessionState.Starting or SessionState.Thinking or SessionState.Writing or SessionState.Tool or SessionState.Waiting;

    // the conversation
    private readonly StackPanel chat = new() { Margin = new Thickness(0, 0, 12, 0) };
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 132, MaxHeight = 420, FontSize = 15.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8) };
    private readonly Button send = new() { MinWidth = 90, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button stop = new() { MinWidth = 80, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border startPanel = new();
    private readonly Dictionary<int, object> blocks = new();
    private readonly Dictionary<string, ToolCard> cards = new();
    private readonly HashSet<string> streamed = new();
    private ThinkingChip? lastChip;
    private bool autoAllow;
    private int silentTurns;
    private bool historyLoaded;

    // the dashboard
    private readonly StatusOrb orb = new() { Width = 64, Height = 64 };
    private readonly TextBlock stateText = new() { FontSize = 19, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock doingText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
    private readonly TextBlock clock = new() { FontSize = 13.5 };
    private readonly TextBlock speed = new() { FontSize = 24, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock speedCaption = new() { FontSize = 12.5 };
    private readonly RingGauge ring = new() { Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Meter tokens = new(T("Tokens this session"));
    private readonly TextBlock cost = new() { FontSize = 13.5, Margin = new Thickness(0, -6, 0, 10) };
    private readonly Meter fiveHour = new(T("Plan: this 5 hours"));
    private readonly Meter sevenDay = new(T("Plan: this week"));
    private readonly ActivityRibbon ribbon = new();
    private readonly StackPanel files = new();
    private readonly ComboBox model = new();
    private readonly ComboBox mode = new();
    private readonly TextBlock folderText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Cursor = Cursors.Hand };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private DateTime? turnStart;
    private long turnOutput, messageOutput, context;
    private double processCost;
    private string modelId = "";
    private readonly Queue<(DateTime At, int Chars)> recent = new();
    private readonly Dictionary<string, int> changed = new(StringComparer.OrdinalIgnoreCase);

    public SessionView(SessionInfo info, ClaudeCodeSettings settings, Func<string?> findExe, PlanUsage usage)
    {
        Info = info;
        this.usage = usage;
        this.settings = settings;
        this.findExe = findExe;

        // ---- the conversation and the box
        scroller.Content = chat;
        send.Content = T("Send");
        send.SetResourceReference(StyleProperty, "AccentButton");
        send.Click += (_, _) => SendInput();
        stop.Content = T("Stop");
        stop.ToolTip = T("Stop what Claude is doing (Esc)");
        stop.Click += (_, _) => Interrupt();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; SendInput(); }
            else if (e.Key == Key.Escape && Busy) { e.Handled = true; Interrupt(); }
        };
        input.ToolTip = T("Enter sends, Shift+Enter starts a new line. Slash commands work too (/compact, /rename…).");
        var inputRow = new DockPanel { Margin = new Thickness(0, 10, 12, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Children.Add(stop);
        buttons.Children.Add(send);
        DockPanel.SetDock(buttons, Dock.Right);
        inputRow.Children.Add(buttons);
        inputRow.Children.Add(input);
        var left = new DockPanel();
        DockPanel.SetDock(startPanel, Dock.Top);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        left.Children.Add(startPanel);
        left.Children.Add(inputRow);
        left.Children.Add(scroller);

        // ---- the dashboard
        var dash = new StackPanel();
        var statusRow = new DockPanel();
        DockPanel.SetDock(orb, Dock.Left);
        statusRow.Children.Add(orb);
        var statusText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        statusText.Children.Add(stateText);
        doingText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        clock.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        statusText.Children.Add(doingText);
        statusText.Children.Add(clock);
        statusRow.Children.Add(statusText);
        dash.Children.Add(Card(statusRow));

        var speedStack = new StackPanel();
        speedStack.Children.Add(speed);
        speedCaption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        speedStack.Children.Add(speedCaption);
        speedStack.Children.Add(new TextBlock { Text = T("This turn"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7 });
        speedStack.Children.Add(ribbon);
        dash.Children.Add(Card(speedStack, T("Activity")));

        var usageCard = new StackPanel();
        usageCard.Children.Add(ring);
        usageCard.Children.Add(new Border { Height = 10 });
        usageCard.Children.Add(tokens);
        cost.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        usageCard.Children.Add(cost);
        usageCard.Children.Add(fiveHour);
        usageCard.Children.Add(sevenDay);
        dash.Children.Add(Card(usageCard, T("Usage")));

        dash.Children.Add(Card(files, T("Files changed")));
        dash.Children.Add(Card(SessionSettings(), T("Session")));
        var dashScroll = new ScrollViewer { Content = dash, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        grid.Children.Add(left);
        Grid.SetColumn(dashScroll, 1);
        grid.Children.Add(dashScroll);
        Content = grid;

        tick.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            if (!historyLoaded) { historyLoaded = true; _ = LoadHistoryAsync(); }
            input.Focus();
        };
        BuildStartPanel();
        ShowTotals();
        ShowFiles();
        SetState(SessionState.Offline, "");
        ring.Set(0, "–", T("context"));
        usage.Changed += ShowPlan;
        Look.Changed += ApplyLook;
        settings.Changed += OptionsChanged;
        ApplyLook();
        ShowPlan();
    }

    // ---------------------------------------------------------------- sending

    private void SendInput()
    {
        string text = input.Text.Trim();
        if (text.Length == 0) return;
        input.Clear();
        Send(text, bubble: true);
    }

    /// <summary>A message (or a slash command) to Claude; the process starts if it isn't running.</summary>
    public void Send(string text, bool bubble)
    {
        if (!EnsureProcess()) return;
        if (bubble) Add(new UserBubble(text, queued: Busy));
        if (!Busy)
        {
            turnStart = DateTime.UtcNow;
            turnOutput = messageOutput = 0;
            ribbon.Clear();
            SetState(SessionState.Thinking, T("Sending…"));
        }
        proc!.Send(text);
        if (!Info.Started)
        {
            Info.Started = true;
            startPanel.Visibility = Visibility.Collapsed;
            settings.LastFolder = Info.Folder;
            settings.Save();
        }
        ScrollToEnd(force: true);
    }

    /// <summary>Renames the session: at once if it's running (/rename), else at its next start (--name).</summary>
    public void Rename(string name)
    {
        Info.Name = name;
        settings.Save();
        if (proc is { Running: true })
        {
            silentTurns++;
            proc.Send("/rename " + name);
        }
    }

    private void Interrupt()
    {
        if (proc is not { Running: true }) return;
        proc.Interrupt();
        SetState(State, T("Stopping…"));
    }

    private bool EnsureProcess()
    {
        if (proc is { Running: true }) return true;
        string? exe = findExe();
        if (exe == null)
        {
            Add(new NoteLine(F("Claude Code isn't installed (claude.exe wasn't found). Install it ({0}), sign in, and send again.", ClaudeCli.InstallUrl), error: true));
            return false;
        }
        if (!Directory.Exists(Info.Folder))
        {
            Add(new NoteLine(F("The folder {0} doesn't exist: pick another above.", Info.Folder), error: true));
            startPanel.Visibility = Visibility.Visible;
            return false;
        }
        try
        {
            proc = ClaudeProcess.Start(exe, Info, Dispatcher, settings.SkipPermissions);
            processCost = 0;
            var me = proc;
            proc.Message += OnMessage;
            proc.Exited += tail => OnExited(me, tail);
            SetState(SessionState.Starting, T("Starting Claude Code…"));
            return true;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Starting Claude Code", ex);
            Add(new NoteLine(F("Claude Code couldn't start: {0}", ex.Message), error: true));
            return false;
        }
    }

    private void OnExited(ClaudeProcess which, string tail)
    {
        if (proc != which) return;
        proc = null;
        bool wasBusy = Busy;
        foreach (var c in cards.Values.Where(c => !c.Finished)) c.Denied(T("Claude Code stopped"));
        if (wasBusy || tail.Length > 0)
        {
            string why = tail.Length > 0 ? tail.Split('\n').Last(l => l.Trim().Length > 0) : T("it closed");
            bool login = tail.Contains("login", StringComparison.OrdinalIgnoreCase) || tail.Contains("auth", StringComparison.OrdinalIgnoreCase);
            Add(new NoteLine(F("Claude Code stopped: {0}", why) + (login ? " " + T("Sign in at the top of the tab, then send again.") : ""), error: true));
        }
        ribbon.End();
        SetState(SessionState.Offline, "");
    }

    // ---------------------------------------------------------------- what Claude Code says

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    private static double Dbl(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static JsonElement Obj(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private void OnMessage(JsonElement e)
    {
        try
        {
            string? parent = Str(e, "parent_tool_use_id");
            switch (Str(e, "type"))
            {
                case "stream_event": if (parent == null) Stream(Obj(e, "event")); break;
                case "assistant": Assistant(Obj(e, "message"), parent); break;
                case "user": if (parent == null) ToolResults(Obj(e, "message")); break;
                case "system": SystemMessage(e); break;
                case "result": Result(e); break;
                case "control_request": ControlRequest(e); break;
                case "rate_limit_event": RateLimit(Obj(e, "rate_limit_info")); break;
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Claude Code message", ex); }
    }

    private void Stream(JsonElement ev)
    {
        switch (Str(ev, "type"))
        {
            case "message_start":
                {
                    var m = Obj(ev, "message");
                    if (Str(m, "id") is { } id) streamed.Add(id);
                    if (Str(m, "model") is { Length: > 0 } mid && mid != "<synthetic>") modelId = mid;
                    turnOutput += messageOutput;
                    messageOutput = 0;
                    blocks.Clear();
                    Context(Obj(m, "usage"));
                    SetState(SessionState.Thinking, T("Asking Claude…"));
                    ribbon.Begin(Palette.Of(SessionState.Thinking), T("Waiting for Claude"));
                    break;
                }
            case "content_block_start":
                {
                    int index = (int)Num(ev, "index");
                    var block = Obj(ev, "content_block");
                    switch (Str(block, "type"))
                    {
                        case "text":
                            {
                                var t = new AssistantText();
                                Add(t);
                                blocks[index] = t;
                                SetState(SessionState.Writing, T("Writing"));
                                ribbon.Begin(Palette.Of(SessionState.Writing), T("Writing"));
                                break;
                            }
                        case "thinking":
                            {
                                var chip = new ThinkingChip();
                                Add(chip);
                                blocks[index] = lastChip = chip;
                                SetState(SessionState.Thinking, T("Thinking"));
                                ribbon.Begin(Palette.Of(SessionState.Thinking), T("Thinking"));
                                break;
                            }
                        case "tool_use":
                            {
                                string id = Str(block, "id") ?? Guid.NewGuid().ToString();
                                string name = Str(block, "name") ?? "Tool";
                                var card = new ToolCard(id, name) { Root = Info.Folder };
                                cards[id] = card;
                                Add(card);
                                blocks[index] = card;
                                SetState(SessionState.Tool, F("Getting {0} ready", name));
                                ribbon.Begin(Palette.Of(Palette.KindOf(name)), name);
                                break;
                            }
                    }
                    break;
                }
            case "content_block_delta":
                {
                    int index = (int)Num(ev, "index");
                    var d = Obj(ev, "delta");
                    blocks.TryGetValue(index, out var target);
                    switch (Str(d, "type"))
                    {
                        case "text_delta":
                            if (target is AssistantText t && Str(d, "text") is { } more) { t.Append(more); Count(more.Length); ScrollToEnd(); }
                            break;
                        case "input_json_delta":
                            if (target is ToolCard c && Str(d, "partial_json") is { } part) { c.Partial(part.Length); Count(part.Length); }
                            break;
                        case "thinking_delta":
                            if (Str(d, "thinking") is { } th) Count(th.Length);
                            break;
                    }
                    break;
                }
            case "content_block_stop":
                {
                    if (blocks.TryGetValue((int)Num(ev, "index"), out var target))
                    {
                        if (target is AssistantText t) t.Finish();
                        else if (target is ThinkingChip chip) chip.Done();
                    }
                    break;
                }
            case "message_delta":
                {
                    var u = Obj(ev, "usage");
                    messageOutput = Num(u, "output_tokens");
                    Context(u);
                    break;
                }
        }
    }

    private void Assistant(JsonElement m, string? parent)
    {
        string? id = Str(m, "id");
        bool synthetic = Str(m, "model") == "<synthetic>";
        if (!Obj(m, "content").ValueKind.Equals(JsonValueKind.Array)) return;
        foreach (var b in Obj(m, "content").EnumerateArray())
        {
            string type = Str(b, "type") ?? "";
            if (parent != null)
            {
                // a step of an agent Claude started: a line under its card
                if (type == "tool_use" && cards.TryGetValue(parent, out var owner)) owner.SubStep(Str(b, "name") ?? "", ToolCard.Summary(Str(b, "name") ?? "", Obj(b, "input")));
                continue;
            }
            if (type == "tool_use")
            {
                string tid = Str(b, "id") ?? "";
                if (!cards.TryGetValue(tid, out var card))
                {
                    card = new ToolCard(tid, Str(b, "name") ?? "Tool") { Root = Info.Folder };
                    cards[tid] = card;
                    Add(card);
                }
                card.SetInput(Obj(b, "input"));
                SetState(SessionState.Tool, F("{0}: {1}", card.Tool, card.Summary()));
            }
            else if (type == "text" && (id == null || !streamed.Contains(id)) && !(synthetic && silentTurns > 0))
            {
                var t = new AssistantText();
                t.Finish(Str(b, "text") ?? "");
                Add(t);
            }
        }
        ScrollToEnd();
    }

    private void ToolResults(JsonElement m)
    {
        if (Obj(m, "content") is not { ValueKind: JsonValueKind.Array } content) return;
        foreach (var b in content.EnumerateArray())
        {
            if (Str(b, "type") != "tool_result" || Str(b, "tool_use_id") is not { } id || !cards.TryGetValue(id, out var card)) continue;
            bool error = Obj(b, "is_error").ValueKind == JsonValueKind.True;
            card.SetResult(ResultText(Obj(b, "content")), error);
            if (!error && card.Changed && Str(card.Input, "file_path") is { } file)
            {
                changed[file] = changed.GetValueOrDefault(file) + 1;
                ShowFiles();
            }
            ribbon.End();
            SetState(SessionState.Thinking, T("Reading the result"));
        }
    }

    private static string ResultText(JsonElement c) => c.ValueKind switch
    {
        JsonValueKind.String => c.GetString() ?? "",
        JsonValueKind.Array => string.Join("\n", c.EnumerateArray().Select(x => Str(x, "type") == "text" ? Str(x, "text") ?? "" : "[" + (Str(x, "type") ?? "") + "]")),
        _ => "",
    };

    private void SystemMessage(JsonElement e)
    {
        switch (Str(e, "subtype"))
        {
            case "init":
                if (Str(e, "model") is { Length: > 0 } m) modelId = m;
                if (Info.Teleport != null && Str(e, "session_id") is { Length: > 0 } sid)
                {
                    // brought here: an ordinary session of this tab from now on
                    Info.Teleport = null;
                    Info.Id = sid;
                    Info.Started = true;
                    settings.Save();
                    Add(new NoteLine(T("It's here: write below to carry on."), icon: "\u2714"));
                    _ = LoadHistoryAsync();
                    if (State == SessionState.Starting) SetState(SessionState.Idle, "");
                }
                if (State == SessionState.Starting) SetState(SessionState.Thinking, T("Connected"));
                break;
            case "status":
                if (Str(e, "status") == "compacting") SetState(SessionState.Thinking, T("Compacting the conversation"));
                else if (Str(e, "status") == "requesting" && Busy) SetState(SessionState.Thinking, T("Asking Claude…"));
                break;
            case "thinking_tokens":
                lastChip?.Tokens((int)Num(e, "estimated_tokens"));
                break;
            case "session_title_changed":
                if (Str(e, "title") is { Length: > 0 } title && title != Info.Name) { Info.Name = title; settings.Save(); }
                Renamed?.Invoke();
                break;
            case "permission_denied":
                if (Str(e, "tool_use_id") is { } id && cards.TryGetValue(id, out var card)) card.Denied(Str(e, "message") ?? "");
                break;
            case "compact_boundary":
                Add(new NoteLine(T("The conversation was compacted (summarised) to make room."), icon: "✂"));
                break;
        }
    }

    private void Result(JsonElement e)
    {
        if (silentTurns > 0 && Num(e, "num_turns") == 0) { silentTurns--; if (!cards.Values.Any(c => !c.Finished)) SetState(SessionState.Idle, ""); return; }
        ribbon.End();
        var u = Obj(e, "usage");
        Info.InputTokens += Num(u, "input_tokens");
        Info.OutputTokens += Num(u, "output_tokens");
        Info.CacheReadTokens += Num(u, "cache_read_input_tokens");
        Info.CacheWriteTokens += Num(u, "cache_creation_input_tokens");
        double total = Dbl(e, "total_cost_usd");
        Info.CostUsd += Math.Max(0, total - processCost);
        processCost = total;
        Info.Turns++;
        settings.Save();
        ShowTotals();
        double seconds = turnStart is { } s ? (DateTime.UtcNow - s).TotalSeconds : Dbl(e, "duration_ms") / 1000;
        bool error = Obj(e, "is_error").ValueKind == JsonValueKind.True || (Str(e, "subtype") ?? "success") != "success";
        string line = F("Done in {0:0.0} s · {1} steps · {2} tokens out", seconds, Num(e, "num_turns"), Palette.Tokens(Num(u, "output_tokens")));
        if (error) Add(new NoteLine((Str(e, "result") is { Length: > 0 } r ? r : Str(e, "subtype") ?? T("Something went wrong")), error: true));
        Add(new NoteLine(line, icon: error ? "⚠" : "✔"));
        turnStart = null;
        foreach (var c in cards.Values.Where(c => !c.Finished && c.Started < DateTime.UtcNow.AddSeconds(-1))) c.SetResult("", false);
        SetState(error ? SessionState.Error : SessionState.Idle, "");
        ScrollToEnd();
    }

    private void ControlRequest(JsonElement e)
    {
        string requestId = Str(e, "request_id") ?? "";
        var r = Obj(e, "request");
        if (Str(r, "subtype") != "can_use_tool") return;
        string tool = Str(r, "tool_name") ?? "Tool";
        var toolInput = Obj(r, "input").Clone();
        // (never asking: Settings → Claude Code; or Allow all this session)
        if (autoAllow || settings.SkipPermissions) { proc?.Answer(requestId, true, toolInput); return; }
        string? id = Str(r, "tool_use_id");
        ToolCard? card = id != null && cards.TryGetValue(id, out var c) ? c : cards.Values.LastOrDefault(x => x.Tool == tool && !x.Finished);
        if (card == null)
        {
            card = new ToolCard(id ?? Guid.NewGuid().ToString(), tool) { Root = Info.Folder };
            cards[card.Id] = card;
            Add(card);
            card.SetInput(toolInput);
        }
        string what = ToolCard.Summary(tool, toolInput, Info.Folder);
        string question = Palette.KindOf(tool) switch
        {
            Palette.ToolKind.Change => F("Let Claude change {0}?", what),
            Palette.ToolKind.Run => F("Let Claude run this? {0}", what),
            Palette.ToolKind.Web => F("Let Claude go to {0}?", what),
            _ => F("Let Claude use {0}? {1}", tool, what),
        };
        var me = proc;
        card.Ask(question, (allow, all) =>
        {
            if (all) autoAllow = true;
            me?.Answer(requestId, allow, toolInput, allow ? null : T("The user said no to this. Ask them what they'd like instead."));
            SetState(allow ? SessionState.Tool : SessionState.Thinking, allow ? F("{0}: {1}", tool, what) : T("Told no"));
        });
        SetState(SessionState.Waiting, F("Asking you: {0}", tool));
        ScrollToEnd(force: true);
        // (a message in the tab's dot and the title bar, if the window isn't in front)
        if (Window.GetWindow(this) is { IsActive: false } w) FlashWindow(w);
    }

    /// <summary>Each request's rate-limit report: to the plan's usage (shared by every tab, PlanUsage).</summary>
    private void RateLimit(JsonElement info)
    {
        usage.FromRateLimit(info);
        if (Str(info, "status") == "rejected" && DateTime.Now - limitNoted > TimeSpan.FromMinutes(5))
        {
            limitNoted = DateTime.Now;
            var back = usage.BlockedUntil;
            Add(new NoteLine(back is { } b ? F("Your plan's limit is reached: it resets {0} ({1}). You'll get a notification when it has.", PlanUsage.When(b), PlanUsage.Until(b)) : T("Your plan's limit is reached."), error: true));
        }
    }

    private readonly TextBlock skipNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>Settings → Claude Code changed: never asking (switched at once in a running session), and the look.</summary>
    private void OptionsChanged()
    {
        ShowSkip();
        if (proc is { Running: true })
            proc.Control("set_permission_mode", new() { ["mode"] = settings.SkipPermissions ? "bypassPermissions" : Info.Mode });
        // a question waiting now is answered too
        if (settings.SkipPermissions)
            foreach (var c in cards.Values.Where(c => c.Asking)) c.AnswerNow(true);
    }

    private void ShowSkip()
    {
        skipNote.Visibility = settings.SkipPermissions ? Visibility.Visible : Visibility.Collapsed;
        mode.IsEnabled = !settings.SkipPermissions;
        mode.ToolTip = settings.SkipPermissions ? T("Permission prompts are off in Settings → Claude Code") : null;
    }

    /// <summary>The conversation's colours and sizes, and the box's, from the look (Settings → Claude Code).</summary>
    private void ApplyLook()
    {
        var a = Look.A;
        scroller.Background = Look.Brush(a.ChatBackground) ?? Brushes.Transparent;
        chat.Margin = Look.Brush(a.ChatBackground) != null ? new Thickness(12, 8, 12, 0) : new Thickness(0, 0, 12, 0);
        input.FontFamily = Look.Font;
        input.FontSize = Math.Clamp(a.InputFontSize, 8, 40);
        double line = input.FontSize * 1.35;
        int rows = Math.Clamp(a.InputRows, 1, 40);
        input.MinHeight = rows * line + input.Padding.Top + input.Padding.Bottom + 4;
        input.MaxHeight = Math.Max(input.MinHeight, rows * 2 * line + input.Padding.Top + input.Padding.Bottom + 4);
        if (Look.Brush(a.InputBackground) is { } ib) input.Background = ib; else input.ClearValue(BackgroundProperty);
        if (Look.Brush(a.InputText) is { } it) input.Foreground = it; else input.ClearValue(ForegroundProperty);
        foreach (var item in chat.Children.OfType<ILooked>()) item.ApplyLook();
    }

    /// <summary>The dashboard's plan meters, from the plan's usage (with the time to each reset).</summary>
    private void ShowPlan()
    {
        void Show(Meter meter, PlanUsage.Window? w)
        {
            if (w == null) { meter.Set(Array.Empty<(double, Color, string)>(), "–"); return; }
            string left = w.Resets is { } r ? " · " + PlanUsage.Until(r) : "";
            meter.Set(new[] { (w.Used, UsagePanel.Colour(w.Used), F("{0:0}% used", w.Used * 100)) }, F("{0:0}%", w.Used * 100) + left,
                w.Resets is { } rr ? F("Resets {0}", PlanUsage.When(rr)) : null);
        }
        Show(fiveHour, usage.Session);
        Show(sevenDay, usage.Week);
    }

    // ---------------------------------------------------------------- the dashboard

    private void SetState(SessionState state, string what)
    {
        bool changedState = state != State;
        State = state;
        doing = what;
        orb.State = state;
        stateText.Text = state switch
        {
            SessionState.Starting => T("Starting"),
            SessionState.Thinking => T("Thinking"),
            SessionState.Writing => T("Writing"),
            SessionState.Tool => T("Working"),
            SessionState.Waiting => T("Needs you"),
            SessionState.Error => T("Stopped with an error"),
            SessionState.Offline => Info.Started ? T("Not running") : T("Ready"),
            _ => T("Done"),
        };
        stateText.Foreground = Palette.Brush(Palette.Of(state));
        doingText.Text = what;
        stop.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
        if (Busy) tick.Start();
        Tick();
        if (changedState) StateChanged?.Invoke();
    }

    private void Count(int chars) => recent.Enqueue((DateTime.UtcNow, chars));

    private void Tick()
    {
        var now = DateTime.UtcNow;
        while (recent.Count > 0 && now - recent.Peek().At > TimeSpan.FromSeconds(3)) recent.Dequeue();
        double rate = recent.Sum(r => r.Chars) / 4.0 / 3;
        speed.Text = Busy && rate > 0.5 ? F("{0:0} tokens/s", rate) : "–";
        speedCaption.Text = F("{0} tokens out this turn", Palette.Tokens(turnOutput + messageOutput));
        clock.Text = turnStart is { } s && Busy ? F("{0:0} s", (now - s).TotalSeconds) : "";
        if (!Busy) tick.Stop();
    }

    private void Context(JsonElement usage)
    {
        long used = Num(usage, "input_tokens") + Num(usage, "cache_read_input_tokens") + Num(usage, "cache_creation_input_tokens");
        if (used <= 0) return;
        context = used;
        long limit = modelId.Contains("1m", StringComparison.OrdinalIgnoreCase) || used > 200_000 ? 1_000_000 : 200_000;
        ring.Set((double)used / limit, Palette.Tokens(used), F("of {0} context", Palette.Tokens(limit)));
        ring.ToolTip = F("{0:N0} tokens of the {1:N0} the model can hold: what Claude reads each step (the conversation, files, results). Near full, Claude Code compacts it.", used, limit);
    }

    private void ShowTotals()
    {
        double sum = Info.InputTokens + Info.OutputTokens + Info.CacheReadTokens + Info.CacheWriteTokens, all = Math.Max(1, sum);
        tokens.Set(new[]
        {
            (Info.CacheReadTokens / all, Color.FromRgb(0x39, 0xC5, 0xCF), F("Read from the cache: {0}", Palette.Tokens(Info.CacheReadTokens))),
            (Info.CacheWriteTokens / all, Color.FromRgb(0x58, 0xA6, 0xFF), F("Written to the cache: {0}", Palette.Tokens(Info.CacheWriteTokens))),
            (Info.InputTokens / all, Color.FromRgb(0xA3, 0x71, 0xF7), F("Sent fresh: {0}", Palette.Tokens(Info.InputTokens))),
            (Info.OutputTokens / all, Color.FromRgb(0xF0, 0x88, 0x3E), F("Written by Claude: {0}", Palette.Tokens(Info.OutputTokens))),
        }, Palette.Tokens(sum), T("Cache read (teal), cache written (blue), sent fresh (purple), written by Claude (orange)"));
        cost.Text = F("{0} turns · about ${1} at API prices", Info.Turns, Info.CostUsd.ToString(Info.CostUsd < 0.1 ? "0.000" : "0.00", System.Globalization.CultureInfo.CurrentCulture));
        cost.ToolTip = T("What these tokens would cost on the API: with a Claude plan, your plan's usage (below) is what counts");
    }

    private void ShowFiles()
    {
        files.Children.Clear();
        if (changed.Count == 0) { files.Children.Add(new TextBlock { Text = T("None yet"), FontSize = 13.5, Opacity = 0.6 }); return; }
        foreach (var (file, times) in changed.OrderBy(f => f.Key))
        {
            var t = new TextBlock { FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand, ToolTip = F("{0} (changed {1}×): click to show it in Explorer", file, times), Margin = new Thickness(0, 1, 0, 1) };
            t.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = Palette.Brush(Palette.Of(Palette.ToolKind.Change)) });
            t.Inlines.Add(Path.GetFileName(file) + (times > 1 ? $" ×{times}" : ""));
            t.MouseLeftButtonUp += (_, _) => { try { Process.Start("explorer.exe", $"/select,\"{file}\""); } catch { } };
            files.Children.Add(t);
        }
    }

    private UIElement SessionSettings()
    {
        var s = new StackPanel();
        folderText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        folderText.ToolTip = T("Open the folder");
        folderText.MouseLeftButtonUp += (_, _) => { if (Directory.Exists(Info.Folder)) Process.Start("explorer.exe", $"\"{Info.Folder}\""); };
        s.Children.Add(folderText);
        ShowFolder();

        foreach (var (value, name) in new[] { ("", T("Claude Code's default")), ("opus", "Opus"), ("sonnet", "Sonnet"), ("haiku", "Haiku"), ("fable", "Fable") })
            model.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        model.SelectedItem = model.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Model) ?? model.Items[0];
        model.SelectionChanged += (_, _) =>
        {
            if (model.SelectedItem is not ComboBoxItem { Tag: string v } || v == Info.Model) return;
            Info.Model = settings.Model = v;
            settings.Save();
            proc?.Control("set_model", new() { ["model"] = v.Length > 0 ? v : null });
        };
        foreach (var (value, name) in new[] { ("manual", T("Ask before acting")), ("acceptEdits", T("Accept edits, ask the rest")), ("auto", T("Auto")), ("plan", T("Plan only (no changes)")), ("bypassPermissions", T("Don't ask (bypass permissions)")) })
            mode.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Mode) ?? mode.Items[0];
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not ComboBoxItem { Tag: string v } || v == Info.Mode) return;
            Info.Mode = settings.Mode = v;
            autoAllow = false;
            settings.Save();
            proc?.Control("set_permission_mode", new() { ["mode"] = v });
        };
        s.Children.Add(Labelled(T("Model"), model));
        s.Children.Add(Labelled(T("Permissions"), mode));
        skipNote.Text = T("Permission prompts are off (Settings → Claude Code): Claude does everything without asking.");
        skipNote.Foreground = Palette.Brush(Palette.Of(SessionState.Error));
        s.Children.Add(skipNote);
        ShowSkip();

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var compact = new Button { Content = T("Compact"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("Summarise the conversation so far to free up the context (/compact)") };
        compact.Click += (_, _) => Send("/compact", bubble: true);
        var copy = new Button { Content = T("Copy session id"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = F("{0}: claude --resume {0} opens it in a terminal", Info.Id) };
        copy.Click += (_, _) => { try { Clipboard.SetText(Info.Id); } catch { } };
        var end = new Button { Content = T("End process"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("Close Claude Code for this tab (the session is kept: sending again resumes it)") };
        end.Click += (_, _) => { var p = proc; proc = null; p?.Dispose(); SetState(SessionState.Offline, ""); };
        var export = new Button { Content = T("Export…"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("Save the session as a file, to carry on with it on another PC (Import a session file… in a new tab there)") };
        export.Click += (_, _) => ExportFile();
        actions.Children.Add(compact);
        actions.Children.Add(export);
        actions.Children.Add(copy);
        actions.Children.Add(end);
        s.Children.Add(actions);
        return s;
    }

    private void ShowFolder() => folderText.Text = Info.Folder.Length > 0 ? Info.Folder : T("(no folder)");

    // ---------------------------------------------------------------- a new tab: the folder, or a saved session

    private void BuildStartPanel()
    {
        startPanel.Visibility = Info.Started ? Visibility.Collapsed : Visibility.Visible;
        startPanel.SetResourceReference(StyleProperty, "Card");
        startPanel.Margin = new Thickness(0, 0, 12, 10);
        var s = new StackPanel();
        var head = new TextBlock { Text = T("Where should Claude work?") };
        head.SetResourceReference(StyleProperty, "CardHeader");
        s.Children.Add(head);
        var folder = new TextBox { Text = Info.Folder, Margin = new Thickness(0, 6, 0, 0) };
        var browse = new Button { Content = T("Browse…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 6, 0, 0) };
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(folder);
        s.Children.Add(row);
        var savedHead = new TextBlock { Margin = new Thickness(0, 10, 0, 4), FontSize = 13.5 };
        savedHead.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var saved = new StackPanel();
        s.Children.Add(savedHead);
        s.Children.Add(new ScrollViewer { Content = saved, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var otherHead = new TextBlock { Text = T("Or open one by its id, from the web or your phone, or from another PC:"), Margin = new Thickness(0, 12, 0, 4), FontSize = 13.5 };
        otherHead.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(otherHead);
        var reference = new TextBox { ToolTip = T("The id Claude Code gave you when it closed (claude --resume <id>, or just the id); a claude.ai/code link or session id from the web or the mobile app (brought here with --teleport)") };
        var open = new Button { Content = T("Open"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        var import = new Button { Content = T("Import a session file…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("A session exported on another PC (Session → Export…)") };
        var refRow = new DockPanel();
        DockPanel.SetDock(import, Dock.Right);
        DockPanel.SetDock(open, Dock.Right);
        refRow.Children.Add(import);
        refRow.Children.Add(open);
        refRow.Children.Add(reference);
        s.Children.Add(refRow);
        var refStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 4, 0, 0) };
        s.Children.Add(refStatus);
        void Tell(string text, bool error)
        {
            refStatus.Text = text;
            refStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
        }
        void OpenReference()
        {
            var (local, remote) = ClaudeCli.ParseReference(reference.Text);
            if (local != null) { if (OpenById(local) is { } why) Tell(why, true); }
            else if (remote != null) BringFromWeb(remote);
            else Tell(T("That isn't a session id or a claude.ai/code link."), true);
        }
        open.Click += (_, _) => OpenReference();
        reference.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenReference(); };
        import.Click += (_, _) => { if (ImportFile() is { } why) Tell(why, true); };

        var hint = new TextBlock { Text = T("Then write below what you'd like done. Claude Code works in that folder: it reads, edits and runs things there, asking first as the permissions say."), TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(hint);
        startPanel.Child = s;

        void Use(string path)
        {
            Info.Folder = path;
            settings.LastFolder = path;
            settings.Save();
            ShowFolder();
            saved.Children.Clear();
            var list = Directory.Exists(path) ? ClaudeCli.SavedSessions(path) : new();
            savedHead.Text = list.Count == 0 ? T("No saved sessions in this folder.") : T("Or carry on with a saved session here:");
            foreach (var x in list)
            {
                var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 3), ToolTip = x.Id };
                b.Content = new TextBlock { Text = $"{x.Title}   ·   {x.When:g}", TextTrimming = TextTrimming.CharacterEllipsis };
                b.Click += (_, _) => { if (Adopt(x.Id, x.Title, null) is { } why) Tell(why, true); };
                saved.Children.Add(b);
            }
        }
        folder.LostFocus += (_, _) => { if (folder.Text.Trim() != Info.Folder) Use(folder.Text.Trim()); };
        folder.KeyDown += (_, e) => { if (e.Key == Key.Enter) Use(folder.Text.Trim()); };
        browse.Click += (_, _) =>
        {
            var d = new OpenFolderDialog { Title = T("The folder Claude works in"), InitialDirectory = Directory.Exists(Info.Folder) ? Info.Folder : "" };
            if (d.ShowDialog(Window.GetWindow(this)) == true) { folder.Text = d.FolderName; Use(d.FolderName); }
        };
        Use(Info.Folder);
    }

    /// <summary>This tab becomes an existing session (resumed at the first message, its conversation shown now); the
    /// reason it can't, or null.</summary>
    private string? Adopt(string id, string? title, string? folder)
    {
        if (settings.Sessions.FirstOrDefault(o => o != Info && o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } other)
            return F("That session is open already, in the tab {0}.", other.Name);
        var old = proc;
        proc = null;
        old?.Dispose();
        Info.Id = id;
        Info.Started = true;
        Info.Teleport = null;
        if (folder != null) { Info.Folder = folder; ShowFolder(); }
        if (title is { Length: > 0 } && (Info.Name.Length == 0 || Info.Name.StartsWith(T("Session")))) Info.Name = title.Length > 40 ? title[..40] : title;
        settings.Save();
        startPanel.Visibility = Visibility.Collapsed;
        Renamed?.Invoke();
        chat.Children.Clear();
        cards.Clear();
        _ = LoadHistoryAsync();
        SetState(SessionState.Offline, "");
        return null;
    }

    /// <summary>A session by its id (as "claude --resume" takes it): found on this PC, in the folder it worked in.</summary>
    private string? OpenById(string id)
    {
        if (ClaudeCli.FindTranscript(id) is not { } path)
            return F("There's no session {0} on this PC. From another PC: export it there (Session → Export…) and import it here. From the web or your phone: paste its claude.ai/code link.", id);
        var (folder, title) = ClaudeCli.Describe(path);
        if (folder != null && !Directory.Exists(folder))
        {
            // it worked in a folder that isn't here: carried on in this tab's folder, where Claude Code then finds it
            folder = Info.Folder;
            try { File.Copy(path, Path.Combine(Directory.CreateDirectory(ClaudeCli.ProjectFolderFor(folder)).FullName, id + ".jsonl"), true); } catch (Exception ex) { return ex.Message; }
        }
        return Adopt(id, title ?? (folder != null ? Path.GetFileName(folder.TrimEnd('\\')) : null), folder);
    }

    /// <summary>A session on claude.ai/code (the web, the mobile app) brought here: Claude Code fetches it (--teleport)
    /// when this tab starts it, in this tab's folder (a checkout of the same repository).</summary>
    private void BringFromWeb(string remote)
    {
        Info.Teleport = remote;
        Info.Started = false;
        settings.Save();
        startPanel.Visibility = Visibility.Collapsed;
        Add(new NoteLine(F("Bringing {0} here from claude.ai/code, into {1} (it should be a copy of the same repository). Claude Code may ask you to sign in to claude.ai, or to switch to the session's branch.", remote, Info.Folder), icon: "\u21E3"));
        if (EnsureProcess()) SetState(SessionState.Starting, T("Fetching the session from claude.ai/code…"));
    }

    /// <summary>A session file exported on another PC: put where this tab's folder's sessions are, and opened.</summary>
    private string? ImportFile()
    {
        var d = new OpenFileDialog { Title = T("A session exported from Claude Code here"), Filter = T("Session files") + "|*.claude-session.zip;*.zip" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return null;
        try
        {
            var x = ClaudeCli.ReadExport(d.FileName);
            // in the folder it worked in, if this PC has it; else this tab's
            string folder = Directory.Exists(x.Folder) ? x.Folder : Info.Folder;
            ClaudeCli.Import(d.FileName, folder);
            if (x.Model.Length > 0) Info.Model = x.Model;
            if (x.Mode.Length > 0) Info.Mode = x.Mode;
            string? why = Adopt(x.Id, x.Name, folder);
            if (why == null) Add(new NoteLine(F("Imported from {0}. Write below to carry on here.", x.From ?? T("another PC")), icon: "\u21E3"));
            return why;
        }
        catch (Exception ex) { return F("It couldn't be imported: {0}", ex.Message); }
    }

    /// <summary>The session as a file, to carry on with on another PC (Import there).</summary>
    private void ExportFile()
    {
        if (!Info.Started || ClaudeCli.TranscriptOf(Info) is not { } path) { Add(new NoteLine(T("There's nothing to export yet: send a message first."), error: true)); return; }
        string safe = string.Concat(Info.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var d = new SaveFileDialog { Title = T("Export the session"), FileName = safe + ".claude-session.zip", Filter = T("Session files") + "|*.claude-session.zip" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            ClaudeCli.Export(Info, path, d.FileName);
            Add(new NoteLine(F("Exported to {0}. On the other PC: + (a new tab), then Import a session file…", d.FileName), icon: "\u21E1"));
        }
        catch (Exception ex) { Add(new NoteLine(F("It couldn't be exported: {0}", ex.Message), error: true)); }
    }

    // ---------------------------------------------------------------- history

    /// <summary>A resumed session's conversation so far, from Claude Code's transcript (the last 300 messages).</summary>
    private async Task LoadHistoryAsync()
    {
        if (!Info.Started || ClaudeCli.TranscriptOf(Info) is not { } path) return;
        List<JsonElement> lines;
        try { lines = await Task.Run(() => ClaudeCli.ReadTranscript(path)); }
        catch (Exception ex) { Add(new NoteLine(F("The conversation so far couldn't be read: {0}", ex.Message), error: true)); return; }
        if (lines.Count > 300) { Add(new NoteLine(F("{0} earlier messages aren't shown.", lines.Count - 300))); lines = lines.Skip(lines.Count - 300).ToList(); }
        foreach (var l in lines)
        {
            var m = Obj(l, "message");
            var content = Obj(m, "content");
            if (Str(l, "type") == "user")
            {
                if (content.ValueKind == JsonValueKind.String) { string t = content.GetString() ?? ""; if (Visible(t)) Add(new UserBubble(t)); }
                else if (content.ValueKind == JsonValueKind.Array)
                    foreach (var b in content.EnumerateArray())
                    {
                        if (Str(b, "type") == "text" && Str(b, "text") is { } t && Visible(t)) Add(new UserBubble(t));
                        else if (Str(b, "type") == "tool_result" && Str(b, "tool_use_id") is { } id && cards.TryGetValue(id, out var card))
                            card.SetResult(ResultText(Obj(b, "content")), Obj(b, "is_error").ValueKind == JsonValueKind.True, timed: false);
                    }
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in content.EnumerateArray())
                {
                    if (Str(b, "type") == "text" && Str(b, "text") is { Length: > 0 } t) { var at = new AssistantText(); at.Finish(t); Add(at); }
                    else if (Str(b, "type") == "tool_use" && Str(b, "id") is { } id)
                    {
                        var card = new ToolCard(id, Str(b, "name") ?? "Tool") { Root = Info.Folder };
                        cards[id] = card;
                        card.SetInput(Obj(b, "input"));
                        Add(card);
                    }
                }
            }
        }
        foreach (var c in cards.Values.Where(c => !c.Finished)) c.SetResult("", false, timed: false);
        if (lines.Count > 0) Add(new NoteLine(T("The conversation so far. Write below to carry on."), icon: "↺"));
        ScrollToEnd(force: true);
    }

    // what Claude Code puts in a user turn that isn't the user's (command output, reminders)
    private static bool Visible(string text) => text.Length > 0 && !text.StartsWith("<") && !text.StartsWith("Caveat:");

    // ---------------------------------------------------------------- helpers

    private void Add(UIElement e)
    {
        chat.Children.Add(e);
        if (chat.Children.Count > 600) chat.Children.RemoveAt(0);
    }

    private void ScrollToEnd(bool force = false)
    {
        // only if it's at the end already (reading further up isn't interrupted)
        if (force || scroller.VerticalOffset >= scroller.ScrollableHeight - 80) Dispatcher.BeginInvoke(scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private static Border Card(UIElement child, string? title = null)
    {
        var s = new StackPanel();
        if (title != null)
        {
            var h = new TextBlock { Text = title };
            h.SetResourceReference(StyleProperty, "CardHeader");
            s.Children.Add(h);
        }
        s.Children.Add(child);
        var b = new Border { Child = s };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static FrameworkElement Labelled(string label, FrameworkElement control)
    {
        var s = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var l = new TextBlock { Text = label, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 3) };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(l);
        s.Children.Add(control);
        return s;
    }

    private static void FlashWindow(Window w)
    {
        var info = new FLASHWINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(), hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle, dwFlags = 3 | 12, uCount = 0 };
        FlashWindowEx(ref info);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);

    /// <summary>The process ends with the tab (or the app).</summary>
    public void Close()
    {
        usage.Changed -= ShowPlan;
        Look.Changed -= ApplyLook;
        settings.Changed -= OptionsChanged;
        tick.Stop();
        proc?.Dispose();
        proc = null;
    }
}
