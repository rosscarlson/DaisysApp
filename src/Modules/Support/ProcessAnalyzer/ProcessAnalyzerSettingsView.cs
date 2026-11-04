using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>Settings → Support → Process Analyzer: Daisy's Server, the look while the tab isn't showing, VirusTotal's
/// key, and how many days of history.</summary>
internal sealed class ProcessAnalyzerSettingsView : UserControl
{
    private readonly TextBlock serverStatus = Ui.Secondary("", wrap: true);
    private readonly ProcessAnalyzerApplet applet;

    public ProcessAnalyzerSettingsView(ProcessAnalyzerApplet applet)
    {
        this.applet = applet;
        var s = applet.Settings;
        var cards = new WrapPanel();

        var useServer = Check(T("Use Daisy's Server's database"), s.UseServer, T("Send and receive classifications; the server's database is the master (set the server up in Settings → General)"), on =>
        {
            s.UseServer = on;
            applet.ApplyServer();
        });
        cards.Children.Add(Card(T("Daisy's Server"), useServer, serverStatus));

        var watch = Check(T("Look while the tab isn't showing"), s.Watch, T("And notify about a risky or dangerous program"), on => { s.Watch = on; applet.ApplyWatch(); });
        var minutes = Ui.Choice(new[] { 1, 5, 15, 30, 60 }.Select(m => (P(m, "every minute", "every {0} minutes"), m)), s.WatchMinutes, m =>
        {
            s.WatchMinutes = m;
            s.SaveSoon();
            applet.ApplyWatch();
        }, 160);
        minutes.Margin = new Thickness(24, 0, 0, 8);
        minutes.HorizontalAlignment = HorizontalAlignment.Left;
        var days = Ui.Choice(new[] { 1, 3, 7, 14, 30 }.Select(d => (P(d, "{0} day", "{0} days"), d)), s.HistoryDays, d => { s.HistoryDays = d; s.SaveSoon(); }, 160);
        days.HorizontalAlignment = HorizontalAlignment.Left;
        days.ToolTip = T("How far back Hardware → Performance's log is read for each program's history");
        cards.Children.Add(Card(T("Looking"), watch, minutes, Ui.Secondary(T("History")), days));

        var key = new TextBox { Text = s.VirusTotalKey, Width = 360, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = T("Free from virustotal.com (your account → API key)") };
        System.Windows.Automation.AutomationProperties.SetName(key, T("VirusTotal API key"));
        key.TextChanged += (_, _) => { s.VirusTotalKey = key.Text.Trim(); s.SaveSoon(); };
        var auto = Check(T("Look up unknown programs that aren't signed"), s.AutoVirusTotal, T("Only the file's hash is sent; 4 lookups a minute"), on => s.AutoVirusTotal = on);
        cards.Children.Add(Card(T("VirusTotal"), Ui.Secondary(T("API key")), key, auto));

        Content = cards;
        applet.ServerChanged += () => Dispatcher.BeginInvoke(ShowStatus);
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowStatus(); };
        ShowStatus();
    }

    private void ShowStatus()
    {
        var db = ProcessDb.Db;
        serverStatus.Text = (applet.Settings.UseServer ? applet.ServerStatus : T("Local only")) + "\n"
            + F("{0} programs known · version {1} · {2} changes to send", db.Count, db.Version, db.DirtyCount);
    }

    private CheckBox Check(string text, bool on, string tip, Action<bool> changed)
    {
        var c = new CheckBox { Content = text, IsChecked = on, ToolTip = tip, Margin = new Thickness(0, 0, 0, 8) };
        Ui.OnToggle(c, v => { changed(v); applet.Settings.SaveSoon(); });
        return c;
    }

    private static Border Card(string title, params UIElement[] rows)
    {
        var panel = new StackPanel { Children = { Ui.Header(title) } };
        foreach (var r in rows) panel.Children.Add(r);
        var card = Ui.Card(panel, new Thickness(0, 0, 12, 12));
        card.Width = 420;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }
}
