using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Tools.AudioLevel.Audio;
using DaisysApp.Tools.AudioLevel.Controls;
using DaisysApp.Theming;
using DaisysApp.Tools.AudioLevel.Voicemeeter;
using Microsoft.Win32;
using DaisysApp.Tools.AudioLevel.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DaisysApp.Tools.AudioLevel;

public partial class AudioLevelView : UserControl
{
    private readonly AudioLevelSettings settings;
    private readonly DeviceService deviceService = new();
    private readonly ObservableCollection<SpeakerVm> speakers = new();
    private readonly DispatcherTimer cycleTimer = new();
    private readonly DispatcherTimer refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer trimRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer resetConfirmTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer vmWatchdog = new() { Interval = TimeSpan.FromSeconds(2) };
    private VoicemeeterKind vmKind;   // what's running; None = Voicemeeter isn't running (or not connected)
    private string? vmError;

    /// <summary>Test signal level: −20 dBFS RMS, the standard for speaker calibration.</summary>
    private const double SignalLevelDb = -20;
    private bool suppressVmBus;

    private List<DeviceInfo> devices = new();
    private List<CaptureDeviceInfo> micDevices = new();
    private string? currentLayoutKey;
    private WasapiOut? output;
    private TestSignalProvider? provider;
    private ILevelControl? channelVolume;
    private MicMeter? mic;
    private readonly double[] micPower = new double[3];
    private DateTime lastClip = DateTime.MinValue;
    private int cycleIndex;
    private bool initializing = true;
    private bool suppressDeviceChange;
    private bool suppressMicChange;
    private bool suppressListen;
    private bool stopping;
    private bool autoRunning;
    private CancellationTokenSource? autoCts;
    private string? autoStatus;
    private string? errorMessage;
    private string? infoMessage;
    private LockedReference? reference;
    private SpeakerVm? meterSpeaker;
    private DateTime meterSince;
    private readonly Queue<double[]> readingWindow = new(); // per-100 ms band powers for the speaker now playing
    private const int ReadingBlocks = 15;                    // 1.5 s rolling average for readings

    /// <summary>The mic level other speakers are compared with (session only), plus the channel volumes at the time.</summary>
    private sealed record LockedReference(double MicDb, double SignalDb, string SpeakerName, Dictionary<int, double> Channels);

    public AudioLevelView(AudioLevelSettings settings)
    {
        this.settings = settings;
        InitializeComponent();

        SpeakerItems.ItemsSource = speakers;

        (settings.Signal switch
        {
            SignalType.PinkNoiseBand => SigPinkBand,
            SignalType.WhiteNoise => SigWhite,
            SignalType.Sine => SigSine,
            _ => SigPink,
        }).IsChecked = true;
        SineFreqBox.Text = settings.SineFrequency.ToString("0.#", CultureInfo.CurrentCulture);
        LfeLowPassBox.IsChecked = settings.LfeLowPass;
        LfeCutoffSlider.Value = Math.Clamp(settings.LfeCutoffHz, LfeCutoffSlider.Minimum, LfeCutoffSlider.Maximum);
        CycleSlider.Value = Math.Clamp(settings.CycleSeconds, 1, 15);
        CycleBox.IsChecked = settings.CycleEnabled;

        cycleTimer.Interval = TimeSpan.FromSeconds(CycleSlider.Value);
        cycleTimer.Tick += CycleTimer_Tick;
        refreshDebounce.Tick += (_, _) => { refreshDebounce.Stop(); RefreshDevices(); };
        trimRefreshTimer.Tick += (_, _) => { trimRefreshTimer.Stop(); RefreshTrimsFromSystem(); };
        meterTimer.Tick += MeterTimer_Tick;
        resetConfirmTimer.Tick += (_, _) => { resetConfirmTimer.Stop(); ResetTrimsButton.Content = "Reset levels"; };
        vmWatchdog.Tick += VmWatchdog_Tick;
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() =>
        {
            refreshDebounce.Stop();
            refreshDebounce.Start();
        });

