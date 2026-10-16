using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>
/// The Claude Code tab: at the top who's signed in to Claude Code (sign in / out, or how to install it), then a tab per
/// session, each with a dot showing what it's doing (so one working in the background can be seen), its name (click
/// the name of the tab showing to rename it: the Claude Code session is renamed too) and a close button; + opens a
/// new one. Sessions keep running when another tab is showing.
/// </summary>
internal sealed class ClaudeCodeView : UserControl
{
    private readonly ClaudeCodeSettings settings;
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    private readonly Grid pages = new();
    private readonly TextBlock account = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel accountButtons = new() { Orientation = Orientation.Horizontal };
    private readonly List<Tab> tabs = new();
    private readonly DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool blinkOn, checkedAccount;

    private sealed class Tab
    {
        public required SessionView View;
        public required RadioButton Button;
        public required Ellipse Dot;
        public required TextBlock Name;
        public required TextBox Edit;
    }

    public ClaudeCodeView(ClaudeCodeSettings settings)
    {
        this.settings = settings;

        // who's signed in
        var accountRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(accountButtons, Dock.Right);
        accountRow.Children.Add(accountButtons);
        var logo = new TextBlock { Text = "✳", FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Brush(Color.FromRgb(0xD9, 0x77, 0x57)) };
        DockPanel.SetDock(logo, Dock.Left);
        accountRow.Children.Add(logo);
        account.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        accountRow.Children.Add(account);

        var add = new Button { Content = new TextBlock { Text = "+", FontSize = 16, Margin = new Thickness(0, -2, 0, 0) }, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("A new session") };
        add.Click += (_, _) => NewSession();
        var stripRow = new StackPanel { Orientation = Orientation.Horizontal };
        stripRow.Children.Add(strip);
        stripRow.Children.Add(add);
        var stripScroll = new ScrollViewer { Content = stripRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 10) };

        var dock = new DockPanel();
        DockPanel.SetDock(accountRow, Dock.Top);
        DockPanel.SetDock(stripScroll, Dock.Top);
        dock.Children.Add(accountRow);
        dock.Children.Add(stripScroll);
        dock.Children.Add(pages);
        Content = dock;

        foreach (var s in settings.Sessions.ToList()) AddTab(s);
        if (tabs.Count == 0) NewSession();
        else Select(tabs.FirstOrDefault(t => t.View.Info.Id == settings.Selected) ?? tabs[0]);

        blink.Tick += (_, _) => { blinkOn = !blinkOn; foreach (var t in tabs) ShowDot(t); };
        IsVisibleChanged += (_, _) => { if (IsVisible && !checkedAccount) { checkedAccount = true; _ = CheckAccountAsync(); } };
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
            account.Text = T("Claude Code isn't installed on this PC: install it, sign in, and come back (or point to claude.exe).");
            accountButtons.Children.Add(Small(T("How to install"), () => Open(ClaudeCli.InstallUrl)));
            accountButtons.Children.Add(Small(T("Find claude.exe…"), PickExe));
            accountButtons.Children.Add(Small(T("Check again"), () => _ = CheckAccountAsync()));
            return;
        }
        account.Text = T("Checking your Claude Code sign-in…");
        var a = await ClaudeCli.StatusAsync(exe);
        if (a is { LoggedIn: true })
        {
            account.Text = F("Claude Code, signed in as {0}{1}", a.Email ?? T("you"), a.Method is { } m ? $" ({m})" : "");
            account.ToolTip = $"{exe}{(a.Org != null ? "\n" + a.Org : "")}";
            accountButtons.Children.Add(Small(T("Sign out"), () => { ClaudeCli.Auth(exe, false); }));
        }
        else
        {
            account.Text = a == null ? T("Couldn't ask Claude Code who's signed in.") : T("Not signed in to Claude Code: sign in with your Claude account (a browser opens).");
            accountButtons.Children.Add(Small(T("Sign in"), () => ClaudeCli.Auth(exe, true)));
        }
        accountButtons.Children.Add(Small(T("Check again"), () => _ = CheckAccountAsync()));
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
        string folder = settings.LastFolder is { Length: > 0 } f && System.IO.Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        int n = 1;
        while (settings.Sessions.Any(s => s.Name == F("Session {0}", n))) n++;
        var info = new SessionInfo { Name = F("Session {0}", n), Folder = folder, Model = settings.Model, Mode = settings.Mode };
        settings.Sessions.Add(info);
        settings.Save();
        Select(AddTab(info));
    }

    private Tab AddTab(SessionInfo info)
    {
        var view = new SessionView(info, settings, () => Exe) { Visibility = Visibility.Collapsed };
        pages.Children.Add(view);
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = info.Name, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        var edit = new TextBox { Text = info.Name, MinWidth = 120, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 0, 2, 0) };
        var close = new Button { Content = "✕", FontSize = 9, Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Close this session"), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(name);
        header.Children.Add(edit);
        header.Children.Add(close);
        var button = new RadioButton { Content = header, GroupName = "ClaudeCodeSessions" };
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
        view.StateChanged += () => { ShowDot(tab); UpdateBlink(); };
        view.Renamed += () => { name.Text = info.Name; AutomationProperties.SetName(button, info.Name); };
        ShowDot(tab);
        return tab;
    }

    private void Select(Tab tab)
    {
        foreach (var t in tabs) t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed;
        if (tab.Button.IsChecked != true) tab.Button.IsChecked = true;
        settings.Selected = tab.View.Info.Id;
        settings.Save();
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
        if (tab.View.Busy && MessageBox.Show(Window.GetWindow(this), F("{0} is still working. Stop it and close the tab? (The session is kept in Claude Code: claude --resume picks it up.)", tab.View.Info.Name),
                T("Claude Code"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        tab.View.Close();
        int index = tabs.IndexOf(tab);
        tabs.Remove(tab);
        strip.Children.Remove(tab.Button);
        pages.Children.Remove(tab.View);
        settings.Sessions.Remove(tab.View.Info);
        settings.Save();
        if (tabs.Count == 0) NewSession();
        else Select(tabs[Math.Min(index, tabs.Count - 1)]);
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

    /// <summary>Whether any session is working (for the tray, and asking before closing).</summary>
    public int Working => tabs.Count(t => t.View.Busy);

    public void CloseAll()
    {
        foreach (var t in tabs) t.View.Close();
    }

    // ---------------------------------------------------------------- helpers

    private static Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
