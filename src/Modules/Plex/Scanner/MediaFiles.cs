using System.IO;
using System.Net.Http;
using System.Text.Json;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Plex.Scanner;

/// <summary>One file of a movie or episode, as Plex lists it (a version of a title is a Media; a file a Part of it).</summary>
public sealed class MediaFile
{
    public string ItemKey { get; init; } = "";
    public string MediaId { get; init; } = "";
    public string Guid { get; init; } = "";
    public string LibraryKey { get; init; } = "";
    public string Library { get; init; } = "";
    /// <summary>The movie's title, or the episode's.</summary>
    public string Title { get; init; } = "";
    public int Year { get; init; }
    /// <summary>An episode's show (empty for a movie).</summary>
    public string Show { get; init; } = "";
    public string ShowKey { get; init; } = "";
    public int Season { get; init; }
    public int Episode { get; init; }
    /// <summary>The path on the server.</summary>
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public long DurationMs { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>The whole file's bit rate, kbit/s.</summary>
    public int BitrateKbps { get; init; }
    public string VideoCodec { get; init; } = "";
    public string Resolution { get; init; } = "";
    public string Container { get; init; } = "";
    public string AudioCodec { get; init; } = "";
    public int AudioChannels { get; init; }
    public string VideoProfile { get; init; } = "";
    public DateTime? Added { get; init; }

    public bool IsEpisode => ShowKey.Length > 0;

    /// <summary>"Colony (2026)" or "Buffy · S02E20 · Go Fish".</summary>
    public string Name => IsEpisode ? $"{Show} · S{Season:00}E{Episode:00} · {Title}" : Year > 0 ? $"{Title} ({Year})" : Title;

    /// <summary>"1080p" or "4K" (Plex's resolution, else the height).</summary>
    public string ResolutionText => Resolution switch
    {
        "4k" => "4K", "sd" => "SD", "" => Height > 0 ? Height + "p" : "",
        _ => char.IsDigit(Resolution[0]) ? Resolution + "p" : Resolution.ToUpperInvariant(),
    };

    public string CodecText => Codec(VideoCodec);

    public static string Codec(string c) => c switch { "hevc" => "HEVC", "h264" => "H.264", "av1" => "AV1", "mpeg4" => "MPEG-4", "vc1" => "VC-1", "mpeg2video" => "MPEG-2", _ => c.ToUpperInvariant() };

    public double Mbps => BitrateKbps / 1000.0;
    public string MbpsText => BitrateKbps > 0 ? F("{0:0.0} Mbit/s", Mbps) : "";
    public string SizeText => Sizes.Bytes(Size);
    public string DurationText => Views.PlexUi.Clock(DurationMs);
    public string AddedText => Added?.ToString("d") ?? "";
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Reads a library's files from the server (every movie, or every episode, with its versions and files), a page at a
/// time, a few pages at once; kept for the session, so the Scanner's tools share one reading.
/// </summary>
public sealed class MediaFiles(PlexService service)
{
    private const int Page = 1000;
    private readonly Dictionary<string, (DateTime At, List<MediaFile> Files)> read = new();

    /// <summary>When the library was read, if it has been.</summary>
    public DateTime? ReadAt(string libraryKey) => read.TryGetValue(libraryKey, out var r) ? r.At : null;

    public void Forget(string? libraryKey = null)
    {
        if (libraryKey == null) read.Clear();
        else read.Remove(libraryKey);
    }

    /// <summary>Takes out files deleted here.</summary>
    public void Removed(MediaFile f)
    {
        if (read.TryGetValue(f.LibraryKey, out var r)) r.Files.Remove(f);
    }

    /// <summary>Every file in the library (read once, then kept); <paramref name="progress"/> gets how many of how many.</summary>
    public async Task<List<MediaFile>> AllAsync(PlexLibrary lib, Action<long, long>? progress, CancellationToken cancel)
    {
        if (read.TryGetValue(lib.Key, out var r)) return r.Files;
        var files = await ReadAsync(lib, "", progress, cancel);
        read[lib.Key] = (DateTime.Now, files);
        return files;
    }

    /// <summary>The library's titles with more than one version (Plex's own duplicate filter: quick).</summary>
    public Task<List<MediaFile>> DuplicatesAsync(PlexLibrary lib, Action<long, long>? progress, CancellationToken cancel) =>
        ReadAsync(lib, "&duplicate=1", progress, cancel);

    private async Task<List<MediaFile>> ReadAsync(PlexLibrary lib, string filter, Action<long, long>? progress, CancellationToken cancel)
    {
        var c = service.Need();
        int type = lib.Type == "show" ? 4 : 1;
        string path = $"/library/sections/{lib.Key}/all?type={type}{filter}&excludeFields=summary,tagline&excludeElements=Genre,Role,Director,Writer,Country,Image,Guid,Collection,Label,Producer";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var first = await c.GetAsync($"{path}&X-Plex-Container-Start=0&X-Plex-Container-Size={Page}", cancel);
        long total = PlexClient.Num(first, "totalSize");
        var pages = new List<JsonElement> { first };
        long done = PlexClient.List(first, "Metadata").Count();
        progress?.Invoke(done, total);
        var starts = new Queue<long>();
        for (long s = Page; s < total; s += Page) starts.Enqueue(s);
        var gate = new SemaphoreSlim(3);
        var tasks = starts.Select(async s =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                var p = await c.GetAsync($"{path}&X-Plex-Container-Start={s}&X-Plex-Container-Size={Page}", cancel);
                lock (pages) { pages.Add(p); done += PlexClient.List(p, "Metadata").Count(); progress?.Invoke(done, total); }
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
        var files = new List<MediaFile>();
        foreach (var p in pages)
            foreach (var m in PlexClient.List(p, "Metadata"))
                files.AddRange(Files(m, lib));
        Log.Here.Info($"Plex Scanner: read {lib.Title}{filter}: {total} titles, {files.Count} files in {sw.ElapsedMilliseconds} ms");
        return files;
    }

    private static IEnumerable<MediaFile> Files(JsonElement m, PlexLibrary lib)
    {
        bool episode = PlexClient.Str(m, "type") == "episode";
        foreach (var media in PlexClient.List(m, "Media"))
            foreach (var part in PlexClient.List(media, "Part"))
                yield return new MediaFile
                {
                    ItemKey = PlexClient.Str(m, "ratingKey") ?? "",
                    MediaId = PlexClient.Num(media, "id").ToString(),
                    Guid = PlexClient.Str(m, "guid") ?? "",
                    LibraryKey = lib.Key,
                    Library = lib.Title,
                    Title = PlexClient.Str(m, "title") ?? "",
                    Year = (int)PlexClient.Num(m, "year"),
                    Show = episode ? PlexClient.Str(m, "grandparentTitle") ?? "" : "",
                    ShowKey = episode ? PlexClient.Str(m, "grandparentRatingKey") ?? "" : "",
                    Season = (int)PlexClient.Num(m, "parentIndex"),
                    Episode = (int)PlexClient.Num(m, "index"),
                    Path = PlexClient.Str(part, "file") ?? "",
                    Size = PlexClient.Num(part, "size"),
                    DurationMs = PlexClient.Num(part, "duration") is > 0 and var d ? d : PlexClient.Num(media, "duration"),
                    Width = (int)PlexClient.Num(media, "width"),
                    Height = (int)PlexClient.Num(media, "height"),
                    BitrateKbps = (int)PlexClient.Num(media, "bitrate"),
                    VideoCodec = PlexClient.Str(media, "videoCodec") ?? "",
                    Resolution = PlexClient.Str(media, "videoResolution") ?? "",
                    Container = PlexClient.Str(media, "container") ?? "",
                    AudioCodec = PlexClient.Str(media, "audioCodec") ?? "",
                    AudioChannels = (int)PlexClient.Num(media, "audioChannels"),
                    VideoProfile = PlexClient.Str(media, "videoProfile") ?? "",
                    Added = PlexClient.Time(m, "addedAt"),
                };
    }

    /// <summary>Asks Plex to look at a folder again (after a file there was deleted or replaced).</summary>
    public async Task RescanFolderAsync(string libraryKey, string serverFolder)
    {
        try { await service.Need().SendAsync(HttpMethod.Get, $"/library/sections/{libraryKey}/refresh?path={Uri.EscapeDataString(serverFolder)}"); }
        catch (Exception ex) { Log.Here.Warn($"Plex Scanner: rescan of {serverFolder}: {ex.Message}"); }
    }
}