        initializing = false;
        UpdateCycleText();
        UpdateSignalUi();
        UpdateLfeUi();
        RefreshDevices();
        UpdatePlayUi();
        UpdateAutoUi();
    }

    private DeviceInfo? SelectedDevice => DeviceBox.SelectedItem as DeviceInfo;
    private CaptureDeviceInfo? SelectedMic => MicBox.SelectedItem as CaptureDeviceInfo;
    private bool IsPlaying => output != null;

    private static string FormatLevel(double db) => db.ToString("0.0", CultureInfo.CurrentCulture).Replace('-', '−') + " dB";

    // ---------------------------------------------------------------- devices

    private void RefreshDevices()
    {
        RefreshMics();

        List<DeviceInfo> list;
        try { list = deviceService.GetDevices(); }
        catch (Exception ex) { ShowError("Could not list audio devices: " + ex.Message); return; }

        if (list.Select(d => d.Signature).SequenceEqual(devices.Select(d => d.Signature)))
            return;

        string? keepId = SelectedDevice?.Id ?? settings.DeviceId;
        devices = list;

        suppressDeviceChange = true;
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Id == keepId)
                                 ?? devices.FirstOrDefault(d => d.IsDefault)
                                 ?? devices.FirstOrDefault();
        suppressDeviceChange = false;

        _ = ApplySelectedDeviceAsync(userInitiated: false);
    }

    private void RefreshMics()
    {
        List<CaptureDeviceInfo> list;
        try { list = deviceService.GetCaptureDevices(); }
        catch { return; }
        if (list.SequenceEqual(micDevices)) return;

        string? keepId = SelectedMic?.Id ?? settings.MicDeviceId;
        micDevices = list;
        suppressMicChange = true;
        MicBox.ItemsSource = micDevices;
        MicBox.SelectedItem = micDevices.FirstOrDefault(d => d.Id == keepId)
                              ?? micDevices.FirstOrDefault(d => d.IsDefault)
                              ?? micDevices.FirstOrDefault();
        suppressMicChange = false;
        OpenMicGain();
        UpdateRefText();
    }

    private async void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppressDeviceChange) return;
        await ApplySelectedDeviceAsync(userInitiated: true);
    }

    private async Task ApplySelectedDeviceAsync(bool userInitiated)
    {
        var dev = SelectedDevice;
        NoDeviceText.Visibility = dev == null ? Visibility.Visible : Visibility.Collapsed;
        DeviceSummary.Text = dev?.Summary ?? "";
        LayoutText.Text = dev == null ? "" : $"{dev.Layout.Name} layout";
        if (dev != null) settings.DeviceId = dev.Id;
        bool virtualMixer = IsVoicemeeterDevice(dev) && settings.VoicemeeterIntegration;
        bool haveRemote = VoicemeeterRemote.FindDll() != null;
        DeviceWarning.Visibility = virtualMixer && !haveRemote ? Visibility.Visible : Visibility.Collapsed;
        VmPanel.Visibility = virtualMixer && haveRemote ? Visibility.Visible : Visibility.Collapsed;
        if (virtualMixer && haveRemote) EnsureVoicemeeter();
        UpdateVmUi();

        if (dev?.LayoutKey == currentLayoutKey)
        {
            // same speakers, but the level control may need to switch between Windows volume and Voicemeeter
            if ((channelVolume is VoicemeeterEqLevels) != UseVoicemeeter(dev)) OpenChannelVolume(dev);
            UpdatePlayUi();
            return;
        }

        bool wasPlaying = IsPlaying;
        if (wasPlaying) await StopPlaybackAsync();

        BuildSpeakers(dev);

        if (wasPlaying && dev != null && userInitiated)
            StartPlayback();
        else if (wasPlaying && !userInitiated)
            errorMessage = "Playback stopped: the device or its speaker configuration changed.";

        UpdatePlayUi();
        UpdateRefText();
    }

    private void BuildSpeakers(DeviceInfo? dev)
    {
        foreach (var s in speakers)
        {
            s.PropertyChanged -= Speaker_PropertyChanged;
            s.TrimChangedByUser -= Speaker_TrimChangedByUser;
        }
        speakers.Clear();
        currentLayoutKey = dev?.LayoutKey;
        Listener.Visibility = dev?.Layout.ShowListener == true ? Visibility.Visible : Visibility.Collapsed;

        if (dev != null)
        {
            ulong saved = settings.SelectionByDevice.TryGetValue(dev.Id, out var m) ? m : 1; // default: first channel
            foreach (var def in dev.Layout.Speakers)
            {
                var vm = new SpeakerVm(def) { IsSelected = def.Channel < 64 && (saved & (1UL << def.Channel)) != 0 };
                vm.PropertyChanged += Speaker_PropertyChanged;
                vm.TrimChangedByUser += Speaker_TrimChangedByUser;
                speakers.Add(vm);
            }
        }
        cycleIndex = 0;
        meterSpeaker = null;
        ClearReference();
        OpenChannelVolume(dev);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        devices = new(); // force the lists to rebuild
        micDevices = new();
        RefreshDevices();
    }

    // ---------------------------------------------------------------- Windows channel volume (trims)

    private void OpenChannelVolume(DeviceInfo? dev)
    {
        if (channelVolume != null)
        {
            channelVolume.Changed -= ChannelVolume_Changed;
            channelVolume.Dispose();
            channelVolume = null;
        }
        if (dev != null)
        {
            try
            {
                if (UseVoicemeeter(dev)) channelVolume = CreateVoicemeeterLevels(dev);
                else if (!IsVoicemeeterDevice(dev) || !settings.VoicemeeterIntegration)
                    channelVolume = new ChannelVolume(deviceService.GetDevice(dev.Id), dev.Channels, dev.Layout.Speakers);
                // else: a Voicemeeter device while Voicemeeter can't be controlled (not running, or no bus EQ): no knobs
                if (channelVolume != null) channelVolume.Changed += ChannelVolume_Changed;
            }
            catch (Exception ex)
            {
                ShowError("This device's channel volumes can't be controlled: " + ex.Message);
            }
        }
        RefreshTrimsFromSystem();
    }

    private void ChannelVolume_Changed() => Dispatcher.BeginInvoke(() =>
    {
        trimRefreshTimer.Stop();
        trimRefreshTimer.Start();
    });

    /// <summary>Reads the channel volumes back from Windows (they may also be changed in Sound settings).</summary>
    private void RefreshTrimsFromSystem()
    {
        if (Mouse.Captured is TrimKnob) return; // don't fight a knob mid-drag; it commits a refresh when released
        var cv = channelVolume;
        foreach (var s in speakers)
        {
            s.CanTrim = cv?.CanControl(s.Channel) == true;
            s.BusChannel = s.CanTrim && cv is VoicemeeterEqLevels vm ? vm.BusChannel(s.Channel) : null;
            if (!s.CanTrim) continue;
            s.TrimMin = cv!.MinDb;
            s.TrimMax = cv.MaxDb;
            try { s.SetTrimFromSystem(cv!.Get(s.Channel)); }
            catch { s.CanTrim = false; }
        }
        UpdateRefText();
    }

    private void Speaker_TrimChangedByUser(SpeakerVm vm)
    {
        if (channelVolume?.CanControl(vm.Channel) != true) return;
        if (vm == meterSpeaker)
        {
            readingWindow.Clear();
            meterSince = DateTime.Now.AddMilliseconds(-400); // ~0.3 s for the change to reach the mic
        }
        try { channelVolume.Set(vm.Channel, vm.TrimDb); }
        catch (Exception ex) { ShowError("Could not set the channel volume: " + ex.Message); }
    }

    private void Knob_Committed(object sender, RoutedEventArgs e)
    {
        trimRefreshTimer.Stop();
        trimRefreshTimer.Start();
    }

    private void ResetTrimsButton_Click(object sender, RoutedEventArgs e)
    {
        var cv = channelVolume;
        var controllable = speakers.Where(s => s.CanTrim).ToList();
        if (cv == null || controllable.Count == 0) return;

        if (!resetConfirmTimer.IsEnabled)
        {
            ResetTrimsButton.Content = "Click to confirm";
            resetConfirmTimer.Start();
            return;
        }
        resetConfirmTimer.Stop();
        ResetTrimsButton.Content = "Reset levels";

        try
        {
            double top = cv is VoicemeeterEqLevels ? 0 : controllable.Max(s => cv.Get(s.Channel));
            foreach (var s in controllable) cv.Set(s.Channel, top);
            infoMessage = $"All channels set to {FormatLevel(top)}.";
        }
        catch (Exception ex) { ShowError("Could not reset channel volumes: " + ex.Message); }
        RefreshTrimsFromSystem();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- speakers

    private void Speaker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SpeakerVm.IsSelected)) return;
        SaveSelection();
        UpdateActiveChannels();
    }

    private void SaveSelection()
    {
        if (SelectedDevice is not { } dev) return;
        ulong mask = 0;
        foreach (var s in speakers)
            if (s.IsSelected && s.Channel < 64) mask |= 1UL << s.Channel;
        settings.SelectionByDevice[dev.Id] = mask;
    }

    private void SetSelection(Func<SpeakerVm, bool> predicate)
    {
        foreach (var s in speakers) s.IsSelected = predicate(s);
    }

    private void Solo(SpeakerVm target)
    {
        SetSelection(s => s == target);
        cycleIndex = 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(_ => true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetSelection(_ => false);

    private void Speaker_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (autoRunning) return;
        if ((sender as FrameworkElement)?.DataContext is SpeakerVm vm)
        {
            Solo(vm);
            e.Handled = true;
        }
    }

    /// <summary>Pushes the set of channels that should be sounding right now to the generator and the UI.</summary>
    private void UpdateActiveChannels()
    {
        if (autoRunning) return; // auto-level drives the channels itself

        var selected = speakers.Where(s => s.IsSelected).ToList();
        bool cycling = CycleBox.IsChecked == true && selected.Count > 1;
        HashSet<int> active;
        if (cycling)
        {
            cycleIndex %= selected.Count;
            active = [selected[cycleIndex].Channel];
        }
        else
        {
            active = selected.Select(s => s.Channel).ToHashSet();
        }

        if (provider != null)
            for (int c = 0; c < provider.Channels; c++)
                provider.SetChannelEnabled(c, active.Contains(c));

        bool playing = IsPlaying && !stopping;
        foreach (var s in speakers) s.IsSounding = playing && active.Contains(s.Channel);

        UpdateStatus();
    }

    /// <summary>Sounds exactly one speaker (or none); used by auto-level.</summary>
    private void SoundOnly(SpeakerVm? only)
    {
        if (provider != null)
            for (int c = 0; c < provider.Channels; c++)
                provider.SetChannelEnabled(c, only != null && c == only.Channel);
        foreach (var s in speakers) s.IsSounding = s == only;
    }

    // ---------------------------------------------------------------- playback

    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await TogglePlaybackAsync();

    private async Task TogglePlaybackAsync()
    {
        if (autoRunning) return;
        infoMessage = null;
        if (IsPlaying) await StopPlaybackAsync();
        else StartPlayback();
        UpdatePlayUi();
    }

    private void StartPlayback()
    {
        if (SelectedDevice is not { } dev || IsPlaying) return;
        errorMessage = null;
        try
        {
            var mm = deviceService.GetDevice(dev.Id);
            var format = DeviceService.GetMixFormat(mm);
            var p = new TestSignalProvider(format, dev.Layout.Speakers.Where(s => s.IsLfe).Select(s => s.Channel))
            {
                Signal = settings.Signal,
                LevelDb = SignalLevelDb,
                SineFrequency = settings.SineFrequency,
                LfeLowPass = settings.LfeLowPass,
                LfeCutoff = settings.LfeCutoffHz,
            };
            provider = p;
            output = new WasapiOut(mm, AudioClientShareMode.Shared, true, 60);
            output.PlaybackStopped += Output_PlaybackStopped;
            cycleIndex = 0;
            UpdateActiveChannels();
            output.Init(p);
            output.Play();
            if (CycleBox.IsChecked == true) cycleTimer.Start();
        }
        catch (Exception ex)
        {
            CleanupOutput();
            ShowError("Could not start playback: " + ex.Message);
        }
        UpdateActiveChannels();
    }

    private async Task StopPlaybackAsync()
    {
        if (output == null || stopping) return;
        stopping = true;
        provider?.Mute();
        UpdateActiveChannels();
        await Task.Delay(90); // let the fade-out reach the speakers
        CleanupOutput();
        stopping = false;
        UpdateActiveChannels();
    }

    private void CleanupOutput()
    {
        cycleTimer.Stop();
        if (output != null)
        {
            output.PlaybackStopped -= Output_PlaybackStopped;
            try { output.Stop(); } catch { }
            output.Dispose();
            output = null;
        }
        provider = null;
        foreach (var s in speakers) s.IsSounding = false;
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Only reached for unexpected stops (we unsubscribe before stopping ourselves), e.g. device unplugged.
        Dispatcher.BeginInvoke(() =>
        {
            autoCts?.Cancel();
            CleanupOutput();
            errorMessage = e.Exception != null ? "Playback stopped: " + e.Exception.Message : "Playback stopped.";
            UpdateActiveChannels();
            UpdatePlayUi();
        });
    }

    private void CycleTimer_Tick(object? sender, EventArgs e)
    {
        cycleIndex++;
        UpdateActiveChannels();
    }

    // ---------------------------------------------------------------- microphone

    private void ListenToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || suppressListen) return;
        if (ListenToggle.IsChecked == true) StartMic();
        else StopMic();
    }

    private void MicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppressMicChange) return;
        settings.MicDeviceId = SelectedMic?.Id;
        OpenMicGain();
        if (mic != null) StartMic();
        ClearReference();
        ClearReadings();
    }

    // ---------------------------------------------------------------- mic input level (Windows input volume)

    private MicGain? micGain;
    private bool suppressMicGain;

    /// <summary>Raised when the selected mic's input level changes (here, in the wizard, or in Windows).</summary>
    public event Action? MicGainChanged;

    /// <summary>Live mic meter for the wizard: bar position (0–1), readout text, and whether it's clipping.</summary>
    public event Action<double, string, bool>? MicLevelUpdated;

    /// <summary>The selected mic's Windows input volume in %, or null if it can't be controlled.</summary>
    public double? MicGainPercent
    {
        get
        {
            try { return micGain?.Percent; }
            catch { return null; }
        }
        set
        {
            if (micGain == null || value is not double v) return;
            try { micGain.Percent = v; }
            catch (Exception ex) { ShowError("Couldn't change the mic level: " + ex.Message); }
            SyncMicGain();
        }
    }

    /// <summary>Opens the mic (as Listen does) so the meter runs. False with the reason shown if it can't.</summary>
    internal bool EnsureMicListening() => mic != null || StartMic();

    private void OpenMicGain()
    {
        if (micGain != null)
        {
            micGain.Changed -= MicGain_Changed;
            micGain.Dispose();
            micGain = null;
        }
        if (SelectedMic is { } sel)
        {
            try
            {
                micGain = new MicGain(deviceService.GetDevice(sel.Id));
                micGain.Changed += MicGain_Changed;
            }
            catch { micGain = null; } // some devices don't expose an input volume
        }
        SyncMicGain();
    }

    private void MicGain_Changed() => Dispatcher.BeginInvoke(SyncMicGain);

    private void SyncMicGain()
    {
        double? pct = MicGainPercent;
        MicGainRow.IsEnabled = pct != null;
        suppressMicGain = true;
        MicGainSlider.Value = pct ?? 0;
        suppressMicGain = false;
        MicGainText.Text = pct is double p ? $"{p:0} %" : "—";
        MicGainChanged?.Invoke();
    }

    private void MicGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing || suppressMicGain || micGain == null) return;
        try { micGain.Percent = MicGainSlider.Value; }
        catch (Exception ex) { ShowError("Couldn't change the mic level: " + ex.Message); }
        MicGainText.Text = $"{MicGainSlider.Value:0} %";
    }

    private bool StartMic()
    {
        StopMic();
        if (SelectedMic is not { } sel)
        {
            SetListen(false);
            ShowError("Choose a microphone first.");
            return false;
        }
        try
        {
            var m = new MicMeter(deviceService.GetDevice(sel.Id)) { LfeBandHz = settings.LfeCutoffHz * 1.5 };
            m.Stopped += ex => Dispatcher.BeginInvoke(() =>
            {
                if (mic != m) return;
                StopMic();
                autoCts?.Cancel();
                ShowError("The microphone stopped" + (ex != null ? ": " + ex.Message : "."));
            });
            m.Start();
            mic = m;
            Array.Clear(micPower);
            meterTimer.Start();
            SetListen(true);
            if (errorMessage != null && errorMessage.Contains("microphone", StringComparison.OrdinalIgnoreCase)) errorMessage = null;
            UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            SetListen(false);
            bool denied = ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));
            ShowError(denied
                ? "Windows blocked microphone access. Turn on Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\"."
                : "Could not open the microphone: " + ex.Message);
            return false;
        }
    }

    private void StopMic()
    {
        meterTimer.Stop();
        if (mic != null)
        {
            var m = mic;
            mic = null;
            m.Dispose();
        }
        SetListen(false);
        MicLevelText.Text = "—";
        MicLevelText.ClearValue(TextBlock.ForegroundProperty);
        MicBarScale.ScaleX = 0;
        MicLevelUpdated?.Invoke(0, "—", false);
        MicDeltaText.Text = "";
        MicBandText.Text = "";
    }

    private void SetListen(bool on)
    {
        suppressListen = true;
        ListenToggle.IsChecked = on;
        suppressListen = false;
    }

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        if (mic == null) return;
        var r = mic.TakeMeterReading();
        var band = ContextBand(out var sounding);
        if (sounding != meterSpeaker)
        {
            // a different speaker is playing now: restart smoothing and wait for it to settle before reading it
            meterSpeaker = sounding;
            meterSince = DateTime.Now;
            Array.Clear(micPower);
            readingWindow.Clear();
        }
        var blockPower = new double[3];
        for (int b = 0; b < 3; b++)
        {
            double p = Math.Pow(10, r.Get((MicBand)b) / 10);
            blockPower[b] = p;
            micPower[b] = micPower[b] <= 0 ? p : micPower[b] * 0.75 + p * 0.25; // fast smoothing for the bar
        }
        if (r.Peak > 0.98) lastClip = DateTime.Now;
        if (SettledMs >= 700)
        {
            readingWindow.Enqueue(blockPower);
            while (readingWindow.Count > ReadingBlocks) readingWindow.Dequeue();
        }

        MicBarScale.ScaleX = Math.Clamp((10 * Math.Log10(Math.Max(micPower[(int)band], 1e-12)) + 90) / 90, 0, 1);
        double db = readingWindow.Count > 0
            ? 10 * Math.Log10(Math.Max(readingWindow.Average(w => w[(int)band]), 1e-12))
            : 10 * Math.Log10(Math.Max(micPower[(int)band], 1e-12));

        bool clipping = (DateTime.Now - lastClip).TotalSeconds < 1.5;
        if (clipping)
        {
            MicLevelText.Text = "CLIPPING";
            MicLevelText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
        else
        {
            MicLevelText.Text = FormatLevel(db);
            MicLevelText.ClearValue(TextBlock.ForegroundProperty);
        }
        MicLevelUpdated?.Invoke(MicBarScale.ScaleX, MicLevelText.Text, clipping);

        MicBandText.Text = band switch
        {
            MicBand.Mains => "500 Hz–2 kHz band",
            MicBand.Lfe => "Subwoofer band",
            _ => "Full range",
        };

        if (sounding != null && IsPlaying && !stopping && !autoRunning && readingWindow.Count >= 5)
            sounding.SetMicReading(db, SignalLevelDb);
        UpdateDeltas();

        if (sounding?.DeltaDb is double delta)
        {
            MicDeltaText.Text = (Math.Abs(delta) < 0.05 ? "0.0" : delta.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)) + " dB vs ref";
            if (Math.Abs(delta) <= 0.5) MicDeltaText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            else MicDeltaText.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            MicDeltaText.Text = "";
        }
    }

    /// <summary>Which mic band matches what's playing: the calibration band, the sub band, or full range.</summary>
    private MicBand ContextBand(out SpeakerVm? single)
    {
        var sounding = speakers.Where(s => s.IsSounding).ToList();
        single = sounding.Count == 1 ? sounding[0] : null;
        var signal = autoRunning ? SignalType.PinkNoiseBand : settings.Signal;
        bool lfeLowPass = autoRunning || settings.LfeLowPass;
        if (single != null && single.IsLfe && lfeLowPass && signal != SignalType.Sine) return MicBand.Lfe;
        if (signal == SignalType.PinkNoiseBand && sounding.Count > 0 && sounding.All(s => !s.IsLfe)) return MicBand.Mains;
        return MicBand.Wide;
    }

    // ---------------------------------------------------------------- reference

    private double SettledMs => (DateTime.Now - meterSince).TotalMilliseconds;

    /// <summary>
    /// Reference level relative to the signal level, adjusted for any change of the Windows master volume since it
    /// was locked (a shift every channel shares; one speaker's own knob doesn't move it).
    /// </summary>
    private double? ReferenceRelative()
    {
        var r = reference;
        if (r == null) return null;
        double shift = 0;
        var cv = channelVolume;
        if (cv != null && r.Channels.Count > 0)
        {
            try
            {
                var shifts = r.Channels.Where(kv => cv.CanControl(kv.Key)).Select(kv => cv.Get(kv.Key) - kv.Value).ToList();
                if (shifts.Count > 0) shift = shifts.MinBy(Math.Abs); // smallest move = what every channel shares
            }
            catch { }
        }
        return r.MicDb - r.SignalDb + shift;
    }

    /// <summary>The mic level each speaker should read at the current signal level.</summary>
    private double? ReferenceDb() => ReferenceRelative() + SignalLevelDb;

    private void LockReference(double micDb, double signalDb, string speakerName)
    {
        var snapshot = new Dictionary<int, double>();
        if (channelVolume is { } cv)
            foreach (var s in speakers.Where(s => s.CanTrim))
                snapshot[s.Channel] = cv.Get(s.Channel);
        reference = new LockedReference(micDb, signalDb, speakerName, snapshot);
        UpdateDeltas();
        UpdateRefText();
    }

    private void ClearReference()
    {
        reference = null;
        UpdateDeltas();
        UpdateRefText();
    }

    private void ClearReadings()
    {
        foreach (var s in speakers) s.ClearMicReading();
        meterSpeaker = null;
    }

    private void UpdateDeltas()
    {
        double? refRel = ReferenceRelative();
        foreach (var s in speakers)
            s.DeltaDb = refRel != null && s.MicDb is double m ? m - s.MicSignalDb - refRel.Value : null;
    }

    private void UpdateRefText()
    {
        if (RefText == null) return;
        RefText.Text = reference == null
            ? "Play one speaker, then lock its level."
            : $"Locked: {FormatLevel(reference.MicDb)} from {reference.SpeakerName}";
    }

    private async void SetRefButton_Click(object sender, RoutedEventArgs e)
    {
        if (autoRunning) return;
        errorMessage = null;
        var sounding = speakers.Where(s => s.IsSounding).ToList();
        if (!IsPlaying || sounding.Count != 1)
        {
            ShowError("Play a single speaker (right-click or 1–9 to solo it), then click Set reference.");
            return;
        }
        if (mic == null && !StartMic()) return;

        SetRefButton.IsEnabled = false;
        try
        {
            // give the mic time to settle on this speaker
            for (int i = 0; i < 40 && (readingWindow.Count < ReadingBlocks || sounding[0].MicDb == null); i++)
                await Task.Delay(100);
        }
        finally { SetRefButton.IsEnabled = !autoRunning; }

        var s = sounding[0];
        if (!s.IsSounding || s.MicDb is not double db)
        {
            ShowError("Couldn't get a reading — keep one speaker playing and try again.");
            return;
        }
        LockReference(db, s.MicSignalDb, s.Name);
        infoMessage = $"Reference locked to {s.Name} ({FormatLevel(db)}). Play each other speaker and turn its knob until it reads Δ 0.0.";
        UpdateStatus();
    }

    // ---------------------------------------------------------------- auto-level (the AutoLevelWindow wizard)

    private sealed class AutoLevelException(string message) : Exception(message);

    /// <summary>The mic clipped during a measurement; the run lowers the mic level and starts over.</summary>
    private sealed class MicClippedException() : Exception("The microphone is clipping.");

    private const int SettleMs = 800, MeasureMs = 3200; // 4 s per speaker

    /// <summary>The most auto-level cuts any speaker; beyond that the others are raised instead (Voicemeeter's limit is ±12 dB).</summary>
    private const double MaxCutDb = 10;

    private sealed record Outcome(double MaxErr, string Baseline, List<string> ShortOf, string? LfeNote, double Lift, List<AutoLevelRow> Active);

    private void AutoButton_Click(object sender, RoutedEventArgs e)
    {
        if (autoRunning) return;
        errorMessage = null;
        infoMessage = null;
        var dev = SelectedDevice;
        var cv = channelVolume;
        var targets = speakers.Where(s => s.CanTrim).ToList();
        if (dev == null || cv == null || targets.Count < 2)
        {
            ShowError(cv == null && VmPanel.Visibility == Visibility.Visible
                ? "Voicemeeter needs to be running (Banana or Potato) before the speakers can be leveled."
                : "This output device doesn't let its speaker levels be set, or has only one speaker.");
            return;
        }

        string savedIn = cv is VoicemeeterEqLevels vl
            ? $"Voicemeeter, bus {vl.BusName} EQ (stays applied without Daisy's App running)"
            : "Windows channel volume for this device (Sound settings → Levels → Balance)";
        var rows = targets.Select(s => new AutoLevelRow(s, FormatLevel(cv.Get(s.Channel)))).ToList();
        bool wasListening = mic != null;
        EnsureMicListening(); // live meter in the wizard; any error shows in the status line
        var window = new AutoLevelWindow(this, rows, dev.Display, savedIn,
            SelectedMic?.Display is { } micName ? micName + " (change the microphone on the Audio Leveler tab)" : "No microphone — choose one on the Audio Leveler tab")
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
        if (!wasListening) StopMic();
        UpdateStatus();
    }

    private async Task<MicReading> MeasureAsync(int milliseconds, CancellationToken ct)
    {
        var m = mic ?? throw new AutoLevelException("The microphone was closed.");
        m.BeginMeasure();
        await Task.Delay(milliseconds, ct);
        return (mic ?? throw new AutoLevelException("The microphone was closed.")).EndMeasure();
    }

    /// <summary>
    /// The wizard's run: plays band-limited pink noise on each included speaker in turn (4 s each) and measures it.
    /// Pass 1 finds the softest speaker and turns every other one down to match it; pass 2 checks; pass 3 runs only if
    /// a speaker is still more than 0.5 dB out. All speakers start from the same level, so the result doesn't depend on
    /// earlier settings. On cancel or error the original levels are put back. Returns the summary to show.
    /// </summary>
    internal async Task<string> RunAutoLevelAsync(IReadOnlyList<AutoLevelRow> rows, Action<string, double> report, CancellationToken ct)
    {
        var cv = channelVolume ?? throw new AutoLevelException("This output device's speaker levels can't be set.");
        var targets = rows.Where(r => r.Include).ToList();
        if (targets.Count < 2) throw new AutoLevelException("Tick at least two speakers.");
        if (!EnsureMicListening()) throw new AutoLevelException(errorMessage ?? "Couldn't open the microphone.");

        autoRunning = true;
        autoCts = CancellationTokenSource.CreateLinkedTokenSource(ct); // also cancelled if playback or the mic stops
        ct = autoCts.Token;
        bool wasPlaying = IsPlaying;
        cycleTimer.Stop();
        UpdateAutoUi();

        var original = targets.ToDictionary(r => r, r => cv.Get(r.Speaker.Channel));
        AutoLevelRow? liveRow = null;
        int livePass = 0;
        void Live(double bar, string text, bool clipping) => liveRow?.SetPass(livePass, text);
        MicLevelUpdated += Live;
        bool succeeded = false;
        int steps = 1 + targets.Count * 3, step = 0;
        void Status(string text) { autoStatus = text; UpdateStatus(); report(text, (double)step / steps); }

        try
        {
            if (!IsPlaying) StartPlayback();
            if (provider == null) throw new AutoLevelException(errorMessage ?? "Couldn't start playback.");
            provider.Signal = SignalType.PinkNoiseBand;
            provider.LfeLowPass = true;

            // Start every speaker from the same level: 0 dB in Voicemeeter, or the loudest current Windows channel volume.
            double start = cv is VoicemeeterEqLevels ? 0 : original.Values.Max();

            // If the mic clips, lower its input level and start over: readings taken at the old level no longer compare.
            const int MaxRestarts = 6;
            Outcome outcome;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    outcome = await LevelOnceAsync();
                    break;
                }
                catch (MicClippedException)
                {
                    SoundOnly(null);
                    foreach (var r in targets) { r.IsActive = false; r.ClearResults(); }
                    bool lowered = false;
                    if (attempt < MaxRestarts && micGain != null)
                    {
                        try { lowered = micGain.LowerBy(6); }
                        catch { lowered = false; }
                    }
                    if (!lowered)
                        throw new AutoLevelException(micGain == null
                            ? "The microphone is clipping, and this mic's level can't be set from here. Lower its input volume in Windows Sound settings and try again."
                            : "The microphone is still clipping at its lowest level. Turn the speakers or amplifier down and try again.");
                    SyncMicGain();
                    step = 0;
                    Status($"The microphone was clipping, so its level was lowered to {MicGainPercent ?? 0:0} %. Starting over…");
                    await Task.Delay(1500, ct);
                }
            }

            foreach (var r in outcome.Active) r.After = FormatLevel(cv.Get(r.Speaker.Channel));
            succeeded = true;
            step = steps;
            string where = cv is VoicemeeterEqLevels v
                ? $"The levels are saved in Voicemeeter (bus {v.BusName} EQ)."
                : "The levels are saved as this device's Windows channel volumes.";
            string result = outcome.Active.Count < 2
                ? "Nothing left to level against each other."
                : $"Done. {outcome.Active.Count} speakers matched to the softest, {outcome.Baseline}, within ±{outcome.MaxErr:0.0} dB.";
            if (outcome.Lift > 0.05)
                result += $" To keep every cut within {MaxCutDb:0} dB, all speakers were raised by {outcome.Lift:0.0} dB.";
            result += " " + where;
            if (outcome.ShortOf.Count > 0) result += " Out of range: " + string.Join(", ", outcome.ShortOf) + ".";
            if (outcome.LfeNote != null) result += " " + outcome.LfeNote;
            report(result, 1);
            infoMessage = result;
            return result;

            async Task<MicReading> MeasureCheckedAsync(int milliseconds)
            {
                var m = await MeasureAsync(milliseconds, ct);
                if (m.Peak > 0.98) throw new MicClippedException();
                return m;
            }

            // One complete run: noise floor, then up to three passes. Throws MicClippedException if the mic clips.
            async Task<Outcome> LevelOnceAsync()
            {
                foreach (var r in targets) cv.Set(r.Speaker.Channel, start);
                RefreshTrimsFromSystem();
                var active = targets.ToList();
                string? lfeNote = null;
                double lift = 0;

                // The subwoofer is often much quieter at the mic than the other speakers. Rather than turn everything
                // else far down (or fail), leave it out of the leveling and put it back to where it was.
                void SkipLfe(AutoLevelRow r, string why)
                {
                    active.Remove(r);
                    double back = cv is VoicemeeterEqLevels ? 0 : original[r];
                    cv.Set(r.Speaker.Channel, back);
                    r.IsActive = false;
                    r.After = "Skipped";
                    lfeNote = $"The subwoofer ({r.Speaker.Name}) {why}, so it was left out and set to {FormatLevel(back)}. " +
                              "It may need more power (turn up the sub or its amplifier) to be heard properly.";
                }

                SoundOnly(null);
                Status("Measuring the room's background noise. Keep the room quiet…");
                await Task.Delay(500, ct);
                var floor = await MeasureCheckedAsync(1000);
                step++;

                var level = new Dictionary<AutoLevelRow, double>();
                Dictionary<AutoLevelRow, double>? prevLevel = null, prevCh = null;
                double target = 0, maxErr = 0;
                string baseline = "";
                var shortOf = new List<string>();

                for (int pass = 1; pass <= 3; pass++)
                {
                    var ch = active.ToDictionary(r => r, r => cv.Get(r.Speaker.Channel));
                    var round = active.ToList();
                    for (int i = 0; i < round.Count; i++)
                    {
                        var r = round[i];
                        Status($"Pass {pass}: {(pass == 1 ? "measuring" : "checking")} {r.Name} ({i + 1} of {round.Count})…");
                        r.IsActive = true;
                        liveRow = r;
                        livePass = pass;
                        SoundOnly(r.Speaker);
                        await Task.Delay(SettleMs, ct); // fade-in, room and capture latency
                        var m = await MeasureCheckedAsync(MeasureMs);
                        liveRow = null;
                        r.IsActive = false;
                        step++;
                        var band = r.Speaker.IsLfe ? MicBand.Lfe : MicBand.Mains;
                        double margin = m.Get(band) - floor.Get(band);
                        if (margin < 10)
                        {
                            if (r.Speaker.IsLfe)
                            {
                                r.SetPass(pass, "too quiet");
                                SkipLfe(r, $"was too quiet to auto-level (only {FormatLevel(margin)} above the room's background noise)");
                                continue;
                            }
                            throw new AutoLevelException($"Couldn't hear {r.Name} clearly — only {FormatLevel(margin)} above the background noise. Check the speaker, the mic position and the mic gain.");
                        }
                        level[r] = m.Get(band);
                        r.SetPass(pass, FormatLevel(level[r]));
                        r.Speaker.SetMicReading(level[r], SignalLevelDb);
                    }
                    SoundOnly(null);
                    if (active.Count < 2) return new Outcome(0, "", shortOf, lfeNote, lift, active);

                    if (prevLevel != null && prevCh != null)
                    {
                        foreach (var r in active.Where(prevLevel.ContainsKey))
                        {
                            double dCh = ch[r] - prevCh[r];
                            if (Math.Abs(dCh) >= 2 && Math.Abs(level[r] - prevLevel[r]) < Math.Abs(dCh) * 0.3)
                                throw new AutoLevelException(
                                    $"Changing {r.Name}'s level by {dCh.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)} dB made no measurable difference, " +
                                    (cv is VoicemeeterEqLevels vl
                                        ? $"so the speakers aren't on Voicemeeter bus {vl.BusName}. Pick the bus your speakers are connected to under Output device."
                                        : "so this device doesn't apply per-channel volume (common with virtual devices). Try leveling on the physical output device."));
                        }
                    }

                    if (pass == 1)
                    {
                        // A subwoofer far quieter than every other speaker would drag them all down: leave it out instead.
                        if (active.FirstOrDefault(r => r.Speaker.IsLfe) is { } lfe && active.Count > 2)
                        {
                            double others = active.Where(r => r != lfe).Min(r => level[r] - ch[r]);
                            if (others - (level[lfe] - ch[lfe]) > MaxCutDb)
                                SkipLfe(lfe, $"was {others - (level[lfe] - ch[lfe]):0.0} dB quieter than the softest other speaker, too quiet to auto-level");
                        }

                        // the softest speaker is the baseline; the others come down to it
                        var softest = active.MinBy(r => level[r] - ch[r])!;
                        target = level[softest];
                        baseline = softest.Name;
                    }

                    maxErr = active.Max(r => Math.Abs(level[r] - target));
                    if (pass > 1 && maxErr <= 0.5) break;
                    if (pass == 3)
                    {
                        // still not balanced after the last pass: don't let the subwoofer spoil the result
                        if (active.FirstOrDefault(r => r.Speaker.IsLfe) is { } lfe && Math.Abs(level[lfe] - target) > 1 && active.Count > 2)
                        {
                            SkipLfe(lfe, "couldn't be balanced with the other speakers");
                            maxErr = active.Max(r => Math.Abs(level[r] - target));
                        }
                        break;
                    }

                    var want = active.ToDictionary(r => r, r => ch[r] + (target - level[r]));

                    // Never cut a speaker by more than MaxCutDb: raise all of them by the same amount instead, which keeps
                    // them balanced and leaves room below the ±12 dB limit.
                    double lowest = want.Values.Min();
                    if (lowest < -MaxCutDb)
                    {
                        double up = -MaxCutDb - lowest;
                        foreach (var r in active) want[r] += up;
                        target += up;
                        lift += up;
                    }

                    shortOf.Clear();
                    foreach (var r in active)
                    {
                        if (want[r] < cv.MinDb - 0.05) shortOf.Add($"{r.Name} by {cv.MinDb - want[r]:0.0} dB");
                        else if (want[r] > cv.MaxDb + 0.05) shortOf.Add($"{r.Name} by {want[r] - cv.MaxDb:0.0} dB");
                        cv.Set(r.Speaker.Channel, want[r]);
                    }
                    prevLevel = new Dictionary<AutoLevelRow, double>(level);
                    prevCh = ch;
                    RefreshTrimsFromSystem();
                    Status("Adjusting levels…");
                    await Task.Delay(400, ct);
                }
                return new Outcome(maxErr, baseline, shortOf, lfeNote, lift, active);
            }
        }
        finally
        {
            MicLevelUpdated -= Live;
            if (!succeeded)
            {
                try { foreach (var (r, db) in original) cv.Set(r.Speaker.Channel, db); }
                catch { /* device gone: nothing to restore */ }
            }
            autoRunning = false;
            autoCts?.Dispose();
            autoCts = null;
            autoStatus = null;
            if (provider != null)
            {
                provider.Signal = settings.Signal;
                provider.LfeLowPass = settings.LfeLowPass;
            }
            if (!wasPlaying) await StopPlaybackAsync();
            else
            {
                cycleIndex = 0;
                if (CycleBox.IsChecked == true) cycleTimer.Start();
            }
            UpdateActiveChannels();
            RefreshTrimsFromSystem();
            UpdateAutoUi();
            UpdatePlayUi();
        }
    }

    private void UpdateAutoUi()
    {
        SpeakerItems.IsHitTestVisible = !autoRunning;
        DeviceBox.IsEnabled = MicBox.IsEnabled = !autoRunning;
        SetRefButton.IsEnabled = ResetTrimsButton.IsEnabled = AutoButton.IsEnabled = !autoRunning;
        PlayButton.IsEnabled = !autoRunning && SelectedDevice != null;
        UpdateRefText();
    }

    // ---------------------------------------------------------------- signal controls

    private void Signal_Checked(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        settings.Signal = sender == SigPinkBand ? SignalType.PinkNoiseBand
                        : sender == SigWhite ? SignalType.WhiteNoise
                        : sender == SigSine ? SignalType.Sine
                        : SignalType.PinkNoise;
        if (provider != null && !autoRunning) provider.Signal = settings.Signal;
        ClearReadings();
        UpdateSignalUi();
        UpdateStatus();
    }

    private void UpdateSignalUi()
    {
        SineFreqBox.IsEnabled = SigSine.IsChecked == true;
    }

    private void SineFreqBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplySineFrequency();
    }

    private void SineFreqBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplySineFrequency();

    private void ApplySineFrequency()
    {
        if (double.TryParse(SineFreqBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double hz))
            settings.SineFrequency = Math.Clamp(hz, 10, 20000);
        SineFreqBox.Text = settings.SineFrequency.ToString("0.#", CultureInfo.CurrentCulture);
        if (provider != null) provider.SineFrequency = settings.SineFrequency;
        UpdateStatus();
    }

    private void LfeLowPass_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        settings.LfeLowPass = LfeLowPassBox.IsChecked == true;
        if (provider != null && !autoRunning) provider.LfeLowPass = settings.LfeLowPass;
        ClearReadings();
        UpdateLfeUi();
    }

    private void LfeCutoffSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing) return;
        settings.LfeCutoffHz = LfeCutoffSlider.Value;
        if (provider != null) provider.LfeCutoff = settings.LfeCutoffHz;
        if (mic != null) mic.LfeBandHz = settings.LfeCutoffHz * 1.5;
        UpdateLfeUi();
    }

    private void UpdateLfeUi()
    {
        LfeCutoffRow.IsEnabled = LfeLowPassBox.IsChecked == true;
        LfeCutoffText.Text = $"{LfeCutoffSlider.Value:0} Hz";
    }

    private void CycleBox_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        settings.CycleEnabled = CycleBox.IsChecked == true;
        cycleIndex = 0;
        if (settings.CycleEnabled && IsPlaying && !autoRunning) cycleTimer.Start(); else cycleTimer.Stop();
        UpdateActiveChannels();
    }

    private void CycleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing) return;
        settings.CycleSeconds = (int)CycleSlider.Value;
        cycleTimer.Interval = TimeSpan.FromSeconds(settings.CycleSeconds);
        UpdateCycleText();
        UpdateStatus();
    }

    private void UpdateCycleText() => CycleText.Text = $"{(int)CycleSlider.Value} s";

    // ---------------------------------------------------------------- status / chrome

    private void UpdatePlayUi()
    {
        bool playing = IsPlaying;
        PlayButton.Style = (Style)FindResource(playing ? "DangerButton" : "AccentButton");
        PlayIcon.Text = playing ? "" : ""; // Stop / Play glyphs
        PlayLabel.Text = playing ? "Stop" : "Play";
        PlayButton.IsEnabled = SelectedDevice != null && !autoRunning;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (StatusText == null) return;
        StatusText.ClearValue(TextBlock.ForegroundProperty);

        if (autoRunning)
        {
            StatusText.Text = autoStatus ?? "Auto-level…";
            return;
        }
        if (errorMessage != null)
        {
            StatusText.Text = errorMessage;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            return;
        }
        if (infoMessage != null)
        {
            StatusText.Text = infoMessage;
            return;
        }

        if (!IsPlaying || stopping)
        {
            if (SelectedDevice == null) StatusText.Text = "No output device.";
            else if (!speakers.Any(s => s.IsSelected)) StatusText.Text = "Select one or more speakers, then press Play.";
            else StatusText.Text = "Ready.";
            return;
        }

        var sounding = speakers.Where(s => s.IsSounding).Select(s => s.Name).ToList();
        string what = settings.Signal switch
        {
            SignalType.PinkNoiseBand => "band-limited pink noise",
            SignalType.WhiteNoise => "white noise",
            SignalType.Sine => $"{settings.SineFrequency:0.#} Hz sine",
            _ => "pink noise",
        };
        string where = sounding.Count == 0 ? "no speakers (none selected)"
                     : sounding.Count == speakers.Count && speakers.Count > 1 ? "all speakers"
                     : string.Join(", ", sounding);
        string cycle = CycleBox.IsChecked == true && speakers.Count(s => s.IsSelected) > 1
            ? $"  ·  cycling every {(int)CycleSlider.Value} s" : "";
        StatusText.Text = $"Playing {what} on {where} at {SignalLevelDb:0.0} dBFS RMS{cycle}".Replace('-', '−');
    }

    private void ShowError(string message)
    {
        errorMessage = message;
        UpdateStatus();
    }

    /// <summary>Shortcuts (Space, 1–9, Esc); the shell forwards key presses while this tab is open.</summary>
    public async void HandlePreviewKeyDown(KeyEventArgs e)
    {
        if (autoRunning)
        {
            if (e.Key == Key.Escape) autoCts?.Cancel();
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;
        if (e.OriginalSource is ComboBox or ComboBoxItem) return;

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            errorMessage = null;
            await TogglePlaybackAsync();
            return;
        }

        int n = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (n >= 0 && n < speakers.Count)
        {
            Solo(speakers[n]);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- lifecycle (called by AudioLevelTool)

    /// <summary>The window was hidden to the tray: stop the test signal and the mic. Voicemeeter levels stay applied.</summary>
    public async void OnWindowHidden()
    {
        autoCts?.Cancel();
        if (IsPlaying) await StopPlaybackAsync();
        StopMic();
        settings.Save();
    }

    /// <summary>The app is exiting: release the audio devices and Voicemeeter.</summary>
    public void Shutdown()
    {
        autoCts?.Cancel();
        CleanupOutput();
        StopMic();
        channelVolume?.Dispose();
        channelVolume = null;
        settings.Save();
        micGain?.Dispose();
        micGain = null;
        deviceService.Dispose();
        vmWatchdog.Stop();
        VoicemeeterRemote.Disconnect();
    }

    // ---------------------------------------------------------------- Voicemeeter

    private static bool IsVoicemeeterDevice(DeviceInfo? dev) =>
        dev != null && (dev.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

    /// <summary>Banana and Potato have per-channel bus EQ, which is where the levels are stored.</summary>
    private bool VoicemeeterReady => vmKind is VoicemeeterKind.Banana or VoicemeeterKind.Potato;

    private bool UseVoicemeeter(DeviceInfo? dev) => IsVoicemeeterDevice(dev) && settings.VoicemeeterIntegration && VoicemeeterReady;

    /// <summary>Selected bus, corrected to one that exists in the running Voicemeeter edition.</summary>
    private (string Name, int Index) CurrentBus()
    {
        var names = VoicemeeterRemote.BusNames(vmKind);
        if (names.Count == 0) return (settings.VoicemeeterBus, 0);
        int i = names.ToList().IndexOf(settings.VoicemeeterBus);
        if (i < 0) { i = 0; settings.VoicemeeterBus = names[0]; }
        return (names[i], i);
    }

    private VoicemeeterEqLevels CreateVoicemeeterLevels(DeviceInfo dev)
    {
        var (bus, index) = CurrentBus();
        using var mm = deviceService.GetDevice(dev.Id);
        var map = SpeakerChannelMap.Build(mm, dev.Channels, dev.Layout.Speakers, 8);
        return new VoicemeeterEqLevels(bus, index, map);
    }

    /// <summary>Connects to the Voicemeeter Remote API (once) and starts watching for Voicemeeter starting or stopping.</summary>
    private void EnsureVoicemeeter()
    {
        if (!VoicemeeterRemote.Connect(out vmError))
        {
            vmKind = VoicemeeterKind.None;
            return;
        }
        VoicemeeterRemote.Refresh();
        vmKind = VoicemeeterRemote.Kind;
        vmWatchdog.Start();
    }

    private void VmWatchdog_Tick(object? sender, EventArgs e)
    {
        var kind = VoicemeeterRemote.Kind;
        if (kind != vmKind)
        {
            // Voicemeeter started, stopped or changed edition
            vmKind = kind;
            if (SelectedDevice is { } dev && IsVoicemeeterDevice(dev)) OpenChannelVolume(dev);
            UpdateVmUi();
            return;
        }
        if (channelVolume is VoicemeeterEqLevels levels) levels.Poll(); // changes made in Voicemeeter's own EQ dialog
    }

    private void UpdateVmUi()
    {
        VoicemeeterChanged?.Invoke();
        if (VmPanel == null || VmPanel.Visibility != Visibility.Visible) return;

        var names = VoicemeeterRemote.BusNames(vmKind);
        suppressVmBus = true;
        VmBusBox.ItemsSource = names.Count > 0 ? names : [settings.VoicemeeterBus];
        VmBusBox.SelectedItem = CurrentBus().Name;
        suppressVmBus = false;
        VmBusBox.IsEnabled = VoicemeeterReady;

        // Only shown when something needs attention (not running, wrong edition, EQ warning); otherwise hidden.
        string? warning = (channelVolume as VoicemeeterEqLevels)?.OtherEqWarning();
        string? shown = warning ?? (VoicemeeterReady && vmError == null ? null : VoicemeeterStatus());
        VmStatusText.Text = shown ?? "";
        VmStatusText.Visibility = shown == null ? Visibility.Collapsed : Visibility.Visible;
        if (warning != null || vmError != null || vmKind == VoicemeeterKind.Standard)
            VmStatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else
            VmStatusText.ClearValue(TextBlock.ForegroundProperty);
    }

    private string VoicemeeterStatus() =>
        vmError != null ? vmError
        : vmKind == VoicemeeterKind.None ? "Voicemeeter isn't running. Start it to see and set the speaker levels."
        : vmKind == VoicemeeterKind.Standard ? "Standard Voicemeeter has no per-channel bus EQ, so speaker levels need Voicemeeter Banana or Potato."
        : $"Levels are stored in Voicemeeter, in bus {CurrentBus().Name}'s EQ (cells 5 and 6 of each channel), so they stay applied without Daisy's App running.";

    private void VmBusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppressVmBus || VmBusBox.SelectedItem is not string bus || bus == settings.VoicemeeterBus) return;
        settings.VoicemeeterBus = bus;
        ClearReference();
        ClearReadings();
        OpenChannelVolume(SelectedDevice);
        UpdateVmUi();
        settings.Save();
    }

    // ---------------------------------------------------------------- settings page (AudioLevelSettingsView)

    /// <summary>Raised whenever the Voicemeeter connection or its settings change.</summary>
    public event Action? VoicemeeterChanged;

    public string VoicemeeterSummary =>
        VoicemeeterRemote.FindDll() == null ? "Voicemeeter isn't installed on this PC."
        : !settings.VoicemeeterIntegration ? "Off. Voicemeeter devices are treated like any other output device (their level knobs won't do anything)."
        : !vmWatchdog.IsEnabled ? "Not in use yet. It starts the first time you pick a Voicemeeter output device."
        : VoicemeeterStatus();

    public bool VoicemeeterProblem => settings.VoicemeeterIntegration && vmWatchdog.IsEnabled && (vmError != null || vmKind == VoicemeeterKind.Standard);

    /// <summary>Turns setting the levels in Voicemeeter's bus EQ on or off. Levels already saved there are left as they are.</summary>
    public void SetVoicemeeterIntegration(bool on)
    {
        if (settings.VoicemeeterIntegration == on) return;
        settings.VoicemeeterIntegration = on;
        ClearReference();
        ClearReadings();
        _ = ApplySelectedDeviceAsync(userInitiated: false);
        settings.Save();
        VoicemeeterChanged?.Invoke();
    }
}
