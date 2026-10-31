using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Applets.Support.Networking;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Support.StressTests;

/// <summary>
/// Two runs side by side: A and B each one of this PC's runs, or a run exported on another PC (Import…), and the report
/// of how they differ (the PCs, every score and number, in percent). Copy and Save Report… (a web page).
/// </summary>
internal sealed class CompareWindow
{
    private readonly Runner runner;
    private readonly StressSettings settings;
    private readonly Window window;
    private readonly ComboBox pickA = new() { MinWidth = 300 }, pickB = new() { MinWidth = 300 };
    private readonly StackPanel report = new();
    private readonly List<StressRun> imported = [];
    private bool filling;

    public static void Show(FrameworkElement owner, Runner runner, StressSettings settings, StressRun? a = null, StressRun? b = null)
    {
        var w = new CompareWindow(owner, runner, settings, a, b);
        w.window.Show();
    }

    private CompareWindow(FrameworkElement owner, Runner runner, StressSettings settings, StressRun? a, StressRun? b)
    {
        this.runner = runner;
        this.settings = settings;
        window = Ui.Dialog(T("Compare Stress Test Runs"), owner, 1100, 820);
        window.ShowInTaskbar = true;
        window.ResizeMode = ResizeMode.CanResize;

        foreach (var path in settings.Imported.ToList())
        {
            try { var r = StressRun.Read(path); r.Imported = true; imported.Add(r); }
            catch { settings.Imported.Remove(path); }
        }

        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(Label("A"));
        top.Children.Add(pickA);
        top.Children.Add(Label("B", 18));
        top.Children.Add(pickB);
        var import = NetUi.IconButton("", T("Import…"), T("Open a run exported on another PC"), Import);
        import.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(import);
        var swap = NetUi.IconButton("", "", T("Swap A and B"), () =>
        {
            var (x, y) = (pickA.SelectedItem, pickB.SelectedItem);
            filling = true;
            pickA.SelectedItem = y;
            filling = false;
            pickB.SelectedItem = x;
        });
        swap.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(swap);
        var copy = NetUi.IconButton("", T("Copy"), T("Copy the report as text"), () => { if (Pair() is var (x, y)) NetUi.Copy(Report.Text(x, y)); });
        copy.Margin = new Thickness(18, 0, 0, 0);
        top.Children.Add(copy);
        var save = NetUi.IconButton("", T("Save Report…"), T("Save the report as a web page"), SaveReport);
        save.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(save);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(Ui.Page(report));
        window.Content = root;

        Fill(a ?? Find(settings.CompareA), b ?? Find(settings.CompareB));
        pickA.SelectionChanged += (_, _) => Picked();
        pickB.SelectionChanged += (_, _) => Picked();
        Render();
    }

