using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysApp.Applets.ClaudeCode;

/// <summary>
/// Settings → Claude Code: permission prompts (never ask), and how the conversation looks: its colours (each can follow
/// the theme), the text and code fonts and sizes, line spacing, the tool cards' size, how wide text goes, and the box
/// you write in (how many lines tall, its text size and colours), with a preview that follows as they change. The open
/// session tabs follow at once.
/// </summary>
internal sealed class ClaudeCodeSettingsView : UserControl
{
    private readonly ClaudeCodeSettings settings;
    private readonly DispatcherTimer apply = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Border preview = new() { Padding = new Thickness(14), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel previewChat = new();
    private readonly TextBox previewInput = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = "Write here…", Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 10, 0, 0), IsReadOnly = true };
    private readonly List<Action> refreshers = new();

    private Appearance A => settings.Look;

    public ClaudeCodeSettingsView(ClaudeCodeSettings settings)
    {
        this.settings = settings;
        apply.Tick += (_, _) => { apply.Stop(); Look.Set(A); };

        var root = new StackPanel();
        root.Children.Add(Card(Permissions(), T("Permissions")));
        root.Children.Add(Card(Appearance(), T("How the conversation looks")));
        Content = root;
        Look.Changed += ShowPreview;
        BuildPreview();
    }

    // ---------------------------------------------------------------- permissions

    private UIElement Permissions()
    {
        var s = new StackPanel();
        var box = new CheckBox
        {
            Content = new TextBlock { Text = T("Never ask before Claude uses a tool (dangerously skip permissions)"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold },
            IsChecked = settings.SkipPermissions,
        };
        box.Click += (_, _) =>
        {
            bool on = box.IsChecked == true;
            if (on && MessageBox.Show(Window.GetWindow(this),
                    T("Claude will edit and delete files, run any command and use the web without asking, in every session (running ones too). Only do this if you trust what you ask it to do. Turn it on?"),
                    T("Claude Code"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                box.IsChecked = false;
                return;
            }
            settings.SkipPermissions = on;
            settings.Save();
            settings.NotifyChanged();
        };
        s.Children.Add(box);
        s.Children.Add(Secondary(T("Claude Code's --dangerously-skip-permissions: every session runs in its bypass mode and anything that would still ask is allowed. It takes effect at once, in running sessions too; each session's dashboard says so in red. Off, each session's own Permissions setting decides.")));
        return s;
    }

    // ---------------------------------------------------------------- the look

    private UIElement Appearance()
    {
        var s = new StackPanel();
        s.Children.Add(Secondary(T("A colour left on Theme follows the app's light or dark theme. Changes show at once, in the preview and in the open tabs.")));

        s.Children.Add(Section(T("Colours")));
        var colours = new Grid();
        colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        colours.ColumnDefinitions.Add(new ColumnDefinition());
        void Colour(string label, Func<string> get, Action<string> set)
        {
            int row = colours.RowDefinitions.Count;
            colours.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(l, row);
            colours.Children.Add(l);
            var picker = new ColourPicker(get(), v => { set(v); Changed(); });
            refreshers.Add(() => picker.Value = get());
            Grid.SetRow(picker, row);
            Grid.SetColumn(picker, 1);
            colours.Children.Add(picker);
        }
        Colour(T("Background"), () => A.ChatBackground, v => A.ChatBackground = v);
        Colour(T("Claude's text"), () => A.TextColor, v => A.TextColor = v);
        Colour(T("Your messages"), () => A.BubbleBackground, v => A.BubbleBackground = v);
        Colour(T("Your messages' text"), () => A.BubbleText, v => A.BubbleText = v);
        Colour(T("Code blocks"), () => A.CodeBackground, v => A.CodeBackground = v);
        Colour(T("Code text"), () => A.CodeText, v => A.CodeText = v);
        Colour(T("The box you write in"), () => A.InputBackground, v => A.InputBackground = v);
        Colour(T("Its text"), () => A.InputText, v => A.InputText = v);
        s.Children.Add(colours);

        s.Children.Add(Section(T("Text")));
        s.Children.Add(FontRow(T("Font"), () => A.Font, v => A.Font = v));
        s.Children.Add(SliderRow(T("Size"), 10, 28, 0.5, () => A.FontSize, v => A.FontSize = v, "{0:0.#} pt"));
        s.Children.Add(SliderRow(T("Line spacing"), 1, 2.5, 0.05, () => A.LineSpacing, v => A.LineSpacing = v, "{0:0.00}×"));
        s.Children.Add(SliderRow(T("Widest it goes"), 0, 1800, 50, () => A.MaxTextWidth, v => A.MaxTextWidth = (int)v, null,
            v => v <= 0 ? T("the whole width") : F("{0:0} px", v)));
        s.Children.Add(FontRow(T("Code font"), () => A.CodeFont, v => A.CodeFont = v));
        s.Children.Add(SliderRow(T("Code size"), 9, 26, 0.5, () => A.CodeFontSize, v => A.CodeFontSize = v, "{0:0.#} pt"));
        s.Children.Add(SliderRow(T("Tool cards and notes"), 10, 24, 0.5, () => A.CardFontSize, v => A.CardFontSize = v, "{0:0.#} pt"));

        s.Children.Add(Section(T("The box you write in")));
        s.Children.Add(SliderRow(T("Lines tall"), 1, 20, 1, () => A.InputRows, v => A.InputRows = (int)v, null,
            v => P((long)v, "{0} line (grows to twice that)", "{0} lines (grows to twice that)")));
        s.Children.Add(SliderRow(T("Its text size"), 10, 28, 0.5, () => A.InputFontSize, v => A.InputFontSize = v, "{0:0.#} pt"));

        s.Children.Add(Section(T("Preview")));
        preview.Child = new StackPanel { Children = { previewChat, previewInput } };
        s.Children.Add(preview);

        var reset = new Button { Content = T("Back to the defaults"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            settings.Look = new Appearance();
            foreach (var r in refreshers) r();
            Changed();
        };
        s.Children.Add(reset);
        return s;
    }

    /// <summary>Saved, and shown (a moment later, so a slider being dragged doesn't redraw every step).</summary>
    private void Changed()
    {
        settings.Save();
        apply.Stop();
        apply.Start();
    }

    private void BuildPreview()
    {
        previewChat.Children.Add(new UserBubble("Can you tidy up the README and add a section on building it?"));
        var a = new AssistantText();
        a.Finish("I'll **restructure** the README into sections and add *Building*:\n\n- What it is\n- How to build it: `.\\build.ps1`\n- Where the installers go\n\n```powershell\n.\\build.ps1 -Version 1.7.0\n```");
        previewChat.Children.Add(a);
        var card = new ToolCard("preview", "Edit") { Root = "" };
        using (var doc = System.Text.Json.JsonDocument.Parse("{\"file_path\":\"README.md\",\"old_string\":\"## Build\",\"new_string\":\"## Building\\nRun build.ps1.\"}"))
            card.SetInput(doc.RootElement);
        card.SetResult("The file README.md has been updated.", false, timed: false);
        previewChat.Children.Add(card);
        previewChat.Children.Add(new NoteLine("Done in 12.4 s · 3 steps · 412 tokens out", icon: "✔"));
        ShowPreview();
    }

    private void ShowPreview()
    {
        Look.Paint(preview, Border.BackgroundProperty, A.ChatBackground, "RoomBrush");
        foreach (var item in previewChat.Children.OfType<ILooked>()) item.ApplyLook();
        previewInput.FontFamily = Look.Font;
        previewInput.FontSize = Math.Clamp(A.InputFontSize, 8, 40);
        previewInput.Height = Math.Clamp(A.InputRows, 1, 40) * previewInput.FontSize * 1.35 + 20;
        if (Look.Brush(A.InputBackground) is { } ib) previewInput.Background = ib; else previewInput.ClearValue(BackgroundProperty);
        if (Look.Brush(A.InputText) is { } it) previewInput.Foreground = it; else previewInput.ClearValue(ForegroundProperty);
    }

    // ---------------------------------------------------------------- rows

    private FrameworkElement FontRow(string label, Func<string> get, Action<string> set)
    {
        var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = f, FontFamily = new FontFamily(f), Tag = f });
        void Select() => combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, get(), StringComparison.OrdinalIgnoreCase));
        Select();
        refreshers.Add(Select);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string f } && f != get()) { set(f); Changed(); } };
        return Row(label, combo);
    }

    private FrameworkElement SliderRow(string label, double min, double max, double step, Func<double> get, Action<double> set, string? format, Func<double, string>? text = null)
    {
        var value = new TextBlock { Width = 210, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center, Value = get() };
        void Show() => value.Text = text?.Invoke(slider.Value) ?? string.Format(format!, slider.Value);
        Show();
        bool quiet = false;
        slider.ValueChanged += (_, _) => { Show(); if (!quiet) { set(slider.Value); Changed(); } };
        refreshers.Add(() => { quiet = true; slider.Value = get(); quiet = false; Show(); });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(slider);
        line.Children.Add(value);
        return Row(label, line);
    }

    private static FrameworkElement Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static Border Card(UIElement child, string title)
    {
        var s = new StackPanel();
        var h = new TextBlock { Text = title };
        h.SetResourceReference(StyleProperty, "CardHeader");
        s.Children.Add(h);
        s.Children.Add(child);
        var b = new Border { Child = s };
        b.SetResourceReference(StyleProperty, "Card");
        return b;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static TextBlock Section(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 16, 0, 6) };
}

