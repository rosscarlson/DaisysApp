using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Settings → Support → Networking Tools: Ping's and Speed Test's logging (on / off, how often, for how long, how long
/// the files are kept), Speed Test's length and warning levels, Events' weak Wi-Fi level, and Port Scan's timing.
/// </summary>
internal sealed class NetworkingSettingsView : UserControl
{
    private static readonly int[] ForHours = [0, 1, 4, 8, 24, 72, 168];
    private readonly NetworkingSettings settings;
    private readonly PingMonitor? pings;
    private readonly TextBlock pingUntil = Ui.Secondary(), speedUntil = Ui.Secondary();

    /// <param name="speedOnly">Only Speed Test's card (its gear on the Speed Test tab).</param>
    public NetworkingSettingsView(NetworkingSettings settings, PingMonitor? pings, SpeedTester? speed, EventMonitor? events, bool speedOnly = false)
    {
        this.settings = settings;
        this.pings = pings;
        var cards = new WrapPanel();
        if (pings != null) cards.Children.Add(PingCard());
        if (speed != null) cards.Children.Add(SpeedCard());
        if (events != null) cards.Children.Add(Card(T("Events"), Row(T("Weak Wi-Fi under"), Number(settings.WeakSignalDbm, -100, -30, v => settings.WeakSignalDbm = v), "dBm")));
        if (!speedOnly) cards.Children.Add(Card(T("Port Scan and Host Scan"),
            Row(T("Wait for an answer"), Number(settings.ScanTimeoutMs, 100, 10000, v => settings.ScanTimeoutMs = v), "ms"),
            Row(T("Ports at once"), Number(settings.ScanParallel, 1, 1000, v => settings.ScanParallel = v), "")));
        Content = cards;
        if (pings != null) pings.LoggingChanged += () => Dispatcher.BeginInvoke(ShowUntil);
        IsVisibleChanged += (_, _) => { if (IsVisible) ShowUntil(); };
        ShowUntil();
    }

    private static Border Card(string title, params UIElement[] rows)
    {
        var panel = new StackPanel { Children = { Ui.Header(title) } };
        foreach (var r in rows) panel.Children.Add(r);
        var card = Ui.Card(panel, new Thickness(0, 0, 12, 12));
        card.Width = 400;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private static FrameworkElement Row(string label, FrameworkElement control, string unit)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
        var l = new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(l);
        row.Children.Add(control);
        if (unit.Length > 0) row.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        return row;
    }

    /// <summary>A number box, saved as it's typed when it's within its range.</summary>
    private TextBox Number(double value, double min, double max, Action<int> set)
    {
        var box = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int v) && v >= min && v <= max)
            {
                set(v);
                settings.Save();
                box.ClearValue(Control.BorderBrushProperty);
            }
            else box.SetResourceReference(Control.BorderBrushProperty, "DangerBrush");
        };
        return box;
    }

    private static IEnumerable<(string, int)> ForChoices() => ForHours.Select(h => (h == 0 ? T("Until switched off") : h < 24 ? P(h, "{0} hour", "{0} hours") : P(h / 24, "{0} day", "{0} days"), h));

    private static DateTime? Until(int hours) => hours > 0 ? DateTime.Now.AddHours(hours) : null;

    private Border PingCard()
    {
        var log = new CheckBox { Content = T("Log every round"), IsChecked = settings.PingLog, Margin = new Thickness(0, 0, 0, 10) };
        Ui.OnToggle(log, on =>
        {
            settings.PingLog = on;
            settings.PingLogUntil = settings.PingLog ? Until(settings.PingLogHours) : null;
            settings.Save();
            Logging.Log.Here.Info(settings.PingLog ? $"Ping logging on, every {settings.PingSeconds} s" : "Ping logging off");
            ShowUntil();
        });
        var every = Ui.Choice(new[] { 1, 2, 5, 10, 30, 60 }.Select(s => (F("{0} s", s), s)), settings.PingSeconds,
            s => { settings.PingSeconds = s; settings.Save(); pings?.Reschedule(); }, 150);
        var hours = Ui.Choice(ForChoices(), settings.PingLogHours, h =>
        {
            settings.PingLogHours = h;
            if (settings.PingLog) settings.PingLogUntil = Until(h);
            settings.Save();
            ShowUntil();
        }, 150);
        var keep = Ui.Choice(new[] { 7, 30, 90, 365 }.Select(d => (P(d, "{0} day", "{0} days"), d)), settings.KeepDays,
            d => { settings.KeepDays = d; settings.Save(); pings?.Cleanup(); }, 150);
        var folder = Ui.Button(T("Open Folder"), () =>
        {
            Directory.CreateDirectory(PingMonitor.Folder);
            Ui.OpenUrl(PingMonitor.Folder);
        });
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        pingUntil.Margin = new Thickness(0, 0, 0, 8);
        return Card(T("Ping"), log, Row(T("Every"), every, ""), Row(T("For"), hours, ""), pingUntil, Row(T("Keep files"), keep, ""), folder);
    }

    private Border SpeedCard()
    {
        var log = new CheckBox { Content = T("Test on a schedule"), IsChecked = settings.SpeedLog, Margin = new Thickness(0, 0, 0, 10) };
        Ui.OnToggle(log, on =>
        {
            settings.SpeedLog = on;
            settings.SpeedLogUntil = settings.SpeedLog ? Until(settings.SpeedLogHours) : null;
            settings.Save();
            Logging.Log.Here.Info(settings.SpeedLog ? $"Speed tests every {settings.SpeedMinutes} min" : "Scheduled speed tests off");
            ShowUntil();
        });
        var every = Ui.Choice(new[] { 5, 10, 15, 30, 60, 120, 240, 360, 720, 1440 }.Select(m => (SpeedView.Every(m), m)), settings.SpeedMinutes,
            m => { settings.SpeedMinutes = m; settings.Save(); }, 150);
        var hours = Ui.Choice(ForChoices(), settings.SpeedLogHours, h =>
        {
            settings.SpeedLogHours = h;
            if (settings.SpeedLog) settings.SpeedLogUntil = Until(h);
            settings.Save();
            ShowUntil();
        }, 150);
        speedUntil.Margin = new Thickness(0, 0, 0, 8);
        var length = Number(settings.SpeedSeconds, SpeedTester.MinSeconds, SpeedTester.MaxSeconds, v => settings.SpeedSeconds = v);
        length.ToolTip = T("Seconds of download, then of upload");
        var levels = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (int c = 0; c < 4; c++) levels.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(130) : GridLength.Auto });
        void Level(int row, string name, double warn, double bad, Action<int> setWarn, Action<int> setBad, string unit)
        {
            levels.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = new FrameworkElement[]
            {
                new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                Number(warn, 0, 100000, setWarn), Number(bad, 0, 100000, setBad), Ui.Secondary(unit),
            };
            for (int c = 0; c < cells.Length; c++)
            {
                cells[c].Margin = new Thickness(c == 0 ? 0 : 8, 0, 0, 6);
                if (cells[c] is TextBox t) t.Width = 70;
                cells[c].VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(cells[c], row + 1);
                Grid.SetColumn(cells[c], c);
                levels.Children.Add(cells[c]);
            }
        }
        levels.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var orange = Ui.Secondary(T("Orange"));
        var red = Ui.Secondary(T("Red"));
        orange.Margin = red.Margin = new Thickness(8, 0, 0, 4);
        Grid.SetColumn(orange, 1);
        Grid.SetColumn(red, 2);
        levels.Children.Add(orange);
        levels.Children.Add(red);
        Level(0, T("Download under"), settings.SpeedWarnDown, settings.SpeedBadDown, v => settings.SpeedWarnDown = v, v => settings.SpeedBadDown = v, "Mbit/s");
        Level(1, T("Upload under"), settings.SpeedWarnUp, settings.SpeedBadUp, v => settings.SpeedWarnUp = v, v => settings.SpeedBadUp = v, "Mbit/s");
        Level(2, T("Latency over"), settings.SpeedWarnLatency, settings.SpeedBadLatency, v => settings.SpeedWarnLatency = v, v => settings.SpeedBadLatency = v, "ms");
        levels.ToolTip = T("0 turns a level off");
        return Card(T("Speed Test"), log, Row(T("Every"), every, ""), Row(T("For"), hours, ""), speedUntil, Row(T("Test length"), length, T("seconds")), levels);
    }

    private void ShowUntil()
    {
        pingUntil.Text = settings.PingLog ? settings.PingLogUntil is DateTime p ? F("Logging until {0}", Ui.When(p)) : T("Logging") : "";
        pingUntil.Visibility = pingUntil.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        speedUntil.Text = settings.SpeedLog ? settings.SpeedLogUntil is DateTime s ? (s < DateTime.Now ? F("Finished {0}", Ui.When(s)) : F("Testing until {0}", Ui.When(s))) : T("Testing") : "";
        speedUntil.Visibility = speedUntil.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
