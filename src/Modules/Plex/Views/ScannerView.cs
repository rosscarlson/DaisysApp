using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.Plex.Scanner;
using DaisysApp.Shared.Video;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// (4.16.0) Scanner: tools that look through the libraries' files. Duplicates (titles with more than one file),
/// Space (what each show, season or movie takes, and roughly what it would with a video profile) and Transcode (a queue
/// of files made smaller with FFmpeg on the graphics card, to one of Core's video profiles).
/// </summary>
internal sealed class ScannerView : Border, IDisposable
{
    private readonly PlexService service;
    private readonly Transcoder transcoder;
    private readonly List<(RadioButton Button, FrameworkElement Panel)> parts = [];

    public ScannerView(PlexService service)
    {
        this.service = service;
        var files = new MediaFiles(service);
        transcoder = new Transcoder(service, files);
        var transcode = new TranscodePanel(service, transcoder);
        var space = new SpacePanel(service, files, transcoder, () => Show(2));
        var duplicates = new DuplicatesPanel(service, files);

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var host = new Grid();
        foreach (var (title, tip, panel) in new (string, string, FrameworkElement)[]
        {
            (T("Duplicates"), T("Titles with more than one file"), duplicates),
            (T("Space"), T("What each show, season or movie takes"), space),
            (T("Transcode"), T("Make files smaller with the graphics card"), transcode),
        })
        {
            var b = new RadioButton { Content = title, ToolTip = tip, GroupName = "PlexScanner" + GetHashCode(), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 4, 12, 4) };
            b.SetResourceReference(StyleProperty, "ChipToggle");
            int i = parts.Count;
            b.Checked += (_, _) => Show(i);
            strip.Children.Add(b);
            panel.Visibility = Visibility.Collapsed;
            host.Children.Add(panel);
            parts.Add((b, panel));
        }
        var layout = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(host);
        Child = Ui.Page(layout);
        parts[0].Button.IsChecked = true;
    }

    private void Show(int index)
    {
        for (int i = 0; i < parts.Count; i++) parts[i].Panel.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (parts[index].Button.IsChecked != true) parts[index].Button.IsChecked = true;
    }

    public void Dispose() => transcoder.Shutdown();

    /// <summary>A drop-down of Core's video profiles, the one transcodes use picked; follows changes to the list.</summary>
    internal static ComboBox ProfileBox(PlexService service, Transcoder t, Action? changed = null)
    {
        var box = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Video profile (Video → Recording's settings)") };
        System.Windows.Automation.AutomationProperties.SetName(box, T("Video profile"));
        bool filling = false;
        void Fill()
        {
            filling = true;
            box.ItemsSource = VideoProfiles.All.Select(p => p.Name).ToList();
            box.SelectedItem = t.Profile.Name;
            filling = false;
        }
        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (filling || box.SelectedItem is not string name || name == service.Settings.TranscodeProfile) return;
            service.Settings.TranscodeProfile = name;
            service.Settings.Save();
            t.RaiseProfile();
        };
        t.ProfileChanged += () => { Fill(); changed?.Invoke(); };
        VideoProfiles.Changed += () => box.Dispatcher.BeginInvoke(() => { Fill(); changed?.Invoke(); });
        return box;
    }

    /// <summary>"HEVC 720p, 1–2 Mbit/s".</summary>
    internal static string Describe(RecordingProfile p) =>
        $"{(p.Codec == "H264" ? "H.264" : p.Codec)} {(p.Height > 0 ? p.Height + "p" : T("same size"))}, "
        + (p.RateControl == RateControl.Cbr ? F("{0:0.#} Mbit/s", p.BitrateMbps) : F("{0:0.#}–{1:0.#} Mbit/s", p.BitrateMbps, p.MaxBitrateMbps));

    /// <summary>A library drop-down's items: movie and TV libraries.</summary>
    internal static async Task<List<PlexLibrary>> VideoLibraries(PlexService service) =>
        (await service.LibrariesAsync()).Where(l => l.Type is "movie" or "show").ToList();

    /// <summary>A label and a field on one line.</summary>
    internal static FrameworkElement Labelled(string label, FrameworkElement field, double width = 110)
    {
        System.Windows.Automation.AutomationProperties.SetName(field, label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = width, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(field);
        return row;
    }
}
