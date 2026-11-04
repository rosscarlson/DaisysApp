using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaisysApp.Logging;
using Microsoft.Win32;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// The Process Analyzer sub-tab: every running program (its processes together) in a table, the suggestions first,
/// with what's known about it and what was found; its details on the right (<see cref="ProcessDetails"/>). Looks again
/// every 2 seconds while it's showing. Export… and Import… write and merge the database as a file.
/// </summary>
internal sealed class ProcessAnalyzerView : Grid
{
    private readonly ProcessAnalyzerApplet applet;
    private readonly ProcessAnalyzerSettings settings;
    private readonly Analyzer analyzer;
    private readonly ObservableCollection<ProgramRow> rows = new();
    private readonly Dictionary<string, ProgramRow> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListCollectionView view;
    private readonly DataGrid table = Ui.Table();
    private readonly TextBlock status = Ui.Secondary();
    private readonly TextBlock server = Ui.Secondary();
    private readonly TextBox search = new() { Width = 180, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProcessDetails details;
    private List<Assessment> last = new();
    private volatile bool busy;
    private string note = "";

    public ProcessAnalyzerView(ProcessAnalyzerApplet applet)
    {
        this.applet = applet;
        settings = applet.Settings;
        analyzer = new Analyzer(settings);
        details = new ProcessDetails(applet, () => last, Refresh);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        // the bar: where it's at, the server, search, filter, Export… and Import…
        search.ToolTip = T("Search by name, title, publisher or category");
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search"));
        search.TextChanged += (_, _) => view!.Refresh();
        var filter = Ui.Choice(new[] { (T("Everything"), 0), (T("Suggestions"), 1), (T("Not reviewed"), 2), (T("Risky"), 3) }, settings.Filter, f =>
        {
            settings.Filter = f;
            settings.SaveSoon();
            view!.Refresh();
        }, 130);
        filter.ToolTip = T("Which programs to list");
        var export = Ui.IconButton("", T("Export…"), T("Save the process database as a file, to merge on another PC"), Export);
        var import = Ui.IconButton("", T("Import…"), T("Merge a database file into this one"), Import);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(status);
        server.Margin = new Thickness(16, 0, 0, 0);
        left.Children.Add(server);
        var bar = Ui.HeaderRow(left, import, export, filter, search);
        Children.Add(bar);

        // the table
        view = (ListCollectionView)CollectionViewSource.GetDefaultView(rows);
        view.Filter = o => Show((ProgramRow)o);
        view.SortDescriptions.Add(new SortDescription(nameof(ProgramRow.Score), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(ProgramRow.Cpu), ListSortDirection.Descending));
        view.IsLiveSorting = true;
        view.IsLiveFiltering = true;
        foreach (var p in new[] { nameof(ProgramRow.Score), nameof(ProgramRow.Cpu), nameof(ProgramRow.MemoryMB), nameof(ProgramRow.Threat), nameof(ProgramRow.Count) })
            view.LiveSortingProperties.Add(p);
        foreach (var p in new[] { nameof(ProgramRow.Suggestion), nameof(ProgramRow.Threat), nameof(ProgramRow.Source) }) view.LiveFilteringProperties.Add(p);
        table.ItemsSource = view;
        table.Columns.Add(ThreatColumn());
        table.Columns.Add(Ui.Column(T("Program"), nameof(ProgramRow.Name), 140));
        table.Columns.Add(Ui.Column(T("What it is"), nameof(ProgramRow.Title), -1.3));
        table.Columns.Add(Ui.Column(T("Publisher"), nameof(ProgramRow.Publisher), -1));
        table.Columns.Add(Ui.Column(T("Category"), nameof(ProgramRow.Category), 95));
        table.Columns.Add(Ui.Column(T("Needed"), nameof(ProgramRow.Necessity), 100));
        table.Columns.Add(Ui.Column(T("CPU %"), nameof(ProgramRow.CpuText), 55, nameof(ProgramRow.Cpu), right: true));
        table.Columns.Add(Ui.Column(T("Memory"), nameof(ProgramRow.MemoryText), 75, nameof(ProgramRow.MemoryMB), right: true));
        table.Columns.Add(Ui.Column("#", nameof(ProgramRow.Count), 34, right: true));
        table.Columns.Add(Ui.Column("", nameof(ProgramRow.Source), 30));
        table.Columns.Add(Ui.Column(T("Suggestion"), nameof(ProgramRow.Suggestion), -2, nameof(ProgramRow.Score)));
        table.Columns[^2].Header = new TextBlock { Text = "✓", ToolTip = T("Reviewed (by a person, or the list it comes with), or classified automatically") };
        table.SelectionChanged += (_, _) => details.Show(table.SelectedItem as ProgramRow);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460), MinWidth = 280 });
        body.Children.Add(table);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent };
        SetColumn(splitter, 1);
        body.Children.Add(splitter);
        SetColumn(details, 2);
        body.Children.Add(details);
        SetRow(body, 1);
        Children.Add(body);

        status.Text = T("Looking…");
        ShowServer();
        applet.ServerChanged += () => Dispatcher.BeginInvoke(ShowServer);
        Ui.Every(this, 2000, Tick, now: true, shown: s => applet.ViewShowing = s);
    }

    private static DataGridTemplateColumn ThreatColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProgramRow.ThreatText)) { Mode = BindingMode.OneWay });
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ProgramRow.ThreatBrush)) { Mode = BindingMode.OneWay });
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataGridTemplateColumn
        {
            Header = T("Threat"), Width = new DataGridLength(80), SortMemberPath = nameof(ProgramRow.Threat),
            CellTemplate = new DataTemplate { VisualTree = text },
        };
    }

    private bool Show(ProgramRow r)
    {
        string q = search.Text.Trim();
        if (q.Length > 0 && !(r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase))) return false;
        return settings.Filter switch
        {
            1 => r.Suggestion.Length > 0,
            2 => !r.Reviewed,
            3 => r.Threat >= Threat.Medium,
            _ => true,
        };
    }

    private void Tick()
    {
        if (busy) return;
        busy = true;
        Task.Run(() =>
        {
            try
            {
                var found = analyzer.Run(observe: true);
                analyzer.LookUpUnknown(found);
                Dispatcher.BeginInvoke(() => Apply(found));
            }
            catch (Exception ex) { Log.Here.Warn($"Process Analyzer: {ex.Message}"); }
            finally { busy = false; }
        });
    }

    /// <summary>Looks again now (after an edit, a lookup or an import).</summary>
    private void Refresh() => Tick();

    private void Apply(List<Assessment> found)
    {
        last = found;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in found)
        {
            names.Add(a.Name);
            if (!byName.TryGetValue(a.Name, out var row)) { byName[a.Name] = row = new ProgramRow(a.Name); rows.Add(row); }
            row.Update(a);
        }
        foreach (var gone in byName.Keys.Where(n => !names.Contains(n)).ToList())
        {
            rows.Remove(byName[gone]);
            byName.Remove(gone);
        }
        int suggestions = found.Count(a => a.Suggestion.Length > 0), unknown = found.Count(a => !a.Entry.Reviewed);
        status.Text = F("{0} programs, {1} processes · {2} suggestions · {3} not reviewed · {4} known", found.Count, found.Sum(a => a.Procs.Count),
            suggestions, unknown, ProcessDb.Db.Count) + (note.Length > 0 ? " · " + note : "");
        if (table.SelectedItem == null && view.Count > 0 && details.Showing == null) table.SelectedIndex = 0;
        details.Update();
    }

    private void ShowServer()
    {
        server.Text = !settings.UseServer ? T("Local only") : applet.ServerStatus;
        server.ToolTip = settings.UseServer ? F("Database version {0} · {1} changes to send", ProcessDb.Db.Version, ProcessDb.Db.DirtyCount) : null;
    }

    // ---------------------------------------------------------------- export and import

    private void Export()
    {
        var d = new SaveFileDialog
        {
            Title = T("Export the process database"), Filter = T("Process database") + " (*.json)|*.json",
            FileName = $"processes-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(d.FileName, ProcessDb.Db.Export());
            note = F("Exported to {0}", Path.GetFileName(d.FileName));
            Log.Here.Info($"Process Analyzer: exported to {d.FileName}");
        }
        catch (Exception ex) { note = F("Not exported: {0}", ex.Message); }
        Refresh();
    }

    private void Import()
    {
        var d = new OpenFileDialog { Title = T("Merge a process database"), Filter = T("Process database") + " (*.json)|*.json|*.*|*.*" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var list = ProcessDb.ReadExport(File.ReadAllText(d.FileName));
            var (added, replaced) = ProcessDb.Db.Merge(list, "import", fromServer: false);
            note = F("Merged {0}: {1} new, {2} changed, {3} kept", Path.GetFileName(d.FileName), added, replaced, list.Count - added - replaced);
            Log.Here.Info($"Process Analyzer: imported {d.FileName}: {added} new, {replaced} changed of {list.Count}");
        }
        catch (Exception ex) { note = F("Not imported: {0}", ex.Message); }
        Refresh();
    }
}
