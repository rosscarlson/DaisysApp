using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Resizer;

/// <summary>
/// Lets scripts and the Stream Deck apply profiles while the app runs, as in Resize Rabbit:
/// <c>echo apply-profile "my profile" &gt; \\.\pipe\resize-rabbit</c> (also <c>apply-group</c> and <c>show</c>).
/// Listens on Resize Rabbit's pipe name, so existing scripts keep working, and on <c>\\.\pipe\daisysapp-resizer</c>.
/// </summary>
public sealed class CommandPipe : IDisposable
{
    public static readonly string[] PipeNames = ["resize-rabbit", "daisysapp-resizer"];

    private static readonly Regex Tokens = new("(\"[^\"]+\"|\\S+)", RegexOptions.Compiled);
    private readonly CancellationTokenSource cts = new();

    /// <summary>Raised on a background thread with the command and its arguments (quotes removed).</summary>
    public event Action<string, IReadOnlyList<string>>? Command;

    public void Start()
    {
        foreach (string name in PipeNames) _ = ListenAsync(name, cts.Token);
    }

    private async Task ListenAsync(string name, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                var buffer = new byte[1024];
                int read = await server.ReadAsync(buffer, ct);
                var tokens = Tokens.Matches(Encoding.UTF8.GetString(buffer, 0, read))
                                   .Select(m => m.Value.Trim('"')).Where(t => t.Length > 0).ToList();
                if (tokens.Count > 0) Command?.Invoke(tokens[0].ToLowerInvariant(), tokens.Skip(1).ToList());
            }
            catch (OperationCanceledException) { return; }
            catch (IOException ex)
            {
                // e.g. Resize Rabbit itself is running and already owns this pipe name: try again later
                ErrorLog.Write($"Resizer pipe {name}", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch { return; }
            }
            catch (Exception ex)
            {
                ErrorLog.Write($"Resizer pipe {name}", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { return; }
            }
        }
    }

    public void Dispose() => cts.Cancel();
}
