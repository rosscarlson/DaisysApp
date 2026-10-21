using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaisysApp.Applets.Gaming.Joy2Key;
using DaisysApp.Settings;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>The module's settings, in %APPDATA%\DaisysApp\Butt2KeyRemote.json. ButtScreens are files of their own.</summary>
public sealed class RemoteSettings
{
    private const string FileName = "Butt2KeyRemote";
    public const int DefaultPort = 38870;

    /// <summary>Running: devices can connect (on <see cref="Port"/>) and button presses, theirs and pop-outs', do what
    /// they're set to. Off to start with; kept as it was left after a restart.</summary>
    public bool Enabled { get; set; }

    /// <summary>A shortcut (keys, or a controller button) that switches it on and off, or null.</summary>
    public string? Hotkey { get; set; }

    public int Port { get; set; } = DefaultPort;
    public string PassphraseProtected { get; set; } = "";

    [JsonIgnore]
    public string Passphrase { get => Secret.Unprotect(PassphraseProtected); set => PassphraseProtected = Secret.Protect(value); }

    /// <summary>The BigButt in use when no BigButt's game is running (or always, without <see cref="AutoSwitch"/>).</summary>
    public string Active { get; set; } = BigButt.DefaultName;

    /// <summary>Switch to a BigButt while one of its games is running, and to Default when it closes.</summary>
    public bool AutoSwitch { get; set; } = true;

    public List<BigButt> BigButts { get; set; } = new();

    /// <summary>The devices that have connected, with their screens' sizes (offered as sizes for a ButtScreen).</summary>
    public List<SeenDevice> Devices { get; set; } = new();

    /// <summary>Where each popped-out ButtScreen is, by its id.</summary>
    public Dictionary<string, PopOutPlace> PopOuts { get; set; } = new();

    /// <summary>A green bubble in the window's title bar while it's on.</summary>
    public bool TitleBadge { get; set; } = true;

    public static RemoteSettings Load() => JsonStore.Load<RemoteSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}

/// <summary>A group of ButtScreens (usually for one game), in the order a device shows them, and the games that make it
/// the one in use while they run.</summary>
public sealed class BigButt
{
    public const string DefaultName = "Default";

    public string Name { get; set; } = DefaultName;
    public List<string> Programs { get; set; } = new();
    /// <summary>Its ButtScreens' ids, in order.</summary>
    public List<string> Screens { get; set; } = new();
}

public sealed class SeenDevice
{
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    /// <summary>Its screen in pixels, the way round it was held.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime LastSeen { get; set; }
}

public sealed class PopOutPlace
{
    public bool Open { get; set; }
    /// <summary>Where it was last (null: not placed yet, so it opens in the middle of the screen).</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    /// <summary>Its width in screen units (its height follows the ButtScreen's shape).</summary>
    public double Width { get; set; } = 320;
    /// <summary>1 = solid, down to 0.2.</summary>
    public double Opacity { get; set; } = 1;
    public bool OnTop { get; set; } = true;
    /// <summary>Can't be moved or resized (and the grip is hidden).</summary>
    public bool Locked { get; set; }
}

/// <summary>How a button looks. A ButtScreen has one for new buttons; each button has its own copy.</summary>
public sealed class ButtStyle
{
    public bool Rounded { get; set; } = true;
    /// <summary>Colours as #AARRGGBB (or #RRGGBB).</summary>
    public string Background { get; set; } = "#FF2D3540";
    public string Outline { get; set; } = "#FF4CC2FF";
    /// <summary>In the ButtScreen's pixels (0 = none).</summary>
    public double OutlineWidth { get; set; } = 4;
    public string Font { get; set; } = "Segoe UI";
    /// <summary>In the ButtScreen's pixels.</summary>
    public double FontSize { get; set; } = 40;
    public string FontColor { get; set; } = "#FFFFFFFF";
    public bool Bold { get; set; } = true;

    public ButtStyle Clone() => (ButtStyle)MemberwiseClone();
}

