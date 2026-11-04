using System.Globalization;
using System.IO;
using System.Text;

namespace DaisysApp.Shared;

/// <summary>
/// A history kept as a file a day (<c>&lt;folder&gt;\&lt;prefix&gt;-yyyy-MM-dd.&lt;ext&gt;</c>): the days it has, its size, old
/// days deleted, all of it deleted, and a line appended to today's file (opened once and kept, shared for reading).
/// Hardware's performance and sensor history and Support's ping log are each one. Also the bits those logs share:
/// a time's slot (10 s rows), numbers written the same way. (Since Core 0.55: before, each had its own copy.)
/// </summary>
public sealed class DailyLog : IDisposable
{
    public string Folder { get; }
    public string Prefix { get; }
    public string Extension { get; }

    private readonly object gate = new();
    private StreamWriter? writer;
    private DateTime writerDay;

    public DailyLog(string folder, string prefix, string extension = "csv")
    {
        Folder = folder;
        Prefix = prefix;
        Extension = extension;
    }

    public string FileFor(DateTime day) => Path.Combine(Folder, $"{Prefix}-{day:yyyy-MM-dd}.{Extension}");

    private string Pattern => $"{Prefix}-????-??-??.{Extension}";

    /// <summary>The days that have a file, oldest first.</summary>
    public List<DateTime> Days()
    {
        try
        {
            if (!Directory.Exists(Folder)) return new();
            int skip = Prefix.Length + 1;
            return Directory.GetFiles(Folder, Pattern)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(n => n.Length > skip && DateTime.TryParseExact(n[skip..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                .OfType<DateTime>().OrderBy(d => d).ToList();
        }
        catch { return new(); }
    }

    /// <summary>The files' size on disk.</summary>
    public long SizeBytes()
    {
        try { return Directory.Exists(Folder) ? Directory.GetFiles(Folder, Pattern).Sum(f => new FileInfo(f).Length) : 0; }
        catch { return 0; }
    }

    /// <summary>Deletes the days older than <paramref name="keepDays"/> (0 or less: kept forever).</summary>
    public void Cleanup(int keepDays)
    {
        if (keepDays <= 0) return;
        var cutoff = DateTime.Today.AddDays(-Math.Max(1, keepDays));
        lock (gate)
            foreach (var day in Days().Where(d => d < cutoff))
                try { File.Delete(FileFor(day)); } catch { }
    }

    /// <summary>Deletes every day.</summary>
    public void DeleteAll()
    {
        lock (gate)
        {
            Close();
            foreach (var day in Days()) try { File.Delete(FileFor(day)); } catch { }
        }
    }

    /// <summary>
    /// Appends <paramref name="text"/> (whole lines, with their line ends) to <paramref name="time"/>'s day, writing
    /// <paramref name="header"/> first in a new file. False if it couldn't (the disk full, the folder locked).
    /// </summary>
    public bool Append(DateTime time, string text, string? header = null)
    {
        lock (gate)
        {
            try
            {
                if (writer == null || writerDay != time.Date)
                {
                    Close();
                    Directory.CreateDirectory(Folder);
                    string path = FileFor(time);
                    bool fresh = !File.Exists(path) || new FileInfo(path).Length == 0;
                    writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
                    writerDay = time.Date;
                    if (fresh && header != null) writer.WriteLine(header);
                }
                writer.Write(text);
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }
    }

    /// <summary>Runs <paramref name="change"/> on a day's file with nothing writing to it (a file being rewritten).</summary>
    public void WithFile(DateTime day, Action<string> change)
    {
        lock (gate)
        {
            if (writer != null && writerDay == day.Date) Close();
            change(FileFor(day));
        }
    }

    /// <summary>A day's lines, read while it may be written to (none if it isn't there).</summary>
    public string[] ReadLines(DateTime day)
    {
        string file = FileFor(day);
        if (!File.Exists(file)) return Array.Empty<string>();
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Closes today's file (it opens again with the next line).</summary>
    public void Close()
    {
        lock (gate)
        {
            try { writer?.Dispose(); } catch { }
            writer = null;
        }
    }

    public void Dispose() => Close();

    // ---------------------------------------------------------------- what the logs share

    /// <summary>The start of the <paramref name="seconds"/>-long slot <paramref name="t"/> is in (a 10 s row's time).</summary>
    public static DateTime SlotOf(DateTime t, int seconds) => new(t.Ticks - t.Ticks % TimeSpan.FromSeconds(seconds).Ticks, t.Kind);

    /// <summary>A number as the logs write it: invariant, up to 3 decimals, empty for none.</summary>
    public static string Num(double v, string format = "0.###") => double.IsNaN(v) ? "" : v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>A logged number back (NaN if it's empty or not one).</summary>
    public static double Parse(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    /// <summary>A row's time as the logs write it.</summary>
    public static string Time(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static bool TryTime(string s, out DateTime t) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

    /// <summary>Averages and peaks of samples gathered over a slot.</summary>
    public static (double Avg, double Max) AvgMax(IEnumerable<double> values)
    {
        double sum = 0, max = double.NaN;
        int n = 0;
        foreach (var v in values)
        {
            if (double.IsNaN(v)) continue;
            sum += v;
            n++;
            if (double.IsNaN(max) || v > max) max = v;
        }
        return n == 0 ? (double.NaN, double.NaN) : (sum / n, max);
    }
}
