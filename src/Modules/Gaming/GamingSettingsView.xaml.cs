using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DaisysApp.Applets.Gaming;

/// <summary>Settings → Gaming.</summary>
public partial class GamingSettingsView : UserControl
{
    private readonly GamingService service;
    private readonly GamingView view;
    private bool loading = true;

    private static readonly string[] BackgroundColors = { "#000000", "#202020", "#0B1A33", "#0E2A16", "#2A0E14", "#1E1033" };
    private static readonly string[] TextColors = { "#FFFFFF", "#FFE45C", "#6CFF8C", "#6CE7FF", "#FFB040", "#FF7AC8" };

    internal GamingSettingsView(GamingService service, GamingView view)
    {
        this.service = service;
        this.view = view;
        InitializeComponent();
        foreach (string c in BackgroundColors) BackgroundSwatches.Children.Add(Swatch(c, () => { S.BackgroundColor = c; ApplyOverlay(); }));
        foreach (string c in TextColors) TextSwatches.Children.Add(Swatch(c, () => { S.TextColor = c; ApplyOverlay(); }));
        foreach (int fps in new[] { 30, 60, 90, 120, 144 }) FpsBox.Items.Add(new ComboBoxItem { Content = F("{0} fps", fps), Tag = fps });
        HeightBox.Items.Add(new ComboBoxItem { Content = T("The same as what's recorded"), Tag = 0 });
        foreach (int h in new[] { 2160, 1440, 1080, 720 }) HeightBox.Items.Add(new ComboBoxItem { Content = F("{0}p (scaled down to fit)", h), Tag = h });
        foreach (int k in new[] { 1, 2, 4 }) KeyframeBox.Items.Add(new ComboBoxItem { Content = P(k, "{0} second", "{0} seconds"), Tag = k });
        foreach (int a in new[] { 96, 128, 160, 192 }) AudioBox.Items.Add(new ComboBoxItem { Content = F("{0} kbit/s", a), Tag = a });
        foreach (int m in new[] { 3, 6, 12, 24, 0 }) KeepBox.Items.Add(new ComboBoxItem { Content = m == 0 ? T("Always") : P(m, "{0} month", "{0} months"), Tag = m });

        foreach (var (box, get, set) in new (Shared.Hotkeys.ShortcutBox, Func<string?>, Action<string?>)[]
        {
            (RecordShortcut, () => S.RecordHotkey, v => S.RecordHotkey = v),
            (OverlayShortcut, () => S.OverlayHotkey, v => S.OverlayHotkey = v),
            (ModeShortcut, () => S.ModeHotkey, v => S.ModeHotkey = v),
        })
        {
            box.Value = get();
            box.Attach(service.SuspendHotkeys, service.RegisterHotkeys);
            box.Changed += () =>
            {
                set(box.Value);
                S.Save();
                service.RegisterHotkeys();
                ShowShortcutState();
                view.SettingsChanged();
            };
        }
        service.GamesChanged += () => { if (IsVisible) FillGames(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Load(); };
        Load();
    }

    private GamingSettings S => service.Settings;

    private void Load()
    {
        loading = true;
        OnlyGamesBox.IsChecked = S.OverlayOnlyInGames;
        HideCaptureBox.IsChecked = S.HideFromCapture;
        GraphFpsBox.IsChecked = S.GraphFps;
        GraphFrametimeBox.IsChecked = S.GraphFrametime;
        GraphVramBox.IsChecked = S.GraphVram;
        AlignLeft.IsChecked = S.Align == OverlayAlign.Left;
        AlignRight.IsChecked = S.Align == OverlayAlign.Right;
        BackgroundBox.Text = S.BackgroundColor;
        TextColorBox.Text = S.TextColor;
        OpacitySlider.Value = Math.Round(S.BackgroundOpacity * 100);
        OpacityText.Text = F("{0:0}%", OpacitySlider.Value);
        ScaleSlider.Value = Math.Round(S.Scale * 100);
        ScaleText.Text = F("{0:0}%", ScaleSlider.Value);
        LockBox.IsChecked = S.Locked;
        ShowTechBox.IsChecked = S.ShowGameTech;
        NvidiaPanel.Visibility = NvidiaIndicator.Available ? Visibility.Visible : Visibility.Collapsed;
        NvidiaIndicatorBox.IsChecked = NvidiaIndicator.IsOn;

        FolderBox.Text = S.RecordingFolder;
        SystemAudioBox.IsChecked = S.RecordSystemAudio;
        MicBox.IsChecked = S.RecordMicrophone;
        CursorBox.IsChecked = S.RecordCursor;
        HdrBox.SelectedIndex = (int)S.Hdr;
        FillCodecs();
        FillProfiles();

        LogBox.IsChecked = S.LogHistory;
        KeepBox.SelectedItem = KeepBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == S.KeepHistoryMonths) ?? KeepBox.Items[2];
        FillGames();
        ShowShortcutState();
        loading = false;
    }

