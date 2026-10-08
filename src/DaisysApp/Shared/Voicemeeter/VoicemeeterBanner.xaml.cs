using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DaisysApp.Shared.Voicemeeter;

/// <summary>
/// Checks every few seconds that Voicemeeter (Banana or Potato) is installed and running, and shows what to do when it
/// isn't: a link to download it, or a button to start it. Hidden while everything is fine.
/// </summary>
public partial class VoicemeeterBanner : UserControl
{
    private const string DownloadUrl = "https://vb-audio.com/Voicemeeter/";

    private enum State { Unknown, Ready, NotInstalled, NotRunning, Standard, Error }

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private State state = State.Unknown;
    private DateTime startedAt = DateTime.MinValue;

    /// <summary>Raised when Voicemeeter becomes ready (installed, running, Banana or Potato) or stops being ready.</summary>
    public event Action<bool>? ReadyChanged;

    public VoicemeeterBanner()
    {
        InitializeComponent();
        timer.Tick += (_, _) => Check();
        Loaded += (_, _) => { Check(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    public bool IsReady => state == State.Ready;

    public void Check()
    {
        string? error = null;
        var kind = VoicemeeterKind.None;
        State now;
        if (VoicemeeterRemote.FindDll() == null) now = State.NotInstalled;
        else if (!VoicemeeterRemote.Connect(out error)) now = State.Error;
        else
        {
            kind = VoicemeeterRemote.Kind;
            now = kind switch
            {
                VoicemeeterKind.None => State.NotRunning,
                VoicemeeterKind.Standard => State.Standard,
                _ => State.Ready,
            };
        }
        if (now == state) return;

        bool wasReady = state == State.Ready;
        state = now;
        Visibility = now == State.Ready ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.IsEnabled = true;
        switch (now)
        {
            case State.NotInstalled:
                MessageText.Text = T("Voicemeeter isn't installed. The Audio Leveler and Audio Delay use it to set speaker levels and output delays (Voicemeeter Banana or Potato).");
                ActionButton.Content = T("Get Voicemeeter");
                break;
            case State.NotRunning:
                bool starting = (DateTime.Now - startedAt).TotalSeconds < 20;
                MessageText.Text = starting
                    ? T("Starting Voicemeeter…")
                    : T("Voicemeeter isn't running. The Audio Leveler and Audio Delay need it to set speaker levels and output delays.");
                ActionButton.Content = T("Start Voicemeeter");
                ActionButton.IsEnabled = !starting && FindProgram() != null;
                break;
            case State.Standard:
                MessageText.Text = T("This is standard Voicemeeter. Per-speaker levels and output delays need Voicemeeter Banana or Potato.");
                ActionButton.Content = T("Get Banana or Potato");
                break;
            case State.Error:
                MessageText.Text = error ?? T("Couldn't connect to Voicemeeter.");
                ActionButton.Content = T("Get Voicemeeter");
                break;
        }
        if (wasReady != (now == State.Ready)) ReadyChanged?.Invoke(now == State.Ready);
    }

    /// <summary>The installed edition's program, preferring Potato, then Banana, then standard (64-bit first).</summary>
    private static string? FindProgram()
    {
        string? dll = VoicemeeterRemote.FindDll();
        if (dll == null) return null;
        string dir = Path.GetDirectoryName(dll)!;
        foreach (var exe in new[] { "voicemeeter8x64.exe", "voicemeeter8.exe", "voicemeeterpro_x64.exe", "voicemeeterpro.exe", "voicemeeter_x64.exe", "voicemeeter.exe" })
        {
            string path = Path.Combine(dir, exe);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (state == State.NotRunning && FindProgram() is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                startedAt = DateTime.Now;
                MessageText.Text = T("Starting Voicemeeter…");
                ActionButton.IsEnabled = false;
                state = State.Unknown; // re-evaluate on the next check
                return;
            }
            Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageText.Text = T("Couldn't do that: ") + ex.Message;
        }
    }
}
