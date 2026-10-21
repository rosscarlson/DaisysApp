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

    /// <summary>The BigButt in use, or "" when there are none (devices are told so).</summary>
    public string Active { get; set; } = "";

    /// <summary>The BigButt last picked by hand (in the tab, the tray or on a device): the one in use again when a
    /// game that switched to its own BigButt closes. No BigButt is special: any can be renamed, deleted or have games.</summary>
    public string Picked { get; set; } = "";

    /// <summary>Switch to a BigButt while one of its games is running, and back to the one picked before when it closes.</summary>
    public bool AutoSwitch { get; set; } = true;

    /// <summary>Switch Button2Key Remote on while a BigButt's game is running (if it's off), and off again when the
    /// game closes, if the game switched it on (as ForzaWheel's "Start with a game").</summary>
    public bool StartWithGame { get; set; } = true;

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
    /// <summary>The name the first BigButt had before 2.7.7 (when it was special); now an ordinary name.</summary>
    public const string DefaultName = "Default";

    public string Name { get; set; } = "";
    public List<string> Programs { get; set; } = new();
    /// <summary>Its ButtScreens' ids, in order.</summary>
    public List<string> Screens { get; set; } = new();
}

/// <summary>Sizes a ButtScreen can be made at (the editor's Size list, the template wizard's devices), held portrait.</summary>
public sealed record DeviceShape(string Name, int Width, int Height)
{
    public static readonly DeviceShape[] All =
    {
        // devices of our own (a device that connects is listed by itself too, with the size it says, unless it's one of these)
        new("Google Pixel 10 Pro", 1280, 2856),
        // iPhones, one line per size (models of the same size share it)
        new("iPhone 16 Pro, 17, 17 Pro, 18 Pro", 1206, 2622),
        new("iPhone 16 Pro Max, 17 Pro Max, 18 Pro Max", 1320, 2868),
        new("iPhone Air", 1260, 2736),
        new("iPhone 15, 15 Pro, 16", 1179, 2556),
        new("iPhone 15 Plus, 15 Pro Max, 16 Plus", 1290, 2796),
        new("iPhone 12, 13, 14, 16e", 1170, 2532),
        new("iPhone 12 Pro Max, 13 Pro Max, 14 Plus", 1284, 2778),
        new("Phone 20:9", 1080, 2400), new("Phone 19.5:9", 1080, 2340), new("Phone 18:9 (2:1)", 1080, 2160), new("Phone 16:9", 1080, 1920),
        new("Tablet 4:3", 1536, 2048), new("Tablet 3:2", 1600, 2400), new("Tablet 16:10", 1600, 2560), new("Square 1:1", 1440, 1440),
    };

    /// <summary>A real device (a phone by name), not a shape such as "Phone 20:9".</summary>
    public bool IsDevice => !Name.StartsWith("Phone ") && !Name.StartsWith("Tablet ") && !Name.StartsWith("Square ");

