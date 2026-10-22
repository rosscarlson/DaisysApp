using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// Notepad, a sub-tab of the Tools tab: notes in tabs shared by every PC's Daisy's App, hosted by one of them, over
/// the AI module's remote control (its port and passphrase). Typing here goes to the host, and what the others type
/// comes in at once. Each tab is a <see cref="Sheet"/>: the note here, and the host's latest version of it; what
/// differs between them is sent, one change at a time. A file opened with the app becomes a tab, saved back with Ctrl+S
/// on this PC.
/// </summary>
internal sealed class NotepadView : DockPanel
{
    public const string Channel = "notepad";
    private static readonly string Me = Environment.MachineName;
    /// <summary>Files bigger than this aren't opened (the whole note goes to every PC, and the box gets slow).</summary>
    private const long MaxFile = 1 << 20;

    /// <summary>One tab: its note here, as the host last had it and its version there, and the file it came from here.</summary>
    private sealed class Sheet
    {
        public string Id = "", Name = "";
        public List<Ch> Local = new();
        /// <summary>The host's latest; null: added here and not on the host yet.</summary>
        public List<Ch>? Known;
        public int Version;
        /// <summary>Its "new" sent on this connection (while it isn't on the host yet).</summary>
        public bool Offered;
        public string? Path;
        public int CodePage = 65001;
        public bool Bom, Crlf = true;
        public string Saved = "";
        public int Caret;
        public RadioButton? Tab;
        public TextBlock? Title;
    }

