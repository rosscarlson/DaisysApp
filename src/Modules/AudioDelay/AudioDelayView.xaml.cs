using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

using DaisysApp.Shared.Audio;
using DaisysApp.Shared.Voicemeeter;

namespace DaisysApp.Applets.AudioDelay;

/// <summary>
/// A Voicemeeter output: a hardware output (A1…, with the device on it and its current output delay) or a virtual one
/// (B1…, e.g. sent on over VBAN). Only hardware outputs can be delayed.
/// </summary>
public sealed record BusChoice(int Index, string Name, string Device, double DelayMs, bool IsVirtual)
{
    public string Display => IsVirtual ? F("{0} · virtual output", Name)
                           : F("{0} · {1}", Name, (Device.Length > 0 ? Device : T("no output device")));
}

/// <summary>One output in the results table.</summary>
public sealed class DelayRow : INotifyPropertyChanged
{
    private string name = "", pass1 = "", pass2 = "", pass3 = "", delay = "";
    private bool isActive;

    public string Name { get => name; set => Set(ref name, value); }
    public string Pass1 { get => pass1; set => Set(ref pass1, value); }
    public string Pass2 { get => pass2; set => Set(ref pass2, value); }
    public string Pass3 { get => pass3; set => Set(ref pass3, value); }
    public string Delay { get => delay; set => Set(ref delay, value); }
    public bool IsActive { get => isActive; set => Set(ref isActive, value); }

    public void SetPass(int pass, string text)
    {
        if (pass == 1) Pass1 = text; else if (pass == 2) Pass2 = text; else Pass3 = text;
    }

    public void ClearPasses() => Pass1 = Pass2 = Pass3 = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

/// <summary>
/// Audio Delay: brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker) into sync. Short beeps are played
/// on each output in turn, with the other one muted in Voicemeeter, and the mic times when each arrives. The output that
/// arrives later keeps 0 ms; the other gets Voicemeeter's output delay (Option.delay) so both arrive together.
/// Measuring each output separately tells which one is late, so no guessing is needed.
/// </summary>
public partial class AudioDelayView : UserControl
{
    private const double Lead = 0.6;          // silence before the first beep (lets Bluetooth links wake up)
    private const double Spacing = 1.5;       // seconds between beeps
    private const double Window = 1.4;        // how long after a beep to look for it at the mic
    private const double SwitchAfter = 0.75;  // switch outputs this long after a beep was sent
    private const int BeepsPerOutput = 3;
    private const double ToleranceMs = 1.0;   // in sync if within this
    private const double MaxDelayMs = 500;    // Voicemeeter's output delay limit
    private const double BeepAmplitude = 0.5; // −6 dBFS peak

    private readonly AudioDelaySettings settings;
    private readonly DeviceService deviceService = new();
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer resetConfirmTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DelayRow rowA = new(), rowB = new();

    private List<DeviceInfo> outputs = new();
    private List<CaptureDeviceInfo> mics = new();
    private List<BusChoice> buses = new();
    private VoicemeeterKind vmKind;
    private MicRecorder? mic;
    private MicGain? micGain;
    private DateTime lastClip = DateTime.MinValue;
    private bool initializing = true, suppress;
    private CancellationTokenSource? cts;

    // what to put back if the app closes mid-run
    private Dictionary<int, float>? savedMutes;
    private (int Bus, double Ms)[]? savedDelays;

    public AudioDelayView(AudioDelaySettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        RowList.ItemsSource = new[] { rowA, rowB };

        meterTimer.Tick += MeterTimer_Tick;
        refreshDebounce.Tick += (_, _) => { refreshDebounce.Stop(); RefreshDevices(); };
        resetConfirmTimer.Tick += (_, _) => { resetConfirmTimer.Stop(); ResetButton.Content = T("Reset delays"); };
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() => { refreshDebounce.Stop(); refreshDebounce.Start(); });
        VmBanner.ReadyChanged += _ => RefreshBuses(); // Voicemeeter started or stopped

