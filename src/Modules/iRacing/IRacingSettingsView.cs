using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Logging;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// Settings → Gaming → iRacing: importing an export made in the browser (the export script, copied from here),
/// signing in (your iRacing email and password, and your OAuth client's ID and secret, with Test sign-in), what's
/// fetched (hosted sessions, syncing when the tab opens) and the races kept on this PC.
/// </summary>
internal sealed class IRacingSettingsView : UserControl
{
    private readonly IRacingSettings settings;
    private readonly IRacingView view;
    private readonly TextBlock testResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public IRacingSettingsView(IRacingSettings settings, IRacingView view)
    {
        this.settings = settings;
        this.view = view;
        var root = new StackPanel();
        root.Children.Add(Card(Export(), T("Importing from the browser (no OAuth client needed)")));
        root.Children.Add(Card(SignIn(), T("Signing in to iRacing (with an OAuth client)")));
        root.Children.Add(Card(Fetching(), T("Fetching")));
        root.Children.Add(Card(Kept(), T("Races kept on this PC")));
        Content = root;
    }

    private UIElement Export()
    {
        var s = new StackPanel();
        s.Children.Add(Secondary(T("The export script reads your whole history through your own sign-in on iRacing's website, so it works without a client ID. It only reads; it changes nothing.")));
        string[] steps =
        {
            T("1. Copy the export script (below)."),
            T("2. Sign in at members-ng.iracing.com in Chrome, Edge or Firefox."),
            T("3. Press F12 and open the Console (if the browser asks, type allow pasting first)."),
            T("4. Paste the script and press Enter. Leave the tab open until it's done: 10 to 30 minutes, its progress shows in the console."),
            T("5. Four files download: races_all.csv, irating_history.csv, sr_history.csv and iracing_raw.json."),
            T("6. Import iracing_raw.json (it has the most in it; races_all.csv works too, with the two history files beside it)."),
        };
        foreach (var step in steps) s.Children.Add(new TextBlock { Text = step, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        s.Children.Add(Secondary(T("Import again whenever you like: sessions already kept are updated, new ones added.")));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var copy = new Button { Content = T("Copy the export script"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        var result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        result.SetResourceReference(StyleProperty, "SecondaryText");
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(System.IO.File.ReadAllText(RaceImport.ScriptPath));
                result.Text = T("Copied: paste it into the browser's console on members-ng.iracing.com.");
            }
            catch (Exception ex) { result.Text = F("It couldn't be copied: {0}", ex.Message); }
        };
        var import = new Button { Content = T("Import"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        import.Click += (_, _) => view.ImportFile();
        var site = new Button { Content = T("Open iRacing's website"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        site.Click += (_, _) => Open("https://members-ng.iracing.com/");
        buttons.Children.Add(copy);
        buttons.Children.Add(site);
        buttons.Children.Add(import);
        buttons.Children.Add(result);
        s.Children.Add(buttons);
        return s;
    }

    private UIElement SignIn()
    {
        var s = new StackPanel();
        s.Children.Add(Secondary(T("iRacing's Data API signs in with OAuth: your iRacing account (email and password) and an OAuth client of your own, which iRacing gives you (a client ID and a client secret, for the \"password limited\" sign-in, with your account on the client's list of users). The password and secret are sent masked, as iRacing asks, and kept here encrypted for your Windows account. Two-factor sign-in isn't asked for this kind of client.")));
        var link = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };
        var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(T("How to get a client ID (iRacing's OAuth documentation)")));
        h.Click += (_, _) => Open("https://oauth.iracing.com/oauth2/book/");
        link.Inlines.Add(h);
        s.Children.Add(link);

        var email = new TextBox { Text = settings.Email };
        var password = new PasswordBox { Password = settings.Password };
        var clientId = new TextBox { Text = settings.ClientId };
        var secret = new PasswordBox { Password = settings.ClientSecret };
        email.TextChanged += (_, _) => { settings.Email = email.Text.Trim(); Changed(); };
        password.PasswordChanged += (_, _) => { settings.Password = password.Password; Changed(); };
        clientId.TextChanged += (_, _) => { settings.ClientId = clientId.Text.Trim(); Changed(); };
        secret.PasswordChanged += (_, _) => { settings.ClientSecret = secret.Password; Changed(); };
        s.Children.Add(Row(T("iRacing email"), email));
        s.Children.Add(Row(T("Password"), password));
        s.Children.Add(Row(T("Client ID"), clientId));
        s.Children.Add(Row(T("Client secret"), secret));

        var test = new Button { Content = T("Test sign-in"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            testResult.Text = T("Signing in…");
            try
            {
                var api = new IRacingApi(settings);
                await api.SignInAsync(CancellationToken.None, fresh: true);
                var info = await api.GetAsync("/member/info", CancellationToken.None);
                testResult.Text = F("Signed in as {0} (member {1}).", IRacingApi.Str(info, "display_name"), IRacingApi.Str(info, "cust_id"));
                view.SettingsChanged();
            }
            catch (IRacingException ex) { testResult.Text = ex.Message; }
            catch (Exception ex) { Log.Here.Warn("iRacing: test sign-in failed", ex); testResult.Text = F("It didn't work: {0}", ex.Message); }
            finally { test.IsEnabled = true; }
        };
        s.Children.Add(test);
        s.Children.Add(testResult);
        return s;
    }

    private void Changed()
    {
        settings.ForgetSignIn();
        settings.Save();
        view.SettingsChanged();
    }

    private UIElement Fetching()
    {
        var s = new StackPanel();
        var hosted = new CheckBox { Content = T("Hosted and league sessions too, not only official series"), IsChecked = settings.IncludeHosted };
        hosted.Click += (_, _) => { settings.IncludeHosted = hosted.IsChecked == true; settings.Save(); };
        s.Children.Add(hosted);
        var auto = new CheckBox { Content = T("Sync when the tab opens"), IsChecked = settings.SyncWhenOpened, Margin = new Thickness(0, 8, 0, 0) };
        auto.Click += (_, _) => { settings.SyncWhenOpened = auto.IsChecked == true; settings.Save(); };
        s.Children.Add(auto);
        var hours = new ComboBox { Width = 160 };
        foreach (int n in new[] { 1, 3, 6, 12, 24 }) hours.Items.Add(new ComboBoxItem { Content = P(n, "after {0} hour", "after {0} hours"), Tag = n });
        hours.SelectedItem = hours.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.SyncHours) ?? hours.Items[2];
        hours.SelectionChanged += (_, _) => { if (hours.SelectedItem is ComboBoxItem i) { settings.SyncHours = (int)i.Tag; settings.Save(); } };
        s.Children.Add(Row(T("If the last sync was"), hours));
        s.Children.Add(Secondary(T("A sync fetches only what's new since the last one (the first fetches every race since you joined, 90 days at a time, which takes a minute or two), and your ratings and iRacing's totals again. iRacing limits how often its API is called: when the limit is reached, the sync waits and carries on.")));
        return s;
    }

    private UIElement Kept()
    {
        var s = new StackPanel();
        s.Children.Add(Secondary(T("Your races, ratings and totals are kept in the iRacing folder of the app's local data, so the tab shows them at once and a sync only fetches what's new.")));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var open = new Button { Content = T("Open the folder"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        open.Click += (_, _) => { System.IO.Directory.CreateDirectory(RaceData.Folder); Open(RaceData.Folder); };
        var delete = new Button { Content = T("Delete them and fetch everything again"), Padding = new Thickness(12, 4, 12, 4), ToolTip = T("The next sync fetches them all again from iRacing") };
        ConfirmButton.Arm(delete, () =>
        {
            RaceData.Delete();
            view.DataDeleted();
        });
        buttons.Children.Add(open);
        buttons.Children.Add(delete);
        s.Children.Add(buttons);
        return s;
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Here.Warn("iRacing: couldn't open " + target, ex); }
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(l);
        control.HorizontalAlignment = control is ComboBox ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static Border Card(UIElement child, string header)
    {
        var s = new StackPanel();
        var h = new TextBlock { Text = header };
        h.SetResourceReference(StyleProperty, "CardHeader");
        s.Children.Add(h);
        s.Children.Add(child);
        var b = new Border { Child = s };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }
}
