using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Picks an icon from the library: a search box (names, and words such as "car", "arrow", "music") over every icon,
/// drawn in the ButtScreen's icon colour on the button's background, so you see how it will look. Double-click one (or
/// select it and OK). <see cref="Result"/> is the icon's name, or null for none.
/// </summary>
internal sealed class IconPickerWindow : Window
{
    private const int PerRow = 10, Tile = 52;

    private readonly TextBox search = new() { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ListBox rows = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock found = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock chosen = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0) };
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Brush iconBrush, tileBrush;
    private Button? selected;

    public string? Result { get; private set; }

    public IconPickerWindow(string? current, string iconColor, string background)
    {
        Title = T("Pick an icon");
        Width = PerRow * (Tile + 4) + 70;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        iconBrush = ColorText.Brush(iconColor, Colors.White);
        tileBrush = ColorText.Brush(background, Color.FromRgb(0x2D, 0x35, 0x40));
        Result = current;

        var dock = new DockPanel { Margin = new Thickness(16) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = T("Search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        top.Children.Add(label);
        AutomationProperties.SetName(search, T("Search the icons"));
        search.ToolTip = T("Names and words, e.g. car, arrow, music, volume, mic");
        top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = T("OK"), MinWidth = 90, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) => DialogResult = true;
        var none = new Button { Content = T("No icon"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        none.Click += (_, _) => { Result = null; DialogResult = true; };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(ok);
        right.Children.Add(none);
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        found.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(found);
        left.Children.Add(chosen);
        buttons.Children.Add(left);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);

        var credit = new TextBlock { Text = T("Icons: Material Design Icons, by Pictogrammers (Apache 2.0)."), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
        credit.SetResourceReference(StyleProperty, "SecondaryText");
        DockPanel.SetDock(credit, Dock.Bottom);
        dock.Children.Add(credit);

        // rows of icons, made only as they scroll into view
        VirtualizingPanel.SetIsVirtualizing(rows, true);
        VirtualizingPanel.SetVirtualizationMode(rows, VirtualizationMode.Standard);
        ScrollViewer.SetCanContentScroll(rows, true);
        rows.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }),
                new Setter(FocusableProperty, false),
            },
        };
        rows.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(IconRow)) };
        dock.Children.Add(rows);
        Content = dock;

        typing.Tick += (_, _) => { typing.Stop(); Fill(); };
        search.TextChanged += (_, _) => { typing.Stop(); typing.Start(); };
        Loaded += (_, _) =>
        {
            Fill();
            search.Focus();
        };
        ShowChosen();
    }

    private void Fill()
    {
        var list = IconLibrary.Search(search.Text);
        found.Text = P(list.Count, "{0} icon", "{0} icons");
        var chunks = new List<IconChunk>();
        for (int i = 0; i < list.Count; i += PerRow) chunks.Add(new IconChunk(list.Skip(i).Take(PerRow).ToList(), this));
        rows.ItemsSource = chunks;
    }

    private void ShowChosen() => chosen.Text = Result ?? "";

    private void Choose(Button b, string name, bool done)
    {
        if (selected != null) selected.BorderBrush = Brushes.Transparent;
        selected = b;
        b.SetResourceReference(BorderBrushProperty, "AccentBrush");
        Result = name;
        ShowChosen();
        if (done) DialogResult = true;
    }

    private sealed record IconChunk(List<IconLibrary.Icon> Icons, IconPickerWindow Owner);

    /// <summary>One row of icons (its DataContext an <see cref="IconChunk"/>).</summary>
    private sealed class IconRow : StackPanel
    {
        public IconRow()
        {
            Orientation = Orientation.Horizontal;
            DataContextChanged += (_, _) => Build();
        }

        private void Build()
        {
            Children.Clear();
            if (DataContext is not IconChunk chunk) return;
            var w = chunk.Owner;
            foreach (var icon in chunk.Icons)
            {
                var path = new System.Windows.Shapes.Path { Data = IconLibrary.GeometryOf(icon.Name), Fill = w.iconBrush, Width = 24, Height = 24, Stretch = Stretch.None };
                var b = new Button
                {
                    Width = Tile, Height = Tile, Margin = new Thickness(2), Padding = new Thickness(0),
                    Content = new Viewbox { Width = 30, Height = 30, Child = path },
                    Background = w.tileBrush, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
                    ToolTip = icon.Name, Template = TileTemplate,
                };
                AutomationProperties.SetName(b, icon.Name);
                string name = icon.Name;
                if (name == w.Result) { w.selected = b; b.SetResourceReference(BorderBrushProperty, "AccentBrush"); }
                b.Click += (_, _) => w.Choose(b, name, false);
                b.MouseDoubleClick += (_, e) => { w.Choose(b, name, true); e.Handled = true; };
                Children.Add(b);
            }
        }
    }

    // a plain tile: its background and border, nothing of the theme's button look
    private static readonly ControlTemplate TileTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
        </ControlTemplate>
        """);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Down && search.IsKeyboardFocused) { rows.Focus(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
