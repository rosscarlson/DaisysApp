using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DaisysApp.Applets.AudioTools.Tests.Audio;
using DaisysApp.Applets.AudioTools.Tests.Controls;
using DaisysApp.Applets.AudioTools.Tests.Eq;
using DaisysApp.Theming;
using DaisysApp.Applets.AudioTools.Tests.Voicemeeter;
using Microsoft.Win32;
using DaisysApp.Applets.AudioTools.Tests.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

using DaisysApp.Shared.Audio;
using DaisysApp.Shared.Voicemeeter;

namespace DaisysApp.Applets.AudioTools.Tests;

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
        resetConfirmTimer.Tick += (_, _) => { resetConfirmTimer.Stop(); ResetTrimsButton.Content = T("Reset levels"); };
        vmWatchdog.Tick += VmWatchdog_Tick;
        deviceService.DevicesChanged += () => Dispatcher.BeginInvoke(() =>
        {
            refreshDebounce.Stop();
            refreshDebounce.Start();
        });
        // Voicemeeter started (or stopped): pick up its devices and reconnect the level control
        VmBanner.ReadyChanged += _ => { devices = new(); RefreshDevices(); };

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

    /// <summary>The microphones, for the Level Wizard's picker.</summary>
    public IReadOnlyList<CaptureDeviceInfo> MicDevices => micDevices;

    /// <summary>The microphone in use; setting it switches to it and remembers it.</summary>
    public CaptureDeviceInfo? SelectedMicDevice
    {
        get => SelectedMic;
        set => MicBox.SelectedItem = value;
    }
    private bool IsPlaying => output != null;

    private static string FormatLevel(double db) => db.ToString("0.0", CultureInfo.CurrentCulture).Replace('-', '−') + " dB";

    // ---------------------------------------------------------------- devices

    private void RefreshDevices()
    {
        RefreshMics();

        List<DeviceInfo> list;
        try { list = deviceService.GetDevices(); }
        catch (Exception ex) { ShowError(T("Could not list audio devices: ") + ex.Message); return; }

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
        LayoutText.Text = dev == null ? "" : F("{0} layout · drag the speakers to match your room", dev.Layout.Name);
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
            errorMessage = T("Playback stopped: the device or its speaker configuration changed.");

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
        PlaceSpeakers(dev);
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
        OpenEq(dev);
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
                ShowError(T("This device's channel volumes can't be controlled: ") + ex.Message);
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
        catch (Exception ex) { ShowError(T("Could not set the channel volume: ") + ex.Message); }
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
            ResetTrimsButton.Content = T("Click to confirm");
            resetConfirmTimer.Start();
            return;
        }
        resetConfirmTimer.Stop();
        ResetTrimsButton.Content = T("Reset levels");

        try
        {
            double top = cv is VoicemeeterEqLevels ? 0 : controllable.Max(s => cv.Get(s.Channel));
            foreach (var s in controllable) cv.Set(s.Channel, top);
            infoMessage = F("All channels set to {0}.", FormatLevel(top));
        }
        catch (Exception ex) { ShowError(T("Could not reset channel volumes: ") + ex.Message); }
        RefreshTrimsFromSystem();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- save / load levels

    /// <summary>A levels file: each speaker's level on the device it was saved from.</summary>
    public sealed record SavedLevels(string Kind, string Device, string? VoicemeeterBus, DateTime Saved, List<SavedLevel> Levels)
    {
        public const string FileKind = "DaisysApp.SpeakerLevels";
    }

    /// <summary>One speaker's level, and its EQ (null in files from before the EQ Wizard, or where there's none to keep).</summary>
    public sealed record SavedLevel(int Channel, string Speaker, double Db, List<EqBand>? Eq = null);

    private static string LevelsFilter => T("Daisy's App speaker levels (*.levels.json)|*.levels.json|All files (*.*)|*.*");

    private void SaveLevels_Click(object sender, RoutedEventArgs e)
    {
        var cv = channelVolume;
        var eq = eqControl;
        var levels = speakers.Where(s => s.CanTrim).Select(s => new SavedLevel(s.Channel, s.Name, s.TrimDb, SavedEq(eq, s.Channel))).ToList();
        if (cv == null || SelectedDevice is not { } dev || levels.Count == 0)
        {
            ShowError(T("This output device's speaker levels can't be read, so there's nothing to save."));
            return;
        }

        Directory.CreateDirectory(AppPaths.DocumentsFolder);
        var dialog = new SaveFileDialog
        {
            Title = T("Save speaker levels"),
            Filter = LevelsFilter,
            InitialDirectory = AppPaths.DocumentsFolder,
            FileName = $"{SafeFileName(dev.Name)} {DateTime.Now:yyyy-MM-dd}.levels.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var file = new SavedLevels(SavedLevels.FileKind, dev.Name, cv is VoicemeeterEqLevels vl ? vl.BusName : null, DateTime.Now, levels);
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(file, DaisysApp.Settings.JsonStore.Options));
            errorMessage = null;
            infoMessage = F("Saved the levels of {0} speakers to {1}.", levels.Count, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { ShowError(T("Couldn't save the levels: ") + ex.Message); return; }
        UpdateStatus();
    }

    private static List<EqBand>? SavedEq(IEqControl? eq, int channel)
    {
        if (eq?.CanControl(channel) != true) return null;
        try { return eq.Get(channel).ToList(); }
        catch { return null; }
    }

    private void LoadLevels_Click(object sender, RoutedEventArgs e)
    {
        var cv = channelVolume;
        if (cv == null || SelectedDevice is not { } dev)
        {
            ShowError(T("This output device's speaker levels can't be set."));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = T("Load speaker levels"),
            Filter = LevelsFilter,
            InitialDirectory = Directory.Exists(AppPaths.DocumentsFolder) ? AppPaths.DocumentsFolder : null,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        SavedLevels? file;
        try { file = JsonSerializer.Deserialize<SavedLevels>(File.ReadAllText(dialog.FileName), DaisysApp.Settings.JsonStore.Options); }
        catch (Exception ex) { ShowError(T("Couldn't read that file: ") + ex.Message); return; }
        if (file is not { Kind: SavedLevels.FileKind } || file.Levels == null)
        {
            ShowError(T("That isn't a speaker levels file saved by Daisy's App."));
            return;
        }

        // Match by channel; a speaker name that differs means the speaker setup changed, which is worth mentioning.
        int applied = 0, renamed = 0, clamped = 0, eqApplied = 0;
        var eq = eqControl;
        try
        {
            foreach (var level in file.Levels)
            {
                var s = speakers.FirstOrDefault(x => x.Channel == level.Channel);
                if (s == null) continue;
                if (level.Eq != null && eq?.CanControl(s.Channel) == true)
                {
                    eq.Set(s.Channel, level.Eq);
                    eqApplied++;
                }
                if (!s.CanTrim) continue;
                if (!string.Equals(s.Name, level.Speaker, StringComparison.OrdinalIgnoreCase)) renamed++;
                double db = Math.Clamp(level.Db, cv.MinDb, cv.MaxDb);
                if (Math.Abs(db - level.Db) > 0.05) clamped++;
                cv.Set(s.Channel, db);
                applied++;
            }
        }
        catch (Exception ex) { ShowError(T("Couldn't set the levels: ") + ex.Message); return; }
        RefreshTrimsFromSystem();
        ClearReference();
        ClearReadings();

        errorMessage = null;
        infoMessage = applied == 0
            ? T("None of the levels in that file match this device's speakers.")
            : (applied == 1 ? F("Loaded the levels of {0} speaker from {1}.", applied, Path.GetFileName(dialog.FileName)) : F("Loaded the levels of {0} speakers from {1}.", applied, Path.GetFileName(dialog.FileName)));
        if (eqApplied > 0) infoMessage += P(eqApplied, " The EQ of {0} speaker was set too.", " The EQ of {0} speakers was set too.");
        if (applied > 0 && !string.Equals(file.Device, dev.Name, StringComparison.OrdinalIgnoreCase))
            infoMessage += F(" They were saved from {0}.", file.Device);
        if (renamed > 0) infoMessage += P(renamed, " {0} speaker has a different name now, so check the speaker setup matches.", " {0} speakers have a different name now, so check the speaker setup matches.");
        if (clamped > 0) infoMessage += (clamped == 1 ? F(" {0} level was outside this device's range ({1} to {2}) and was limited.", clamped, FormatLevel(cv.MinDb), FormatLevel(cv.MaxDb)) : F(" {0} levels were outside this device's range ({1} to {2}) and were limited.", clamped, FormatLevel(cv.MinDb), FormatLevel(cv.MaxDb)));
        UpdateStatus();
    }

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        return name.Trim();
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
            ShowError(T("Could not start playback: ") + ex.Message);
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
            errorMessage = e.Exception != null ? T("Playback stopped: ") + e.Exception.Message : T("Playback stopped.");
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
        settings.Save(); // remembered right away, for the next run
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
            catch (Exception ex) { ShowError(T("Couldn't change the mic level: ") + ex.Message); }
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
        catch (Exception ex) { ShowError(T("Couldn't change the mic level: ") + ex.Message); }
        MicGainText.Text = $"{MicGainSlider.Value:0} %";
    }

    private bool StartMic()
    {
        StopMic();
        if (SelectedMic is not { } sel)
        {
            SetListen(false);
            ShowError(T("Choose a microphone first."));
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
                ShowError(T("The microphone stopped") + (ex != null ? ": " + ex.Message : "."));
            });
            m.Start();
            mic = m;
            Array.Clear(micPower);
            meterTimer.Start();
            SetListen(true);
            if (errorMessage != null && errorMessage.Contains(T("microphone"), StringComparison.OrdinalIgnoreCase)) errorMessage = null;
            UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            SetListen(false);
            bool denied = ex is UnauthorizedAccessException || (ex is COMException c && c.HResult == unchecked((int)0x80070005));
            ShowError(denied
                ? T("Windows blocked microphone access. Turn on Settings → Privacy & security → Microphone → \"Let desktop apps access your microphone\".")
                : T("Could not open the microphone: ") + ex.Message);
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
            MicBand.Mains => T("500 Hz–2 kHz band"),
            MicBand.Lfe => T("Subwoofer band"),
            _ => T("Full range"),
        };

        if (sounding != null && IsPlaying && !stopping && !autoRunning && readingWindow.Count >= 5)
            sounding.SetMicReading(db, SignalLevelDb);
        UpdateDeltas();

        if (sounding?.DeltaDb is double delta)
        {
            MicDeltaText.Text = (Math.Abs(delta) < 0.05 ? "0.0" : delta.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)) + T(" dB vs ref");
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
            ? T("Play one speaker, then lock its level.")
            : F("Locked: {0} from {1}", FormatLevel(reference.MicDb), reference.SpeakerName);
    }

    private async void SetRefButton_Click(object sender, RoutedEventArgs e)
    {
        if (autoRunning) return;
        errorMessage = null;
        var sounding = speakers.Where(s => s.IsSounding).ToList();
        if (!IsPlaying || sounding.Count != 1)
        {
            ShowError(T("Play a single speaker (right-click or 1–9 to solo it), then click Set reference."));
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
            ShowError(T("Couldn't get a reading — keep one speaker playing and try again."));
            return;
        }
        LockReference(db, s.MicSignalDb, s.Name);
        infoMessage = F("Reference locked to {0} ({1}). Play each other speaker and turn its knob until it reads Δ 0.0.", s.Name, FormatLevel(db));
        UpdateStatus();
    }

    // ---------------------------------------------------------------- auto-level (the AutoLevelWindow wizard)

    private sealed class AutoLevelException(string message) : Exception(message);

    /// <summary>The mic clipped during a measurement; the run lowers the mic level and starts over.</summary>
    private sealed class MicClippedException() : Exception(T("The microphone is clipping."));

    private const int SettleMs = 500, MeasureMs = 1500; // 2 s per speaker

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
                ? T("Voicemeeter needs to be running (Banana or Potato) before the speakers can be leveled.")
                : T("This output device doesn't let its speaker levels be set, or has only one speaker."));
            return;
        }

        string savedIn = cv is VoicemeeterEqLevels vl
            ? F("Voicemeeter, bus {0} EQ (stays applied without Daisy's App running)", vl.BusName)
            : T("Windows channel volume for this device (Sound settings → Levels → Balance)");
        var rows = targets.Select(s => new AutoLevelRow(s, FormatLevel(cv.Get(s.Channel)))).ToList();
        bool wasListening = mic != null;
        EnsureMicListening(); // live meter in the wizard; any error shows in the status line
        var window = new AutoLevelWindow(this, rows, dev.Display, savedIn)
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
        if (!wasListening) StopMic();
        UpdateStatus();
    }

    private async Task<MicReading> MeasureAsync(int milliseconds, CancellationToken ct)
    {
        var m = mic ?? throw new AutoLevelException(T("The microphone was closed."));
        m.BeginMeasure();
        await Task.Delay(milliseconds, ct);
        return (mic ?? throw new AutoLevelException(T("The microphone was closed."))).EndMeasure();
    }

    /// <summary>
    /// The wizard's run: plays band-limited pink noise on each included speaker in turn (2 s each) and measures it.
    /// Pass 1 finds the softest speaker and turns every other one down to match it; pass 2 checks; pass 3 runs only if
    /// a speaker is still more than 0.5 dB out. All speakers start from the same level, so the result doesn't depend on
    /// earlier settings. On cancel or error the original levels are put back. Returns the summary to show.
    /// </summary>
    internal async Task<string> RunAutoLevelAsync(IReadOnlyList<AutoLevelRow> rows, Action<string, double> report, CancellationToken ct)
    {
        var cv = channelVolume ?? throw new AutoLevelException(T("This output device's speaker levels can't be set."));
        var targets = rows.Where(r => r.Include).ToList();
        if (targets.Count < 2) throw new AutoLevelException(T("Tick at least two speakers."));
        if (!EnsureMicListening()) throw new AutoLevelException(errorMessage ?? T("Couldn't open the microphone."));

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
            if (provider == null) throw new AutoLevelException(errorMessage ?? T("Couldn't start playback."));
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
                            ? T("The microphone is clipping, and this mic's level can't be set from here. Lower its input volume in Windows Sound settings and try again.")
                            : T("The microphone is still clipping at its lowest level. Turn the speakers or amplifier down and try again."));
                    SyncMicGain();
                    step = 0;
                    Status(F("The microphone was clipping, so its level was lowered to {0:0} %. Starting over…", MicGainPercent ?? 0));
                    await Task.Delay(1500, ct);
                }
            }

            foreach (var r in outcome.Active) r.After = FormatLevel(cv.Get(r.Speaker.Channel));
            succeeded = true;
            step = steps;
            string where = cv is VoicemeeterEqLevels v
                ? F("The levels are saved in Voicemeeter (bus {0} EQ).", v.BusName)
                : T("The levels are saved as this device's Windows channel volumes.");
            string result = outcome.Active.Count < 2
                ? T("Nothing left to level against each other.")
                : F("Done. {0} speakers matched to the softest, {1}, within ±{2:0.0} dB.", outcome.Active.Count, outcome.Baseline, outcome.MaxErr);
            if (outcome.Lift > 0.05)
                result += F(" To keep every cut within {0:0} dB, all speakers were raised by {1:0.0} dB.", MaxCutDb, outcome.Lift);
            result += " " + where;
            if (outcome.ShortOf.Count > 0) result += T(" Out of range: ") + string.Join(", ", outcome.ShortOf) + ".";
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
                    r.After = T("Skipped");
                    lfeNote = F("The subwoofer ({0}) {1}, so it was left out and set to {2}. ", r.Speaker.Name, why, FormatLevel(back)) +
                              T("It may need more power (turn up the sub or its amplifier) to be heard properly.");
                }

                SoundOnly(null);
                Status(T("Measuring the room's background noise. Keep the room quiet…"));
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
                        Status((pass == 1 ? F("Pass {0}: measuring {1} ({2} of {3})…", pass, r.Name, i + 1, round.Count) : F("Pass {0}: checking {1} ({2} of {3})…", pass, r.Name, i + 1, round.Count)));
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
                                r.SetPass(pass, T("too quiet"));
                                SkipLfe(r, F("was too quiet to auto-level (only {0} above the room's background noise)", FormatLevel(margin)));
                                continue;
                            }
                            throw new AutoLevelException(F("Couldn't hear {0} clearly — only {1} above the background noise. Check the speaker, the mic position and the mic gain.", r.Name, FormatLevel(margin)));
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
                                    F("Changing {0}'s level by {1} dB made no measurable difference, ", r.Name, dCh.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)) +
                                    (cv is VoicemeeterEqLevels vl
                                        ? F("so the speakers aren't on Voicemeeter bus {0}. Pick the bus your speakers are connected to under Output device.", vl.BusName)
                                        : T("so this device doesn't apply per-channel volume (common with virtual devices). Try leveling on the physical output device.")));
                        }
                    }

                    if (pass == 1)
                    {
                        // A subwoofer far quieter than every other speaker would drag them all down: leave it out instead.
                        if (active.FirstOrDefault(r => r.Speaker.IsLfe) is { } lfe && active.Count > 2)
                        {
                            double others = active.Where(r => r != lfe).Min(r => level[r] - ch[r]);
                            if (others - (level[lfe] - ch[lfe]) > MaxCutDb)
                                SkipLfe(lfe, F("was {0:0.0} dB quieter than the softest other speaker, too quiet to auto-level", others - (level[lfe] - ch[lfe])));
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
                            SkipLfe(lfe, T("couldn't be balanced with the other speakers"));
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
                        if (want[r] < cv.MinDb - 0.05) shortOf.Add(F("{0} by {1:0.0} dB", r.Name, cv.MinDb - want[r]));
                        else if (want[r] > cv.MaxDb + 0.05) shortOf.Add(F("{0} by {1:0.0} dB", r.Name, want[r] - cv.MaxDb));
                        cv.Set(r.Speaker.Channel, want[r]);
                    }
                    prevLevel = new Dictionary<AutoLevelRow, double>(level);
                    prevCh = ch;
                    RefreshTrimsFromSystem();
                    Status(T("Adjusting levels…"));
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

    // ---------------------------------------------------------------- EQ (the EqWizardWindow wizard)

    private IEqControl? eqControl;

    /// <summary>Seconds of pink noise recorded per speaker per pass.</summary>
    private const double EqMeasureSeconds = 6;

    /// <summary>The test signal's own spectrum, by output sample rate, LFE low-pass and its cutoff.</summary>
    private readonly Dictionary<(int Rate, bool Lfe, double Cutoff), double[]> sourceSpectra = new();

    /// <summary>The EQ goes where the levels go: Voicemeeter's bus EQ, or Equalizer APO for any other device (if it's installed).</summary>
    private void OpenEq(DeviceInfo? dev)
    {
        eqControl?.Dispose();
        eqControl = null;
        if (dev == null) return;
        try
        {
            if (UseVoicemeeter(dev))
            {
                var (bus, index) = CurrentBus();
                eqControl = new VoicemeeterEq(bus, index, VoicemeeterMap(dev));
            }
            else if (!IsVoicemeeterDevice(dev))
                eqControl = EqualizerApoEq.TryCreate(dev.Id, dev.Channels);
        }
        catch { eqControl = null; }
    }

    private void EqButton_Click(object sender, RoutedEventArgs e)
    {
        if (autoRunning) return;
        errorMessage = null;
        infoMessage = null;
        var dev = SelectedDevice;
        var eq = eqControl;
        if (dev == null)
        {
            ShowError(T("No output device."));
            return;
        }
        if (eq == null)
        {
            ShowError(IsVoicemeeterDevice(dev)
                ? T("Voicemeeter needs to be running (Banana or Potato) for the EQ Wizard, as the EQ is kept in its bus EQ.")
                : T("Windows has no per-speaker EQ of its own, so the EQ Wizard needs Equalizer APO (free, at sourceforge.net/projects/equalizerapo). Install it, tick this device in its Configurator, restart, then try again."));
            return;
        }

        var rows = speakers.Where(s => eq.CanControl(s.Channel)).Select(s =>
        {
            IReadOnlyList<EqBand> current;
            try { current = eq.Get(s.Channel); }
            catch { current = []; }
            return new EqRow(s, current);
        }).ToList();
        if (rows.Count == 0)
        {
            ShowError(T("This output device's speakers can't be given an EQ."));
            return;
        }
        string? warning = null;
        try { warning = eq.Warning; } catch { }

        bool wasListening = mic != null;
        EnsureMicListening(); // live meter in the wizard; any error shows in the status line
        var window = new EqWizardWindow(this, settings, rows, dev.Display, eq.Description, warning)
        {
            Owner = Window.GetWindow(this),
        };
        window.ShowDialog();
        if (!wasListening) StopMic();
        UpdateStatus();
    }

    /// <summary>The output, the mic and where the EQ is kept, as they are now (for the wizard's Export diagnostics).</summary>
    internal string DescribeEqSetup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Daisy's App {System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}, {Environment.OSVersion}, exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        var dev = SelectedDevice;
        sb.AppendLine($"output: {dev?.Display} id {dev?.Id}, {dev?.Channels} channels");
        sb.AppendLine($"speakers: {string.Join(", ", speakers.Select(s => $"{s.Name} (channel {s.Channel}{(s.IsLfe ? ", LFE" : "")})"))}");
        sb.AppendLine($"mic level: {MicGainPercent?.ToString("0", CultureInfo.InvariantCulture) ?? "can't be read"} %");
        try { if (mic != null) sb.AppendLine().AppendLine("----- mic -----").AppendLine(mic.Describe()); }
        catch (Exception ex) { sb.AppendLine("mic: " + ex.Message); }
        try { sb.AppendLine().AppendLine("----- EQ now -----").AppendLine(eqControl?.Dump() ?? "(no EQ for this output)"); }
        catch (Exception ex) { sb.AppendLine("EQ: " + ex.Message); }
        return sb.ToString();
    }

    /// <summary>Removes the EQ from the given speakers (the wizard's Remove EQ button).</summary>
    internal string RemoveEq(IReadOnlyList<EqRow> rows)
    {
        var eq = eqControl ?? throw new InvalidOperationException(T("This output device can't be given an EQ."));
        foreach (var r in rows)
        {
            eq.Set(r.Speaker.Channel, []);
            r.Current = [];
            r.ClearResults();
        }
        return P(rows.Count, "EQ removed from {0} speaker.", "EQ removed from {0} speakers.");
    }

    private async Task<double[]> SourceSpectrumAsync(int rate, bool lfe, double cutoff)
    {
        var key = (rate, lfe, lfe ? cutoff : 0);
        if (sourceSpectra.TryGetValue(key, out var s)) return s;
        s = await Task.Run(() => Spectrum.ToGrid(Spectrum.Power(TestSignalProvider.Sample(SignalType.PinkNoise, lfe, cutoff, rate, rate * 12)), rate));
        sourceSpectra[key] = s;
        return s;
    }

    private static string FormatDeviation(double db) => double.IsNaN(db) ? "—" : $"±{db:0.0} dB";

    /// <summary>
    /// The wizard's run: records the room's background noise, then plays full-range pink noise on each included speaker
    /// in turn (6 s each) and works out its response at the mic, designs filters that bring it close to the target and
    /// puts them in place; then measures every speaker again to check. On cancel or error the EQ is put back as it was.
    /// Returns the summary to show.
    /// </summary>
    internal async Task<string> RunEqAsync(IReadOnlyList<EqRow> rows, EqOptions options, Action<string, double> report, CancellationToken ct)
    {
        var eq = eqControl ?? throw new AutoLevelException(T("This output device can't be given an EQ."));
        var targets = rows.Where(r => r.Include).ToList();
        if (targets.Count == 0) throw new AutoLevelException(T("Tick at least one speaker."));
        if (!EnsureMicListening()) throw new AutoLevelException(errorMessage ?? T("Couldn't open the microphone."));

        autoRunning = true;
        autoCts = CancellationTokenSource.CreateLinkedTokenSource(ct); // also cancelled if playback or the mic stops
        ct = autoCts.Token;
        bool wasPlaying = IsPlaying;
        cycleTimer.Stop();
        UpdateAutoUi();

        var snapshot = eq.Snapshot();
        var limits = eq.Limits(options.MaxBoostDb);
        double cutoff = settings.LfeCutoffHz;
        bool succeeded = false;
        string outcome = "cancelled or failed";

        var log = new EqRunLog();
        try
        {
            var dev = SelectedDevice;
            log.Line($"output: {dev?.Display} id {dev?.Id}, {dev?.Channels} channels; speakers: {string.Join(", ", targets.Select(r => $"{r.Name} (channel {r.Speaker.Channel}{(r.Speaker.IsLfe ? ", LFE" : "")})"))}");
            log.Line($"EQ kept in: {eq.Description}");
            log.Options(options, limits, cutoff);
            log.Line($"mic level: {MicGainPercent?.ToString("0", CultureInfo.InvariantCulture) ?? "can't be read"} %");
            if (mic != null) log.Block("mic", mic.Describe());
            log.Block("eq-at-start", eq.Dump());
        }
        catch (Exception ex) { log.Line("couldn't describe the setup: " + ex.Message); }
        int steps = 1 + targets.Count * 2, step = 0;
        void Status(string text) { autoStatus = text; UpdateStatus(); report(text, (double)step / steps); }

        try
        {
            if (!IsPlaying) StartPlayback();
            if (provider == null) throw new AutoLevelException(errorMessage ?? T("Couldn't start playback."));
            provider.Signal = SignalType.PinkNoise;
            provider.LfeLowPass = true;
            int rate = provider.WaveFormat.SampleRate;
            log.Line($"playback format: {provider.WaveFormat}");

            Status(T("Getting ready…"));
            var mainsSource = await SourceSpectrumAsync(rate, false, cutoff);
            var lfeSource = targets.Any(r => r.Speaker.IsLfe) ? await SourceSpectrumAsync(rate, true, cutoff) : mainsSource;
            log.Sources(mainsSource, lfeSource);

            // If the mic clips, lower its input level and start over (the background noise was recorded at the old level).
            const int MaxRestarts = 6;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await EqOnceAsync(mainsSource, lfeSource);
                    break;
                }
                catch (MicClippedException)
                {
                    log.Line("the mic clipped: lowering its level and starting over");
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
                            ? T("The microphone is clipping, and this mic's level can't be set from here. Lower its input volume in Windows Sound settings and try again.")
                            : T("The microphone is still clipping at its lowest level. Turn the speakers or amplifier down and try again."));
                    SyncMicGain();
                    step = 0;
                    Status(F("The microphone was clipping, so its level was lowered to {0:0} %. Starting over…", MicGainPercent ?? 0));
                    await Task.Delay(1500, ct);
                }
            }

            succeeded = true;
            step = steps;
            foreach (var r in targets) r.Current = r.Bands ?? [];
            int withEq = targets.Count(r => r.Bands is { Count: > 0 });
            double before = targets.Select(r => r.Before!.Deviation(f => EqDesigner.TargetDb(r.Target, f), r.From, r.To)).Where(d => !double.IsNaN(d)).DefaultIfEmpty(0).Average();
            double after = targets.Select(r => r.After!.Deviation(f => EqDesigner.TargetDb(r.Target, f), r.From, r.To)).Where(d => !double.IsNaN(d)).DefaultIfEmpty(0).Average();
            string result = withEq == 0
                ? F("Done. None of the {0} speakers needed an EQ (they're within ±{1:0.0} dB of the target).", targets.Count, before)
                : (targets.Count == 1 ? F("Done. EQ set on {0} of {1} speaker: on average within ±{2:0.0} dB of the target, from ±{3:0.0} dB.", withEq, targets.Count, after, before) : F("Done. EQ set on {0} of {1} speakers: on average within ±{2:0.0} dB of the target, from ±{3:0.0} dB.", withEq, targets.Count, after, before));
            result += eq is VoicemeeterEq v ? F(" It's saved in Voicemeeter (bus {0} EQ).", v.BusName) : T(" It's saved in Equalizer APO's config.");
            if (withEq > 0) result += T(" The EQ changes each speaker's loudness a little, so run the Level Wizard again now.");
            report(result, 1);
            infoMessage = result;
            outcome = result;
            return result;

            // one complete run: background noise, measure and EQ every speaker, then check them all
            async Task EqOnceAsync(double[] mains, double[] lfe)
            {
                foreach (var r in targets) eq.Set(r.Speaker.Channel, []); // measure the speakers without the EQ being replaced
                SoundOnly(null);
                Status(T("Measuring the room's background noise. Keep the room quiet…"));
                await Task.Delay(800, ct);
                var floor = await RecordAsync(2.5);
                log.Floor(floor, mic?.SampleRate ?? 48000);
                step++;

                for (int i = 0; i < targets.Count; i++)
                {
                    var r = targets[i];
                    Status(F("Measuring {0} ({1} of {2})…", r.Name, i + 1, targets.Count));
                    var (resp, rec, level) = await MeasureSpeakerAsync(r, floor, r.Speaker.IsLfe ? lfe : mains);
                    double from = r.Speaker.IsLfe ? EqDesigner.LowLimit(resp, 20, Math.Max(40, cutoff)) : EqDesigner.LowLimit(resp, 20);
                    double to = r.Speaker.IsLfe ? Math.Min(Math.Min(cutoff * 1.5, 250), options.UpToHz) : Math.Min(options.UpToHz, 16000);
                    var bands = await Task.Run(() => EqDesigner.Design(resp, options.Target, from, to, limits), ct);
                    log.Measured(r.Name, r.Speaker.Channel, r.Speaker.IsLfe, false, rec, mic?.SampleRate ?? 48000, r.Speaker.IsLfe ? lfe : mains,
                                 options.Calibration, resp, level, options.Target, from, to, bands);
                    r.Before = resp;
                    r.From = from;
                    r.To = to;
                    r.Target = options.Target;
                    r.Bands = bands;
                    r.BeforeText = FormatDeviation(resp.Deviation(f => EqDesigner.TargetDb(options.Target, f), from, to));
                    r.FiltersText = bands.Count.ToString(CultureInfo.CurrentCulture);
                    r.NotifyResults();
                    eq.Set(r.Speaker.Channel, bands);
                    step++;
                }

                await Task.Delay(300, ct);
                try { log.Block("eq-while-checking", eq.Dump()); } catch (Exception ex) { log.Line("couldn't read the EQ back: " + ex.Message); }
                for (int i = 0; i < targets.Count; i++)
                {
                    var r = targets[i];
                    Status(F("Checking {0} with its EQ ({1} of {2})…", r.Name, i + 1, targets.Count));
                    var (resp, rec, level) = await MeasureSpeakerAsync(r, floor, r.Speaker.IsLfe ? lfe : mains);
                    log.Measured(r.Name, r.Speaker.Channel, r.Speaker.IsLfe, true, rec, mic?.SampleRate ?? 48000, r.Speaker.IsLfe ? lfe : mains,
                                 options.Calibration, resp, level, options.Target, r.From, r.To, r.Bands);
                    if (r.Before != null && r.Bands != null) log.Compare(r.Name, r.Before, resp, r.Bands);
                    r.After = resp;
                    r.AfterText = FormatDeviation(resp.Deviation(f => EqDesigner.TargetDb(options.Target, f), r.From, r.To));
                    r.NotifyResults();
                    step++;
                }

                // A big cut that made no difference at all means the EQ isn't reaching the speakers.
                var strong = targets.Where(r => r.Bands!.Any(b => b.GainDb <= -4)).ToList();
                if (strong.Count > 0 && strong.All(r => !EqTookEffect(r)))
                    throw new AutoLevelException(eq is VoicemeeterEq vm
                        ? F("The EQ made no measurable difference, so the speakers aren't on Voicemeeter bus {0}. Pick the bus your speakers are connected to on the Audio Tools page.", vm.BusName)
                        : T("The EQ made no measurable difference, so Equalizer APO isn't working on this device. Open Equalizer APO's Configurator, tick this device, restart Windows, and try again."));
            }

            async Task<float[]> RecordAsync(double seconds)
            {
                var m = mic ?? throw new AutoLevelException(T("The microphone was closed."));
                m.BeginRecording(seconds + 1);
                m.BeginMeasure();
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                m = mic ?? throw new AutoLevelException(T("The microphone was closed."));
                var rec = m.EndRecording();
                if (m.EndMeasure().Peak > 0.98) throw new MicClippedException();
                return rec;
            }

            async Task<(Response Levelled, float[] Recording, double Level)> MeasureSpeakerAsync(EqRow r, float[] floor, double[] source)
            {
                r.IsActive = true;
                SoundOnly(r.Speaker);
                try
                {
                    await Task.Delay(900, ct); // fade-in, room and capture latency
                    var rec = await RecordAsync(EqMeasureSeconds);
                    double micRate = mic?.SampleRate ?? 48000;
                    var resp = await Task.Run(() => Response.Measure(rec, floor, micRate, source, options.Calibration), ct);
                    var (lo, hi) = r.Speaker.IsLfe ? (25.0, Math.Max(40, cutoff)) : (300.0, 3000.0);
                    // level it so the reference band sits on the target (the room curve isn't at 0 dB there)
                    double level = resp.MeanAbove(f => EqDesigner.TargetDb(options.Target, f), lo, hi);
                    if (double.IsNaN(level))
                        throw new AutoLevelException(F("Couldn't hear {0} clearly over the room's background noise. Check the speaker, the mic position and the mic level.", r.Name));
                    return (resp.Shift(-level), rec, level);
                }
                finally
                {
                    SoundOnly(null);
                    r.IsActive = false;
                }
            }
        }
        catch (Exception ex)
        {
            outcome = ex is OperationCanceledException ? "cancelled" : "error: " + ex;
            throw;
        }
        finally
        {
            if (!succeeded)
            {
                try { eq.Restore(snapshot); }
                catch { /* device gone: nothing to restore */ }
            }
            try { log.Block("eq-at-end", eq.Dump()); } catch { }
            log.Save(outcome);
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
            UpdateAutoUi();
            UpdatePlayUi();
        }
    }

    /// <summary>Whether the strongest cut shows in the second measurement (at least a third of it).</summary>
    private static bool EqTookEffect(EqRow r)
    {
        if (r.Before == null || r.After == null || r.Bands == null || r.Bands.Count == 0) return true;
        var cut = r.Bands.MinBy(b => b.GainDb)!;
        double lo = cut.Hz / Math.Pow(2, 1.0 / 6), hi = cut.Hz * Math.Pow(2, 1.0 / 6);
        double drop = r.Before.Mean(lo, hi) - r.After.Mean(lo, hi);
        return double.IsNaN(drop) || drop >= -cut.GainDb / 3;
    }


    private void UpdateAutoUi()
    {
        SpeakerItems.IsHitTestVisible = !autoRunning;
        DeviceBox.IsEnabled = MicBox.IsEnabled = !autoRunning;
        SetRefButton.IsEnabled = ResetTrimsButton.IsEnabled = AutoButton.IsEnabled = EqButton.IsEnabled = !autoRunning;
        SaveLevelsButton.IsEnabled = LoadLevelsButton.IsEnabled = !autoRunning;
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
        PlayLabel.Text = playing ? T("Stop") : T("Play");
        PlayButton.IsEnabled = SelectedDevice != null && !autoRunning;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (StatusText == null) return;
        StatusText.ClearValue(TextBlock.ForegroundProperty);

        if (autoRunning)
        {
            StatusText.Text = autoStatus ?? T("Auto-level…");
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
            if (SelectedDevice == null) StatusText.Text = T("No output device.");
            else if (!speakers.Any(s => s.IsSelected)) StatusText.Text = T("Select one or more speakers, then press Play.");
            else StatusText.Text = T("Ready.");
            return;
        }

        var sounding = speakers.Where(s => s.IsSounding).Select(s => s.Name).ToList();
        string what = settings.Signal switch
        {
            SignalType.PinkNoiseBand => T("band-limited pink noise"),
            SignalType.WhiteNoise => T("white noise"),
            SignalType.Sine => F("{0:0.#} Hz sine", settings.SineFrequency),
            _ => T("pink noise"),
        };
        string where = sounding.Count == 0 ? T("no speakers (none selected)")
                     : sounding.Count == speakers.Count && speakers.Count > 1 ? T("all speakers")
                     : string.Join(", ", sounding);
        string cycle = CycleBox.IsChecked == true && speakers.Count(s => s.IsSelected) > 1
            ? F("  ·  cycling every {0} s", (int)CycleSlider.Value) : "";
        StatusText.Text = F("Playing {0} on {1} at {2:0.0} dBFS RMS{3}", what, where, SignalLevelDb, cycle).Replace('-', '−');
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

    // ---------------------------------------------------------------- lifecycle (called by AudioLevelApplet)

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
        eqControl?.Dispose();
        eqControl = null;
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
        return new VoicemeeterEqLevels(bus, index, VoicemeeterMap(dev));
    }

    /// <summary>Each speaker channel's Voicemeeter bus channel (or -1).</summary>
    private int[] VoicemeeterMap(DeviceInfo dev)
    {
        using var mm = deviceService.GetDevice(dev.Id);
        return SpeakerChannelMap.Build(mm, dev.Channels, dev.Layout.Speakers, 8);
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
        : vmKind == VoicemeeterKind.None ? T("Voicemeeter isn't running. Start it to see and set the speaker levels.")
        : vmKind == VoicemeeterKind.Standard ? T("Standard Voicemeeter has no per-channel bus EQ, so speaker levels need Voicemeeter Banana or Potato.")
        : F("Levels are stored in Voicemeeter, in bus {0}'s EQ (cells 5 and 6 of each channel), so they stay applied without Daisy's App running.", CurrentBus().Name);

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
        VoicemeeterRemote.FindDll() == null ? T("Voicemeeter isn't installed on this PC.")
        : !settings.VoicemeeterIntegration ? T("Off. Voicemeeter devices are treated like any other output device (their level knobs won't do anything).")
        : !vmWatchdog.IsEnabled ? T("Not in use yet. It starts the first time you pick a Voicemeeter output device.")
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

    // ---------------------------------------------------------------- speaker map: grid and dragging

    private int gridSize = SpeakerGrid.DefaultSize;
    private Dictionary<int, GridCell> cells = new();
    private readonly Dictionary<GridCell, Border> cellBorders = new();
    private SpeakerVm? dragVm;
    private Point dragStart, dragGrab;
    private bool dragging;
    private Border? dragTarget;

    /// <summary>Puts each tile in its saved cell (or its default one), on a grid big enough for all of them.</summary>
    private void PlaceSpeakers(DeviceInfo? dev)
    {
        bool listener = dev?.Layout.ShowListener == true;
        gridSize = Math.Clamp(settings.SpeakerGridSize, SpeakerGrid.MinSize, SpeakerGrid.MaxSize);
        while (gridSize * gridSize - (SpeakerGrid.ListenerCell(gridSize, listener) != null ? 1 : 0) < speakers.Count) gridSize++;

        cells = dev == null ? new() : SpeakerGrid.Defaults(dev.Layout, gridSize);
        if (dev != null && settings.SpeakerCellsByDevice.TryGetValue(dev.Id, out var saved))
        {
            // saved cells first, so they win any clash; channels the saved map doesn't know keep their default
            var wanted = speakers.Where(s => saved.ContainsKey(s.Channel)).Select(s => (s.Channel, saved[s.Channel]))
                .Concat(speakers.Where(s => !saved.ContainsKey(s.Channel) && cells.ContainsKey(s.Channel)).Select(s => (s.Channel, cells[s.Channel])))
                .ToList();
            cells = SpeakerGrid.Resolve(wanted, gridSize, SpeakerGrid.ListenerCell(gridSize, listener));
        }

        RoomGrid.Width = SpeakerGrid.Width(gridSize);
        RoomGrid.Height = SpeakerGrid.Height(gridSize);
        var centre = SpeakerGrid.Centre(gridSize);
        Canvas.SetLeft(Listener, centre.X - Listener.Width / 2);
        Canvas.SetTop(Listener, centre.Y - 24);
        BuildCellLayer(listener);
        foreach (var s in speakers) PlaceInCell(s);
    }

    private void PlaceInCell(SpeakerVm s)
    {
        if (!cells.TryGetValue(s.Channel, out var cell)) return;
        var p = SpeakerGrid.CellOrigin(cell);
        s.Place(p.X, p.Y);
    }

    /// <summary>The cell outlines shown while dragging (not in the listener's cell).</summary>
    private void BuildCellLayer(bool listener)
    {
        CellLayer.Children.Clear();
        cellBorders.Clear();
        var reserved = SpeakerGrid.ListenerCell(gridSize, listener);
        for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
            {
                var cell = new GridCell(c, r);
                if (cell == reserved) continue;
                var b = new Border
                {
                    Width = SpeakerLayout.TileWidth,
                    Height = SpeakerLayout.TileHeight,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1.5),
                };
                b.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                var p = SpeakerGrid.CellOrigin(cell);
                Canvas.SetLeft(b, p.X);
                Canvas.SetTop(b, p.Y);
                CellLayer.Children.Add(b);
                cellBorders[cell] = b;
            }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void SpeakerItems_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragVm = null;
        var source = e.OriginalSource as DependencyObject;
        if (autoRunning || FindAncestor<TrimKnob>(source) != null) return; // the knob has its own drag
        if (FindAncestor<FrameworkElement>(source)?.DataContext is not SpeakerVm vm) return;
        dragVm = vm;
        dragStart = e.GetPosition(SpeakerItems);
        dragging = false;
    }

    private void SpeakerItems_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (dragVm is not { } vm || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(SpeakerItems);
        if (!dragging)
        {
            if ((p - dragStart).Length < 8) return; // still a click, not a drag
            dragging = true;
            dragGrab = new Point(dragStart.X - vm.Left, dragStart.Y - vm.Top);
            if (SpeakerItems.ItemContainerGenerator.ContainerFromItem(vm) is UIElement container) Panel.SetZIndex(container, 10);
            CellLayer.Visibility = Visibility.Visible;
            SpeakerItems.CaptureMouse(); // the tile's button loses the press, so the drag doesn't select it
        }
        vm.Place(p.X - dragGrab.X, p.Y - dragGrab.Y);
        HighlightTarget(SpeakerGrid.CellAt(gridSize, new Point(vm.Left, vm.Top)));
        e.Handled = true;
    }

    private void HighlightTarget(GridCell? cell)
    {
        dragTarget?.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        dragTarget = cell == null ? null : cellBorders.GetValueOrDefault(cell);
        dragTarget?.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
    }

    private void SpeakerItems_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dragVm is not { } vm) return;
        if (dragging)
        {
            e.Handled = true;
            var target = SpeakerGrid.CellAt(gridSize, new Point(vm.Left, vm.Top));
            if (cellBorders.ContainsKey(target) && cells.TryGetValue(vm.Channel, out var from) && target != from)
            {
                // dropping on another speaker swaps the two
                foreach (var other in cells.Where(c => c.Value == target).Select(c => c.Key).ToList())
                {
                    cells[other] = from;
                    if (speakers.FirstOrDefault(s => s.Channel == other) is { } o) PlaceInCell(o);
                }
                cells[vm.Channel] = target;
                SaveCells();
            }
        }
        EndDrag();
    }

    private void SpeakerItems_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // only the map itself losing the mouse (e.g. Alt+Tab mid-drag); the tile's button losing it is what starts a drag
        if (dragging && e.OriginalSource == SpeakerItems) EndDrag();
    }

    private void EndDrag()
    {
        var vm = dragVm;
        bool wasDragging = dragging;
        dragVm = null;
        dragging = false;
        if (!wasDragging || vm == null) return;
        if (SpeakerItems.ItemContainerGenerator.ContainerFromItem(vm) is UIElement container) Panel.SetZIndex(container, 0);
        HighlightTarget(null);
        CellLayer.Visibility = Visibility.Collapsed;
        PlaceInCell(vm);
        if (SpeakerItems.IsMouseCaptured) SpeakerItems.ReleaseMouseCapture();
    }

    private void SaveCells()
    {
        if (SelectedDevice is not { } dev) return;
        settings.SpeakerCellsByDevice[dev.Id] = new Dictionary<int, GridCell>(cells);
        settings.Save();
    }

    /// <summary>Grid size from Settings → Audio Tools. Saved positions move onto the new grid.</summary>
    public void SetSpeakerGridSize(int n)
    {
        n = Math.Clamp(n, SpeakerGrid.MinSize, SpeakerGrid.MaxSize);
        int old = settings.SpeakerGridSize;
        if (n == old) return;
        foreach (var id in settings.SpeakerCellsByDevice.Keys.ToList())
            settings.SpeakerCellsByDevice[id] = SpeakerGrid.Rescale(settings.SpeakerCellsByDevice[id], old, n, showListener: true);
        settings.SpeakerGridSize = n;
        settings.Save();
        PlaceSpeakers(SelectedDevice);
    }

    /// <summary>Puts every device's speakers back in their default places.</summary>
    public void ResetSpeakerPositions()
    {
        settings.SpeakerCellsByDevice.Clear();
        settings.Save();
        PlaceSpeakers(SelectedDevice);
    }
}