    private static TextBlock Label(string text, double left = 0) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0),
    };

    private IEnumerable<StressRun> All => runner.Runs.OrderByDescending(r => r.Started).Concat(imported);

    private StressRun? Find(string? file) => file == null ? null : All.FirstOrDefault(r => string.Equals(r.File, file, StringComparison.OrdinalIgnoreCase));

    private static string Name(StressRun r) =>
        $"{(r.Imported ? "⤓ " : "")}{r.System.Pc} · {r.Started:g} · {(r.Score > 0 ? F("score {0:#,0}", r.Score) : T("no score"))}{(r.Interrupted ? " · " + T("interrupted") : "")}";

    private void Fill(StressRun? a, StressRun? b)
    {
        filling = true;
        foreach (var box in new[] { pickA, pickB })
        {
            box.Items.Clear();
            foreach (var r in All) box.Items.Add(new ComboBoxItem { Content = Name(r), Tag = r });
        }
        var items = pickA.Items.Cast<ComboBoxItem>().ToList();
        pickA.SelectedIndex = a != null ? items.FindIndex(i => i.Tag == a) : items.Count > 1 ? 1 : 0;
        var itemsB = pickB.Items.Cast<ComboBoxItem>().ToList();
        pickB.SelectedIndex = b != null ? itemsB.FindIndex(i => i.Tag == b) : 0;
        filling = false;
    }

    private void Picked()
    {
        if (filling) return;
        if (Pair() is var (a, b))
        {
            settings.CompareA = a.File;
            settings.CompareB = b.File;
            settings.Save();
        }
        Render();
    }

    private (StressRun A, StressRun B)? Pair() =>
        pickA.SelectedItem is ComboBoxItem { Tag: StressRun a } && pickB.SelectedItem is ComboBoxItem { Tag: StressRun b } ? (a, b) : null;

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Stress test runs") + " (*.json)|*.json", Title = T("Open a Stress Test Run") };
        if (dialog.ShowDialog(window) != true) return;
        try
        {
            var run = StressRun.Read(dialog.FileName);
            run.Imported = true;
            imported.RemoveAll(r => string.Equals(r.File, run.File, StringComparison.OrdinalIgnoreCase));
            imported.Insert(0, run);
            settings.Imported.RemoveAll(p => string.Equals(p, run.File, StringComparison.OrdinalIgnoreCase));
            settings.Imported.Insert(0, dialog.FileName);
            if (settings.Imported.Count > 20) settings.Imported.RemoveRange(20, settings.Imported.Count - 20);
            settings.Save();
            var a = Pair()?.A;
            Fill(a, run);
            Picked();
            Log.Here.Info($"Stress Tests: imported {run.System.Pc}'s run of {run.Started:g} from {dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(window, F("That file couldn't be opened: {0}", ex.Message), T("Compare Stress Test Runs"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveReport()
    {
        if (Pair() is not var (a, b)) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = T("Web page") + " (*.html)|*.html", Title = T("Save Report"),
            FileName = string.Concat($"{a.System.Pc} vs {b.System.Pc} {DateTime.Now:yyyy-MM-dd}".Split(Path.GetInvalidFileNameChars())) + ".html",
        };
        if (dialog.ShowDialog(window) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, Report.Html(a, b));
            Ui.Open(dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(window, ex.Message, T("Save Report"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Render()
    {
        report.Children.Clear();
        if (Pair() is not var (a, b))
        {
            report.Children.Add(Ui.Secondary(T("Run the tests at least once (or import another PC's run) to compare."), wrap: true));
            return;
        }
        report.Children.Add(Ui.Card(Heads(a, b), new Thickness(0, 0, 0, 12)));
        foreach (var s in Report.Build(a, b))
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Header(s.Title));
            if (s.Note != null)
            {
                var note = Ui.Secondary(s.Note, wrap: true);
                note.Margin = new Thickness(0, -6, 0, 8);
                panel.Children.Add(note);
            }
            panel.Children.Add(Table(s));
            report.Children.Add(Ui.Card(panel, new Thickness(0, 0, 0, 12)));
        }
    }

    /// <summary>The two runs' scores, big, with the difference.</summary>
    private UIElement Heads(StressRun a, StressRun b)
    {
        var g = new Grid();
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Side(StressRun r, int col, string letter, string color)
        {
            var p = new StackPanel();
            var name = new TextBlock { Text = $"{letter}  {r.System.Pc}", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(report, color) };
            p.Children.Add(name);
            p.Children.Add(Ui.Secondary(r.Started.ToString("g") + (r.Imported ? " · " + T("imported") : "")));
            p.Children.Add(new TextBlock { Text = r.Score > 0 ? r.Score.ToString("#,0") : "—", FontSize = 34, FontWeight = FontWeights.Light });
            p.Children.Add(Ui.Secondary(r.System.Cpu));
            p.Children.Add(Ui.Secondary(r.System.Gpu?.ToString() ?? ""));
            Grid.SetColumn(p, col);
            g.Children.Add(p);
        }
        Side(a, 0, "A", "AccentBrush");
        var mid = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (a.Score > 0 && b.Score > 0)
        {
            double d = (b.Score / a.Score - 1) * 100;
            mid.Children.Add(new TextBlock { Text = Report.Percent(d), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, Foreground = DiffBrush(d) });
            mid.Children.Add(Ui.Secondary(Math.Abs(d) < 0.5 ? T("The same overall") : d > 0 ? T("B is faster overall") : T("B is slower overall")));
        }
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);
        Side(b, 2, "B", NetUi.Orange);
        return g;
    }

    private Brush DiffBrush(double d) => d > 0.5 ? Ui.Brush(report, "#7FD07A") : d < -0.5 ? Ui.Brush(report, "#FF7B72") : Ui.Brush(report, "TextBrush");

    private Grid Table(ReportSection s)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        int row = 0;
        void Add(UIElement e, int col)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(Ui.Secondary(""), 0);
        Add(Head("A", "AccentBrush"), 1);
        Add(Head("B", NetUi.Orange), 2);
        var against = Ui.Secondary(T("B against A"));
        against.HorizontalAlignment = HorizontalAlignment.Right;
        Add(against, 3);
        row++;
        foreach (var r in s.Rows)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = r.Detail ? Ui.Secondary(r.Label) : new TextBlock { Text = r.Label, FontWeight = r.Strong ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            label.Margin = new Thickness(r.Detail ? 16 : 0, r.Strong && row > 1 ? 10 : 3, 10, 3);
            Add(label, 0);
            Add(Cell(r.A, r.BarA, "AccentBrush", r), 1);
            Add(Cell(r.B, r.BarB, NetUi.Orange, r), 2);
            if (r.Better is double d)
            {
                var t = new TextBlock { Text = Report.Percent(d), Foreground = DiffBrush(d), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 3), FontWeight = r.Strong ? FontWeights.SemiBold : FontWeights.Normal };
                Add(t, 3);
            }
            row++;
        }
        return g;
    }

    private TextBlock Head(string text, string color) => new() { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush(report, color), Margin = new Thickness(0, 0, 0, 4) };

    private UIElement Cell(string text, double bar, string color, ReportRow r)
    {
        var p = new StackPanel { Margin = new Thickness(0, r.Strong && !r.Detail ? 10 : 3, 12, 3) };
        var t = NetUi.Selectable(text);
        if (r.Differs) t.Foreground = Ui.Brush(report, "#F2CC60");
        if (r.Strong) t.FontWeight = FontWeights.SemiBold;
        p.Children.Add(t);
        if (!double.IsNaN(bar))
        {
            var track = new Grid { Height = 6, Margin = new Thickness(0, 3, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(bar, 0.001, 1), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(bar, 0, 0.999), GridUnitType.Star) });
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = Ui.Brush(report, color) };
            track.Children.Add(fill);
            p.Children.Add(track);
        }
        return p;
    }
}
