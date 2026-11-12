using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Posters: a library's titles (its 4K ones, with HDR or Dolby Vision, or all of them), picked with checkboxes; a banner
/// across the top of their posters ("4K", "4K HDR", "4K DOLBY VISION", each text and the look set here, with a preview)
/// put on, or taken off again, for all of them at once.
/// </summary>
internal sealed class PostersView : Border
{
    private readonly PlexService service;
    private PlexSettings S => service.Settings;
    private BannerStyle B => S.Banner;
    private readonly ComboBox library = new() { Width = 200 };
    private readonly ComboBox show = new() { Width = 170, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox search = new() { Width = 180, Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Search titles") };
    private readonly ListBox list = new() { Height = 470, BorderThickness = new Thickness(0), ItemContainerStyle = PlexUi.ListRow };
    private readonly TextBlock count = Ui.Secondary();
    private readonly TextBlock status = Ui.Note("", 8);
    private readonly Image preview = new() { Width = 260, Height = 390, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button apply, remove, cancel;
    private readonly Debounce redraw, filter;
    private List<PosterItem> items = [];
    private CancellationTokenSource? job;
    private byte[]? previewSource;
    private string? previewKey;

    public PostersView(PlexService service)
    {
        this.service = service;
        redraw = new Debounce(TimeSpan.FromMilliseconds(250), () => Dispatcher.BeginInvoke(DrawPreview));
        filter = new Debounce(TimeSpan.FromMilliseconds(250), () => Dispatcher.BeginInvoke(Filter));

        // ---- the list
        show.ItemsSource = new[] { T("4K titles"), T("4K HDR"), T("4K Dolby Vision"), T("4K without HDR"), T("With a banner"), T("Everything") };
        show.SelectedIndex = 0;
        library.SelectionChanged += (_, _) => { if (library.SelectedItem is PlexLibrary l) { S.PosterLibrary = l.Key; S.Save(); _ = Load(); } };
        show.SelectionChanged += (_, _) => _ = Load();
        search.TextChanged += (_, _) => filter.Poke();
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search titles"));
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PosterItem.Picked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PosterItem.Label)));
        list.ItemTemplate = new DataTemplate { VisualTree = check };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        list.SetResourceReference(BackgroundProperty, "CardBrush");
        list.SelectionChanged += (_, _) => _ = LoadPreview(list.SelectedItem as PosterItem);
        PosterItem.PickedChanged += Count;

        var listTop = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { library, show, search } };
        var picks = PlexUi.Buttons(
            PlexUi.Small(T("All"), () => Pick(true), T("Pick every title shown")),
            PlexUi.Small(T("None"), () => Pick(false)));
        picks.Margin = new Thickness(0, 8, 0, 0);
        var countRow = new DockPanel { Children = { picks } };
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(8, 0, 0, 6);
        countRow.Children.Add(count);
        var left = Ui.Card(new StackPanel { Children = { Ui.Header(T("Titles")), listTop, list, countRow } }, new Thickness(0));

