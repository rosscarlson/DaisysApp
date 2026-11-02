using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DaisysApp.Shell;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Puts NaturalVoiceSAPIAdapter on this PC (Settings → AI → Install Narrator's Voices, since 3.36.11), which is what opens
/// Narrator's natural voices to other programs. Not shipped: one release, by its hash, is fetched from its GitHub page.
/// </summary>
public static class VoiceAdapter
{
    public enum Outcome { Installed, Declined, DownloadFailed, BadFile, RegisterFailed }

    public static async Task<Outcome> InstallAsync() => await ModuleTasks.RunAsync<VoiceAdapterTask>([], elevated: true) switch
    {
        null => Outcome.Declined,
        0 => Outcome.Installed,
        VoiceAdapterTask.BadFile => Outcome.BadFile,
        VoiceAdapterTask.RegisterFailed => Outcome.RegisterFailed,
        _ => Outcome.DownloadFailed,
    };
}

/// <summary>Runs as administrator (<see cref="ModuleTasks"/>): fetches, checks and registers it under Program Files, where a program without rights can't change what every program then loads.</summary>
public sealed class VoiceAdapterTask : IModuleTask
{
    public const int BadFile = 2, RegisterFailed = 3;
    private const string Release = "v0.2.11";
    private const string Page = "https://github.com/gexgd0419/NaturalVoiceSAPIAdapter/releases/download/" + Release + "/NaturalVoiceSAPIAdapter_" + Release + "_";

    public int Run(string[] args)
    {
        bool arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        var (zip, hash, folder) = arm
            ? ("ARM64.zip", "4c924250cbacacb0581cc842ca533c9063666418b2d90fa1f5edaf0113519268", "ARM64")
            : ("x86_x64.zip", "01a0d38e4b42750af3cbd82a120215c83bea9a5b1ae133805e0f82819c911994", "x64");
        byte[] data;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            data = http.GetByteArrayAsync(Page + zip).GetAwaiter().GetResult();
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Narrator's voices: download", ex); return 1; }
        if (Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() != hash) return BadFile;
        try
        {
            string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NaturalVoiceSAPIAdapter", Release);
            Directory.CreateDirectory(target);
            using (var archive = new ZipArchive(new MemoryStream(data)))
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(folder + "/") && e.Name.Length > 0))
                    entry.ExtractToFile(Path.Combine(target, entry.Name), overwrite: true);
            string regsvr = Path.Combine(Environment.SystemDirectory, "regsvr32.exe");
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(regsvr) { ArgumentList = { "/s", Path.Combine(target, "NaturalVoiceSAPIAdapter.dll") }, UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit();
            return p.ExitCode == 0 ? 0 : RegisterFailed;
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Narrator's voices: install", ex); return RegisterFailed; }
    }
}
