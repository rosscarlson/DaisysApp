using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysApp.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DaisysApp.Shared.YouTube;

/// <summary>
/// (since 4.15.0) A YouTube video in the app: YouTube's own player (its IFrame API) in a page Edge's WebView2 shows
/// (a window of its own: nothing WPF draws shows over it, so keep buttons off it), without YouTube's controls:
/// the caller seeks, plays (at YouTube's rates), pauses, sets the volume, and hears where it is
/// (<see cref="Time"/>, <see cref="Updated"/> ten times a second, asked of the page). A paused seek shows that frame.
/// Seeks go at most every 120 ms (the last one wins). The page is served from a made-up https address (YouTube wants
/// one to embed).
/// </summary>
public sealed class YouTubePlayer : Grid
{
    private const string Host = "daisysapp.player";
    private static Task<CoreWebView2Environment>? environment;

    private readonly WebView2 view = new() { DefaultBackgroundColor = System.Drawing.Color.Black };
    private readonly TextBlock note = Ui.NoteCentred("");
    private readonly DispatcherTimer seeker = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer poller = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool pageReady, apiReady, polling;
    private string? id, loaded;
    private double seekTo = double.NaN;
    private double volume = 1;
    private bool muted;

    /// <summary>Where it is (seconds), as last heard.</summary>
    public double Time { get; private set; } = double.NaN;
    /// <summary>Its length (seconds), once loaded.</summary>
    public double Duration { get; private set; }
    /// <summary>Playing (as YouTube says).</summary>
    public bool Playing { get; private set; }
    /// <summary>Loaded and ready to seek and play.</summary>
    public bool Ready { get; private set; }
    /// <summary>Why it can't play (YouTube's error, or WebView2 missing), else null.</summary>
    public string? Error { get; private set; }

    /// <summary>Where it is, what it's doing or an error changed (on the UI thread).</summary>
    public event Action? Updated;

    public YouTubePlayer()
    {
        SetResourceReference(BackgroundProperty, "RoomBrush");
        Children.Add(view);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(8);
        Children.Add(note);
        seeker.Tick += (_, _) => SendSeek();
        poller.Tick += async (_, _) => await Poll();
        Loaded += async (_, _) => { poller.Start(); await Start(); };
        // (a window of its own isn't clipped by a scrolling page: cut to the part that shows)
        LayoutUpdated += (_, _) => CutToView();
        Unloaded += (_, _) => { seeker.Stop(); poller.Stop(); };
    }

    /// <summary>Loads a video (its ID); null: none.</summary>
    public void Load(string? video)
    {
        if (video == id) return;
        id = video;
        Ready = false;
        Time = double.NaN;
        Duration = 0;
        Playing = false;
        Error = null;
        seekTo = double.NaN;
        ShowNote(id == null ? "" : T("Opening the video…"));
        Send();
    }

    /// <summary>The video asked for, to the page once YouTube's API is there.</summary>
    private void Send()
    {
        if (!apiReady || id == loaded) return;
        loaded = id;
        Run(id == null ? "stop()" : $"load('{id}')");
    }

    /// <summary>Shows the frame at a time (paused).</summary>
    public void Seek(double seconds)
    {
        seekTo = Math.Max(0, seconds);
        if (Playing) { Run("pause()"); Playing = false; }
        if (!seeker.IsEnabled) { SendSeek(); seeker.Start(); }
    }

    /// <summary>Plays from a time at a rate (YouTube's: 0.25 to 2).</summary>
    public void Play(double from, double rate = 1)
    {
        if (!Ready) return;
        seekTo = double.NaN;
        string r = Math.Clamp(rate, 0.25, 2).ToString(CultureInfo.InvariantCulture);
        Run($"play({Math.Max(0, from).ToString(CultureInfo.InvariantCulture)},{r})");
        Playing = true;
    }

    public void Pause()
    {
        if (!Ready) return;
        Run("pause()");
        Playing = false;
    }

