using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using DaisysApp.Shared;

namespace DaisysApp.Applets.AI;

/// <summary>
/// One session tab. Left: the conversation (your messages, Claude's text as it streams, each tool call as a card with
/// its permission prompt) and the box to write in. Right: the dashboard, live while it works: the status orb and what
/// it's doing, its speed, the context window filling, the tokens by kind and the cost, your plan's usage, the turn's
/// activity ribbon, the files it changed, and the session's folder, model and permission mode.
/// </summary>
internal sealed class SessionView : UserControl
{
    public SessionInfo Info { get; }
    private readonly AiSettings settings;
    private readonly Func<string?> findExe;
    private readonly PlanUsage usage;
    private IAgent? proc;
    // switched to another account while this one worked: ended when the turn is done
    private bool restartForAccount;
    // the account's folder Claude Code was started in (null: Claude Code's own sign-in)
    private string? procAccountFolder;
    /// <summary>The Claude account it runs on (null: none picked, the one Claude Code is signed in to).</summary>
    private AiAccount? Account => IsClaude && !IsRemote ? ClaudeAccounts.Of(settings, Info.Account) : null;
    /// <summary>It runs on another Claude account than Claude Code's own sign-in: its plan's meters are its own
    /// (<see cref="ownPlan"/>), not PlanUsage's.</summary>
    private bool OnOwnAccount => Account is { } a && a.Id != settings.ActiveAccount;
    /// <summary>(For the Claude Account box: this PC's usage details aren't this session's.)</summary>
    public bool OnOtherAccount => IsRemote ? sessionAccount != null && sessionOwnAccount != null && sessionAccount != sessionOwnAccount : OnOwnAccount;
    /// <summary>On another account: its id and the usage it has (for the Claude Account box's list of accounts).</summary>
    public (string Id, PlanUsage.Elsewhere Plan)? OtherAccountPlan => OnOwnAccount && ownPlan != null ? (Account!.Id, ownPlan) : null;
    private bool readingPlan;
    /// <summary>That account's plan usage, from its own rate-limit reports (null: none yet).</summary>
    private PlanUsage.Elsewhere? ownPlan;
    /// <summary>Its plan's usage changed (remote control: a PC with it open is sent <see cref="PlanToRemote"/>).</summary>
    public event Action? PlanChanged;
    // a new tab: which Claude account: the Claude Account box's list (AccountsUsage, the same code: click one), of this
    // PC's accounts, or of the PC picked under Host's, with their usage as it sent them (since 3.15.2; a drop-down before)
    private AccountsUsage? startAccounts;
    private readonly StackPanel startLocal = new();
    private readonly StackPanel accountCol = new() { Margin = new Thickness(0, 0, 16, 0) };
    private List<AccountsUsage.RemoteAccount>? remoteAccounts;
    private string? remoteAccountPick, remoteOwnAccount, remotePcName;
    // (asks the PC under Host for its accounts again: {op: "accounts"}, since 3.15.3)
    private Func<Task>? remoteAccountsAsk;
    private bool remoteClaude;

    /// <summary>The tab's provider (null: Claude Code, or a session on another PC).</summary>
    private Provider? ApiProvider => Info.Provider.Length == 0 || IsRemote ? null : settings.Providers.FirstOrDefault(p => p.Id == Info.Provider);
    private bool IsClaude => Info.Provider.Length == 0;
    /// <summary>The session runs on another PC (remote control): this tab shows it and sends to it.</summary>
    private bool IsRemote => Info.Remote is { Length: > 0 };
    public bool OnOtherPc => IsRemote;
    /// <summary>On another PC: its Claude Code's own sign-in's usage, as it sends it (the Claude Account box and the usage
    /// details show it as this PC's; their ⟳ and Check now ask that PC).</summary>
    public PlanUsage RemoteUsage => remoteUsage ??= PlanUsage.Mirror(settings, Dispatcher, () => { proc?.Control("refresh_usage", null); return Task.CompletedTask; });
    private PlanUsage? remoteUsage;
    /// <summary>On another PC: its Claude accounts with their usage, its Claude Code's own sign-in, and the one this
    /// session runs on there.</summary>
    public List<AccountsUsage.RemoteAccount> RemoteAccounts => sessionAccounts;
    public string? RemoteOwnAccount => sessionOwnAccount;
    public string? RemoteSessionAccount => sessionAccount;
    /// <summary>The plan's usage on the PC a remote session runs on, as its server sends it (null: none yet, or an
    /// older server that doesn't).</summary>
    private PlanUsage.Elsewhere? remotePlan;
    private readonly TextBlock planWhere = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    /// <summary>The Claude Account picker, at the bottom of the Usage card.</summary>
    private StackPanel? accountBox;
    // a session on another PC: that PC's Claude accounts and the one it runs on there (from a 3.5.2 server), picked in
    // the Claude Account box (AiView), as this PC's are (since 3.15.3; with their usage since 3.15.2)
    private List<AccountsUsage.RemoteAccount> sessionAccounts = new();
    private string? sessionAccount, sessionOwnAccount;
    /// <summary>"Claude", or the provider's name.</summary>
    private string Who => IsClaude ? "Claude" : IsRemote ? Info.RemoteWho ?? T("the model") : ApiProvider?.Name ?? T("the model");
    private readonly DispatcherTimer resumeTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool limitHit;
    /// <summary>When Claude Code said the limit resets ("You've hit your session limit · resets 9:20am (America/New_York)").</summary>
    private DateTime? limitResets;
    /// <summary>Pause: stopping once the step it's on is done (pausing), and stopped there until Resume (paused).</summary>
    private bool pausing, paused;
    /// <summary>Messages written while it's busy: sent one at a time, each once the turn before is done.</summary>
    private readonly List<(string Text, List<ImageData> Images)> queued = new();
    private readonly StackPanel queuePanel = new() { Margin = new Thickness(0, 8, 12, 0), Visibility = Visibility.Collapsed };
    private readonly Button pause = new() { MinWidth = 90, MinHeight = 36, Margin = new Thickness(8, 4, 0, 0), Visibility = Visibility.Collapsed };
    /// <summary>Context: more to know, sent at once so it's read mid-turn (between two steps) and the work carries on with it.</summary>
    private readonly Button contextButton = new() { MinWidth = 90, MinHeight = 36, Margin = new Thickness(8, 4, 0, 0), Visibility = Visibility.Collapsed };
    /// <summary>The turn's times by kind, in two columns (top to bottom, then the next column).</summary>
    private readonly Grid activity = new() { Margin = new Thickness(0, 4, 0, 0), ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };

    /// <summary>What Claude Code (or the CLI's stderr) says when your plan's limit is reached.</summary>
    private const string LimitPattern = @"(usage|rate|session|weekly|5-hour)\s*limit|limit (reached|resets|will reset)|hit your (\w+ )?limit|out of (usage|extra usage|credits)";
    private DateTime? waitingUntil;
    private DateTime limitNoted;

    /// <summary>The state changed (for the tab's dot).</summary>
    public event Action? StateChanged;
    /// <summary>Claude Code renamed the session (or confirmed a rename).</summary>
    public event Action? Renamed;
    /// <summary>Remote control (this PC the server): everything the session does, for the PCs that have it open (as
    /// Claude Code's stream-json, and daisy_* messages for what's said here: your messages, notes, answers).</summary>
    public event Action<JsonElement>? Shared;
    /// <summary>The tab is closing.</summary>
    public event Action? Closing;

    /// <summary>The bottom of the dashboard: the tab's Claude Account box (the sign-in, the accounts, its buttons, Usage),
    /// moved to the tab showing (AiView).</summary>
    public ContentControl Tools { get; } = new();

    public SessionState State { get; private set; } = SessionState.Offline;
    private string doing = "";
    public bool Busy => State is SessionState.Starting or SessionState.Thinking or SessionState.Writing or SessionState.Tool or SessionState.Waiting;

