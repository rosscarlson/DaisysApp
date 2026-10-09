using System.Windows;

namespace DaisysApp.Applets.Performance;

/// <summary>
/// Keeps the widgets: shows the saved ones when the app starts, makes new ones from a history window, and redraws them
/// all after each sample. Their positions and options are saved in <see cref="PerformanceSettings.Widgets"/>.
/// </summary>
internal sealed class WidgetManager
{
    private readonly PerfMonitor monitor;
    private readonly PerformanceSettings settings;
    private readonly Dictionary<Guid, WidgetWindow> windows = new();

    public static WidgetManager? Instance { get; private set; }

    /// <summary>Raised when a widget is added, changed or deleted.</summary>
    public event Action? Changed;

    public WidgetManager(PerfMonitor monitor, PerformanceSettings settings)
    {
        this.monitor = monitor;
        this.settings = settings;
        Instance = this;
    }

    public IReadOnlyList<WidgetDefinition> Widgets => settings.Widgets;

    public void Start()
    {
        foreach (var def in settings.Widgets.ToList()) Open(def);
        monitor.Sampled += OnSample;
        HistoryWindow.ScaleChanged += Redraw;
    }

    /// <summary>A new widget for <paramref name="group"/>, beside <paramref name="near"/>, ready to be placed.</summary>
    public void Create(MetricGroup group, Window near)
    {
        var def = new WidgetDefinition { Group = group.Key };
        // in physical pixels, a little in from the window it was made from
        var source = PresentationSource.FromVisual(near);
        double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        def.X = (int)((near.Left + 60) * scale);
        def.Y = (int)((near.Top + 80) * scale);
        def.Width = (int)(320 * scale);
        def.Height = (int)(150 * scale);
        settings.Widgets.Add(def);
        settings.Save();
        Open(def)?.BeginEdit();
        Changed?.Invoke();
    }

    public void Edit(WidgetDefinition def)
    {
        if (windows.TryGetValue(def.Id, out var w)) { w.BeginEdit(); w.Activate(); }
    }

    public void Delete(WidgetDefinition def)
    {
        if (windows.Remove(def.Id, out var w)) w.Close();
        settings.Widgets.RemoveAll(d => d.Id == def.Id);
        settings.Save();
        Changed?.Invoke();
    }

    public string TitleOf(WidgetDefinition def) => MetricGroup.ByKey(def.Group)?.Title ?? def.Group;

    private WidgetWindow? Open(WidgetDefinition def)
    {
        if (MetricGroup.ByKey(def.Group) is not { } group) return null;
        var w = new WidgetWindow(def, group, settings, () => { settings.Save(); Changed?.Invoke(); }, () => Delete(def));
        windows[def.Id] = w;
        w.Show();
        w.Update(monitor.Live());
        return w;
    }

    private void OnSample(PerfSample _) => Redraw();

    private void Redraw()
    {
        if (windows.Count == 0) return;
        var live = monitor.Live();
        foreach (var w in windows.Values) w.Update(live);
    }

    public void Shutdown()
    {
        monitor.Sampled -= OnSample;
        HistoryWindow.ScaleChanged -= Redraw;
        foreach (var w in windows.Values) w.Close();
        windows.Clear();
    }
}
