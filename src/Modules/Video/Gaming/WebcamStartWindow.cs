using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Record → Start virtual webcam: says what the camera shows, and chooses its sound. A webcam carries no sound, and
/// Discord takes sound only from a microphone, so the game's sound (one playback device, recorded as it plays) can be
/// played into a virtual cable, whose other end Discord then uses as its microphone. The choice is remembered.
/// </summary>
internal sealed class WebcamStartWindow : Window
{
    private readonly GamingSettings settings;
    private readonly RadioButton noSound = new() { GroupName = "Sound", Margin = new Thickness(0, 0, 0, 6) };
    private readonly RadioButton withSound = new() { GroupName = "Sound" };
    private readonly ComboBox fromBox = new() { MinWidth = 340 }, toBox = new() { MinWidth = 340 };
    private readonly StackPanel soundRows = new() { Margin = new Thickness(26, 10, 0, 0) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };

    /// <summary>The sound chosen: from and to (playback device ids), or both null for none.</summary>
    public string? AudioFrom { get; private set; }
    public string? AudioTo { get; private set; }

    public WebcamStartWindow(GamingSettings settings, string what)
    {
        this.settings = settings;
        Title = T("Virtual webcam");
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var p = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        p.Children.Add(new TextBlock { Text = T("Virtual webcam"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        p.Children.Add(Secondary(F("Starts \"{0}\": {1}, with your cameras over it, as a camera Discord, Teams, OBS or a browser can pick. In Discord: User Settings → Voice & Video → Camera.", VirtualCamera.CameraName, what), 6));
        if (!VirtualCamera.IsRegistered)
            p.Children.Add(Secondary(T("Windows doesn't know the camera yet: starting adds it (Windows asks for an administrator's OK, once)."), 8));

        var card = new Border { Margin = new Thickness(0, 14, 0, 0) };
        card.SetResourceReference(StyleProperty, "Card");
        var sound = new StackPanel();
        sound.Children.Add(new TextBlock { Text = T("Sound"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        noSound.Content = T("None (Discord keeps using your microphone)");
        withSound.Content = T("Send the game's sound to Discord");
        noSound.Checked += (_, _) => soundRows.IsEnabled = false;
        withSound.Checked += (_, _) => soundRows.IsEnabled = true;
        sound.Children.Add(noSound);
        sound.Children.Add(withSound);
        soundRows.Children.Add(Row(T("The game plays on"), fromBox));
        soundRows.Children.Add(Row(T("Send it to"), toBox));
        soundRows.Children.Add(Secondary(T("Discord takes sound only from a microphone, not from a webcam. This plays the game's sound into a virtual cable (Voicemeeter's AUX or VAIO3 input, or VB-CABLE); in Discord, choose the cable's other end (e.g. Voicemeeter Out B2, or CABLE Output) as the input device under Voice & Video. Pick the device only the game plays on, so nothing else goes with it."), 8));
        sound.Children.Add(soundRows);
        card.Child = sound;
        p.Children.Add(card);

        var devices = AudioRelay.PlaybackDevices();
        foreach (var (id, name) in devices) fromBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        foreach (var (id, name) in devices.OrderBy(d => AudioRelay.LooksLikeCable(d.Name) ? 0 : 1))
            toBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        // (the game's sound: the one chosen before, else Windows' main playback device, not its Communications one)
        Pick(fromBox, settings.WebcamAudioFrom ?? AudioRelay.MainPlayback());
        Pick(toBox, settings.WebcamAudioTo ?? devices.FirstOrDefault(d => AudioRelay.LooksLikeCable(d.Name)).Id);
        if (!devices.Any(d => AudioRelay.LooksLikeCable(d.Name)))
            soundRows.Children.Add(Secondary(T("No virtual cable found. Voicemeeter has some; VB-CABLE is a free one from the same maker."), 6));
        (settings.WebcamAudioTo != null ? withSound : noSound).IsChecked = true;
        soundRows.IsEnabled = withSound.IsChecked == true;

        error.SetResourceReference(ForegroundProperty, "ErrorTextBrush");
        p.Children.Add(error);

        var start = new Button { Content = T("Start"), MinWidth = 90, IsDefault = true };
        start.SetResourceReference(StyleProperty, "AccentButton");
        start.Click += (_, _) => Start();
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(start);
        buttons.Children.Add(cancel);
        p.Children.Add(buttons);
        Content = p;
    }

    private static void Pick(ComboBox box, string? id)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == id) ?? box.Items.Cast<ComboBoxItem>().FirstOrDefault();
    }

    private static FrameworkElement Row(string label, ComboBox box)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var t = new TextBlock { Text = label, MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(t, Dock.Left);
        row.Children.Add(t);
        row.Children.Add(box);
        return row;
    }

    private static TextBlock Secondary(string text, double top) => Ui.Note(text, top);

    private void Start()
    {
        if (withSound.IsChecked == true)
        {
            string? from = (fromBox.SelectedItem as ComboBoxItem)?.Tag as string, to = (toBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (from == null || to == null) { Fail(T("Choose where the game's sound comes from and where it goes.")); return; }
            if (from == to) { Fail(T("The sound can't be sent to the device it comes from: choose a virtual cable to send it to.")); return; }
            (AudioFrom, AudioTo) = (from, to);
        }
        settings.WebcamAudioFrom = AudioFrom ?? settings.WebcamAudioFrom;
        settings.WebcamAudioTo = AudioTo;
        settings.Save();
        if (!VirtualCamera.IsRegistered && VirtualCamera.Register() is { } problem)
        {
            Fail(F("The camera couldn't be added to Windows: {0}", problem));
            return;
        }
        DialogResult = true;
    }

    private void Fail(string text)
    {
        error.Text = text;
        error.Visibility = Visibility.Visible;
    }
}
