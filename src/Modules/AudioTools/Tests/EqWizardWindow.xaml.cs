using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Applets.AudioTools.Tests.Controls;
using DaisysApp.Applets.AudioTools.Tests.Eq;
using DaisysApp.Applets.AudioTools.Tests.ViewModels;
using DaisysApp.Shared.Audio;
using DaisysApp.Theming;
using Microsoft.Win32;

namespace DaisysApp.Applets.AudioTools.Tests;

/// <summary>One speaker in the EQ Wizard: whether it's included, what was measured and the filters it got.</summary>
public sealed class EqRow : INotifyPropertyChanged
{
    private bool include = true, canEdit = true, isActive, isShown;
    private string beforeText = "", filtersText, afterText = "";

    public EqRow(SpeakerVm speaker, IReadOnlyList<EqBand> current)
    {
        Speaker = speaker;
        Current = current;
        filtersText = CurrentText;
    }

    public SpeakerVm Speaker { get; }
    public string Name => Speaker.BusChannel is int b ? F("{0}  (Out {1})", Speaker.Name, b + 1) : Speaker.Name;

    /// <summary>The EQ the speaker had when the wizard opened (or after the last run).</summary>
    public IReadOnlyList<EqBand> Current { get; set; }

    private string CurrentText => Current.Count > 0 ? F("{0} now", Current.Count) : "";

    public bool Include { get => include; set => Set(ref include, value); }
    public bool CanEdit { get => canEdit; set => Set(ref canEdit, value); }
    public bool IsActive { get => isActive; set => Set(ref isActive, value); }
    public bool IsShown { get => isShown; set => Set(ref isShown, value); }
    public string BeforeText { get => beforeText; set => Set(ref beforeText, value); }
    public string FiltersText { get => filtersText; set => Set(ref filtersText, value); }
    public string AfterText { get => afterText; set => Set(ref afterText, value); }

    // results of the last run (set by AudioLevelView.RunEqAsync)
    public Response? Before { get; set; }
    public Response? After { get; set; }
    public IReadOnlyList<EqBand>? Bands { get; set; }
    public double From { get; set; } = 20;
    public double To { get; set; } = 20000;
    public EqTarget Target { get; set; }

    /// <summary>Raised when the results change, so the graph can redraw.</summary>
    public event Action<EqRow>? ResultsChanged;

    public void NotifyResults() => ResultsChanged?.Invoke(this);

    public void ClearResults()
    {
        Before = After = null;
        Bands = null;
        BeforeText = AfterText = "";
        FiltersText = CurrentText;
        NotifyResults();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>What the EQ Wizard was asked for.</summary>
public sealed record EqOptions(EqTarget Target, double UpToHz, double MaxBoostDb, MicCalibration? Calibration);

/// <summary>
/// The EQ Wizard: measures each included speaker's response with the microphone, works out peaking filters that even
/// it out, puts them in place, and measures again to check. The measuring is done by <see cref="AudioLevelView.RunEqAsync"/>.
/// </summary>
public partial class EqWizardWindow : Window
{
    private const string GreenBrush = "#2EAD5B";

    private readonly AudioLevelView view;
    private readonly AudioLevelSettings settings;
    private readonly List<EqRow> rows;
    private CancellationTokenSource? cts;
    private bool closeWhenDone, loading = true, removeArmed;
    private MicCalibration? calibration;
    private EqRow? shown;

    public EqWizardWindow(AudioLevelView view, AudioLevelSettings settings, IReadOnlyList<EqRow> rows, string output, string savedIn, string? warning)
    {
        this.view = view;
        this.settings = settings;
        this.rows = rows.ToList();
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        OutputText.Text = output;
        SavedInText.Text = savedIn;
        eqWarning = warning;
        ShowWarnings();
        MicBox.ItemsSource = view.MicDevices;
        MicBox.SelectedItem = view.SelectedMicDevice;
        RowList.ItemsSource = this.rows;
        foreach (var r in this.rows) r.ResultsChanged += r2 => { if (r2 == shown) DrawGraph(); };
        foreach (var r in this.rows) r.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(EqRow.IsActive) && s is EqRow { IsActive: true } active) ShowRow(active);
        };

        TargetBox.SelectedIndex = settings.EqTarget == EqTarget.RoomCurve ? 0 : 1;
        RangeBox.SelectedIndex = settings.EqUpToHz <= 300 ? 0 : settings.EqUpToHz <= 1000 ? 1 : 2;
        BoostBox.SelectedIndex = settings.EqMaxBoostDb <= 0 ? 0 : settings.EqMaxBoostDb <= 3 ? 1 : 2;
        loading = false;

        LoadCalibrationForMic();
        ShowRow(this.rows.FirstOrDefault());
        StatusText.Text = T("Put the microphone at the listening position, at ear height and pointing at the ceiling, choose the speakers, then press Start.");

        view.MicLevelUpdated += OnMicLevel;
        view.MicGainChanged += SyncMicGain;
        Closed += (_, _) =>
        {
            view.MicLevelUpdated -= OnMicLevel;
            view.MicGainChanged -= SyncMicGain;
        };
        SyncMicGain();
    }

