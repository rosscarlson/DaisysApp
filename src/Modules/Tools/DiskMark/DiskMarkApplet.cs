using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.DiskMark;

/// <summary>
/// DiskMark, a sub-tab of the Tools tab: a drive's read and write speeds measured as CrystalDiskMark 8 does (its
/// profiles, or your own tests), every run kept, with a schedule (off until set). Settings → Tools → DiskMark: the
/// unit, the bars' scale and the schedule.
/// </summary>
public sealed class DiskMarkApplet : IApplet
{
    private readonly DiskMarkSettings settings = DiskMarkSettings.Load();
    private readonly Runner runner;
    private readonly DiskMarkView view;
    private readonly FrameworkElement settingsView;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };

    public DiskMarkApplet()
    {
        runner = new Runner(settings);
        view = new DiskMarkView(settings, runner);
        settingsView = SettingsPage(settings, runner, view.Refresh);
        clock.Tick += (_, _) => runner.Tick();
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        clock.Start();
        view.Dispatcher.BeginInvoke(runner.Tick, DispatcherPriority.ApplicationIdle);
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        clock.Stop();
        runner.Cancel();
        view.Shutdown();
    }

    // ---------------------------------------------------------------- Settings → Tools → DiskMark

    private static FrameworkElement SettingsPage(DiskMarkSettings settings, Runner runner, Action changed)
    {
        var shown = new StackPanel();
        shown.Children.Add(Ui.Header(T("Results")));
        shown.Children.Add(Line(T("Speeds in"), Combo([("GB/s", "GB/s"), ("MB/s", "MB/s")], settings.Unit, v => { settings.Unit = (string)v; settings.Save(); changed(); })));
        shown.Children.Add(Line(T("Bars"), Combo([(T("Linear"), false), (T("Logarithmic"), true)], settings.LogScale, v => { settings.LogScale = (bool)v; settings.Save(); changed(); },
            T("Logarithmic: a random 4K result's bar still shows beside a sequential one"))));
        var left = Ui.Card(shown);
        left.VerticalAlignment = VerticalAlignment.Top;
        left.Margin = new Thickness(0, 0, 12, 0);
        left.MinWidth = 280;
        var right = Ui.Card(ScheduleBox(settings, runner));
        right.VerticalAlignment = VerticalAlignment.Top;
        right.MinWidth = 380;
        return new WrapPanel { Children = { left, right } };
    }

    private static FrameworkElement Line(string label, FrameworkElement control)
    {
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 110 };
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { t, control } };
    }

    private static ComboBox Combo(IEnumerable<(string Text, object Value)> items, object current, Action<object> set, string? tip = null)
    {
        var box = new ComboBox { MinWidth = 140, ToolTip = tip };
        foreach (var (text, value) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, current)) ?? box.Items[0];
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: { } v }) set(v); };
        return box;
    }

    /// <summary>The schedule: on or off (off at first), when it runs first, how often, and on which drives (with the page's options).</summary>
    private static FrameworkElement ScheduleBox(DiskMarkSettings settings, Runner runner)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header(T("Schedule")));
        var on = new CheckBox { Content = T("Run tests on a schedule"), IsChecked = settings.ScheduleOn, Margin = new Thickness(0, 8, 0, 0) };
        var body = new StackPanel();
        var next = Ui.Secondary();
        var first = settings.NextRun ?? DateTime.Today.AddDays(1).AddHours(3);
        var date = new TextBox { Text = first.ToString("d"), Width = 110, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = F("A date, e.g. {0}", DateTime.Today.ToString("d")) };
        System.Windows.Automation.AutomationProperties.SetName(date, T("Date"));
        var time = new TextBox { Text = first.ToString("HH:mm"), Width = 60, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = T("24-hour time, e.g. 03:00") };
        System.Windows.Automation.AutomationProperties.SetName(time, T("Time"));

        void Show()
        {
            body.IsEnabled = settings.ScheduleOn;
            body.Opacity = settings.ScheduleOn ? 1 : 0.5;
            next.Text = settings.ScheduleOn && settings.NextRun is DateTime n
                ? F("Next run {0}", n.ToString("g")) + (settings.ScheduleDrives.Count == 0 ? " · " + T("no drives picked") : "")
                : "";
        }
        void SetWhen()
        {
            if (!DateTime.TryParse(date.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)) return;
            if (!TimeSpan.TryParseExact(time.Text.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var t) || t.TotalHours >= 24) return;
            var when = d.Date + t;
            // in the past and repeating: from its next turn
            if (settings.RepeatHours > 0) while (when < DateTime.Now) when = when.AddHours(settings.RepeatHours);
            settings.NextRun = when;
            settings.Save();
            Show();
        }
        on.Checked += (_, _) => { settings.ScheduleOn = true; if (settings.NextRun == null || settings.NextRun < DateTime.Now) SetWhen(); settings.Save(); Show(); };
        on.Unchecked += (_, _) => { settings.ScheduleOn = false; settings.Save(); Show(); };
        date.LostFocus += (_, _) => SetWhen();
        date.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SetWhen(); };
        time.LostFocus += (_, _) => SetWhen();
        time.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) SetWhen(); };
        body.Children.Add(Line(T("First run"), new StackPanel { Orientation = Orientation.Horizontal, Children = { date, time } }));
        body.Children.Add(Line(T("Repeat"), Combo([(T("Once"), 0), (T("Every hour"), 1), (T("Every 6 hours"), 6), (T("Every 12 hours"), 12), (T("Every day"), 24), (T("Every week"), 168), (T("Every 30 days"), 720)],
            settings.RepeatHours, v => { settings.RepeatHours = (int)v; settings.Save(); SetWhen(); })));
        var drivesBox = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var d in Drives.List())
        {
            var box = new CheckBox { Content = string.IsNullOrEmpty(d.Label) ? d.Letter : $"{d.Letter} {d.Label}", Margin = new Thickness(0, 4, 14, 0), ToolTip = d.Model,
                IsChecked = settings.ScheduleDrives.Contains(d.Root, StringComparer.OrdinalIgnoreCase) };
            box.Checked += (_, _) => { if (!settings.ScheduleDrives.Contains(d.Root, StringComparer.OrdinalIgnoreCase)) settings.ScheduleDrives.Add(d.Root); settings.Save(); Show(); };
            box.Unchecked += (_, _) => { settings.ScheduleDrives.RemoveAll(x => x.Equals(d.Root, StringComparison.OrdinalIgnoreCase)); settings.Save(); Show(); };
            drivesBox.Children.Add(box);
        }
        var drivesLine = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var dl = new TextBlock { Text = T("Drives"), Width = 110, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(dl, Dock.Left);
        drivesLine.Children.Add(dl);
        drivesLine.Children.Add(drivesBox);
        body.Children.Add(drivesLine);
        var uses = Ui.Secondary(T("Uses the DiskMark page's profile, runs, size and times"));
        uses.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(uses);
        next.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(on);
        panel.Children.Add(body);
        panel.Children.Add(next);
        runner.Changed += () => panel.Dispatcher.BeginInvoke(Show);
        Show();
        return panel;
    }

    /// <summary>The schedule in a window (the page's Schedule… button).</summary>
    internal static void EditSchedule(FrameworkElement near, DiskMarkSettings settings, Runner runner)
    {
        var w = Dialog(near, T("DiskMark Schedule"), 470);
        var close = new Button { Content = T("Close"), MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        close.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        close.Click += (_, _) => w.Close();
        w.Content = new StackPanel { Margin = new Thickness(20, 16, 20, 16), Children = { ScheduleBox(settings, runner), close } };
        w.ShowDialog();
    }

    /// <summary>The Custom profile's tests: up to six, each sequential or random, its block size, queue and threads.</summary>
    internal static void EditTests(FrameworkElement near, DiskMarkSettings settings)
    {
        var w = Dialog(near, T("Custom Tests"), 560);
        var tests = settings.CustomTests.Select(t => t.Copy()).ToList();
        if (tests.Count == 0) tests = Profiles.DefaultTests();
        var rows = new StackPanel();
        int[] blocks = [4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192];
        int[] queues = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
        int[] threads = [1, 2, 4, 8, 16, 32, 64];

        void Build()
        {
            rows.Children.Clear();
            foreach (var t in tests.ToList())
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                line.Children.Add(Combo([(T("Sequential"), Pattern.Seq), (T("Random"), Pattern.Rnd)], t.Pattern, v => t.Pattern = (Pattern)v));
                line.Children.Add(Small(Combo(blocks.Select(b => (b >= 1024 ? $"{b / 1024} MiB" : $"{b} KiB", (object)b)), t.BlockKiB, v => t.BlockKiB = (int)v, T("Block size"))));
                line.Children.Add(Small(Combo(queues.Select(q => ($"Q{q}", (object)q)), t.Queues, v => t.Queues = (int)v, T("Requests each thread keeps waiting"))));
                line.Children.Add(Small(Combo(threads.Select(n => ($"T{n}", (object)n)), t.Threads, v => t.Threads = (int)v, T("Threads"))));
                var remove = Ui.IconButton("", "", T("Remove"), (_, _) => { if (tests.Count > 1) { tests.Remove(t); Build(); } });
                remove.Margin = new Thickness(8, 0, 0, 0);
                remove.IsEnabled = tests.Count > 1;
                line.Children.Add(remove);
                rows.Children.Add(line);
            }
        }
        static ComboBox Small(ComboBox c) { c.MinWidth = 90; c.Margin = new Thickness(8, 0, 0, 0); return c; }
        Build();
        var add = Ui.IconButton("", T("Add"), null, (_, _) => { if (tests.Count < 6) { tests.Add(new TestDef(Pattern.Rnd, 4, 1, 1)); Build(); } });
        var reset = new Button { Content = T("Default Tests"), Margin = new Thickness(8, 0, 0, 0) };
        reset.Click += (_, _) => { tests = Profiles.DefaultTests(); Build(); };
        var ok = new Button { Content = T("Save"), MinWidth = 90, IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        ok.Click += (_, _) => { settings.CustomTests = tests; settings.Profile = Profiles.Custom; settings.Save(); w.DialogResult = true; };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { ok, cancel } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { add, reset } });
        w.Content = new StackPanel { Margin = new Thickness(20, 16, 20, 16), Children = { rows, buttons } };
        w.ShowDialog();
    }

    private static Window Dialog(FrameworkElement near, string title, double width)
    {
        var owner = Window.GetWindow(near) ?? Application.Current?.MainWindow;
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Owner = owner, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => Theming.ThemeManager.ApplyTitleBar(w);
        return w;
    }
}
