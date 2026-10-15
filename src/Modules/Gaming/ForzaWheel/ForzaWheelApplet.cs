using System.Windows;
using System.Windows.Interop;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Gaming.ForzaWheel;

/// <summary>
/// ForzaWheel: Forza EmuWheel's three parts in one place. The emulator (a wheel, pedals, shifter, handbrake and button
/// boxes as one vJoy controller), Hush (mutes the real devices for Forza, as EmuWheel's Hush.exe) and the binding tool
/// (what drives what, saved as JSON). It checks the vJoy device is set up as Forza needs and can set it up. See README.md.
/// </summary>
public sealed class ForzaWheelApplet : IApplet
{
    private readonly ForzaWheelSettings settings = ForzaWheelSettings.Load();
    private readonly ForzaWheellator emulator;
    private readonly ForzaWheelView view;
    // a hidden window of its own, which DirectInput reads controllers in the background for
    private readonly HwndSource window = new(new HwndSourceParameters("DaisysApp ForzaWheel") { Width = 0, Height = 0, WindowStyle = 0 });

    public ForzaWheelApplet()
    {
        emulator = new ForzaWheellator(settings);
        emulator.SetWindow(window.Handle);
        view = new ForzaWheelView(settings, emulator, window.Handle);
    }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        if (settings.StartWithApp) emulator.Start();
    }

    public void SaveSettings() => settings.Save();

    public IReadOnlyList<AppletMenuItem>? TrayMenu => new[]
    {
        new AppletMenuItem(T("Wheel emulator running"), () => { if (emulator.Running) emulator.Stop(); else emulator.Start(); view.Refresh(); }) { Checked = emulator.Running },
    };

    public void Dispose()
    {
        settings.Save();
        emulator.Dispose();
        window.Dispose();
    }
}