    private bool Running => cts != null;

    private readonly string? eqWarning;

    /// <summary>The EQ's own warning, and whether the mic can't be read raw.</summary>
    private void ShowWarnings()
    {
        var text = string.Join("\n", new[] { eqWarning, view.MicRawWarning }.Where(s => s != null));
        WarningText.Text = text;
        WarningText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    // ---------------------------------------------------------------- microphone

    private void MicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicBox.SelectedItem is CaptureDeviceInfo mic && mic != view.SelectedMicDevice) view.SelectedMicDevice = mic;
        if (!loading)
        {
            LoadCalibrationForMic();
            ShowWarnings();
        }
    }

    private bool syncingGain;

    private void OnMicLevel(double bar, string text, bool clipping)
    {
        MicBarScale.ScaleX = bar;
        MicLevelText.Text = text;
        if (clipping)
        {
            MicLevelText.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
            MicBar.SetResourceReference(BackgroundProperty, "DangerBrush");
        }
        else
        {
            MicLevelText.ClearValue(ForegroundProperty);
            MicBar.SetResourceReference(BackgroundProperty, "SuccessBrush");
        }
    }

    private void SyncMicGain()
    {
        double? pct = view.MicGainPercent;
        syncingGain = true;
        MicGainSlider.Value = pct ?? 0;
        syncingGain = false;
        MicGainText.Text = pct is double p ? $"{p:0} %" : "—";
        MicGainRow.IsEnabled = pct != null && !Running;
    }

    private void MicGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (syncingGain || !IsLoaded) return;
        view.MicGainPercent = MicGainSlider.Value;
    }

    // ---------------------------------------------------------------- calibration file (kept per microphone)

    internal static string CalibrationFolder => Path.Combine(AppPaths.SettingsFolder, T("Mic calibration"));

    private void LoadCalibrationForMic()
    {
        calibration = null;
        string? id = view.SelectedMicDevice?.Id;
        if (id != null && settings.MicCalibrationByMic.TryGetValue(id, out var path) && File.Exists(path))
        {
            try { calibration = MicCalibration.Load(path); }
            catch { calibration = null; }
        }
        ShowCalibration();
    }

    private void ShowCalibration()
    {
        CalText.Text = calibration != null
            ? F("{0} ({1} points). Used for this microphone.", calibration.Name, calibration.Points)
            : T("None, so the mic's own response is measured too. Load the file for your mic: for the iMM-6, Dayton Audio has it on its website for the serial number on the mic. If there's a 0° and a 90° file, use the 90° one (the mic points at the ceiling).");
        ClearCalButton.IsEnabled = calibration != null && !Running;
    }

    private void LoadCal_Click(object sender, RoutedEventArgs e)
    {
        if (view.SelectedMicDevice is not { } mic)
        {
            ShowError(T("Choose the microphone first."));
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = T("Load the microphone's calibration file"),
            Filter = T("Calibration files (*.txt;*.cal;*.frd)|*.txt;*.cal;*.frd|All files (*.*)|*.*"),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var cal = MicCalibration.Load(dialog.FileName);
            // keep a copy, so it still works if the download is moved or deleted
            Directory.CreateDirectory(CalibrationFolder);
            string copy = Path.Combine(CalibrationFolder, Path.GetFileName(dialog.FileName));
            if (!string.Equals(Path.GetFullPath(copy), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                File.Copy(dialog.FileName, copy, overwrite: true);
            settings.MicCalibrationByMic[mic.Id] = copy;
            settings.Save();
            calibration = cal;
            ShowCalibration();
            StatusText.ClearValue(ForegroundProperty);
            StatusText.Text = F("Calibration loaded: {0}.", cal.Name);
        }
        catch (Exception ex) { ShowError(T("Couldn't load that file: ") + ex.Message); }
    }

    private void ClearCal_Click(object sender, RoutedEventArgs e)
    {
        if (view.SelectedMicDevice is { } mic && settings.MicCalibrationByMic.Remove(mic.Id)) settings.Save();
        calibration = null;
        ShowCalibration();
    }

    // ---------------------------------------------------------------- options

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        var o = Options();
        settings.EqTarget = o.Target;
        settings.EqUpToHz = o.UpToHz;
        settings.EqMaxBoostDb = o.MaxBoostDb;
        settings.Save();
    }

    private static double TagOf(ComboBox box, double fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: string t } && double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private EqOptions Options() => new(
        TargetBox.SelectedIndex == 0 ? EqTarget.RoomCurve : EqTarget.Flat,
        TagOf(RangeBox, 1000),
        TagOf(BoostBox, 3),
        calibration);

    // ---------------------------------------------------------------- graph

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EqRow row) ShowRow(row);
    }

    private void ShowRow(EqRow? row)
    {
        if (shown != null) shown.IsShown = false;
        shown = row;
        if (row != null) row.IsShown = true;
        DrawGraph();
    }

    private void DrawGraph()
    {
        var r = shown;
        GraphTitle.Text = r == null ? T("Response") : r.Speaker.Name;
        if (r == null)
        {
            Graph.Show([], 20, 20000);
            FilterList.Text = "";
            return;
        }
        var grid = Response.Grid;
        var bands = r.Bands ?? r.Current;
        var curves = new List<GraphCurve>();
        if (r.Before != null)
            curves.Add(new GraphCurve(grid.Select(f => EqDesigner.TargetDb(r.Target, f)).ToArray(), "TextSecondaryBrush", 1, Dashed: true, Opacity: 0.6));
        if (bands.Count > 0) curves.Add(new GraphCurve(grid.Select(f => EqBand.ResponseDb(bands, f)).ToArray(), GreenBrush, 1.6, Dashed: true));
        if (r.Before != null) curves.Add(new GraphCurve(r.Before.Db, "TextSecondaryBrush", 1.4, Opacity: 0.9));
        if (r.After != null) curves.Add(new GraphCurve(r.After.Db, "AccentBrush", 2.2));
        Graph.Show(curves, r.Before != null ? r.From : 20, r.Before != null ? r.To : 20000);

        FilterList.Text = bands.Count == 0
            ? (r.Before == null ? T("No EQ on this speaker yet.") : NoFiltersText(r))
            : (r.Bands == null ? T("EQ now: ") : T("Filters: ")) + string.Join("  ·  ", bands);
    }

    /// <summary>Why a measured speaker got no filters.</summary>
    private static string NoFiltersText(EqRow r)
    {
        string text = F("No filters needed: from {0} to {1} it has no peaks worth cutting.", EqBand.FormatHz(r.From), EqBand.FormatHz(r.To));
        if (!r.Speaker.IsLfe && EqDesigner.ShortOfBassBelow(r.Before!, r.Target) is var hz && !double.IsNaN(hz))
            text += " " + F("Below about {0} it's under the target because a small speaker's range ends there; the EQ doesn't boost that, as it would only strain the speaker.", EqBand.FormatHz(hz));
        return text;
    }

    // ---------------------------------------------------------------- run

    private void SetRunning(bool running)
    {
        foreach (var r in rows)
        {
            r.CanEdit = !running;
            if (!running) r.IsActive = false;
        }
        StartButton.Style = (Style)FindResource(running ? "DangerButton" : "AccentButton");
        StartIcon.Text = running ? "" : "";
        StartLabel.Text = running ? T("Cancel") : T("Start");
        CloseButton.IsEnabled = RemoveButton.IsEnabled = ExportButton.IsEnabled = !running;
        MicBox.IsEnabled = LoadCalButton.IsEnabled = !running;
        TargetBox.IsEnabled = RangeBox.IsEnabled = BoostBox.IsEnabled = !running;
        MicGainRow.IsEnabled = !running && view.MicGainPercent != null;
        ShowCalibration();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Running)
        {
            cts!.Cancel();
            return;
        }

        cts = new CancellationTokenSource();
        foreach (var r in rows) r.ClearResults();
        SetRunning(true);
        StatusText.ClearValue(ForegroundProperty);
        try
        {
            StatusText.Text = await view.RunEqAsync(rows, Options(), Report, cts.Token);
            ProgressScale.ScaleX = 1;
        }
        catch (OperationCanceledException)
        {
            foreach (var r in rows) r.ClearResults();
            StatusText.Text = T("Cancelled. The EQ is back to how it was.");
            ProgressScale.ScaleX = 0;
        }
        catch (Exception ex)
        {
            foreach (var r in rows) r.ClearResults();
            ShowError(ex.Message + T(" The EQ is back to how it was."));
            ProgressScale.ScaleX = 0;
        }
        finally
        {
            cts.Dispose();
            cts = null;
            SetRunning(false);
            StartLabel.Text = T("Start again");
            SyncMicGain();
            DrawGraph();
        }
        if (closeWhenDone) Close();
    }

    private void Report(string status, double progress)
    {
        StatusText.Text = status;
        ProgressScale.ScaleX = Math.Clamp(progress, 0, 1);
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = rows.Where(r => r.Include).ToList();
        if (targets.Count == 0)
        {
            ShowError(T("Tick the speakers to remove the EQ from."));
            return;
        }
        if (!removeArmed)
        {
            removeArmed = true;
            RemoveButton.Content = T("Click to confirm");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); removeArmed = false; RemoveButton.Content = T("Remove EQ"); };
            timer.Start();
            return;
        }
        removeArmed = false;
        RemoveButton.Content = T("Remove EQ");
        try
        {
            StatusText.ClearValue(ForegroundProperty);
            StatusText.Text = view.RemoveEq(targets);
        }
        catch (Exception ex) { ShowError(T("Couldn't remove the EQ: ") + ex.Message); }
        DrawGraph();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EqRunLog.ExportWithDialog(this, view.DescribeEqSetup(), CalibrationFolder) is not { } path) return;
            StatusText.ClearValue(ForegroundProperty);
            StatusText.Text = F("Diagnostics saved: {0}", path);
        }
        catch (Exception ex) { ShowError(T("Couldn't export the diagnostics: ") + ex.Message); }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (Running) cts!.Cancel();
        else Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!Running) return;
        // Cancel first; the run puts the EQ back and then closes the window.
        e.Cancel = true;
        closeWhenDone = true;
        cts!.Cancel();
    }
}
