using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>
/// nmap, when it's installed (nmap.org): its version detection, default scripts and, when it's allowed to (it needs
/// Npcap and administrator rights), its operating system detection. Its XML is read into the scan's report.
/// </summary>
internal static class Nmap
{
    public sealed class Result
    {
        public readonly List<(string Name, int Accuracy)> Os = new();
        /// <summary>Each port nmap found open: its service, product, version and extra information.</summary>
        public readonly List<(int Port, string Protocol, string Service, string Product, string Version, string Extra, string DeviceType)> Ports = new();
        /// <summary>Each script's output: which port (0: the host), the script and what it said.</summary>
        public readonly List<(int Port, string Script, string Output)> Scripts = new();
        public string Command = "";
        public string? Error;
    }

    /// <summary>nmap.exe, if it's installed (on the PATH or in its usual folder).</summary>
    public static string? Find()
    {
        var places = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }.Select(p => Path.Combine(p, "Nmap")));
        foreach (var dir in places)
        {
            try
            {
                string file = Path.Combine(dir.Trim('"'), "nmap.exe");
                if (File.Exists(file)) return file;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>Runs nmap on one address (with OS detection, then without it if it isn't allowed to).</summary>
    public static async Task<Result> RunAsync(string exe, string ip, bool allPorts, CancellationToken ct)
    {
        string ports = allPorts ? "-p-" : "--top-ports 1000";
        var r = await RunOnceAsync(exe, $"-sV -sC -O --osscan-guess -T4 -Pn {ports} -oX - {ip}", ct);
        if (r.Error != null && (r.Error.Contains("privileges", StringComparison.OrdinalIgnoreCase) || r.Error.Contains("root", StringComparison.OrdinalIgnoreCase) || r.Error.Contains("npcap", StringComparison.OrdinalIgnoreCase)))
            r = await RunOnceAsync(exe, $"-sT -sV -sC -T4 -Pn {ports} -oX - {ip}", ct);
        return r;
    }

    private static async Task<Result> RunOnceAsync(string exe, string args, CancellationToken ct)
    {
        var result = new Result { Command = "nmap " + args };
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi);
        if (p == null) { result.Error = T("nmap didn't start"); return result; }
        using var kill = ct.Register(() => { try { p.Kill(true); } catch { } });
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        string xml = await output, err = (await error).Trim();
        Logging.Log.Here.Info($"{result.Command}: exit {p.ExitCode}{(err.Length > 0 ? ", " + err.Split('\n')[0] : "")}");
        if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<nmaprun")) { result.Error = err.Length > 0 ? err : F("nmap stopped ({0})", p.ExitCode); return result; }
        if (p.ExitCode != 0 && err.Length > 0) result.Error = err;
        try { Read(XDocument.Parse(xml), result); }
        catch (Exception ex) { result.Error = ex.Message; }
        return result;
    }

    private static void Read(XDocument x, Result r)
    {
        var host = x.Root?.Element("host");
        if (host == null) return;
        foreach (var m in host.Element("os")?.Elements("osmatch") ?? [])
            r.Os.Add(((string?)m.Attribute("name") ?? "", (int?)m.Attribute("accuracy") ?? 0));
        foreach (var port in host.Element("ports")?.Elements("port") ?? [])
        {
            int id = (int?)port.Attribute("portid") ?? 0;
            if ((string?)port.Element("state")?.Attribute("state") != "open") continue;
            var s = port.Element("service");
            string A(string n) => (string?)s?.Attribute(n) ?? "";
            r.Ports.Add((id, (string?)port.Attribute("protocol") ?? "tcp", A("name"), A("product"), A("version"), A("extrainfo"), A("devicetype")));
            foreach (var sc in port.Elements("script")) r.Scripts.Add((id, (string?)sc.Attribute("id") ?? "", ((string?)sc.Attribute("output") ?? "").Trim()));
        }
        foreach (var sc in host.Element("hostscript")?.Elements("script") ?? [])
            r.Scripts.Add((0, (string?)sc.Attribute("id") ?? "", ((string?)sc.Attribute("output") ?? "").Trim()));
    }
}
