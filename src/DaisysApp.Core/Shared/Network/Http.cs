using System.Net;
using System.Net.Http;

namespace DaisysApp.Shared.Network;

/// <summary>
/// One connection pool for every module's web requests (Support's lookups and map, iRacing, the hardware monitor,
/// updates): each caller gets its own <see cref="HttpClient"/> (its timeout and headers) over one shared handler, so
/// connections and DNS answers are pooled and recycled (since Core 0.55: before, each made its own).
/// </summary>
public static class Http
{
    private static readonly SocketsHttpHandler shared = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>The app's own User-Agent: "DaisysApp/3.38.0".</summary>
    public static string UserAgent => $"{AppPaths.ShortName}/{(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Http).Assembly).GetName().Version?.ToString(3)}";

    /// <summary>A client on the shared pool, sending the app's User-Agent (plus <paramref name="userAgent"/> after it,
    /// or instead of it when it starts with "Mozilla"), and <paramref name="accept"/> if given. Keep it (a static field):
    /// it's cheap, but made once is enough.</summary>
    public static HttpClient Client(TimeSpan timeout, string? accept = null, string? userAgent = null)
    {
        var h = new HttpClient(shared, disposeHandler: false) { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent?.StartsWith("Mozilla") == true ? userAgent : userAgent != null ? $"{UserAgent} {userAgent}" : UserAgent);
        if (accept != null) h.DefaultRequestHeaders.Accept.ParseAdd(accept);
        return h;
    }

    public static HttpClient Client(double seconds, string? accept = null, string? userAgent = null) =>
        Client(TimeSpan.FromSeconds(seconds), accept, userAgent);
}