    // ---------------------------------------------------------------- overlay

    private Button Swatch(string color, Action pick)
    {
        var b = new Button { Style = (Style)FindResource("Swatch"), Background = new SolidColorBrush(OverlayWindow.ParseColor(color, Colors.Black)), ToolTip = color };
        b.Click += (_, _) => pick();
        return b;
    }

    private void ApplyOverlay()
    {
        BackgroundBox.Text = S.BackgroundColor;
        TextColorBox.Text = S.TextColor;
        service.ApplyOverlaySettings();
        view.SettingsChanged();
    }

    private void Overlay_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.OverlayOnlyInGames = OnlyGamesBox.IsChecked == true;
        S.HideFromCapture = HideCaptureBox.IsChecked == true;
        S.GraphFps = GraphFpsBox.IsChecked == true;
        S.GraphFrametime = GraphFrametimeBox.IsChecked == true;
        S.GraphVram = GraphVramBox.IsChecked == true;
        var align = AlignRight.IsChecked == true ? OverlayAlign.Right : OverlayAlign.Left;
        if (align != S.Align) S.PositionSet = false; // its anchor corner changes
        S.Align = align;
        S.Locked = LockBox.IsChecked == true;
        S.ShowGameTech = ShowTechBox.IsChecked == true;
        ApplyOverlay();
    }

    private void NvidiaIndicator_Click(object sender, RoutedEventArgs e)
    {
        bool want = NvidiaIndicatorBox.IsChecked == true;
        NvidiaIndicatorBox.IsEnabled = false;
        // the approval prompt and reg.exe: off the UI thread
        Task.Run(() => NvidiaIndicator.Set(want)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            NvidiaIndicatorBox.IsEnabled = true;
            NvidiaIndicatorBox.IsChecked = NvidiaIndicator.IsOn;
        }));
    }

    private void ColorBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        string text = box.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        var c = OverlayWindow.ParseColor(text, Color.FromArgb(0, 1, 2, 3));
        if (c == Color.FromArgb(0, 1, 2, 3)) { box.Text = box == BackgroundBox ? S.BackgroundColor : S.TextColor; return; }
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (box == BackgroundBox) S.BackgroundColor = hex; else S.TextColor = hex;
        ApplyOverlay();
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText == null) return;
        OpacityText.Text = F("{0:0}%", OpacitySlider.Value);
        if (loading) return;
        S.BackgroundOpacity = OpacitySlider.Value / 100;
        service.ApplyOverlaySettings();
    }

    private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleText == null) return;
        ScaleText.Text = F("{0:0}%", ScaleSlider.Value);
        if (loading) return;
        S.Scale = ScaleSlider.Value / 100;
        service.ApplyOverlaySettings();
    }

    // ---------------------------------------------------------------- recording

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = S.RecordingFolder, Title = T("Save recordings to") };
        if (dialog.ShowDialog() != true) return;
        S.Folder = dialog.FolderName;
        S.Save();
        FolderBox.Text = S.RecordingFolder;
    }

    private void Recording_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.RecordSystemAudio = SystemAudioBox.IsChecked == true;
        S.RecordMicrophone = MicBox.IsChecked == true;
        S.RecordCursor = CursorBox.IsChecked == true;
        S.Hdr = HdrBox.SelectedIndex == 1 ? HdrRecording.Hdr10 : HdrRecording.Sdr;
        S.Save();
    }

    private void FillCodecs()
    {
        var available = Encoders.Available();
        CodecBox.Items.Clear();
        foreach (string c in Encoders.Codecs)
            CodecBox.Items.Add(new ComboBoxItem
            {
                Content = available.ContainsKey(c) ? Encoders.Label(c) : F("{0} (not on this graphics card)", Encoders.Label(c)),
                Tag = c,
                IsEnabled = available.ContainsKey(c),
            });
    }

    private RecordingProfile? Editing => S.Profiles.FirstOrDefault(p => p.Name == (string?)(ProfileBox.SelectedItem as ComboBoxItem)?.Tag);

    private void FillProfiles(string? select = null)
    {
        bool was = loading;
        loading = true;
        ProfileBox.Items.Clear();
        foreach (var p in S.Profiles) ProfileBox.Items.Add(new ComboBoxItem { Content = GamingView.ProfileName(p.Name), Tag = p.Name });
        select ??= S.ActiveProfile.Name;
        ProfileBox.SelectedItem = ProfileBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == select) ?? ProfileBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
        ShowProfile();
        loading = was;
    }

    private static void Select(ComboBox box, object value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value)) ?? box.Items.Cast<ComboBoxItem>().FirstOrDefault();

    private void ShowProfile()
    {
        var p = Editing;
        if (p == null) return;
        bool was = loading;
        loading = true;
        NameBox.Text = GamingView.ProfileName(p.Name);
        Select(CodecBox, p.Codec);
        Select(FpsBox, p.Fps);
        Select(HeightBox, p.Height);
        RateControlBox.SelectedIndex = p.RateControl == RateControl.Cbr ? 1 : 0;
        BitrateBox.Text = p.BitrateMbps.ToString("0.#", CultureInfo.CurrentCulture);
        MaxBitrateBox.Text = p.MaxBitrateMbps.ToString("0.#", CultureInfo.CurrentCulture);
        MaxBitrateBox.IsEnabled = MaxLabel.IsEnabled = p.RateControl == RateControl.Vbr;
        QualityBox.SelectedItem = QualityBox.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs(int.Parse((string)i.Tag) - p.Quality)).First();
        Select(KeyframeBox, p.KeyframeSeconds);
        Select(AudioBox, p.AudioKbps);
        DeleteProfileButton.IsEnabled = S.Profiles.Count > 1;
        ShowProfileNote(p);
        loading = was;
    }

    private void ShowProfileNote(RecordingProfile p)
    {
        // a rough size, so a 100 Mbit/s profile doesn't fill a drive by surprise
        double gbPerHour = Math.Max(p.BitrateMbps, 0.1) * 3600 / 8 / 1000;
        string note = F("About {0:0.#} GB an hour.", gbPerHour);
        if (p.Codec == "H264") note += " " + T("H.264 goes up to 4096 pixels wide: wider screens are scaled down to fit.");
        if (!Encoders.Available().ContainsKey(p.Codec)) note += " " + T("This graphics card can't encode this codec: choose another.");
        ProfileNote.Text = note;
    }

    private void ProfileBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        ShowProfile();
    }

    private void Profile_Edited(object sender, RoutedEventArgs e)
    {
        if (loading || Editing is not { } p) return;
        string name = NameBox.Text.Trim();
        if (name.Length > 0 && name != GamingView.ProfileName(p.Name) && S.Profiles.All(o => o.Name != name))
        {
            if (S.Profile == p.Name) S.Profile = name;
            p.Name = name;
        }
        if (CodecBox.SelectedItem is ComboBoxItem { Tag: string codec }) p.Codec = codec;
        if (FpsBox.SelectedItem is ComboBoxItem { Tag: int fps }) p.Fps = fps;
        if (HeightBox.SelectedItem is ComboBoxItem { Tag: int height }) p.Height = height;
        p.RateControl = RateControlBox.SelectedIndex == 1 ? RateControl.Cbr : RateControl.Vbr;
        if (double.TryParse(BitrateBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double rate)) p.BitrateMbps = Math.Clamp(rate, 1, 500);
        if (double.TryParse(MaxBitrateBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double max)) p.MaxBitrateMbps = Math.Clamp(max, 1, 500);
        if (p.MaxBitrateMbps < p.BitrateMbps) p.MaxBitrateMbps = p.BitrateMbps;
        if (QualityBox.SelectedItem is ComboBoxItem { Tag: string q }) p.Quality = int.Parse(q);
        if (KeyframeBox.SelectedItem is ComboBoxItem { Tag: int k }) p.KeyframeSeconds = k;
        if (AudioBox.SelectedItem is ComboBoxItem { Tag: int a }) p.AudioKbps = a;
        S.Save();
        FillProfiles(p.Name);
        view.SettingsChanged();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var copy = (Editing ?? S.ActiveProfile).Clone();
        int n = 1;
        string name;
        do name = F("My profile {0}", n++); while (S.Profiles.Any(p => p.Name == name));
        copy.Name = name;
        S.Profiles.Add(copy);
        S.Save();
        FillProfiles(name);
        view.SettingsChanged();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Editing is not { } p || S.Profiles.Count <= 1) return;
        S.Profiles.Remove(p);
        if (S.Profile == p.Name) S.Profile = S.Profiles[0].Name;
        S.Save();
        FillProfiles();
        view.SettingsChanged();
    }

    private void ResetProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this)!, T("Put the High, Medium and Low profiles back as they came, and remove your own?"), T("Gaming"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        S.Profiles = RecordingProfile.Defaults();
        S.Profile = "High";
        S.Save();
        FillProfiles();
        view.SettingsChanged();
    }

    // ---------------------------------------------------------------- shortcuts

    private void ShowShortcutState()
    {
        var failed = service.FailedHotkeys;
        ShortcutWarning.Text = failed.Count > 0
            ? F("{0} is already used by another program (the NVIDIA app uses Alt+F9 and Alt+Z, for example), so it won't work here. Pick another.", string.Join(", ", failed))
            : "";
        ShortcutWarning.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- games

    private void FillGames()
    {
        GamesList.Items.Clear();
        List<KeyValuePair<string, GameEntry>> games;
        lock (S.Games) games = S.Games.OrderByDescending(g => g.Value.LastSeen).ToList();
        foreach (var (exe, entry) in games)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
            var track = new CheckBox { IsChecked = entry.Track, VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Treat it as a game") };
            var name = new TextBox { Text = entry.Name, Width = 260, Margin = new Thickness(8, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
            var file = new TextBlock { Text = exe, VerticalAlignment = VerticalAlignment.Center, Width = 220, TextTrimming = TextTrimming.CharacterEllipsis };
            file.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var remove = new Button { Content = T("Remove"), Margin = new Thickness(8, 0, 0, 0) };
            track.Click += (_, _) => { lock (S.Games) entry.Track = track.IsChecked == true; S.Save(); };
            name.LostFocus += (_, _) =>
            {
                string n = name.Text.Trim();
                lock (S.Games) entry.Name = n.Length > 0 ? n : Path.GetFileNameWithoutExtension(exe);
                name.Text = entry.Name;
                S.Save();
            };
            remove.Click += (_, _) => { lock (S.Games) S.Games.Remove(exe); S.Save(); FillGames(); };
            foreach (UIElement c in new UIElement[] { track, name, file, remove }) row.Children.Add(c);
            GamesList.Items.Add(row);
        }
        NoGamesText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- history

    private void History_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        S.LogHistory = LogBox.IsChecked == true;
        if (KeepBox.SelectedItem is ComboBoxItem { Tag: int months }) S.KeepHistoryMonths = months;
        S.Save();
        service.UpdateFrameMonitor();
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(GameLog.Folder);
            Process.Start("explorer.exe", $"\"{GameLog.Folder}\"");
        }
        catch { }
    }
}
