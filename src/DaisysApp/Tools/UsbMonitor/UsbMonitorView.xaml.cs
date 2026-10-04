using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaisysApp.Tools.UsbMonitor.Models;
using DaisysApp.Tools.UsbMonitor.ViewModels;

namespace DaisysApp.Tools.UsbMonitor;

public partial class UsbMonitorView : UserControl
{
    /// <summary>Bump when the column order changes, so saved widths from the old order are ignored.</summary>
    private const int ColumnLayoutVersion = 2;

    private readonly ObservableCollection<DeviceRecordRow> _rows = new();
    private int _eventCount;
    private bool _wasShown;
    private bool _sortApplied;

    public UsbMonitorView(UsbMonitorSettings settings)
    {
        InitializeComponent();

        EventGrid.ItemsSource = _rows;

        if (settings.ColumnLayout == ColumnLayoutVersion && settings.ColumnWidths.Count == EventGrid.Columns.Count)
        {
            for (int i = 0; i < EventGrid.Columns.Count; i++)
            {
                EventGrid.Columns[i].Width = new DataGridLength(settings.ColumnWidths[i]);
            }
        }

        Loaded += (_, _) => ApplyInitialSort();
        IsVisibleChanged += (_, e) =>
        {
            // Column widths are only meaningful once the tab has actually been shown and laid
            // out — a tray-only session (or one that never opens this tab) must not overwrite
            // good saved values with unmeasured defaults (e.g. DataGrid column ActualWidth
            // before a layout pass reads as a few px, not its real XAML width).
            if (e.NewValue is true) _wasShown = true;
        };
    }

    /// <summary>Every launch starts newest-first (Timestamp descending), so new events appear at the top.
    /// Clicking a column header still re-sorts for the rest of the session.</summary>
    private void ApplyInitialSort()
    {
        if (_sortApplied || EventGrid.Columns.Count == 0) return;
        _sortApplied = true;
        var column = EventGrid.Columns[0]; // Timestamp ("yyyy-MM-dd HH:mm:ss.fff" sorts chronologically as text)

        var view = CollectionViewSource.GetDefaultView(_rows);
        view.SortDescriptions.Clear();
        if (column is DataGridBoundColumn bound && bound.Binding is Binding b)
        {
            view.SortDescriptions.Add(new SortDescription(b.Path.Path, ListSortDirection.Descending));
            column.SortDirection = ListSortDirection.Descending;
        }
    }

    /// <summary>Shows where events are being logged, that logging is off, or why it couldn't start.</summary>
    public void SetLogStatus(string? logFilePath, string? error)
    {
        LogPathText.Text = error ?? (logFilePath != null ? $"Logging to: {logFilePath}" : "Not logging to a file (see Settings → USB Monitor).");
        LogPathText.ToolTip = logFilePath;
        if (error != null) LogPathText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else LogPathText.ClearValue(TextBlock.ForegroundProperty);
    }

    public void AddRecord(DeviceRecord record)
    {
        _eventCount++;
        CountText.Text = $"{_eventCount} event{(_eventCount == 1 ? "" : "s")}";
        _rows.Insert(0, new DeviceRecordRow(record));
    }

    private void EventGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Only a double-click on a row opens details (not on a header, the scrollbar or empty space).
        if (ItemsControl.ContainerFromElement(EventGrid, (DependencyObject)e.OriginalSource) is not DataGridRow { Item: DeviceRecordRow row })
            return;
        e.Handled = true;
        var details = new DeviceDetailsWindow(row.Record) { Owner = Window.GetWindow(this) };
        details.ShowDialog();
    }

    /// <summary>Copies the column widths into the settings (the tool saves them).</summary>
    public void StoreLayout(UsbMonitorSettings settings)
    {
        // A session that never showed this tab has nothing real to report for
        // columns — never clobber good saved values with unmeasured defaults.
        if (!_wasShown) return;

        settings.ColumnWidths = EventGrid.Columns.Select(c => (int)c.ActualWidth).ToList();
        settings.ColumnLayout = ColumnLayoutVersion;
    }
}
