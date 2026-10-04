using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DaisysApp.Theming;
using DaisysApp.Tools.AudioLevel.ViewModels;

namespace DaisysApp.Tools.AudioLevel;

/// <summary>One speaker in the auto-level wizard: whether it's included, and what each pass measured.</summary>
public sealed class AutoLevelRow(SpeakerVm speaker, string before) : INotifyPropertyChanged
{
    private bool include = true, canEdit = true, isActive;
    private string pass1 = "", pass2 = "", pass3 = "", after = "";

    public SpeakerVm Speaker { get; } = speaker;
    public string Name => Speaker.BusChannel is int b ? $"{Speaker.Name}  (Out {b + 1})" : Speaker.Name;
    public string Before { get; } = before;

    public bool Include { get => include; set => Set(ref include, value); }
    public bool CanEdit { get => canEdit; set => Set(ref canEdit, value); }
    public bool IsActive { get => isActive; set => Set(ref isActive, value); }
    public string Pass1 { get => pass1; set => Set(ref pass1, value); }
    public string Pass2 { get => pass2; set => Set(ref pass2, value); }
    public string Pass3 { get => pass3; set => Set(ref pass3, value); }
    public string After { get => after; set => Set(ref after, value); }

    public void SetPass(int pass, string text)
    {
        if (pass == 1) Pass1 = text; else if (pass == 2) Pass2 = text; else Pass3 = text;
    }

    public void ClearResults() => Pass1 = Pass2 = Pass3 = After = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The auto-level wizard: measures every included speaker, turns the louder ones down to match the softest, checks,
/// and leaves the levels saved where the device keeps them (Voicemeeter's bus EQ, or Windows channel volume).
/// The measuring itself is done by <see cref="AudioLevelView.RunAutoLevelAsync"/>.
/// </summary>
public partial class AutoLevelWindow : Window
{
    private readonly AudioLevelView view;
    private readonly List<AutoLevelRow> rows;
    private CancellationTokenSource? cts;
    private bool closeWhenDone;

    public AutoLevelWindow(AudioLevelView view, IReadOnlyList<AutoLevelRow> rows, string output, string savedIn, string mic)
    {
        this.view = view;
        this.rows = rows.ToList();
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        OutputText.Text = output;
        SavedInText.Text = savedIn;
        MicText.Text = mic;
        RowList.ItemsSource = this.rows;
        StatusText.Text = "Choose the speakers to level, check the microphone is at the listening position, then press Start.";
    }

    private bool Running => cts != null;

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (Running)
        {
            cts!.Cancel();
            return;
        }

        cts = new CancellationTokenSource();
        foreach (var r in rows)
        {
            r.CanEdit = false;
            r.ClearResults();
        }
        StartButton.Style = (Style)FindResource("DangerButton");
        StartIcon.Text = "";
        StartLabel.Text = "Cancel";
        CloseButton.IsEnabled = false;
        StatusText.ClearValue(ForegroundProperty);

        try
        {
            StatusText.Text = await view.RunAutoLevelAsync(rows, Report, cts.Token);
            ProgressScale.ScaleX = 1;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled. The levels are back to how they were.";
            ProgressScale.ScaleX = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message + " The levels are back to how they were.";
            StatusText.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
            ProgressScale.ScaleX = 0;
        }
        finally
        {
            cts.Dispose();
            cts = null;
            foreach (var r in rows)
            {
                r.CanEdit = true;
                r.IsActive = false;
            }
            StartButton.Style = (Style)FindResource("AccentButton");
            StartIcon.Text = "";
            StartLabel.Text = "Start again";
            CloseButton.IsEnabled = true;
        }
        if (closeWhenDone) Close();
    }

    private void Report(string status, double progress)
    {
        StatusText.Text = status;
        ProgressScale.ScaleX = Math.Clamp(progress, 0, 1);
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
        // Cancel first; the run restores the levels and then closes the window.
        e.Cancel = true;
        closeWhenDone = true;
        cts!.Cancel();
    }
}
