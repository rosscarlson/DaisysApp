using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Tools.AudioLevel.Audio;
using DaisysApp.Tools.AudioLevel.Voicemeeter;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Tools.SetDelay;

/// <summary>A Voicemeeter hardware output (A1…) with the device on it and its current output delay.</summary>
public sealed record BusChoice(int Index, string Name, string Device, double DelayMs)
{
    public string Display => $"{Name} · {(Device.Length > 0 ? Device : "no output device")}";
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
/// Set Delay: brings two Voicemeeter outputs (e.g. a sound card and a Bluetooth speaker) into sync. Short beeps are played
/// on each output in turn, with the other one muted in Voicemeeter, and the mic times when each arrives. The output that
/// arrives later keeps 0 ms; the other gets Voicemeeter's output delay (Option.delay) so both arrive together.
/// Measuring each output separately tells which one is late, so no guessing is needed.
/// </summary>
public partial class SetDelayView : UserControl
{
    private const double Lead = 0.6;          // silence before the first beep (lets Bluetooth links wake up)
    private const double Spacing = 1.5;       // seconds between beeps
    private const double Window = 1.4;        // how long after a beep to look for it at the mic
    private const double SwitchAfter = 0.75;  // switch outputs this long after a beep was sent
    private const int BeepsPerOutput = 3;
    private const double ToleranceMs = 1.0;   // in sync if within this
    private const double MaxDelayMs = 500;    // Voicemeeter's output delay limit
    private const double BeepAmplitude = 0.5; // −6 dBFS peak

    private readonly SetDelaySettings settings;
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

    public SetDelayView(SetDelaySettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        RowList.ItemsSource = new[] { rowA, rowB };

        meterTimer.Tick += MeterTimer_Tick;
        refreshDebounce.Tick += (_, _) => { refreshDebounce.Stop(); RefreshDevices(); };
        resetConfirmTimer.Tick += (_, _) => { resetConfirmTimer.Stop(); ResetButton.Content = "Reset delays"; };
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() => { refreshDebounce.Stop(); refreshDebounce.Start(); });

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
        StatusText.Text = "Choose the two outputs and the microphone, then press Start.";
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
            ShowError("Couldn't list the audio devices: " + ex.Message);
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
            if (vmKind == VoicemeeterKind.None) problem = "Voicemeeter isn't running. Start it, then press refresh.";
            else if (physical < 2) problem = "This Voicemeeter edition has only one hardware output. Syncing two outputs needs Voicemeeter Banana or Potato.";
            for (int i = 0; i < physical && i < names.Count; i++)
                buses.Add(new BusChoice(i, names[i], VoicemeeterRemote.GetText($"Bus[{i}].device.name") ?? "",
                                        VoicemeeterRemote.Get($"Option.delay[{i}]") ?? 0));
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
        rowA.Name = BusA?.Display ?? "Device 1";
        rowB.Name = BusB?.Display ?? "Device 2";
        rowA.Delay = BusA != null ? Ms(BusA.DelayMs) : "";
        rowB.Delay = BusB != null ? Ms(BusB.DelayMs) : "";
    }

