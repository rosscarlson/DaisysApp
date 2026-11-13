using System.Windows;
using System.Windows.Controls;
using DaisysApp.Applets.Plex.Scanner;
using DaisysApp.Shared.Video;

namespace DaisysApp.Applets.Plex.Views;

/// <summary>
/// Transcode: FFmpeg (found, or downloaded here), how files are made (Core's video profile, the audio, replace or keep
/// the original, where the server's folders are on this PC), and the queue (Space adds to it): each file's state and how
/// far, what it saved; Start, Stop after this one, Stop now, Remove, Clear finished.
/// </summary>
internal sealed class TranscodePanel : StackPanel
{
    private readonly PlexService service;
    private readonly Transcoder transcoder;
    private PlexSettings S => service.Settings;
    private readonly TextBlock ffmpegText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button download, start, stopAfter, stopNow;
    private readonly ProgressBar downloadBar = Ui.Bar(4);
    private readonly TextBlock profileText = Ui.Secondary();
    private readonly DataGrid queue = Ui.Table(380);
    private readonly TextBlock summary = Ui.Secondary("", true);
    private readonly TextBox detail = Ui.CopyText("", "ErrorTextBrush", 120);
    private readonly TextBlock status = Ui.Note("", 6);
    private CancellationTokenSource? downloading;