    /// <summary>The volume (0 to 1) and mute.</summary>
    public void SetVolume(double level, bool mute)
    {
        volume = Math.Clamp(level, 0, 1);
        muted = mute;
        if (Ready) Run(VolumeScript());
    }

    private string VolumeScript() => $"player.setVolume({(int)Math.Round(volume * 100)});" + (muted ? "player.mute()" : "player.unMute()");

    private void SendSeek()
    {
        if (double.IsNaN(seekTo)) { seeker.Stop(); return; }
        if (!Ready) return;
        Run($"seek({seekTo.ToString(CultureInfo.InvariantCulture)})");
        Time = seekTo;
        seekTo = double.NaN;
    }

    private async Task Start()
    {
        if (view.CoreWebView2 != null || Error != null) return;
        try
        {
            await view.EnsureCoreWebView2Async(await (environment ??= MakeEnvironment()));
            if (view.CoreWebView2 == null) return;
            string folder = Path.Combine(AppPaths.SettingsFolder, "YouTubePlayer");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "player.html"), Page);
            view.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.Settings.IsZoomControlEnabled = false;
            // (links in the player, such as its title, go to the browser)
            view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Ui.Open(e.Uri); };
            view.CoreWebView2.NavigationStarting += (_, _) => { apiReady = false; loaded = null; };
            view.CoreWebView2.Navigate($"https://{Host}/player.html");
            pageReady = true;
        }
        catch (Exception ex)
        {
            Log.App.Warn("YouTube player: WebView2 couldn't start", ex);
            // (tried again by the next player)
            if (environment is { IsFaulted: true }) environment = null;
            Error = ex is WebView2RuntimeNotFoundException ? T("Needs Microsoft Edge WebView2") : ex.Message;
            ShowNote(F("Can't play this video: {0}", Error));
            Updated?.Invoke();
        }
    }

    private static Task<CoreWebView2Environment> MakeEnvironment()
    {
        // (the loader's in the module's folder, under runtimes; the app's own folder is where it's looked for)
        string arch = RuntimeInformation.ProcessArchitecture switch { Architecture.Arm64 => "arm64", Architecture.X86 => "x86", _ => "x64" };
        string loader = Path.Combine(Path.GetDirectoryName(typeof(YouTubePlayer).Assembly.Location) ?? "", "runtimes", $"win-{arch}", "native");
        if (File.Exists(Path.Combine(loader, "WebView2Loader.dll"))) CoreWebView2Environment.SetLoaderDllFolderPath(loader);
        return CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.SettingsFolder, "WebView2"),
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
    }

    /// <summary>Asks the page how the player is (the page's own messages didn't arrive, served this way).</summary>
    private async Task Poll()
    {
        if (!pageReady || polling || view.CoreWebView2 == null) return;
        polling = true;
        try
        {
            string answer = await view.CoreWebView2.ExecuteScriptAsync("typeof poll=='function'?poll():''");
            using var outer = JsonDocument.Parse(answer);
            if (outer.RootElement.ValueKind != JsonValueKind.String || outer.RootElement.GetString() is not { Length: > 0 } inner) return;
            using var doc = JsonDocument.Parse(inner);
            var m = doc.RootElement;
            if (!apiReady && m.GetProperty("a").GetBoolean()) { apiReady = true; Send(); }
            if (m.TryGetProperty("e", out var e) && e.GetInt32() is var code and not 0 && Error == null)
            {
                Error = code switch
                {
                    100 => T("Not found on YouTube (deleted or private)"),
                    101 or 150 => T("Its owner doesn't let it play outside YouTube (or it's private)"),
                    2 => T("Not a YouTube video"),
                    _ => F("YouTube error {0}", code),
                };
                ShowNote(F("Can't play this video: {0}", Error));
            }
            if (m.GetProperty("p").GetBoolean() && !Ready && id != null && loaded == id)
            {
                Ready = true;
                Run(VolumeScript());
                ShowNote(null);
                if (!double.IsNaN(seekTo)) SendSeek();
            }
            if (m.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Number && d.GetDouble() > 0) Duration = d.GetDouble();
            if (Ready && m.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Number)
            {
                if (double.IsNaN(seekTo)) Time = t.GetDouble();
                Playing = m.GetProperty("s").GetInt32() == 1;
            }
            Updated?.Invoke();
        }
        catch (Exception ex) { Log.App.Debug($"YouTube player: {ex.Message}"); }
        finally { polling = false; }
    }

    private Rect clipped = Rect.Empty;
    private IntPtr clippedWindow;

    /// <summary>The page's window cut to what the scrolling views it's in show of it.</summary>
    private void CutToView()
    {
        if (view.Handle == IntPtr.Zero || !view.IsVisible || view.ActualWidth <= 0 || PresentationSource.FromVisual(view) is not { CompositionTarget: { } target }) return;
        var full = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
        var shown = full;
        for (var d = System.Windows.Media.VisualTreeHelper.GetParent(view); d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ScrollContentPresenter port && port.IsAncestorOf(view))
                shown.Intersect(port.TransformToDescendant(view).TransformBounds(new Rect(0, 0, port.ActualWidth, port.ActualHeight)));
        // (moved to another window: a new one, uncut)
        if (shown == clipped && view.Handle == clippedWindow) return;
        clipped = shown;
        clippedWindow = view.Handle;
        var m = target.TransformToDevice;
        IntPtr region = shown == full ? IntPtr.Zero : shown.IsEmpty ? CreateRectRgn(0, 0, 0, 0)
            : CreateRectRgn((int)Math.Floor(shown.Left * m.M11), (int)Math.Floor(shown.Top * m.M22), (int)Math.Ceiling(shown.Right * m.M11), (int)Math.Ceiling(shown.Bottom * m.M22));
        // (the window owns the region once it's set)
        if (SetWindowRgn(view.Handle, region, true) == 0 && region != IntPtr.Zero) DeleteObject(region);
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    private void Run(string script)
    {
        if (!pageReady || view.CoreWebView2 == null) return;
        _ = view.CoreWebView2.ExecuteScriptAsync("try{" + script + "}catch(e){}");
    }

    private void ShowNote(string? text)
    {
        note.Text = text ?? "";
        note.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        // (the page is a window of its own, over anything drawn here: hidden while there's a note)
        view.Visibility = note.Visibility == Visibility.Visible || id == null ? Visibility.Hidden : Visibility.Visible;
    }

    private const string Page = """
        <!doctype html><html><head><meta charset="utf-8"><meta name="referrer" content="strict-origin-when-cross-origin">
        <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{position:absolute;inset:0;width:100%;height:100%}</style></head>
        <body><div id="p"></div><script>
        var api=false,player=null,primed=false,err=0,want=false;
        function onYouTubeIframeAPIReady(){api=true;}
        function load(id){
          primed=false;err=0;want=false;
          if(player){player.cueVideoById(id);primed=true;return;}
          player=new YT.Player('p',{videoId:id,width:'100%',height:'100%',
            playerVars:{controls:0,disablekb:1,rel:0,playsinline:1,iv_load_policy:3,fs:0,origin:location.origin,widget_referrer:location.href},
            events:{
              onReady:function(){primed=true;},
              onStateChange:function(e){if(e.data==1&&!want)player.pauseVideo();},
              onError:function(e){err=e.data;}}});
        }
        function seek(t){player.seekTo(t,true);}
        function play(t,r){want=true;player.setPlaybackRate(r);player.seekTo(t,true);player.playVideo();}
        function pause(){want=false;player.pauseVideo();}
        function stop(){primed=false;want=false;if(player)player.stopVideo();}
        function poll(){
          var o={a:api,p:primed,e:err};
          if(player&&player.getCurrentTime){o.t=player.getCurrentTime();o.s=player.getPlayerState();o.d=player.getDuration();}
          return JSON.stringify(o);
        }
        var s=document.createElement('script');s.src='https://www.youtube.com/iframe_api';document.head.appendChild(s);
        </script></body></html>
        """;
}