/// <summary>
/// A colour: its swatch (or "Theme"), which opens a palette of colours and a box for any "#RRGGBB", and Theme to
/// follow the app's theme again.
/// </summary>
internal sealed class ColourPicker : StackPanel
{
    private readonly Action<string> changed;
    private readonly Border swatch = new() { Width = 46, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly TextBlock name = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), MinWidth = 70 };
    private readonly Popup popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly TextBox hex = new() { Width = 110, Margin = new Thickness(0, 8, 8, 0) };
    private string value;

    private static readonly string[] Swatches =
    {
        "#000000", "#141414", "#1E1E1E", "#252526", "#2D2D30", "#3C3C3C", "#5A5A5A", "#808080",
        "#A0A0A0", "#C8C8C8", "#E6E6E6", "#F3F3F3", "#FAFAFA", "#FFFFFF", "#FFF8E7", "#F5F0E1",
        "#0D1117", "#161B22", "#1F2937", "#1E293B", "#0F172A", "#172554", "#1E3A5F", "#003049",
        "#4CC2FF", "#58A6FF", "#79C0FF", "#A5D6FF", "#3FB950", "#7EE787", "#56D364", "#2EA043",
        "#E3B341", "#F2CC60", "#FFA657", "#F0883E", "#F85149", "#FF7B72", "#FFA198", "#D2A8FF",
        "#BC8CFF", "#A371F7", "#F778BA", "#FF9BCE", "#39C5CF", "#76E3EA", "#2D333B", "#22272E",
    };

    public ColourPicker(string initial, Action<string> changed)
    {
        Orientation = Orientation.Horizontal;
        this.changed = changed;
        value = initial;
        swatch.SetResourceReference(Border.BorderBrushProperty, "ControlBorderStrongBrush");
        var open = new Button { Content = swatch, Padding = new Thickness(3), ToolTip = T("Pick a colour") };
        open.Click += (_, _) => { hex.Text = value; popup.IsOpen = true; };
        Children.Add(open);
        Children.Add(name);
        var theme = new Button { Content = T("Theme"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Follow the app's light or dark theme") };
        theme.Click += (_, _) => Pick("");
        Children.Add(theme);

        var grid = new UniformGrid { Columns = 8 };
        foreach (var c in Swatches)
        {
            var b = new Button { Width = 26, Height = 22, Margin = new Thickness(2), Padding = new Thickness(0), ToolTip = c, Background = Look.Brush(c), BorderThickness = new Thickness(1) };
            b.Content = new Border { Background = Look.Brush(c), Width = 20, Height = 16, CornerRadius = new CornerRadius(2) };
            string colour = c;
            b.Click += (_, _) => { Pick(colour); popup.IsOpen = false; };
            grid.Children.Add(b);
        }
        var use = new Button { Content = T("Use"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 8, 0, 0) };
        void UseHex()
        {
            string h = hex.Text.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            if (Look.Brush(h) != null) { Pick(h.ToUpperInvariant()); popup.IsOpen = false; }
        }
        use.Click += (_, _) => UseHex();
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) UseHex(); };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal };
        hexRow.Children.Add(hex);
        hexRow.Children.Add(use);
        var panel = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = new StackPanel { Children = { grid, hexRow } } };
        panel.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        popup.Child = panel;
        popup.PlacementTarget = open;
        Show();
    }

    public string Value
    {
        get => value;
        set { this.value = value; Show(); }
    }

    private void Pick(string v)
    {
        value = v;
        Show();
        changed(v);
    }

    private void Show()
    {
        if (Look.Brush(value) is { } b) { swatch.Background = b; name.Text = value; }
        else
        {
            swatch.Background = new LinearGradientBrush(Colors.White, Color.FromRgb(0x20, 0x20, 0x20), 45);
            name.Text = T("Theme");
        }
    }
}
