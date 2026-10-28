using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Shared.Widgets;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// The Ping and Speed Test widgets (Core's <see cref="Widget"/>, made with Make Widget on a right-click of their card):
/// shows the saved ones when the app starts, and redraws them as results come in. Their places are in
/// <see cref="NetworkingSettings.Widgets"/>.
/// </summary>
internal sealed class NetWidgets
{
    private readonly NetworkingSettings settings;
    private readonly PingMonitor? pings;
    private readonly SpeedTester? speed;
    private readonly Dictionary<Guid, NetWidget> open = new();

    public NetWidgets(NetworkingSettings settings, PingMonitor? pings, SpeedTester? speed)
    {
        this.settings = settings;
        this.pings = pings;
        this.speed = speed;
    }

    public void Start()
    {
        foreach (var place in settings.Widgets.ToList()) Open(place);
        if (pings != null) pings.Updated += Redraw;
        if (speed != null) speed.Changed += Redraw;
    }

    /// <summary>A new widget of this kind, near <paramref name="near"/>, being edited.</summary>
    public void Create(string kind, Window? near)
    {
        var place = new NetWidgetPlace { Kind = kind, Span = kind == NetWidgetPlace.PingKind ? 5 : 24 };
        if (near != null)
        {
            double scale = PresentationSource.FromVisual(near)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            place.X = (int)((near.Left + 80) * scale);
            place.Y = (int)((near.Top + 100) * scale);
        }
        settings.Widgets.Add(place);
        settings.Save();
        Open(place)?.BeginEdit();
    }

    private NetWidget? Open(NetWidgetPlace place)
    {
        if (place.Kind == NetWidgetPlace.PingKind ? pings == null : speed == null) return null; // its tool is switched off
        var w = new NetWidget(place, settings, pings, speed, settings.Save, () => Delete(place));
        open[place.Id] = w;
        w.Show();
        w.Draw();
        return w;
    }

    private void Delete(NetWidgetPlace place)
    {
        if (open.Remove(place.Id, out var w)) w.Close();
        settings.Widgets.RemoveAll(p => p.Id == place.Id);
        settings.Save();
    }

    private void Redraw()
    {
        if (open.Count == 0) return;
        Application.Current?.Dispatcher.BeginInvoke(() => { foreach (var w in open.Values) w.Draw(); });
    }

    public void Shutdown()
    {
        if (pings != null) pings.Updated -= Redraw;
        if (speed != null) speed.Changed -= Redraw;
        foreach (var w in open.Values.ToList()) w.Close();
        open.Clear();
    }
}

/// <summary>One widget: Ping (each target's time now, and the graph) or Speed Test (the last result, and the graph).</summary>
internal sealed class NetWidget : Widget
{
    private readonly NetWidgetPlace place;
    private readonly NetworkingSettings settings;
    private readonly PingMonitor? pings;
    private readonly SpeedTester? speed;
    private readonly LineGraph graph = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel values = new();

    public NetWidget(NetWidgetPlace place, NetworkingSettings settings, PingMonitor? pings, SpeedTester? speed, Action save, Action delete)
        : base(place, save, delete, firstSize: new Size(340, 170))
    {
        this.place = place;
        this.settings = settings;
        this.pings = pings;
        this.speed = speed;
        bool ping = place.Kind == NetWidgetPlace.PingKind;
        Title = ping ? T("Ping widget") : T("Speed Test widget");
        var title = new TextBlock { Text = ping ? T("Ping") : T("Speed Test"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0) };
        values.Children.Add(title);
        DockPanel.SetDock(values, Dock.Top);
        Body = new DockPanel { Children = { values, graph } };

        var spans = ping
            ? new[] { 1, 5, 15, 60 }.Select(m => (m < 60 ? F("{0} min", m) : F("{0} h", m / 60), m))
            : new[] { 24, 168, 720 }.Select(h => (h <= 24 ? F("{0} h", h) : F("{0} days", h / 24), h));
        if (place.Span <= 0) place.Span = ping ? 5 : 24;
        AddOption(Ui.Choice(spans, place.Span, v => { place.Span = v; Draw(); }, 80));
    }

    public void Draw()
    {
        while (values.Children.Count > 1) values.Children.RemoveAt(1);
        var now = DateTime.Now;
        if (place.Kind == NetWidgetPlace.PingKind && pings != null)
        {
            var from = now.AddMinutes(-place.Span);
            var series = new List<GraphSeries>();
            double top = 0;
            var targets = settings.Targets.Where(t => t.Address.Trim().Length > 0).GroupBy(t => t.Key).Select(g => g.First()).ToList();
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                string color = Ui.Colors[i % Ui.Colors.Length];
                var points = PingView.Thin(pings.Points(t.Key, from), from, now, 200);
                double last = points.Count > 0 ? points[^1].V : double.NaN;
                bool bad = double.IsNaN(last) && points.Count > 0 || last > t.AlertMs;
                series.Add(new GraphSeries { Name = t.Display, Metric = MsUnit.Instance, Points = points, Color = bad ? "DangerBrush" : color, Fill = false, Bold = bad });
                top = Math.Max(top, points.Where(p => !double.IsNaN(p.V)).Select(p => p.V).DefaultIfEmpty(0).Max());
                values.Children.Add(Value(t.Display, double.IsNaN(last) ? (points.Count > 0 ? "✕" : "—") : Ui.Ms(last), color, bad));
            }
            graph.Show(series, from, now, Ui.NiceCeiling(Math.Max(top * 1.15, 10)), TimeSpan.FromSeconds(Math.Max(settings.PingSeconds * 3, place.Span * 60 / 200.0 * 2.5)));
        }
        else if (speed != null)
        {
            var results = speed.Results;
            var last = results.LastOrDefault(r => !r.Failed);
            values.Children.Add(Value(T("Down"), speed.Running && speed.Phase == "Download" ? Ui.Mbps(speed.LiveMbps) : Ui.Mbps(last?.DownMbps ?? double.NaN), "AccentBrush", false));
            values.Children.Add(Value(T("Up"), speed.Running && speed.Phase == "Upload" ? Ui.Mbps(speed.LiveMbps) : Ui.Mbps(last?.UpMbps ?? double.NaN), Ui.Orange, false));
            values.Children.Add(Value(T("Latency"), Ui.Ms(last?.PingMs ?? double.NaN), null, false));
            var from = now.AddHours(-place.Span);
            var shown = results.Where(r => r.Time >= from).ToList();
            var series = new List<GraphSeries>
            {
                new() { Name = T("Download"), Metric = MbpsUnit.Instance, Points = shown.Select(r => (r.Time, r.DownMbps)).ToList(), Color = "AccentBrush", Fill = true },
                new() { Name = T("Upload"), Metric = MbpsUnit.Instance, Points = shown.Select(r => (r.Time, r.UpMbps)).ToList(), Color = Ui.Orange, Fill = false },
            };
            double top = shown.SelectMany(r => new[] { r.DownMbps, r.UpMbps }).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
            graph.Show(series, from, now, Ui.NiceCeiling(Math.Max(top * 1.1, 10)), TimeSpan.FromMinutes(Math.Max(settings.SpeedMinutes, 10) * 2.5));
        }
    }

    private FrameworkElement Value(string name, string value, string? color, bool bad)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 2) };
        if (color != null)
            panel.Children.Add(new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Fill = Ui.BrushOf(this, color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        panel.Children.Add(Ui.Secondary(name + " "));
        var v = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold };
        if (bad) v.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        panel.Children.Add(v);
        return panel;
    }
}