    private readonly NotepadSettings settings;
    private readonly RichTextBox box;
    private readonly Rules rules;
    private readonly ComboBox host;
    private readonly TextBlock status;
    private readonly ToggleButton bold, italic, underline;
    private readonly DispatcherTimer push, save;
    private readonly Button pop, saveFile, add;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal };

    /// <summary>The pop-out button was pressed (NotepadApplet moves the page to its own window, or back).</summary>
    public event Action? PopClicked;

    private List<Sheet> sheets = new();
    private Sheet cur;
    /// <summary>Tabs closed here while the host couldn't be reached (closed there when it's back).</summary>
    private readonly HashSet<string> closedHere = new();
    private int myId, pendingSeq;
    /// <summary>The change sent to the host and not answered yet, and its note.</summary>
    private Change? pending;
    private string pendingNote = "";
    private bool connected, applying, dirty, filling;
    private volatile NoteServer? server;
    private ILink? link;
    private string linkProblem = "", fileProblem = "";
    private IReadOnlyList<string> others = Array.Empty<string>();

    public NotepadView(NotepadSettings settings)
    {
        this.settings = settings;
        foreach (var st in settings.Sheets.Where(st => st.Id.Length > 0))
            sheets.Add(new Sheet
            {
                Id = st.Id, Name = st.Name, Local = NoteText.FromJson(st.Doc), Known = st.Known != null ? NoteText.FromJson(st.Known) : null, Version = st.Version,
                Path = st.Path, CodePage = st.CodePage, Bom = st.Bom, Crlf = st.Crlf, Saved = st.Saved,
            });
        if (sheets.Count == 0) sheets.Add(new Sheet { Id = NotepadSettings.FirstNote });
        cur = sheets.FirstOrDefault(x => x.Id == settings.Selected) ?? sheets[0];
        if (settings.ClientId.Length == 0) settings.ClientId = Guid.NewGuid().ToString("N");
        pending = NoteText.ChangeOf(settings.Pending);
        pendingNote = settings.PendingNote;
        pendingSeq = settings.PendingSeq;

        host = new ComboBox { MinWidth = 160, ToolTip = T("Host") };
        host.SelectionChanged += (_, _) =>
        {
            if (filling || host.SelectedItem is not ComboBoxItem { Tag: string h } || h == settings.Host) return;
            settings.Host = h;
            settings.Save();
            Connect();
        };
        status = Ui.Secondary();
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(10, 0, 0, 0);

        bold = Format("B", T("Bold (Ctrl+B)"), EditingCommands.ToggleBold);
        bold.FontWeight = FontWeights.Bold;
        italic = Format("I", T("Italic (Ctrl+I)"), EditingCommands.ToggleItalic);
        italic.FontStyle = FontStyles.Italic;
        underline = Format("U", T("Underline (Ctrl+U)"), EditingCommands.ToggleUnderline);
        underline.Content = new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline };

        var font = new ComboBox { Width = 170, Margin = new Thickness(10, 0, 0, 0), ToolTip = T("Font"), IsEditable = false };
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) font.Items.Add(f);
        font.SelectedItem = settings.FontFamily;
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is string f) { settings.FontFamily = f; settings.Save(); Look(); } };
        var size = new ComboBox { Width = 64, Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Size") };
        foreach (var s in new double[] { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 36 }) size.Items.Add(s);
        size.SelectedItem = settings.FontSize;
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is double s) { settings.FontSize = s; settings.Save(); Look(); } };
        var wrap = new CheckBox { Content = T("Word wrap"), IsChecked = settings.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        wrap.Click += (_, _) => { settings.Wrap = wrap.IsChecked == true; settings.Save(); Look(); };

        saveFile = new Button { Content = Ui.Icon("", 13), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3), Focusable = false };
        saveFile.Click += (_, _) => SaveFile();
        System.Windows.Automation.AutomationProperties.SetName(saveFile, T("Save"));
        pop = new Button { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3), Focusable = false };
        pop.Click += (_, _) => PopClicked?.Invoke();
        Popped(false);
        var formatting = new StackPanel { Orientation = Orientation.Horizontal, Children = { bold, italic, underline, font, size, wrap, saveFile, pop } };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(formatting, Dock.Right);
        top.Children.Add(formatting);
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { host, status } });
        SetDock(top, Dock.Top);
        Children.Add(top);

        add = new Button { Content = Ui.Icon("", 12), ToolTip = T("New tab (Ctrl+T)"), Padding = new Thickness(10, 4, 10, 6), MinHeight = 30 };
        add.SetResourceReference(StyleProperty, "TabActionButton");
        System.Windows.Automation.AutomationProperties.SetName(add, T("New tab"));
        add.Click += (_, _) => NewTab();
        var strip = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 6) };
        SetDock(strip, Dock.Top);
        Children.Add(strip);

        box = new RichTextBox
        {
            AcceptsReturn = true, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
            Document = new FlowDocument { PagePadding = new Thickness(0) },
        };
        box.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RichTextBox">
              <Border x:Name="Bd" CornerRadius="4" BorderThickness="1" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}">
                <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Stretch"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocused" Value="True">
                  <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        box.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextBrush");
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "AccentBrush");
        box.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        // (copied and cut as plain text only, so it pastes elsewhere with no formatting)
        void CopyPlain(bool cut)
        {
            if (box.Selection.IsEmpty) return;
            try { Clipboard.SetText(box.Selection.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            if (cut) box.Selection.Text = "";
        }
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopyPlain(false), (_, e) => { e.CanExecute = !box.Selection.IsEmpty; e.Handled = true; }));
        box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, _) => CopyPlain(true), (_, e) => { e.CanExecute = !box.Selection.IsEmpty; e.Handled = true; }));
        // (pasted as plain text, as in Notepad)
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is string text) { e.DataObject = new DataObject(DataFormats.UnicodeText, text); e.FormatToApply = DataFormats.UnicodeText; }
            else e.CancelCommand();
        });
        box.TextChanged += (_, _) => Typed();
        box.SelectionChanged += (_, _) => Toggles();
        rules = new Rules(box);
        box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => rules.InvalidateVisual()));
        box.SizeChanged += (_, _) => rules.Redraw();
        Children.Add(new Grid { Children = { box, rules } });

        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S) { SaveFile(); e.Handled = true; }
            else if (e.Key == Key.T) { NewTab(); e.Handled = true; }
        };

        push = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        push.Tick += (_, _) => { push.Stop(); Titles(); Push(); };
        save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        save.Tick += (_, _) => Save();
        save.Start();

        Look();
        BuildTabs();
        Show(null);
        Hosts();
        RemoteChannels.Changed += () => Dispatcher.BeginInvoke(() => { Hosts(); ShowStatus(); });
    }

    /// <summary>The pop-out button for where the page is: in the tab (pop out) or in its own window (back).</summary>
    public void Popped(bool popped)
    {
        var glyph = new TextBlock { Text = popped ? "" : "", FontSize = 13 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        pop.Content = glyph;
        pop.ToolTip = popped ? T("Back in the tab") : T("Pop out");
        System.Windows.Automation.AutomationProperties.SetName(pop, (string)pop.ToolTip);
    }

    private ToggleButton Format(string text, string tip, RoutedUICommand command)
    {
        var t = new ToggleButton { Content = text, ToolTip = tip, Width = 30, Margin = new Thickness(0, 0, 4, 0), Focusable = false };
        t.SetResourceReference(StyleProperty, "ChipToggle");
        t.Click += (_, _) => { command.Execute(null, box); Toggles(); box.Focus(); };
        return t;
    }

    private void Toggles()
    {
        var s = box.Selection;
        bold.IsChecked = s.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() >= 600;
        italic.IsChecked = s.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle st && st == FontStyles.Italic;
        underline.IsChecked = s.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection d && d.Any(x => x.Location == TextDecorationLocation.Underline);
    }

    private void Look()
    {
        try { box.FontFamily = new FontFamily(settings.FontFamily); } catch { }
        box.FontSize = settings.FontSize;
        box.Document.PageWidth = settings.Wrap ? double.NaN : 20000;
        box.HorizontalScrollBarVisibility = settings.Wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        rules.Redraw();
    }

    /// <summary>The host picker: this PC, and the other PCs in Settings → AI → Remote control.</summary>
    private void Hosts()
    {
        filling = true;
        host.Items.Clear();
        host.Items.Add(new ComboBoxItem { Content = T("This PC"), Tag = "" });
        var pcs = RemoteChannels.Pcs?.Invoke().ToList() ?? new List<string>();
        if (settings.Host.Length > 0 && !pcs.Contains(settings.Host, StringComparer.OrdinalIgnoreCase)) pcs.Add(settings.Host);
        foreach (var pc in pcs) host.Items.Add(new ComboBoxItem { Content = pc, Tag = pc });
        host.SelectedItem = host.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ((string)i.Tag).Equals(settings.Host, StringComparison.OrdinalIgnoreCase));
        filling = false;
    }

    // ---- the tabs

    private Sheet? Find(string id) => sheets.FirstOrDefault(s => s.Id == id);

    private void BuildTabs()
    {
        tabs.Children.Clear();
        foreach (var s in sheets) tabs.Children.Add(TabOf(s));
        tabs.Children.Add(add);
        Titles();
    }

    private RadioButton TabOf(Sheet s)
    {
        if (s.Tab is { } made)
        {
            made.IsChecked = s == cur;
            return made;
        }
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Focusable = false };
        // (a tab with something in it closes on a second click, as it closes on every PC)
        DispatcherTimer? back = null;
        void Plain()
        {
            close.ClearValue(StyleProperty);
            close.Content = "✕";
            close.FontSize = 9;
            close.Padding = new Thickness(5, 1, 5, 1);
            close.Background = Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            close.ToolTip = T("Close this tab");
        }
        Plain();
        close.Click += (_, e) =>
        {
            e.Handled = true;
            if (back != null || s.Local.All(c => char.IsWhiteSpace(c.C)))
            {
                back?.Stop();
                back = null;
                Plain();
                CloseTab(s);
                return;
            }
            close.ClearValue(BackgroundProperty);
            close.ClearValue(Control.BorderThicknessProperty);
            close.SetResourceReference(StyleProperty, "DangerButton");
            close.Content = T("Confirm?");
            close.FontSize = 11;
            close.Padding = new Thickness(6, 0, 6, 0);
            back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            back.Tick += (_, _) => { back?.Stop(); back = null; Plain(); };
            back.Start();
        };
        var tab = new RadioButton { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, close } }, Padding = new Thickness(12, 4, 8, 7), MinHeight = 30, Focusable = false };
        tab.SetResourceReference(StyleProperty, "TabButton");
        tab.IsChecked = s == cur;
        tab.Checked += (_, _) => Select(s);
        s.Tab = tab;
        s.Title = title;
        return tab;
    }

    /// <summary>Each tab's title: its name (a file's), or its first line; • when a file's changed since it was saved.</summary>
    private void Titles()
    {
        foreach (var s in sheets)
        {
            if (s.Title == null || s.Tab == null) continue;
            string t = s.Name.Length > 0 ? s.Name : FirstLine(s.Local);
            if (t.Length == 0) t = T("New tab");
            if (s.Path != null && Hash(s.Local) != s.Saved) t = "• " + t;
            s.Title.Text = t;
            s.Tab.ToolTip = s.Path;
            System.Windows.Automation.AutomationProperties.SetName(s.Tab, t);
        }
        // (the close buttons, but not on the last tab)
        foreach (var s in sheets)
            if (s.Tab?.Content is StackPanel { Children: [_, Button close] }) close.Visibility = sheets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        saveFile.Visibility = cur.Path != null ? Visibility.Visible : Visibility.Collapsed;
        saveFile.ToolTip = cur.Path != null ? F("Save to {0} (Ctrl+S)", cur.Path) : null;
    }

    private static string FirstLine(List<Ch> doc)
    {
        var sb = new StringBuilder();
        foreach (var c in doc)
        {
            if (c.C == '\n') { if (sb.ToString().Trim().Length > 0) break; sb.Clear(); continue; }
            if (sb.Length < 60) sb.Append(c.C);
        }
        return sb.ToString().Trim();
    }

    private void Select(Sheet s)
    {
        if (s == cur && s.Tab?.IsChecked == true) return;
        if (box.Document.Blocks.Count > 0) cur.Caret = OffsetOf(box.Selection.Start);
        cur = s;
        settings.Selected = s.Id;
        dirty = true;
        fileProblem = "";
        if (s.Tab != null) s.Tab.IsChecked = true;
        Show(null, s.Caret);
        Titles();
        ShowStatus();
        box.Focus();
    }

    private void AddTab(Sheet s)
    {
        sheets.Add(s);
        BuildTabs();
        Select(s);
        Push();
    }

    private void NewTab() => AddTab(new Sheet { Id = NoteServer.NewId() });

    /// <summary>Closed here, and on every PC (the last tab can't be).</summary>
    private void CloseTab(Sheet s)
    {
        if (sheets.Count <= 1 || !sheets.Contains(s)) return;
        if (s.Known != null || s.Offered)
        {
            if (connected && link != null) link.Post(new JsonObject { ["op"] = "close", ["id"] = s.Id });
            else closedHere.Add(s.Id);
        }
        Forget(s);
        Push();
    }

    private void Forget(Sheet s)
    {
        int i = sheets.IndexOf(s);
        if (i < 0) return;
        sheets.RemoveAt(i);
        dirty = true;
        // (the last one closed on another PC: the host's new one follows, and is shown then)
        if (s == cur && sheets.Count > 0) Select(sheets[Math.Min(i, sheets.Count - 1)]);
        BuildTabs();
    }

    // ---- files

    /// <summary>A file opened with the app: in a new tab (or its tab, if it's open already).</summary>
    public void Open(string path)
    {
        if (sheets.FirstOrDefault(s => s.Path != null && s.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Select(open);
            return;
        }
        var info = new FileInfo(path);
        if (info.Length > MaxFile) throw new IOException(F("{0} is bigger than 1 MB", info.Name));
        var (text, codePage, bom) = Decode(File.ReadAllBytes(path));
        if (text.Contains('\0')) throw new IOException(F("{0} isn't a text file", info.Name));
        bool crlf = text.Contains("\r\n") || !text.Contains('\n');
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var doc = text.Select(c => new Ch(c, Fmt.None, Me)).ToList();
        AddTab(new Sheet { Id = NoteServer.NewId(), Name = info.Name, Local = doc, Path = path, CodePage = codePage, Bom = bom, Crlf = crlf, Saved = Hash(doc) });
        Logging.Log.Here.Info($"Notepad: opened {path} ({info.Length} bytes, code page {codePage})");
    }

    private static (string Text, int CodePage, bool Bom) Decode(byte[] b)
    {
        if (b is [0xEF, 0xBB, 0xBF, ..]) return (Encoding.UTF8.GetString(b, 3, b.Length - 3), 65001, true);
        if (b is [0xFF, 0xFE, ..]) return (Encoding.Unicode.GetString(b, 2, b.Length - 2), 1200, true);
        if (b is [0xFE, 0xFF, ..]) return (Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), 1201, true);
        try { return (new UTF8Encoding(false, true).GetString(b), 65001, false); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding(1252).GetString(b), 1252, false);
        }
    }

    private static Encoding EncodingOf(Sheet s) => s.CodePage switch
    {
        65001 => new UTF8Encoding(s.Bom),
        1200 => new UnicodeEncoding(false, true),
        1201 => new UnicodeEncoding(true, true),
        _ => Encoding.GetEncoding(s.CodePage),
    };

    /// <summary>Ctrl+S: the tab back to the file it came from (on this PC), as it was written (encoding, line ends).</summary>
    private void SaveFile()
    {
        if (cur.Path is not { } path) return;
        string text = new(cur.Local.Select(c => c.C).ToArray());
        if (cur.Crlf) text = text.Replace("\n", "\r\n");
        try
        {
            File.WriteAllText(path, text, EncodingOf(cur));
            cur.Saved = Hash(cur.Local);
            fileProblem = "";
            dirty = true;
            Logging.Log.Here.Info($"Notepad: saved {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            fileProblem = F("Not saved: {0}", ex.Message.TrimEnd('.'));
            Logging.Log.Here.Warn($"Notepad: {path} not saved", ex);
        }
        Titles();
        ShowStatus();
    }

    /// <summary>The text's hash (FNV-1a), to tell whether a file's tab has changed since it was saved.</summary>
    private static string Hash(List<Ch> doc)
    {
        ulong h = 14695981039346656037;
        foreach (var c in doc) { h ^= c.C; h *= 1099511628211; }
        return h.ToString("x16");
    }

    // ---- the connection

    private interface ILink : IDisposable
    {
        void Post(JsonObject m);
    }

    /// <summary>This PC hosts: its notepad joins its own server.</summary>
    private sealed class LocalLink(NotepadView view, NoteServer server) : NoteServer.Peer, ILink
    {
        public override string Name => Me;
        public override void Send(JsonObject m) => view.Dispatcher.BeginInvoke(() => view.Receive(this, m));
        public void Post(JsonObject m) => server.Handle(this, m);
        public void Dispose() => server.Leave(this);
    }

    /// <summary>Another PC hosts: connected to it, and again 5 seconds after it's lost.</summary>
    private sealed class RemoteLink : ILink
    {
        private readonly NotepadView view;
        private readonly string pc;
        private readonly CancellationTokenSource stop = new();
        private volatile IRemoteChannel? channel;

        public RemoteLink(NotepadView view, string pc)
        {
            this.view = view;
            this.pc = pc;
            _ = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            var ct = stop.Token;
            while (!ct.IsCancellationRequested)
            {
                string why;
                try
                {
                    if (RemoteChannels.Connect is not { } connect) throw new InvalidOperationException(T("Needs the AI module"));
                    var c = await connect(pc, Channel, ct);
                    channel = c;
                    c.Post(view.Join());
                    Logging.Log.Here.Info($"Notepad: connected to {pc}");
                    while (await c.ReadAsync(ct) is { } m)
                    {
                        if (NoteText.Str(m, "op") == "error") throw new InvalidOperationException(NoteText.Str(m, "message"));
                        _ = view.Dispatcher.BeginInvoke(() => view.Receive(this, m));
                    }
                    why = T("closed");
                }
                catch (Exception ex) { why = ex.Message.TrimEnd('.'); }
                channel?.Dispose();
                channel = null;
                if (ct.IsCancellationRequested) return;
                Logging.Log.Here.Info($"Notepad: {pc}: {why}");
                _ = view.Dispatcher.BeginInvoke(() => view.Lost(this, why));
                try { await Task.Delay(5000, ct); } catch { return; }
            }
        }

        public void Post(JsonObject m) => channel?.Post(m);

        public void Dispose()
        {
            stop.Cancel();
            channel?.Dispose();
        }
    }

    /// <summary>Joins the host the settings say (from Start, and when it's changed).</summary>
    public void Connect()
    {
        link?.Dispose();
        link = null;
        if (server is { } old)
        {
            old.Close();
            settings.HostNotes = HostNotes(old);
            settings.HostSeqs = old.Seqs();
        }
        server = null;
        connected = false;
        others = Array.Empty<string>();
        linkProblem = "";
        if (settings.Host.Length == 0)
        {
            var s = new NoteServer(settings.HostNotes.Select(n => new NoteServer.Note { Id = n.Id, Name = n.Name, Doc = NoteText.FromJson(n.Doc) }), settings.HostSeqs);
            s.Changed += () => dirty = true;
            s.PeersChanged += () => Dispatcher.BeginInvoke(ShowStatus);
            server = s;
            var l = new LocalLink(this, s);
            link = l;
            l.Post(Join());
        }
        else link = new RemoteLink(this, settings.Host);
        ShowStatus();
    }

    private static List<NotepadSettings.HostNote> HostNotes(NoteServer s) =>
        s.Snapshot().Select(n => new NotepadSettings.HostNote { Id = n.Id, Name = n.Name, Doc = NoteText.ToJson(n.Doc) }).ToList();

    /// <summary>Another PC's notepad asking for the notes (Core's RemoteChannels, on a background thread).</summary>
    public Task ServeAsync(IRemoteChannel channel, CancellationToken ct)
    {
        if (server is { } s) return s.ServeAsync(channel, ct);
        channel.Post(new JsonObject { ["op"] = "error", ["message"] = F("{0} isn't hosting the notepad", Me) });
        return Task.CompletedTask;
    }

    private JsonObject Join() => new() { ["op"] = "join", ["cid"] = settings.ClientId };

    private void Receive(ILink from, JsonObject m)
    {
        if (from != link) return;
        switch (NoteText.Str(m, "op"))
        {
            case "state": State(m); break;
            case "edit":
                {
                    // (3.0.5's host: one note)
                    string id = NoteText.Str(m, "note") is { Length: > 0 } n ? n : NotepadSettings.FirstNote;
                    if (NoteText.Int(m, "id") == myId && id == pendingNote) pending = null;
                    if (Find(id) is not { Known: { } known } s || NoteText.ChangeOf(m["parts"]) is not { } c) break;
                    if (NoteText.Int(m, "id") != myId)
                    {
                        var moved = NoteText.Diff(known, s.Local) is { } mine ? NoteText.Transform(c, mine) : c;
                        s.Local = NoteText.Apply(s.Local, moved);
                        if (s == cur) Show(moved);
                    }
                    s.Known = NoteText.Apply(known, c);
                    s.Version = NoteText.Int(m, "v") ?? s.Version + 1;
                    dirty = true;
                    Titles();
                    Push();
                    break;
                }
            case "new":
                {
                    string id = NoteText.Str(m, "id");
                    if (id.Length == 0 || closedHere.Contains(id)) break;
                    var doc = NoteText.FromJson(m["doc"]);
                    if (Find(id) is not { } s)
                    {
                        s = new Sheet { Id = id, Name = NoteText.Str(m, "name"), Local = doc.ToList() };
                        sheets.Add(s);
                    }
                    // (in the host's order: after the tabs it has, before ones added here it hasn't got yet)
                    sheets.Remove(s);
                    sheets.Insert(sheets.Count(x => x.Known != null), s);
                    // (one added here: what's typed in it since goes as a change)
                    s.Known = doc;
                    s.Version = NoteText.Int(m, "v") ?? 0;
                    BuildTabs();
                    if (!sheets.Contains(cur)) Select(s);
                    dirty = true;
                    Push();
                    break;
                }
            case "close":
                {
                    string id = NoteText.Str(m, "id");
                    if (id == pendingNote) pending = null;
                    if (Find(id) is { } s) Forget(s);
                    Push();
                    break;
                }
            case "stale":
                pending = null;
                Push();
                break;
            case "who":
                others = (m["pcs"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0 && !n.Equals(Me, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<string>();
                ShowStatus();
                break;
        }
    }

    /// <summary>The host's notes on joining: what changed there and what was typed here while apart both kept, tabs added
    /// or closed there followed, and tabs added or closed here while apart sent.</summary>
    private void State(JsonObject m)
    {
        myId = NoteText.Int(m, "you") ?? 0;
        bool same = settings.KnownHost == settings.Host;
        var notes = m["notes"] is JsonArray all
            ? all.OfType<JsonObject>().Select(n => (Id: NoteText.Str(n, "id"), Name: NoteText.Str(n, "name"), V: NoteText.Int(n, "v") ?? 0, Doc: NoteText.FromJson(n["doc"]))).Where(n => n.Id.Length > 0).ToList()
            // (3.0.5's host: one note)
            : new() { (NotepadSettings.FirstNote, "", NoteText.Int(m, "v") ?? 0, NoteText.FromJson(m["doc"])) };
        // (the change on its way when the connection went: in already, or still to send)
        if (pending != null && same && Find(pendingNote) is { Known: { } k } ps && k.Count == pending.From && (NoteText.Int(m, "seq") ?? -1) >= pendingSeq)
            ps.Known = NoteText.Apply(k, pending);
        pending = null;
        // (closed here while apart: closed there now, unless that would leave none)
        if (notes.All(n => closedHere.Contains(n.Id))) closedHere.Clear();
        foreach (var id in closedHere.Where(id => notes.Any(n => n.Id == id))) link?.Post(new JsonObject { ["op"] = "close", ["id"] = id });

        var next = new List<Sheet>();
        Change? curMoved = null;
        bool curNew = false;
        foreach (var h in notes)
        {
            if (closedHere.Contains(h.Id)) continue;
            var s = Find(h.Id);
            if (s == null) s = new Sheet { Id = h.Id, Local = h.Doc.ToList() };
            // (added here, and its "new" got there: what's typed since goes as a change)
            else if (s.Known == null) { }
            else if (same && NoteText.Diff(s.Known, s.Local) is { } mine)
            {
                if (NoteText.Diff(s.Known, h.Doc) is { } theirs)
                {
                    var moved = NoteText.Transform(theirs, mine);
                    s.Local = NoteText.Apply(s.Local, moved);
                    if (s == cur) curMoved = moved;
                }
            }
            else
            {
                s.Local = h.Doc.ToList();
                if (s == cur) curNew = true;
            }
            s.Known = h.Doc;
            s.Version = h.V;
            s.Name = h.Name;
            next.Add(s);
        }
        closedHere.Clear();
        // (added here and not there yet: they go to it; an empty one isn't worth a tab)
        foreach (var s in sheets)
            if (s.Known == null && !next.Contains(s) && (s.Local.Count > 0 || s.Path != null)) { s.Offered = false; next.Add(s); }
        sheets = next;
        settings.KnownHost = settings.Host;
        connected = true;
        linkProblem = "";
        dirty = true;
        BuildTabs();
        if (!sheets.Contains(cur)) Select(sheets[0]);
        else if (curNew) Show(null);
        else if (curMoved != null) Show(curMoved);
        ShowStatus();
        Push();
    }

    private void Lost(ILink from, string why)
    {
        if (from != link) return;
        connected = false;
        others = Array.Empty<string>();
        linkProblem = why;
        ShowStatus();
    }

    /// <summary>Sends tabs added here, then what's changed in a tab since the host's latest, if nothing is on its way already.</summary>
    private void Push()
    {
        if (!connected || link == null) return;
        foreach (var s in sheets.Where(s => s.Known == null && !s.Offered))
        {
            s.Offered = true;
            link.Post(new JsonObject { ["op"] = "new", ["id"] = s.Id, ["name"] = s.Name, ["doc"] = NoteText.ToJson(s.Local) });
        }
        if (pending != null) return;
        foreach (var s in sheets.OrderBy(s => s == cur ? 0 : 1))
        {
            if (s.Known == null || NoteText.Diff(s.Known, s.Local) is not { } c) continue;
            var m = new JsonObject { ["op"] = "edit", ["note"] = s.Id, ["base"] = s.Version, ["seq"] = pendingSeq = ++settings.Seq, ["parts"] = NoteText.ToJson(c) };
            pending = c;
            pendingNote = s.Id;
            dirty = true;
            link.Post(m);
            return;
        }
    }

    private void ShowStatus()
    {
        string text;
        bool bad = false;
        if (fileProblem.Length > 0) { text = fileProblem; bad = true; }
        else if (settings.Host.Length == 0)
        {
            if (RemoteChannels.Serving == null) { text = T("Not shared: needs the AI module"); bad = true; }
            else if (RemoteChannels.Serving() != true) { text = T("Not shared: AI remote control is off"); bad = true; }
            else text = others.Count == 0 ? T("Hosting") : F("Hosting · {0}", string.Join(", ", others));
        }
        else if (connected) text = T("Connected");
        else if (linkProblem.Length > 0) { text = linkProblem; bad = true; }
        else text = T("Connecting…");
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    // ---- the box

    /// <summary>Something typed (or undone, pasted, formatted) here.</summary>
    private void Typed()
    {
        if (applying) return;
        rules.Redraw();
        var now = Read();
        if (NoteText.Diff(cur.Local, now, authors: false) is not { } typed) return;
        // (what's put in is this PC's)
        var mine = new Change();
        foreach (var p in typed.Parts)
            if (p.Put != null) mine.Insert(p.Put.Select(c => c with { A = Me }).ToList());
            else if (p.Keep > 0) mine.Retain(p.Keep);
            else mine.Delete(p.Remove);
        cur.Local = NoteText.Apply(cur.Local, mine);
        dirty = true;
        push.Stop();
        push.Start();
    }

    /// <summary>The tab's note drawn again (another PC's change came in, or another tab is shown), the caret moved along
    /// with the change, or put at <paramref name="caret"/>.</summary>
    private void Show(Change? moved, int? caret = null)
    {
        int a = 0, b = 0;
        double scroll = caret != null ? 0 : box.VerticalOffset;
        if (caret is int at) a = b = Math.Min(at, cur.Local.Count);
        else if (box.Document.Blocks.Count > 0)
        {
            a = OffsetOf(box.Selection.Start);
            b = OffsetOf(box.Selection.End);
            if (moved != null) { a = NoteText.Map(a, moved); b = NoteText.Map(b, moved); }
        }
        var local = cur.Local;
        applying = true;
        try
        {
            var blocks = box.Document.Blocks;
            blocks.Clear();
            var p = new Paragraph();
            int i = 0;
            while (i < local.Count)
            {
                if (local[i].C == '\n') { blocks.Add(p); p = new Paragraph(); i++; continue; }
                int j = i;
                var sb = new StringBuilder();
                while (j < local.Count && local[j].C != '\n' && local[j].F == local[i].F && local[j].A == local[i].A) sb.Append(local[j++].C);
                var run = new Run(sb.ToString());
                var f = local[i].F;
                if (f.HasFlag(Fmt.Bold)) run.FontWeight = FontWeights.Bold;
                if (f.HasFlag(Fmt.Italic)) run.FontStyle = FontStyles.Italic;
                if (f.HasFlag(Fmt.Underline)) run.TextDecorations = TextDecorations.Underline;
                p.Inlines.Add(run);
                i = j;
            }
            blocks.Add(p);
            // (undo can't go back past another PC's change)
            box.IsUndoEnabled = false;
            box.IsUndoEnabled = true;
            box.Selection.Select(PointerAt(Math.Min(a, b)), PointerAt(Math.Max(a, b)));
        }
        finally { applying = false; }
        box.UpdateLayout();
        if (caret != null) box.Selection.Start.Paragraph?.BringIntoView();
        else box.ScrollToVerticalOffset(scroll);
        rules.Redraw();
    }

    /// <summary>
    /// Lines of nothing but dashes (three or more) drawn as a line across the page, over the box: the note keeps the
    /// dashes, so it's the same on every PC, and the line goes through them.
    /// </summary>
    private sealed class Rules : FrameworkElement
    {
        private readonly RichTextBox box;
        private bool queued;

        public Rules(RichTextBox box)
        {
            this.box = box;
            IsHitTestVisible = false;
        }

        /// <summary>Drawn again once the box's layout is done.</summary>
        public void Redraw()
        {
            if (queued) return;
            queued = true;
            Dispatcher.BeginInvoke(() => { queued = false; InvalidateVisual(); }, DispatcherPriority.Loaded);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (box.Template?.FindName("PART_ContentHost", box) is not ScrollViewer sv || sv.ViewportWidth <= 0) return;
            var origin = sv.TranslatePoint(new Point(0, 0), this);
            dc.PushClip(new RectangleGeometry(new Rect(origin, new Size(sv.ViewportWidth, sv.ViewportHeight))));
            // (through the middle of the dashes, as thick as they are)
            var dash = new FormattedText("-", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), box.FontSize, box.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                .BuildGeometry(new Point(0, 0)).Bounds;
            if (dash.IsEmpty) dash = new Rect(0, box.FontSize * 0.6, box.FontSize / 2, 1);
            double thick = Math.Max(1, Math.Ceiling(dash.Height - 0.2));
            double right = origin.X + sv.ViewportWidth;
            foreach (var p in Paragraphs(box.Document.Blocks))
            {
                if (!IsRule(p)) continue;
                var at = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (at.IsEmpty) continue;
                var left = box.TranslatePoint(at.TopLeft, this);
                // (on whole pixels, as the text is)
                double y = Math.Round(left.Y + dash.Top);
                if (y < origin.Y - 10 || y > origin.Y + sv.ViewportHeight + 10 || right <= left.X) continue;
                dc.PushGuidelineSet(new GuidelineSet(null, [y, y + thick]));
                dc.DrawRectangle(box.Foreground, null, new Rect(left.X, y, right - left.X, thick));
                dc.Pop();
            }
            dc.Pop();
        }

        private static bool IsRule(Paragraph p)
        {
            int dashes = 0;
            foreach (var piece in Pieces(p.Inlines))
            {
                if (piece is not Run run) return false;
                foreach (char c in run.Text)
                    if (c == '-') dashes++;
                    else if (c != ' ' && c != '\t') return false;
            }
            return dashes >= 3;
        }
    }

    /// <summary>The box's paragraphs (inside lists and tables too), in order.</summary>
    private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
            switch (b)
            {
                case Paragraph p: yield return p; break;
                case Section s: foreach (var p in Paragraphs(s.Blocks)) yield return p; break;
                case List l: foreach (var item in l.ListItems) foreach (var p in Paragraphs(item.Blocks)) yield return p; break;
                case Table t:
                    foreach (var g in t.RowGroups) foreach (var r in g.Rows) foreach (var c in r.Cells) foreach (var p in Paragraphs(c.Blocks)) yield return p;
                    break;
            }
    }

    /// <summary>A paragraph's runs and line breaks, in order (inside bold, italic… spans too).</summary>
    private static IEnumerable<Inline> Pieces(IEnumerable<Inline> inlines)
    {
        foreach (var i in inlines)
            switch (i)
            {
                case Run or LineBreak: yield return i; break;
                case Span s: foreach (var x in Pieces(s.Inlines)) yield return x; break;
            }
    }

    private static Fmt FmtOf(Run r)
    {
        var f = Fmt.None;
        if (r.FontWeight.ToOpenTypeWeight() >= 600) f |= Fmt.Bold;
        if (r.FontStyle == FontStyles.Italic) f |= Fmt.Italic;
        for (DependencyObject? e = r; e is Inline i; e = i.Parent)
            if (i.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true) { f |= Fmt.Underline; break; }
        return f;
    }

    /// <summary>What the box holds: its characters and their formatting (whose they are comes from the tab's note).</summary>
    private List<Ch> Read()
    {
        var r = new List<Ch>();
        bool first = true;
        foreach (var p in Paragraphs(box.Document.Blocks))
        {
            if (!first) r.Add(new Ch('\n', Fmt.None, ""));
            first = false;
            foreach (var piece in Pieces(p.Inlines))
                if (piece is Run run) { var f = FmtOf(run); foreach (char c in run.Text) r.Add(new Ch(c == '\r' ? '\n' : c, f, "")); }
                else r.Add(new Ch('\n', Fmt.None, ""));
        }
        return r;
    }

    private int OffsetOf(TextPointer at)
    {
        int n = 0;
        bool first = true;
        foreach (var p in Paragraphs(box.Document.Blocks))
        {
            if (!first) n++;
            first = false;
            if (at.CompareTo(p.ContentStart) <= 0) return n;
            foreach (var piece in Pieces(p.Inlines))
            {
                if (piece is Run run)
                {
                    if (at.CompareTo(run.ContentStart) <= 0) return n;
                    if (at.CompareTo(run.ContentEnd) <= 0) return n + run.ContentStart.GetOffsetToPosition(at);
                    n += run.Text.Length;
                }
                else
                {
                    if (at.CompareTo(piece.ElementStart) <= 0) return n;
                    n++;
                }
            }
            if (at.CompareTo(p.ContentEnd) <= 0) return n;
        }
        return n;
    }

    private TextPointer PointerAt(int offset)
    {
        int n = 0;
        bool first = true;
        foreach (var p in Paragraphs(box.Document.Blocks))
        {
            if (!first) n++;
            first = false;
            var pieces = Pieces(p.Inlines).ToList();
            int length = pieces.Sum(x => x is Run r ? r.Text.Length : 1);
            if (offset > n + length) { n += length; continue; }
            foreach (var piece in pieces)
            {
                if (piece is Run run)
                {
                    if (offset <= n + run.Text.Length) return run.ContentStart.GetPositionAtOffset(offset - n) ?? run.ContentEnd;
                    n += run.Text.Length;
                }
                else
                {
                    if (offset <= n) return piece.ElementStart;
                    n++;
                }
            }
            return p.ContentEnd;
        }
        return box.Document.ContentEnd;
    }

    public void Save()
    {
        if (!dirty) return;
        dirty = false;
        settings.Sheets = sheets.Select(s => new NotepadSettings.SheetState
        {
            Id = s.Id, Name = s.Name, Doc = NoteText.ToJson(s.Local), Known = s.Known != null ? NoteText.ToJson(s.Known) : null, Version = s.Version,
            Path = s.Path, CodePage = s.CodePage, Bom = s.Bom, Crlf = s.Crlf, Saved = s.Saved,
        }).ToList();
        settings.Selected = cur.Id;
        settings.Pending = pending != null ? NoteText.ToJson(pending) : null;
        settings.PendingNote = pendingNote;
        settings.PendingSeq = pendingSeq;
        if (server is { } s)
        {
            settings.HostNotes = HostNotes(s);
            settings.HostSeqs = s.Seqs();
        }
        settings.Save();
    }

    public void Close()
    {
        save.Stop();
        link?.Dispose();
        link = null;
        server?.Close();
        dirty = true;
        Save();
        server = null;
    }
}