    private void UpdateButtons()
    {
        bool ready = BusA != null && BusB != null && BusA.Index != BusB.Index;
        StartButton.IsEnabled = Running || ready;
        ResetButton.IsEnabled = !Running && ready;
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
                ShowError("The microphone stopped" + (ex != null ? ": " + ex.Message : "."));
            });
            m.Start();
            mic = m;
            meterTimer.Start();
        }
        catch (Exception ex)
        {
            bool denied = ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));
            ShowError(denied
                ? "Windows blocked microphone access. Turn on Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\"."
                : "Could not open the microphone: " + ex.Message);
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
        catch (Exception ex) { ShowError("Couldn't change the mic level: " + ex.Message); }
        MicGainText.Text = $"{MicGainSlider.Value:0} %";
    }

    // ---------------------------------------------------------------- Voicemeeter helpers

    private static double GetDelay(int bus) => VoicemeeterRemote.Get($"Option.delay[{bus}]") ?? 0;

    private static void SetDelays(params (int Bus, double Ms)[] delays) =>
        VoicemeeterRemote.Set(delays.Select(d => ($"Option.delay[{d.Bus}]", Math.Round(Math.Clamp(d.Ms, 0, MaxDelayMs), 1))));

    /// <summary>Mutes every hardware output except <paramref name="only"/>, so the mic hears one output at a time.</summary>
    private void SoloBus(int only) =>
        VoicemeeterRemote.Set(Enumerable.Range(0, VoicemeeterRemote.PhysicalBuses(vmKind)).Select(i => ($"Bus[{i}].Mute", i == only ? 0.0 : 1.0)));

    private void RestoreMutes()
    {
        if (savedMutes == null) return;
        try
        {
            if (VoicemeeterRemote.Connect(out _))
                VoicemeeterRemote.Set(savedMutes.Select(kv => ($"Bus[{kv.Key}].Mute", (double)kv.Value)));
        }
        catch { /* Voicemeeter gone */ }
        savedMutes = null;
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
            ResetButton.Content = "Click to confirm";
            resetConfirmTimer.Start();
            return;
        }
        resetConfirmTimer.Stop();
        ResetButton.Content = "Reset delays";
        try
        {
            SetDelays((a.Index, 0), (b.Index, 0));
            await Task.Delay(300);
            StatusText.ClearValue(TextBlock.ForegroundProperty);
            StatusText.Text = $"{a.Name} and {b.Name} are back to 0 ms delay.";
        }
        catch (Exception ex) { ShowError("Couldn't reset the delays: " + ex.Message); }
        RefreshBuses();
    }

    // ---------------------------------------------------------------- the run

    private sealed class DelayException(string message) : Exception(message);
    private sealed class MicClippedException() : Exception("The microphone is clipping.");

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
        if (new[] { a, b }.FirstOrDefault(x => x.Device.Length == 0) is { } empty)
        {
            ShowError($"{empty.Name} has no output device in Voicemeeter. Pick the device for it in Voicemeeter, then press refresh.");
            return;
        }
        if (PlayBox.SelectedItem is not DeviceInfo play) { ShowError("Choose the device to play through (usually Voicemeeter Input)."); return; }
        if (mic == null) OpenMic();
        if (mic == null) { ShowError("Choose a microphone first."); return; }

        cts = new CancellationTokenSource();
        var ct = cts.Token;
        rowA.ClearPasses();
        rowB.ClearPasses();
        StartButton.Style = (Style)FindResource("DangerButton");
        StartIcon.Text = "";
        StartLabel.Text = "Cancel";
        UpdateButtons();
        SyncMicGain();
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        ProgressScale.ScaleX = 0;

        int physical = VoicemeeterRemote.PhysicalBuses(vmKind);
        VoicemeeterRemote.Refresh();
        savedMutes = Enumerable.Range(0, physical).ToDictionary(i => i, i => VoicemeeterRemote.Get($"Bus[{i}].Mute") ?? 0);
        savedDelays = [(a.Index, GetDelay(a.Index)), (b.Index, GetDelay(b.Index))];
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
                            ? "The microphone is clipping, and this mic's level can't be set from here. Lower its input volume in Windows Sound settings and try again."
                            : "The microphone is still clipping at its lowest level. Turn the outputs down and try again.");
                    SyncMicGain();
                    Status($"The microphone was clipping, so its level was lowered to {micGain!.Percent:0} %. Starting over…");
                    await Task.Delay(1500, ct);
                }
            }
            succeeded = true;
            ProgressScale.ScaleX = 1;
            StatusText.Text = result;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled. The delays are back to how they were.";
            ProgressScale.ScaleX = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = (ex is DelayException ? ex.Message : "Measuring failed: " + ex.Message) + " The delays are back to how they were.";
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
            StartLabel.Text = "Start";
            SyncMicGain();
            await Task.Delay(300); // let Voicemeeter apply before reading back
            RefreshBuses();
            if (!IsVisible) CloseMic();
        }

        // One complete sync: baseline, adjust, verify. Throws MicClippedException if the mic clips.
        async Task<string> SyncOnceAsync()
        {
            double dA = GetDelay(a.Index), dB = GetDelay(b.Index);

            // 1. Baseline: how much later B arrives than A, with the delays as they are now
            double d1 = await MeasureAsync(1, "Baseline");
            ShowPass(1, d1);
            stepsDone++;
            // B's own latency minus A's, without any delay: the later output keeps 0, the other gets the difference
            double natural = d1 - dB + dA;
            var (newA, newB) = Split(natural);
            SetDelays((a.Index, newA), (b.Index, newB));
            rowA.Delay = Ms(newA);
            rowB.Delay = Ms(newB);
            await Task.Delay(400, ct);

            // 2. Adjusting: check the new delays
            double d2 = await MeasureAsync(2, "Adjusting");
            ShowPass(2, d2);
            stepsDone++;
            double final = d2;
            if (Math.Abs(d2) > ToleranceMs)
            {
                (newA, newB) = Split(d2 + newA - newB);
                SetDelays((a.Index, newA), (b.Index, newB));
                rowA.Delay = Ms(newA);
                rowB.Delay = Ms(newB);
                await Task.Delay(400, ct);

                // 3. Verifying: one more check after the correction
                final = await MeasureAsync(3, "Verifying");
                ShowPass(3, final);
            }
            else
            {
                rowA.Pass3 = rowB.Pass3 = "—";
            }
            stepsDone = totalSteps;

            var (delayed, delayMs, other) = newA > 0 ? (a, newA, b) : (b, newB, a);
            string text = delayMs < 0.05
                ? $"Done. {a.Name} and {b.Name} already arrive together, so neither needs a delay."
                : $"Done. {other.Name} arrives later, so it keeps 0 ms; {delayed.Name} is delayed by {Ms(delayMs)}.";
            text += Math.Abs(final) <= ToleranceMs
                ? $" They now arrive within {Ms(Math.Abs(final))} of each other."
                : $" They're still {Ms(Math.Abs(final))} apart; run it again, or check the mic can clearly hear both outputs.";
            if (Math.Max(newA, newB) >= MaxDelayMs - 0.05)
                text += $" The difference is more than Voicemeeter's {MaxDelayMs:0} ms maximum delay.";
            return text + " Saved in Voicemeeter.";
        }

        (double A, double B) Split(double bMinusA) =>
            bMinusA > 0 ? (Math.Min(bMinusA, MaxDelayMs), 0) : (0, Math.Min(-bMinusA, MaxDelayMs));

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
            rowA.SetPass(pass, "listening…");
            rowB.SetPass(pass, "listening…");
            SoloBus(a.Index);
            await Task.Delay(150, ct); // let the mutes take effect

            using var device = deviceService.GetDevice(play.Id);
            var player = new BurstPlayer(DeviceService.GetMixFormat(device), times, BeepAmplitude);
            float[] recording;
            using (var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60))
            {
                output.Init(player);
                var recorder = mic ?? throw new DelayException("The microphone was closed.");
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
                        Status($"{label}: beep {beep} of {times.Count} on {(IsA(beep - 1) ? a.Name : b.Name)}…", Math.Clamp(pos / end, 0, 1));
                        if (pos >= end) break;
                    }
                }
                finally
                {
                    try { output.Stop(); } catch { }
                    recording = (mic ?? recorder).EndRecording();
                }
            }

            Status($"{label}: working out the timing…", 1);
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
            $"Couldn't hear the beeps from {bus.Display}. Check that it's on and turned up, that the Voicemeeter strip for " +
            $"{(PlayBox.SelectedItem as DeviceInfo)?.Name ?? "the playback device"} is routed to {bus.Name}, and that the mic can hear it.";

        void CheckSpread(BusChoice bus, List<double> ms)
        {
            double spread = ms.Max() - ms.Min();
            if (spread > 5)
                throw new DelayException($"The beeps from {bus.Name} didn't arrive at a steady time (they varied by {Ms(spread)}). " +
                                         "Keep the room quiet and the mic still, and try again.");
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

    // ---------------------------------------------------------------- lifecycle (called by SetDelayTool)

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
