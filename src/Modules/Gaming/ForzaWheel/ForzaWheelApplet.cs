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
        // Auto Hush: starting the emulator mutes the bound devices, stopping it unmutes them (from the tab, the tray or
        // at launch alike)
        emulator.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(AutoHush);
    }

    private bool wasRunning, closing;

    private async void AutoHush()
    {
        bool running = emulator.Running;
        if (running == wasRunning || closing) return;
        wasRunning = running;
        if (!settings.AutoHush) return;
        try
        {
            // the bound devices that are plugged in, by their GameInput key
            var bound = settings.Config.Devices.Select(d => d.Device).ToHashSet();
            var keys = GameControllers.List().Where(c => bound.Contains(c.Instance)).Select(c => c.GameInputKey).Distinct().ToList();
            Elevated.Outcome? result = null;
            if (running)
            {
                var mute = keys.Where(k => !GameInputRegistry.IsMuted(k)).ToList();
                bool register = !GameInputRegistry.VJoyIsWheel();
                if (mute.Count > 0 || register) result = await Elevated.RunAsync(GameInputRegistry.MuteCommands(mute, register));
            }
            else
            {
                var unmute = keys.Where(GameInputRegistry.IsMuted).ToList();
                if (unmute.Count > 0) result = await GameInputRegistry.UnmuteAsync(unmute);
            }
            if (result is { Ok: false }) Logging.Log.Here.Warn("ForzaWheel Auto Hush: " + result.Output);
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ForzaWheel Auto Hush", ex); }
        view.ShowHush();
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
        closing = true; // closing the app leaves them muted rather than asking for permission on the way out
        settings.Save();
        emulator.Dispose();
        window.Dispose();
    }
}