/// <summary>
/// One button on a ButtScreen: where it is on the grid (column and row from 0, and how many of each it covers), what it
/// shows (a label, an icon from the library or a picture of your own) and what pressing it does (a Button2Key action).
/// </summary>
public sealed class ButtButton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public string Label { get; set; } = "";
    /// <summary>An icon from the library (its name, e.g. "gamepad-variant"), or null.</summary>
    public string? Icon { get; set; }
    /// <summary>A picture of your own (a file in the Images folder), shown instead of the icon, or null.</summary>
    public string? Image { get; set; }
    public ButtStyle Style { get; set; } = new();
    public J2KAction? Action { get; set; }

    public ButtButton Clone() => JsonSerializer.Deserialize<ButtButton>(JsonSerializer.Serialize(this, ScreenStore.Options), ScreenStore.Options)!;
}

/// <summary>
/// A ButtScreen: a virtual screen of buttons for a device, laid out on a grid. <see cref="Width"/> × <see cref="Height"/>
/// is its size in pixels (the device's, or the shape wanted); the grid is <see cref="Columns"/> × <see cref="Rows"/>
/// cells with <see cref="Gap"/> pixels between them and round the edge. Saved as
/// %APPDATA%\DaisysApp\Butt2KeyRemote\Screens\&lt;id&gt;.json.
/// </summary>
public sealed class ButtScreen
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 2400;
    public int Columns { get; set; } = 4;
    public int Rows { get; set; } = 8;
    public double Gap { get; set; } = 24;
    public string Background { get; set; } = "#FF15181C";
    /// <summary>The colour every library icon on this ButtScreen is drawn in.</summary>
    public string IconColor { get; set; } = "#FFFFFFFF";
    /// <summary>How new buttons look.</summary>
    public ButtStyle ButtonStyle { get; set; } = new();
    public List<ButtButton> Buttons { get; set; } = new();
    /// <summary>Counts up each time it's saved, so a device knows its copy is old.</summary>
    public int Version { get; set; }

    [JsonIgnore] public double CellWidth => Math.Max(1, (Width - Gap * (Columns + 1)) / Columns);
    [JsonIgnore] public double CellHeight => Math.Max(1, (Height - Gap * (Rows + 1)) / Rows);

    /// <summary>A button's place in the ButtScreen's pixels.</summary>
    public System.Windows.Rect RectOf(ButtButton b) => new(
        Gap + b.Column * (CellWidth + Gap), Gap + b.Row * (CellHeight + Gap),
        b.ColumnSpan * CellWidth + (b.ColumnSpan - 1) * Gap, b.RowSpan * CellHeight + (b.RowSpan - 1) * Gap);

    /// <summary>A rounded button's corner radius: 18% of the smaller side of one cell.</summary>
    [JsonIgnore] public double CornerRadius => Math.Min(CellWidth, CellHeight) * 0.18;

    /// <summary>The button covering a cell, if any (but not <paramref name="except"/>).</summary>
    public ButtButton? At(int column, int row, ButtButton? except = null) => Buttons.FirstOrDefault(b => b != except
        && column >= b.Column && column < b.Column + b.ColumnSpan && row >= b.Row && row < b.Row + b.RowSpan);

    /// <summary>Whether a button fits there: on the grid and on no other button.</summary>
    public bool Fits(int column, int row, int columnSpan, int rowSpan, ButtButton? except = null)
    {
        if (column < 0 || row < 0 || columnSpan < 1 || rowSpan < 1 || column + columnSpan > Columns || row + rowSpan > Rows) return false;
        for (int c = column; c < column + columnSpan; c++)
            for (int r = row; r < row + rowSpan; r++)
                if (At(c, r, except) != null) return false;
        return true;
    }

    public ButtScreen Clone() => JsonSerializer.Deserialize<ButtScreen>(JsonSerializer.Serialize(this, ScreenStore.Options), ScreenStore.Options)!;

    /// <summary>Takes everything but its id and version from another (an undo).</summary>
    public void Assign(ButtScreen o)
    {
        Name = o.Name;
        Width = o.Width;
        Height = o.Height;
        Columns = o.Columns;
        Rows = o.Rows;
        Gap = o.Gap;
        Background = o.Background;
        IconColor = o.IconColor;
        ButtonStyle = o.ButtonStyle;
        Buttons = o.Buttons;
    }
}

