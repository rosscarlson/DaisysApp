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
/// Notepad, a sub-tab of the Tools tab: one note shared by every PC's Daisy's App, hosted by one of them, over the AI
/// module's remote control (its port and passphrase). Typing here goes to the host, and what the others type comes
/// in at once. The note here is the box's text (<see cref="local"/>); <see cref="known"/> is the
/// host's latest version, and what differs between them is sent, one change at a time.
/// </summary>
internal sealed class NotepadView : DockPanel
{
    public const string Channel = "notepad";
    private static readonly string Me = Environment.MachineName;

    private readonly NotepadSettings settings;
    private readonly RichTextBox box;
    private readonly ComboBox host;
    private readonly TextBlock status;
    private readonly ToggleButton bold, italic, underline;
    private readonly DispatcherTimer push, save;
    private readonly Button pop;

    /// <summary>The pop-out button was pressed (NotepadApplet moves the page to its own window, or back).</summary>
    public event Action? PopClicked;

    private List<Ch> local;
    private List<Ch>? known;
    private int knownVersion, myId, pendingSeq;
    /// <summary>The change sent to the host and not answered yet.</summary>
    private Change? pending;
    private bool connected, applying, dirty, filling;
    private volatile NoteServer? server;
    private ILink? link;
    private string linkProblem = "";
    private IReadOnlyList<string> others = Array.Empty<string>();

    public NotepadView(NotepadSettings settings)
    {
        this.settings = settings;
        local = NoteText.FromJson(settings.Doc);
        if (settings.KnownHost != null) known = NoteText.FromJson(settings.Known);
        if (settings.ClientId.Length == 0) settings.ClientId = Guid.NewGuid().ToString("N");
        pending = NoteText.ChangeOf(settings.Pending);
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

        pop = new Button { Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3), Focusable = false };
        pop.Click += (_, _) => PopClicked?.Invoke();
        Popped(false);
        var formatting = new StackPanel { Orientation = Orientation.Horizontal, Children = { bold, italic, underline, font, size, wrap, pop } };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(formatting, Dock.Right);
        top.Children.Add(formatting);
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { host, status } });
        SetDock(top, Dock.Top);
        Children.Add(top);

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
        Children.Add(box);

        push = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        push.Tick += (_, _) => { push.Stop(); Push(); };
        save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        save.Tick += (_, _) => Save();
        save.Start();

        Look();
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
            settings.HostDoc = NoteText.ToJson(old.Snapshot());
            settings.HostSeqs = old.Seqs();
        }
        server = null;
        connected = false;
        others = Array.Empty<string>();
        linkProblem = "";
        if (settings.Host.Length == 0)
        {
            var s = new NoteServer(NoteText.FromJson(settings.HostDoc), settings.HostSeqs);
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

    /// <summary>Another PC's notepad asking for the note (Core's RemoteChannels, on a background thread).</summary>
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
            case "state":
                {
                    var state = NoteText.FromJson(m["doc"]);
                    myId = NoteText.Int(m, "you") ?? 0;
                    knownVersion = NoteText.Int(m, "v") ?? 0;
                    // (the change on its way when the connection went: in already, or still to send)
                    if (pending != null && known != null && settings.KnownHost == settings.Host && (NoteText.Int(m, "seq") ?? -1) >= pendingSeq)
                        known = NoteText.Apply(known, pending);
                    pending = null;
                    // (what changed there and what was typed here while apart, both kept)
                    if (known != null && settings.KnownHost == settings.Host && NoteText.Diff(known, local) is { } mine)
                    {
                        if (NoteText.Diff(known, state) is { } theirs)
                        {
                            var moved = NoteText.Transform(theirs, mine);
                            local = NoteText.Apply(local, moved);
                            Show(moved);
                        }
                    }
                    else
                    {
                        local = state.ToList();
                        Show(null);
                    }
                    known = state;
                    settings.KnownHost = settings.Host;
                    connected = true;
                    linkProblem = "";
                    dirty = true;
                    ShowStatus();
                    Push();
                    break;
                }
            case "edit":
                {
                    if (known == null || NoteText.ChangeOf(m["parts"]) is not { } c) break;
                    if (NoteText.Int(m, "id") == myId) pending = null;
                    else
                    {
                        var moved = NoteText.Diff(known, local) is { } mine ? NoteText.Transform(c, mine) : c;
                        local = NoteText.Apply(local, moved);
                        Show(moved);
                    }
                    known = NoteText.Apply(known, c);
                    knownVersion = NoteText.Int(m, "v") ?? knownVersion + 1;
                    dirty = true;
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

    private void Lost(ILink from, string why)
    {
        if (from != link) return;
        connected = false;
        others = Array.Empty<string>();
        linkProblem = why;
        ShowStatus();
    }

    /// <summary>Sends what's changed here since the host's latest, if nothing is on its way already.</summary>
    private void Push()
    {
        if (!connected || pending != null || known == null || link == null) return;
        if (NoteText.Diff(known, local) is not { } c) return;
        var m = new JsonObject { ["op"] = "edit", ["base"] = knownVersion, ["seq"] = pendingSeq = ++settings.Seq, ["parts"] = NoteText.ToJson(c) };
        pending = c;
        dirty = true;
        link.Post(m);
    }

    private void ShowStatus()
    {
        string text;
        bool bad = false;
        if (settings.Host.Length == 0)
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
        var now = Read();
        if (NoteText.Diff(local, now, authors: false) is not { } typed) return;
        // (what's put in is this PC's)
        var mine = new Change();
        foreach (var p in typed.Parts)
            if (p.Put != null) mine.Insert(p.Put.Select(c => c with { A = Me }).ToList());
            else if (p.Keep > 0) mine.Retain(p.Keep);
            else mine.Delete(p.Remove);
        local = NoteText.Apply(local, mine);
        dirty = true;
        push.Stop();
        push.Start();
    }

    /// <summary>The note drawn again (another PC's change came in), the caret moved along with it.</summary>
    private void Show(Change? moved)
    {
        int a = 0, b = 0;
        double scroll = box.VerticalOffset;
        if (box.Document.Blocks.Count > 0)
        {
            a = OffsetOf(box.Selection.Start);
            b = OffsetOf(box.Selection.End);
            if (moved != null) { a = NoteText.Map(a, moved); b = NoteText.Map(b, moved); }
        }
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
                var sb = new System.Text.StringBuilder();
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
        box.ScrollToVerticalOffset(scroll);
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

    /// <summary>What the box holds: its characters and their formatting (whose they are comes from <see cref="local"/>).</summary>
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
        settings.Doc = NoteText.ToJson(local);
        settings.Known = known != null ? NoteText.ToJson(known) : new JsonArray();
        if (known == null) settings.KnownHost = null;
        settings.Pending = pending != null ? NoteText.ToJson(pending) : null;
        settings.PendingSeq = pendingSeq;
        if (server is { } s)
        {
            settings.HostDoc = NoteText.ToJson(s.Snapshot());
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
