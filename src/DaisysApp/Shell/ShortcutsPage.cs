using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Shared;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Shell;

/// <summary>
/// Settings → Shortcuts: every shortcut the modules have (Core's <see cref="Hotkeys"/>), a card per module: click one
/// to change it, Reset puts back the one it came with, Delete unbinds it. Taken and shared ones say so under them.
/// Bindings without a key show with Show Unbound.
/// </summary>
internal sealed class ShortcutsPage : StackPanel
{
    private readonly StackPanel cards = new();
    private readonly CheckBox unbound = new() { Content = T("Show Unbound"), Margin = new Thickness(0, 0, 0, 12) };
    private string shown = "";
    private bool queued;

    public ShortcutsPage()
    {
        unbound.Click += (_, _) => Build(force: true);
        Children.Add(unbound);
        Children.Add(cards);
        Hotkeys.Changed += Later;
        Loaded += (_, _) => Build(force: false);
    }

    // (after a binding is set or added: not while a box on the page is still in its own Click)
    private void Later()
    {
        if (queued) return;
        queued = true;
        Dispatcher.BeginInvoke(() => { queued = false; Build(force: false); }, DispatcherPriority.Background);
    }

    private List<Hotkey> Listed() => Hotkeys.All.Where(h => unbound.IsChecked == true || h.Value != null).ToList();

    /// <summary>Builds the cards again when which bindings are listed changed (a value changing keeps them).</summary>
    private void Build(bool force)
    {
        var list = Listed();
        string ids = string.Join("\n", list.Select(h => h.Id));
        if (!force && ids == shown) return;
        shown = ids;
        cards.Children.Clear();
        if (list.Count == 0)
        {
            var none = new TextBlock { Text = unbound.IsChecked == true ? T("No shortcuts") : T("No shortcuts set") };
            none.SetResourceReference(StyleProperty, "SecondaryText");
            cards.Children.Add(none);
            return;
        }
        foreach (var module in list.GroupBy(h => h.Spec.Module).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var card = new Border();
            card.SetResourceReference(StyleProperty, "Card");
            card.Margin = new Thickness(0, 0, 0, 12);
            var body = new StackPanel();
            var header = new TextBlock { Text = module.Key };
            header.SetResourceReference(StyleProperty, "CardHeader");
            body.Children.Add(header);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var h in module) Row(grid, h);
            body.Children.Add(grid);
            card.Child = body;
            cards.Children.Add(card);
        }
    }

    private static void Row(Grid grid, Hotkey h)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 8), TextWrapping = TextWrapping.Wrap };
        if (h.Spec.Target is { } target) name.Inlines.Add(new System.Windows.Documents.Run(target + " · ") { FontWeight = FontWeights.SemiBold });
        name.Inlines.Add(new System.Windows.Documents.Run(h.Spec.Name));
        if (h.Spec.Where is { } where)
        {
            var w = new System.Windows.Documents.Run("  " + where);
            w.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondaryBrush");
            name.Inlines.Add(w);
        }
        Grid.SetRow(name, row);
        grid.Children.Add(name);

        var box = new ShortcutBox(h) { ShowClear = false, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 8) };
        var reset = new Button { Content = T("Reset"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = h.Spec.Default };
        reset.Click += (_, _) => h.Bind(h.Spec.Default);
        var delete = ConfirmButton.Make(T("Delete"), () => h.Bind(null));
        delete.Padding = new Thickness(8, 3, 8, 3);
        void Show()
        {
            reset.Visibility = h.Spec.Default != null && !string.Equals(h.Value, h.Spec.Default, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Hidden;
            delete.IsEnabled = h.Value != null;
        }
        Show();
        h.Changed += Show;
        box.Unloaded += (_, _) => h.Changed -= Show;
        box.Loaded += (_, _) => { h.Changed -= Show; h.Changed += Show; Show(); };
        buttons.Children.Add(reset);
        buttons.Children.Add(delete);
        Grid.SetRow(buttons, row);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
    }
}