/// <summary>ButtScreens on disk (one JSON file each), and the pictures used on buttons.</summary>
public static class ScreenStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } = Path.Combine(AppPaths.SettingsFolder, "Butt2KeyRemote");
    public static string ScreensFolder { get; } = Path.Combine(Folder, "Screens");
    public static string ImagesFolder { get; } = Path.Combine(Folder, "Images");

    public static List<ButtScreen> LoadAll()
    {
        var list = new List<ButtScreen>();
        try
        {
            if (Directory.Exists(ScreensFolder))
                foreach (var file in Directory.GetFiles(ScreensFolder, "*.json"))
                {
                    try
                    {
                        var s = JsonSerializer.Deserialize<ButtScreen>(File.ReadAllText(file), Options);
                        if (s == null) continue;
                        s.Id = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(s.Name)) s.Name = s.Id;
                        list.Add(s);
                    }
                    catch (Exception ex) { Logging.ErrorLog.Write("ButtScreen " + Path.GetFileName(file), ex); }
                }
        }
        catch (Exception ex) { Logging.ErrorLog.Write("ButtScreens", ex); }
        return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Save(ButtScreen s)
    {
        try
        {
            Directory.CreateDirectory(ScreensFolder);
            s.Version++;
            string path = Path.Combine(ScreensFolder, s.Id + ".json"), temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(s, Options));
            File.Move(temp, path, true);
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Saving ButtScreen", ex); }
    }

    public static void Delete(ButtScreen s)
    {
        try { File.Delete(Path.Combine(ScreensFolder, s.Id + ".json")); }
        catch (Exception ex) { Logging.ErrorLog.Write("Deleting ButtScreen", ex); }
    }

    /// <summary>Copies a picture in for a button (made at most 512 pixels across, as PNG): its file name, or null.</summary>
    public static string? AddImage(string path)
    {
        try
        {
            var source = new System.Windows.Media.Imaging.BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(path);
            source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            source.EndInit();
            System.Windows.Media.Imaging.BitmapSource image = source;
            double scale = Math.Min(1, 512.0 / Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale < 1) image = new System.Windows.Media.Imaging.TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            Directory.CreateDirectory(ImagesFolder);
            string name = Guid.NewGuid().ToString("N")[..12] + ".png";
            using (var file = File.Create(Path.Combine(ImagesFolder, name))) encoder.Save(file);
            return name;
        }
        catch (Exception ex)
        {
            Logging.ErrorLog.Write("Adding a button picture", ex);
            return null;
        }
    }

    public static string ImagePath(string name) => Path.Combine(ImagesFolder, Path.GetFileName(name));

    /// <summary>Deletes pictures no button uses any more.</summary>
    public static void TidyImages(IEnumerable<ButtScreen> screens)
    {
        try
        {
            if (!Directory.Exists(ImagesFolder)) return;
            var used = screens.SelectMany(s => s.Buttons).Select(b => b.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(ImagesFolder))
                if (!used.Contains(Path.GetFileName(file))) File.Delete(file);
        }
        catch (Exception ex) { Logging.ErrorLog.Write("Tidying button pictures", ex); }
    }

    /// <summary>A name no ButtScreen has: the name itself, or "name (2)"…</summary>
    public static string FreeName(string name, IEnumerable<ButtScreen> screens, ButtScreen? except = null)
    {
        var used = screens.Where(s => s != except).Select(s => s.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!used.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!used.Contains($"{name} ({i})")) return $"{name} ({i})";
    }
}

/// <summary>Text kept encrypted for this Windows user (DPAPI): the passphrase.</summary>
internal static class Secret
{
    public static string Protect(string value) => value.Length == 0 ? "" :
        Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(value), null, System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>Colours as the files keep them (#AARRGGBB).</summary>
internal static class ColorText
{
    public static System.Windows.Media.Color Parse(string? text, System.Windows.Media.Color fallback)
    {
        try { return text is { Length: > 0 } ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text) : fallback; }
        catch { return fallback; }
    }

    public static bool TryParse(string text, out System.Windows.Media.Color color)
    {
        try { color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text); return true; }
        catch { color = default; return false; }
    }

    public static System.Windows.Media.SolidColorBrush Brush(string? text, System.Windows.Media.Color fallback)
    {
        var b = new System.Windows.Media.SolidColorBrush(Parse(text, fallback));
        b.Freeze();
        return b;
    }

    public static string Text(System.Windows.Media.Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
