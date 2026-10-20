using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Add an account…: its kind first (only Claude so far), then for Claude how it signs in (a subscription or the
/// Anthropic Console), its name here and email. Sign in… keeps the account in use, runs Claude Code's sign-in (a
/// console window that opens the browser) and watches for the new sign-in, which is then added and used.
/// </summary>
internal sealed class AddAccountWindow : Window
{
    private readonly AiSettings settings;
    private readonly Func<string?> findExe;
    private readonly ComboBox how = new();
    private readonly TextBox name = new() { ToolTip = T("What it's called here (empty: its email)") };
    private readonly TextBox email = new() { ToolTip = T("Filled in on the sign-in page (optional)") };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button signIn = new() { Content = T("Sign in…"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button close = new() { Content = T("Cancel"), Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? before;
    private DateTime started;

    public AddAccountWindow(AiSettings settings, Func<string?> findExe)
    {
        this.settings = settings;
        this.findExe = findExe;
        Title = T("Add an account");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var s = new StackPanel { Margin = new Thickness(20) };
        s.Children.Add(Heading(T("What kind of account")));
        foreach (var (id, kindName, about) in AccountKinds.All)
        {
            var radio = new RadioButton { GroupName = "Kind", IsChecked = id == AccountKinds.Claude, Margin = new Thickness(0, 2, 0, 0), Content = new TextBlock { Text = kindName, FontWeight = FontWeights.SemiBold } };
            s.Children.Add(radio);
            s.Children.Add(Secondary(about, new Thickness(22, 2, 0, 0)));
        }
        s.Children.Add(Secondary(T("Other AIs (Google Gemini, OpenAI, OpenRouter, Ollama…) are set up with their keys under Providers."), new Thickness(0, 6, 0, 0)));

        s.Children.Add(Heading(T("The Claude account")));
        how.Items.Add(T("Claude subscription (Pro, Max, Team, Enterprise)"));
        how.Items.Add(T("Anthropic Console (API usage billing)"));
        how.SelectedIndex = 0;
        s.Children.Add(Row(T("It signs in with"), how));
        s.Children.Add(Row(T("Name here"), name));
        s.Children.Add(Row(T("Its email"), email));
        s.Children.Add(Secondary(T("Sign in… keeps the account in use now (so you can switch back to it), then opens a window running Claude Code's sign-in, which opens your browser: sign in there with the other account (if the browser is signed in to Claude as someone else, sign out there first). Once it's done, the account is added here, and Claude Code uses it: the sessions here start on it with their next message."), new Thickness(0, 8, 0, 0)));
        signIn.SetResourceReference(StyleProperty, "AccentButton");
        signIn.Click += (_, _) => Start();
        s.Children.Add(signIn);
        s.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(close);
        s.Children.Add(buttons);
        Content = s;

        watch.Tick += (_, _) => Check();
        Closed += (_, _) => watch.Stop();
        Loaded += (_, _) => name.Focus();
    }

    private void Start()
    {
        if (findExe() is not { } exe) { Tell(T("Claude Code isn't installed on this PC (or point to claude.exe in the Claude Account box)."), true); return; }
        // the one in use is kept first: the sign-in replaces it
        ClaudeAccounts.Keep(settings);
        settings.NotifyAccountsChanged();
        before = ClaudeAccounts.Current()?.Json;
        started = DateTime.Now;
        try { ClaudeCli.Login(exe, how.SelectedIndex == 1, email.Text.Trim()); }
        catch (Exception ex) { Tell(F("Claude Code's sign-in couldn't be started: {0}", ex.Message), true); return; }
        Logging.Log.Here.Info("Claude accounts: signing in to another account");
        Tell(T("Waiting for you to sign in, in the browser (then close the console window)…"), false);
        signIn.Content = T("Sign in again…");
        watch.Start();
    }

    /// <summary>Every 2 seconds while it waits: once Claude Code has a different sign-in, it's added.</summary>
    private void Check()
    {
        if (DateTime.Now - started > TimeSpan.FromMinutes(15)) { watch.Stop(); Tell(T("No new sign-in after 15 minutes: Sign in again… to try once more."), true); return; }
        if (ClaudeAccounts.Current() is not { } now || now.Json == before) return;
        watch.Stop();
        string? was = settings.ActiveAccount;
        var (a, added, _) = ClaudeAccounts.Keep(settings, name.Text.Trim());
        if (a == null) return;
        Tell(added ? F("Signed in as {0}: added, and Claude Code uses it now.", a.Description)
            : F("That's {0}, already in the list: its sign-in is renewed, and Claude Code uses it now.", a.Description), false);
        close.Content = T("Done");
        settings.NotifyAccountsChanged();
        if (a.Id != was) settings.NotifyAccountSwitched();
    }

    private void Tell(string text, bool error)
    {
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    internal static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 12, 0, 6) };

    internal static TextBlock Secondary(string text, Thickness margin)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }
}