    /// <summary>The devices that have connected (newest first, with their own sizes; not one the same size as a device
    /// above, which is listed by its full name, e.g. "Google Pixel 10 Pro"), then the rest: a name and a size held portrait.</summary>
    public static List<DeviceShape> Choices(IEnumerable<SeenDevice> seen)
    {
        var list = new List<DeviceShape>();
        foreach (var d in seen.Where(d => d.Width > 0 && d.Height > 0).OrderByDescending(d => d.LastSeen))
        {
            int w = Math.Min(d.Width, d.Height), h = Math.Max(d.Width, d.Height);
            if (All.Any(x => x.IsDevice && x.Width == w && x.Height == h) || list.Any(x => x.Width == w && x.Height == h)) continue;
            list.Add(new DeviceShape(F("{0} ({1} × {2})", d.Name, w, h), w, h));
        }
        list.AddRange(All.Select(x => x with { Name = T(x.Name) }));
        return list;
    }
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

/// <summary>
/// A ButtScreen's colours in one go (the editor's Style → Theme): its background and icons, and every button's
/// background, outline and text. <see cref="Was"/> is its name before 2.8.1 (templates may still use it).
/// </summary>
public sealed record ButtTheme(string Name, string Was, string Background, string Icons, string Button, string Outline, string Text)
{
    public static readonly ButtTheme[] All =
    {
        new("Slate", "Daisy's Derrière", "#FF15181C", "#FFFFFFFF", "#FF2D3540", "#FF4CC2FF", "#FFFFFFFF"),
        new("Coral", "Cheeky Peach", "#FF2B1D1A", "#FF2B1D1A", "#FFF09A7E", "#FFFFD7C9", "#FF2B1D1A"),
        new("Magenta", "Bubble Butt", "#FF24122A", "#FFFFFFFF", "#FFE0569B", "#FFFFC4E1", "#FFFFFFFF"),
        new("Night Sky", "Full Moon", "#FF0D1120", "#FFF6E7A1", "#FF27314C", "#FFC9D6FF", "#FFF2F4FF"),
        new("Bronze", "Junk in the Trunk", "#FF1C1510", "#FFFFCF86", "#FF6A4A2E", "#FFC89B6D", "#FFFFF3E3"),
        new("Racing Orange", "Badonkadonk", "#FF0F0F0F", "#FF111111", "#FFFF6A00", "#FFFFB27A", "#FF111111"),
        new("Royal Purple", "Gluteus Maximus", "#FF180F24", "#FFE2B94A", "#FF452766", "#FFE2B94A", "#FFFFF6D8"),
        new("Steel Blue", "Plumber's Crack", "#FF121D2A", "#FFFFFFFF", "#FF33557A", "#FF8FB3D9", "#FFF0F6FF"),
        new("Teal", "Tushie Teal", "#FF0B2024", "#FFA6F0E6", "#FF1D6871", "#FF7FD8D1", "#FFE9FFFC"),
        new("Neon", "Booty Call", "#FF08080F", "#FF00F0FF", "#FF1A1A2C", "#FF00F0FF", "#FFFF3EA5"),
    };

    /// <summary>A theme by its name (or its name before 2.8.1).</summary>
    public static ButtTheme? Named(string? name) => string.IsNullOrWhiteSpace(name) ? null
        : All.FirstOrDefault(t => Same(t.Name, name) || Same(t.Was, name));

    /// <summary>The theme a ButtScreen's colours are, if they're one.</summary>
    public static ButtTheme? Of(ButtScreen s) => All.FirstOrDefault(t => Same(t.Background, s.Background) && Same(t.Icons, s.IconColor)
        && s.Buttons.All(b => Same(t.Button, b.Style.Background) && Same(t.Outline, b.Style.Outline) && Same(t.Text, b.Style.FontColor)));

    /// <summary>Puts it on a ButtScreen: its colours, every button's, and new buttons'.</summary>
    public void Apply(ButtScreen s)
    {
        s.Background = Background;
        s.IconColor = Icons;
        foreach (var st in s.Buttons.Select(b => b.Style).Append(s.ButtonStyle))
        {
            st.Background = Button;
            st.Outline = Outline;
            st.FontColor = Text;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

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
    /// <summary>Its label is shown (unless the ButtScreen's <see cref="ButtScreen.Titles"/> says otherwise); off: only its icon.</summary>
    public bool ShowText { get; set; } = true;
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
    /// <summary>Every button's label shown (true) or hidden (false) whatever each says; null: each button's own Show Text.</summary>
    public bool? Titles { get; set; }
    /// <summary>Counts up each time it's saved, so a device knows its copy is old.</summary>
    public int Version { get; set; }

    /// <summary>The label a button shows: its own, unless hidden by it or by <see cref="Titles"/> ("" then).</summary>
    public string LabelOf(ButtButton b) => (Titles ?? b.ShowText) ? b.Label : "";

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
        Titles = o.Titles;
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
