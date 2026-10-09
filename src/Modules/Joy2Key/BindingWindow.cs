using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;
using Microsoft.Win32;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>What one input does: keys (held, tapped, repeated or toggled, and other keys for a long press), mouse
/// movement, a program to run, or a profile to switch to. <see cref="Result"/> is null for "nothing".</summary>
internal sealed class BindingWindow : Window
{
    private readonly J2KAction a;
    private readonly ComboBox kind = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel keysPanel = new(), macroPanel = new(), mousePanel = new(), runPanel = new(), profilePanel = new();
    private readonly StackPanel stepList = new();
    private readonly ScrollViewer stepScroll = new() { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private List<MacroStep> steps = new();
    private int macroHold;
    private KeyPicker? firstPicker;
    private readonly CheckBox sameGap = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox mode = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly FrameworkElement pressRow, repeatRow, repeatDelayRow, longRow;
    private readonly TextBlock modeHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
    private readonly CheckBox longPress = new() { VerticalAlignment = VerticalAlignment.Center };

    public J2KAction? Result { get; private set; }

    public BindingWindow(string title, J2KAction? action, IEnumerable<string> profileNames)
    {
        a = action?.Clone() ?? new J2KAction();
        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis });

        foreach (var s in new[] { T("Press keys"), T("Play a macro (keys in order)"), T("Move the mouse"), T("Run a program"), T("Switch profile"), T("Nothing") }) kind.Items.Add(s);
        kind.SelectedIndex = action == null || action.IsEmpty && action.Kind == ActionKind.Keys ? 0 : Array.IndexOf(Kinds, a.Kind);
        panel.Children.Add(Row(T("What it does"), kind));

        // keys
        var keys = new KeyPicker(a.Keys);
        keys.Changed += () => a.Keys = keys.Keys;
        keysPanel.Children.Add(Row(T("Keys"), keys, stretch: true));
        foreach (var s in new[] { T("Hold them while it's held"), T("Tap them once"), T("Repeat while it's held"), T("Toggle: press on, press off") }) mode.Items.Add(s);
        mode.SelectedIndex = (int)a.Mode;
        keysPanel.Children.Add(Row(T("How"), mode));
        modeHint.SetResourceReference(StyleProperty, "SecondaryText");
        keysPanel.Children.Add(modeHint);
        pressRow = Row(T("Each press lasts"), Number(a.PressMs, v => a.PressMs = v, T("ms"), 1, 10000));
        repeatRow = Row(T("Press again every"), Number(a.RepeatMs, v => a.RepeatMs = v, T("ms"), 10, 60000));
        repeatDelayRow = Row(T("First repeat after"), Number(a.RepeatDelayMs, v => a.RepeatDelayMs = v, T("ms (0 = the same)"), 0, 60000));
        keysPanel.Children.Add(pressRow);
        keysPanel.Children.Add(repeatRow);
        keysPanel.Children.Add(repeatDelayRow);

