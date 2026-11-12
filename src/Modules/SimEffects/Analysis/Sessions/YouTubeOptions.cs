using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DaisysApp.Shared.Network;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.15.0) The Analysis Manager's YouTube window: the app's YouTube account (Core's
/// <see cref="YouTubeAccountView"/>) and what Analysis does with it: each race's video uploaded when it's saved, who
/// can watch it, and its file deleted once it's up. Kept in AnalysisVideos.json (<see cref="RaceVideos"/>). One window.
/// </summary>
internal static class YouTubeOptions
{
    private static Window? open;

    /// <summary>YouTube's mark: a red rounded box with a white play triangle.</summary>
    public static FrameworkElement Logo(double width = 18)
    {
        double h = width * 0.7;
        var box = new Border { Width = width, Height = h, CornerRadius = new CornerRadius(h * 0.28), Background = Ui.Brush(Color.FromRgb(0xFF, 0x00, 0x33)) };
        var play = new Path
        {
            Data = Geometry.Parse($"M0,0 L{h * 0.42:0.##},{h * 0.24:0.##} L0,{h * 0.48:0.##} Z"), Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(h * 0.08, 0, 0, 0),
        };
        return new Grid { Children = { box, play }, VerticalAlignment = VerticalAlignment.Center };
    }

    public static void Show(FrameworkElement owner)
    {
        if (open is { IsLoaded: true }) { open.Activate(); return; }
        var w = open = Ui.Dialog(T("YouTube"), owner, 480);
        w.Closed += (_, _) => open = null;
        var all = RaceVideos.All;
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        body.Children.Add(Ui.Header(T("Account")));
        body.Children.Add(new YouTubeAccountView { Margin = new Thickness(0, 6, 0, 16) });

        body.Children.Add(Ui.Header(T("Race videos")));
        var auto = new CheckBox { Content = T("Upload each race's video"), IsChecked = all.YouTubeAuto, Margin = new Thickness(0, 8, 0, 8), ToolTip = T("When its recording is saved") };
        auto.Click += (_, _) => { all.YouTubeAuto = auto.IsChecked == true; all.Save(); };
        body.Children.Add(auto);
        var privacy = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = T("Private videos can't play here; an unverified Google project's uploads are private") };
        foreach (var p in Enum.GetValues<YouTubePrivacy>())
            privacy.Items.Add(new ComboBoxItem { Content = p switch { YouTubePrivacy.Private => T("Private"), YouTubePrivacy.Public => T("Public"), _ => T("Unlisted") }, Tag = p });
        privacy.SelectedIndex = (int)all.YouTubePrivacy;
        privacy.SelectionChanged += (_, _) => { if (privacy.SelectedItem is ComboBoxItem { Tag: YouTubePrivacy p }) { all.YouTubePrivacy = p; all.Save(); } };
        System.Windows.Automation.AutomationProperties.SetName(privacy, T("Upload as"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = T("Upload as"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(privacy);
        body.Children.Add(row);
        var delete = new CheckBox { Content = T("Delete the video file once it's uploaded"), IsChecked = all.YouTubeDeleteLocal, ToolTip = T("Watched from YouTube") };
        delete.Click += (_, _) => { all.YouTubeDeleteLocal = delete.IsChecked == true; all.Save(); };
        body.Children.Add(delete);

        var close = new Button { Content = T("Close"), MinWidth = 90, Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        close.Click += (_, _) => w.Close();
        body.Children.Add(close);
        w.Content = body;
        w.Show();
    }
}
