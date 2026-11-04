using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Logging;
using DaisysApp.Shared.Charts;

namespace DaisysApp.Applets.Support.ProcessAnalyzer;

/// <summary>
/// The right of the Process Analyzer: one program's details. What it is (the database's entry: title, publisher,
/// category, threat, whether it's needed, how heavy it should be, where it came from), what was found, its use now,
/// its history (Hardware → Performance's log, this PC's own figures, the server's average), its file (path, version,
/// signature, hash, command line, parent, what starts it), and buttons: Edit, Fine Here, look it up, open its folder,
/// end it. Built again when the selection or its entry changes; the figures now are updated in place.
/// </summary>
internal sealed class ProcessDetails : ScrollViewer
{
    private readonly ProcessAnalyzerApplet applet;
    private readonly Func<List<Assessment>> current;
    private readonly Action refresh;
    private readonly StackPanel panel = new() { Margin = new Thickness(0, 0, 8, 0) };
    private TextBlock? nowText;
    private DateTime shownUpdated;
    private Threat shownThreat;
    private int shownFindings;
    private bool editing;
    private string? hash;
    private string problem = "";

    /// <summary>The program showing (its name), or null.</summary>
    public string? Showing { get; private set; }

    public ProcessDetails(ProcessAnalyzerApplet applet, Func<List<Assessment>> current, Action refresh)
    {
        this.applet = applet;
        this.current = current;
        this.refresh = refresh;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = panel;
    }

    private Assessment? Now => Showing == null ? null : current().FirstOrDefault(a => a.Name == Showing);

    public void Show(ProgramRow? row)
    {
        if (row == null || row.Name == Showing) return;
        Showing = row.Name;
        editing = false;
        hash = null;
        problem = "";
        Build();
        ScrollToTop();
    }

    /// <summary>After a look: the figures now, or everything if its entry or findings changed.</summary>
    public void Update()
    {
        var a = Now;
        if (a == null || editing) return;
        if (a.Entry.Updated != shownUpdated || a.Threat != shownThreat || a.Findings.Count != shownFindings) { Build(); return; }
        if (nowText != null) nowText.Text = NowText(a);
    }

