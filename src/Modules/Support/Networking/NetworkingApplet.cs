using System.IO;
using System.Windows;
using DaisysApp.Logging;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// Networking Tools, a sub-tab of the Support tab, with its tools on sub-tabs of its own: Connection, Ping, Speed Test,
/// Traceroute, Wi-Fi, Port Scan and Events. Each tool is a sub-module of Support (switched on or off in Settings →
/// General → Modules, needing Networking Tools); one that's off isn't made. Ping, Speed Test and Events keep working in
/// the background while the app runs (each has its own on / off); the others only while their tab is showing.
/// </summary>
public sealed class NetworkingApplet : IApplet
{
    public const string Connection = "Connection", Ping = "Ping", Speed = "SpeedTest", Trace = "Traceroute", WiFi = "WiFi", PortScan = "PortScan", Events = "Events";

    private readonly NetworkingSettings settings = NetworkingSettings.Load();
    private readonly SubTabs tabs = new();
    private readonly PingMonitor? pings;
    private readonly SpeedTester? speed;
    private readonly EventMonitor? events;
    private readonly NetWidgets widgets;
    private readonly List<IDisposable> views = new();
    private readonly FrameworkElement settingsView;

    public NetworkingApplet()
    {
        bool On(string id, string title)
        {
            ToolSwitches.Declare(SupportApplet.ModuleId, id, title, SupportApplet.Networking);
            return ToolSwitches.IsOn(SupportApplet.ModuleId, id, SupportApplet.Networking);
        }
        bool connection = On(Connection, T("Connection")), ping = On(Ping, T("Ping")), speedOn = On(Speed, T("Speed Test")),
            trace = On(Trace, T("Traceroute")), wifi = On(WiFi, T("Wi-Fi")), scan = On(PortScan, T("Port Scan")), log = On(Events, T("Events"));

        if (ping) pings = new PingMonitor(settings);
        if (speedOn) speed = new SpeedTester(settings, Path.Combine(PingMonitor.Folder, "speedtest.csv"), Log.Here);
        if (log) events = new EventMonitor(settings, pings, speed);
        widgets = new NetWidgets(settings, pings, speed);

        void Add(string id, string title, string icon, Func<FrameworkElement> make)
        {
            try
            {
                var view = make();
                if (view is IDisposable d) views.Add(d);
                tabs.Add(id, title, icon, view);
            }
            catch (Exception ex)
            {
                Log.Here.Error($"{id} couldn't be created", ex);
                tabs.Add(id, title, icon, Ui.Secondary(F("{0} couldn't load: {1}", title, ex.Message), wrap: true));
            }
        }
        if (connection) Add(Connection, T("Connection"), "", () => new ConnectionView());
        if (pings != null) Add(Ping, T("Ping"), "", () => new PingView(settings, pings, widgets));
        if (speed != null) Add(Speed, T("Speed Test"), "", () => new SpeedView(settings, speed, widgets));
        if (trace) Add(Trace, T("Traceroute"), "", () => new TraceView(settings));
        if (wifi) Add(WiFi, T("Wi-Fi"), "", () => new WiFiView(settings));
        if (scan) Add(PortScan, T("Port Scan"), "", () => new PortScanView(settings));
        if (events != null) Add(Events, T("Events"), "", () => new EventsView(settings, events));
        tabs.Select(settings.Tab);
        tabs.Selected += id => settings.Tab = id;
        settingsView = new NetworkingSettingsView(settings, pings, speed, events);
    }

    public FrameworkElement View => tabs;

    public FrameworkElement? SettingsView => settingsView;

    public void Start()
    {
        pings?.Start();
        speed?.Start();
        events?.Start();
        widgets.Start();
    }

    public void SaveSettings() => settings.Save();

    public void Dispose()
    {
        widgets.Shutdown();
        foreach (var v in views) v.Dispose();
        events?.Dispose();
        pings?.Dispose();
        speed?.Dispose();
        settings.Save();
    }
}
