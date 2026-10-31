using System.Windows;
using System.Windows.Controls;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools → Events: what happened to the connection (drops, adapters, addresses, Wi-Fi, speed tests), newest
/// first, a chip per kind to show or hide it, and the last 24 hours in figures (drops and the longest, Wi-Fi changes,
/// adapter disconnects).
/// </summary>
internal sealed class EventsView : UserControl, IDisposable
{
    private sealed record EventRow(DateTime Time, string When, string Kind, string Text, string Mark);

    private readonly NetworkingSettings settings;
    private readonly EventMonitor monitor;
    private readonly DataGrid table = Ui.Table(520);
    private readonly NetUi.Fields summary = new();
    private readonly CheckBox onBox;

    public EventsView(NetworkingSettings settings, EventMonitor monitor)
    {
        this.settings = settings;
        this.monitor = monitor;
        onBox = NetUi.OnBox(T("Events"), settings.EventsOn, T("Untick to stop watching"));
        Ui.OnToggle(onBox, on => { settings.EventsOn = on; settings.Save(); });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var kind in EventMonitor.Kinds)
        {
            var chip = Ui.Chip(KindName(kind), !settings.EventsHidden.Contains(kind));
            Ui.OnToggle(chip, on =>
            {
                settings.EventsHidden.Remove(kind);
                if (!on) settings.EventsHidden.Add(kind);
                settings.Save();
                Draw();
            });
            chips.Children.Add(chip);
        }
        var clear = ConfirmButton.Make(T("Clear"), monitor.Clear);
        clear.ToolTip = T("Delete every event");
        var header = Ui.HeaderRow(onBox, chips, clear);

        table.Columns.Add(Ui.Column("", nameof(EventRow.Mark), 30));
        table.Columns.Add(Ui.Column(T("Time"), nameof(EventRow.When), 150, nameof(EventRow.Time)));
        table.Columns.Add(Ui.Column(T("Kind"), nameof(EventRow.Kind), 90));
        table.Columns.Add(NetUi.CopyColumn(T("What"), nameof(EventRow.Text), -1));
        NetUi.ItemMenu(table, o => o is EventRow r ? [r.Text] : []);

        var day = Ui.Card(new StackPanel { Children = { Ui.Header(T("Last 24 Hours")), summary } }, new Thickness(0));
        var list = Ui.Card(new StackPanel { Children = { header, table } }, new Thickness(0, 12, 0, 0));
        Content = Ui.Page(new StackPanel { Children = { day, list } });

        monitor.Changed += OnChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) Draw(); };
    }

    public static string KindName(string kind) => kind switch
    {
        EventMonitor.Drop => T("Drops"),
        EventMonitor.Adapter => T("Adapters"),
        EventMonitor.Address => T("Addresses"),
        EventMonitor.WiFi => T("Wi-Fi"),
        EventMonitor.Speed => T("Speed Test"),
        _ => kind,
    };

    private void OnChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Draw(); });

    private void Draw()
    {
        onBox.IsChecked = settings.EventsOn;
        var all = monitor.Events;
        table.ItemsSource = all.Where(e => !settings.EventsHidden.Contains(e.Kind)).Reverse()
            .Select(e => new EventRow(e.Time, e.Time.ToString("g"), KindName(e.Kind), e.Text, e.Bad ? "⚠" : "")).ToList();

        var day = all.Where(e => e.Time >= DateTime.Now.AddHours(-24)).ToList();
        var drops = day.Where(e => e.Kind == EventMonitor.Drop && e.Bad).ToList();
        var backs = day.Where(e => e.Kind == EventMonitor.Drop && !e.Bad && !double.IsNaN(e.Seconds)).ToList();
        summary.Clear();
        var danger = (System.Windows.Media.Brush)FindResource("ErrorTextBrush");
        summary.Add(T("Drops"), drops.Count == 0 ? T("none") : drops.Count + (backs.Count > 0 ? " · " + F("longest {0}", NetUi.Duration(TimeSpan.FromSeconds(backs.Max(b => b.Seconds)))) : ""),
            null, drops.Count > 0 ? danger : null);
        int wifi = day.Count(e => e.Kind == EventMonitor.WiFi && e.Bad);
        summary.Add(T("Wi-Fi"), wifi == 0 ? T("no problems") : P(wifi, "{0} problem", "{0} problems"), null, wifi > 0 ? danger : null);
        int adapters = day.Count(e => e.Kind == EventMonitor.Adapter && e.Bad);
        summary.Add(T("Adapters"), adapters == 0 ? T("no disconnects") : P(adapters, "{0} disconnect", "{0} disconnects"), null, adapters > 0 ? danger : null);
        int addresses = day.Count(e => e.Kind == EventMonitor.Address);
        summary.Add(T("Addresses"), addresses == 0 ? T("no changes") : P(addresses, "{0} change", "{0} changes"));
        int speed = day.Count(e => e.Kind == EventMonitor.Speed);
        if (speed > 0) summary.Add(T("Speed Test"), P(speed, "{0} failed", "{0} failed"), null, danger);
    }

    public void Dispose() => monitor.Changed -= OnChanged;
}