    public TranscodePanel(PlexService service, Transcoder transcoder)
    {
        this.service = service;
        this.transcoder = transcoder;

        // ---- FFmpeg
        download = PlexUi.Small(T("Get FFmpeg"), () => _ = Download(), T("Download FFmpeg 7.1 (gyan.dev's build, about 90 MB)"));
        var browse = PlexUi.Small(T("Browse…"), Browse, T("Use an ffmpeg.exe already on this PC"));
        downloadBar.Visibility = Visibility.Collapsed;
        downloadBar.Margin = new Thickness(0, 0, 0, 8);
        var ffCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("FFmpeg")), ffmpegText, downloadBar, PlexUi.Buttons(download, browse) } }, new Thickness(0, 0, 0, 12));

        // ---- how
        var profile = ScannerView.ProfileBox(service, transcoder, ShowProfile);
        var how = new StackPanel();
        how.Children.Add(Ui.Header(T("Output")));
        var profileRow = (StackPanel)ScannerView.Labelled(T("Profile"), profile);
        profileText.VerticalAlignment = VerticalAlignment.Center;
        profileText.Margin = new Thickness(10, 0, 0, 0);
        profileRow.Children.Add(profileText);
        how.Children.Add(profileRow);
        var aac = new CheckBox { Content = T("Audio as AAC at the profile's rate"), IsChecked = S.TranscodeAac, Margin = new Thickness(0, 0, 0, 6), ToolTip = T("Off: every audio track copied as it is") };
        Ui.OnToggle(aac, on => { S.TranscodeAac = on; S.Save(); transcoder.RaiseProfile(); });
        var keep = new CheckBox { Content = T("Keep the original (Plex shows both as versions)"), IsChecked = S.TranscodeKeepBoth, Margin = new Thickness(0, 0, 0, 8), ToolTip = T("Off: the original is deleted once the new file checks out") };
        Ui.OnToggle(keep, on => { S.TranscodeKeepBoth = on; S.Save(); });
        how.Children.Add(aac);
        how.Children.Add(keep);
        var from = new TextBox { Text = S.PathFrom, Width = 200, ToolTip = T("A folder as the server names it, e.g. /data/media") };
        var to = new TextBox { Text = S.PathTo, Width = 200, ToolTip = T("The same folder from this PC, e.g. \\\\nas\\media") };
        from.TextChanged += (_, _) => { S.PathFrom = from.Text; S.SaveSoon(); };
        to.TextChanged += (_, _) => { S.PathTo = to.Text; S.SaveSoon(); };
        System.Windows.Automation.AutomationProperties.SetName(to, T("This PC's path"));
        var paths = (StackPanel)ScannerView.Labelled(T("Server path"), from);
        paths.Children.Add(new TextBlock { Text = "→", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        paths.Children.Add(to);
        how.Children.Add(paths);
        var howCard = Ui.Card(how, new Thickness(0, 0, 0, 12));

        // ---- the queue
        queue.SelectionMode = DataGridSelectionMode.Extended;
        queue.Columns.Add(Ui.Column(T("Title"), nameof(TranscodeJob.Name), -3));
        queue.Columns.Add(Ui.Column(T("Source"), nameof(TranscodeJob.Source), -2));
        queue.Columns.Add(Ui.Column(T("State"), nameof(TranscodeJob.Status), -2.5));
        queue.Columns.Add(Ui.Column(T("Saved"), nameof(TranscodeJob.SavedText), 80, nameof(TranscodeJob.Saved), true));
        queue.ItemsSource = transcoder.Jobs;
        queue.SelectionChanged += (_, _) => ShowDetail();
        start = PlexUi.Small(T("Start"), () =>
        {
            if (Ffmpeg.Find(S) == null) { status.Text = T("Get FFmpeg first (above)."); return; }
            status.Text = "";
            transcoder.Start();
        }, T("Transcode the waiting files, one at a time"));
        start.SetResourceReference(StyleProperty, "AccentButton");
        start.Padding = new Thickness(10, 3, 10, 3);
        stopAfter = PlexUi.Small(T("Stop After This One"), transcoder.StopAfter);
        stopNow = PlexUi.SmallConfirm(T("Stop Now"), transcoder.Cancel, T("Stop the file in hand (its original stays as it was)"));
        var buttons = PlexUi.Buttons(start, stopAfter, stopNow,
            PlexUi.Small(T("Remove"), Remove, T("Take the picked waiting files off the queue")),
            PlexUi.Small(T("Clear Finished"), () => { foreach (var j in transcoder.Jobs.Where(j => j.Finished).ToList()) transcoder.Jobs.Remove(j); }),
            PlexUi.Small(T("Show in Explorer"), () => { if (queue.SelectedItem is TranscodeJob j) Ui.ShowInExplorer(S.LocalPath(j.File.Path)); }));
        buttons.Margin = new Thickness(0, 8, 0, 0);
        detail.Visibility = Visibility.Collapsed;
        detail.Margin = new Thickness(0, 6, 0, 0);
        summary.Margin = new Thickness(0, 0, 0, 6);
        var queueCard = Ui.Card(new StackPanel { Children = { Ui.Header(T("Queue")), summary, queue, buttons, detail, status } }, new Thickness(0));

        Children.Add(ffCard);
        Children.Add(howCard);
        Children.Add(queueCard);

        transcoder.Changed += Update;
        transcoder.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (TranscodeJob j in e.NewItems) j.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
            Update();
        };
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) _ = ShowFfmpeg(); };
        ShowProfile();
        Update();
    }

    private void ShowProfile()
    {
        profileText.Text = ScannerView.Describe(transcoder.Profile);
        if (IsVisible) _ = ShowFfmpeg();
    }

    private async Task ShowFfmpeg()
    {
        string? ff = Ffmpeg.Find(S);
        download.Visibility = ff == null ? Visibility.Visible : Visibility.Collapsed;
        if (ff == null) { ffmpegText.Text = T("FFmpeg isn't on this PC: Get FFmpeg downloads it, or Browse… to one."); return; }
        try
        {
            string version = await Ffmpeg.VersionAsync(ff);
            var enc = await Ffmpeg.EncoderAsync(ff, transcoder.Profile.Codec);
            ffmpegText.Text = F("FFmpeg {0} ({1}) · {2}: {3}", version, ff, transcoder.Profile.Codec,
                enc == null ? T("no encoder for it here") : $"{enc.Label} ({enc.Name})");
        }
        catch (Exception ex) { ffmpegText.Text = F("{0}: {1}", ff, ex.Message); }
    }

    private async Task Download()
    {
        if (downloading != null) { downloading.Cancel(); return; }
        downloading = new CancellationTokenSource();
        download.Content = T("Cancel");
        downloadBar.Visibility = Visibility.Visible;
        downloadBar.Value = 0;
        downloadBar.Maximum = 1;
        try
        {
            ffmpegText.Text = T("Downloading FFmpeg…");
            await Ffmpeg.DownloadAsync(v => Dispatcher.BeginInvoke(() => downloadBar.Value = v), downloading.Token);
            S.FfmpegPath = "";
            S.Save();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = F("FFmpeg couldn't be downloaded: {0}", ex.Message); }
        finally
        {
            downloading = null;
            download.Content = T("Get FFmpeg");
            downloadBar.Visibility = Visibility.Collapsed;
        }
        await ShowFfmpeg();
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe", Title = T("FFmpeg") };
        if (dlg.ShowDialog() != true) return;
        S.FfmpegPath = dlg.FileName;
        S.Save();
        _ = ShowFfmpeg();
    }

    private void Remove()
    {
        foreach (var j in queue.SelectedItems.Cast<TranscodeJob>().Where(j => j.State != JobState.Running).ToList()) transcoder.Jobs.Remove(j);
    }

    private void ShowDetail()
    {
        var j = queue.SelectedItem as TranscodeJob;
        detail.Text = j?.Detail ?? "";
        detail.Visibility = detail.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Update()
    {
        var jobs = transcoder.Jobs;
        int waiting = jobs.Count(j => j.State == JobState.Waiting), done = jobs.Count(j => j.State == JobState.Done),
            skipped = jobs.Count(j => j.State == JobState.Skipped), failed = jobs.Count(j => j.State is JobState.Failed or JobState.Cancelled);
        summary.Text = jobs.Count == 0 ? T("Nothing queued: pick shows, seasons or movies in Space.")
            : F("{0} waiting · {1} done · {2} skipped · {3} failed · saved {4}", waiting, done, skipped, failed, Sizes.Bytes(jobs.Sum(j => j.Saved)));
        start.IsEnabled = waiting > 0 && !transcoder.Running;
        stopAfter.IsEnabled = stopNow.IsEnabled = transcoder.Running;
        ShowDetail();
    }
}
