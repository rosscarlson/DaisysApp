using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.SimEffects.Analysis;

/// <summary>
/// A block's rows, label and value, on the tab (every row) and in its widget (the rows not hidden). While the widget is
/// being edited every row shows, with a checkbox before its label: unticked, it's hidden once Done is pressed.
/// </summary>
internal sealed class BlockView : Grid
{
    private readonly Block block;
    private readonly ISet<string>? hidden;
    private readonly Action? changed;
    private readonly List<(BlockRow Row, CheckBox Box, TextBlock Label, TextBlock Value)> rows = new();
    private bool editing;

    /// <param name="hidden">The widget's hidden rows (null: the tab's, every row, no checkboxes).</param>
    /// <param name="changed">A row ticked or unticked.</param>
    public BlockView(Block block, ISet<string>? hidden = null, Action? changed = null, bool title = false)
    {
        this.block = block;
        this.hidden = hidden;
        this.changed = changed;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        if (title)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = Ui.Header(block.Title);
            t.Margin = new Thickness(0, 0, 0, 4);
            SetColumnSpan(t, 3);
            Children.Add(t);
            r++;
        }
        foreach (var row in block.Rows)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var box = new CheckBox { IsChecked = hidden?.Contains(row.Key) != true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed, ToolTip = T("Show in the widget") };
            AutomationProperties.SetName(box, F("Show {0}", row.Label));
            // (Checked / Unchecked, not Click: a toggle through UI Automation raises no Click)
            box.Checked += (_, _) => { if (hidden?.Remove(row.Key) == true) this.changed?.Invoke(); };
            box.Unchecked += (_, _) => { if (hidden?.Add(row.Key) == true) this.changed?.Invoke(); };
            var label = new TextBlock { Text = row.Label, Margin = new Thickness(0, 1, 12, 1), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var value = new TextBlock { Text = "—", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            SetRow(box, r); SetRow(label, r); SetRow(value, r);
            SetColumn(label, 1); SetColumn(value, 2);
            Children.Add(box); Children.Add(label); Children.Add(value);
            rows.Add((row, box, label, value));
            r++;
        }
        ShowRows();
    }

    public Block Block => block;

    /// <summary>Its widget is being edited: every row, with its checkbox.</summary>
    public bool Editing
    {
        get => editing;
        set { editing = value; ShowRows(); }
    }

    private void ShowRows()
    {
        foreach (var (row, box, label, value) in rows)
        {
            bool show = editing || hidden?.Contains(row.Key) != true;
            box.Visibility = editing && hidden != null ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = value.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            label.Opacity = value.Opacity = editing && hidden?.Contains(row.Key) == true ? 0.45 : 1;
        }
    }

    public void Update(Reading reading)
    {
        foreach (var (row, box, label, value) in rows)
        {
            if (value.Visibility != Visibility.Visible) continue;
            string text = row.Show(reading);
            if (value.Text != text) value.Text = text;
            label.Opacity = value.Opacity = editing && box.IsChecked != true ? 0.45 : 1;
        }
    }
}
