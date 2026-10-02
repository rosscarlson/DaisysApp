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
    private InsertState vmState = InsertState.Off;
    private string? vmDetail;
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
        LevelSlider.Value = Math.Clamp(settings.LevelDb, LevelSlider.Minimum, LevelSlider.Maximum);
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
        if (settings.VoicemeeterEnabled) EnsureVoicemeeter();
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() =>
        {
            refreshDebounce.Stop();
            refreshDebounce.Start();
        });

        initializing = false;
        UpdateLevelText();
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
        if (virtualMixer && haveRemote && !settings.VoicemeeterEnabled)
        {
            settings.VoicemeeterEnabled = true;
            EnsureVoicemeeter();
            OpenChannelVolume(dev);
        }
        UpdateVmUi();

        if (dev?.LayoutKey == currentLayoutKey)
        {
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
                channelVolume = IsVoicemeeterDevice(dev) && settings.VoicemeeterEnabled && VoicemeeterRemote.FindDll() != null
                    ? CreateVoicemeeterLevels(dev)
                    : new ChannelVolume(deviceService.GetDevice(dev.Id), dev.Channels, dev.Layout.Speakers);
                channelVolume.Changed += ChannelVolume_Changed;
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
            double top = cv is VoicemeeterLevels ? 0 : controllable.Max(s => cv.Get(s.Channel));
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
                LevelDb = LevelSlider.Value,
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
        if (mic != null) StartMic();
        ClearReference();
        ClearReadings();
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

        if ((DateTime.Now - lastClip).TotalSeconds < 1.5)
        {
            MicLevelText.Text = "CLIPPING";
            MicLevelText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        }
        else
        {
            MicLevelText.Text = FormatLevel(db);
            MicLevelText.ClearValue(TextBlock.ForegroundProperty);
        }

        MicBandText.Text = band switch
        {
            MicBand.Mains => "500 Hz–2 kHz band",
            MicBand.Lfe => "Subwoofer band",
            _ => "Full range",
        };

        if (sounding != null && IsPlaying && !stopping && !autoRunning && readingWindow.Count >= 5)
            sounding.SetMicReading(db, LevelSlider.Value);
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
    private double? ReferenceDb() => ReferenceRelative() + LevelSlider.Value;

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

    // ---------------------------------------------------------------- auto-level

    private sealed class AutoLevelException(string message) : Exception(message);

    private async void AutoButton_Click(object sender, RoutedEventArgs e)
    {
        if (autoRunning) { autoCts?.Cancel(); return; }
        await RunAutoLevelAsync();
    }

    private async Task<MicReading> MeasureAsync(int milliseconds, CancellationToken ct)
    {
        var m = mic ?? throw new AutoLevelException("The microphone was closed.");
        m.BeginMeasure();
        await Task.Delay(milliseconds, ct);
        return (mic ?? throw new AutoLevelException("The microphone was closed.")).EndMeasure();
    }

    private void SetAutoStatus(string text)
    {
        autoStatus = text;
        UpdateStatus();
    }

    /// <summary>
    /// Plays band-limited pink noise on each target speaker in turn, measures it with the mic, and sets the Windows
    /// channel volumes so every speaker reads the reference level. Up to three measure/adjust passes.
    /// With no reference yet: one speaker → it becomes the reference; several → the quietest one does.
    /// </summary>
    private async Task RunAutoLevelAsync()
    {
        if (autoRunning) return;
        errorMessage = null;
        infoMessage = null;

        var dev = SelectedDevice;
        var cv = channelVolume;
        if (dev == null || cv == null)
        {
            ShowError("This output device's channel volumes can't be controlled.");
            return;
        }
        var targets = speakers.Where(s => s.IsSelected && s.CanTrim).ToList();
        if (targets.Count == 0)
        {
            ShowError("Select the speakers to level.");
            return;
        }
        if (mic == null && !StartMic()) return;

        autoRunning = true;
        autoCts = new CancellationTokenSource();
        var ct = autoCts.Token;
        bool wasPlaying = IsPlaying;
        cycleTimer.Stop();
        UpdateAutoUi();

        string? result = null;
        try
        {
            if (!IsPlaying) StartPlayback();
            if (provider == null) throw new AutoLevelException(errorMessage ?? "Could not start playback.");
            provider.Signal = SignalType.PinkNoiseBand;
            provider.LfeLowPass = true;

            SoundOnly(null);
            SetAutoStatus("Measuring background noise — keep the room quiet…");
            await Task.Delay(500, ct);
            var floor = await MeasureAsync(1000, ct);

            double? target = ReferenceDb();
            string referenceName = reference?.SpeakerName ?? "";
            var level = new Dictionary<SpeakerVm, double>();
            Dictionary<SpeakerVm, double>? prevLevel = null, prevCh = null;
            var shortOf = new List<string>();
            double maxErr = 0;

            for (int pass = 1; pass <= 3; pass++)
            {
                var ch = targets.ToDictionary(s => s, s => cv.Get(s.Channel));
                foreach (var s in targets)
                {
                    SetAutoStatus($"{(pass == 1 ? "Measuring" : "Checking")} {s.Name}…  (Esc to cancel)");
                    SoundOnly(s);
                    await Task.Delay(800, ct); // fade-in, room and capture latency
                    var m = await MeasureAsync(1500, ct);
                    if (m.Peak > 0.98)
                        throw new AutoLevelException("The microphone is clipping. Lower the mic gain or the signal level and try again.");
                    var band = s.IsLfe ? MicBand.Lfe : MicBand.Mains;
                    double margin = m.Get(band) - floor.Get(band);
                    if (margin < 10)
                        throw new AutoLevelException($"Couldn't hear {s.Name} clearly — only {FormatLevel(margin)} above the background noise. Check the speaker, the mic position and the mic gain.");
                    level[s] = m.Get(band);
                    s.SetMicReading(level[s], LevelSlider.Value);
                }
                SoundOnly(null);

                if (prevLevel != null && prevCh != null)
                {
                    foreach (var s in targets)
                    {
                        double dCh = ch[s] - prevCh[s];
                        if (Math.Abs(dCh) >= 2 && Math.Abs(level[s] - prevLevel[s]) < Math.Abs(dCh) * 0.3)
                            throw new AutoLevelException(
                                $"Changing {s.Name}'s Windows channel volume by {dCh.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)} dB made no measurable difference, " +
                                (cv is VoicemeeterLevels vl
                                    ? $"so the speakers aren't on Voicemeeter bus {vl.BusName}. Pick the bus your speakers are connected to under Output device."
                                    : "so this device doesn't apply per-channel volume (common with virtual devices). Try leveling on the physical output device."));
                    }
                }

                if (pass == 1)
                {
                    if (target == null && targets.Count == 1)
                    {
                        target = level[targets[0]];
                        LockReference(target.Value, LevelSlider.Value, targets[0].Name);
                        result = $"Reference set from {targets[0].Name} ({FormatLevel(target.Value)} at the mic). Now select other speakers and press Auto-level.";
                        break;
                    }

                    // Without a usable reference, match everything to the quietest speaker, keeping the current loudest level.
                    double top = targets.Max(s => ch[s]);
                    double quietest = targets.Min(s => level[s] - ch[s]) + top;
                    bool unreachable = target != null && targets.Count > 1 && targets.Any(s => ch[s] + (target.Value - level[s]) > cv.MaxDb + 0.05);
                    if (target == null || unreachable)
                    {
                        if (unreachable) infoMessage = $"The reference was louder than the quietest speaker can reach, so it was lowered by {target!.Value - quietest:0.0} dB.";
                        target = quietest;
                        referenceName = targets.MinBy(s => level[s] - ch[s])!.Name;
                    }
                }

                maxErr = targets.Max(s => Math.Abs(level[s] - target!.Value));
                if (pass > 1 && maxErr <= 0.5) break;
                if (pass == 3) break;

                shortOf.Clear();
                foreach (var s in targets)
                {
                    double want = ch[s] + (target!.Value - level[s]);
                    if (want > cv.MaxDb + 0.05) shortOf.Add($"{s.Name} by {want - cv.MaxDb:0.0} dB");
                    cv.Set(s.Channel, want);
                }
                prevLevel = new Dictionary<SpeakerVm, double>(level);
                prevCh = ch;
                RefreshTrimsFromSystem();
                SetAutoStatus("Adjusting levels…");
                await Task.Delay(400, ct);
            }

            if (result == null)
            {
                LockReference(target!.Value, LevelSlider.Value, referenceName);
                result = $"Leveled {targets.Count} speaker{(targets.Count == 1 ? "" : "s")} to {FormatLevel(target.Value)} at the mic, within ±{maxErr:0.0} dB.";
                if (shortOf.Count > 0) result += " Couldn't raise " + string.Join(", ", shortOf) + " — already at full volume.";
                if (infoMessage != null) result = infoMessage + " " + result;
            }
        }
        catch (OperationCanceledException) { result = "Auto-level cancelled."; }
        catch (AutoLevelException ex) { errorMessage = ex.Message; }
        catch (Exception ex) { errorMessage = "Auto-level failed: " + ex.Message; }
        finally
        {
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
        infoMessage = result;
        UpdateStatus();
    }

    private void UpdateAutoUi()
    {
        AutoLabel.Text = autoRunning ? "Cancel" : "Auto-level";
        AutoIcon.Text = autoRunning ? "" : ""; // Cancel / Auto glyphs
        SpeakerItems.IsHitTestVisible = !autoRunning;
        DeviceBox.IsEnabled = MicBox.IsEnabled = !autoRunning;
        SetRefButton.IsEnabled = ResetTrimsButton.IsEnabled = !autoRunning;
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

    private void LevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing) return;
        settings.LevelDb = LevelSlider.Value;
        if (provider != null) provider.LevelDb = LevelSlider.Value;
        UpdateLevelText();
        UpdateStatus();
        UpdateRefText();
    }

    private void UpdateLevelText() => LevelText.Text = $"{LevelSlider.Value:0.0} dBFS RMS".Replace('-', '−');

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
        StatusText.Text = $"Playing {what} on {where} at {LevelSlider.Value:0.0} dBFS RMS{cycle}".Replace('-', '−');
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
        deviceService.Dispose();
        vmWatchdog.Stop();
        VoicemeeterRemote.Shutdown();
    }

    // ---------------------------------------------------------------- Voicemeeter

    private static bool IsVoicemeeterDevice(DeviceInfo? dev) =>
        dev != null && (dev.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

    private double[] VoicemeeterGains(string bus)
    {
        if (!settings.VoicemeeterGains.TryGetValue(bus, out var g) || g.Length != 8)
            settings.VoicemeeterGains[bus] = g = new double[8];
        return g;
    }

    /// <summary>Selected bus, corrected to one that exists in the running Voicemeeter edition.</summary>
    private (string Name, int Index) CurrentBus()
    {
        var names = VoicemeeterRemote.BusNames(VoicemeeterRemote.Kind);
        if (names.Count == 0) return (settings.VoicemeeterBus, 0);
        int i = names.ToList().IndexOf(settings.VoicemeeterBus);
        if (i < 0) { i = 0; settings.VoicemeeterBus = names[0]; }
        return (names[i], i);
    }

    private VoicemeeterLevels CreateVoicemeeterLevels(DeviceInfo dev)
    {
        var (bus, index) = CurrentBus();
        using var mm = deviceService.GetDevice(dev.Id);
        var map = SpeakerChannelMap.Build(mm, dev.Channels, dev.Layout.Speakers, 8);
        var levels = new VoicemeeterLevels(bus, index, map, VoicemeeterGains(bus));
        levels.ApplyAll();
        return levels;
    }

    /// <summary>Connects to Voicemeeter's bus insert (if not already) and applies the saved gains.</summary>
    private void EnsureVoicemeeter()
    {
        if (!settings.VoicemeeterEnabled) return;
        if (vmState != InsertState.Running || !VoicemeeterRemote.IsRegistered)
        {
            vmState = VoicemeeterRemote.Start(out vmDetail);
            if (vmState == InsertState.Running)
            {
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                ApplyVoicemeeterGains();
            }
        }
        vmWatchdog.Start();
        UpdateVmUi();
    }

    private void ApplyVoicemeeterGains()
    {
        VoicemeeterRemote.ResetAllGains();
        var (bus, index) = CurrentBus();
        var gains = VoicemeeterGains(bus);
        for (int c = 0; c < 8; c++) VoicemeeterRemote.SetGain(index * 8 + c, gains[c]);
    }

    private void VmWatchdog_Tick(object? sender, EventArgs e)
    {
        if (VoicemeeterRemote.TakeChangeRequest()) VoicemeeterRemote.RestartStream();

        var before = vmState;
        if (vmState == InsertState.Running && !VoicemeeterRemote.IsStreaming)
        {
            // Voicemeeter was closed or restarted: release and reconnect when it's back
            VoicemeeterRemote.Unregister();
            vmState = InsertState.NotRunning;
        }
        if (vmState != InsertState.Running)
        {
            vmState = VoicemeeterRemote.Start(out vmDetail);
            if (vmState == InsertState.Running) ApplyVoicemeeterGains();
        }
        if (vmState != before)
        {
            UpdateVmUi();
            if (vmState == InsertState.Running && SelectedDevice is { } dev && IsVoicemeeterDevice(dev)) OpenChannelVolume(dev);
        }
    }

    private void UpdateVmUi()
    {
        VoicemeeterChanged?.Invoke();
        if (VmPanel == null || VmPanel.Visibility != Visibility.Visible) return;

        var names = VoicemeeterRemote.BusNames(VoicemeeterRemote.Kind);
        suppressVmBus = true;
        VmBusBox.ItemsSource = names.Count > 0 ? names : [settings.VoicemeeterBus];
        VmBusBox.SelectedItem = CurrentBus().Name;
        suppressVmBus = false;

        VmRetryButton.Visibility = vmState is InsertState.Busy or InsertState.Error or InsertState.NotRunning ? Visibility.Visible : Visibility.Collapsed;
        VmStatusText.ClearValue(TextBlock.ForegroundProperty);
        VmStatusText.Text = vmState switch
        {
            InsertState.Running => "Active. Daisy's App sets the speaker levels inside Voicemeeter for as long as it's running.",
            InsertState.Busy => $"Voicemeeter's bus insert is in use by \"{vmDetail}\". If that's the 8x8 Matrix, close it (Voicemeeter → Other Tools → Shut Down Matrix 8x8). Daisy's App connects automatically once it's free.",
            InsertState.NotRunning => "Voicemeeter isn't running. Daisy's App connects automatically when it starts.",
            InsertState.Error => vmDetail ?? "Couldn't connect to Voicemeeter.",
            _ => "Not connected.",
        };
        if (vmState is InsertState.Busy or InsertState.Error)
            VmStatusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }

    private void VmBusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || suppressVmBus || VmBusBox.SelectedItem is not string bus || bus == settings.VoicemeeterBus) return;
        settings.VoicemeeterBus = bus;
        ApplyVoicemeeterGains();
        ClearReference();
        ClearReadings();
        OpenChannelVolume(SelectedDevice);
        settings.Save();
    }

    private void VmRetry_Click(object sender, RoutedEventArgs e)
    {
        vmState = InsertState.Off;
        EnsureVoicemeeter();
        if (vmState == InsertState.Running) OpenChannelVolume(SelectedDevice);
    }

    // ---------------------------------------------------------------- settings page (AudioLevelSettingsView)

    /// <summary>Raised whenever the Voicemeeter connection or its settings change.</summary>
    public event Action? VoicemeeterChanged;

    public string VoicemeeterSummary =>
        VoicemeeterRemote.FindDll() == null ? "Voicemeeter isn't installed on this PC."
        : !settings.VoicemeeterIntegration ? "Off. Voicemeeter devices are treated like any other output device."
        : !settings.VoicemeeterEnabled ? "Not in use yet. It turns on the first time you pick a Voicemeeter output device."
        : vmState switch
        {
            InsertState.Running => $"Connected. Speaker levels are applied on bus {settings.VoicemeeterBus}.",
            InsertState.Busy => $"Voicemeeter's bus insert is in use by \"{vmDetail}\".",
            InsertState.NotRunning => "Voicemeeter isn't running. Daisy's App connects automatically when it starts.",
            InsertState.Error => vmDetail ?? "Couldn't connect to Voicemeeter.",
            _ => "Not connected.",
        };

    public bool VoicemeeterProblem => settings.VoicemeeterIntegration && settings.VoicemeeterEnabled && vmState is InsertState.Busy or InsertState.Error;

    /// <summary>Turns the Voicemeeter bus-insert level control on or off. Off releases the insert for other tools.</summary>
    public void SetVoicemeeterIntegration(bool on)
    {
        if (settings.VoicemeeterIntegration == on) return;
        settings.VoicemeeterIntegration = on;
        if (!on && settings.VoicemeeterEnabled)
        {
            settings.VoicemeeterEnabled = false;
            vmWatchdog.Stop();
            VoicemeeterRemote.Shutdown();
            vmState = InsertState.Off;
            ClearReference();
            ClearReadings();
            OpenChannelVolume(SelectedDevice);
        }
        _ = ApplySelectedDeviceAsync(userInitiated: false);
        settings.Save();
        VoicemeeterChanged?.Invoke();
    }
}