        // The mic is only open while this tab is showing (or a run is going).
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                RefreshBuses();
                OpenMic();
            }
            else if (!Running) CloseMic();
        };

        initializing = false;
        RefreshDevices();
        StatusText.Text = T("Choose the two outputs and the microphone, then press Start.");
    }

    private bool Running => cts != null;
    private BusChoice? BusA => BusABox.SelectedItem as BusChoice;
    private BusChoice? BusB => BusBBox.SelectedItem as BusChoice;

    private static string Ms(double ms) => ms.ToString("0.0", CultureInfo.CurrentCulture).Replace('-', '−') + " ms";

    // ---------------------------------------------------------------- devices

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        outputs = new();
        mics = new();
        RefreshDevices();
        RefreshBuses();
    }

    private void RefreshDevices()
    {
        try
        {
            var outList = deviceService.GetDevices();
            if (!outList.Select(d => d.Id).SequenceEqual(outputs.Select(d => d.Id)))
            {
                string? keep = (PlayBox.SelectedItem as DeviceInfo)?.Id ?? settings.PlayDeviceId;
                outputs = outList;
                suppress = true;
                PlayBox.ItemsSource = outputs;
                PlayBox.SelectedItem = outputs.FirstOrDefault(d => d.Id == keep)
                                       ?? outputs.FirstOrDefault(d => d.Name.StartsWith("Voicemeeter Input", StringComparison.OrdinalIgnoreCase))
                                       ?? outputs.FirstOrDefault(d => d.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase))
                                       ?? outputs.FirstOrDefault(d => d.IsDefault);
                suppress = false;
            }

            var micList = deviceService.GetCaptureDevices();
            if (!micList.SequenceEqual(mics))
            {
                string? keep = (MicBox.SelectedItem as CaptureDeviceInfo)?.Id ?? settings.MicDeviceId;
                mics = micList;
                suppress = true;
                MicBox.ItemsSource = mics;
                MicBox.SelectedItem = mics.FirstOrDefault(d => d.Id == keep) ?? mics.FirstOrDefault(d => d.IsDefault) ?? mics.FirstOrDefault();
                suppress = false;
                if (IsVisible) OpenMic();
            }
        }
        catch (Exception ex)
        {
            ShowError(T("Couldn't list the audio devices: ") + ex.Message);
        }
    }

    /// <summary>Reads Voicemeeter's hardware outputs (A1…), their devices and current delays.</summary>
    private void RefreshBuses()
    {
        if (Running) return;
        string? problem = null;
        buses = new();
        if (!VoicemeeterRemote.Connect(out var error)) problem = error;
        else
        {
            VoicemeeterRemote.Refresh();
            vmKind = VoicemeeterRemote.Kind;
            var names = VoicemeeterRemote.BusNames(vmKind);
            int physical = VoicemeeterRemote.PhysicalBuses(vmKind);
            if (vmKind == VoicemeeterKind.None) problem = T("Voicemeeter isn't running. Start it, then press refresh.");
            // hardware outputs (A…) first, then the virtual ones (B…), which can be measured but not delayed
            for (int i = 0; i < names.Count; i++)
            {
                bool isVirtual = i >= physical;
                buses.Add(new BusChoice(i, names[i],
                    isVirtual ? "" : VoicemeeterRemote.GetText($"Bus[{i}].device.name") ?? "",
                    isVirtual ? 0 : VoicemeeterRemote.Get($"Option.delay[{i}]") ?? 0,
                    isVirtual));
            }
        }

        suppress = true;
        BusABox.ItemsSource = buses;
        BusBBox.ItemsSource = buses;
        BusABox.SelectedItem = buses.FirstOrDefault(b => b.Index == settings.BusA) ?? buses.ElementAtOrDefault(0);
        BusBBox.SelectedItem = buses.FirstOrDefault(b => b.Index == settings.BusB) ?? buses.ElementAtOrDefault(1);
        suppress = false;

        VmStatusText.Text = problem ?? "";
        VmStatusText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        UpdateRows();
        UpdateButtons();
    }

    private void UpdateRows()
    {
        rowA.Name = BusA?.Display ?? T("Device 1");
        rowB.Name = BusB?.Display ?? T("Device 2");
        rowA.Delay = BusA == null ? "" : BusA.IsVirtual ? "—" : Ms(BusA.DelayMs);
        rowB.Delay = BusB == null ? "" : BusB.IsVirtual ? "—" : Ms(BusB.DelayMs);
    }

    private void UpdateButtons()
    {
        bool ready = BusA != null && BusB != null && BusA.Index != BusB.Index;
        SaveButton.IsEnabled = LoadButton.IsEnabled = !Running && buses.Any(x => !x.IsVirtual);
        StartButton.IsEnabled = Running || ready;
        ResetButton.IsEnabled = !Running && ready && (!BusA!.IsVirtual || !BusB!.IsVirtual);
        BusABox.IsEnabled = BusBBox.IsEnabled = PlayBox.IsEnabled = MicBox.IsEnabled = !Running;
    }

    private void BusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppress) return;
        if (BusA != null) settings.BusA = BusA.Index;
        if (BusB != null) settings.BusB = BusB.Index;
        settings.Save();
        rowA.ClearPasses();
        rowB.ClearPasses();
        UpdateRows();
        UpdateButtons();
    }

    private void PlayBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppress) return;
        settings.PlayDeviceId = (PlayBox.SelectedItem as DeviceInfo)?.Id;
        settings.Save();
    }

    // ---------------------------------------------------------------- microphone

    private void MicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppress) return;
        settings.MicDeviceId = (MicBox.SelectedItem as CaptureDeviceInfo)?.Id;
        settings.Save();
        if (IsVisible) OpenMic();
    }

    private void OpenMic()
    {
        CloseMic();
        if (MicBox.SelectedItem is not CaptureDeviceInfo sel) return;
        try
        {
            var m = new MicRecorder(deviceService.GetDevice(sel.Id));
            m.Stopped += ex => Dispatcher.BeginInvoke(() =>
            {
                if (mic != m) return;
                CloseMic();
                cts?.Cancel();
                ShowError(T("The microphone stopped") + (ex != null ? ": " + ex.Message : "."));
            });
            m.Start();
            mic = m;
            meterTimer.Start();
        }
        catch (Exception ex)
        {
            bool denied = ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));
            ShowError(denied
                ? T("Windows blocked microphone access. Turn on Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\".")
                : T("Could not open the microphone: ") + ex.Message);
        }

        try
        {
            micGain = new MicGain(deviceService.GetDevice(sel.Id));
            micGain.Changed += () => Dispatcher.BeginInvoke(SyncMicGain);
        }
        catch { micGain = null; }
        SyncMicGain();
    }

    private void CloseMic()
    {
        meterTimer.Stop();
        mic?.Dispose();
        mic = null;
        micGain?.Dispose();
        micGain = null;
        MicBarScale.ScaleX = 0;
        MicLevelText.Text = "—";
        MicLevelText.ClearValue(TextBlock.ForegroundProperty);
        SyncMicGain();
    }

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        if (mic == null) return;
        var (peak, _) = mic.TakeMeter();
        if (peak > 0.98) lastClip = DateTime.Now;
        double peakDb = 20 * Math.Log10(Math.Max(peak, 1e-6));
        MicBarScale.ScaleX = Math.Clamp((peakDb + 60) / 60, 0, 1);
        if ((DateTime.Now - lastClip).TotalSeconds < 1.5)
        {
            MicLevelText.Text = "CLIPPING";
            MicLevelText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            MicBar.SetResourceReference(Border.BackgroundProperty, "DangerBrush");
        }
        else
        {
            MicLevelText.Text = peakDb.ToString("0.0", CultureInfo.CurrentCulture).Replace('-', '−') + " dB";
            MicLevelText.ClearValue(TextBlock.ForegroundProperty);
            MicBar.SetResourceReference(Border.BackgroundProperty, "SuccessBrush");
        }
    }

    private void SyncMicGain()
    {
        double? pct = null;
        try { pct = micGain?.Percent; } catch { }
        MicGainRow.IsEnabled = pct != null && !Running;
        suppress = true;
        MicGainSlider.Value = pct ?? 0;
        suppress = false;
        MicGainText.Text = pct is double p ? $"{p:0} %" : "—";
    }

    private void MicGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing || suppress || micGain == null) return;
        try { micGain.Percent = MicGainSlider.Value; }
        catch (Exception ex) { ShowError(T("Couldn't change the mic level: ") + ex.Message); }
        MicGainText.Text = $"{MicGainSlider.Value:0} %";
    }

    // ---------------------------------------------------------------- Voicemeeter helpers

    private static double GetDelay(int bus) => VoicemeeterRemote.Get($"Option.delay[{bus}]") ?? 0;

    private static void SetDelays(params (int Bus, double Ms)[] delays) =>
        VoicemeeterRemote.Set(delays.Select(d => ($"Option.delay[{d.Bus}]", Math.Round(Math.Clamp(d.Ms, 0, MaxDelayMs), 1))));

    /// <summary>A bus's current output delay; virtual buses have none.</summary>
    private static double DelayOf(BusChoice bus) => bus.IsVirtual ? 0 : GetDelay(bus.Index);

    /// <summary>Sets output delays, skipping virtual buses (Voicemeeter can only delay hardware outputs).</summary>
    private static void SetDelaysFor(params (BusChoice Bus, double Ms)[] delays) =>
        SetDelays(delays.Where(d => !d.Bus.IsVirtual).Select(d => (d.Bus.Index, d.Ms)).ToArray());

    private int BusCount => VoicemeeterRemote.BusNames(vmKind).Count;

    /// <summary>The buses muted and unmuted during a run: every hardware output plus the two being synced, but never the
    /// bus the microphone comes through (a Voicemeeter "Out B…" mic would go silent).</summary>
    private int[] soloSet = [];

    /// <summary>Unmutes <paramref name="only"/> and mutes the rest of <see cref="soloSet"/>, so the mic hears one output at a time.</summary>
    private void SoloBus(int only) =>
        VoicemeeterRemote.Set(soloSet.Select(i => ($"Bus[{i}].Mute", i == only ? 0.0 : 1.0)));

    /// <summary>
    /// The Voicemeeter virtual bus a Voicemeeter capture device records ("Voicemeeter Out B2" → B2,
    /// "Voicemeeter Output" → B1, "Voicemeeter Aux Output" → B2, "Voicemeeter VAIO3 Output" → B3), or −1 for a real mic.
    /// </summary>
    private int MicBus()
    {
        if (MicBox.SelectedItem is not CaptureDeviceInfo m) return -1;
        string n = m.Name;
        string? bus = null;
        var match = System.Text.RegularExpressions.Regex.Match(n, @"Voicemeeter Out (B\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) bus = match.Groups[1].Value.ToUpperInvariant();
        else if (n.StartsWith("Voicemeeter VAIO3 Output", StringComparison.OrdinalIgnoreCase)) bus = "B3";
        else if (n.StartsWith("Voicemeeter Aux Output", StringComparison.OrdinalIgnoreCase)) bus = "B2";
        else if (n.StartsWith("Voicemeeter Output", StringComparison.OrdinalIgnoreCase)) bus = "B1";
        return bus == null ? -1 : VoicemeeterRemote.BusNames(vmKind).ToList().IndexOf(bus);
    }

    /// <summary>The Voicemeeter virtual input strip the playback device feeds (Voicemeeter Input, Aux, VAIO3), or −1.</summary>
    private int PlayStrip()
    {
        if (PlayBox.SelectedItem is not DeviceInfo d || !d.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase)) return -1;
        int hardware = vmKind switch { VoicemeeterKind.Standard => 2, VoicemeeterKind.Banana => 3, VoicemeeterKind.Potato => 5, _ => -1 };
        if (hardware < 0) return -1;
        int k = d.Name.Contains("VAIO3", StringComparison.OrdinalIgnoreCase) ? 2
              : d.Name.Contains("Aux", StringComparison.OrdinalIgnoreCase) ? 1
              : 0;
        return hardware + k;
    }

    // a strip → mic-bus route switched off for the run (so the beep can't reach a Voicemeeter mic electronically)
    private (string Name, float Value)? savedRoute;

    private void RestoreMutes()
    {
        try
        {
            if ((savedMutes != null || savedRoute != null) && VoicemeeterRemote.Connect(out _))
            {
                if (savedMutes != null)
                    VoicemeeterRemote.Set(savedMutes.Select(kv => ($"Bus[{kv.Key}].Mute", (double)kv.Value)));
                if (savedRoute is { } route)
                    VoicemeeterRemote.Set([(route.Name, (double)route.Value)]);
            }
        }
        catch { /* Voicemeeter gone */ }
        savedMutes = null;
        savedRoute = null;
    }

    private void RestoreDelays()
    {
        if (savedDelays == null) return;
        try
        {
            if (VoicemeeterRemote.Connect(out _)) SetDelays(savedDelays);
        }
        catch { }
        savedDelays = null;
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (BusA is not { } a || BusB is not { } b) return;
        if (!resetConfirmTimer.IsEnabled)
        {
            ResetButton.Content = T("Click to confirm");
            resetConfirmTimer.Start();
            return;
        }
        resetConfirmTimer.Stop();
        ResetButton.Content = T("Reset delays");
        try
        {
            SetDelaysFor((a, 0), (b, 0));
            await Task.Delay(300);
            StatusText.ClearValue(TextBlock.ForegroundProperty);
            StatusText.Text = string.Join(T(" and "), new[] { a, b }.Where(x => !x.IsVirtual).Select(x => x.Name)) + T(" back to 0 ms delay.");
        }
        catch (Exception ex) { ShowError(T("Couldn't reset the delays: ") + ex.Message); }
        RefreshBuses();
    }

    // ---------------------------------------------------------------- save / load delays

    /// <summary>A delays file: Voicemeeter's output delay for each hardware output.</summary>
    public sealed record SavedDelays(string Kind, DateTime Saved, List<SavedDelay> Delays)
    {
        public const string FileKind = "DaisysApp.OutputDelays";
    }

    public sealed record SavedDelay(string Bus, string Device, double Ms);

    private static string DelaysFilter => T("Daisy's App output delays (*.delays.json)|*.delays.json|All files (*.*)|*.*");

    private void SaveDelays_Click(object sender, RoutedEventArgs e)
    {
        RefreshBuses(); // current values from Voicemeeter
        var delays = buses.Where(x => !x.IsVirtual).Select(x => new SavedDelay(x.Name, x.Device, x.DelayMs)).ToList();
        if (delays.Count == 0) { ShowError(T("Voicemeeter isn't running, so there are no delays to save.")); return; }

        System.IO.Directory.CreateDirectory(AppPaths.DocumentsFolder);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("Save output delays"),
            Filter = DelaysFilter,
            InitialDirectory = AppPaths.DocumentsFolder,
            FileName = F("Output delays {0:yyyy-MM-dd}.delays.json", DateTime.Now),
        };
        if (dialog.ShowDialog(System.Windows.Window.GetWindow(this)) != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName,
                System.Text.Json.JsonSerializer.Serialize(new SavedDelays(SavedDelays.FileKind, DateTime.Now, delays), DaisysApp.Settings.JsonStore.Options));
            StatusText.ClearValue(TextBlock.ForegroundProperty);
            StatusText.Text = T("Saved ") + string.Join(", ", delays.Select(d => $"{d.Bus} {Ms(d.Ms)}")) + F(" to {0}.", System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { ShowError(T("Couldn't save the delays: ") + ex.Message); }
    }

    private async void LoadDelays_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Load output delays"),
            Filter = DelaysFilter,
            InitialDirectory = System.IO.Directory.Exists(AppPaths.DocumentsFolder) ? AppPaths.DocumentsFolder : null,
        };
        if (dialog.ShowDialog(System.Windows.Window.GetWindow(this)) != true) return;

        SavedDelays? file;
        try { file = System.Text.Json.JsonSerializer.Deserialize<SavedDelays>(System.IO.File.ReadAllText(dialog.FileName), DaisysApp.Settings.JsonStore.Options); }
        catch (Exception ex) { ShowError(T("Couldn't read that file: ") + ex.Message); return; }
        if (file is not { Kind: SavedDelays.FileKind } || file.Delays == null)
        {
            ShowError(T("That isn't an output delays file saved by Daisy's App."));
            return;
        }

        RefreshBuses();
        // match by bus name (A1, A2 …); only hardware outputs have a delay
        var matches = file.Delays
            .Select(d => (Saved: d, Bus: buses.FirstOrDefault(x => !x.IsVirtual && string.Equals(x.Name, d.Bus, StringComparison.OrdinalIgnoreCase))))
            .Where(m => m.Bus != null)
            .ToList();
        if (matches.Count == 0) { ShowError(T("None of the outputs in that file exist in the running Voicemeeter.")); return; }
        try
        {
            SetDelaysFor(matches.Select(m => (m.Bus!, m.Saved.Ms)).ToArray());
            await Task.Delay(300); // let Voicemeeter apply before reading back
        }
        catch (Exception ex) { ShowError(T("Couldn't set the delays: ") + ex.Message); return; }

        StatusText.ClearValue(TextBlock.ForegroundProperty);
        StatusText.Text = T("Loaded ") + string.Join(", ", matches.Select(m => $"{m.Bus!.Name} {Ms(m.Saved.Ms)}")) + ".";
        var moved = matches.Where(m => m.Saved.Device.Length > 0 && !string.Equals(m.Saved.Device, m.Bus!.Device, StringComparison.OrdinalIgnoreCase)).ToList();
        if (moved.Count > 0)
            StatusText.Text += T(" Note: ") + string.Join(", ", moved.Select(m => F("{0} was {1} when saved", m.Bus!.Name, m.Saved.Device))) + ".";
        rowA.ClearPasses();
        rowB.ClearPasses();
        RefreshBuses();
    }

    // ---------------------------------------------------------------- the run

    private sealed class DelayException(string message) : Exception(message);
    private sealed class MicClippedException() : Exception(T("The microphone is clipping."));

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Running)
        {
            cts!.Cancel();
            return;
        }
        await RunAsync();
    }

    private async Task RunAsync()
    {
        if (BusA is not { } a || BusB is not { } b || a.Index == b.Index) return;
        if (new[] { a, b }.FirstOrDefault(x => !x.IsVirtual && x.Device.Length == 0) is { } empty)
        {
            ShowError(F("{0} has no output device in Voicemeeter. Pick the device for it in Voicemeeter, then press refresh.", empty.Name));
            return;
        }
        int micBus = MicBus();
        if (micBus >= 0 && (micBus == a.Index || micBus == b.Index))
        {
            string bus = micBus == a.Index ? a.Name : b.Name;
            ShowError(F("The microphone records Voicemeeter's {0}, which is also one of the outputs being synced. Choose a different output, or a microphone that doesn't come through {1}.", bus, bus));
            return;
        }
        if (a.IsVirtual && b.IsVirtual)
        {
            ShowError(T("At least one of the two outputs must be a hardware output (A1…): Voicemeeter can only delay those."));
            return;
        }
        if (PlayBox.SelectedItem is not DeviceInfo play) { ShowError(T("Choose the device to play through (usually Voicemeeter Input).")); return; }
        if (mic == null) OpenMic();
        if (mic == null) { ShowError(T("Choose a microphone first.")); return; }

        cts = new CancellationTokenSource();
        var ct = cts.Token;
        rowA.ClearPasses();
        rowB.ClearPasses();
        StartButton.Style = (Style)FindResource("DangerButton");
        StartIcon.Text = "";
        StartLabel.Text = T("Cancel");
        UpdateButtons();
        SyncMicGain();
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        ProgressScale.ScaleX = 0;

        VoicemeeterRemote.Refresh();
        soloSet = Enumerable.Range(0, VoicemeeterRemote.PhysicalBuses(vmKind)).Append(a.Index).Append(b.Index)
                            .Distinct().Where(i => i != micBus).ToArray();
        savedMutes = soloSet.ToDictionary(i => i, i => VoicemeeterRemote.Get($"Bus[{i}].Mute") ?? 0);

        // If the mic is a Voicemeeter bus and the playback strip also feeds that bus, the beep would reach the "mic"
        // directly, with no delay at all. Switch that route off for the run (it's put back afterwards).
        int playStrip = PlayStrip();
        if (micBus >= 0 && playStrip >= 0)
        {
            string route = $"Strip[{playStrip}].{VoicemeeterRemote.BusNames(vmKind)[micBus]}";
            if (VoicemeeterRemote.Get(route) is float on && on > 0.5)
            {
                savedRoute = (route, on);
                VoicemeeterRemote.Set([(route, 0.0)]);
            }
        }
        savedDelays = new[] { a, b }.Where(x => !x.IsVirtual).Select(x => (x.Index, GetDelay(x.Index))).ToArray();
        bool succeeded = false;
        int totalSteps = 3, stepsDone = 0;

        void Status(string text, double within = 0)
        {
            StatusText.Text = text;
            ProgressScale.ScaleX = Math.Clamp((stepsDone + within) / totalSteps, 0, 1);
        }

        try
        {
            string result;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    stepsDone = 0;
                    result = await SyncOnceAsync();
                    break;
                }
                catch (MicClippedException)
                {
                    rowA.ClearPasses();
                    rowB.ClearPasses();
                    bool lowered = false;
                    if (attempt < 6 && micGain != null)
                    {
                        try { lowered = micGain.LowerBy(6); } catch { }
                    }
                    if (!lowered)
                        throw new DelayException(micGain == null
                            ? T("The microphone is clipping, and this mic's level can't be set from here. Lower its input volume in Windows Sound settings and try again.")
                            : T("The microphone is still clipping at its lowest level. Turn the outputs down and try again."));
                    SyncMicGain();
                    Status(F("The microphone was clipping, so its level was lowered to {0:0} %. Starting over…", micGain!.Percent));
                    await Task.Delay(1500, ct);
                }
            }
            succeeded = true;
            ProgressScale.ScaleX = 1;
            StatusText.Text = result;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = T("Cancelled. The delays are back to how they were.");
            ProgressScale.ScaleX = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = (ex is DelayException ? ex.Message : T("Measuring failed: ") + ex.Message) + T(" The delays are back to how they were.");
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            ProgressScale.ScaleX = 0;
        }
        finally
        {
            RestoreMutes();
            if (succeeded) savedDelays = null;
            else RestoreDelays();
            rowA.IsActive = rowB.IsActive = false;
            cts.Dispose();
            cts = null;
            StartButton.Style = (Style)FindResource("AccentButton");
            StartIcon.Text = "";
            StartLabel.Text = T("Start");
            SyncMicGain();
            await Task.Delay(300); // let Voicemeeter apply before reading back
            RefreshBuses();
            if (!IsVisible) CloseMic();
        }

        // One complete sync: baseline, adjust, verify. Throws MicClippedException if the mic clips.
        // Device 1 (a) is the base and keeps no delay; Device 2 (b) gets the delay. If Device 2 turns out to be the later
        // one, the two are swapped (and the user told) so the delay still goes on the one that arrives first.
        async Task<string> SyncOnceAsync()
        {
            string swapNote = "";
            double dA = DelayOf(a), dB = DelayOf(b);

            // 1. Baseline: how much later Device 2's beeps arrive than Device 1's, with the delays as they are now
            double d1 = await MeasureAsync(1, T("Baseline"));
            ShowPass(1, d1);
            stepsDone++;

            // How much later Device 1 arrives than Device 2 with no delays at all: the delay Device 2 needs.
            double need = -d1 - dA + dB;
            if (need < 0)
            {
                if (a.IsVirtual)
                    throw new DelayException(
                        F("{0} arrives {1} later than {2}, so {3} is the one that needs the delay, but it's a virtual output ", b.Name, Ms(-need), a.Name, a.Name) +
                        T("and Voicemeeter can only delay hardware outputs (A1…)."));
                swapNote = F("{0} arrives later than {1}, so they've been swapped: {2} is now Device 1 (the base) and {3} is Device 2 (gets the delay). ", b.Name, a.Name, b.Name, a.Name);
                (a, b) = (b, a);
                SwapSelections();
                ShowPass(1, -d1); // the baseline in the new order
                need = -need;
                StatusText.Text = swapNote;
                await Task.Delay(3000, ct); // time to read it
            }
            else if (b.IsVirtual && need > ToleranceMs)
            {
                throw new DelayException(
                    F("{0} needs a delay of {1}, but it's a virtual output and Voicemeeter can only delay hardware outputs (A1…). ", b.Name, Ms(need)) +
                    T("Choose a hardware output as Device 2."));
            }

            double delay = Math.Clamp(need, 0, MaxDelayMs);
            SetDelaysFor((a, 0), (b, delay));
            rowA.Delay = a.IsVirtual ? "—" : Ms(0);
            rowB.Delay = b.IsVirtual ? "—" : Ms(delay);
            await Task.Delay(400, ct);

            // 2. Adjusting: check the new delay
            double d2 = await MeasureAsync(2, T("Adjusting"));
            ShowPass(2, d2);
            stepsDone++;
            double final = d2;
            if (Math.Abs(d2) > ToleranceMs && !b.IsVirtual)
            {
                // d2 > 0: Device 2 now arrives after Device 1, so it has too much delay
                delay = Math.Clamp(delay - d2, 0, MaxDelayMs);
                SetDelaysFor((b, delay));
                rowB.Delay = Ms(delay);
                await Task.Delay(400, ct);

                // 3. Verifying: one more check after the correction
                final = await MeasureAsync(3, T("Verifying"));
                ShowPass(3, final);
            }
            else
            {
                rowA.Pass3 = rowB.Pass3 = "—";
            }
            stepsDone = totalSteps;

            string text = swapNote + (delay < 0.05
                ? F("Done. {0} and {1} already arrive together, so no delay is needed.", a.Name, b.Name)
                : F("Done. {0} is the base (no delay); {1} is delayed by {2}.", a.Name, b.Name, Ms(delay)));
            text += Math.Abs(final) <= ToleranceMs
                ? F(" They now arrive within {0} of each other.", Ms(Math.Abs(final)))
                : F(" They're still {0} apart; run it again, or check the mic can clearly hear both outputs.", Ms(Math.Abs(final)));
            if (need > MaxDelayMs)
                text += F(" The difference is more than Voicemeeter's {0:0} ms maximum delay.", MaxDelayMs);
            return text + T(" Saved in Voicemeeter.");
        }

        // After finding the two the wrong way round: show and remember the new order.
        void SwapSelections()
        {
            suppress = true;
            BusABox.SelectedItem = buses.FirstOrDefault(x => x.Index == a.Index);
            BusBBox.SelectedItem = buses.FirstOrDefault(x => x.Index == b.Index);
            suppress = false;
            settings.BusA = a.Index;
            settings.BusB = b.Index;
            settings.Save();
            rowA.ClearPasses();
            rowB.ClearPasses();
            UpdateRows();
        }

        void ShowPass(int pass, double bMinusA)
        {
            rowA.SetPass(pass, bMinusA < 0 ? "+" + Ms(-bMinusA) : Ms(0));
            rowB.SetPass(pass, bMinusA > 0 ? "+" + Ms(bMinusA) : Ms(0));
        }

        // Plays BeepsPerOutput beeps on each output, alternating, and returns how much later B's beeps arrive (ms).
        async Task<double> MeasureAsync(int pass, string label)
        {
            var times = Enumerable.Range(0, BeepsPerOutput * 2).Select(k => Lead + k * Spacing).ToList();
            bool IsA(int k) => k % 2 == 0;
            double end = times[^1] + Window + 0.1;

            rowA.IsActive = rowB.IsActive = true;
            rowA.SetPass(pass, T("listening…"));
            rowB.SetPass(pass, T("listening…"));
            SoloBus(a.Index);
            await Task.Delay(150, ct); // let the mutes take effect

            using var device = deviceService.GetDevice(play.Id);
            var player = new BurstPlayer(DeviceService.GetMixFormat(device), times, BeepAmplitude);
            float[] recording;
            using (var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60))
            {
                output.Init(player);
                var recorder = mic ?? throw new DelayException(T("The microphone was closed."));
                recorder.BeginRecording();
                output.Play();
                try
                {
                    int next = 1;
                    while (true)
                    {
                        await Task.Delay(20, ct);
                        double pos = player.PositionSeconds;
                        while (next < times.Count && pos >= times[next - 1] + SwitchAfter)
                        {
                            SoloBus(IsA(next) ? a.Index : b.Index);
                            next++;
                        }
                        int beep = Math.Clamp((int)((pos - Lead) / Spacing) + 1, 1, times.Count);
                        Status(F("{0}: beep {1} of {2} on {3}…", label, beep, times.Count, (IsA(beep - 1) ? a.Name : b.Name)), Math.Clamp(pos / end, 0, 1));
                        if (pos >= end) break;
                    }
                }
                finally
                {
                    try { output.Stop(); } catch { }
                    recording = (mic ?? recorder).EndRecording();
                }
            }

            Status(F("{0}: working out the timing…", label), 1);
            int rate = mic?.SampleRate ?? 48000;
            var arrivals = await Task.Run(() => DelayAnalyzer.FindArrivals(recording, rate, times, Window), ct);
            rowA.IsActive = rowB.IsActive = false;

            if (arrivals.Any(x => x?.Clipped == true)) throw new MicClippedException();
            var aTimes = arrivals.Where((x, k) => IsA(k) && x != null).Select(x => x!.Seconds * 1000).ToList();
            var bTimes = arrivals.Where((x, k) => !IsA(k) && x != null).Select(x => x!.Seconds * 1000).ToList();
            if (aTimes.Count < 2) throw new DelayException(NotHeard(a));
            if (bTimes.Count < 2) throw new DelayException(NotHeard(b));
            CheckSpread(a, aTimes);
            CheckSpread(b, bTimes);
            return Median(bTimes) - Median(aTimes);
        }

        string NotHeard(BusChoice bus) =>
            F("Couldn't hear the beeps from {0}. Check that it's on and turned up, that the Voicemeeter strip for ", bus.Display) +
            F("{0} is routed to {1}, and that the mic can hear it.", (PlayBox.SelectedItem as DeviceInfo)?.Name ?? T("the playback device"), bus.Name);

        void CheckSpread(BusChoice bus, List<double> ms)
        {
            double spread = ms.Max() - ms.Min();
            if (spread > 5)
                throw new DelayException(F("The beeps from {0} didn't arrive at a steady time (they varied by {1}). ", bus.Name, Ms(spread)) +
                                         T("Keep the room quiet and the mic still, and try again."));
        }
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }

    // ---------------------------------------------------------------- lifecycle (called by AudioDelayApplet)

    public void HandlePreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Running)
        {
            cts!.Cancel();
            e.Handled = true;
        }
    }

    public void OnWindowHidden()
    {
        cts?.Cancel();
        CloseMic();
    }

    /// <summary>The app is exiting: put Voicemeeter's mutes and delays back if a run was in progress.</summary>
    public void Shutdown()
    {
        cts?.Cancel();
        RestoreMutes();
        RestoreDelays();
        CloseMic();
        deviceService.Dispose();
    }
}