        var longHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        longPress.IsChecked = a.LongMs > 0;
        longHeader.Children.Add(longPress);
        longHeader.Children.Add(new TextBlock { Text = T("Long press: held for"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        longHeader.Children.Add(Number(a.LongMs > 0 ? a.LongMs : 500, v => { if (longPress.IsChecked == true) a.LongMs = v; }, T("ms, press these instead:"), 50, 60000));
        keysPanel.Children.Add(longHeader);
        var longKeys = new KeyPicker(a.LongKeys);
        longKeys.Changed += () => a.LongKeys = longKeys.Keys;
        longRow = Row("", longKeys, stretch: true);
        keysPanel.Children.Add(longRow);
        var longHint = new TextBlock { Text = T("With a long press, a short press taps the keys above, and a long one holds these until it's let go."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        longHint.SetResourceReference(StyleProperty, "SecondaryText");
        keysPanel.Children.Add(longHint);
        longPress.Checked += (_, _) => { if (a.LongMs <= 0) a.LongMs = 500; Update(); };
        longPress.Unchecked += (_, _) => { a.LongMs = 0; Update(); };
        mode.SelectionChanged += (_, _) => { a.Mode = (PressMode)Math.Max(mode.SelectedIndex, 0); Update(); };
        panel.Children.Add(keysPanel);

        // macro
        BuildMacro();
        panel.Children.Add(macroPanel);

        // mouse
        mousePanel.Children.Add(Row(T("Left / right"), Number(a.MouseX, v => a.MouseX = v, T("pixels a second (minus = left)"), -20000, 20000)));
        mousePanel.Children.Add(Row(T("Up / down"), Number(a.MouseY, v => a.MouseY = v, T("pixels a second (minus = up)"), -20000, 20000)));
        var mouseHint = new TextBlock { Text = T("On an axis, the speed follows how far it's pushed. For clicks and the wheel, choose Press keys and right-click the keys box."), TextWrapping = TextWrapping.Wrap };
        mouseHint.SetResourceReference(StyleProperty, "SecondaryText");
        mousePanel.Children.Add(mouseHint);
        panel.Children.Add(mousePanel);

        // run
        var program = new TextBox { Text = a.Program ?? "", MinWidth = 300 };
        program.TextChanged += (_, _) => a.Program = program.Text;
        var browse = new Button { Content = T("Browse…"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = T("Programs") + "|*.exe;*.bat;*.cmd;*.lnk|" + T("All files") + "|*.*" };
            if (dlg.ShowDialog(this) == true) program.Text = dlg.FileName;
        };
        var programLine = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        programLine.Children.Add(browse);
        programLine.Children.Add(program);
        runPanel.Children.Add(Row(T("Program"), programLine, stretch: true));
        var args = new TextBox { Text = a.Arguments ?? "" };
        args.TextChanged += (_, _) => a.Arguments = args.Text;
        runPanel.Children.Add(Row(T("Arguments"), args, stretch: true));
        panel.Children.Add(runPanel);

        // profile
        var profile = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var n in profileNames) profile.Items.Add(n);
        profile.SelectedItem = a.Profile;
        if (profile.SelectedIndex < 0 && profile.Items.Count > 0) profile.SelectedIndex = 0;
        profile.SelectionChanged += (_, _) => a.Profile = profile.SelectedItem as string;
        a.Profile ??= profile.SelectedItem as string;
        profilePanel.Children.Add(Row(T("Switch to"), profile));
        var profileHint = new TextBlock { Text = T("Until another profile's game comes to the front."), TextWrapping = TextWrapping.Wrap };
        profileHint.SetResourceReference(StyleProperty, "SecondaryText");
        profilePanel.Children.Add(profileHint);
        panel.Children.Add(profilePanel);

        kind.SelectionChanged += (_, _) =>
        {
            Update();
            // a new macro: its first box is ready for a key
            if (SelectedKind == ActionKind.Macro && steps.All(s => s.Keys.Count == 0) && firstPicker != null)
                Dispatcher.BeginInvoke(() => firstPicker.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = T("Save"), MinWidth = 90, IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Update();
    }

    // the "What it does" list, in its order (the last entry is "Nothing")
    private static readonly ActionKind[] Kinds = { ActionKind.Keys, ActionKind.Macro, ActionKind.Mouse, ActionKind.Run, ActionKind.Profile };

    private ActionKind? SelectedKind => kind.SelectedIndex >= 0 && kind.SelectedIndex < Kinds.Length ? Kinds[kind.SelectedIndex] : null;

    private void Update()
    {
        var k = SelectedKind;
        keysPanel.Visibility = k == ActionKind.Keys ? Visibility.Visible : Visibility.Collapsed;
        macroPanel.Visibility = k == ActionKind.Macro ? Visibility.Visible : Visibility.Collapsed;
        mousePanel.Visibility = k == ActionKind.Mouse ? Visibility.Visible : Visibility.Collapsed;
        runPanel.Visibility = k == ActionKind.Run ? Visibility.Visible : Visibility.Collapsed;
        profilePanel.Visibility = k == ActionKind.Profile ? Visibility.Visible : Visibility.Collapsed;
        bool isLong = longPress.IsChecked == true;
        mode.IsEnabled = !isLong;
        var m = (PressMode)Math.Max(mode.SelectedIndex, 0);
        pressRow.Visibility = isLong || m is PressMode.Tap or PressMode.Repeat ? Visibility.Visible : Visibility.Collapsed;
        repeatRow.Visibility = repeatDelayRow.Visibility = !isLong && m == PressMode.Repeat ? Visibility.Visible : Visibility.Collapsed;
        longRow.Visibility = isLong ? Visibility.Visible : Visibility.Collapsed;
        modeHint.Text = isLong ? T("A long press is set below, so a short press taps the keys.") : m switch
        {
            PressMode.Tap => T("One press of the length below, however long it's held."),
            PressMode.Repeat => T("Pressed over and over while it's held, like holding a key down in a text box."),
            PressMode.Toggle => T("The first push holds the keys down; the next lets them go."),
            _ => T("The keys go down when it's pressed and up when it's let go, like a key on the keyboard."),
        };
    }

    private void Save()
    {
        if (SelectedKind is not { } k) { Result = null; DialogResult = true; return; }
        a.Kind = k;
        if (longPress.IsChecked != true) { a.LongMs = 0; a.LongKeys = new(); }
        // keep only what this kind uses, so the saved file stays readable
        if (a.Kind != ActionKind.Keys) { a.Keys = new(); a.LongKeys = new(); a.LongMs = 0; a.Mode = PressMode.Hold; }
        if (a.Kind == ActionKind.Macro)
        {
            a.Steps = steps.Where(s => s.Keys.Count > 0).ToList();
            a.PressMs = macroHold;
        }
        else { a.Steps = null; a.Loop = false; }
        if (a.Kind != ActionKind.Mouse) a.MouseX = a.MouseY = 0;
        if (a.Kind != ActionKind.Run) a.Program = a.Arguments = null;
        if (a.Kind != ActionKind.Profile) a.Profile = null;
        Result = a.IsEmpty ? null : a;
        DialogResult = true;
    }

    private void BuildMacro()
    {
        steps = (a.Steps ?? new()).Select(s => new MacroStep { Keys = s.Keys.ToList(), PauseMs = s.PauseMs }).ToList();
        if (steps.Count == 0) steps.Add(new MacroStep());
        macroHold = a.Kind == ActionKind.Macro ? a.PressMs : 50;

        var hint = new TextBlock { Text = T("The keys are pressed one after another, top to bottom, each with its own keys box (several keys in one box are pressed together, like Ctrl + C)."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        macroPanel.Children.Add(hint);

        var gapLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        sameGap.IsChecked = a.SameGap;
        gapLine.Children.Add(sameGap);
        gapLine.Children.Add(new TextBlock { Text = T("Same pause between every key:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        var gap = Seconds(a.GapMs, v => a.GapMs = v);
        gapLine.Children.Add(gap);
        gapLine.Children.Add(new TextBlock { Text = T("seconds"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        macroPanel.Children.Add(gapLine);
        sameGap.Checked += (_, _) => { a.SameGap = true; RenderSteps(); };
        sameGap.Unchecked += (_, _) => { a.SameGap = false; RenderSteps(); };

        macroPanel.Children.Add(Row(T("Each key is held"), Number(macroHold, v => macroHold = v, T("ms"), 1, 10000)));

        var loop = new ComboBox { MinWidth = 220 };
        loop.Items.Add(T("Play it once"));
        loop.Items.Add(T("Play it over and over while it's held"));
        loop.SelectedIndex = a.Loop ? 1 : 0;
        loop.SelectionChanged += (_, _) => a.Loop = loop.SelectedIndex == 1;
        macroPanel.Children.Add(Row(T("Each press"), loop));

        stepScroll.Content = stepList;
        stepScroll.Margin = new Thickness(0, 4, 0, 0);
        macroPanel.Children.Add(stepScroll);

        var add = new Button { Padding = new Thickness(10, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), ToolTip = T("Add a key at the end") };
        var addContent = new StackPanel { Orientation = Orientation.Horizontal };
        var plus = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        plus.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        addContent.Children.Add(plus);
        addContent.Children.Add(new TextBlock { Text = T("Add a key") });
        add.Content = addContent;
        System.Windows.Automation.AutomationProperties.SetName(add, T("Add a key"));
        add.Click += (_, _) =>
        {
            steps.Add(new MacroStep());
            RenderSteps(focusLast: true);
        };
        macroPanel.Children.Add(add);
        RenderSteps();
    }

    private void RenderSteps(bool focusLast = false)
    {
        stepList.Children.Clear();
        KeyPicker? last = null;
        for (int i = 0; i < steps.Count; i++)
        {
            int index = i;
            var step = steps[i];
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var number = new TextBlock { Text = $"{i + 1}.", Width = 26, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(number, Dock.Left);
            line.Children.Add(number);

            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            if (sameGap.IsChecked != true && i < steps.Count - 1)
            {
                tools.Children.Add(new TextBlock { Text = T("then wait"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var wait = Seconds(step.PauseMs ?? a.GapMs, v => step.PauseMs = v);
                tools.Children.Add(wait);
                tools.Children.Add(new TextBlock { Text = T("s"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) });
            }
            tools.Children.Add(Icon("", T("Move up"), i > 0, () => { (steps[index - 1], steps[index]) = (steps[index], steps[index - 1]); RenderSteps(); }));
            tools.Children.Add(Icon("", T("Move down"), i < steps.Count - 1, () => { (steps[index + 1], steps[index]) = (steps[index], steps[index + 1]); RenderSteps(); }));
            tools.Children.Add(Icon("", T("Remove this key"), steps.Count > 1, () => { steps.RemoveAt(index); RenderSteps(); }));
            DockPanel.SetDock(tools, Dock.Right);
            line.Children.Add(tools);

            var picker = new KeyPicker(step.Keys);
            picker.Changed += () => step.Keys = picker.Keys;
            line.Children.Add(picker);
            stepList.Children.Add(line);
            if (i == 0) firstPicker = picker;
            last = picker;
        }
        if (focusLast && last != null)
        {
            // ready for the key straight away
            Dispatcher.BeginInvoke(() => { last.Focus(); stepScroll.ScrollToBottom(); }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private static Button Icon(string glyph, string tip, bool enabled, Action click)
    {
        var b = new Button { Content = glyph, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(2, 0, 0, 0), ToolTip = tip, IsEnabled = enabled };
        b.SetResourceReference(Control.FontFamilyProperty, "IconFont");
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A pause typed in seconds (0.5), kept in milliseconds.</summary>
    private static TextBox Seconds(int ms, Action<int> set)
    {
        var box = new TextBox { Text = (ms / 1000.0).ToString("0.###"), Width = 56, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim().Replace(',', '.');
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s))
                set((int)Math.Round(Math.Clamp(s, 0, 600) * 1000));
        };
        return box;
    }

    private static FrameworkElement Row(string label, FrameworkElement content, bool stretch = false)
    {
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var l = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        line.Children.Add(l);
        if (!stretch) content.HorizontalAlignment = HorizontalAlignment.Left;
        line.Children.Add(content);
        return line;
    }

    private static FrameworkElement Number(int value, Action<int> set, string unit, int min, int max)
    {
        var box = new TextBox { Text = value.ToString(), Width = 70, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out int v)) set(Math.Clamp(v, min, max)); };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        line.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return line;
    }
}
