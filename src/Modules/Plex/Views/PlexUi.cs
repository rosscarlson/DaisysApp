using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>The module's own pieces (the rest is Core's <see cref="Ui"/>).</summary>
internal static class PlexUi
{
    /// <summary>Plex's amber, for warnings.</summary>
    public const string Amber = "#E5A00D";

    /// <summary>A poster from the server, scaled down by its photo transcoder, loaded as it shows.</summary>
    public static Image Poster(PlexClient? client, string? thumb, double width, double height)
    {
        var img = new Image { Width = width, Height = height, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
        SetPoster(img, client, thumb);
        return img;
    }

    public static void SetPoster(Image img, PlexClient? client, string? thumb)
    {
        if (client == null || string.IsNullOrEmpty(thumb)) { img.Source = null; return; }
        if (img.Tag as string == thumb) return;
        img.Tag = thumb;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = client.Photo(thumb, (int)(img.Width * 2), (int)(img.Height * 2));
            b.DecodePixelWidth = (int)(img.Width * 2);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            b.EndInit();
            img.Source = b;
        }
        catch { img.Source = null; }
    }

    /// <summary>A list's rows in the app's colours (WPF's own are white).</summary>
    public static readonly Style ListRow = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="Padding" Value="6,3,6,3"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Bg" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowHoverBrush}"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowSelectedBrush}"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        """);

    /// <summary>A line of label: value pairs, the labels in the secondary colour.</summary>
    public static TextBlock Facts(params (string Label, string? Value)[] facts)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        bool first = true;
        foreach (var (label, value) in facts)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (!first) t.Inlines.Add(new System.Windows.Documents.Run("   "));
            first = false;
            var l = new System.Windows.Documents.Run(label + " ");
            l.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondaryBrush");
            t.Inlines.Add(l);
            t.Inlines.Add(new System.Windows.Documents.Run(value));
        }
        return t;
    }

    /// <summary>A small coloured label (Direct Play, Transcode…).</summary>
    public static Border Tag(string text, string brushKey)
    {
        var b = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Ui.SetBrush(b, Border.BackgroundProperty, brushKey);
        b.Child = new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        return b;
    }

    /// <summary>A problem line: a coloured dot and the text.</summary>
    public static FrameworkElement Finding(string level, string text, UIElement? action = null)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 5, 9, 0), VerticalAlignment = VerticalAlignment.Top };
        Ui.SetBrush(dot, System.Windows.Shapes.Shape.FillProperty, level == "error" ? "DangerBrush" : level == "warn" ? Amber : level == "info" ? "AccentBrush" : "SuccessBrush");
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        if (action is FrameworkElement a)
        {
            DockPanel.SetDock(a, Dock.Right);
            a.Margin = new Thickness(10, 0, 0, 0);
            a.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(a);
        }
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    public static Button Small(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A Confirm? button in the small size.</summary>
    public static Button SmallConfirm(string text, Action click, string? tip = null)
    {
        var b = ConfirmButton.Make(text, click, tip);
        b.Padding = new Thickness(10, 3, 10, 3);
        b.Margin = new Thickness(0, 0, 6, 6);
        return b;
    }

    public static WrapPanel Buttons(params UIElement[] buttons)
    {
        var w = new WrapPanel();
        foreach (var b in buttons) w.Children.Add(b);
        return w;
    }

    /// <summary>"in 3 days", "2 hours ago"…</summary>
    public static string Ago(DateTime? when)
    {
        if (when is not { } w) return "—";
        var d = DateTime.Now - w;
        if (d < TimeSpan.Zero) return w.ToString("g");
        return d.TotalMinutes < 1 ? T("just now")
            : d.TotalHours < 1 ? P((int)d.TotalMinutes, "{0} minute ago", "{0} minutes ago")
            : d.TotalDays < 1 ? P((int)d.TotalHours, "{0} hour ago", "{0} hours ago")
            : P((int)d.TotalDays, "{0} day ago", "{0} days ago");
    }

    /// <summary>Milliseconds as 1:02:03 or 2:03.</summary>
    public static string Clock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