    // the conversation
    private readonly StackPanel chat = new() { Margin = new Thickness(0, 0, 12, 0) };
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly RichInput input = new() { MinHeight = 132, MaxHeight = 420, FontSize = 15.5, Padding = new Thickness(10, 8, 10, 8) };
    private readonly Button send = new() { MinWidth = 90, MinHeight = 36, Margin = new Thickness(8, 4, 0, 0) };
    private readonly Button stop = new() { MinWidth = 90, MinHeight = 36, Margin = new Thickness(8, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border startPanel = new();
    private readonly Dictionary<int, object> blocks = new();
    private readonly Dictionary<string, ToolCard> cards = new();
    private readonly HashSet<string> streamed = new();
    private ThinkingChip? lastChip;
    private bool autoAllow;
    private int silentTurns;
    private NoteLine? compactingLine;  // "Compacting the conversation…" while Claude Code compacts it
    private bool compactedTurn;  // this turn was a compaction (/compact): its result line says so
    private bool notesDue;  // a request has been sent since the notes were last asked for (KeepNotes)
    private bool notesSkipped;
    private NotesGroup? notesGroup;  // the notes' turn showing: what it does goes in here, as one line
    private bool historyLoaded;
    private bool inEvent;  // drawing what the session did (its notes are the other PC's to draw too, not to be sent)
    /// <summary>Questions showing (their request id), and those asked here, for a PC opening the session (remote control).</summary>
    private readonly Dictionary<string, ToolCard> asks = new();
    private readonly Dictionary<string, JsonElement> pendingAsks = new();
    /// <summary>The .md files (in the folder) to send before the first message: Read first.</summary>
    private readonly List<string> readFirst = new();
    /// <summary>Set while a new tab is to start on another PC: the message goes there instead.</summary>
    private Action<string, List<ImageData>>? sendOverride;
    private readonly TextBox nameBox = new() { Width = 220 };

    // the dashboard
    private readonly StatusOrb orb = new() { Width = 64, Height = 64 };
    // fixed heights (two lines at most of what it's doing, the clock's line kept when empty), so the box doesn't jump as they change
    private readonly TextBlock stateText = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, Height = 26, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock doingText = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13.5, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Height = 36 };
    private readonly TextBlock clock = new() { FontSize = 13.5, Height = 18 };
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

    public SessionView(SessionInfo info, AiSettings settings, Func<string?> findExe, PlanUsage usage)
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
        pause.Click += (_, _) => Pause();
        contextButton.Content = T("Context");
        contextButton.ToolTip = T("Tell it more while it works: read after the step it's on, then it carries on");
        contextButton.Click += (_, _) => AddContext();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; SendInput(now: (Keyboard.Modifiers & ModifierKeys.Control) != 0); }
            else if (e.Key == Key.Escape && Busy) { e.Handled = true; Interrupt(); }
        };
        var inputRow = new DockPanel { Margin = new Thickness(0, 10, 12, 0) };
        // Context, Pause, Stop and Send stacked, Send at the bottom
        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Children.Add(contextButton);
        buttons.Children.Add(pause);
        buttons.Children.Add(stop);
        buttons.Children.Add(send);
        DockPanel.SetDock(buttons, Dock.Right);
        inputRow.Children.Add(buttons);
        inputRow.Children.Add(input);
        var left = new DockPanel();
        // (the box first, so a tall new-tab panel scrolls rather than pushing it off the screen)
        var startScroll = new ScrollViewer { Content = startPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        startScroll.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = startPanel });
        DockPanel.SetDock(startScroll, Dock.Top);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        DockPanel.SetDock(queuePanel, Dock.Bottom);
        left.Children.Add(inputRow);
        left.Children.Add(queuePanel);
        left.Children.Add(startScroll);
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
        statusRow.MinHeight = 64;
        foreach (var line in new[] { doingText, clock })
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(line, (_, _) => line.Visibility = string.IsNullOrEmpty(line.Text) ? Visibility.Collapsed : Visibility.Visible);
        doingText.Visibility = clock.Visibility = Visibility.Collapsed;
        dash.Children.Add(Card(statusRow));

        var speedStack = new StackPanel();
        speedStack.Children.Add(speed);
        speedCaption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        speedStack.Children.Add(speedCaption);
        speedStack.Children.Add(new TextBlock { Text = T("This turn"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.7 });
        speedStack.Children.Add(ribbon);
        speedStack.Children.Add(activity);
        dash.Children.Add(Card(speedStack, T("Activity")));

        var usageCard = new StackPanel();
        usageCard.Children.Add(ring);
        usageCard.Children.Add(new Border { Height = 10 });
        usageCard.Children.Add(tokens);
        cost.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        usageCard.Children.Add(cost);
        usageCard.Children.Add(fiveHour);
        usageCard.Children.Add(sevenDay);
        planWhere.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        usageCard.Children.Add(planWhere);
        // (this PC's account box; for a session on another PC, that PC's accounts)
        accountBox = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        accountBox.Children.Add(Tools);
        usageCard.Children.Add(accountBox);
        dash.Children.Add(Card(usageCard, T("Usage")));

        dash.Children.Add(Card(files, T("Files changed")));
        dash.Children.Add(Card(SessionSettings(), T("Session")));
        var dashScroll = new ScrollViewer { Content = dash, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // (not while it's a new tab: its choices are all in the panel above the box)
        dashScroll.Width = 290;
        void ShowDash() => dashScroll.Visibility = startPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Border)).AddValueChanged(startPanel, (_, _) => ShowDash());
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(dashScroll, 1);
        grid.Children.Add(dashScroll);
        Content = grid;

        tick.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            if (!historyLoaded)
            {
                historyLoaded = true;
                // (a session on another PC: its conversation comes when it's open there)
                if (IsRemote) { historyReady = true; EnsureProcess(); }
                else _ = LoadHistoryAsync();
            }
            input.Focus();
        };
        BuildStartPanel();
        ShowDash();
        resumeTimer.Tick += (_, _) => ResumeIfDue();
        if (Info.ResumeAt != null) Dispatcher.BeginInvoke(() => { SetState(SessionState.Paused, ResumeText()); resumeTimer.Start(); });
        ShowTotals();
        ShowFiles();
        SetState(SessionState.Offline, "");
        ring.Set(0, "–", T("context"));
        usage.Changed += ShowPlan;
        Closing += () => usage.Working(this, false);
        Look.Changed += ApplyLook;
        settings.Changed += OptionsChanged;
        ApplyLook();
        ShowPlan();
    }

    // ---------------------------------------------------------------- sending

    private void SendInput(bool now = false)
    {
        if (input.IsEmpty) return;
        var (text, images) = input.Message();
        text = text.Trim();
        if (text.Length == 0 && images.Count == 0) return;
        // (a new tab starting on another PC: the box is emptied once it has)
        if (sendOverride != null) { sendOverride(text, images); return; }
        input.Clear();
        // (busy, paused or waiting for a limit: it waits its turn, unless it's to go now)
        if (!now && (Busy || paused || State == SessionState.Paused || Info.ResumeAt != null)) { Queue(text, images); return; }
        Send(text, bubble: true, images);
    }

    private void Queue(string text, List<ImageData> images)
    {
        queued.Add((text, images));
        ShowQueue();
    }

    /// <summary>The messages waiting, above the box: each can be sent at once (Claude reads it as soon as it can) or taken out.</summary>
    private void ShowQueue()
    {
        queuePanel.Children.Clear();
        queuePanel.Visibility = queued.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (queued.Count == 0) return;
        queuePanel.Children.Add(Secondary(P(queued.Count, "{0} message waits: it's sent when this is done.", "{0} messages wait: each is sent when the one before is done."), new Thickness(0, 0, 0, 4)));
        foreach (var item in queued.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var now = new Button { Content = T("Send now"), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Send it at once: it's read as soon as it can be") };
            var drop = new Button { Content = "\u2715", Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Take it out of the queue") };
            now.Click += (_, _) => { queued.Remove(item); ShowQueue(); Send(item.Text, bubble: true, item.Images); };
            drop.Click += (_, _) => { queued.Remove(item); ShowQueue(); };
            DockPanel.SetDock(drop, Dock.Right);
            DockPanel.SetDock(now, Dock.Right);
            row.Children.Add(drop);
            row.Children.Add(now);
            string first = item.Text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            row.Children.Add(new TextBlock
            {
                Text = "\u23F3  " + (item.Images.Count > 0 ? "\U0001F5BC " : "") + first, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = item.Text.Length > 600 ? item.Text[..600] + "\u2026" : item.Text, FontSize = 13.5,
            });
            queuePanel.Children.Add(row);
        }
    }

    /// <summary>The next message waiting, now the turn before is done.</summary>
    private void SendQueued()
    {
        var (text, images) = queued[0];
        queued.RemoveAt(0);
        ShowQueue();
        Send(text, bubble: true, images);
    }

    // ---------------------------------------------------------------- context

    /// <summary>
    /// Context: a message sent at once, as Ctrl+Enter does. Claude Code reads a message sent mid-turn after the step it's
    /// on (it isn't stopped) and carries on with it in mind; it's told so, so it doesn't drop what it was doing.
    /// </summary>
    private void AddContext()
    {
        var w = new ContextWindow { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() != true || w.Text.Length == 0) return;
        Logging.Log.Here.Info($"Session {Info.Id}: context sent mid-turn");
        Send(w.Text, bubble: true, sendAs: "More context for what you're working on now:\n\n" + w.Text + "\n\nTake it into account and carry on with the task.");
    }

    // ---------------------------------------------------------------- pause

    /// <summary>
    /// Pause: it isn't stopped in the middle of anything. It carries on with the step it's on (a command, an edit, its
    /// reply), and once that's done it's stopped there (as Stop does, between steps), until Resume tells it to carry on.
    /// </summary>
    private void Pause()
    {
        if (paused) { Resume(); return; }
        if (!Busy || pausing) return;
        pausing = true;
        Logging.Log.Here.Info($"Session {Info.Id}: pausing after this step");
        SetState(State, T("Pausing once this step is done…"));
        // nothing running: it pauses after the next step, or when its reply is done
    }

    /// <summary>Pausing, and a step is done: stopped here.</summary>
    private void PauseNow()
    {
        if (!pausing || proc is not { Running: true }) return;
        proc.Interrupt();
    }

    private void Paused()
    {
        paused = true;
        Add(new NoteLine(F("Paused. Resume carries on (it's told \u201C{0}\u201D); messages you send meanwhile wait in the queue, and Ctrl+Enter sends one at once.", ResumeMessage), icon: "\u23F8"));
        SetState(SessionState.Paused, T("Paused by you: Resume carries on"));
    }

    private void Resume()
    {
        paused = false;
        Logging.Log.Here.Info($"Session {Info.Id}: resumed");
        Send(ResumeMessage, bubble: true);
    }

    private string ResumeMessage => settings.ResumeMessage.Trim().Length > 0 ? settings.ResumeMessage.Trim() : "Please continue where you left off.";

    /// <summary>A message (or a slash command) to Claude, with any images; the process starts if it isn't running. The
    /// files to read first (if any) go before the first message.</summary>
    public void Send(string text, bool bubble, IReadOnlyList<ImageData>? images = null, string? sendAs = null)
    {
        if (!historyLoaded && !IsRemote)
        {
            // (sent from another PC before this tab was shown: its conversation so far first)
            historyLoaded = historyReady = true;
            if (Info.Started) RenderHistory(ReadHistory());
        }
        if (!EnsureProcess())
        {
            Share(new JsonObject { ["type"] = "daisy_state", ["state"] = SessionState.Error.ToString() });
            return;
        }
        // (anything sent carries on from a pause, and from waiting for a limit)
        paused = false;
        if (Info.ResumeAt != null)
        {
            Info.ResumeAt = null;
            settings.Save();
            resumeTimer.Stop();
        }
        string sent = sendAs ?? text;
        if (readFirst.Count > 0 && !text.StartsWith('/') && !IsRemote)
        {
            Add(new NoteLine(F("Read first: {0}", string.Join(", ", readFirst)), icon: "📄"));
            sent = ReadFirstMessage(sent);
            readFirst.Clear();
        }
        // (a session on another PC: the bubble comes back from there, as it does to every PC that has it open)
        if (bubble && !IsRemote) Add(new UserBubble(text, queued: Busy, images));
        if (bubble) Share(new JsonObject { ["type"] = "daisy_user", ["text"] = text, ["queued"] = Busy, ["images"] = ImagesJson(images) });
        if (!text.StartsWith('/')) notesDue = true;
        if (bubble && !IsRemote && settings.LogRequests == true && !text.StartsWith('/') && text.Trim() != ResumeMessage)
            RequestLog.Add(Info.Folder, text);
        if (!Busy)
        {
            turnStart = DateTime.UtcNow;
            turnOutput = messageOutput = 0;
            ribbon.Clear();
            SetState(SessionState.Thinking, T("Sending…"));
        }
        proc!.Send(sent, images);
        if (!Info.Started)
        {
            Info.Started = true;
            startPanel.Visibility = Visibility.Collapsed;
            settings.LastFolder = Info.Folder;
            settings.Save();
        }
        ScrollToEnd(force: true);
    }

    /// <summary>Renames the session: at once if it's running (/rename), else at its next start (--name). Since 3.16.2
    /// a session on another PC is renamed there too (control "rename"), and the PCs that have this one open are told
    /// (daisy_name), so the name is the same everywhere.</summary>
    public void Rename(string name)
    {
        Info.Name = name;
        settings.Save();
        if (nameBox.Text.Trim() != name) nameBox.Text = name;
        if (IsRemote)
        {
            proc?.Control("rename", new() { ["name"] = name });
            return;
        }
        if (proc is { Running: true })
        {
            silentTurns++;
            proc.Send("/rename " + name);
        }
        Share(new JsonObject { ["type"] = "daisy_name", ["name"] = name });
    }

    private void Interrupt()
    {
        pausing = false;
        // waiting to carry on after a limit: not any more
        if (Info.ResumeAt != null)
        {
            Info.ResumeAt = null;
            settings.Save();
            resumeTimer.Stop();
            Add(new NoteLine(T("It won't carry on by itself: write below when you want it to."), icon: "\u23F8"));
            SetState(SessionState.Idle, "");
            return;
        }
        if (proc is not { Running: true }) return;
        proc.Interrupt();
        SetState(State, T("Stopping…"));
    }

    private bool EnsureProcess()
    {
        if (proc is { Running: true }) return true;
        if (IsRemote)
        {
            var remote = RemoteAgent.Start(settings.Remote, Info, Dispatcher);
            proc = remote;
            processCost = 0;
            remote.Message += OnMessage;
            remote.Exited += tail => OnExited(remote, tail);
            remote.Attached += OnAttached;
            remote.Usage += plan => { remotePlan = plan; ShowPlan(); };
            remote.AccountIs += id => { sessionAccount = id; FillSessionAccounts(); };
            remote.OwnUsage += o => RemoteUsage.ApplyRemote(o);
            remote.AccountsAre += (list, own) => { sessionAccounts = AccountsUsage.FromRemote(list); sessionOwnAccount = own ?? sessionOwnAccount; FillSessionAccounts(); };
            SetState(SessionState.Starting, F("Connecting to {0}…", Info.RemotePc ?? Info.Remote));
            return true;
        }
        if (!Directory.Exists(Info.Folder))
        {
            Add(new NoteLine(F("The folder {0} doesn't exist: pick another above.", Info.Folder), error: true));
            startPanel.Visibility = Visibility.Visible;
            return false;
        }
        if (!IsClaude)
        {
            if (ApiProvider is not { } provider)
            {
                Add(new NoteLine(T("This tab's provider isn't set up any more: pick one above (Providers… sets them up)."), error: true));
                startPanel.Visibility = Visibility.Visible;
                return false;
            }
            proc = ApiAgent.Start(provider, Info, Dispatcher, () => settings.SkipPermissions);
            processCost = 0;
            proc.Message += OnMessage;
            SetState(SessionState.Starting, F("Connecting to {0}…", provider.Name));
            return true;
        }
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
        string? accountFolder;
        try { accountFolder = ClaudeAccounts.FolderFor(settings, Info.Account); }
        catch (Exception ex)
        {
            Logging.Log.Here.Warn($"Session {Info.Id}: the account's folder: {ex.Message}");
            Add(new NoteLine(ex is InvalidOperationException ? ex.Message : F("Claude Code couldn't be started on {0}: {1}", Account?.Label ?? "?", ex.Message), error: true));
            return false;
        }
        try
        {
            proc = ClaudeProcess.Start(exe, Info, Dispatcher, settings.SkipPermissions, accountFolder);
            procAccountFolder = accountFolder;
            // (its account's meters, until its first request reports them)
            if (accountFolder != null && ownPlan == null) _ = RefreshPlanAsync();
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

    private void OnExited(IAgent which, string tail)
    {
        if (proc != which) return;
        proc = null;
        if (IsRemote)
        {
            Add(new NoteLine(F("Disconnected from {0}: {1} Sending again connects again.", Info.RemotePc ?? Info.Remote, tail), error: true));
            ribbon.End();
            SetState(SessionState.Offline, "");
            return;
        }
        if (IsClaude && settings.AutoResume && System.Text.RegularExpressions.Regex.IsMatch(tail, LimitPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            // (it ended at your plan's limit: it carries on once that resets)
            limitResets = ResetTime(tail);
            ribbon.End();
            PlanResume();
            return;
        }
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
        inEvent = true;
        try
        {
            string? parent = Str(e, "parent_tool_use_id");
            string? type = Str(e, "type");
            // (to the PCs that have it open; a question only once it's asked here, not one answered by itself)
            if (Shared != null && type != "control_request") Shared(e);
            switch (type)
            {
                case "stream_event": if (parent == null) Stream(Obj(e, "event")); break;
                case "assistant":
                    // (your plan's limit: a message of Claude Code's own, marked rate_limit)
                    if (Str(e, "error") == "rate_limit" && parent == null) limitHit = true;
                    Assistant(Obj(e, "message"), parent);
                    break;
                case "user": if (parent == null) ToolResults(Obj(e, "message")); break;
                case "system": SystemMessage(e); break;
                case "result": Result(e); break;
                case "control_request": ControlRequest(e); break;
                case "rate_limit_event": RateLimit(Obj(e, "rate_limit_info")); break;
                case "daisy_user": RemoteUser(e); break;
                case "daisy_note" when Str(e, "text") == NotesLine: StartNotes(); break;
                case "daisy_note": Add(new NoteLine(Str(e, "text") ?? "", error: Obj(e, "error").ValueKind == JsonValueKind.True, icon: Str(e, "icon"))); break;
                case "daisy_name" when Str(e, "name") is { Length: > 0 } renamed && renamed != Info.Name:
                    Info.Name = renamed;
                    settings.Save();
                    if (nameBox.Text.Trim() != renamed) nameBox.Text = renamed;
                    Renamed?.Invoke();
                    break;
                case "daisy_answered":
                    if (Str(e, "request_id") is { } rid && asks.Remove(rid, out var asked)) asked.Answered(Obj(e, "allow").ValueKind == JsonValueKind.True);
                    if (State == SessionState.Waiting) SetState(SessionState.Tool, "");
                    break;
                case "daisy_state":
                    if (Enum.TryParse<SessionState>(Str(e, "state"), out var st)) SetState(st, "");
                    break;
            }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Claude Code message", ex); }
        finally { inEvent = false; }
    }

    // ---------------------------------------------------------------- remote control

    /// <summary>A message sent to the session (here, from the other PC, or on the PC it runs on): its bubble.</summary>
    private void RemoteUser(JsonElement e)
    {
        Add(new UserBubble(Str(e, "text") ?? "", queued: Obj(e, "queued").ValueKind == JsonValueKind.True, ImagesOf(Obj(e, "images"))));
        if (!Busy)
        {
            turnStart = DateTime.UtcNow;
            turnOutput = messageOutput = 0;
            ribbon.Clear();
            SetState(SessionState.Thinking, T("Sending…"));
        }
        ScrollToEnd(force: true);
    }

    /// <summary>The session's open on the other PC: as it is there, with its conversation so far.</summary>
    private void OnAttached(JsonObject m)
    {
        string S(string n) => m[n] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        if (S("name").Length > 0) Info.Name = S("name");
        Info.Folder = S("folder");
        Info.Provider = S("provider");
        Info.RemoteWho = S("who");
        Info.RemotePc = S("pc");
        Info.Model = S("model");
        if (S("mode").Length > 0) Info.Mode = S("mode");
        settings.Save();
        sessionAccounts = AccountsUsage.FromRemote(m["accounts"] as JsonArray);
        sessionAccount = S("account") is { Length: > 0 } acc ? acc : null;
        sessionOwnAccount = S("ownAccount") is { Length: > 0 } own ? own : null;
        FillSessionAccounts();
        ShowFolder();
        FillModels();
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Mode) ?? mode.SelectedItem;
        Renamed?.Invoke();
        chat.Children.Clear();
        notesGroup = null;
        cards.Clear();
        asks.Clear();
        blocks.Clear();
        var lines = new List<JsonElement>();
        if (m["history"] is JsonArray history)
            foreach (var h in history)
            {
                if (h == null) continue;
                using var d = JsonDocument.Parse(h.ToJsonString());
                lines.Add(d.RootElement.Clone());
            }
        RenderHistory(lines);
        Add(new NoteLine(F("Open on {0}: this session runs there, and what it does shows on both PCs.", Info.RemotePc ?? Info.Remote), icon: "⇄"));
        if (Enum.TryParse<SessionState>(S("state"), out var state))
        {
            if (state is SessionState.Starting or SessionState.Thinking or SessionState.Writing or SessionState.Tool or SessionState.Waiting) turnStart = DateTime.UtcNow;
            SetState(state, S("doing"));
        }
        else SetState(SessionState.Idle, "");
        ScrollToEnd(force: true);
    }

    /// <summary>To the PCs that have the session open (remote control).</summary>
    private void Share(JsonObject o)
    {
        if (Shared == null) return;
        using var d = JsonDocument.Parse(o.ToJsonString());
        Shared(d.RootElement.Clone());
    }

    /// <summary>The session as it is now, for a PC opening it ({op: "attached"}).</summary>
    public JsonObject Snapshot() => new()
    {
        ["op"] = "attached", ["id"] = Info.Id, ["name"] = Info.Name, ["folder"] = Info.Folder, ["provider"] = Info.Provider,
        ["who"] = IsClaude ? "Claude Code" : ApiProvider?.Name ?? "?", ["model"] = Info.Model, ["mode"] = Info.Mode,
        ["state"] = State.ToString(), ["doing"] = doing, ["pc"] = Environment.MachineName,
        ["context"] = proc?.ContextWindow ?? (ApiProvider is { ContextWindow: > 0 } p ? p.ContextWindow : null),
        // (since 3.5.2: this PC's Claude accounts, and the one it runs on, for the other PC to switch it)
        ["accounts"] = IsClaude ? AccountsUsage.ToRemote(settings, usage) : null, ["account"] = SessionAccountId,
        // (since 3.15.2: each account's usage in "accounts", and which is Claude Code's own sign-in here)
        ["ownAccount"] = IsClaude ? settings.ActiveAccount : null,
    };

    /// <summary>The Claude account it runs on, by id (Claude Code's own sign-in when none is picked); null if not Claude Code.</summary>
    private string? SessionAccountId => !IsClaude || IsRemote ? null : Account?.Id ?? settings.ActiveAccount;

    /// <summary>The questions showing now, as they were asked (for a PC opening the session).</summary>
    public IEnumerable<JsonElement> PendingAsks => pendingAsks.Values.ToList();

    public void RemoteInterrupt() => Interrupt();

    /// <summary>A question answered on the other PC.</summary>
    public void RemoteAnswer(string requestId, bool allow, JsonObject? answers = null)
    {
        if (!asks.TryGetValue(requestId, out var card)) return;
        if (card.AskingQuestions && pendingAsks.TryGetValue(requestId, out var e))
        {
            // (Claude's questions, answered on the other PC)
            asks.Remove(requestId);
            pendingAsks.Remove(requestId);
            card.QuestionsAnswered(allow ? answers : null);
            AnswerQuestions(proc, requestId, Obj(Obj(e, "request"), "input"), allow ? answers : null);
            Share(new JsonObject { ["type"] = "daisy_answered", ["request_id"] = requestId, ["allow"] = allow });
            SetState(SessionState.Thinking, "");
            return;
        }
        card.AnswerNow(allow);
    }

    /// <summary>The model or the permissions changed on the other PC.</summary>
    public void RemoteControl(string subtype, string value)
    {
        // (its ⟳ or Check now there: as this PC's own)
        if (subtype == "refresh_usage")
        {
            _ = RefreshPlanAsync();
            _ = AccountsUsage.ReadAllAsync(settings, findExe);
        }
        else if (subtype == "set_model")
        {
            Info.Model = value;
            settings.Save();
            FillModels();
            proc?.Control("set_model", new() { ["model"] = value.Length > 0 ? value : null });
        }
        else if (subtype == "set_permission_mode" && value.Length > 0)
        {
            mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value) ?? mode.SelectedItem;
        }
        // (its tab renamed there: here, and to every other PC that has it open)
        else if (subtype == "rename" && value.Trim() is { Length: > 0 } name && name != Info.Name)
        {
            Rename(name);
            Renamed?.Invoke();
        }
        // (as its own Claude Account box would: only this session moves)
        else if (subtype == "set_account" && IsClaude && ClaudeAccounts.Of(settings, value) != null)
        {
            Logging.Log.Here.Info($"Session {Info.Id}: account picked on another PC");
            UseAccount(value);
        }
    }

    /// <summary>The files a session started from another PC reads first.</summary>
    public void SetReadFirst(IEnumerable<string> files)
    {
        readFirst.Clear();
        readFirst.AddRange(files.Where(IsDocPath));
    }

    /// <summary>A session's conversation so far, for a PC opening it: the last 300 messages, long results cut short.</summary>
    public static JsonArray RemoteHistory(SessionInfo info)
    {
        var array = new JsonArray();
        try
        {
            var lines = HistoryOf(info);
            foreach (var l in lines.Skip(Math.Max(0, lines.Count - 300)))
            {
                var node = JsonNode.Parse(l.GetRawText());
                Shorten(node);
                array.Add(node);
            }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Remote control: reading the history: " + ex.Message); }
        return array;

        static void Shorten(JsonNode? n)
        {
            if (n is JsonObject o)
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (o[key] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 4000)
                        o[key] = key == "data" ? (text.Length < 2_500_000 ? text : "") : text[..4000] + "…";
                    else Shorten(o[key]);
                }
            else if (n is JsonArray a)
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 4000) a[i] = text[..4000] + "…";
                    else Shorten(a[i]);
                }
        }
    }

    /// <summary>Images as JSON ([{media_type, data}]), and back.</summary>
    public static JsonArray ImagesJson(IReadOnlyList<ImageData>? images) =>
        new((images ?? Array.Empty<ImageData>()).Select(i => (JsonNode)new JsonObject { ["media_type"] = i.MediaType, ["data"] = i.Data }).ToArray());

    public static List<ImageData> Images(JsonNode? node) =>
        (node as JsonArray)?.OfType<JsonObject>().Select(o => new ImageData(o["media_type"]?.GetValue<string>() ?? "image/png", o["data"]?.GetValue<string>() ?? ""))
            .Where(i => i.Data.Length > 0).ToList() ?? new();

    private static List<ImageData> ImagesOf(JsonElement e) => e.ValueKind != JsonValueKind.Array ? new() :
        e.EnumerateArray().Select(o => new ImageData(Str(o, "media_type") ?? "image/png", Str(o, "data") ?? "")).Where(i => i.Data.Length > 0).ToList();

    // ---------------------------------------------------------------- read first

    private const string ReadFirstHead = "Before my request, read these files from the working folder: they describe the project and what we're working on.";
    private const string RequestHead = "\n\n--- My request ---\n";

    /// <summary>The first message, with the files to read first before it (their contents, or where to read them if they're big).</summary>
    private string ReadFirstMessage(string text)
    {
        var sb = new StringBuilder(ReadFirstHead);
        long total = 0;
        foreach (var name in readFirst)
        {
            // Claude Code has the working folder's CLAUDE.md already (it reads it on every turn): don't send it twice
            if (IsClaude && name.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase)) continue;
            string? body = null;
            try { body = File.ReadAllText(Path.Combine(Info.Folder, name)); } catch { }
            if (body == null) sb.Append($"\n\n<file name=\"{name}\">(it couldn't be read here: read it yourself, first)</file>");
            else if (total + body.Length > 400_000) sb.Append($"\n\n<file name=\"{name}\">(too long to include here: read it yourself, first)</file>");
            else
            {
                total += body.Length;
                sb.Append($"\n\n<file name=\"{name}\">\n{body.TrimEnd()}\n</file>");
            }
        }
        return sb.Append(RequestHead).Append(text).ToString();
    }

    /// <summary>A message without the files read before it (as it was written).</summary>
    public static string WithoutReadFirst(string text)
    {
        if (!text.StartsWith(ReadFirstHead)) return text;
        int at = text.IndexOf(RequestHead, StringComparison.Ordinal);
        return at < 0 ? "" : text[(at + RequestHead.Length)..];
    }

    /// <summary>The names of the files a first message had read first (none if it didn't).</summary>
    private static List<string> ReadFirstNames(string text)
    {
        if (!text.StartsWith(ReadFirstHead)) return new();
        int at = text.IndexOf(RequestHead, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Matches(at < 0 ? text : text[..at], "^<file name=\"([^\"]+)\">", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>The .md files in a folder (README and CLAUDE first), for Read first.</summary>
    public static List<string> MarkdownFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "*.md").Select(f => Path.GetFileName(f))
                .OrderBy(n => n.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? 0 : n.Equals("CLAUDE.md", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Folders not looked in for module docs: build output, packages, other people's code.</summary>
    private static readonly HashSet<string> NotDocFolders = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", "artifacts", "packages", "third_party", "vendor", "out", "dist", "build", "target" };

    /// <summary>
    /// The README.md files in a folder's subfolders (5 deep), for Read first's Module Docs: each with its path from the
    /// folder (with /) and its name, its folder's name ("Parent/Folder" where two folders share a name); sorted by name.
    /// </summary>
    public static List<(string Path, string Name)> ModuleDocs(string folder)
    {
        var found = new List<(string Path, string Name)>();
        var headings = new Dictionary<string, string>();
        int looked = 0;
        void Look(string dir, int depth)
        {
            if (depth > 5 || found.Count >= 200 || ++looked > 5000) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var sub in subs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(sub);
                if (name.StartsWith('.') || NotDocFolders.Contains(name)) continue;
                string readme = Path.Combine(sub, "README.md");
                if (File.Exists(readme))
                {
                    var rel = Path.GetRelativePath(folder, readme).Replace('\\', '/');
                    found.Add((rel, name));
                    if (Heading(readme) is { } h) headings[rel] = h;
                }
                Look(sub, depth + 1);
            }
        }
        try { Look(folder, 1); } catch { }
        // (this app's own source: named "Tab → Sub-tab" from each README's heading, in the order the window shows them)
        IReadOnlyList<Shell.AppNavigation.ShownTab> shown;
        try { shown = Shell.AppNavigation.TabsShown?.Invoke() ?? Array.Empty<Shell.AppNavigation.ShownTab>(); }
        catch { shown = Array.Empty<Shell.AppNavigation.ShownTab>(); }
        if (shown.Count > 0 && found.Any(f => headings.TryGetValue(f.Path, out var h) && InTabs(shown, h) != null))
            return InTabOrder(found, headings, shown);
        // (two "Tests" folders: "AudioTools/Tests" and "Hardware/Tests")
        var twice = found.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return found.Select(f =>
        {
            if (!twice.Contains(f.Name)) return f;
            var parts = f.Path.Split('/');
            return parts.Length >= 3 ? (f.Path, $"{parts[^3]}/{f.Name}") : f;
        }).OrderBy(f => f.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A README's first "# " heading (in its first lines), or null.</summary>
    private static string? Heading(string readme)
    {
        try
        {
            foreach (var line in File.ReadLines(readme).Take(5))
                if (line.StartsWith("# ")) return line[2..].Trim();
        }
        catch { }
        return null;
    }

    /// <summary>"Audio: Volume", "SimEffects → Shaker", "Resizer", "Template — writing a module": the tab and the sub-tab (or null).</summary>
    private static (string Tab, string? Sub) SplitHeading(string heading)
    {
        foreach (var dash in new[] { " — ", " - " })
            if (heading.IndexOf(dash, StringComparison.Ordinal) is var d and > 0) heading = heading[..d];
        foreach (var sep in new[] { " → ", " -> ", ": " })
            if (heading.IndexOf(sep, StringComparison.Ordinal) is var i and > 0) return (heading[..i].Trim(), heading[(i + sep.Length)..].Trim());
        return (heading.Trim(), null);
    }

    /// <summary>
    /// Where a README's heading sits among the tabs shown: (tab index, sub-tab indexes (none: the tab itself), the tab's
    /// title, the sub-tabs' titles), or null. A lone name can be a tab or a sub-tab ("Resizer"); "Overlay and Recording"
    /// is two sub-tabs.
    /// </summary>
    private static (int Tab, List<int> Subs, string Title, List<string> Names)? InTabs(IReadOnlyList<Shell.AppNavigation.ShownTab> shown, string heading)
    {
        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var (tab, sub) = SplitHeading(heading);
        int ti = shown.ToList().FindIndex(t => Same(t.Title, tab));
        if (sub == null)
        {
            if (ti >= 0) return (ti, new(), Name(shown[ti]), new());
            for (int i = 0; i < shown.Count; i++)
                if (shown[i].Tools.ToList().FindIndex(t => Same(t, tab)) is var k and >= 0) return (i, new() { k }, Name(shown[i]), new() { shown[i].Tools[k] });
            return null;
        }
        if (ti < 0) return null;
        var tools = shown[ti].Tools.ToList();
        var parts = tools.FindIndex(t => Same(t, sub)) >= 0 ? new[] { sub } : sub.Split(" and ", StringSplitOptions.TrimEntries);
        var subs = new List<int>();
        var names = new List<string>();
        foreach (var p in parts)
            if (tools.FindIndex(t => Same(t, p)) is var k and >= 0) { subs.Add(k); names.Add(tools[k]); }
        // (a sub-tab the window doesn't list: still "Tab → Sub-tab", after the ones it does)
        return subs.Count > 0 ? (ti, subs, Name(shown[ti]), names) : (ti, new() { tools.Count }, Name(shown[ti]), new() { sub });
    }

    /// <summary>A tab's name as it shows (the user's, where they renamed it: "Claude" for the AI tab).</summary>
    private static string Name(Shell.AppNavigation.ShownTab t) => string.IsNullOrWhiteSpace(t.Named) ? t.Title : t.Named;

    /// <summary>Module Docs named and sorted as the window shows the tabs: "Audio", "Audio → Volume"…; the rest after, by name.</summary>
    private static List<(string Path, string Name)> InTabOrder(List<(string Path, string Name)> found, Dictionary<string, string> headings, IReadOnlyList<Shell.AppNavigation.ShownTab> shown)
    {
        var placed = new List<(int Tab, int Sub, string Path, string Name)>();
        var rest = new List<(string Path, string Name)>();
        foreach (var f in found)
        {
            var at = headings.TryGetValue(f.Path, out var h) ? InTabs(shown, h) : null;
            if (at is not { } a) { rest.Add((f.Path, h != null ? SplitHeading(h).Tab : f.Name)); continue; }
            if (a.Subs.Count == 0) placed.Add((a.Tab, -1, f.Path, a.Title));
            else for (int i = 0; i < a.Subs.Count; i++) placed.Add((a.Tab, a.Subs[i], f.Path, $"{a.Title} → {a.Names[i]}"));
        }
        return placed.OrderBy(p => p.Tab).ThenBy(p => p.Sub).Select(p => (p.Path, p.Name))
            .Concat(rest.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Read first's list: a header and a box of checkboxes 6 rows high (it scrolls); ticked ones go in <paramref name="chosen"/>.</summary>
    private static FrameworkElement DocsBox(string header, IEnumerable<(string Path, string Name, string Tip)> items, List<string> chosen)
    {
        var list = new StackPanel { Margin = new Thickness(6, 2, 6, 2) };
        foreach (var (path, name, tip) in items)
        {
            var box = new CheckBox { Content = name, Margin = new Thickness(0, 2, 0, 2), ToolTip = tip, IsChecked = chosen.Contains(path) };
            box.Click += (_, _) => { if (box.IsChecked == true) { if (!chosen.Contains(path)) chosen.Add(path); } else chosen.Remove(path); };
            list.Children.Add(box);
        }
        var frame = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 6 * 22 + 6,
            Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Visible },
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        var panel = new StackPanel();
        panel.Children.Add(Secondary(header, new Thickness(0, 0, 0, 4)));
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Read first's Project Docs and Module Docs boxes side by side (Module Docs only when there are some).</summary>
    private static void FillDocs(Grid grid, List<string> project, string folder, List<(string Path, string Name)> modules, List<string> chosen)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.Visibility = project.Count + modules.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var boxes = new List<FrameworkElement>();
        if (project.Count > 0) boxes.Add(DocsBox(T("Project Docs"), project.Select(f => (f, f, Path.Combine(folder, f))), chosen));
        if (modules.Count > 0) boxes.Add(DocsBox(T("Module Docs"), modules.Select(m => (m.Path, m.Name, m.Path)), chosen));
        for (int i = 0; i < boxes.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            boxes[i].Margin = new Thickness(i > 0 ? 8 : 0, 0, i < boxes.Count - 1 ? 8 : 0, 0);
            Grid.SetColumn(boxes[i], i);
            grid.Children.Add(boxes[i]);
        }
    }

    /// <summary>A file to read first, as given by another PC: an .md file inside the working folder.</summary>
    private static bool IsDocPath(string f) =>
        f.Length > 0 && f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(f)
        && !f.Replace('\\', '/').Split('/').Any(p => p is "" or "." or "..");

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
                    ribbon.Begin(Palette.Of(SessionState.Thinking), T("Waiting"));
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
                                ribbon.Begin(Palette.Of(Palette.KindOf(name)), name, KindName(Palette.KindOf(name)));
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
                if (synthetic && parent == null && Str(b, "text") is { } said && System.Text.RegularExpressions.Regex.IsMatch(said, LimitPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    limitHit = true;
                    limitResets = ResetTime(said) ?? limitResets;
                }
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
        // (pausing: the step's done, so it stops here)
        if (pausing && !cards.Values.Any(c => !c.Finished)) PauseNow();
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
                if (Str(e, "status") == "waiting_for_limit" && DateTime.TryParse(Str(e, "until"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
                {
                    waitingUntil = until;
                    SetState(SessionState.Paused, F("{0}'s limit: trying again {1}", Who, PlanUsage.When(until)));
                    doingText.ToolTip = Str(e, "detail");
                }
                else if (Str(e, "status") == "requesting" && State == SessionState.Paused) { waitingUntil = null; SetState(SessionState.Thinking, F("Asking {0}…", Who)); }
                else if (Str(e, "status") == "compacting") Compacting();
                else if (Str(e, "compact_result") is { } compactResult && compactResult != "success") CompactEnded(null, failed: true);
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
                var meta = Obj(e, "compact_metadata");
                CompactEnded(meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("post_tokens", out _) ? (Num(meta, "pre_tokens"), Num(meta, "post_tokens")) : null, failed: false);
                break;
        }
    }

    /// <summary>Claude Code compacts the conversation (/compact, or by itself when it's near full): it says so, in
    /// the chat and the dashboard, until it's done.</summary>
    private void Compacting()
    {
        SetState(SessionState.Thinking, T("Compacting the conversation…"));
        turnStart ??= DateTime.UtcNow;
        if (compactingLine != null) return;
        compactingLine = new NoteLine(T("Compacting the conversation…"), icon: "✂");
        Add(compactingLine);
        ScrollToEnd();
    }

    /// <summary>Compacted (with the context before and after, when Claude Code gives them), or it failed: the line
    /// says so, and the context ring shows what's left.</summary>
    private void CompactEnded((long Pre, long Post)? tokens, bool failed)
    {
        string text = failed ? T("Compacting the conversation didn't work.")
            : tokens is { } t ? F("The conversation was compacted (summarised): {0} → {1} tokens of context.", Palette.Tokens(t.Pre), Palette.Tokens(t.Post))
            : T("The conversation was compacted (summarised) to make room.");
        if (compactingLine != null) compactingLine.Set(text, failed);
        else Add(new NoteLine(text, error: failed, icon: "✂"));
        compactingLine = null;
        if (!failed)
        {
            compactedTurn = true;
            if (tokens is { } after) ShowContext(after.Post);
            if (State == SessionState.Thinking) SetState(SessionState.Thinking, T("Compacted"));
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
        bool wasPausing = pausing;
        pausing = false;
        string line = compactedTurn && Num(e, "num_turns") <= 1 && Num(u, "output_tokens") == 0 ? F("Compacted in {0}", Palette.Time(seconds))
            : F("Done in {0} · {1} steps · {2} tokens out", Palette.Time(seconds), Num(e, "num_turns"), Palette.Tokens(Num(u, "output_tokens")));
        compactedTurn = false;
        if (compactingLine != null) CompactEnded(null, failed: error);
        string resultText = Str(e, "result") ?? "";
        // (stopped to pause: not an error)
        if (error && !wasPausing) Add(new NoteLine(resultText.Length > 0 ? resultText : Str(e, "subtype") ?? T("Something went wrong"), error: true));
        Add(new NoteLine(line, icon: error ? "⚠" : "✔"));
        EndNotes();
        turnStart = null;
        foreach (var c in cards.Values.Where(c => !c.Finished && c.Started < DateTime.UtcNow.AddSeconds(-1))) c.SetResult("", false);
        // your plan's limit: Claude Code's own message said so (a turn that isn't an error), or the result does
        bool atLimit = limitHit || (error && System.Text.RegularExpressions.Regex.IsMatch(resultText, LimitPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (atLimit && limitResets == null) limitResets = ResetTime(resultText);
        if (atLimit) Logging.Log.Here.Info($"Session {Info.Id}: Claude's limit reached{(limitResets is { } lr ? $", it resets {lr:g}" : "")}");
        limitHit = false;
        waitingUntil = null;
        // (a session on another PC carries on, and keeps its notes, there)
        if (atLimit && settings.AutoResume && IsClaude && !IsRemote) PlanResume();
        else if (wasPausing) Paused();
        else SetState(error ? SessionState.Error : SessionState.Idle, "");
        limitResets = null;
        // done: the next message waiting, or (when none is) the session notes
        if (!error && !atLimit && !wasPausing)
        {
            if (queued.Count > 0) SendQueued();
            else if (!IsRemote) WriteNotes();
        }
        ScrollToEnd();
    }

    /// <summary>Session notes (Settings → AI): a request done, the AI is asked to bring CLAUDE.md (and README.md) in
    /// the folder up to date. Once per request: the notes' own turn doesn't ask again.</summary>
    private void WriteNotes()
    {
        if (!settings.KeepNotes || !notesDue || proc is not { Running: true }) return;
        string full = Path.GetFullPath(Info.Folder).TrimEnd('\\');
        if (full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || Path.GetPathRoot(full)?.TrimEnd('\\') == full)
        {
            // CLAUDE.md here would be read by every project under it
            if (!notesSkipped) Add(new NoteLine(T("Session notes aren't written in your user folder or a drive's root: start the session in a project's folder for them."), icon: "\U0001F4DD"));
            notesSkipped = true;
            return;
        }
        StartNotes();
        // (said while drawing the result: to the PCs that have the session open too)
        Share(new JsonObject { ["type"] = "daisy_note", ["text"] = NotesLine, ["icon"] = NotesGroup.Icon });
        Send(NotesMessage, bubble: false);
        notesDue = false;
    }

    private string NotesMessage => settings.NotesMessage.Trim().Length > 0 ? settings.NotesMessage.Trim() : AiSettings.DefaultNotesMessage;

    private static string NotesLine => T("Bringing the session notes (CLAUDE.md) up to date");

    /// <summary>The notes' request, now or read back (and the earlier default ones, in older conversations).</summary>
    private bool IsNotesMessage(string text) => text.Trim() == NotesMessage || AiSettings.EarlierNotesMessages.Contains(text.Trim());

    /// <summary>
    /// The notes' turn starts: one line, with what it does under it (closed), until its turn is over. It goes above
    /// the reply before it (when the last thing said was a reply), so that reply stays at the bottom, to be read.
    /// </summary>
    private void StartNotes()
    {
        EndNotes();
        notesGroup = new NotesGroup(NotesLine);
        var all = older ?? chat.Children.Cast<UIElement>().ToList();
        int reply = all.FindLastIndex(x => x is AssistantText);
        bool replyLast = reply >= 0 && !all.Skip(reply).Any(x => x is UserBubble or NotesGroup);
        if (older != null)
        {
            if (replyLast) older.Insert(reply, notesGroup);
            else older.Add(notesGroup);
        }
        else if (replyLast) chat.Children.Insert(reply, notesGroup);
        else chat.Children.Add(notesGroup);
    }

    private void EndNotes()
    {
        notesGroup?.Done();
        notesGroup = null;
    }

    private void ControlRequest(JsonElement e)
    {
        string requestId = Str(e, "request_id") ?? "";
        var r = Obj(e, "request");
        if (Str(r, "subtype") != "can_use_tool") return;
        string tool = Str(r, "tool_name") ?? "Tool";
        var toolInput = Obj(r, "input").Clone();
        // (Claude's own questions: always shown, even with everything allowed, since only you can answer them)
        if (tool == "AskUserQuestion") { AskQuestions(e, requestId, r, toolInput); return; }
        // (never asking: Settings → AI, which is this PC's, not the other's for a session there; or Allow all this session)
        if (autoAllow || (settings.SkipPermissions && !IsRemote)) { proc?.Answer(requestId, true, toolInput); return; }
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
            Palette.ToolKind.Change => F("Let {0} change {1}?", Who, what),
            Palette.ToolKind.Run => F("Let {0} run this? {1}", Who, what),
            Palette.ToolKind.Web => F("Let {0} go to {1}?", Who, what),
            _ => F("Let {0} use {1}? {2}", Who, tool, what),
        };
        var me = proc;
        asks[requestId] = card;
        notesGroup?.Open();
        if (!IsRemote && Shared != null) { pendingAsks[requestId] = e.Clone(); Shared(e); }
        else if (!IsRemote) pendingAsks[requestId] = e.Clone();
        card.Ask(question, (allow, all) =>
        {
            if (all) autoAllow = true;
            asks.Remove(requestId);
            pendingAsks.Remove(requestId);
            me?.Answer(requestId, allow, toolInput, allow ? null : T("The user said no to this. Ask them what they'd like instead."));
            if (!IsRemote) Share(new JsonObject { ["type"] = "daisy_answered", ["request_id"] = requestId, ["allow"] = allow });
            SetState(allow ? SessionState.Tool : SessionState.Thinking, allow ? F("{0}: {1}", tool, what) : T("Told no"));
        }, IsRemote ? null : () => settings.AllowEverything(true));
        SetState(SessionState.Waiting, F("Asking you: {0}", tool));
        ScrollToEnd(force: true);
        NeedYou(F("{0} is asking to go on", Who), question);
    }

    /// <summary>
    /// Claude's own questions (AskUserQuestion): a card to answer them on, never answered for you. The answers go back
    /// as Claude Code expects them, in the tool's input ("answers": question → answer); Skip says no, so it carries on
    /// without.
    /// </summary>
    private void AskQuestions(JsonElement e, string requestId, JsonElement r, JsonElement toolInput)
    {
        string? id = Str(r, "tool_use_id");
        ToolCard? card = id != null && cards.TryGetValue(id, out var c) ? c : cards.Values.LastOrDefault(x => x.Tool == "AskUserQuestion" && !x.Finished);
        if (card == null)
        {
            card = new ToolCard(id ?? Guid.NewGuid().ToString(), "AskUserQuestion") { Root = Info.Folder };
            cards[card.Id] = card;
            Add(card);
            card.SetInput(toolInput);
        }
        var me = proc;
        asks[requestId] = card;
        if (!IsRemote) { pendingAsks[requestId] = e.Clone(); Shared?.Invoke(e); }
        card.AskQuestions(toolInput, answers =>
        {
            asks.Remove(requestId);
            pendingAsks.Remove(requestId);
            AnswerQuestions(me, requestId, toolInput, answers);
            if (!IsRemote) Share(new JsonObject { ["type"] = "daisy_answered", ["request_id"] = requestId, ["allow"] = answers != null });
            SetState(SessionState.Thinking, answers != null ? T("Answered") : T("Skipped the question"));
        });
        SetState(SessionState.Waiting, T("Asking you a question"));
        notesGroup?.Open();
        ScrollToEnd(force: true);
        var first = Obj(toolInput, "questions") is { ValueKind: JsonValueKind.Array } qs && qs.GetArrayLength() > 0 ? Str(qs[0], "question") : null;
        NeedYou(F("{0} has a question for you", Who), first ?? T("It's waiting for your answer."));
    }

    /// <summary>The answers (or Skip, null) to the agent: the tool's input with "answers" added.</summary>
    private static void AnswerQuestions(IAgent? agent, string requestId, JsonElement input, JsonObject? answers)
    {
        if (answers == null)
        {
            agent?.Answer(requestId, false, input, T("The user skipped the question without answering. Carry on with your best judgement, or ask them in your reply."));
            return;
        }
        var updated = JsonNode.Parse(input.GetRawText()) as JsonObject ?? new JsonObject();
        updated["answers"] = answers;
        using var doc = JsonDocument.Parse(updated.ToJsonString());
        agent?.Answer(requestId, true, doc.RootElement.Clone());
    }

    /// <summary>
    /// It's waiting for you: if you aren't looking at this tab (another tab or app in front, or the window in the tray),
    /// a Windows notification (clicking it opens the window) and the taskbar button flashing.
    /// </summary>
    private void NeedYou(string title, string text)
    {
        if (settings.QuestionSound) PlayAsk();
        var w = Window.GetWindow(this);
        bool looking = w is { IsActive: true, IsVisible: true } && IsVisible;
        if (looking) return;
        string where = Info.Name.Length > 0 ? Info.Name + ": " : "";
        DaisysApp.Shell.Notifications.Show(title, where + (text.Length > 200 ? text[..200] + "…" : text));
        if (w is { IsVisible: true }) FlashWindow(w);
    }

    /// <summary>Each request's rate-limit report: to the plan's usage (shared by every tab, PlanUsage).</summary>
    private void RateLimit(JsonElement info)
    {
        // (a session on another PC: that PC's plan, maybe not yours; on another account: that account's)
        if (OnOwnAccount)
        {
            ownPlan = PlanUsage.Merge(ownPlan, info, Account?.Label);
            ShowPlan();
            PlanChanged?.Invoke();
        }
        else if (!IsRemote) usage.FromRateLimit(info);
        if (Str(info, "status") == "rejected") limitHit = true;
        if (Str(info, "status") == "rejected" && DateTime.Now - limitNoted > TimeSpan.FromMinutes(5))
        {
            limitNoted = DateTime.Now;
            var back = OnOwnAccount ? ownPlan?.BlockedUntil : usage.BlockedUntil;
            Add(new NoteLine(back is { } b ? F("Your plan's limit is reached: it resets {0} ({1}). You'll get a notification when it has.", PlanUsage.When(b), PlanUsage.Until(b)) : T("Your plan's limit is reached."), error: true));
        }
    }

    /// <summary>Settings → AI changed: never asking (switched at once in a running session), and the look.</summary>
    private void OptionsChanged()
    {
        ShowSkip();
        ShowTotals();
        // (this PC's Allow everything isn't the other PC's, for a session there)
        if (IsRemote) return;
        if (proc is { Running: true })
            proc.Control("set_permission_mode", new() { ["mode"] = settings.SkipPermissions ? "bypassPermissions" : Info.Mode });
        // a question waiting now is answered too
        if (settings.SkipPermissions)
            foreach (var c in cards.Values.Where(c => c.Asking)) c.AnswerNow(true);
    }

    private void ShowSkip()
    {
        bool skip = settings.SkipPermissions && !IsRemote;
        mode.IsEnabled = !skip;
        mode.ToolTip = skip ? T("Everything is allowed (Settings → AI)") : null;
    }

    /// <summary>The conversation's colours and sizes, and the box's, from the look (Settings → AI).</summary>
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
        input.Paint(Look.Brush(a.InputBackground), Look.Brush(a.InputText));
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
        // a session on another PC: that PC's plan (as its server sends it), never this one's; on another account than
        // Claude Code's own: that account's, from its own reports
        bool own = OnOwnAccount;
        var there = IsRemote ? remotePlan : own ? ownPlan : null;
        Show(fiveHour, IsRemote || own ? there?.Session : usage.Session);
        Show(sevenDay, IsRemote || own ? there?.Week : usage.Week);
        fiveHour.Visibility = sevenDay.Visibility = IsClaude && (!IsRemote || there != null) ? Visibility.Visible : Visibility.Collapsed;
        planWhere.Text = there == null && !own ? "" : string.Join(" · ", new[]
        {
            IsRemote ? F("On {0}", Info.RemotePc ?? Info.Remote) : null,
            there?.Account ?? Account?.Label,
            there is { Blocked: true } ? there.BlockedUntil is { } b ? F("limit reached: back {0}", PlanUsage.When(b)) : T("limit reached") : null,
        }.Where(x => !string.IsNullOrEmpty(x)));
        // (the Claude Account box lists every account's usage, this one's in bold: since 3.11.1, and on another PC since 3.15.3)
        planWhere.Visibility = Visibility.Collapsed;
        // (on another PC: that PC's accounts, when its server lists them, for Claude Code)
        bool there2 = IsRemote && IsClaude && sessionAccounts.Count > 0;
        if (accountBox != null) accountBox.Visibility = !IsRemote || there2 ? Visibility.Visible : Visibility.Collapsed;
        Tools.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- carrying on after a limit

    /// <summary>
    /// Claude's plan limit stopped the turn: it carries on by itself once the limit resets (when the plan's usage says,
    /// or in half an hour if it doesn't), sending the resume message (Settings → AI). Kept across restarts.
    /// </summary>
    private void PlanResume()
    {
        var now = DateTime.Now;
        // when Claude Code said it resets; else when the plan's usage says; else in half an hour
        DateTime? reset = limitResets is { } said && said > now ? said : null;
        bool own = OnOwnAccount;
        reset ??= (own ? ownPlan?.BlockedUntil : usage.BlockedUntil) is { } blocked && blocked > now ? blocked : null;
        reset ??= (own ? ownPlan?.Windows ?? new() : usage.Windows).Where(w => w.Used >= 0.99 && w.Resets > now).Select(w => w.Resets).Max();
        Info.ResumeAt = (reset ?? now.AddMinutes(30)).AddMinutes(1);
        settings.Save();
        Logging.Log.Here.Info($"Session {Info.Id}: carries on by itself at {Info.ResumeAt:g}");
        Add(new NoteLine(F("Claude's limit is reached. This session carries on by itself {0} ({1}); Stop cancels that.", PlanUsage.When(Info.ResumeAt.Value), PlanUsage.Until(Info.ResumeAt.Value)), icon: "\u23F3"));
        SetState(SessionState.Paused, ResumeText());
        resumeTimer.Start();
    }

    private string ResumeText() => Info.ResumeAt is { } at ? F("Carries on by itself {0}", PlanUsage.When(at)) : "";

    private void ResumeIfDue()
    {
        if (Info.ResumeAt is not { } at) { resumeTimer.Stop(); return; }
        // as soon as the plan's usage says the limit's gone, or at the time
        // (on another account than Claude Code's own, only at the time: its usage isn't asked for)
        bool free = !OnOwnAccount && usage.Checked > DateTime.Now.AddMinutes(-3) && !usage.Blocked && usage.Windows.All(w => w.Used < 0.99);
        if (DateTime.Now < at && !(free && DateTime.Now > at.AddMinutes(-10))) return;
        Info.ResumeAt = null;
        settings.Save();
        resumeTimer.Stop();
        Logging.Log.Here.Info($"Session {Info.Id}: the limit has reset, carrying on");
        Add(new NoteLine(T("The limit has reset: carrying on."), icon: "\u25B6"));
        SetState(SessionState.Idle, "");
        Send(ResumeMessage, bubble: true);
    }

    /// <summary>
    /// When Claude Code's message says the limit resets: "resets 9:20am (America/New_York)", "resets Oct 21, 5pm (…)",
    /// or "…|1760000000"; in this PC's time. Null if it doesn't say.
    /// </summary>
    internal static DateTime? ResetTime(string text)
    {
        var epoch = System.Text.RegularExpressions.Regex.Match(text, @"\|(\d{10})\b");
        if (epoch.Success) return DateTimeOffset.FromUnixTimeSeconds(long.Parse(epoch.Groups[1].Value)).LocalDateTime;
        var m = System.Text.RegularExpressions.Regex.Match(text,
            @"resets?\s+(?:at\s+)?(?:(?<mon>[A-Z][a-z]{2,8})\s+(?<day>\d{1,2}),?\s+(?:at\s+)?)?(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?(?:\s*\((?<tz>[^)]+)\))?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        int hour = int.Parse(m.Groups["h"].Value), minute = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value) : 0;
        string ap = m.Groups["ap"].Value.ToLowerInvariant();
        if (ap == "pm" && hour < 12) hour += 12;
        if (ap == "am" && hour == 12) hour = 0;
        if (hour > 23 || minute > 59) return null;
        var zone = TimeZoneInfo.Local;
        if (m.Groups["tz"].Success)
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(m.Groups["tz"].Value.Trim()); } catch { }
        var there = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        DateTime at;
        if (m.Groups["mon"].Success && DateTime.TryParseExact(m.Groups["mon"].Value[..3], "MMM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month))
        {
            at = new DateTime(there.Year, month.Month, Math.Min(int.Parse(m.Groups["day"].Value), DateTime.DaysInMonth(there.Year, month.Month)), hour, minute, 0);
            if (at < there.AddDays(-1)) at = at.AddYears(1);
        }
        else
        {
            at = there.Date.AddHours(hour).AddMinutes(minute);
            if (at <= there) at = at.AddDays(1);
        }
        try { return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(at, DateTimeKind.Unspecified), zone).ToLocalTime(); }
        catch { return null; }
    }

    // ---------------------------------------------------------------- the dashboard

    /// <summary>Claude Code for this tab closed (the session is kept: sending again resumes it).</summary>
    private void EndProcess()
    {
        restartForAccount = false;
        var p = proc;
        proc = null;
        p?.Dispose();
        SetState(SessionState.Offline, "");
    }

    /// <summary>
    /// Claude Code's own sign-in changed (signed in to another account some other way): a session running on it ends
    /// (now, or once the turn it's on is done), so the next message starts it again on its account; one with none picked
    /// that's waiting for the old account's limit carries on at once. Sessions in an account's own folder carry on.
    /// </summary>
    public void AccountSwitched()
    {
        if (IsRemote || !IsClaude || (proc != null && procAccountFolder != null)) return;
        Restart(carryOn: Info.Account == null);
        ShowPlan();
    }

    /// <summary>This session switched to another Claude account (its dashboard's Claude Account box): it starts on it
    /// with its next message. One waiting for a limit keeps waiting (since 3.9.1; it carried on at once before): only
    /// the usage shown changes, and it goes on when you send something, or by itself at the time it was waiting for.</summary>
    public void UseAccount(string? id)
    {
        if (IsRemote && IsClaude && id != null) { PickSessionAccount(id); return; }
        if (IsRemote || !IsClaude || id == Info.Account) return;
        Info.Account = id;
        settings.Save();
        ownPlan = null;
        Logging.Log.Here.Info($"Session {Info.Id}: on account {Account?.Email ?? "(Claude Code's own)"} from its next message");
        ShowPlan();
        PlanChanged?.Invoke();
        Restart(carryOn: false);
        _ = RefreshPlanAsync();
    }

    /// <summary>Claude Code ends (now, or once the turn it's on is done), so the next message starts it again on the
    /// account it's on now; waiting for a limit, it carries on at once (carryOn).</summary>
    private void Restart(bool carryOn)
    {
        if (Info.ResumeAt != null && carryOn)
        {
            Info.ResumeAt = null;
            settings.Save();
            resumeTimer.Stop();
            if (proc != null) EndProcess();
            Logging.Log.Here.Info($"Session {Info.Id}: another account, carrying on");
            Add(new NoteLine(T("Switched to another Claude account: carrying on."), icon: "▶"));
            SetState(SessionState.Idle, "");
            Send(ResumeMessage, bubble: true);
            return;
        }
        if (proc == null) return;
        // (waiting for a limit, its turn is over: Claude Code ends now, the session still waiting)
        if (Info.ResumeAt != null && !Busy)
        {
            restartForAccount = false;
            var old = proc;
            proc = null;
            old.Dispose();
            return;
        }
        if (Busy || State == SessionState.Paused) restartForAccount = true;
        else EndProcess();
    }

    /// <summary>
    /// Its plan's usage read again (the refresh icon on the Usage button): on another account than Claude Code's own,
    /// "/usage" asked in that account's folder (no model is asked); else Claude Code's own (PlanUsage).
    /// </summary>
    public async Task RefreshPlanAsync()
    {
        if (IsRemote) { proc?.Control("refresh_usage", null); return; }
        if (IsRemote || !IsClaude) return;
        if (!OnOwnAccount) { await usage.CheckAsync(); return; }
        if (readingPlan || findExe() is not { } exe || Account is not { } account) return;
        readingPlan = true;
        try
        {
            string? folder;
            try { folder = ClaudeAccounts.FolderFor(settings, account.Id); }
            catch (Exception ex) { Logging.Log.Here.Warn($"Session {Info.Id}: the account's folder: {ex.Message}"); return; }
            if (folder == null) { await usage.CheckAsync(); return; }
            var (windows, problem) = await PlanUsage.ReadAsync(exe, folder);
            if (Account?.Id != account.Id) return;
            if (windows.Count > 0)
                ownPlan = new PlanUsage.Elsewhere(windows, ownPlan?.Blocked == true && windows.Any(w => w.Used >= 0.99), ownPlan?.BlockedUntil, account.Label);
            else Logging.Log.Here.Info($"Session {Info.Id}: {account.Email}'s usage: {problem}");
            ShowPlan();
            PlanChanged?.Invoke();
        }
        finally { readingPlan = false; }
    }

    /// <summary>Its plan's usage, for a PC that has it open ({op: "usage"}): its own account's, or Claude Code's.</summary>
    public JsonObject PlanToRemote()
    {
        var o = OnOwnAccount
            ? PlanUsage.ToRemote(ownPlan?.Windows ?? new(), ownPlan?.Blocked ?? false, ownPlan?.BlockedUntil, Account?.Label)
            : usage.ToRemote();
        // (which account it runs on, by id: the other PC's Claude Account box follows it; and, since 3.15.2, every
        // account's usage for its list)
        o["sessionAccount"] = SessionAccountId;
        if (IsClaude && !IsRemote)
        {
            o["accounts"] = AccountsUsage.ToRemote(settings, usage);
            o["ownAccount"] = settings.ActiveAccount;
            // (since 3.15.3: Claude Code's own sign-in's usage in full, for the other PC's usage details)
            o["own"] = usage.ToRemote();
        }
        return o;
    }

    /// <summary>A session on another PC: its Claude Account box lists that PC's accounts, the one it runs on chosen.</summary>
    private void FillSessionAccounts()
    {
        ShowPlan();
        PlanChanged?.Invoke();
    }

    /// <summary>Another of that PC's accounts clicked: that PC switches the session to it, as its own box would.</summary>
    private void PickSessionAccount(string id)
    {
        if (!IsRemote || id == sessionAccount) return;
        sessionAccount = id;
        FillSessionAccounts();
        Logging.Log.Here.Info($"Session {Info.Id}: account picked for it on {Info.RemotePc ?? Info.Remote}");
        proc?.Control("set_account", new() { ["account"] = id });
    }

    private void SetState(SessionState state, string what)
    {
        // switched to another account while it worked: Claude Code ends now that the turn's done
        if (restartForAccount && !(state is SessionState.Starting or SessionState.Thinking or SessionState.Writing or SessionState.Tool or SessionState.Waiting or SessionState.Paused))
        {
            restartForAccount = false;
            Dispatcher.BeginInvoke(() => { if (!Busy && State != SessionState.Paused && proc != null) EndProcess(); });
        }
        bool changedState = state != State;
        State = state;
        doing = what;
        orb.State = state;
        stateText.Text = state switch
        {
            SessionState.Starting => T("Starting"),
            SessionState.Thinking => compactingLine != null ? T("Compacting") : T("Thinking"),
            SessionState.Writing => T("Writing"),
            SessionState.Tool => T("Working"),
            SessionState.Waiting => T("Needs you"),
            SessionState.Error => T("Stopped with an error"),
            SessionState.Paused => paused ? T("Paused") : T("Waiting for the limit"),
            SessionState.Offline => Info.Started ? T("Not running") : T("Ready"),
            _ => T("Done"),
        };
        stateText.Foreground = Palette.Brush(Palette.Of(state));
        doingText.Text = what;
        doingText.ToolTip = string.IsNullOrEmpty(what) ? null : what;
        stop.Visibility = Busy || (state == SessionState.Paused && !paused) ? Visibility.Visible : Visibility.Collapsed;
        // Pause while it works; Resume once it's paused
        pause.Visibility = Busy || paused ? Visibility.Visible : Visibility.Collapsed;
        contextButton.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
        pause.Content = paused ? T("Resume") : pausing ? T("Pausing…") : T("Pause");
        pause.IsEnabled = !pausing;
        pause.ToolTip = paused ? T("Carry on where it paused") : T("Pause once the step it's on is done (it isn't stopped in the middle of anything); Resume carries on");
        if (paused) pause.SetResourceReference(StyleProperty, "AccentButton"); else pause.ClearValue(StyleProperty);
        if (Busy || state == SessionState.Paused) tick.Start();
        Tick();
        // (the title bar's plan bubble shows only while a Claude Code session here works)
        usage.Working(this, Busy && IsClaude && !IsRemote && !OnOwnAccount);
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
        ShowActivity();
        clock.Text = turnStart is { } s && Busy ? Palette.Time((now - s).TotalSeconds) : "";
        if (State == SessionState.Paused && (Info.ResumeAt ?? waitingUntil) is { } at) clock.Text = F("carries on {0}", PlanUsage.Until(at));
        if (!Busy && State != SessionState.Paused) tick.Stop();
    }

    /// <summary>The turn's time by kind (thinking, writing, reading, editing, running…), under the ribbon.</summary>
    private void ShowActivity()
    {
        activity.Children.Clear();
        activity.RowDefinitions.Clear();
        var kinds = ribbon.Summary().ToList();
        int rows = (kinds.Count + 1) / 2;
        for (int r = 0; r < rows; r++) activity.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < kinds.Count; i++)
        {
            var (group, colour, seconds) = kinds[i];
            var t = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            t.Inlines.Add(new System.Windows.Documents.Run("\u25CF ") { Foreground = Palette.Brush(colour) });
            t.Inlines.Add(new System.Windows.Documents.Run(group + " " + Palette.Time(seconds)));
            Grid.SetRow(t, i % rows);
            Grid.SetColumn(t, i / rows);
            activity.Children.Add(t);
        }
    }

    /// <summary>A kind of tool's name, for the activity's times.</summary>
    private static string KindName(Palette.ToolKind kind) => kind switch
    {
        Palette.ToolKind.Look => T("Reading"),
        Palette.ToolKind.Change => T("Editing"),
        Palette.ToolKind.Run => T("Running"),
        Palette.ToolKind.Web => T("Web Access"),
        Palette.ToolKind.Agent => T("Agents"),
        Palette.ToolKind.Plan => T("Planning"),
        _ => T("Other Tools"),
    };

    private void Context(JsonElement usage)
    {
        long used = Num(usage, "input_tokens") + Num(usage, "cache_read_input_tokens") + Num(usage, "cache_creation_input_tokens");
        if (used <= 0) return;
        ShowContext(used);
    }

    /// <summary>The context ring: how much of the model's window the conversation fills (after a compaction too).</summary>
    private void ShowContext(long used)
    {
        context = used;
        long limit = proc?.ContextWindow is int known ? known
            : modelId.Contains("1m", StringComparison.OrdinalIgnoreCase) || used > 200_000 ? 1_000_000 : 200_000;
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
            (Info.OutputTokens / all, Color.FromRgb(0xF0, 0x88, 0x3E), F("Written by {0}: {1}", Who, Palette.Tokens(Info.OutputTokens))),
        }, Palette.Tokens(sum), T("Cache read (teal), cache written (blue), sent fresh (purple), written by the model (orange)"));
        cost.Visibility = settings.ShowApiCost ? Visibility.Visible : Visibility.Collapsed;
        if (!IsClaude) { cost.Text = P(Info.Turns, "{0} turn", "{0} turns"); cost.ToolTip = null; return; }
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

        FillModels();
        model.SelectionChanged += (_, _) =>
        {
            if (fillingModels || model.SelectedItem is not ComboBoxItem { Tag: string v } || v == Info.Model) return;
            Info.Model = v;
            if (IsClaude && !IsRemote) settings.Model = v;
            settings.Save();
            proc?.Control("set_model", new() { ["model"] = v.Length > 0 ? v : null });
        };
        foreach (var (value, name) in Modes())
            mode.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Mode) ?? mode.Items[0];
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not ComboBoxItem { Tag: string v } || v == Info.Mode) return;
            Info.Mode = v;
            if (!IsRemote) settings.Mode = v;
            autoAllow = false;
            settings.Save();
            proc?.Control("set_permission_mode", new() { ["mode"] = v });
        };
        providerText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(providerText);
        s.Children.Add(Labelled(T("Model"), model));
        s.Children.Add(Labelled(T("Permissions"), mode));
        ShowSkip();

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var compact = new Button { Content = T("Compact"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("Summarise the conversation so far to free up the context (/compact)") };
        compact.Click += (_, _) => Send("/compact", bubble: true);
        var copy = new Button { Content = IconText("\uE8C8", T("Sess ID")), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = F("{0}: claude --resume {0} opens it in a terminal", Info.Id) };
        copy.Click += (_, _) => { try { Clipboard.SetText(Info.Id); } catch { } };
        System.Windows.Automation.AutomationProperties.SetName(copy, T("Copy session id"));
        var export = new Button { Content = T("Export"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = T("Save the session as a file, to carry on with it on another PC (Import a session file… in a new tab there)") };
        export.Click += (_, _) => ExportFile();
        actions.Children.Add(compact);
        actions.Children.Add(export);
        actions.Children.Add(copy);
        s.Children.Add(actions);
        return s;
    }

    /// <summary>The permission modes: Claude Code's value, and its name here.</summary>
    private static (string Value, string Name)[] Modes() => new[]
    {
        ("manual", T("Ask before acting")), ("acceptEdits", T("Accept edits, ask the rest")), ("auto", T("Auto")),
        ("plan", T("Plan only (no changes)")), ("bypassPermissions", T("Don't ask (bypass permissions)")),
    };

    private bool fillingModels;

    /// <summary>The Model box: Claude Code's models, or the provider's (its own default first).</summary>
    private void FillModels()
    {
        fillingModels = true;
        model.Items.Clear();
        if (IsClaude)
            foreach (var (value, name) in new[] { ("", T("Claude Code's default")), ("opus", "Opus"), ("sonnet", "Sonnet"), ("haiku", "Haiku"), ("fable", "Fable") })
                model.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        else if (IsRemote)
        {
            // (its models are the other PC's: its default, or the one it uses)
            model.Items.Add(new ComboBoxItem { Content = F("{0}'s default", Info.RemoteWho ?? "?"), Tag = "" });
            if (Info.Model.Length > 0) model.Items.Add(new ComboBoxItem { Content = Info.Model, Tag = Info.Model });
        }
        else if (ApiProvider is { } p)
        {
            model.Items.Add(new ComboBoxItem { Content = F("{0}'s default ({1})", p.Name, p.Model), Tag = "" });
            foreach (var m in p.Models.Append(Info.Model).Where(m => m.Length > 0 && m != p.Model).Distinct()) model.Items.Add(new ComboBoxItem { Content = m, Tag = m });
        }
        model.SelectedItem = model.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Model) ?? (model.Items.Count > 0 ? model.Items[0] : null);
        providerText.Text = IsRemote ? F("On {0} (remote control) · {1}", Info.RemotePc ?? Info.Remote, IsClaude ? T("Claude Code") : Info.RemoteWho ?? "?")
            : IsClaude ? T("Claude Code") : ApiProvider is { } pp ? F("{0} · {1}", pp.Name, pp.BaseUrl) : T("(provider not set up)");
        fillingModels = false;
        ShowTotals();
        ShowPlan();
    }

    private readonly TextBlock providerText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 0) };

    private void ShowFolder() => folderText.Text = Info.Folder.Length > 0 ? Info.Folder + (IsRemote ? F(" (on {0})", Info.RemotePc ?? Info.Remote) : "") : T("(no folder)");

    // ---------------------------------------------------------------- a new tab: its name, which AI, the folder, a previous session

    /// <summary>The Host choice for a session on another PC (remote control).</summary>
    private const string RemoteChoice = "\u0001remote";
    private const double SavedRowHeight = 35;

    private void BuildStartPanel()
    {
        startPanel.Visibility = Info.Started || IsRemote ? Visibility.Collapsed : Visibility.Visible;
        startPanel.SetResourceReference(StyleProperty, "Card");
        startPanel.Margin = new Thickness(0, 0, 12, 10);
        var s = new StackPanel();

        // ---- its name and where it runs (Host) at the left; its Claude account and permissions at the right
        nameBox.Text = Info.Name;
        nameBox.Margin = new Thickness(0, 6, 16, 12);
        nameBox.HorizontalAlignment = HorizontalAlignment.Left;
        nameBox.ToolTip = T("The session's name, on its tab (click the tab's name to change it later)");
        nameBox.TextChanged += (_, _) =>
        {
            string n = nameBox.Text.Trim();
            if (n.Length == 0 || n == Info.Name) return;
            Info.Name = n;
            settings.Save();
            Renamed?.Invoke();
        };
        var nameCol = new StackPanel();
        nameCol.Children.Add(Header(T("Name")));
        nameCol.Children.Add(nameBox);
        var who = new ComboBox { MinWidth = 260 };
        var providers = new Button { Content = T("Providers…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Set up Gemini, OpenAI, OpenRouter, Ollama and other models (their keys and settings)") };
        var whoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        whoRow.Children.Add(who);
        whoRow.Children.Add(providers);
        var whoCol = new StackPanel();
        whoCol.Children.Add(Header(T("Host")));
        whoCol.Children.Add(whoRow);
        whoCol.Margin = new Thickness(0, 0, 16, 0);
        // its permissions, from the start (the dashboard's Permissions, the same setting)
        var startMode = new ComboBox { MinWidth = 230, Margin = new Thickness(0, 6, 0, 12) };
        foreach (var (value, name) in Modes()) startMode.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        void ShowMode()
        {
            startMode.SelectedItem = startMode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Mode) ?? startMode.Items[0];
            startMode.IsEnabled = !settings.SkipPermissions || IsRemote;
            startMode.ToolTip = startMode.IsEnabled ? T("What it asks you before doing (it can be changed later in the dashboard)") : T("Everything is allowed (Settings → AI)");
        }
        ShowMode();
        startMode.SelectionChanged += (_, _) =>
        {
            if (startMode.SelectedItem is ComboBoxItem { Tag: string v } && v != Info.Mode)
                mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == v);
        };
        mode.SelectionChanged += (_, _) => ShowMode();
        settings.Changed += ShowMode;
        var modeCol = new StackPanel();
        modeCol.Children.Add(Header(T("Permissions")));
        modeCol.Children.Add(startMode);
        // which Claude account (this PC's, or the PC's picked under Host)
        accountCol.Children.Add(Header(T("Claude Account")));
        startAccounts = new AccountsUsage(settings, usage, findExe);
        startAccounts.Picked += id =>
        {
            if (remoteAccounts == null) { PickLocalAccount(id); return; }
            remoteAccountPick = id;
            startAccounts.Showing(id);
        };
        var addAccount = new Button { Content = T("Add an account…"), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        addAccount.Click += (_, _) =>
        {
            var w = new AddAccountWindow(settings, findExe) { Owner = Window.GetWindow(this) };
            w.ShowDialog();
            if (w.Added is { } added) PickLocalAccount(added.Id);
        };
        startLocal.Children.Add(addAccount);
        var accountsWrap = new StackPanel { Margin = new Thickness(0, 6, 0, 12) };
        accountsWrap.Children.Add(startAccounts);
        accountsWrap.Children.Add(startLocal);
        accountCol.Children.Add(accountsWrap);
        accountCol.Margin = new Thickness(0);
        settings.AccountsChanged += FillAccounts;
        Closing += () => settings.AccountsChanged -= FillAccounts;
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(nameCol);
        left.Children.Add(whoCol);
        var right = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        right.Children.Add(accountCol);
        right.Children.Add(modeCol);
        Grid.SetColumn(right, 1);
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(left);
        top.Children.Add(right);
        s.Children.Add(top);

        // ---- on this PC: the folder it works in, and the files to read first (ticked ones are sent before the first message)
        var here = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        here.Children.Add(Header(T("Working Directory")));
        var folder = new TextBox { Text = Info.Folder, Margin = new Thickness(0, 6, 0, 0) };
        var browse = new Button { Content = T("Browse…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 6, 0, 0) };
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(folder);
        here.Children.Add(row);
        var readList = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        here.Children.Add(readList);
        s.Children.Add(here);

        // ---- on another PC (remote control): its details (its sessions go in the row below)
        var there = new StackPanel { Visibility = Visibility.Collapsed };
        var thereSessions = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 20, 12) };
        s.Children.Add(there);

        // ---- side by side: the folder's previous sessions (or the other PC's sessions), and tabs closed before
        var closedBox = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        closedBox.Children.Add(Header(T("Recently Closed")));
        var closedList = new StackPanel();
        closedBox.Children.Add(new ScrollViewer { Content = closedList, MaxHeight = 3 * SavedRowHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) });
        var closedStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        closedStatus.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        closedBox.Children.Add(closedStatus);

        var prevBox = new StackPanel { Margin = new Thickness(0, 0, 20, 12) };
        prevBox.Children.Add(Header(T("Previous Sessions")));
        var saved = new StackPanel();
        var savedScroll = new ScrollViewer { Content = saved, MaxHeight = 3 * SavedRowHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
        var savedNone = Secondary(T("None in this folder."), new Thickness(0, 6, 0, 0));
        prevBox.Children.Add(savedScroll);
        prevBox.Children.Add(savedNone);
        // (by its id, from the web, your phone or another PC; or a file exported on another PC)
        var claudeOnly = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var reference = new TextBox { ToolTip = T("The id Claude Code gave you when it closed (claude --resume <id>, or just the id); a claude.ai/code link or session id from the web or the mobile app (brought here with --teleport)") };
        var open = new Button { Content = T("Open"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Open the session with this id or link") };
        var import = new Button { Content = T("Import a session file…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("A session exported on another PC (Session → Export…)") };
        var refRow = new DockPanel();
        DockPanel.SetDock(import, Dock.Right);
        DockPanel.SetDock(open, Dock.Right);
        refRow.Children.Add(import);
        refRow.Children.Add(open);
        refRow.Children.Add(reference);
        claudeOnly.Children.Add(refRow);
        prevBox.Children.Add(claudeOnly);
        var refStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 4, 0, 0) };
        prevBox.Children.Add(refStatus);
        claudeOnly.Visibility = IsClaude ? Visibility.Visible : Visibility.Collapsed;

        // (a hidden one takes no column: one left alone has the whole width)
        var lower = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        lower.Children.Add(prevBox);
        lower.Children.Add(thereSessions);
        lower.Children.Add(closedBox);
        s.Children.Add(lower);
        startPanel.Child = s;

        (string Where, string Who, bool Remote) Describe(SessionInfo c)
        {
            bool remote = c.Remote is { Length: > 0 };
            string where = remote ? F("on {0}", c.RemotePc ?? c.Remote) : c.Folder.Length > 0 ? Path.GetFileName(c.Folder.TrimEnd('\\')) : "";
            string who = remote ? F("On {0} (remote control) · {1}", c.RemotePc ?? c.Remote, c.RemoteWho ?? "?")
                : c.Provider.Length == 0 ? T("Claude Code") : settings.Providers.FirstOrDefault(p => p.Id == c.Provider)?.Name ?? "?";
            return (where, who, remote);
        }

        void SayClosed(string? why)
        {
            closedStatus.Text = why ?? "";
            closedStatus.Visibility = why == null ? Visibility.Collapsed : Visibility.Visible;
        }
        sayClosed = SayClosed;

        void ShowClosed()
        {
            var list = settings.Closed.Where(c => !settings.Sessions.Any(o => o.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            closedBox.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            closedList.Children.Clear();
            foreach (var c in list) closedList.Children.Add(ClosedRow(c));
        }

        // a closed tab: click it to open it here or take it off the list (as a previous session's row)
        FrameworkElement ClosedRow(SessionInfo c)
        {
            var (where, whoText, remote) = Describe(c);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            var openIt = new Button { Content = T("Open"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Carry on with it in this tab, where it left off") };
            openIt.SetResourceReference(StyleProperty, "AccentButton");
            var forget = new Button { Content = T("Remove"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Take it off this list (the session itself is kept)") };
            actions.Children.Add(openIt);
            actions.Children.Add(forget);
            var title = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3), ToolTip = c.Folder + "\n" + whoText,
                Content = new TextBlock { Text = $"{c.Name}   ·   {where}   ·   {c.ClosedAt:g}", TextTrimming = TextTrimming.CharacterEllipsis },
            };
            var line = new DockPanel { Height = SavedRowHeight - 3, Margin = new Thickness(0, 0, 4, 3), Tag = actions };
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(title);
            title.Click += (_, _) =>
            {
                bool show = actions.Visibility != Visibility.Visible;
                foreach (var other in closedList.Children.OfType<FrameworkElement>()) if (other.Tag is UIElement x) x.Visibility = Visibility.Collapsed;
                actions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };
            openIt.Click += async (_, _) =>
            {
                openIt.IsEnabled = false;
                SayClosed(await ReopenAsync(c));
                openIt.IsEnabled = true;
            };
            forget.Click += (_, _) => settings.ForgetClosed(c.Id);
            return line;
        }

        ShowClosed();
        settings.ClosedChanged += ShowClosed;
        Closing += () => settings.ClosedChanged -= ShowClosed;

        void Tell(string text, bool error)
        {
            refStatus.Text = text;
            refStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
        }

        void FillWho()
        {
            who.Items.Clear();
            who.Items.Add(new ComboBoxItem { Content = T("Local"), Tag = "", ToolTip = T("Claude Code, with your Claude sign-in, on this PC") });
            foreach (var p in settings.Providers) who.Items.Add(new ComboBoxItem { Content = F("Local: {0} ({1})", p.Name, p.Model), Tag = p.Id });
            if (settings.Remote.Connect)
                foreach (var pc in settings.Remote.Pcs.Where(p => p.Address.Trim().Length > 0))
                    who.Items.Add(new ComboBoxItem { Content = F("{0} (Remote)", pc.Label), Tag = RemoteChoice + pc.Id });
            who.SelectedItem = who.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Provider) ?? who.Items[0];
        }

        int docsLook = 0;
        async void ShowReadFirst(string path)
        {
            readFirst.Clear();
            int look = ++docsLook;
            var md = Directory.Exists(path) ? MarkdownFiles(path) : new();
            FillDocs(readList, md, path, new(), readFirst);
            if (!Directory.Exists(path)) return;
            // (the subfolders' READMEs, looked for off the UI thread: a big folder takes a while)
            // (at the app's start the tabs aren't there yet: their order names the docs, so it waits for them a moment)
            for (int i = 0; i < 50 && Shell.AppNavigation.TabsShown == null; i++) await Task.Delay(200);
            var modules = await Task.Run(() => ModuleDocs(path));
            if (look == docsLook && modules.Count > 0) FillDocs(readList, md, path, modules, readFirst);
        }

        void ShowSaved()
        {
            saved.Children.Clear();
            string path = Info.Folder;
            var list = !Directory.Exists(path) ? new() : IsClaude ? ClaudeCli.SavedSessions(path) : ApiAgent.SavedSessions(path);
            list = list.Where(x => !x.Id.Equals(Info.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            savedNone.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            savedScroll.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var x in list) saved.Children.Add(SavedRow(x));
        }

        // a previous session: click it to open it or delete it
        FrameworkElement SavedRow(ClaudeCli.Saved x)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            var openIt = new Button { Content = T("Open"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Carry on with it in this tab") };
            openIt.SetResourceReference(StyleProperty, "AccentButton");
            var delete = new Button { Content = T("Delete"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Delete it from this PC") };
            actions.Children.Add(openIt);
            actions.Children.Add(delete);
            var title = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3), ToolTip = T("Click to open it or delete it") + "\n" + x.Id,
                Content = new TextBlock { Text = $"{x.Title}   ·   {x.When:g}", TextTrimming = TextTrimming.CharacterEllipsis },
            };
            var line = new DockPanel { Height = SavedRowHeight - 3, Margin = new Thickness(0, 0, 4, 3), Tag = actions };
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(title);
            title.Click += (_, _) =>
            {
                bool show = actions.Visibility != Visibility.Visible;
                foreach (var other in saved.Children.OfType<FrameworkElement>()) if (other.Tag is UIElement a) a.Visibility = Visibility.Collapsed;
                actions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            };
            openIt.Click += (_, _) => { if (Adopt(x.Id, x.Title, null) is { } why) Tell(why, true); };
            // (one open in another tab isn't deleted: the first click says so)
            ConfirmButton.Arm(delete, () =>
            {
                if (OpenElsewhere() is { } open)
                {
                    Tell(F("That session is open in the tab {0}: close it there first.", open.Name), true);
                    return;
                }
                try
                {
                    if (IsClaude) ClaudeCli.DeleteSession(Info.Folder, x.Id); else ApiAgent.Delete(x.Id);
                    Tell(F("Deleted \"{0}\".", x.Title), false);
                }
                catch (Exception ex) { Tell(F("It couldn't be deleted: {0}", ex.Message), true); }
                ShowSaved();
            }, skip: () => OpenElsewhere() != null);
            SessionInfo? OpenElsewhere() => settings.Sessions.FirstOrDefault(o => o != Info && o.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase));
            return line;
        }

        void Use(string path)
        {
            Info.Folder = path;
            settings.LastFolder = path;
            settings.Save();
            ShowFolder();
            ShowSaved();
            ShowReadFirst(path);
        }

        FillWho();
        who.SelectionChanged += (_, _) =>
        {
            if (who.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            var remote = id.StartsWith(RemoteChoice) ? settings.Remote.Pcs.FirstOrDefault(p => RemoteChoice + p.Id == id) : null;
            here.Visibility = prevBox.Visibility = remote != null ? Visibility.Collapsed : Visibility.Visible;
            there.Visibility = remote != null ? Visibility.Visible : Visibility.Collapsed;
            if (remote == null) thereSessions.Visibility = Visibility.Collapsed;
            // (the PC's own accounts, once it's listed them)
            remoteAccounts = remote != null ? new() : null;
            remoteAccountPick = null;
            FillAccounts();
            if (remote != null) { BuildThere(there, thereSessions, remote); return; }
            sendOverride = null;
            if (id == Info.Provider) return;
            Info.Provider = settings.LastProvider = id;
            Info.Model = "";
            var old = proc;
            proc = null;
            old?.Dispose();
            settings.Save();
            claudeOnly.Visibility = IsClaude ? Visibility.Visible : Visibility.Collapsed;
            FillModels();
            FillAccounts();
            ShowSaved();
            Renamed?.Invoke();
        };
        providers.Click += (_, _) =>
        {
            new ProvidersWindow(settings) { Owner = Window.GetWindow(this) }.ShowDialog();
            FillWho();
            FillModels();
        };
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
        folder.LostFocus += (_, _) => { if (folder.Text.Trim() != Info.Folder) Use(folder.Text.Trim()); };
        folder.KeyDown += (_, e) => { if (e.Key == Key.Enter) Use(folder.Text.Trim()); };
        browse.Click += (_, _) =>
        {
            var d = new OpenFolderDialog { Title = T("The folder it works in"), InitialDirectory = Directory.Exists(Info.Folder) ? Info.Folder : "" };
            if (d.ShowDialog(Window.GetWindow(this)) == true) { folder.Text = d.FolderName; Use(d.FolderName); }
        };
        Use(Info.Folder);
        FillAccounts();
    }

    /// <summary>
    /// A new tab's Claude Account: this PC's accounts with their usage (the session's chosen; Add an account… under them),
    /// or, with another PC picked under Host, that PC's, with their usage as it sent them (Claude Code there only; the
    /// one clicked is the one a new session there gets). Hidden for other AIs.
    /// </summary>
    private void FillAccounts()
    {
        startLocal.Visibility = remoteAccounts == null ? Visibility.Visible : Visibility.Collapsed;
        if (remoteAccounts != null)
        {
            if (!remoteAccounts.Any(a => a.Id == remoteAccountPick)) remoteAccountPick = remoteAccounts.FirstOrDefault()?.Id;
            startAccounts?.ShowRemote(remoteAccounts, remoteOwnAccount, remotePcName, remoteAccountsAsk);
            startAccounts?.Showing(remoteAccountPick);
            accountCol.Visibility = remoteClaude && remoteAccounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            startAccounts?.ShowRemote(null, null, null);
            startAccounts?.Showing(Account?.Id ?? settings.ActiveAccount);
            accountCol.Visibility = IsClaude && !IsRemote ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>One of this PC's accounts clicked in a new tab's list (or added with Add an account…): this session's.</summary>
    private void PickLocalAccount(string id)
    {
        if (id != Info.Account)
        {
            Info.Account = id;
            settings.Save();
            ownPlan = null;
            // (a Claude Code started for the tab already is on the other one)
            var old = proc;
            proc = null;
            old?.Dispose();
            ShowPlan();
        }
        FillAccounts();
    }

    /// <summary>
    /// Another PC (remote control), when it's picked under Host: connects, and lists its sessions (open one here) and
    /// what a new one there would be (which AI there, the folder there and its files to read first). A new one starts
    /// there with the first message.
    /// </summary>
    private void BuildThere(StackPanel there, StackPanel sessionsPanel, RemotePc r)
    {
        there.Children.Clear();
        sessionsPanel.Children.Clear();
        sessionsPanel.Visibility = Visibility.Collapsed;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
        var again = new Button { Content = T("Connect again"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var statusRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(again, Dock.Right);
        statusRow.Children.Add(again);
        statusRow.Children.Add(status);
        there.Children.Add(statusRow);
        var body = new StackPanel { Visibility = Visibility.Collapsed };
        there.Children.Add(body);

        sessionsPanel.Children.Add(Header(T("Remote Sessions")));
        var list = new StackPanel();
        sessionsPanel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 3 * SavedRowHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) });
        var none = Secondary(T("None open there."), new Thickness(0, 6, 0, 0));
        sessionsPanel.Children.Add(none);

        body.Children.Add(Header(T("Remote Session Details"), new Thickness(0, 8, 0, 4)));
        var whoThere = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(Labelled(T("Which AI there"), whoThere));
        var folderThere = new TextBox();
        var browseThere = new Button { Content = T("Browse…"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Pick the folder on that PC, or make a new one there") };
        var folderRow = new DockPanel();
        DockPanel.SetDock(browseThere, Dock.Right);
        folderRow.Children.Add(browseThere);
        folderRow.Children.Add(folderThere);
        body.Children.Add(Labelled(T("The folder there"), folderRow));
        var readHead = Secondary("", new Thickness(0, 8, 0, 4));
        var readThere = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var chosen = new List<string>();
        body.Children.Add(readHead);
        body.Children.Add(readThere);

        string pc = r.Label;
        void Say(string text, bool error)
        {
            status.Text = text;
            status.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
        }

        async Task FilesAsync()
        {
            chosen.Clear();
            readThere.Children.Clear();
            string path = folderThere.Text.Trim();
            try
            {
                var reply = await RemoteClient.AskAsync(r, new JsonObject { ["op"] = "files", ["folder"] = path });
                bool exists = reply["exists"]?.GetValue<bool>() == true;
                var md = (reply["md"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList() ?? new();
                readHead.Text = !exists ? F("That folder isn't on {0}.", pc) : md.Count == 0 ? T("No .md files in that folder to read first.") : "";
                readHead.SetResourceReference(TextBlock.ForegroundProperty, exists ? "TextSecondaryBrush" : "ErrorTextBrush");
                readHead.Visibility = readHead.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                // (its subfolders' READMEs, from a PC on 3.4.0 or later)
                var docs = (reply["docs"] as JsonArray)?.OfType<JsonObject>()
                    .Select(d => (Path: d["path"]?.GetValue<string>() ?? "", Name: d["name"]?.GetValue<string>() ?? ""))
                    .Where(d => IsDocPath(d.Path)).Select(d => (d.Path, d.Name.Length > 0 ? d.Name : d.Path))
                    .OrderBy(d => d.Item2, StringComparer.CurrentCultureIgnoreCase).ToList() ?? new();
                FillDocs(readThere, md, path, docs, chosen);
            }
            catch (Exception ex) { readHead.Text = ex.Message; }
        }
        whoThere.SelectionChanged += (_, _) =>
        {
            remoteClaude = whoThere.SelectedItem is ComboBoxItem { Tag: "" };
            if (remoteAccounts != null) FillAccounts();
        };
        folderThere.LostFocus += (_, _) => _ = FilesAsync();
        folderThere.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = FilesAsync(); };
        browseThere.Click += (_, _) =>
        {
            var w = new RemoteFolderWindow(r, folderThere.Text.Trim(), pc) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() != true || w.Folder is not { } picked) return;
            folderThere.Text = picked;
            _ = FilesAsync();
        };

        async Task ConnectAsync()
        {
            Say(F("Connecting to {0}…", r.Target), false);
            body.Visibility = sessionsPanel.Visibility = Visibility.Collapsed;
            sendOverride = (_, _) => Add(new NoteLine(F("Not connected to {0} yet.", pc), error: true));
            try
            {
                var reply = await RemoteClient.AskAsync(r, new JsonObject { ["op"] = "list" });
                pc = r.Name.Trim() is { Length: > 0 } named ? named : reply["pc"]?.GetValue<string>() ?? pc;
                Say(F("Connected to {0}. Open one of its sessions here, or start a new one there.", pc), false);
                list.Children.Clear();
                var sessions = (reply["sessions"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
                none.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var x in sessions)
                {
                    string Sx(string n) => x[n]?.GetValue<string>() ?? "";
                    var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 3), Height = SavedRowHeight - 3, ToolTip = T("Open it here: what it does shows on both PCs") };
                    var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                    Enum.TryParse<SessionState>(Sx("state"), out var st);
                    t.Inlines.Add(new System.Windows.Documents.Run("●  ") { Foreground = Palette.Brush(Palette.Of(st)) });
                    // (its Claude account, from a server since 3.2.1)
                    string who = Sx("accountName") is { Length: > 0 } account ? $"{Sx("who")} ({account})" : Sx("who");
                    t.Inlines.Add($"{Sx("name")}   ·   {who}   ·   {Sx("folder")}");
                    b.Content = t;
                    b.Click += (_, _) => Attach(x, pc);
                    list.Children.Add(b);
                }
                // its Claude accounts (since 3.2.1; with their usage since 3.15.2), its own sign-in picked first
                remoteAccounts = AccountsUsage.FromRemote(reply["accounts"] as JsonArray);
                remoteAccountPick = remoteOwnAccount = reply["account"]?.GetValue<string>();
                remotePcName = pc;
                remoteAccountsAsk = async () =>
                {
                    try
                    {
                        var again = await RemoteClient.AskAsync(r, new JsonObject { ["op"] = "accounts" });
                        if (remoteAccounts == null || remotePcName != pc) return;
                        remoteAccounts = AccountsUsage.FromRemote(again["accounts"] as JsonArray);
                        FillAccounts();
                    }
                    catch (Exception ex) { Logging.Log.Here.Info($"Remote control: {pc}'s accounts: {ex.Message}"); }
                };
                whoThere.Items.Clear();
                foreach (var p in (reply["providers"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    whoThere.Items.Add(new ComboBoxItem { Content = p["name"]?.GetValue<string>() ?? "?", Tag = p["id"]?.GetValue<string>() ?? "" });
                if (whoThere.Items.Count > 0) whoThere.SelectedIndex = 0;
                remoteClaude = whoThere.SelectedItem is ComboBoxItem { Tag: "" };
                FillAccounts();
                folderThere.Text = reply["folder"]?.GetValue<string>() ?? "";
                body.Visibility = sessionsPanel.Visibility = Visibility.Visible;
                sendOverride = (text, images) => _ = StartThereAsync(text, images);
                await FilesAsync();
            }
            catch (Exception ex) { Say(ex is RemoteException ? ex.Message : F("It couldn't connect: {0}", ex.Message), true); }
        }

        async Task StartThereAsync(string text, List<ImageData> images)
        {
            if (whoThere.SelectedItem is not ComboBoxItem { Tag: string provider } item) return;
            string folder = folderThere.Text.Trim();
            sendOverride = (_, _) => { };
            Say(F("Starting it on {0}…", pc), false);
            try
            {
                var ask = new JsonObject { ["op"] = "new", ["name"] = Info.Name, ["folder"] = folder, ["provider"] = provider, ["read"] = new JsonArray(chosen.Select(c => (JsonNode)c).ToArray()) };
                if (provider.Length == 0 && remoteAccountPick is { Length: > 0 } account) ask["account"] = account;
                var reply = await RemoteClient.AskAsync(r, ask);
                Info.Id = reply["id"]?.GetValue<string>() ?? Info.Id;
                Info.Folder = folder;
                Info.Provider = provider;
                Info.RemoteWho = provider.Length == 0 ? "Claude Code" : (item.Content as string ?? "?");
                BecomeRemote(pc, r.Target);
                input.Clear();
                Send(text, bubble: true, images);
            }
            catch (Exception ex)
            {
                Say(ex is RemoteException ? ex.Message : F("It couldn't start there: {0}", ex.Message), true);
                sendOverride = (t, i) => _ = StartThereAsync(t, i);
            }
        }

        void Attach(JsonObject x, string where)
        {
            string id = x["id"]?.GetValue<string>() ?? "";
            if (settings.Sessions.FirstOrDefault(o => o != Info && o.Remote == r.Target && o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } open)
            {
                Say(F("That session is open already, in the tab {0}.", open.Name), true);
                return;
            }
            Info.Id = id;
            Info.Name = x["name"]?.GetValue<string>() ?? Info.Name;
            Info.Folder = x["folder"]?.GetValue<string>() ?? "";
            Info.Provider = x["provider"]?.GetValue<string>() ?? "";
            Info.RemoteWho = x["who"]?.GetValue<string>();
            BecomeRemote(where, r.Target);
        }

        again.Click += (_, _) => _ = ConnectAsync();
        _ = ConnectAsync();
    }

    /// <summary>This tab becomes a session on another PC (remote control): it connects, and shows the session there.</summary>
    private void BecomeRemote(string pc, string target)
    {
        var old = proc;
        proc = null;
        old?.Dispose();
        remoteAccounts = null;
        Info.Account = null;
        Info.Remote = target;
        Info.RemotePc = pc;
        remotePlan = null;
        Info.Started = true;
        Info.Teleport = null;
        Info.ResumeAt = null;
        settings.Save();
        sendOverride = null;
        readFirst.Clear();
        startPanel.Visibility = Visibility.Collapsed;
        chat.Children.Clear();
        notesGroup = null;
        cards.Clear();
        ShowFolder();
        FillModels();
        ShowSkip();
        ShowPlan();
        Renamed?.Invoke();
        historyLoaded = historyReady = true;
        EnsureProcess();
    }

    private static TextBlock Header(string text, Thickness? margin = null)
    {
        var h = new TextBlock { Text = text };
        h.SetResourceReference(StyleProperty, "CardHeader");
        if (margin is { } m) h.Margin = m;
        return h;
    }

    private static TextBlock Secondary(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = margin };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
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
        notesGroup = null;
        cards.Clear();
        _ = LoadHistoryAsync();
        SetState(SessionState.Offline, "");
        return null;
    }

    /// <summary>Says why a closed tab couldn't be opened again, under Recently Closed (a new tab's start panel).</summary>
    private Action<string?>? sayClosed;

    /// <summary>A closed tab opened again in this one: here, or on the PC it runs on; the reason it can't, or null.</summary>
    private Task<string?> ReopenAsync(SessionInfo c) => c.Remote is { Length: > 0 } ? ReopenRemoteAsync(c) : Task.FromResult(Reopen(c));

    /// <summary>
    /// The tab closed last (Ctrl+Shift+T), opened again in this new tab, as Open in its Recently Closed row does; if it
    /// can't be, why shows there. False when there's no closed tab that isn't open.
    /// </summary>
    public bool ReopenLatest()
    {
        var c = settings.Closed.FirstOrDefault(x => !settings.Sessions.Any(o => o.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase)));
        if (c == null) return false;
        _ = Dispatcher.InvokeAsync(async () => sayClosed?.Invoke(await ReopenAsync(c)));
        return true;
    }

    /// <summary>A tab closed before, opened again in this one as it was (its name, AI, folder, model, permissions and
    /// totals); the reason it can't, or null.</summary>
    private string? Reopen(SessionInfo c)
    {
        bool claude = c.Provider.Length == 0;
        if (!claude && !settings.Providers.Any(p => p.Id == c.Provider))
            return F("{0} used an AI that isn't set up any more (Providers…).", c.Name);
        if (!Directory.Exists(c.Folder))
            return F("The folder it worked in, {0}, isn't on this PC any more.", c.Folder);
        if (claude ? ClaudeCli.TranscriptOf(c) == null : !ApiAgent.Exists(c.Id))
        {
            settings.ForgetClosed(c.Id);
            return F("{0} isn't on this PC any more.", c.Name);
        }
        if (settings.Sessions.FirstOrDefault(o => o != Info && o.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase)) is { } other)
            return F("That session is open already, in the tab {0}.", other.Name);
        Info.Provider = c.Provider;
        Info.Model = c.Model;
        Info.Mode = c.Mode;
        (Info.InputTokens, Info.OutputTokens, Info.CacheReadTokens, Info.CacheWriteTokens, Info.CostUsd, Info.Turns) =
            (c.InputTokens, c.OutputTokens, c.CacheReadTokens, c.CacheWriteTokens, c.CostUsd, c.Turns);
        if (Adopt(c.Id, null, c.Folder.Length > 0 ? c.Folder : null) is { } why) return why;
        Info.Name = c.Name;
        settings.ForgetClosed(c.Id);
        settings.Save();
        FillModels();
        mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == Info.Mode) ?? mode.SelectedItem;
        Renamed?.Invoke();
        return null;
    }

    /// <summary>A closed tab whose session runs on another PC: opened again here if it's still open there (it carried
    /// on there after the tab closed); the reason it can't, or null.</summary>
    private async Task<string?> ReopenRemoteAsync(SessionInfo c)
    {
        string pc = c.RemotePc ?? c.Remote ?? "?";
        if (settings.Remote.Find(c.Remote) is not { } conn)
            return F("{0} isn't in the list of other PCs any more (Settings → AI → Remote control).", pc);
        JsonObject reply;
        try { reply = await RemoteClient.AskAsync(conn, new JsonObject { ["op"] = "list" }); }
        catch (Exception ex) { return ex is RemoteException ? ex.Message : F("It couldn't connect to {0}: {1}", pc, ex.Message); }
        var there = (reply["sessions"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["id"]?.GetValue<string>(), c.Id, StringComparison.OrdinalIgnoreCase));
        if (there == null)
        {
            settings.ForgetClosed(c.Id);
            return F("{0} isn't open on {1} any more.", c.Name, pc);
        }
        if (settings.Sessions.FirstOrDefault(o => o != Info && o.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase)) is { } other)
            return F("That session is open already, in the tab {0}.", other.Name);
        Info.Id = c.Id;
        Info.Name = there["name"]?.GetValue<string>() is { Length: > 0 } n ? n : c.Name;
        Info.Folder = there["folder"]?.GetValue<string>() ?? c.Folder;
        Info.Provider = there["provider"]?.GetValue<string>() ?? c.Provider;
        Info.RemoteWho = there["who"]?.GetValue<string>() ?? c.RemoteWho;
        Info.Mode = c.Mode;
        (Info.InputTokens, Info.OutputTokens, Info.CacheReadTokens, Info.CacheWriteTokens, Info.CostUsd, Info.Turns) =
            (c.InputTokens, c.OutputTokens, c.CacheReadTokens, c.CacheWriteTokens, c.CostUsd, c.Turns);
        settings.ForgetClosed(c.Id);
        BecomeRemote(conn.Name.Trim() is { Length: > 0 } named ? named : reply["pc"]?.GetValue<string>() ?? pc, conn.Target);
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
        if (IsRemote) { Add(new NoteLine(F("This session runs on {0}: export it there.", Info.RemotePc ?? Info.Remote), error: true)); return; }
        if (!IsClaude) { Add(new NoteLine(F("Export is for Claude Code sessions. This one's conversation is kept in {0}.", ApiAgent.Folder), error: true)); return; }
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
        historyReady = false;
        try { await ReadHistoryAsync(); }
        finally { historyReady = true; }
        if (toReveal is { } hit)
        {
            toReveal = null;
            if (!await Dispatcher.InvokeAsync(() => Reveal(hit), DispatcherPriority.Background))
                Add(new NoteLine(T("The message you searched for is older than the conversation shown here."), icon: "🔍"));
        }
    }

    private async Task ReadHistoryAsync()
    {
        if (!Info.Started || IsRemote) return;
        List<JsonElement> lines;
        try { lines = await Task.Run(() => HistoryOf(Info)); }
        catch (Exception ex) { Add(new NoteLine(F("The conversation so far couldn't be read: {0}", ex.Message), error: true)); return; }
        RenderHistory(lines);
    }

    /// <summary>A session's conversation so far (none if it isn't there): Claude Code's transcript, or the agent's.</summary>
    private static List<JsonElement> HistoryOf(SessionInfo info)
    {
        if (!info.Started || info.Remote is { Length: > 0 }) return new();
        if (info.Provider.Length > 0) return ApiAgent.Transcript(info.Id);
        return ClaudeCli.TranscriptOf(info) is { } path ? ClaudeCli.ReadTranscript(path) : new();
    }

    private List<JsonElement> ReadHistory()
    {
        try { return HistoryOf(Info); }
        catch (Exception ex) { Add(new NoteLine(F("The conversation so far couldn't be read: {0}", ex.Message), error: true)); return new(); }
    }

    // the conversation read back: the last HistoryChunk messages at first; Show Earlier / Show All put older ones above
    private const int HistoryChunk = 300;
    private List<JsonElement> history = new();
    private int shownFrom;
    private FrameworkElement? earlierRow;
    /// <summary>While older messages are drawn: where they go (then put above the rest at once).</summary>
    private List<UIElement>? older;
    /// <summary>After a backfill the chat keeps everything (no dropping of the oldest at 600).</summary>
    private bool keepAll;

    /// <summary>The conversation so far drawn (the last 300 messages; older ones with Show Earlier).</summary>
    private void RenderHistory(List<JsonElement> lines)
    {
        history = lines;
        shownFrom = Math.Max(0, lines.Count - HistoryChunk);
        if (shownFrom > 0) Add(earlierRow = EarlierRow(shownFrom));
        DrawHistory(lines.Skip(shownFrom));
        foreach (var c in cards.Values.Where(c => !c.Finished)) c.SetResult("", false, timed: false);
        EndNotes();
        if (lines.Count > 0) Add(new NoteLine(T("The conversation so far. Write below to carry on."), icon: "↺"));
        ScrollToEnd(force: true);
    }

    /// <summary>"n earlier messages aren't shown." with Show Earlier (the next 300) and Show All.</summary>
    private FrameworkElement EarlierRow(int hidden)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var note = new NoteLine(F("{0} earlier messages aren't shown.", hidden)) { VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(note);
        var more = new Button { Content = T("Show Earlier"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        more.Click += (_, _) => Backfill(false);
        row.Children.Add(more);
        if (hidden > HistoryChunk)
        {
            var all = new Button { Content = T("Show All"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            all.Click += (_, _) => Backfill(true);
            row.Children.Add(all);
        }
        return row;
    }

    /// <summary>Draws the older messages above what's shown, keeping the place you're reading at.</summary>
    private void Backfill(bool all)
    {
        int to = shownFrom, from = all ? 0 : Math.Max(0, to - HistoryChunk);
        if (to <= 0) return;
        double offset = scroller.VerticalOffset, extent = scroller.ExtentHeight;
        if (earlierRow != null) chat.Children.Remove(earlierRow);
        earlierRow = null;
        var keepNotes = notesGroup;
        notesGroup = null;
        var known = cards.Keys.ToHashSet();
        older = new List<UIElement>();
        try
        {
            if (from > 0) older.Add(earlierRow = EarlierRow(from));
            DrawHistory(history.Skip(from).Take(to - from));
            foreach (var c in cards.Where(c => !known.Contains(c.Key) && !c.Value.Finished)) c.Value.SetResult("", false, timed: false);
            EndNotes();
        }
        finally
        {
            var items = older;
            older = null;
            notesGroup = keepNotes;
            for (int i = 0; i < items.Count; i++) chat.Children.Insert(i, items[i]);
        }
        shownFrom = from;
        keepAll = true;
        // (the scroller's own layout, so its extent counts what was added)
        scroller.UpdateLayout();
        scroller.ScrollToVerticalOffset(offset + scroller.ExtentHeight - extent);
    }

    /// <summary>Draws transcript lines (each with Add, so into <see cref="older"/> while backfilling).</summary>
    private void DrawHistory(IEnumerable<JsonElement> lines)
    {
        foreach (var l in lines)
        {
            var m = Obj(l, "message");
            var content = Obj(m, "content");
            if (Str(l, "type") == "user")
            {
                if (content.ValueKind == JsonValueKind.String) { string t = content.GetString() ?? ""; if (Visible(t)) AddUser(t, null); }
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    // (its words and its images, as one bubble)
                    var words = new List<string>();
                    var images = new List<ImageData>();
                    foreach (var b in content.EnumerateArray())
                    {
                        if (Str(b, "type") == "text" && Str(b, "text") is { } t && Visible(t)) words.Add(t);
                        else if (Str(b, "type") == "image" && Str(Obj(b, "source"), "data") is { Length: > 0 } data) images.Add(new(Str(Obj(b, "source"), "media_type") ?? "image/png", data));
                        else if (Str(b, "type") == "image_url" && ImageData.FromDataUrl(Str(Obj(b, "image_url"), "url")) is { } img) images.Add(img);
                        else if (Str(b, "type") == "tool_result" && Str(b, "tool_use_id") is { } id && cards.TryGetValue(id, out var card))
                            card.SetResult(ResultText(Obj(b, "content")), Obj(b, "is_error").ValueKind == JsonValueKind.True, timed: false);
                    }
                    if (words.Count > 0 || images.Count > 0) AddUser(string.Join("\n\n", words), images);
                }
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in content.EnumerateArray())
                {
                    if (Str(b, "type") == "text" && Str(b, "text") is { Length: > 0 } t) { var at = new AssistantText { When = AssistantText.TimeOf(Str(l, "timestamp")) }; at.Finish(t); Add(at); }
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
    }

    private ChatSearch.Hit? toReveal;
    private bool historyReady;

    /// <summary>
    /// Shows a message the search found: scrolled to and lit up for a moment. False if it isn't in the conversation
    /// showing (older than what's shown). A tab not shown yet does it once its conversation is read.
    /// </summary>
    public bool Reveal(ChatSearch.Hit hit)
    {
        if (!historyLoaded || !historyReady) { toReveal = hit; return true; }
        string want = hit.Text.Trim();
        var same = chat.Children.OfType<FrameworkElement>()
            .Where(e => (hit.Mine ? (e as UserBubble)?.Text : (e as AssistantText)?.Text)?.Trim() == want).ToList();
        if (same.Count == 0) return false;
        // (the same words more than once: counted from the end, as the conversation shows the last messages)
        var target = same[Math.Max(0, same.Count - 1 - hit.SameAfter)];
        target.BringIntoView();
        var glow = new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(450)) { AutoReverse = false, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3) };
        target.BeginAnimation(OpacityProperty, glow);
        return true;
    }

    /// <summary>A message of yours read back: a bubble (after a line for the files it had read first), or a line for
    /// the session notes' request.</summary>
    private void AddUser(string text, List<ImageData>? images)
    {
        if (IsNotesMessage(text)) { StartNotes(); return; }
        EndNotes();
        if (ReadFirstNames(text) is { Count: > 0 } read) Add(new NoteLine(F("Read first: {0}", string.Join(", ", read)), icon: "\U0001F4C4"));
        Add(new UserBubble(WithoutReadFirst(text), images: images));
    }

    // what Claude Code puts in a user turn that isn't the user's (command output, reminders)

    internal static bool Visible(string text) => text.Length > 0 && !text.StartsWith("<") && !text.StartsWith("Caveat:");

    // ---------------------------------------------------------------- helpers

    private void Add(UIElement e)
    {
        if (notesGroup != null) notesGroup.Items.Add(e);
        else if (older != null)
        {
            older.Add(e);
            return;
        }
        else chat.Children.Add(e);
        if (chat.Children.Count > 600 && !keepAll)
        {
            chat.Children.RemoveAt(0);
            // (what's dropped can't be shown again from here)
            if (earlierRow != null && !chat.Children.Contains(earlierRow)) earlierRow = null;
        }
        // (a note said here goes to the PCs that have the session open; what the session did, they draw themselves)
        if (e is NoteLine n && !inEvent && Shared != null)
            Share(new JsonObject { ["type"] = "daisy_note", ["text"] = n.Note, ["error"] = n.Error, ["icon"] = n.Icon });
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

    /// <summary>The sound for a question (Windows' messaging notification, or its asterisk if that's missing).</summary>
    /// <summary>An icon before a button's words.</summary>
    private static StackPanel IconText(string glyph, string text)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(icon);
        p.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    private static void PlayAsk()
    {
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Messaging.wav");
            if (File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
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
        Closing?.Invoke();
        usage.Changed -= ShowPlan;
        Look.Changed -= ApplyLook;
        settings.Changed -= OptionsChanged;
        tick.Stop();
        proc?.Dispose();
        proc = null;
    }
}
