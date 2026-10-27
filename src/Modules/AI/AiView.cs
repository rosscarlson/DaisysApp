using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The Claude Code tab: at the top who's signed in to Claude Code (sign in / out, or how to install it), then a tab per
/// session, each with a dot showing what it's doing (so one working in the background can be seen), its name (click
/// the name of the tab showing to rename it: the Claude Code session is renamed too) and a close button; + opens a
/// new one. Sessions keep running when another tab is showing.
/// </summary>
internal sealed class AiView : UserControl, IRemoteHost
{
    private readonly AiSettings settings;
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    private readonly Grid pages = new();
    private readonly TextBlock account = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
    private readonly WrapPanel accountButtons = new() { VerticalAlignment = VerticalAlignment.Center };
    // its buttons (Usage), at the right under the list of accounts (AccountsUsage: clicking one switches to it)
    private readonly DockPanel accountRow = new() { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
    private bool listing;
    // after Sign in: watching for the sign-in, to check again by itself
    private readonly DispatcherTimer signInWatch = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTime signInStarted;
    // a problem with Claude Code (not installed, not signed in): a line at the top until it's cleared
    private readonly TextBlock warning = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly DockPanel warningRow = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock planProblem = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
    // the Claude Account box: at the bottom of the dashboard of the tab showing
    private readonly StackPanel tools = new();
    private readonly UsagePanel plan;
    // each account's usage, a line each, above the list
    private readonly AccountsUsage accountsUsage;
    private readonly List<Tab> tabs = new();
    private readonly DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool blinkOn, checkedAccount, warningProblem;

    private sealed class Tab
    {
        public required SessionView View;
        public required RadioButton Button;
        public required Ellipse Dot;
        public required TextBlock Name;
        public required TextBox Edit;
    }

    private readonly PlanUsage usage;
    private readonly ChatSearch search;

    public AiView(AiSettings settings, PlanUsage usage)
    {
        this.settings = settings;
        this.usage = usage;

        // a problem with Claude Code: a line at the top, ✕ clears it (until it's a different one)
        var logo = new TextBlock { Text = "✳", FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Brush(Color.FromRgb(0xD9, 0x77, 0x57)) };
        var clear = new Button { Content = "✕", FontSize = 10, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = T("Clear this (it's in the Claude Account box, at the bottom right)") };
        clear.Click += (_, _) =>
        {
            settings.ClearedWarning = warning.Text;
            settings.Save();
            warningRow.Visibility = Visibility.Collapsed;
        };
        DockPanel.SetDock(logo, Dock.Left);
        DockPanel.SetDock(clear, Dock.Right);
        warningRow.Children.Add(logo);
        warningRow.Children.Add(clear);
        warning.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        warningRow.Children.Add(warning);

        var add = new Button { Content = new TextBlock { Text = "+", FontSize = 16, Margin = new Thickness(0, -2, 0, 0) }, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("A new session (Ctrl+T)") };
        add.Click += (_, _) => NewSession();
        var stripRow = new StackPanel { Orientation = Orientation.Horizontal };
        stripRow.Children.Add(strip);
        stripRow.Children.Add(add);
        var stripScroll = new ScrollViewer { Content = stripRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // searching the conversations (Ctrl+F)
        search = new ChatSearch(settings, () => tabs.FirstOrDefault(t => t.View.Info.Id == settings.Selected)?.View.Info);
        search.Go += Reveal;
        var find = new Button { Content = new TextBlock { Text = "🔍  " + T("Search"), FontSize = 13 }, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Search the conversations: what you said, the replies, or both (Ctrl+F)") };
        find.Click += (_, _) => search.Toggle();
        var stripLine = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(find, Dock.Right);
        stripLine.Children.Add(find);
        stripLine.Children.Add(stripScroll);
        // (on the window, while this tab shows: they work wherever the focus is, as a browser's do)
        Window? keysOn = null;
        Loaded += (_, _) => { keysOn = Window.GetWindow(this); if (keysOn != null) keysOn.PreviewKeyDown += Keys; };
        Unloaded += (_, _) => { if (keysOn != null) keysOn.PreviewKeyDown -= Keys; keysOn = null; };

        var dock = new DockPanel();
        DockPanel.SetDock(warningRow, Dock.Top);
        plan = new UsagePanel(usage, settings);
        plan.CodingBadgeChanged += () => ShowCoding();
        DockPanel.SetDock(plan, Dock.Top);

        // the Claude Account box: who's signed in, the accounts, its buttons, Usage
        account.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        planProblem.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        accountsUsage = new AccountsUsage(settings, usage, () => Exe);
        tools.Children.Add(accountsUsage);
        tools.Children.Add(account);
        tools.Children.Add(planProblem);
        accountRow.Children.Add(accountButtons);
        tools.Children.Add(accountRow);
        accountsUsage.Picked += SwitchAccount;
        signInWatch.Tick += (_, _) =>
        {
            if (ClaudeAccounts.Current() != null) { signInWatch.Stop(); _ = CheckAccountAsync(); }
            else if (DateTime.Now - signInStarted > TimeSpan.FromMinutes(15)) signInWatch.Stop();
        };
        settings.AccountsChanged += ShowAccounts;
        settings.AccountSwitched += () =>
        {
            foreach (var t in tabs) t.View.AccountSwitched();
            _ = CheckAccountAsync();
        };
        usage.Changed += ShowPlanProblem;
        // the refresh icon on the Usage button: the showing session's account's usage
        plan.Refresh += () => { if (Showing is { } view) _ = view.RefreshPlanAsync(); else _ = usage.CheckAsync(); _ = accountsUsage.ReadAsync(); };
        ShowPlanProblem();
        DockPanel.SetDock(stripLine, Dock.Top);
        DockPanel.SetDock(search, Dock.Top);
        dock.Children.Add(warningRow);
        dock.Children.Add(plan);
        dock.Children.Add(stripLine);
        dock.Children.Add(search);
        dock.Children.Add(pages);
        Content = dock;

        foreach (var s in settings.Sessions.ToList()) AddTab(s);
        if (tabs.Count == 0) NewSession();
        else Select(tabs.FirstOrDefault(t => t.View.Info.Id == settings.Selected) ?? tabs[0]);

        blink.Tick += (_, _) => { blinkOn = !blinkOn; foreach (var t in tabs) ShowDot(t); };
        // checked when the tab first shows, and again each time it shows while there's a problem (installed or signed in since)
        IsVisibleChanged += (_, _) => { if (IsVisible && (!checkedAccount || warningProblem)) { checkedAccount = true; _ = CheckAccountAsync(); } };
    }

    /// <summary>claude.exe, if there is one.</summary>
    public string? Exe => ClaudeCli.Find(settings.ClaudePath);

    // ---------------------------------------------------------------- the account

    private async Task CheckAccountAsync()
    {
        accountButtons.Children.Clear();
        string? exe = Exe;
        if (exe == null)
        {
            Account(T("Claude Code isn't installed on this PC: install it, sign in, and come back (or point to claude.exe)."), problem: true);
            accountButtons.Children.Add(Small(T("How to install"), () => Open(ClaudeCli.InstallUrl)));
            accountButtons.Children.Add(Small(T("Find claude.exe…"), PickExe));
            accountButtons.Children.Add(plan.Toggle);
            ShowAccounts();
            return;
        }
        Account(T("Checking your Claude Code sign-in…"), problem: false);
        var a = await ClaudeCli.StatusAsync(exe);
        accountButtons.Children.Clear();
        if (a is { LoggedIn: true })
        {
            Account(F("Signed in as {0}{1}", a.Email ?? T("you"), a.Method is { } m ? $" ({m})" : ""), problem: false);
            account.ToolTip = $"{exe}{(a.Org != null ? "\n" + a.Org : "")}";
            // the account signed in is kept (its sign-in renewed), and added to the list if it's new
            var (_, added, switched) = ClaudeAccounts.Keep(settings);
            if (added) settings.NotifyAccountsChanged();
            if (switched) { settings.NotifyAccountSwitched(); return; }
        }
        else
        {
            Account(a == null ? T("Couldn't ask Claude Code who's signed in.") : T("Not signed in to Claude Code: sign in with your Claude account (a browser opens)."), problem: true);
            accountButtons.Children.Add(Small(T("Sign in"), () =>
            {
                ClaudeCli.Auth(exe, true);
                signInStarted = DateTime.Now;
                signInWatch.Start();
            }));
        }
        accountButtons.Children.Add(plan.Toggle);
        ShowAccounts();
    }

    /// <summary>The list of Claude accounts, the showing session's chosen (shown once Claude Code is there and one is kept).</summary>
    private void ShowAccounts()
    {
        listing = Exe != null && settings.Accounts.Any(a => a.Kind == AccountKinds.Claude);
        accountsUsage.Showing(Showing?.Info.Account);
        ArrangeAccount();
    }

    /// <summary>With the list of accounts showing (and all well), it's all the box says: the "Signed in as" line goes
    /// (its email is in the list's tooltips) and Usage is at the right under the list; otherwise the line, then the buttons.</summary>
    private void ArrangeAccount()
    {
        bool list = listing;
        account.Visibility = list && !warningProblem ? Visibility.Collapsed : Visibility.Visible;
        DockPanel.SetDock(accountButtons, list ? Dock.Right : Dock.Left);
        // (beside the list, its buttons' own margins (0, 6, 6, 0) cancelled)
        accountButtons.Margin = list ? new Thickness(6, -6, -6, 0) : new Thickness(0, -8, 0, 0);
        accountRow.Margin = new Thickness(0, account.Visibility == Visibility.Visible ? 8 : 2, 0, 0);
    }

    /// <summary>Another account clicked in the list: the showing session switches to it (SessionView.UseAccount); the
    /// others, and Claude Code's own sign-in, stay as they are.</summary>
    private void SwitchAccount(string picked) => Showing?.UseAccount(picked);

    /// <summary>The sign-in, in the Claude Account box; a problem also on the line at the top, unless it was cleared.</summary>
    private void Account(string text, bool problem)
    {
        account.Text = text;
        warning.Text = text;
        warning.ToolTip = text;
        warningRow.Visibility = problem && settings.ClearedWarning != text ? Visibility.Visible : Visibility.Collapsed;
        warningProblem = problem;
        ArrangeAccount();
        // all well: the next problem shows again
        if (!problem && text != T("Checking your Claude Code sign-in…") && settings.ClearedWarning != null) { settings.ClearedWarning = null; settings.Save(); }
    }

    private void PickExe()
    {
        var d = new OpenFileDialog { Title = T("Claude Code's program"), Filter = "claude.exe|claude.exe;claude.cmd|*.exe|*.exe" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        settings.ClaudePath = d.FileName;
        settings.Save();
        _ = CheckAccountAsync();
    }

    // ---------------------------------------------------------------- the tabs

    private void NewSession()
    {
        string folder = settings.LastFolder is { Length: > 0 } f && System.IO.Directory.Exists(f) ? f : AiSettings.DefaultFolder();
        bool claude = !settings.Providers.Any(p => p.Id == settings.LastProvider);
        var info = new SessionInfo { Name = NextName(), Folder = folder, Provider = claude ? "" : settings.LastProvider, Model = claude ? settings.Model : "", Mode = settings.Mode, Account = settings.ActiveAccount };
        settings.Sessions.Add(info);
        settings.Save();
        Select(AddTab(info));
    }

    /// <summary>"Session 1", "Session 2"…: the first not in use.</summary>
    private string NextName()
    {
        int n = 1;
        while (settings.Sessions.Any(s => s.Name == F("Session {0}", n))) n++;
        return F("Session {0}", n);
    }

    // ---------------------------------------------------------------- remote control (this PC the server)

    public JsonObject Describe()
    {
        var claude = settings.Accounts.Where(a => a.Kind == AccountKinds.Claude).ToList();
        var sessions = new JsonArray();
        foreach (var t in tabs.Where(t => t.View.Info.Remote is not { Length: > 0 }))
        {
            var i = t.View.Info;
            var account = i.Provider.Length > 0 ? null : claude.FirstOrDefault(a => a.Id == (i.Account ?? settings.ActiveAccount));
            sessions.Add(new JsonObject
            {
                ["id"] = i.Id, ["name"] = i.Name, ["folder"] = i.Folder, ["provider"] = i.Provider, ["started"] = i.Started, ["state"] = t.View.State.ToString(),
                ["who"] = i.Provider.Length == 0 ? "Claude Code" : settings.Providers.FirstOrDefault(p => p.Id == i.Provider)?.Name ?? "?",
                ["account"] = account?.Id, ["accountName"] = account?.Label,
            });
        }
        var providers = new JsonArray { new JsonObject { ["id"] = "", ["name"] = T("Claude Code") } };
        foreach (var p in settings.Providers) providers.Add(new JsonObject { ["id"] = p.Id, ["name"] = $"{p.Name} ({p.Model})" });
        // its Claude accounts, for a new Claude Code session there to pick (since 3.2.1); "account": Claude Code's own sign-in
        var accounts = ClaudeAccounts.ToRemote(settings);
        string folder = settings.LastFolder is { Length: > 0 } f && System.IO.Directory.Exists(f) ? f : AiSettings.DefaultFolder();
        return new JsonObject
        {
            ["op"] = "sessions", ["pc"] = Environment.MachineName, ["folder"] = folder, ["providers"] = providers, ["sessions"] = sessions,
            ["accounts"] = accounts, ["account"] = claude.Any(a => a.Id == settings.ActiveAccount) ? settings.ActiveAccount : null,
        };
    }

    public string Create(string name, string folder, string provider, List<string> readFirst, string? account)
    {
        if (!System.IO.Directory.Exists(folder)) throw new RemoteException(F("The folder {0} isn't on {1}.", folder, Environment.MachineName));
        if (provider.Length > 0 && !settings.Providers.Any(p => p.Id == provider)) throw new RemoteException(F("That AI isn't set up on {0} any more.", Environment.MachineName));
        if (account is { Length: > 0 } && ClaudeAccounts.Of(settings, account) == null) throw new RemoteException(F("That Claude account isn't on {0} any more.", Environment.MachineName));
        var info = new SessionInfo
        {
            Name = name.Trim().Length > 0 ? name.Trim() : NextName(), Folder = folder, Provider = provider,
            Model = provider.Length == 0 ? settings.Model : "", Mode = settings.Mode,
            Account = provider.Length > 0 ? null : account is { Length: > 0 } ? account : settings.ActiveAccount,
        };
        settings.Sessions.Add(info);
        settings.Save();
        var tab = AddTab(info);
        tab.View.SetReadFirst(readFirst);
        Logging.Log.Here.Info($"Remote control: session {info.Id} ({info.Name}) started from another PC in {folder}");
        return info.Id;
    }

    public PlanUsage Usage => usage;

    public SessionView? Find(string id) =>
        tabs.FirstOrDefault(t => t.View.Info.Remote is not { Length: > 0 } && t.View.Info.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.View;

    private Tab AddTab(SessionInfo info)
    {
        var view = new SessionView(info, settings, () => Exe, usage) { Visibility = Visibility.Collapsed };
        pages.Children.Add(view);
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = info.Name, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        var edit = new TextBox { Text = info.Name, MinWidth = 120, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 0, 2, 0) };
        var close = new Button { Content = "✕", FontSize = 9, Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Close this session (Ctrl+W)"), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(name);
        // which AI, when it isn't Claude Code
        var tag = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) };
        tag.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        void ShowTag() => tag.Text = info.Remote is { Length: > 0 } ? "\u21C4 " + (info.RemotePc ?? info.Remote)
            : info.Provider.Length == 0 ? "" : settings.Providers.FirstOrDefault(p => p.Id == info.Provider)?.Name ?? "?";
        ShowTag();
        header.Children.Add(tag);
        header.Children.Add(edit);
        header.Children.Add(close);
        var button = new RadioButton { Content = header, GroupName = "AiSessions" };
        button.SetResourceReference(StyleProperty, "TabButton");
        AutomationProperties.SetName(button, info.Name);
        var tab = new Tab { View = view, Button = button, Dot = dot, Name = name, Edit = edit };
        tabs.Add(tab);
        strip.Children.Add(button);

        // clicking the name of the tab showing renames it
        // (the tab button takes the mouse while it's pressed, so it's watched there)
        bool wasChecked = false, onName = false;
        button.PreviewMouseLeftButtonDown += (_, _) => { wasChecked = button.IsChecked == true; onName = name.IsMouseOver; };
        button.Click += (_, _) =>
        {
            // (once the click is over: the button would take the focus back from the box)
            if (wasChecked && onName && edit.Visibility != Visibility.Visible)
                Dispatcher.BeginInvoke(() => BeginRename(tab), DispatcherPriority.Input);
        };
        name.ToolTip = T("Click the name of the tab showing to rename it");
        edit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { EndRename(tab, keep: true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndRename(tab, keep: false); e.Handled = true; }
        };
        edit.LostKeyboardFocus += (_, _) => EndRename(tab, keep: true);
        button.Checked += (_, _) => Select(tab);
        close.Click += (_, _) => CloseTab(tab);
        view.StateChanged += () => { ShowDot(tab); UpdateBlink(); ShowCoding(view); };
        // (switched to another account: the box follows it)
        view.PlanChanged += () =>
        {
            if (view.OtherAccountPlan is { } other) accountsUsage.Report(other.Id, other.Plan);
            if (settings.Selected == info.Id) { ShowContext(view); ShowAccounts(); }
        };
        view.Renamed += () =>
        {
            name.Text = info.Name; ShowTag(); AutomationProperties.SetName(button, info.Name);
            if (settings.Selected == info.Id) ShowContext(view);
        };
        ShowDot(tab);
        return tab;
    }

    private void Select(Tab tab)
    {
        foreach (var t in tabs) t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed;
        if (tab.Button.IsChecked != true) tab.Button.IsChecked = true;
        settings.Selected = tab.View.Info.Id;
        settings.Save();
        search.SessionChanged();
        // the Claude Account box moves to the dashboard showing
        foreach (var t in tabs) if (t != tab && t.View.Tools.Content == tools) t.View.Tools.Content = null;
        tab.View.Tools.Content = tools;
        ShowContext(tab.View);
        ShowAccounts();
    }

    private SessionView? Showing => tabs.FirstOrDefault(t => t.View.Info.Id == settings.Selected)?.View;

    /// <summary>
    /// The Claude Account box follows the session showing: on another PC, or on another account than Claude Code's own,
    /// this PC's usage details and its plan's problem line aren't shown (its own plan shows in its dashboard).
    /// </summary>
    private void ShowContext(SessionView view)
    {
        plan.Hidden = view.OnOtherPc || view.OnOtherAccount;
        accountsUsage.Showing(view.Info.Account);
        ShowPlanProblem();
    }

    private void ShowPlanProblem()
    {
        planProblem.Text = plan.Problem ?? "";
        bool others = Showing is { OnOtherAccount: true } or { OnOtherPc: true };
        planProblem.Visibility = !others && planProblem.Text.Length > 0 && planProblem.Text != account.Text ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A message the search found: its tab shown (a new one, if it isn't open) and the message in it. False if
    /// the tab doesn't show it (it's older than what's shown).</summary>
    private bool Reveal(ChatSearch.Hit hit)
    {
        var tab = tabs.FirstOrDefault(t => t.View.Info.Id.Equals(hit.SessionId, StringComparison.OrdinalIgnoreCase));
        if (tab == null)
        {
            string folder = System.IO.Directory.Exists(hit.Folder) ? hit.Folder : settings.LastFolder is { Length: > 0 } last && System.IO.Directory.Exists(last) ? last : AiSettings.DefaultFolder();
            // (a Claude Code session whose folder isn't on this PC carries on in this one, where Claude Code then finds it)
            if (hit.Provider == null && folder != hit.Folder && ClaudeCli.FindTranscript(hit.SessionId) is { } transcript)
                try { System.IO.File.Copy(transcript, System.IO.Path.Combine(System.IO.Directory.CreateDirectory(ClaudeCli.ProjectFolderFor(folder)).FullName, hit.SessionId + ".jsonl"), true); } catch { }
            var info = new SessionInfo
            {
                Id = hit.SessionId,
                Name = hit.Title.Length > 40 ? hit.Title[..40] : hit.Title,
                Folder = folder,
                Provider = hit.Provider ?? "",
                Started = true,
                Model = hit.Provider == null ? settings.Model : "",
                Mode = settings.Mode,
            };
            settings.Sessions.Add(info);
            settings.Save();
            tab = AddTab(info);
        }
        Select(tab);
        return tab.View.Reveal(hit);
    }

    private void BeginRename(Tab tab)
    {
        tab.Edit.Text = tab.View.Info.Name;
        tab.Name.Visibility = Visibility.Collapsed;
        tab.Edit.Visibility = Visibility.Visible;
        tab.Edit.Focus();
        tab.Edit.SelectAll();
    }

    private void EndRename(Tab tab, bool keep)
    {
        if (tab.Edit.Visibility != Visibility.Visible) return;
        tab.Edit.Visibility = Visibility.Collapsed;
        tab.Name.Visibility = Visibility.Visible;
        string name = tab.Edit.Text.Trim();
        if (!keep || name.Length == 0 || name == tab.View.Info.Name) return;
        tab.View.Rename(name);
        tab.Name.Text = name;
        AutomationProperties.SetName(tab.Button, name);
    }

    private void CloseTab(Tab tab)
    {
        // a session on another PC carries on there: closing its tab here only disconnects
        bool remote = tab.View.Info.Remote is { Length: > 0 };
        if (!remote && tab.View.Busy && MessageBox.Show(Window.GetWindow(this), F("{0} is still working. Stop it and close the tab? (The session is kept in Claude Code: claude --resume picks it up.)", tab.View.Info.Name),
                T("Claude Code"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        tab.View.Close();
        tab.View.Tools.Content = null;
        if (remote) Logging.Log.Here.Info($"Remote control: tab {tab.View.Info.Name} closed here; session {tab.View.Info.Id} carries on on {tab.View.Info.RemotePc ?? tab.View.Info.Remote}");
        int index = tabs.IndexOf(tab);
        tabs.Remove(tab);
        ShowCoding();
        strip.Children.Remove(tab.Button);
        pages.Children.Remove(tab.View);
        settings.Sessions.Remove(tab.View.Info);
        settings.RememberClosed(tab.View.Info);
        settings.Save();
        if (tabs.Count == 0) NewSession();
        else Select(tabs[Math.Min(index, tabs.Count - 1)]);
    }

    /// <summary>Ctrl+F searches; tabs as in a browser: Ctrl+T a new one, Ctrl+W closes this one, Ctrl+Shift+T opens the
    /// one closed last again.</summary>
    private void Keys(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.F && (mods & ModifierKeys.Control) != 0) { e.Handled = true; search.Toggle(); }
        else if (e.Key == Key.T && mods == ModifierKeys.Control) { e.Handled = true; NewSession(); }
        else if (e.Key == Key.T && mods == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; ReopenClosed(); }
        else if (e.Key == Key.W && mods == ModifierKeys.Control && tabs.FirstOrDefault(t => t.View.Info.Id == settings.Selected) is { } shown) { e.Handled = true; CloseTab(shown); }
    }

    /// <summary>
    /// Ctrl+Shift+T: the tab closed last, opened again in a new tab (nothing when there's none), or in the tab showing
    /// when that's a new one not started yet (so trying again doesn't pile up empty tabs).
    /// </summary>
    private void ReopenClosed()
    {
        if (!settings.Closed.Any(c => !settings.Sessions.Any(o => o.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase)))) return;
        if (Showing is not { Info.Started: false, OnOtherPc: false }) NewSession();
        Showing?.ReopenLatest();
    }

    private void ShowDot(Tab t)
    {
        var s = t.View.State;
        t.Dot.Fill = Palette.Brush(Palette.Of(s));
        t.Dot.Opacity = s == SessionState.Waiting && !blinkOn ? 0.25 : s == SessionState.Offline ? 0.5 : 1;
        t.Dot.ToolTip = s.ToString();
    }

    private void UpdateBlink()
    {
        if (tabs.Any(t => t.View.State == SessionState.Waiting)) blink.Start(); else { blink.Stop(); blinkOn = true; foreach (var t in tabs) ShowDot(t); }
    }

    /// <summary>
    /// The title bar's "Coding" bubble (Core's <see cref="TitleBadges"/>), while a session here (or one of another PC's
    /// shown here) works, in its status orb's colour: of the session that changed last.
    /// </summary>
    private void ShowCoding(SessionView? changed = null)
    {
        static bool Coding(SessionView v) => v.Busy && v.State != SessionState.Waiting;
        var working = tabs.Where(t => Coding(t.View)).Select(t => t.View).ToList();
        if (!settings.Usage.CodingBadge || working.Count == 0) { DaisysApp.Shell.TitleBadges.Set("AiCoding", null); return; }
        if (changed != null && working.Contains(changed)) codingLead = changed;
        var lead = codingLead != null && working.Contains(codingLead) ? codingLead : working[^1];
        DaisysApp.Shell.TitleBadges.Set("AiCoding", T("Coding"), working.Count == 1 ? lead.Info.Name : string.Join(", ", working.Select(v => v.Info.Name)), Palette.Hex(lead.State), "AI");
    }

    private SessionView? codingLead;

    /// <summary>Whether any session is working (for the tray, and asking before closing).</summary>
    public int Working => tabs.Count(t => t.View.Busy);

    public void CloseAll()
    {
        foreach (var t in tabs) t.View.Close();
    }

    // ---------------------------------------------------------------- helpers

    private static Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
