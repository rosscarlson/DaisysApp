using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex;

public enum VideoKind { Other, K4, Hdr, Dv }

/// <summary>A movie or show on the Posters tab.</summary>
public sealed class PosterItem : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? Year { get; init; }
    public string? Thumb { get; init; }
    public VideoKind Kind { get; set; }
    public bool HasBanner { get; set; }

    private bool picked;
    public bool Picked
    {
        get => picked;
        set { if (picked == value) return; picked = value; PropertyChanged?.Invoke(this, new(nameof(Picked))); PickedChanged?.Invoke(); }
    }

    public string Label => Title + (Year != null ? $" ({Year})" : "") + (Kind != VideoKind.Other ? "  ·  " + KindName(Kind) : "") + (HasBanner ? "  ·  " + T("banner") : "");

    public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Label)));

    public static string KindName(VideoKind k) => k switch { VideoKind.K4 => "4K", VideoKind.Hdr => "4K HDR", VideoKind.Dv => "4K Dolby Vision", _ => "" };

    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? PickedChanged;
}

/// <summary>
/// The Posters tab's work: finding a library's 4K titles and which have HDR or Dolby Vision (Plex's own filters:
/// resolution=4k, hdr=1, dovi=1; a show by its episodes), drawing a banner across the top of a poster, uploading it
/// (Plex selects an uploaded poster), and taking it off again (the poster selected before is selected again).
/// </summary>
public static class PosterTools
{
    /// <summary>The library's titles: every one, or only its 4K ones, each with its kind and whether it has a banner of ours.</summary>
    public static async Task<List<PosterItem>> ListAsync(PlexService service, PlexLibrary lib, bool only4K, CancellationToken cancel)
    {
        var c = service.Need();
        bool shows = lib.Type == "show";
        var items = new Dictionary<string, PosterItem>();
        if (!only4K || !shows)
            foreach (var m in await All(c, $"/library/sections/{lib.Key}/all?type={(shows ? 2 : 1)}" + (only4K ? "&resolution=4k" : ""), cancel))
                Add(items, PlexClient.Str(m, "ratingKey"), PlexClient.Str(m, "title"), PlexClient.Str(m, "year"), PlexClient.Str(m, "thumb"));

        if (!shows)
        {
            foreach (var m in await All(c, $"/library/sections/{lib.Key}/all?type=1&resolution=4k", cancel))
                if (items.TryGetValue(PlexClient.Str(m, "ratingKey") ?? "", out var i)) i.Kind = VideoKind.K4;
            foreach (var m in await All(c, $"/library/sections/{lib.Key}/all?type=1&resolution=4k&hdr=1", cancel))
                if (items.TryGetValue(PlexClient.Str(m, "ratingKey") ?? "", out var i)) i.Kind = VideoKind.Hdr;
            foreach (var m in await All(c, $"/library/sections/{lib.Key}/all?type=1&resolution=4k&dovi=1", cancel))
                if (items.TryGetValue(PlexClient.Str(m, "ratingKey") ?? "", out var i)) i.Kind = VideoKind.Dv;
        }
        else
        {
            // a show by its 4K episodes: Dolby Vision if they all are, HDR if they all are (or DV), else 4K
            var episodes = await All(c, $"/library/sections/{lib.Key}/all?type=4&resolution=4k", cancel);
            var hdr = (await All(c, $"/library/sections/{lib.Key}/all?type=4&resolution=4k&hdr=1", cancel)).Select(e => PlexClient.Str(e, "ratingKey")).ToHashSet();
            var dv = (await All(c, $"/library/sections/{lib.Key}/all?type=4&resolution=4k&dovi=1", cancel)).Select(e => PlexClient.Str(e, "ratingKey")).ToHashSet();
            foreach (var show in episodes.GroupBy(e => PlexClient.Str(e, "grandparentRatingKey") ?? ""))
            {
                if (show.Key.Length == 0) continue;
                var keys = show.Select(e => PlexClient.Str(e, "ratingKey")).ToList();
                var first = show.First();
                var item = Add(items, show.Key, PlexClient.Str(first, "grandparentTitle"), null, PlexClient.Str(first, "grandparentThumb"));
                item.Kind = keys.All(dv.Contains) ? VideoKind.Dv : keys.All(k => hdr.Contains(k) || dv.Contains(k)) ? VideoKind.Hdr : VideoKind.K4;
            }
        }
        foreach (var i in items.Values) i.HasBanner = service.Settings.Overlaid.ContainsKey(i.Key);
        return items.Values.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static PosterItem Add(Dictionary<string, PosterItem> items, string? key, string? title, string? year, string? thumb)
    {
        key ??= "";
        if (!items.TryGetValue(key, out var i)) items[key] = i = new PosterItem { Key = key, Title = title ?? key, Year = year, Thumb = thumb };
        return i;
    }

    /// <summary>Every item of a query, 500 at a time.</summary>
    private static async Task<List<System.Text.Json.JsonElement>> All(PlexClient c, string path, CancellationToken cancel)
    {
        var all = new List<System.Text.Json.JsonElement>();
        for (int start = 0; ; start += 500)
        {
            var mc = await c.GetAsync($"{path}&X-Plex-Container-Start={start}&X-Plex-Container-Size=500", cancel);
            var page = PlexClient.List(mc, "Metadata").ToList();
            all.AddRange(page);
            long total = PlexClient.Num(mc, "totalSize");
            if (page.Count < 500 || (total > 0 && all.Count >= total)) return all;
        }
    }

    public static string TextFor(VideoKind kind, BannerStyle s) => kind switch { VideoKind.Dv => s.TextDv, VideoKind.Hdr => s.TextHdr, _ => s.Text4K };

    /// <summary>The poster selected now (its URL in the item's /posters list).</summary>
    private static async Task<(string? Url, string? Thumb)> Selected(PlexClient c, string key, CancellationToken cancel)
    {
        var mc = await c.GetAsync($"/library/metadata/{key}/posters", cancel);
        var p = PlexClient.List(mc, "Metadata").FirstOrDefault(p => PlexClient.Flag(p, "selected"));
        return (PlexClient.Str(p, "ratingKey") ?? PlexClient.Str(p, "key"), PlexClient.Str(p, "thumb"));
    }

    /// <summary>
    /// Puts the banner on an item's poster: from the poster selected before our first banner (selected again first,
    /// so a banner is never drawn on a banner), drawn, uploaded.
    /// </summary>
    public static async Task ApplyAsync(PlexService service, PosterItem item, string text, CancellationToken cancel)
    {
        var c = service.Need();
        var s = service.Settings;
        string? original, originalThumb;
        if (s.Overlaid.TryGetValue(item.Key, out var had) && had.Original != null)
        {
            (original, originalThumb) = (had.Original, had.OriginalThumb);
            await c.SendAsync(HttpMethod.Put, $"/library/metadata/{item.Key}/poster?url={Uri.EscapeDataString(original)}", cancel: cancel);
        }
        else (original, originalThumb) = await Selected(c, item.Key, cancel);
        byte[] poster = await c.BytesAsync($"/library/metadata/{item.Key}/thumb?t={DateTime.UtcNow.Ticks}", cancel);
        byte[] jpeg = await Application.Current.Dispatcher.InvokeAsync(() => Render(poster, text, s.Banner, 0));
        await c.SendAsync(HttpMethod.Post, $"/library/metadata/{item.Key}/posters", jpeg, cancel);
        s.Overlaid[item.Key] = new Overlaid { Title = item.Title, Text = text, Original = original, OriginalThumb = originalThumb, When = DateTime.Now };
        item.HasBanner = true;
        Log.Here.Info($"Banner \"{text}\" on {item.Title} ({item.Key})");
    }

    /// <summary>Takes the banner off: the poster selected before it is selected again.</summary>
    public static async Task RemoveAsync(PlexService service, PosterItem item, CancellationToken cancel)
    {
        var c = service.Need();
        var s = service.Settings;
        if (!s.Overlaid.TryGetValue(item.Key, out var had)) return;
        if (had.Original != null)
            await c.SendAsync(HttpMethod.Put, $"/library/metadata/{item.Key}/poster?url={Uri.EscapeDataString(had.Original)}", cancel: cancel);
        s.Overlaid.Remove(item.Key);
        item.HasBanner = false;
        Log.Here.Info($"Banner off {item.Title} ({item.Key})");
    }

    /// <summary>The poster with the banner across its top, as a JPEG (<paramref name="maxWidth"/> &gt; 0: scaled down, for the preview).</summary>
    public static byte[] Render(byte[] image, string text, BannerStyle s, int maxWidth)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.StreamSource = new MemoryStream(image);
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (maxWidth > 0) src.DecodePixelWidth = maxWidth;
        src.EndInit();
        src.Freeze();
        int w = src.PixelWidth, h = src.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(src, new Rect(0, 0, w, h));
            double bh = Math.Round(h * Math.Clamp(s.Height, 0.04, 0.25));
            dc.DrawRectangle(Ui.Brush(Hex(s.Background, "#FF000000")), null, new Rect(0, 0, w, bh));
            if (s.Line.Length > 0) dc.DrawRectangle(Ui.Brush(Hex(s.Line, "#E5A00D")), null, new Rect(0, bh, w, Math.Max(2, Math.Round(bh * 0.07))));
            var face = Ui.Typeface("Segoe UI", s.Bold ? FontWeights.Black : FontWeights.SemiBold);
            double size = bh * 0.6;
            FormattedText Make(double em) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, em, Ui.Brush(Hex(s.Foreground, "#FFFFFF")), 1.0);
            var ft = Make(size);
            if (ft.Width > w * 0.92) ft = Make(size * w * 0.92 / ft.Width);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (bh - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static string Hex(string v, string fallback)
    {
        try { ColorConverter.ConvertFromString(v); return v; }
        catch { return fallback; }
    }
}
