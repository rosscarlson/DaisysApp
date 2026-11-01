using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DaisysApp.Applets.Binder.Formats;
using DaisysApp.Applets.Binder.Input;
using DaisysApp.Controllers;

namespace DaisysApp.Applets.Binder;

/// <summary>
/// The Binder tab: on the left the titles set up (Add for another, the picked one's details under them) and its devices; on
/// the right the example device for the title's kind and the bindings, a group at a time. A slot is clicked, then a key,
/// mouse button, controller button, hat or axis is pressed or moved (Esc cancels); right-click clears it. Every change
/// is written to the game's files a moment later.
/// </summary>
internal sealed class BinderView : Grid
{
    private const string AddIcon = "", RefreshIcon = "", FolderIcon = "", ResetIcon = "", CloseIcon = "", TickIcon = "", Cross = "", WandIcon = "";

    private readonly BinderSettings settings;
    private readonly IntPtr window;
    private readonly StackPanel titlesList = new(), details = new(), devicesList = new(), rowsPanel = new(), reportRows = new();
    private readonly WrapPanel groupChips = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Border titlesCard, devicesCard, exampleCard, reportCard, bindingsCard, chooseCard;
    private readonly StackPanel chooseList = new();
    private readonly DockPanel wizardBar = new() { Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
    private readonly TextBox templateName = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock stepText = Ui.Secondary("");
    private readonly Button back, next;
    /// <summary>The template the wizard is filling in, or null for a title.</summary>
    private MyTemplate? editing;
    /// <summary>The wizard's first step: driving or flight.</summary>
    private bool choosing;
    private readonly TextBlock exampleTitle = Ui.Header(""), status = Ui.Secondary("", wrap: true), saved = Ui.Secondary(), countText = Ui.Secondary(), reportTitle = Ui.Header("");
    private readonly ExampleDevice example = new() { Height = 250 };
    private readonly TextBox search = new() { Width = 170, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton changedOnly = Ui.Chip(T("Changed"), false), conflictsOnly = Ui.Chip(T("Conflicts"), false);
    private readonly StackPanel captureBar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly TextBlock seenText = Ui.Secondary("");
    private readonly AxisBar seenBar = new() { Height = 4, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Hidden };
    private string seen = "";
    private long seenAt;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private TitleData? data;
    private IInputSource? input;
    private bool adding;
    private string? loadError;
    private string group = "";

    private sealed class Row
    {
        public required BinderAction Action;
        public required TextBlock Label;
        public required Button[] Slots;
        public required AxisBar?[] Bars;
        public CheckBox? Invert;
    }
    private readonly List<Row> rows = new();
    private readonly Dictionary<BinderAction, string> conflicts = new();

    public BinderView(BinderSettings settings, IntPtr window)
    {
        this.settings = settings;
        this.window = window;
        Margin = new Thickness(16);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        ColumnDefinitions.Add(new ColumnDefinition());

        var add = Ui.IconButton(AddIcon, T("Add"), T("Set up another title"), () => { adding = true; RenderTitles(); });
        titlesCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Titles"), add), titlesList, details } }, new Thickness(0, 0, 0, 12));
        var refresh = Ui.IconButton(RefreshIcon, "", T("Look for controllers again"), () => input?.Refresh());
        var wand = Ui.IconButton(WandIcon, "", T("Template Wizard"), StartWizard);
        AutomationProperties.SetName(wand, T("Template Wizard"));
        devicesCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Devices"), wand, refresh), devicesList } });
        var left = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { titlesCard, devicesCard } } };
        Children.Add(left);

        exampleCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(exampleTitle), example } }, new Thickness(0, 0, 0, 12));
        var closeReport = Ui.IconButton(CloseIcon, "", T("Close"), () => reportCard!.Visibility = Visibility.Collapsed);
        reportCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(reportTitle, closeReport), new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = reportRows } } },
            new Thickness(0, 0, 0, 12));
        reportCard.Visibility = Visibility.Collapsed;

        var closeWizard = Ui.IconButton(CloseIcon, "", T("Close"), Open);
        chooseCard = Ui.Card(new StackPanel { Children = { Ui.HeaderRow(T("Template Wizard"), closeWizard), chooseList } }, new Thickness(0, 0, 0, 12));
        chooseCard.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(templateName, T("Template name"));
        templateName.ToolTip = T("Template name");
        templateName.TextChanged += (_, _) =>
        {
            if (editing == null || templateName.Text.Trim().Length == 0 || templateName.Text.Trim() == editing.Name) return;
            editing.Name = templateName.Text.Trim();
            if (data != null) data.Def.Name = editing.Name;
            ScheduleSave();
        };
        back = Ui.Button(T("Back"), () => Step(-1));
        next = Ui.Button(T("Next"), () => Step(1), accent: true);
        var leave = Ui.IconButton(CloseIcon, "", T("Close"), () => { Save(); StartWizard(); });
        foreach (var r in new FrameworkElement[] { leave, next, back })
        {
            r.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(r, Dock.Right);
            wizardBar.Children.Add(r);
        }
        stepText.VerticalAlignment = VerticalAlignment.Center;
        stepText.Margin = new Thickness(10, 0, 0, 0);
        wizardBar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { templateName, stepText } });

        AutomationProperties.SetName(search, T("Search"));
        search.ToolTip = T("Search");
        search.TextChanged += (_, _) => RenderRows();
        Ui.OnToggle(changedOnly, _ => RenderRows());
        Ui.OnToggle(conflictsOnly, _ => RenderRows());
        var cancel = Ui.Button(T("Cancel"), () => CancelCapture(), T("Esc"));
        var clear = Ui.Spaced(Ui.Button(T("Clear"), () => Commit(Bind.None)));
        // (what's being pressed or moved while it waits, so a controller's seen working before it's bound)
        seenText.MaxWidth = 220;
        seenText.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(seenText, T("Seen"));
        captureBar.Children.Add(new StackPanel { Width = 220, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Children = { seenText, seenBar } });
        captureBar.Children.Add(cancel);
        captureBar.Children.Add(clear);
        captureBar.Margin = new Thickness(0, 0, 0, 6);
        saved.HorizontalAlignment = HorizontalAlignment.Right;
        var counts = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Children = { countText, saved } };
        var head = Ui.HeaderRow(Ui.Header(T("Bindings"), new Thickness(0)), counts, changedOnly, conflictsOnly, search);
        var statusRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(captureBar, Dock.Right);
        statusRow.Children.Add(captureBar);
        statusRow.Children.Add(status);
        var rowsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rowsPanel };
        var bindings = new DockPanel();
        foreach (var top in new UIElement[] { wizardBar, head, groupChips, statusRow })
        {
            DockPanel.SetDock(top, Dock.Top);
            bindings.Children.Add(top);
        }
        bindings.Children.Add(rowsScroll);
        bindingsCard = Ui.Card(bindings);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.Children.Add(exampleCard);
        right.Children.Add(chooseCard);
        SetRow(reportCard, 1);
        right.Children.Add(reportCard);
        SetRow(bindingsCard, 2);
        right.Children.Add(bindingsCard);
        // (the right side scrolls when the window is too short for the picture, a report and the bindings)
        var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = right };
        void Fit() => right.Height = Math.Max(rightScroll.ViewportHeight, reportCard.Visibility == Visibility.Visible ? 860 : 680);
        rightScroll.SizeChanged += (_, _) => Fit();
        reportCard.IsVisibleChanged += (_, _) => Fit();
        SetColumn(rightScroll, 2);
        Children.Add(rightScroll);

        tick.Tick += (_, _) => Tick();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { if (data == null && loadError == null) Open(); tick.Start(); }
            else { CancelCapture(silent: true); tick.Stop(); }
        };
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
        PreviewMouseDown += OnMouseDown;
        PreviewMouseUp += (_, e) => { if (swallowUp) { swallowUp = false; e.Handled = true; } };
        PreviewMouseWheel += OnWheel;
        PreviewMouseMove += OnMouseMove;
        RenderTitles();
        RenderAll();
    }

    public void Close()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        tick.Stop();
        if (saveTimer.IsEnabled) { saveTimer.Stop(); Save(); }
        input?.Dispose();
        input = null;
    }

    private TitleSetup? Setup => settings.Titles.FirstOrDefault(t => t.Title == settings.Selected) ?? settings.Titles.FirstOrDefault();

    // ---------------------------------------------------------------- opening a title

    /// <summary>Reads the picked title's files, and starts reading its controllers as it does.</summary>
    private void Open() => Show(null, false);

    /// <summary>The picked title (<paramref name="template"/> null), a template being filled in, or the wizard's first step.</summary>
    private void Show(MyTemplate? template, bool choose)
    {
        if (saveTimer.IsEnabled) { saveTimer.Stop(); Save(); }
        CancelCapture(silent: true);
        // (the last title's rows go first: another title's actions have other slots)
        rows.Clear();
        rowsPanel.Children.Clear();
        data = null;
        loadError = null;
        reportCard.Visibility = Visibility.Collapsed;
        editing = template;
        choosing = choose;
        var setup = Setup;
        if (choose) { }
        else if (template != null)
        {
            data = Templates.Data(template);
            if (data == null) loadError = F("No template called {0} (its JSON file is missing)", template.Base);
        }
        else if (setup != null && Titles.Get(setup.Title) is { } def)
        {
            try
            {
                data = def.Reader!.Load(def, setup);
                Logging.Log.Here.Info($"Binder: {data.Summary}: {data.Actions.Count} actions");
            }
            catch (Exception ex) when (ex is BinderException or IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException or System.Text.Json.JsonException)
            {
                loadError = ex.Message;
                Logging.Log.Here.Warn($"Binder: {def.Name}: {ex.Message}");
            }
        }
        else if (setup != null) loadError = F("No title called {0} (its JSON file is missing)", setup.Title);
        StartInput();
        group = template != null ? template.Section ?? "" : data != null && settings.Group.TryGetValue(data.Def.Id, out var g) ? g : "";
        if (template != null) templateName.Text = template.Name;
        RenderTitles();
        RenderAll();
    }

    private void StartInput()
    {
        var want = data?.Format.Input ?? InputKind.DirectInput;
        if (input != null && input.Kind == want && want == InputKind.DirectInput) return;
        if (input is SdlSource && want == InputKind.Sdl) return;
        input?.Dispose();
        input = want == InputKind.Sdl ? new SdlSource(data!.Format.SdlPath(data)) : new DirectInputSource(window);
        input.DevicesChanged += () => { RenderDevices(); UpdateConflicts(); };
    }

    private void RenderAll()
    {
        RenderDevices();
        exampleCard.Visibility = bindingsCard.Visibility = data != null && !choosing ? Visibility.Visible : Visibility.Collapsed;
        chooseCard.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        wizardBar.Visibility = editing != null ? Visibility.Visible : Visibility.Collapsed;
        if (choosing) RenderChoose();
        if (data == null) { rowsPanel.Children.Clear(); rows.Clear(); return; }
        exampleTitle.Text = data.Def.Kind switch { TitleKind.Racing => T("Wheel, pedals and shifter"), TitleKind.Flight => T("Stick and throttle"), _ => T("Stick, throttle and fire") };
        UpdateConflicts();
        RenderGroups();
        RenderRows();
        UpdateExample();
        UpdateStep();
    }

    // ---------------------------------------------------------------- the Template Wizard

    private void StartWizard()
    {
        adding = false;
        Show(null, true);
    }

    /// <summary>Driving or flight (or another kind a template's JSON adds), and the templates filled in so far.</summary>
    private void RenderChoose()
    {
        chooseList.Children.Clear();
        var kinds = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var t in Templates.All)
        {
            var b = Ui.Button(t.Name, () => StartTemplate(t), F("Bind the usual {0} actions, a section at a time", t.Name.ToLowerInvariant()), accent: true);
            b.MinWidth = 140;
            b.Padding = new Thickness(16, 10, 16, 10);
            b.Margin = new Thickness(0, 0, 10, 6);
            kinds.Children.Add(b);
        }
        if (Templates.All.Count == 0) chooseList.Children.Add(Ui.Note(T("No templates (their JSON files are missing)")));
        chooseList.Children.Add(kinds);
        if (settings.Templates.Count == 0) return;
        chooseList.Children.Add(Ui.Header(T("Your Templates"), new Thickness(0, 10, 0, 6)));
        foreach (var m in settings.Templates.ToList())
        {
            var def = Templates.Get(m.Base);
            int bound = Templates.Bound(m), total = def?.Sections.Sum(x => x.Actions.Count) ?? 0;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = m.Name, FontWeight = FontWeights.SemiBold }, Ui.Secondary(F("{0} · {1} of {2} bound", def?.Name ?? m.Base, bound, total)) } };
            var open = Ui.Button(T("Open"), () => { if (def != null) Show(m, false); });
            open.IsEnabled = def != null;
            AutomationProperties.SetName(open, F("Open {0}", m.Name));
            var remove = ConfirmButton.Make(T("Remove"), () =>
            {
                settings.Templates.Remove(m);
                settings.Save();
                RenderChoose();
            }, T("Delete this template"));
            AutomationProperties.SetName(remove, F("Remove {0}", m.Name));
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            foreach (var r in new FrameworkElement[] { remove, open })
            {
                r.Margin = new Thickness(6, 0, 0, 0);
                r.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(r, Dock.Right);
                row.Children.Add(r);
            }
            row.Children.Add(text);
            chooseList.Children.Add(row);
        }
    }

    /// <summary>The kind's template: the one filled in before (where it was left), or a new one from its first section.</summary>
    private void StartTemplate(TemplateDef def)
    {
        var mine = settings.Templates.FirstOrDefault(m => m.Base.Equals(def.Id, StringComparison.OrdinalIgnoreCase));
        if (mine == null)
        {
            mine = new MyTemplate { Name = F("My {0} Template", def.Name), Base = def.Id };
            settings.Templates.Add(mine);
            settings.Save();
            Logging.Log.Here.Info($"Binder: template {mine.Name} started");
        }
        Show(mine, false);
    }

    private void Step(int by)
    {
        if (editing == null || data == null) return;
        var sections = Templates.Sections(editing);
        int i = Math.Max(0, sections.IndexOf(group));
        if (by > 0 && i == sections.Count - 1)
        {
            // (Finish: back to the first step, the template saved)
            editing.Section = null;
            Save();
            Logging.Log.Here.Info($"Binder: template {editing.Name} finished, {editing.Binds.Count} actions bound");
            StartWizard();
            return;
        }
        Pick(sections[Math.Clamp(i + by, 0, sections.Count - 1)]);
    }

    private void Pick(string g)
    {
        CancelCapture(silent: true);
        group = g;
        if (editing != null) { editing.Section = g; settings.Save(); }
        else if (data != null) settings.Group[data.Def.Id] = g;
        RenderGroups();
        RenderRows();
        UpdateStep();
    }

    private void UpdateStep()
    {
        if (editing == null) return;
        var sections = Templates.Sections(editing);
        int i = sections.IndexOf(group);
        stepText.Text = i < 0 ? T("Every section") : F("Step {0} of {1}: {2}", i + 1, sections.Count, group);
        back.IsEnabled = i > 0;
        next.Content = i == sections.Count - 1 ? T("Finish") : T("Next");
    }

    // ---------------------------------------------------------------- the Titles block

    private void RenderTitles()
    {
        titlesList.Children.Clear();
        details.Children.Clear();
        if (settings.Titles.Count == 0 && !adding) titlesList.Children.Add(Ui.Note(T("No titles yet")));
        var snap = RunningPrograms.Get(5000);
        foreach (var s in settings.Titles)
        {
            var def = Titles.Get(s.Title);
            var name = new TextBlock { Text = def?.Name ?? s.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var line = new DockPanel();
            if (def != null && snap.FirstOf(def.Programs) != null)
            {
                var run = Ui.Secondary(T("Running"));
                run.ToolTip = T("It reads its bindings when it starts");
                run.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                DockPanel.SetDock(run, Dock.Right);
                line.Children.Add(run);
            }
            line.Children.Add(name);
            var pick = new Button { Content = line, Template = Ui.FlatRow, Padding = new Thickness(6), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(pick, def?.Name ?? s.Title);
            if (!adding && editing == null && !choosing && s == Setup) pick.SetResourceReference(BackgroundProperty, "RowSelectedBrush");
            else pick.Background = Brushes.Transparent;
            pick.Click += (_, _) =>
            {
                adding = false;
                settings.Selected = s.Title;
                settings.Save();
                Open();
            };
            titlesList.Children.Add(pick);
        }
        if (adding) AddPanel();
        else if (editing == null && !choosing && Setup is { } setup) Details(setup);
    }

    private void Details(TitleSetup setup)
    {
        var def = Titles.Get(setup.Title);
        details.Margin = new Thickness(0, 10, 0, 0);
        if (data != null) details.Children.Add(new TextBlock { Text = data.Summary, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        var folder = Ui.Secondary(setup.Folder);
        folder.ToolTip = setup.Folder;
        var change = Ui.IconButton(FolderIcon, "", T("Change folder"), () =>
        {
            if (Ui.PickFolder(def?.FolderHint ?? T("The game's folder"), setup.Folder) is not { } f) return;
            setup.Folder = f;
            settings.Save();
            Open();
        });
        change.Margin = new Thickness(6, 0, 0, 0);
        var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(change, Dock.Right);
        folderRow.Children.Add(change);
        folderRow.Children.Add(folder);
        details.Children.Add(folderRow);
        if (def != null) details.Children.Add(Checks(def.Reader!.Check(def, setup.Folder)));
        if (loadError != null)
        {
            var err = Ui.CopyText(loadError);
            err.Margin = new Thickness(0, 4, 0, 4);
            details.Children.Add(err);
        }
        if (data != null && data.Format.Details(data, ScheduleSave, Open) is { } own)
        {
            own.Margin = new Thickness(0, 6, 0, 6);
            details.Children.Add(own);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (data != null)
        {
            var others = Sources(setup, data.Def.Kind);
            if (others.Count > 0)
            {
                var from = Ui.Choice(others, others[0].Item2, _ => { }, 150);
                AutomationProperties.SetName(from, T("Copy from"));
                var copy = ConfirmButton.Make(T("Copy from"), () => { if (from.SelectedItem is ComboBoxItem { Tag: { } o }) CopyFrom(o); }, T("Its bindings, matched by what each action does"));
                copy.Margin = new Thickness(0, 0, 6, 6);
                from.Margin = new Thickness(0, 0, 10, 6);
                buttons.Children.Add(copy);
                buttons.Children.Add(from);
            }
            if (def != null && Import.Can(def))
            {
                var import = ConfirmButton.Make(T("Import"), ImportGame, T("The bindings the game has now, from its own file"));
                import.Margin = new Thickness(0, 0, 6, 6);
                buttons.Children.Add(import);
            }
            if (Templates.All.Any(t => t.For(data.Def.Kind)))
            {
                var toTemplate = ConfirmButton.Make(T("Save as Template"), SaveAsTemplate, T("These bindings into your template for this kind of game"));
                toTemplate.Margin = new Thickness(0, 0, 6, 6);
                buttons.Children.Add(toTemplate);
            }
            var resetAll = ConfirmButton.Make(T("Reset All"), ResetAll, T("Every binding back to the game's defaults"));
            resetAll.Margin = new Thickness(0, 0, 6, 6);
            buttons.Children.Add(resetAll);
        }
        var remove = ConfirmButton.Make(T("Remove"), () =>
        {
            settings.Titles.Remove(setup);
            settings.Selected = settings.Titles.FirstOrDefault()?.Title;
            settings.Save();
            Open();
        }, T("Take it off the list (the game's files stay)"));
        remove.Margin = new Thickness(0, 0, 6, 6);
        buttons.Children.Add(remove);
        details.Children.Add(buttons);
    }

    private static StackPanel Checks(IReadOnlyList<Check> checks)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var c in checks)
        {
            var icon = Ui.Icon(c.Ok ? TickIcon : Cross, 11);
            icon.SetResourceReference(TextBlock.ForegroundProperty, c.Ok ? "SuccessBrush" : c.Needed ? "ErrorTextBrush" : "TextSecondaryBrush");
            icon.Margin = new Thickness(0, 0, 6, 0);
            var text = new TextBlock { Text = c.Text, TextWrapping = TextWrapping.Wrap, ToolTip = c.Path };
            text.SetResourceReference(StyleProperty, "SecondaryText");
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>Setting up another title: the game, its folder (found by itself where it can), what's found there, and
    /// whose bindings to start from.</summary>
    private void AddPanel()
    {
        details.Margin = new Thickness(0, 10, 0, 0);
        var free = Titles.All.Where(t => settings.Titles.All(s => s.Title != t.Id)).ToList();
        if (free.Count == 0)
        {
            details.Children.Add(Ui.Note(T("Every title Binder knows is set up")));
            details.Children.Add(Ui.Button(T("Cancel"), () => { adding = false; RenderTitles(); }));
            return;
        }
        var folder = new TextBox { Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(folder, T("Folder"));
        var checks = new ContentControl();
        var copyFrom = Ui.Choice(new[] { (T("Nothing (the game's own)"), (object?)null) }.Concat(Sources(null, null).Select(x => (x.Item1, (object?)x.Item2))),
            null, _ => { }, 200);
        AutomationProperties.SetName(copyFrom, T("Copy bindings from"));
        var addButton = Ui.Button(T("Add"), () => { }, accent: true);
        var importBox = new CheckBox { Content = T("Import its bindings"), Margin = new Thickness(0, 6, 0, 0), ToolTip = T("The bindings the game has now, from its own file") };
        TitleDef current = free[0];
        void Look()
        {
            var list = current.Reader!.Check(current, folder.Text.Trim());
            checks.Content = Checks(list);
            addButton.IsEnabled = (folder.Text.Trim().Length > 0 || !current.Reader!.NeedsFolder) && list.All(c => c.Ok || !c.Needed);
            // (ticked when its file turns up, as the folder's picked)
            bool found = Import.FindFile(current, folder.Text.Trim()) != null;
            importBox.Visibility = Import.Can(current) ? Visibility.Visible : Visibility.Collapsed;
            if (found && !importBox.IsEnabled) importBox.IsChecked = true;
            if (!found) importBox.IsChecked = false;
            importBox.IsEnabled = found;
        }
        void Found()
        {
            folder.Text = current.Find.Select(Paths.First).FirstOrDefault(p => p != null && current.Reader!.Check(current, p).All(c => c.Ok || !c.Needed))
                ?? current.Find.Select(Paths.First).FirstOrDefault(p => p != null) ?? "";
            Look();
        }
        var game = Ui.Choice(free.Select(t => (t.Name, t)), current, t => { current = t; Found(); }, 200);
        AutomationProperties.SetName(game, T("Title"));
        folder.TextChanged += (_, _) => Look();
        var browse = Ui.IconButton(FolderIcon, "", T("Browse"), () =>
        {
            if (Ui.PickFolder(current.FolderHint ?? T("The game's folder"), folder.Text) is { } f) folder.Text = f;
        });
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folder);
        addButton.Click += (_, _) =>
        {
            var setup = new TitleSetup { Title = current.Id, Folder = folder.Text.Trim() };
            var from = (copyFrom.SelectedItem as ComboBoxItem)?.Tag;
            settings.Titles.Add(setup);
            settings.Selected = setup.Title;
            settings.Save();
            adding = false;
            Logging.Log.Here.Info($"Binder: {current.Name} set up in {setup.Folder}");
            bool import = importBox.IsChecked == true && importBox.Visibility == Visibility.Visible;
            Open();
            if (import && data != null) ImportGame();
            if (from != null && data != null) CopyFrom(from);
        };
        details.Children.Add(Field(T("Title"), game));
        details.Children.Add(new TextBlock { Text = T("Where it's installed"), Margin = new Thickness(0, 8, 0, 0) });
        details.Children.Add(folderRow);
        details.Children.Add(checks);
        details.Children.Add(importBox);
        details.Children.Add(Field(T("Copy bindings from"), copyFrom));
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0),
            Children = { addButton, Ui.Spaced(Ui.Button(T("Cancel"), () => { adding = false; RenderTitles(); })) },
        });
        Found();
    }

    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.Width = double.NaN;
        return new StackPanel { Margin = new Thickness(0, 6, 0, 2), Children = { new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) }, control } };
    }

    /// <summary>What bindings can be copied from: the templates (those for <paramref name="kind"/> first), then the other titles.</summary>
    private List<(string, object)> Sources(TitleSetup? except, TitleKind? kind)
    {
        var list = new List<(string, object)>();
        foreach (var m in settings.Templates.OrderBy(m => kind != null && Templates.Get(m.Base)?.For(kind.Value) == true ? 0 : 1))
            if (Templates.Get(m.Base) != null) list.Add((F("Template: {0}", m.Name), m));
        foreach (var t in settings.Titles.Where(t => t != except && Titles.Get(t.Title) != null)) list.Add((Titles.Get(t.Title)!.Name, t));
        return list;
    }

    /// <summary>Another title's or a template's bindings into this one, and what couldn't come over.</summary>
    private void CopyFrom(object from)
    {
        if (data == null) return;
        TitleData? source = null;
        if (from is MyTemplate m) source = Templates.Data(m);
        else if (from is TitleSetup setup && Titles.Get(setup.Title) is { } def)
        {
            try { source = def.Reader!.Load(def, setup); }
            catch (Exception ex)
            {
                Status(F("{0} couldn't be read: {1}", def.Name, ex.Message), error: true);
                return;
            }
        }
        if (source == null) return;
        var report = Migrate.Copy(source, data, DeviceName);
        reportTitle.Text = F("Copied from {0}: {1}", report.From, P(report.Copied, "{0} binding", "{0} bindings"));
        reportRows.Children.Clear();
        void Line(string text, string? brush = null)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            reportRows.Children.Add(t);
        }
        if (report.Refused.Count > 0) reportRows.Children.Add(Ui.Header(F("Couldn't bind ({0})", report.Refused.Count), new Thickness(0, 4, 0, 2)));
        foreach (var r in report.Refused) Line($"{r.Action}: {r.Bind} · {r.Why}", "ErrorTextBrush");
        if (report.Check.Count > 0) reportRows.Children.Add(Ui.Header(F("Check these axes ({0})", report.Check.Count), new Thickness(0, 8, 0, 2)));
        foreach (var c in report.Check) Line(c);
        if (report.Missing.Count > 0) reportRows.Children.Add(Ui.Header(F("Not in your template ({0})", report.Missing.Count), new Thickness(0, 8, 0, 2)));
        foreach (var n in report.Missing) Line(n);
        if (report.NoMatch.Count > 0)
            reportRows.Children.Add(Ui.Header(report.FromTemplate ? F("In your template, not in this game ({0})", report.NoMatch.Count) : F("Nothing like it here ({0})", report.NoMatch.Count), new Thickness(0, 8, 0, 2)));
        foreach (var n in report.NoMatch) Line($"{n.Action}: {n.Bind}");
        reportCard.Visibility = Visibility.Visible;
        UpdateConflicts();
        RenderRows();
        ScheduleSave();
    }

    /// <summary>The bindings the game has in its own file (a <c>sheet</c> title's), and what came in.</summary>
    private void ImportGame()
    {
        if (data == null) return;
        CancelCapture(silent: true);
        var file = Import.FindFile(data.Def, data.Setup.Folder);
        if (file == null) { Status(F("{0}: its bindings weren't found", data.Def.Name), error: true); return; }
        ImportReport report;
        try { report = Import.Apply(data, file); }
        catch (Exception ex) when (ex is BinderException or IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Text.Json.JsonException or FormatException)
        {
            Status(F("{0} couldn't be read: {1}", System.IO.Path.GetFileName(file), ex.Message), error: true);
            Logging.Log.Here.Warn($"Binder: importing {file}: {ex.Message}");
            return;
        }
        reportTitle.Text = F("Imported from {0}: {1}", System.IO.Path.GetFileName(file), P(report.Bound, "{0} binding", "{0} bindings"));
        reportRows.Children.Clear();
        var where = new TextBlock { Text = file, TextWrapping = TextWrapping.Wrap };
        where.SetResourceReference(StyleProperty, "SecondaryText");
        reportRows.Children.Add(where);
        if (report.Added > 0) reportRows.Children.Add(new TextBlock { Text = F("{0} more of the game's own, under From the game", report.Added), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (report.Refused.Count > 0) reportRows.Children.Add(Ui.Header(F("Couldn't bind ({0})", report.Refused.Count), new Thickness(0, 8, 0, 2)));
        foreach (var r in report.Refused)
        {
            var t = new TextBlock { Text = r, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            reportRows.Children.Add(t);
        }
        reportCard.Visibility = Visibility.Visible;
        UpdateConflicts();
        RenderGroups();
        RenderRows();
        UpdateExample();
        ScheduleSave();
    }

    /// <summary>This title's bindings into the template for its kind, instead of going through the wizard.</summary>
    private void SaveAsTemplate()
    {
        if (data == null || Templates.FromTitle(settings.Templates, data) is not { } r) return;
        settings.Save();
        Status(F("{0} saved into {1}", P(r.Copied, "{0} action", "{0} actions"), r.Template.Name));
        RenderTitles();
    }

    private void ResetAll()
    {
        if (data == null) return;
        CancelCapture(silent: true);
        foreach (var a in data.Actions) data.Binds[a.Id] = Defaults(a);
        UpdateConflicts();
        RenderRows();
        ScheduleSave();
        Status(T("Every binding is back to the game's defaults"));
    }

    private Bind[] Defaults(BinderAction a) =>
        Enumerable.Range(0, data!.Format.Slots(a)).Select(i => i < a.Defaults.Length ? a.Defaults[i] : Bind.None).ToArray();

    // ---------------------------------------------------------------- the Devices block

    private readonly List<(LiveDevice Device, Ellipse Dot)> dots = new();

    private void RenderDevices()
    {
        devicesList.Children.Clear();
        dots.Clear();
        if (input?.Problem is { } problem) { devicesList.Children.Add(Ui.CopyText(problem)); return; }
        var list = input?.Devices ?? [];
        if (list.Count == 0) { devicesList.Children.Add(Ui.Note(T("No game controllers"))); return; }
        foreach (var d in list)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = T("Green while it has input"), StrokeThickness = 1 };
            dot.SetResourceReference(Shape.StrokeProperty, "ControlBorderStrongBrush");
            dots.Add((d, dot));
            var text = new StackPanel
            {
                Children = { new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{d.Name} ({d.Id})" }, Ui.Secondary(d.Detail) },
            };
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            if (data?.Format.DeviceExtra(data, d, () => { ScheduleSave(); UpdateConflicts(); RenderRows(); }) is { } extra)
            {
                extra.VerticalAlignment = VerticalAlignment.Center;
                extra.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(extra, Dock.Right);
                row.Children.Add(extra);
            }
            row.Children.Add(text);
            devicesList.Children.Add(row);
        }
    }

    private string? DeviceName(DeviceId id) => input?.Find(id)?.Name;

    private bool Sdl => data?.Format.Input == InputKind.Sdl;

    private string Text(Bind b) => Describe.Text(b, DeviceName, Sdl);

    // ---------------------------------------------------------------- the bindings

    private void RenderGroups()
    {
        groupChips.Children.Clear();
        if (data == null) return;
        var groups = data.Actions.Select(a => a.Group).Distinct().ToList();
        if (group != "" && !groups.Contains(group)) group = "";
        if (group == "" && groups.Count > 0) group = groups[0];
        foreach (var g in groups.Append(T("All")))
        {
            var chip = Ui.Chip(g, g == group);
            chip.Click += (_, _) => Pick(g);
            chip.Margin = new Thickness(0, 0, 6, 6);
            groupChips.Children.Add(chip);
        }
    }

    private void RenderRows()
    {
        rowsPanel.Children.Clear();
        rows.Clear();
        if (data == null) return;
        var q = search.Text.Trim();
        bool all = group == T("All");
        var shown = data.Actions.Where(a =>
        {
            if (q.Length == 0 && !all && a.Group != group) return false;
            if (changedOnly.IsChecked == true && !Changed(a)) return false;
            if (conflictsOnly.IsChecked == true && !conflicts.ContainsKey(a)) return false;
            if (q.Length == 0) return true;
            return a.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (a.GameLabel?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) || data.Get(a).Any(b => Text(b).Contains(q, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        string? lastGroup = null;
        int slots = data.Actions.Count == 0 ? 2 : data.Actions.Max(a => data.Format.Slots(a));
        foreach (var a in shown)
        {
            if ((all || q.Length > 0) && a.Group != lastGroup) rowsPanel.Children.Add(Ui.Header(a.Group, new Thickness(0, lastGroup == null ? 0 : 10, 0, 4)));
            lastGroup = a.Group;
            rowsPanel.Children.Add(MakeRow(a, slots));
        }
        if (shown.Count == 0) rowsPanel.Children.Add(Ui.Note(T("Nothing to show")));
        int changed = data.Actions.Count(Changed);
        countText.Text = string.Join(" · ", new[]
        {
            shown.Count == data.Actions.Count ? P(data.Actions.Count, "{0} action", "{0} actions") : F("{0} of {1}", shown.Count, data.Actions.Count),
            changed > 0 ? F("{0} changed", changed) : null, conflicts.Count > 0 ? F("{0} conflicts", conflicts.Count) : null,
        }.Where(s => s != null));
        countText.Margin = new Thickness(0, 0, 10, 0);
    }

    private bool Changed(BinderAction a) => !data!.Get(a).SequenceEqual(Defaults(a));

    private FrameworkElement MakeRow(BinderAction a, int maxSlots)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        for (int i = 0; i < maxSlots; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = a.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = a.GameLabel != null ? F("{0}: “{1}” in the game", a.Label, a.GameLabel) : a.Label };
        // (the game's default under the name, to bind by)
        var defaultText = string.Join(" · ", a.Defaults.Where(d => !d.IsNone).Select(Text));
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label } };
        if (defaultText.Length > 0)
        {
            var defaults = Ui.Secondary(defaultText);
            defaults.FontSize = 11;
            defaults.ToolTip = editing != null ? T("What it was before") : T("The game's default");
            name.Children.Add(defaults);
        }
        grid.Children.Add(name);
        int n = data!.Format.Slots(a);
        var buttons = new Button[n];
        var bars = new AxisBar?[n];
        for (int i = 0; i < n; i++)
        {
            int slot = i;
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties.SetName(b, F("{0}: {1}", a.Label, data.Format.SlotName(a, slot)));
            b.Click += (_, _) => { if (cap?.Action == a && cap.Value.Slot == slot) CancelCapture(); else StartCapture(a, slot); };
            b.MouseRightButtonUp += (_, e) => { e.Handled = true; if (cap == null) Set(a, slot, Bind.None); };
            buttons[i] = b;
            var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { b } };
            if (a.IsAxis)
            {
                bars[i] = new AxisBar { Centred = a.Centred, Height = 4, Margin = new Thickness(6, 3, 0, 1) };
                cell.Children.Add(bars[i]!);
            }
            SetColumn(cell, 1 + i);
            grid.Children.Add(cell);
        }
        CheckBox? invert = null;
        if (data.Format.CanInvert(a))
        {
            invert = new CheckBox { Content = T("Invert"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsChecked = data.Get(a).Any(x => x.Inverted) };
            AutomationProperties.SetName(invert, F("Invert {0}", a.Label));
            invert.Click += (_, _) =>
            {
                var binds = data.Get(a);
                for (int i = 0; i < binds.Length; i++) binds[i] = binds[i].WithInverted(invert.IsChecked == true);
                Changed(a, false);
            };
            SetColumn(invert, 1 + maxSlots);
            grid.Children.Add(invert);
        }
        var reset = Ui.IconButton(ResetIcon, "", T("Back to the game's default"), () => { data.Binds[a.Id] = Defaults(a); Changed(a, true); });
        reset.Padding = new Thickness(6, 3, 6, 3);
        reset.Margin = new Thickness(6, 0, 0, 0);
        AutomationProperties.SetName(reset, F("Reset {0}", a.Label));
        SetColumn(reset, 2 + maxSlots);
        grid.Children.Add(reset);

        var row = new Row { Action = a, Label = label, Slots = buttons, Bars = bars, Invert = invert };
        rows.Add(row);
        ShowRow(row);
        return grid;
    }

    private void ShowRow(Row r)
    {
        var binds = data!.Get(r.Action);
        conflicts.TryGetValue(r.Action, out var conflict);
        for (int i = 0; i < r.Slots.Length; i++)
        {
            var b = r.Slots[i];
            bool capturing = cap?.Action == r.Action && cap.Value.Slot == i;
            var bind = binds[i];
            var t = new TextBlock { Text = capturing ? T("Press or move…") : bind.IsNone ? "—" : Text(bind), TextTrimming = TextTrimming.CharacterEllipsis };
            b.Content = t;
            if (capturing) b.SetResourceReference(StyleProperty, "AccentButton");
            else b.ClearValue(StyleProperty);
            string? mine = Conflict(r.Action, bind);
            if (mine != null) { b.SetResourceReference(Control.BorderBrushProperty, "DangerBrush"); b.BorderThickness = new Thickness(1.5); }
            else { b.ClearValue(Control.BorderBrushProperty); b.ClearValue(Control.BorderThicknessProperty); }
            var plugged = bind.IsController && input?.Find(bind.Device) == null ? T("Not plugged in") : null;
            b.ToolTip = string.Join("\n", new[] { data.Format.SlotName(r.Action, i), mine, plugged, T("Click, then press or move it; right-click clears") }.Where(s => s != null));
        }
        if (r.Invert != null) r.Invert.IsChecked = binds.Any(x => x.Inverted);
    }

    /// <summary>The other actions using the same input, or null.</summary>
    private string? Conflict(BinderAction a, Bind b)
    {
        if (b.IsNone || data == null) return null;
        var others = data.Actions.Where(o => o != a && o.Context == a.Context && data.Get(o).Any(x => x.SameInput(b))).Select(o => o.Label).ToList();
        return others.Count == 0 ? null : F("Also: {0}", string.Join(", ", others.Take(4)) + (others.Count > 4 ? "…" : ""));
    }

    private void UpdateConflicts()
    {
        conflicts.Clear();
        if (data == null) return;
        var byInput = new Dictionary<(Bind, string), List<BinderAction>>();
        foreach (var a in data.Actions)
            foreach (var b in data.Get(a))
            {
                if (b.IsNone) continue;
                var key = (b with { Inverted = false }, a.Context);
                if (!byInput.TryGetValue(key, out var list)) byInput[key] = list = new();
                if (!list.Contains(a)) list.Add(a);
            }
        foreach (var list in byInput.Values.Where(l => l.Count > 1))
            foreach (var a in list) conflicts[a] = string.Join(", ", list.Where(o => o != a).Select(o => o.Label));
        foreach (var r in rows) ShowRow(r);
    }

    private void Set(BinderAction a, int slot, Bind b)
    {
        var binds = data!.Get(a);
        // (a new axis keeps the slot's Invert)
        binds[slot] = b.IsAxis && binds[slot].IsAxis ? b.WithInverted(binds[slot].Inverted) : b;
        Changed(a, true);
        Status(b.IsNone ? F("{0}: cleared", a.Label) : $"{a.Label} → {Text(binds[slot])}" + (Conflict(a, binds[slot]) is { } c ? $" · {c}" : ""), error: Conflict(a, binds[slot]) != null);
    }

    private void Changed(BinderAction a, bool redraw)
    {
        UpdateConflicts();
        if (redraw) foreach (var r in rows.Where(r => r.Action == a)) ShowRow(r);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        saved.Text = "";
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void Save()
    {
        if (data == null || input == null) return;
        try
        {
            data.Format.Save(data, input);
            saved.Text = T("✓ Saved");
            settings.Save();
            RenderDevices();
            if (Titles.Get(data.Setup.Title) is { } def && RunningPrograms.Get(2000).FirstOf(def.Programs) != null)
                Status(F("{0} is running: it reads its bindings when it starts", def.Name));
        }
        catch (Exception ex)
        {
            saved.Text = "";
            Status(F("Couldn't save: {0}", ex.Message), error: true);
            Logging.Log.Here.Warn($"Binder: saving {data.Summary}", ex);
        }
    }

    private void Status(string text, bool error = false)
    {
        status.Text = text;
        if (error) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);
    }

    // ---------------------------------------------------------------- capturing

    private (BinderAction Action, int Slot)? cap;
    private readonly Dictionary<(LiveDevice, int), double> baseline = new();
    private int? pendingModifier;
    private Point? mouseStart;
    private bool swallowUp;

    private void StartCapture(BinderAction a, int slot)
    {
        CancelCapture(silent: true);
        cap = (a, slot);
        baseline.Clear();
        foreach (var d in input?.Devices ?? [])
            for (int i = 0; i < d.Axis.Length; i++) baseline[(d, i)] = d.Axis[i];
        pendingModifier = null;
        mouseStart = null;
        seen = "";
        seenText.Text = T("Waiting for input…");
        seenBar.Visibility = Visibility.Hidden;
        captureBar.Visibility = Visibility.Visible;
        Status(a.IsAxis ? F("{0}: move an axis all the way", a.Label) : F("{0}: press a key, mouse button, controller button or hat", a.Label));
        foreach (var r in rows.Where(r => r.Action == a)) ShowRow(r);
    }

    private void CancelCapture(bool silent = false)
    {
        if (cap is not { } c) return;
        cap = null;
        pendingModifier = null;
        captureBar.Visibility = Visibility.Collapsed;
        foreach (var r in rows.Where(r => r.Action == c.Action)) ShowRow(r);
        if (!silent) Status(T("Cancelled"));
    }

    /// <summary>Something pressed or moved while waiting: bound if the game takes it there, else why not.</summary>
    private bool Offer(Bind b)
    {
        if (cap is not { } c || data == null) return false;
        if (data.Format.Refuse(data, c.Action, c.Slot, b) is { } why)
        {
            Status($"{Text(b)}: {why}", error: true);
            return false;
        }
        Commit(b);
        return true;
    }

    private void Commit(Bind b)
    {
        if (cap is not { } c) return;
        cap = null;
        captureBar.Visibility = Visibility.Collapsed;
        Set(c.Action, c.Slot, b);
    }

    private void CaptureControllers(List<InputEvent> events)
    {
        if (cap == null || input == null) return;
        foreach (var e in events)
        {
            var b = e switch
            {
                ButtonDown d => Bind.Button(d.Device.Id, d.Button),
                HatDown h => Bind.HatDir(h.Device.Id, h.Hat, h.Dir),
                _ => null,
            };
            if (b != null && Offer(b)) return;
        }
        // (an axis counts once it has moved half its range from where it was)
        foreach (var d in input.Devices)
            for (int i = 0; i < d.Axis.Length; i++)
            {
                double v = d.Axis[i];
                if (double.IsNaN(v)) continue;
                if (!baseline.TryGetValue((d, i), out var start) || double.IsNaN(start)) { baseline[(d, i)] = v; continue; }
                if (Math.Abs(v - start) < 1.0) continue;
                baseline[(d, i)] = v;
                if (Offer(Bind.Axis(d.Id, i))) return;
            }
    }

    // keys: the raw messages, for scancodes, and so Alt, F10 and Space don't do what they'd do
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_DEADCHAR = 0x0103, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_SYSCHAR = 0x0106, WM_SYSDEADCHAR = 0x0107;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_PAUSE = 0x13, VK_ESCAPE = 0x1B, VK_NUMLOCK = 0x90, VK_RMENU = 0xA5;
    private readonly HashSet<int> keysDown = new();

    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    private static bool Down(int vk) => GetKeyState(vk) < 0;

    private bool Ours => IsVisible && Window.GetWindow(this) is { IsActive: true };

    private static int KeyCode(int vk, long lParam)
    {
        if (vk == VK_NUMLOCK) return 0x45;
        if (vk == VK_PAUSE) return 0xC5;
        int sc = (int)((lParam >> 16) & 0xFF);
        return sc | (((lParam >> 24) & 1) != 0 ? 0x80 : 0);
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (!Ours) return;
        int m = msg.message;
        if (m is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP or WM_SYSKEYUP)
        {
            int code = KeyCode((int)msg.wParam, (long)msg.lParam);
            if (m is WM_KEYDOWN or WM_SYSKEYDOWN) keysDown.Add(code); else keysDown.Remove(code);
        }
        if (cap == null) return;
        switch (m)
        {
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                handled = true;
                if (((long)msg.lParam & (1L << 30)) != 0) return; // held, repeating
                CaptureKey((int)msg.wParam, (long)msg.lParam);
                break;
            case WM_KEYUP or WM_SYSKEYUP:
                handled = true;
                int up = KeyCode((int)msg.wParam, (long)msg.lParam);
                if (pendingModifier == up) { pendingModifier = null; Offer(Bind.Key(up)); }
                break;
            case WM_CHAR or WM_SYSCHAR or WM_DEADCHAR or WM_SYSDEADCHAR:
                handled = true;
                break;
        }
    }

    private void CaptureKey(int vk, long lParam)
    {
        if (vk == VK_ESCAPE) { CancelCapture(); return; }
        int code = KeyCode(vk, lParam);
        if (!Keys.Known(code)) { Status(T("That key has no name Binder knows"), error: true); return; }
        // (a modifier is bound when it's let go with nothing pressed meanwhile, so Shift+T stays a combination)
        if (Keys.IsModifier(code)) { pendingModifier = code; return; }
        pendingModifier = null;
        var mods = Mods.None;
        if (Down(VK_SHIFT)) mods |= Mods.Shift;
        if (Down(VK_CONTROL) && !Down(VK_RMENU)) mods |= Mods.Ctrl;
        if (Down(VK_MENU)) mods |= Mods.Alt;
        Offer(Bind.Key(code, mods));
    }

    private bool InCaptureBar(object source)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == captureBar) return true;
        return false;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (cap == null || InCaptureBar(e.OriginalSource)) return;
        e.Handled = true;
        swallowUp = true;
        Offer(new Bind(BindKind.Mouse, e.ChangedButton switch
        {
            MouseButton.Left => 0, MouseButton.Right => 1, MouseButton.Middle => 2, MouseButton.XButton1 => 3, _ => 4,
        }));
    }

    private long wheelUpUntil, wheelDownUntil;

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        long until = Environment.TickCount64 + 200;
        if (e.Delta > 0) wheelUpUntil = until; else wheelDownUntil = until;
        mouseZ = e.Delta > 0 ? 1 : -1;
        if (cap is not { } c) return;
        e.Handled = true;
        Offer(c.Action.IsAxis ? new Bind(BindKind.MouseAxis, 2) : new Bind(BindKind.Mouse, e.Delta > 0 ? 5 : 6));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (cap is not { } c || !c.Action.IsAxis) return;
        var p = e.GetPosition(this);
        if (mouseStart is not { } s) { mouseStart = p; return; }
        double dx = p.X - s.X, dy = p.Y - s.Y;
        if (Math.Abs(dx) > 180 && Math.Abs(dx) > 2 * Math.Abs(dy)) { mouseStart = p; Offer(new Bind(BindKind.MouseAxis, 0)); }
        else if (Math.Abs(dy) > 180 && Math.Abs(dy) > 2 * Math.Abs(dx)) { mouseStart = p; Offer(new Bind(BindKind.MouseAxis, 1)); }
    }

    // ---------------------------------------------------------------- live: held bindings bold, axes' bars, the example

    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint p);
    private CursorPoint? lastCursor;
    private double mouseX, mouseY, mouseZ;

    private void Tick()
    {
        if (input == null) return;
        var events = input.Poll();
        if (cap != null) { ShowSeen(events); CaptureControllers(events); }
        if (!Ours) keysDown.Clear();
        // (the mouse as an axis: the bar jumps with a flick and eases back)
        if (Ours && GetCursorPos(out var p))
        {
            if (lastCursor is { } last)
            {
                mouseX = Follow(mouseX, (p.X - last.X) / 12.0);
                mouseY = Follow(mouseY, (p.Y - last.Y) / 12.0);
            }
            lastCursor = p;
        }
        else { lastCursor = null; mouseX *= 0.8; mouseY *= 0.8; }
        mouseZ *= 0.85;

        foreach (var (d, dot) in dots)
        {
            if (d.Active) dot.SetResourceReference(Shape.FillProperty, "SuccessBrush");
            else dot.Fill = Brushes.Transparent;
        }
        if (data == null) return;
        foreach (var r in rows)
        {
            var binds = data.Get(r.Action);
            bool held = false;
            for (int i = 0; i < r.Slots.Length; i++)
            {
                if (r.Bars[i] is { } bar)
                {
                    // (a bar only under an axis)
                    bar.Visibility = binds[i].IsAxis ? Visibility.Visible : Visibility.Hidden;
                    bar.Value = AxisValue(binds[i]);
                }
                bool h = Held(binds[i]);
                held |= h;
                // (what's held lights up: a binding's seen working as it's pressed)
                if (r.Slots[i].Content is TextBlock t && (t.FontWeight == FontWeights.Bold) != h)
                {
                    t.FontWeight = h ? FontWeights.Bold : FontWeights.Normal;
                    if (h) t.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                    else t.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            r.Label.FontWeight = held ? FontWeights.Bold : FontWeights.Normal;
        }
        UpdateExample();

        static double Follow(double current, double raw)
        {
            raw = Math.Clamp(raw, -1, 1);
            return Math.Abs(raw) > Math.Abs(current) ? raw : current * 0.8;
        }
    }

    /// <summary>While a slot waits: the last thing pressed, or the axis moving most (with its bar), as it's read.</summary>
    private void ShowSeen(List<InputEvent> events)
    {
        long now = Environment.TickCount64;
        foreach (var e in events)
        {
            var b = e switch { ButtonDown d => Bind.Button(d.Device.Id, d.Button), HatDown h => Bind.HatDir(h.Device.Id, h.Hat, h.Dir), _ => null };
            if (b != null) { seen = Text(b); seenAt = now; }
        }
        var key = keysDown.Where(k => !Keys.IsModifier(k)).Select(k => (int?)k).FirstOrDefault() ?? keysDown.Select(k => (int?)k).FirstOrDefault();
        if (key is { } k)
        {
            var mods = Mods.None;
            if (keysDown.Contains(Keys.LeftShift) || keysDown.Contains(Keys.RightShift)) mods |= Mods.Shift;
            if (keysDown.Contains(Keys.LeftCtrl) || keysDown.Contains(Keys.RightCtrl)) mods |= Mods.Ctrl;
            if (keysDown.Contains(Keys.LeftAlt) || keysDown.Contains(Keys.RightAlt)) mods |= Mods.Alt;
            seen = Keys.IsModifier(k) ? Keys.Label(k) : Keys.Text(k, mods);
            seenAt = now;
        }
        (LiveDevice Device, int Axis, double Moved)? most = null;
        foreach (var d in input!.Devices)
            for (int i = 0; i < d.Axis.Length; i++)
            {
                if (double.IsNaN(d.Axis[i]) || !baseline.TryGetValue((d, i), out var start) || double.IsNaN(start)) continue;
                double moved = Math.Abs(d.Axis[i] - start);
                if (moved > 0.08 && (most == null || moved > most.Value.Moved)) most = (d, i, moved);
            }
        if (most is { } m)
        {
            seen = Text(Bind.Axis(m.Device.Id, m.Axis));
            seenAt = now;
            seenBar.Visibility = Visibility.Visible;
            seenBar.Value = m.Device.Axis[m.Axis];
        }
        else seenBar.Visibility = Visibility.Hidden;
        seenText.Text = now - seenAt < 1500 && seen.Length > 0 ? F("Seen: {0}", seen) : T("Waiting for input…");
    }

    private bool Held(Bind b)
    {
        switch (b.Kind)
        {
            case BindKind.Key:
                if (!keysDown.Contains(b.Code)) return false;
                if (Keys.IsModifier(b.Code)) return true;
                var mods = Mods.None;
                if (keysDown.Contains(Keys.LeftShift) || keysDown.Contains(Keys.RightShift)) mods |= Mods.Shift;
                if (keysDown.Contains(Keys.LeftCtrl) || keysDown.Contains(Keys.RightCtrl)) mods |= Mods.Ctrl;
                if (keysDown.Contains(Keys.LeftAlt) || keysDown.Contains(Keys.RightAlt)) mods |= Mods.Alt;
                return mods == b.Mods;
            case BindKind.Mouse:
                if (!Ours) return false;
                long now = Environment.TickCount64;
                return b.Code switch
                {
                    0 => Mouse.LeftButton == MouseButtonState.Pressed, 1 => Mouse.RightButton == MouseButtonState.Pressed,
                    2 => Mouse.MiddleButton == MouseButtonState.Pressed, 3 => Mouse.XButton1 == MouseButtonState.Pressed,
                    4 => Mouse.XButton2 == MouseButtonState.Pressed, 5 => now < wheelUpUntil, 6 => now < wheelDownUntil, _ => false,
                };
            case BindKind.Button:
                return input?.Find(b.Device) is { } d && b.Code < d.Button.Length && d.Button[b.Code];
            case BindKind.Hat:
                return input?.Find(b.Device) is { } h && b.Hat < h.Hat.Length && (h.Hat[b.Hat] & (1 << b.Code)) != 0;
            case BindKind.Axis when b.Half != 0:
                return AxisValue(b with { Half = 0 }) * b.Half > 0.5;
            default:
                return false;
        }
    }

    /// <summary>The input behind an axis binding now, -1 to 1, inverted as bound.</summary>
    private double AxisValue(Bind b)
    {
        double v = b.Kind switch
        {
            BindKind.MouseAxis => b.Code switch { 0 => mouseX, 1 => mouseY, _ => mouseZ },
            BindKind.Axis => input?.Find(b.Device) is { } d && b.Code < d.Axis.Length && !double.IsNaN(d.Axis[b.Code]) ? d.Axis[b.Code] : 0,
            _ => 0,
        };
        v = Math.Clamp(v, -1, 1);
        return b.Inverted ? -v : v;
    }

    private void UpdateExample()
    {
        if (data == null || !example.IsVisible && example.ActualWidth > 0) return;
        var byRole = data.Actions.Where(a => a.Role != null).GroupBy(a => a.Role!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Live? Read(string role)
        {
            if (!byRole.TryGetValue(role, out var a)) return null;
            return LiveOf(a);
        }
        var caps = data.Actions.Where(a => !a.IsAxis && (a.Role == null || !ExampleDevice.Drawn.Contains(a.Role)) && data.Get(a).Any(b => !b.IsNone))
            .OrderBy(a => a.Role == null ? 1 : 0).Take(36).Select(LiveOf).ToList();
        example.Show(data.Def.Kind, Read, caps);
    }

    private Live LiveOf(BinderAction a)
    {
        var binds = data!.Get(a).Where(b => !b.IsNone).ToList();
        double v = 0;
        foreach (var b in binds) { double x = AxisValue(b); if (Math.Abs(x) > Math.Abs(v)) v = x; }
        return new Live(a.Label, binds.Count > 0 ? Text(binds[0]) : "", v, binds.Any(Held), binds.Count > 0);
    }
}