        // ---- the banner
        var b = new StackPanel();
        b.Children.Add(Ui.Header(T("Banner")));
        b.Children.Add(Field(T("4K"), B.Text4K, v => B.Text4K = v));
        b.Children.Add(Field(T("4K HDR"), B.TextHdr, v => B.TextHdr = v));
        b.Children.Add(Field(T("Dolby Vision"), B.TextDv, v => B.TextDv = v));
        var height = new Slider { Minimum = 0.05, Maximum = 0.2, Value = B.Height, Width = 160, SmallChange = 0.005, LargeChange = 0.02, VerticalAlignment = VerticalAlignment.Center };
        height.ValueChanged += (_, _) => { B.Height = height.Value; Changed(); };
        b.Children.Add(Labelled(T("Height"), height));
        b.Children.Add(Labelled(T("Background"), Ui.Choice(new[] { (T("Black"), "#FF000000"), (T("See-through black"), "#B3000000"), (T("Plex amber"), "#FFE5A00D"), (T("Blue"), "#FF1E5AA8"), (T("Red"), "#FFB3261E"), (T("White"), "#FFFFFFFF") }, B.Background, v => { B.Background = v; Changed(); }, 160)));
        b.Children.Add(Labelled(T("Text"), Ui.Choice(new[] { (T("White"), "#FFFFFF"), (T("Black"), "#000000"), (T("Plex amber"), "#E5A00D") }, B.Foreground, v => { B.Foreground = v; Changed(); }, 160)));
        b.Children.Add(Labelled(T("Line"), Ui.Choice(new[] { (T("Plex amber"), "#E5A00D"), (T("White"), "#FFFFFF"), (T("None"), "") }, B.Line, v => { B.Line = v; Changed(); }, 160)));
        var bold = new CheckBox { Content = T("Heavy text"), IsChecked = B.Bold, Margin = new Thickness(90, 0, 0, 10) };
        bold.Click += (_, _) => { B.Bold = bold.IsChecked == true; Changed(); };
        b.Children.Add(bold);
        b.Children.Add(preview);