    private void Build()
    {
        panel.Children.Clear();
        nowText = null;
        var a = Now;
        if (a == null) { panel.Children.Add(Ui.NoteCentred(T("Pick a program"))); return; }
        var e = a.Entry;
        shownUpdated = e.Updated;
        shownThreat = a.Threat;
        shownFindings = a.Findings.Count;
        var f = a.Facts;

        // the title, its threat, the suggestion
        var title = new TextBlock { Text = e.Title.Length > 0 ? e.Title : a.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(title);
        var sub = Ui.Secondary($"{a.Name}.exe · {ProgramRow.CategoryText(e.Category)}");
        panel.Children.Add(sub);
        var threat = new TextBlock { Text = F("Threat: {0}", ProgramRow.Text(a.Threat)), FontWeight = FontWeights.SemiBold, Foreground = ProgramRow.BrushOf(a.Threat), Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(threat);
        if (a.Suggestion.Length > 0) panel.Children.Add(new TextBlock { Text = a.Suggestion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Foreground = ProgramRow.BrushOf((Threat)Math.Max((int)a.Threat, (int)Threat.Medium)) });
        bool ignored = applet.Settings.Ignored.Contains(a.Name, StringComparer.OrdinalIgnoreCase);
        if (ignored) panel.Children.Add(Ui.Secondary(T("Marked fine here: no suggestions for it")));

        panel.Children.Add(Buttons(a, ignored));
        if (problem.Length > 0) panel.Children.Add(new TextBlock { Text = problem, TextWrapping = TextWrapping.Wrap, Foreground = ProgramRow.BrushOf(Threat.High) });
        if (editing) { panel.Children.Add(Editor(a)); return; }

        // what it is
        var what = Table();
        Line(what, T("Publisher"), e.Publisher);
        Line(what, T("Needed"), ProgramRow.Text(e.Necessity));
        var (cpuLimit, memLimit) = Classifier.Limit(e.Resource);
        Line(what, T("Expected use"), F("{0} (up to {1:0}% CPU, {2})", ProgramRow.Text(e.Resource), cpuLimit, Sizes.Bytes((long)(memLimit * 1048576))));
        Line(what, T("About it"), e.Description);
        Line(what, T("Classified"), Classified(e));
        if (e.VtChecked is DateTime vt)
            Line(what, T("VirusTotal"), e.VtEngines is > 0 ? F("{0} of {1} engines flag it ({2:d})", e.VtMalicious ?? 0, e.VtEngines, vt.ToLocalTime()) : F("Not known to VirusTotal ({0:d})", vt.ToLocalTime()));
        panel.Children.Add(Card(T("What It Is"), what));

        // what was found
        var found = new StackPanel();
        if (a.Findings.Count == 0) found.Children.Add(Ui.Secondary(T("Nothing found")));
        foreach (var (level, text) in a.Findings)
            found.Children.Add(new TextBlock { Text = "● " + text, TextWrapping = TextWrapping.Wrap, Foreground = ProgramRow.BrushOf(level), Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(Card(T("Findings"), found));

        // now
        nowText = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = NowText(a) };
        var nowPanel = new StackPanel { Children = { nowText } };
        var net = Network(a);
        if (net.Length > 0) nowPanel.Children.Add(new TextBlock { Text = net, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(Card(T("Now"), nowPanel));

        // history
        panel.Children.Add(Card(T("History"), History(a)));

        // its file
        var file = Table();
        var first = a.Procs.FirstOrDefault(p => p.Path != null);
        Line(file, T("Path"), f?.Path ?? first?.Path ?? T("Can't be read (protected, or another user's)"), copy: true);
        if (f != null)
        {
            Line(file, T("Signature"), f.Sign switch
            {
                SignState.Signed => F("Signed by {0}", f.Signer),
                SignState.Catalog => F("Signed by {0} (Windows catalog)", f.Signer),
                SignState.Unsigned => T("Not signed"),
                SignState.Untrusted => F("Not valid ({0})", f.Signer.Length > 0 ? f.Signer : T("unknown signer")),
                _ => T("Couldn't be checked"),
            });
            Line(file, T("Company"), f.Company);
            Line(file, T("Product"), f.Product);
            Line(file, T("Description"), f.Description);
            Line(file, T("Version"), f.Version);
            if (f.OriginalName.Length > 0 && !f.OriginalName.Equals(a.Name + ".exe", StringComparison.OrdinalIgnoreCase)) Line(file, T("Original name"), f.OriginalName);
            Line(file, T("Size"), Sizes.Bytes(f.Size));
            Line(file, T("Modified"), f.Modified.ToString("g"));
        }
        if (first != null)
        {
            Line(file, T("Command line"), Scanner.CommandLine(first.Pid) ?? "", copy: true);
            var all = current().SelectMany(x => x.Procs).ToList();
            var parent = all.FirstOrDefault(p => p.Pid == first.ParentPid);
            Line(file, T("Started by"), parent != null ? $"{parent.Exe} ({parent.Pid})" : first.ParentPid > 0 ? F("process {0} (ended)", first.ParentPid) : "");
        }
        var starts = Scanner.Autostarts(a.Name, first?.Path, a.Procs.Select(p => p.Pid));
        Line(file, T("Starts by itself"), starts.Count == 0 ? T("No (no service or sign-in entry)") : string.Join("\n", starts));
        if (hash != null) Line(file, "SHA-256", hash, copy: true);
        panel.Children.Add(Card(T("Its File"), file));
    }

    private static string Classified(KnownProcess e)
    {
        string who = e.Source switch
        {
            "seed" => T("from the list Daisy's App comes with"),
            "user" => F("by hand on {0}", e.By),
            "server" => e.Reviewed ? F("from Daisy's Server (by {0})", e.By) : F("from Daisy's Server (automatically, on {0})", e.By),
            "import" => F("imported (from {0})", e.By),
            _ => e.Reviewed ? F("by {0}", e.By) : F("automatically, on {0}", e.By),
        };
        return $"{who}, {e.Updated.ToLocalTime():g}" + (e.Rev > 0 ? " · " + F("server version {0}", e.Rev) : "");
    }

    private static string NowText(Assessment a)
    {
        var started = a.Procs.Where(p => p.Started != null).Select(p => p.Started!.Value).DefaultIfEmpty().Min();
        string s = F("{0} processes · {1:0.0}% CPU · {2} memory · {3} threads · {4} handles", a.Procs.Count, a.Cpu, Sizes.Bytes((long)(a.MemoryMB * 1048576)),
            a.Procs.Sum(p => p.Threads), a.Procs.Sum(p => p.Handles));
        if (started != default) s += "\n" + F("Running since {0:g}", started);
        s += "\n" + F("Process IDs: {0}", string.Join(", ", a.Procs.Select(p => p.Pid).OrderBy(p => p).Take(20)) + (a.Procs.Count > 20 ? "…" : ""));
        if (a.OverNow) s += "\n" + T("Over what's expected of it now");
        return s;
    }

    private static string Network(Assessment a)
    {
        try
        {
            var net = Scanner.Connections();
            var ports = new SortedSet<int>();
            int connected = 0;
            foreach (var p in a.Procs)
                if (net.TryGetValue(p.Pid, out var c)) { ports.UnionWith(c.Listening); connected += c.Connected; }
            if (ports.Count == 0 && connected == 0) return T("No network connections");
            return F("{0} connections open", connected) + (ports.Count > 0 ? " · " + F("listening on {0}", string.Join(", ", ports.Take(12))) : "");
        }
        catch { return ""; }
    }

    private UIElement History(Assessment a)
    {
        var box = new StackPanel();
        int days = applet.Settings.HistoryDays;
        var all = PerfHistory.Read(days);
        var h = all.GetValueOrDefault(a.Name);
        if (!PerfHistory.Exists) box.Children.Add(Ui.Secondary(T("No Performance log (Hardware → Performance logs the busiest programs)"), wrap: true));
        else if (h == null) box.Children.Add(Ui.Secondary(F("Never among the 5 busiest in the last {0} days", days), wrap: true));
        else
        {
            box.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = F("Among the 5 busiest by CPU {0} of the time over {1} days: {2:0.#}% on average then, {3:0.#}% at most", Sizes.Percent(h.TopCpuShare), days, h.AvgCpuWhenTop, h.PeakCpu)
                    + (h.TopMemoryRows > 0 ? "\n" + F("Among the 5 biggest {0} of the time: {1} on average then, {2} at most", Sizes.Percent(h.TopMemoryRows / (double)Math.Max(1, h.Rows)),
                        Sizes.Bytes((long)(h.AvgMemoryWhenTop * 1048576)), Sizes.Bytes((long)(h.PeakMemoryMB * 1048576))) : ""),
            });
            if (a.OverHistory) box.Children.Add(new TextBlock { Text = T("Busier than expected of it"), Foreground = ProgramRow.BrushOf(Threat.High), Margin = new Thickness(0, 2, 0, 0) });
            if (h.Cpu.Count > 1)
            {
                var graph = new LineGraph { Height = 130, Detailed = true, Margin = new Thickness(0, 8, 0, 0) };
                var (cpuLimit, _) = Classifier.Limit(a.Entry.Resource);
                graph.Levels = [(cpuLimit, "#FB8C00")];
                graph.Show([new GraphSeries { Name = T("CPU %"), Points = h.Cpu, Metric = Networking.PercentUnit.Instance }],
                    DateTime.Now.AddDays(-days), DateTime.Now, null, TimeSpan.FromSeconds(30));
                box.Children.Add(graph);
            }
        }
        if (ProcessDb.Db.SeenOf(a.Name) is { Samples: > 0 } o)
            box.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                Text = F("Seen here since {0:d}: {1:0.0}% CPU on average, {2:0.#}% at most; {3} memory on average, {4} at most", o.FirstSeen, o.AvgCpu, o.PeakCpu,
                    Sizes.Bytes((long)(o.AvgMemoryMB * 1048576)), Sizes.Bytes((long)(o.PeakMemoryMB * 1048576))),
            });
        if (a.Entry.FleetPcs is int pcs && pcs > 0)
            box.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                Text = F("On {0} PCs (Daisy's Server): {1:0.0}% CPU and {2} memory on average", pcs, a.Entry.FleetCpu ?? 0, Sizes.Bytes((long)((a.Entry.FleetMemoryMB ?? 0) * 1048576))),
            });
        return box;
    }

    // ---------------------------------------------------------------- buttons

    private UIElement Buttons(Assessment a, bool ignored)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        void Add(UIElement b) { if (b is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 6); wrap.Children.Add(b); }
        var path = a.Facts?.Path ?? a.Procs.FirstOrDefault(p => p.Path != null)?.Path;

        Add(Ui.IconButton("", T("Edit"), T("Classify it yourself (shared with Daisy's Server)"), () => { editing = true; Build(); }));
        Add(Ui.IconButton(ignored ? "" : "", ignored ? T("Not Fine") : T("Fine Here"), ignored ? T("Show its suggestions again") : T("No suggestions for it on this PC"), () =>
        {
            var list = applet.Settings.Ignored;
            if (ignored) list.RemoveAll(n => n.Equals(a.Name, StringComparison.OrdinalIgnoreCase)); else list.Add(a.Name);
            applet.Settings.SaveSoon();
            refresh();
        }));
        if (path != null)
        {
            bool key = applet.Settings.VirusTotalKey.Length > 0;
            Add(Ui.IconButton("", T("VirusTotal"), key ? T("Look its hash up on VirusTotal (only the hash is sent)") : T("Open its hash's page on VirusTotal"), async () =>
            {
                try
                {
                    if (key)
                    {
                        problem = "";
                        await Lookup.VirusTotalAsync(applet.Settings.VirusTotalKey, path, a.Name);
                        hash = ProcessDb.Db.Get(a.Name)?.Sha256;
                        refresh();
                    }
                    else
                    {
                        hash ??= await Task.Run(() => Lookup.Sha256(path));
                        Lookup.OpenVirusTotal(hash);
                        Build();
                    }
                }
                catch (Exception ex) { Log.Here.Info($"Process Analyzer: VirusTotal {a.Name}: {ex.Message}"); problem = ex.Message; Build(); }
            }));
        }
        Add(Ui.IconButton("", T("Search"), T("Search the web for it"), () => Lookup.Search(a.Name, a.Facts?.Company)));
        if (path != null) Add(Ui.IconButton("", T("Open Folder"), T("Show its file in Explorer"), () => Ui.ShowInExplorer(path)));
        var end = ConfirmButton.Make(a.Procs.Count > 1 ? T("End All") : T("End"), () =>
        {
            int failed = 0;
            foreach (var p in a.Procs)
            {
                try { using var pr = Process.GetProcessById(p.Pid); pr.Kill(); }
                catch { failed++; }
            }
            Log.Here.Info($"Process Analyzer: ended {a.Name} ({a.Procs.Count - failed} of {a.Procs.Count})");
            problem = failed > 0 ? F("{0} of its processes couldn't be ended (Windows', or running as administrator)", failed) : "";
            if (failed > 0) Build();
            refresh();
        }, a.Procs.Count > 1 ? T("End every process of it") : T("End the process"));
        Add(end);
        return wrap;
    }

    // ---------------------------------------------------------------- editing

    private UIElement Editor(Assessment a)
    {
        var e = a.Entry.Copy();
        var grid = Table();
        var title = new TextBox { Text = e.Title };
        var publisher = new TextBox { Text = e.Publisher };
        var category = new ComboBox { IsEditable = true, Text = e.Category };
        foreach (var c in ProgramRow.Categories) category.Items.Add(c);
        category.Text = e.Category;
        var threat = Ui.Choice(Enum.GetValues<Threat>().Select(t => (ProgramRow.Text(t), t)), e.Threat, _ => { }, 160);
        var necessity = Ui.Choice(Enum.GetValues<Necessity>().Select(n => (ProgramRow.Text(n), n)), e.Necessity, _ => { }, 160);
        var resource = Ui.Choice(Enum.GetValues<Resource>().Select(r => (ProgramRow.Text(r), r)), e.Resource, _ => { }, 160);
        var description = new TextBox { Text = e.Description, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 60 };
        var paths = new TextBox { Text = string.Join("; ", e.Paths), ToolTip = T("Folders it should run from, separated by ; (%SystemRoot% and the like allowed); empty: anywhere") };
        Field(grid, T("Title"), title);
        Field(grid, T("Publisher"), publisher);
        Field(grid, T("Category"), category);
        Field(grid, T("Threat"), threat);
        Field(grid, T("Needed"), necessity);
        Field(grid, T("Expected use"), resource);
        Field(grid, T("About it"), description);
        Field(grid, T("Runs from"), paths);

        var save = Ui.Button(T("Save"), () =>
        {
            e.Title = title.Text.Trim();
            e.Publisher = publisher.Text.Trim();
            e.Category = category.Text.Trim().Length > 0 ? category.Text.Trim() : "Unknown";
            e.Threat = threat.SelectedItem is ComboBoxItem { Tag: Threat t } ? t : e.Threat;
            e.Necessity = necessity.SelectedItem is ComboBoxItem { Tag: Necessity n } ? n : e.Necessity;
            e.Resource = resource.SelectedItem is ComboBoxItem { Tag: Resource r } ? r : e.Resource;
            e.Description = description.Text.Trim();
            e.Paths = paths.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            e.Source = "user";
            e.Reviewed = true;
            ProcessDb.Db.Set(e);
            Log.Here.Info($"Process Analyzer: {a.Name} classified by hand: {e.Category}, {e.Threat}, {e.Necessity}, {e.Resource}");
            editing = false;
            refresh();
        }, accent: true);
        var cancel = Ui.Button(T("Cancel"), () => { editing = false; Build(); });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { save, Ui.Spaced(cancel) } };
        if (a.Entry.Source is "user" or "import" || (a.Entry.Source == "server" && a.Entry.Reviewed))
            buttons.Children.Add(Ui.Spaced(ConfirmButton.Make(T("Forget"), () =>
            {
                ProcessDb.Db.Remove(a.Name);
                editing = false;
                refresh();
            }, T("Forget this classification here: it's worked out again (the server keeps its own)"))));
        return Card(T("Classify"), new StackPanel { Children = { grid, buttons } });
    }

    // ---------------------------------------------------------------- layout

    private static Grid Table()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        return g;
    }

    private static void Line(Grid g, string label, string value, bool copy = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        UIElement v = copy
            ? new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0) }
            : new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Field(g, label, v);
    }

    private static void Field(Grid g, string label, UIElement value)
    {
        int row = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = Ui.Secondary(label);
        l.Margin = new Thickness(0, 2, 8, 4);
        Grid.SetRow(l, row);
        g.Children.Add(l);
        if (value is FrameworkElement fe)
        {
            fe.Margin = new Thickness(0, 2, 0, 4);
            if (fe is ComboBox { IsEditable: false }) fe.HorizontalAlignment = HorizontalAlignment.Left;
        }
        if (value is TextBox tb && !tb.IsReadOnly) tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
    }

    private static Border Card(string title, UIElement body) =>
        Ui.Card(new StackPanel { Children = { Ui.Header(title, new Thickness(0, 0, 0, 6)), body } }, new Thickness(0, 8, 0, 0));
}
