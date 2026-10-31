using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Add an account…: its kind first (only Claude so far), then for Claude how it signs in (a subscription or the
/// Anthropic Console), its name here and email. Sign in… runs Claude Code's sign-in in a folder of its own (a console
/// window that opens the browser; Claude Code's own sign-in isn't changed) and watches for it, and the account is added.
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
    private string? folder;
    private DateTime started;

    /// <summary>The account added (or signed in to again), once it is.</summary>
    public AiAccount? Added { get; private set; }

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
        s.Children.Add(Secondary(T("Sign in… opens a window running Claude Code's sign-in, which opens your browser: sign in there with the other account (if the browser is signed in to Claude as someone else, sign out there first). Once it's done, the account is added here, for new sessions to pick. Sessions running now, and Claude Code in a terminal, carry on as they are."), new Thickness(0, 8, 0, 0)));
        signIn.SetResourceReference(StyleProperty, "AccentButton");
        signIn.Click += (_, _) => Start();
        s.Children.Add(signIn);
        s.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(close);
        s.Children.Add(buttons);
        Content = s;

        watch.Tick += (_, _) => Check();
        Closed += (_, _) => { watch.Stop(); Forget(); };
        Loaded += (_, _) => name.Focus();
    }

    private void Start()
    {
        if (findExe() is not { } exe) { Tell(T("Claude Code isn't installed on this PC."), true); return; }
        Forget();
        started = DateTime.Now;
        try
        {
            folder = ClaudeAccounts.NewSignInFolder();
            ClaudeCli.Login(exe, how.SelectedIndex == 1, email.Text.Trim(), folder);
        }
        catch (Exception ex) { Tell(F("Claude Code's sign-in couldn't be started: {0}", ex.Message), true); return; }
        Logging.Log.Here.Info("Claude accounts: signing in to another account");
        Tell(T("Waiting for you to sign in, in the browser (then close the console window)…"), false);
        signIn.Content = T("Sign in again…");
        watch.Start();
    }

    /// <summary>Every 2 seconds while it waits: once there's a sign-in in its folder, it's added.</summary>
    private void Check()
    {
        if (DateTime.Now - started > TimeSpan.FromMinutes(15)) { watch.Stop(); Tell(T("No new sign-in after 15 minutes: Sign in again… to try once more."), true); return; }
        if (folder == null || ClaudeAccounts.Current(folder) is not { Uuid.Length: > 0 } and not { Email.Length: > 0 }) return;
        watch.Stop();
        var (a, added) = ClaudeAccounts.KeepFrom(settings, folder, name.Text.Trim());
        folder = null;
        if (a == null) return;
        Added = a;
        Tell(added ? F("Signed in as {0}: added.", a.Description) : F("That's {0}, already in the list: its sign-in is renewed.", a.Description), false);
        close.Content = T("Done");
        settings.NotifyAccountsChanged();
    }

    /// <summary>A sign-in folder not used (cancelled, or signing in again) is deleted.</summary>
    private void Forget()
    {
        if (folder == null) return;
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        folder = null;
    }

    private void Tell(string text, bool error)
    {
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    internal static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 12, 0, 6) };

    internal static TextBlock Secondary(string text, Thickness margin) => Ui.Note(text, margin);

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