        apply = PlexUi.Small(T("Add Banner"), () => Run(true), T("Put the banner on every picked title's poster (again, from the poster under it, if it has one)"));
        apply.SetResourceReference(StyleProperty, "AccentButton");
        apply.Padding = new Thickness(10, 3, 10, 3);
        remove = PlexUi.SmallConfirm(T("Remove Banner"), () => Run(false), T("Select the poster each picked title had before its banner"));
        cancel = PlexUi.Small(T("Cancel"), () => job?.Cancel());
        cancel.Visibility = Visibility.Collapsed;
        var go = PlexUi.Buttons(apply, remove, cancel);
        go.Margin = new Thickness(0, 12, 0, 0);
        b.Children.Add(go);
        b.Children.Add(status);
        var right = Ui.Card(b, new Thickness(0));

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(12) }, new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(right, 2);
        right.VerticalAlignment = left.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(left);
        grid.Children.Add(right);
        Child = Ui.Page(grid);

        IsVisibleChanged += (_, e) => { if (e.NewValue is true && library.Items.Count == 0) _ = LoadLibraries(); };
        service.Changed += () => Dispatcher.BeginInvoke(() => { library.ItemsSource = null; if (IsVisible) _ = LoadLibraries(); });
    }

    private FrameworkElement Field(string label, string value, Action<string> set)
    {
        var box = new TextBox { Text = value, Width = 160 };
        box.TextChanged += (_, _) => { set(box.Text); Changed(); };
        return Labelled(label, box);
    }

    private static FrameworkElement Labelled(string label, FrameworkElement field)
    {
        System.Windows.Automation.AutomationProperties.SetName(field, label);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(field);
        return row;
    }

    private void Changed()
    {
        S.SaveSoon();
        redraw.Poke();
    }

    private async Task LoadLibraries()
    {
        if (service.Client == null) { status.Text = T("Plex isn't set up: Settings → Plex."); return; }
        try
        {
            var libs = (await service.LibrariesAsync()).Where(l => l.Type is "movie" or "show").ToList();
            library.ItemsSource = libs;
            library.SelectedItem = libs.FirstOrDefault(l => l.Key == S.PosterLibrary) ?? libs.FirstOrDefault();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private int loads;

    private async Task Load()
    {
        if (library.SelectedItem is not PlexLibrary lib) return;
        int n = ++loads;
        status.Text = F("Reading {0}…", lib.Title);
        try
        {
            int mode = show.SelectedIndex;
            var found = await PosterTools.ListAsync(service, lib, only4K: mode is >= 0 and <= 3, CancellationToken.None);
            if (n != loads) return;
            items = found;
            status.Text = "";
            Filter();
        }
        catch (Exception ex) { if (n == loads) status.Text = ex.Message; }
    }

    private IEnumerable<PosterItem> Shown()
    {
        string q = search.Text.Trim();
        return items.Where(i => show.SelectedIndex switch
        {
            1 => i.Kind == VideoKind.Hdr,
            2 => i.Kind == VideoKind.Dv,
            3 => i.Kind == VideoKind.K4,
            4 => i.HasBanner,
            _ => true,
        } && (q.Length == 0 || i.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void Filter()
    {
        list.ItemsSource = Shown().ToList();
        if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
        Count();
    }

    private void Count()
    {
        int shown = (list.ItemsSource as List<PosterItem>)?.Count ?? 0;
        count.Text = F("{0} picked of {1} shown", items.Count(i => i.Picked), shown);
    }

    private void Pick(bool on)
    {
        foreach (var i in (list.ItemsSource as List<PosterItem>) ?? []) i.Picked = on;
        if (!on) foreach (var i in items) i.Picked = false;
        Count();
    }

    // ---------------------------------------------------------------- the preview

    private async Task LoadPreview(PosterItem? item)
    {
        if (item == null || service.Client is not { } c) return;
        previewKey = item.Key;
        try
        {
            // the poster under the banner, if it has one of ours: its original from the posters list
            string thumb = item.Thumb ?? $"/library/metadata/{item.Key}/thumb";
            if (S.Overlaid.TryGetValue(item.Key, out var had) && had.OriginalThumb != null) thumb = had.OriginalThumb;
            var bytes = await c.BytesAsync($"/photo/:/transcode?width=520&height=780&minSize=1&upscale=1&url={Uri.EscapeDataString(thumb)}");
            if (previewKey != item.Key) return;
            previewSource = bytes;
            DrawPreview();
        }
        catch (Exception ex) { Log.Here.Debug(() => $"Poster preview: {ex.Message}"); }
    }

    private void DrawPreview()
    {
        if (previewSource == null) return;
        var item = items.FirstOrDefault(i => i.Key == previewKey);
        try
        {
            var jpeg = PosterTools.Render(previewSource, PosterTools.TextFor(item?.Kind ?? VideoKind.Hdr, B), B, 520);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(jpeg);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            preview.Source = bmp;
        }
        catch (Exception ex) { Log.Here.Debug(() => $"Poster preview: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- doing it

    private async void Run(bool add)
    {
        if (job != null) return;
        var picked = items.Where(i => i.Picked && (add || i.HasBanner)).ToList();
        if (picked.Count == 0) { status.Text = add ? T("Pick some titles first.") : T("None of the picked titles has a banner."); return; }
        job = new CancellationTokenSource();
        var cancelToken = job.Token;
        apply.IsEnabled = remove.IsEnabled = false;
        cancel.Visibility = Visibility.Visible;
        int done = 0, failed = 0;
        string? lastError = null;
        try
        {
            foreach (var item in picked)
            {
                if (cancelToken.IsCancellationRequested) break;
                status.Text = F("{0} of {1}: {2}", done + failed + 1, picked.Count, item.Title);
                try
                {
                    if (add) await PosterTools.ApplyAsync(service, item, PosterTools.TextFor(item.Kind == VideoKind.Other ? VideoKind.K4 : item.Kind, B), cancelToken);
                    else await PosterTools.RemoveAsync(service, item, cancelToken);
                    item.Refresh();
                    done++;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    failed++;
                    lastError = $"{item.Title}: {ex.Message}";
                    Log.Here.Warn($"Poster banner: {lastError}");
                }
                S.Save();
            }
        }
        finally
        {
            job = null;
            apply.IsEnabled = remove.IsEnabled = true;
            cancel.Visibility = Visibility.Collapsed;
        }
        status.Text = (add ? P(done, "Banner added to {0} poster.", "Banner added to {0} posters.") : P(done, "Banner removed from {0} poster.", "Banner removed from {0} posters."))
            + (failed > 0 ? " " + F("{0} failed (last: {1}).", failed, lastError) : "")
            + (cancelToken.IsCancellationRequested ? " " + T("Cancelled.") : "");
        Count();
    }
}
