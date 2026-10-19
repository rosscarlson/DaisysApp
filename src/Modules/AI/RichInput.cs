using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.AI;

/// <summary>An image in a message (pasted into the box, or read back): PNG or JPEG, as base64.</summary>
public sealed record ImageData(string MediaType, string Data)
{
    /// <summary>The longest side an image is sent at (what the models read anyway).</summary>
    private const int MaxSide = 1568;

    public static ImageData From(BitmapSource source)
    {
        BitmapSource b = source;
        double scale = Math.Min(1, MaxSide / (double)Math.Max(1, Math.Max(source.PixelWidth, source.PixelHeight)));
        if (scale < 1) b = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        if (b.Format != PixelFormats.Bgra32 && b.Format != PixelFormats.Bgr32) b = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        byte[] Encode(BitmapEncoder encoder)
        {
            encoder.Frames.Add(BitmapFrame.Create(b));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        var png = Encode(new PngBitmapEncoder());
        // (a big photo: as a JPEG, under the APIs' 5 MB)
        if (png.Length > 3_500_000) return new("image/jpeg", Convert.ToBase64String(Encode(new JpegBitmapEncoder { QualityLevel = 85 })));
        return new("image/png", Convert.ToBase64String(png));
    }

    /// <summary>A data: URL's image ("data:image/png;base64,…"), or null.</summary>
    public static ImageData? FromDataUrl(string? url)
    {
        var m = Regex.Match(url ?? "", @"^data:(image/[a-z+]+);base64,(.+)$", RegexOptions.Singleline);
        return m.Success ? new(m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    public string DataUrl => $"data:{MediaType};base64,{Data}";

    public BitmapSource? Bitmap()
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = new MemoryStream(Convert.FromBase64String(Data));
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    /// <summary>A thumbnail of it (click: opened full size in the image viewer).</summary>
    public FrameworkElement Thumbnail(double height = 140)
    {
        var bmp = Bitmap();
        if (bmp == null) return new TextBlock { Text = "[" + T("image") + "]", Opacity = 0.7 };
        double scale = Math.Min(1, Math.Min(height / Math.Max(1, bmp.PixelHeight), 320.0 / Math.Max(1, bmp.PixelWidth)));
        var img = new Image { Source = bmp, Width = bmp.PixelWidth * scale, Height = bmp.PixelHeight * scale, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, ToolTip = F("{0} × {1}: click to open it", bmp.PixelWidth, bmp.PixelHeight) };
        img.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                string file = Path.Combine(Path.GetTempPath(), "DaisysApp-image-" + Guid.NewGuid().ToString("N")[..8] + (MediaType == "image/jpeg" ? ".jpg" : ".png"));
                File.WriteAllBytes(file, Convert.FromBase64String(Data));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch { }
        };
        return new Border { Child = img, Margin = new Thickness(0, 6, 6, 0), CornerRadius = new CornerRadius(4), ClipToBounds = true };
    }
}

/// <summary>
/// The box you write in: rich text. Formatting pasted in (from Word, a web page, an editor, the conversation) is kept as
/// what it means (headings, bold, italic, code, lists, links, tables), and so is typing with Ctrl+B / Ctrl+I; images can
/// be pasted (or dropped) in too. What's sent is Markdown, which every model reads, with the images beside it.
/// </summary>
internal sealed class RichInput : RichTextBox
{
    private static readonly string[] ImageFiles = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

    public RichInput()
    {
        AcceptsReturn = true;
        AcceptsTab = false;
        AllowDrop = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalContentAlignment = VerticalAlignment.Top;
        // like the app's text boxes, but the text starts at the top left (theirs is centred, for one line)
        Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RichTextBox">
              <Border x:Name="Bd" CornerRadius="4" BorderThickness="1" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}">
                <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalAlignment="Stretch"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocused" Value="True">
                  <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
        SetResourceReference(SelectionBrushProperty, "AccentBrush");
        Paint(null, null);
        Document = NewDocument();
        Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        DataObject.AddPastingHandler(this, OnPasting);
        PreviewDragOver += (_, e) =>
        {
            if (DroppedImages(e.Data).Count > 0) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
        };
        PreviewDrop += (_, e) =>
        {
            var images = DroppedImages(e.Data);
            if (images.Count == 0) return;
            e.Handled = true;
            foreach (var i in images) InsertImage(i);
        };
    }

    private static FlowDocument NewDocument() => new() { PagePadding = new Thickness(0) };

    /// <summary>Its colours: the look's, or the theme's (null).</summary>
    public void Paint(Brush? background, Brush? text)
    {
        if (background != null) Background = background; else SetResourceReference(BackgroundProperty, "ControlBrush");
        if (text != null) { Foreground = text; CaretBrush = text; }
        else { SetResourceReference(ForegroundProperty, "TextBrush"); SetResourceReference(CaretBrushProperty, "TextBrush"); }
    }

    /// <summary>Nothing written and no image.</summary>
    public bool IsEmpty => new TextRange(Document.ContentStart, Document.ContentEnd).Text.Trim().Length == 0
        && !Document.Blocks.SelectMany(Descendants).Any(e => e is InlineUIContainer or BlockUIContainer);

    public void Clear() => Document = NewDocument();

    /// <summary>What's written, as Markdown (an image as "[image n]" where it is), and the images.</summary>
    public (string Text, List<ImageData> Images) Message()
    {
        var images = new List<ImageData>();
        string text = FlowMarkdown.From(Document, FontSize, Document.FontFamily, b => { images.Add(ImageData.From(b)); return F("[image {0}]", images.Count); });
        return (text, images);
    }

    // ---------------------------------------------------------------- pasting

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;
        e.CancelCommand();
        Dispatcher.BeginInvoke(() =>
        {
            try { PasteData(data); }
            catch (Exception ex)
            {
                Logging.Log.Here.Warn("Pasting into the AI box: " + ex.Message);
                if (data.GetData(DataFormats.UnicodeText) is string plain) InsertPlain(plain);
            }
        });
    }

    /// <summary>Formatting as Markdown (shown formatted), an image as an image, plain text as it is.</summary>
    private void PasteData(IDataObject data)
    {
        string? plain = data.GetData(DataFormats.UnicodeText) as string;
        var images = new List<BitmapSource>();
        string? md = null;
        if (data.GetDataPresent(DataFormats.Xaml) || data.GetDataPresent(DataFormats.Rtf))
        {
            string format = data.GetDataPresent(DataFormats.Xaml) ? DataFormats.Xaml : DataFormats.Rtf;
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            object raw = data.GetData(format);
            using (var ms = raw is MemoryStream s ? s : new MemoryStream(Encoding.UTF8.GetBytes(raw?.ToString() ?? "")))
                range.Load(ms, format);
            md = FlowMarkdown.From(doc, FlowMarkdown.BodySize(doc), null, b => { images.Add(b); return ""; });
        }
        else if (data.GetData(DataFormats.Html) is string html)
        {
            var (hmd, hImages) = HtmlMarkdown.From(html, plain);
            md = hmd;
            foreach (var i in hImages) if (i.Bitmap() is { } b) images.Add(b);
        }
        // (HTML read in the wrong encoding: the plain text is better)
        if (md != null && md.Contains("â€") && plain != null && !plain.Contains("â€")) md = plain;
        bool noText = md == null ? string.IsNullOrWhiteSpace(plain) : md.Trim().Length == 0;
        if (noText && images.Count == 0) images.AddRange(ClipboardImages(data));
        if (md is { } m && m.Trim().Length > 0)
        {
            // no formatting at all: as plain text (as it was copied)
            if (plain != null && Normal(m) == Normal(plain)) InsertPlain(plain);
            else InsertMarkdown(m);
        }
        else if (images.Count == 0 && plain != null) InsertPlain(plain);
        foreach (var i in images) InsertImage(i);

        static string Normal(string s) => Regex.Replace(s, @"\s+", " ").Trim();
    }

    private void InsertPlain(string text)
    {
        Selection.Text = text;
        CaretPosition = Selection.End;
        Selection.Select(CaretPosition, CaretPosition);
    }

    /// <summary>Markdown, drawn as the conversation draws it (so what's sent is what's shown).</summary>
    private void InsertMarkdown(string md)
    {
        var doc = new FlowDocument();
        foreach (var b in Markdown.Blocks(md, this, FontSize)) doc.Blocks.Add(b);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        using var ms = new MemoryStream();
        new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Xaml);
        ms.Position = 0;
        Selection.Load(ms, DataFormats.Xaml);
        CaretPosition = Selection.End;
        Selection.Select(CaretPosition, CaretPosition);
    }

    /// <summary>An image where the caret is, as a thumbnail (sent full size, up to 1568 pixels a side).</summary>
    public void InsertImage(BitmapSource bmp)
    {
        if (!bmp.IsFrozen && bmp.CanFreeze) bmp.Freeze();
        if (!Selection.IsEmpty) Selection.Text = "";
        double scale = Math.Min(1, Math.Min(120.0 / Math.Max(1, bmp.PixelHeight), 280.0 / Math.Max(1, bmp.PixelWidth)));
        var img = new Image { Source = bmp, Width = bmp.PixelWidth * scale, Height = bmp.PixelHeight * scale, Stretch = Stretch.Uniform, Margin = new Thickness(2), ToolTip = F("{0} × {1}", bmp.PixelWidth, bmp.PixelHeight) };
        var at = CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        var c = new InlineUIContainer(img, at) { BaselineAlignment = BaselineAlignment.Bottom };
        CaretPosition = c.ElementEnd;
        Focus();
    }

    /// <summary>A copied image: its PNG (keeps transparency), else the bitmap, else image files copied in Explorer.</summary>
    private static List<BitmapSource> ClipboardImages(IDataObject data)
    {
        var list = new List<BitmapSource>();
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                list.Add(BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
                return list;
            }
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp) { list.Add(bmp); return list; }
        }
        catch (Exception ex) { Logging.Log.Here.Warn("Pasting an image: " + ex.Message); }
        return DroppedImages(data);
    }

    private static List<BitmapSource> DroppedImages(IDataObject data)
    {
        var list = new List<BitmapSource>();
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files) return list;
        foreach (var f in files.Where(f => ImageFiles.Contains(Path.GetExtension(f).ToLowerInvariant())))
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(f);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                list.Add(bi);
            }
            catch { }
        return list;
    }

    internal static IEnumerable<TextElement> Descendants(TextElement e)
    {
        yield return e;
        IEnumerable<TextElement> children = e switch
        {
            Paragraph p => p.Inlines,
            Span s => s.Inlines,
            List l => l.ListItems,
            ListItem li => li.Blocks,
            Section sec => sec.Blocks,
            Table t => t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks),
            _ => Enumerable.Empty<TextElement>(),
        };
        foreach (var c in children) foreach (var d in Descendants(c)) yield return d;
    }
}

/// <summary>A FlowDocument (what's in the box, or rich text pasted) as Markdown: headings, bold, italic, code, code
/// blocks, lists, links, tables; images where the callback says.</summary>
internal static class FlowMarkdown
{
    private static readonly string[] MonoNames = { "consolas", "courier", "cascadia", "mono", "lucida console", "menlo", "fira code", "source code", "jetbrains" };

    public static bool IsMono(FontFamily? f, FontFamily? body)
    {
        if (f == null) return false;
        if (body != null && f.Source == body.Source) return false;
        string s = f.Source.ToLowerInvariant();
        return MonoNames.Any(s.Contains);
    }

    /// <summary>The size most of the text is in (pasted rich text's body size).</summary>
    public static double BodySize(FlowDocument doc)
    {
        var sizes = doc.Blocks.SelectMany(RichInput.Descendants).OfType<Run>().GroupBy(r => Math.Round(r.FontSize, 1)).Select(g => (Size: g.Key, Chars: g.Sum(r => r.Text.Length)));
        return sizes.OrderByDescending(s => s.Chars).Select(s => s.Size).FirstOrDefault(doc.FontSize);
    }

    public static string From(FlowDocument doc, double body, FontFamily? bodyFont, Func<BitmapSource, string> image)
    {
        var sb = new StringBuilder();
        Blocks(doc.Blocks, sb, "", body, bodyFont, image);
        return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim('\n', '\r').TrimEnd();
    }

    private static void Blocks(IEnumerable<Block> blocks, StringBuilder sb, string indent, double body, FontFamily? bodyFont, Func<BitmapSource, string> image)
    {
        var code = new List<string>();
        void FlushCode()
        {
            if (code.Count == 0) return;
            // (a table drawn monospaced, as the conversation draws them: its lines as they are)
            if (code.All(c => c.TrimStart().StartsWith('|'))) foreach (var c in code) sb.Append(indent).Append(c).Append('\n');
            else sb.Append(indent).Append("```\n").Append(string.Join("\n", code)).Append('\n').Append(indent).Append("```\n");
            code.Clear();
        }
        foreach (var b in blocks)
        {
            if (b is Paragraph mono && MonoParagraph(mono, bodyFont))
            {
                // (lines of code run together, as an editor's are pasted; a code block or table drawn here is one paragraph of its own)
                string lines = new TextRange(mono.ContentStart, mono.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');
                bool table = lines.Split('\n').All(l => l.TrimStart().StartsWith('|'));
                bool whole = mono.Background != null || table;
                if (code.Count > 0 && (whole || code.All(c => c.TrimStart().StartsWith('|')) != table)) FlushCode();
                code.Add(lines);
                if (whole) FlushCode();
                continue;
            }
            FlushCode();
            switch (b)
            {
                case Paragraph p:
                    {
                        string text = Inlines(p.Inlines, body, bodyFont, image, out double size, out bool allBold);
                        if (text.Trim().Length == 0) { sb.Append('\n'); break; }
                        string plain = Regex.Replace(text, @"\*\*|__", "");
                        if (size >= body * 1.25 && !text.Contains('\n')) sb.Append(indent).Append("# ").Append(plain.Trim()).Append('\n');
                        else if (size >= body * 1.08 && allBold && !text.Contains('\n')) sb.Append(indent).Append("## ").Append(plain.Trim()).Append('\n');
                        else
                        {
                            // a bullet drawn as text (the conversation's lists, Word's)
                            var bullet = Regex.Match(text, "^[•·▪●◦o\\-]\\s+");
                            if (bullet.Success && (text[0] != 'o' || text.Length > 1 && text[1] == '\t')) text = "- " + text[bullet.Length..];
                            sb.Append(indent).Append(text.Replace("\n", "\n" + indent)).Append('\n');
                        }
                        break;
                    }
                case List l:
                    {
                        bool numbered = l.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
                        int n = l.StartIndex;
                        foreach (var li in l.ListItems)
                        {
                            var item = new StringBuilder();
                            Blocks(li.Blocks, item, indent + "   ", body, bodyFont, image);
                            string inner = item.ToString().TrimStart();
                            sb.Append(indent).Append(numbered ? $"{n++}. " : "- ").Append(inner.TrimEnd('\n')).Append('\n');
                        }
                        break;
                    }
                case Table t:
                    {
                        bool first = true;
                        foreach (var row in t.RowGroups.SelectMany(g => g.Rows))
                        {
                            var cells = row.Cells.Select(c => Regex.Replace(new TextRange(c.ContentStart, c.ContentEnd).Text, @"\s+", " ").Trim().Replace("|", "\\|")).ToList();
                            sb.Append(indent).Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                            if (first) sb.Append(indent).Append('|').Append(string.Concat(cells.Select(_ => " --- |"))).Append('\n');
                            first = false;
                        }
                        sb.Append('\n');
                        break;
                    }
                case Section s:
                    Blocks(s.Blocks, sb, indent, body, bodyFont, image);
                    break;
                case BlockUIContainer { Child: Image { Source: BitmapSource bmp } }:
                    if (image(bmp) is { Length: > 0 } tag) sb.Append(indent).Append(tag).Append('\n');
                    break;
            }
        }
        FlushCode();
    }

    /// <summary>A paragraph that's all code (a monospaced font).</summary>
    private static bool MonoParagraph(Paragraph p, FontFamily? bodyFont)
    {
        var runs = p.Inlines.SelectMany(RichInput.Descendants).OfType<Run>().Where(r => r.Text.Trim().Length > 0).ToList();
        return runs.Count > 0 && runs.All(r => IsMono(r.FontFamily, bodyFont));
    }

    private record struct Piece(string Text, bool Bold, bool Italic, bool Code, bool Raw);

    private static string Inlines(InlineCollection inlines, double body, FontFamily? bodyFont, Func<BitmapSource, string> image, out double maxSize, out bool allBold)
    {
        var pieces = new List<Piece>();
        double size = 0;
        bool bold = true;
        void Walk(IEnumerable<Inline> list)
        {
            foreach (var i in list)
                switch (i)
                {
                    case Run r when r.Text.Length > 0:
                        size = Math.Max(size, r.FontSize);
                        bool b = r.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
                        if (!b && r.Text.Trim().Length > 0) bold = false;
                        pieces.Add(new(r.Text, b, r.FontStyle == FontStyles.Italic || r.FontStyle == FontStyles.Oblique, IsMono(r.FontFamily, bodyFont), false));
                        break;
                    case LineBreak:
                        pieces.Add(new("\n", false, false, false, true));
                        break;
                    case Hyperlink h:
                        {
                            string text = new TextRange(h.ContentStart, h.ContentEnd).Text;
                            string? uri = h.NavigateUri?.ToString() ?? h.ToolTip as string;
                            bold = false;
                            pieces.Add(new(uri is { Length: > 0 } && uri != text ? $"[{text}]({uri})" : text, false, false, false, true));
                            break;
                        }
                    case Span s:
                        Walk(s.Inlines);
                        break;
                    case InlineUIContainer { Child: Image { Source: BitmapSource bmp } }:
                        pieces.Add(new(image(bmp), false, false, false, true));
                        break;
                }
        }
        Walk(inlines);
        maxSize = size;
        allBold = bold && pieces.Any(p => !p.Raw);
        // neighbours styled the same as one
        var merged = new List<Piece>();
        foreach (var p in pieces)
        {
            if (merged.Count > 0 && merged[^1] is var last && !last.Raw && !p.Raw && last.Bold == p.Bold && last.Italic == p.Italic && last.Code == p.Code)
                merged[^1] = last with { Text = last.Text + p.Text };
            else merged.Add(p);
        }
        var sb = new StringBuilder();
        foreach (var p in merged)
        {
            if (p.Raw || p.Text.Trim().Length == 0 || (!p.Bold && !p.Italic && !p.Code)) { sb.Append(p.Text); continue; }
            // (the marks around the words, the spaces outside them)
            string core = p.Text.Trim();
            int lead = p.Text.Length - p.Text.TrimStart().Length;
            sb.Append(p.Text[..lead]);
            if (p.Code && !core.Contains('`')) sb.Append('`').Append(core).Append('`');
            else sb.Append(p.Bold && p.Italic ? "***" : p.Bold ? "**" : p.Italic ? "*" : "").Append(core).Append(p.Bold && p.Italic ? "***" : p.Bold ? "**" : p.Italic ? "*" : "");
            sb.Append(p.Text[(lead + core.Length)..]);
        }
        return sb.ToString();
    }
}

/// <summary>HTML copied from a web page or another app (the clipboard's CF_HTML) as Markdown, and the images it holds
/// as data. An editor's code (a monospaced font, kept whitespace) becomes a code block of its plain text.</summary>
internal static class HtmlMarkdown
{
    public static (string Markdown, List<ImageData> Images) From(string cfHtml, string? plain)
    {
        var images = new List<ImageData>();
        int s = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase), e = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        string html = s >= 0 && e > s ? cfHtml[(s + 20)..e] : Regex.Match(cfHtml, @"<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase) is { Success: true } b ? b.Groups[1].Value : cfHtml;
        if (plain is { Length: > 0 } && !Regex.IsMatch(html, @"<(pre|p|h[1-6]|li|table)[\s>]", RegexOptions.IgnoreCase)
            && Regex.IsMatch(cfHtml, @"white-space:\s*pre|font-family:[^;""]*(consolas|monospace|courier|cascadia|menlo)", RegexOptions.IgnoreCase)
            && plain.Contains('\n'))
            return ("```\n" + plain.Replace("\r\n", "\n").TrimEnd() + "\n```", images);

        var sb = new StringBuilder();
        var lists = new Stack<(bool Ordered, int N)>();
        int pre = 0, skip = 0, linkAt = -1, row = 0, cells = 0;
        string? href = null;
        void Line() { if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n'); }
        void Para() { Line(); if (sb.Length > 1 && sb[^2] != '\n') sb.Append('\n'); }
        foreach (Match m in Regex.Matches(html, @"<!--.*?-->|<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>|([^<]+)", RegexOptions.Singleline))
        {
            if (m.Groups[4].Success)
            {
                if (skip > 0) continue;
                string t = WebUtility.HtmlDecode(m.Value);
                if (pre == 0)
                {
                    t = Regex.Replace(t, @"\s+", " ");
                    if (sb.Length == 0 || sb[^1] == '\n' || sb[^1] == ' ') t = t.TrimStart();
                }
                sb.Append(t);
                continue;
            }
            if (!m.Groups[2].Success) continue;
            bool close = m.Groups[1].Value == "/";
            string tag = m.Groups[2].Value.ToLowerInvariant(), attrs = m.Groups[3].Value;
            if (skip > 0 && tag is not ("style" or "script" or "head" or "title")) continue;
            switch (tag)
            {
                case "style" or "script" or "head" or "title": skip = Math.Max(0, skip + (close ? -1 : 1)); break;
                case "br": sb.Append('\n'); break;
                case "p" or "div" or "blockquote" or "section" or "article" or "header" or "footer" or "hr": if (pre == 0) Para(); break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Para();
                    if (!close) sb.Append(tag[1] <= '2' ? "# " : "## ");
                    break;
                case "b" or "strong": if (pre == 0) sb.Append("**"); break;
                case "i" or "em": if (pre == 0) sb.Append('*'); break;
                case "code": if (pre == 0) sb.Append('`'); break;
                case "pre":
                    if (!close) { Para(); sb.Append("```\n"); pre++; }
                    else { pre = Math.Max(0, pre - 1); Line(); sb.Append("```\n\n"); }
                    break;
                case "ul" or "ol":
                    if (!close) lists.Push((tag == "ol", 0)); else if (lists.Count > 0) lists.Pop();
                    Line();
                    break;
                case "li":
                    if (close) { Line(); break; }
                    Line();
                    var (ordered, n) = lists.Count > 0 ? lists.Pop() : (false, 0);
                    lists.Push((ordered, ++n));
                    sb.Append(new string(' ', 3 * Math.Max(0, lists.Count - 1))).Append(ordered ? n + ". " : "- ");
                    break;
                case "a":
                    if (!close) { href = Attr(attrs, "href"); linkAt = sb.Length; }
                    else if (href != null && linkAt >= 0 && linkAt <= sb.Length && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        string text = sb.ToString(linkAt, sb.Length - linkAt).Trim();
                        sb.Length = linkAt;
                        sb.Append(text.Length == 0 || text == href ? href : $"[{text}]({href})");
                        href = null;
                    }
                    break;
                case "table": Para(); row = 0; break;
                case "tr":
                    if (!close) { Line(); sb.Append('|'); cells = 0; }
                    else { if (row++ == 0 && cells > 0) sb.Append('\n').Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", cells))); Line(); }
                    break;
                case "td" or "th": if (close) { sb.Append(" |"); cells++; } else sb.Append(' '); break;
                case "img":
                    if (ImageData.FromDataUrl(Attr(attrs, "src")) is { } img) images.Add(img);
                    break;
            }
        }
        string md = Regex.Replace(sb.ToString(), @"[ \t]+\n", "\n");
        md = md.Replace("****", "");
        md = Regex.Replace(md, @"\n{3,}", "\n\n").Trim();
        return (md, images);
    }

    private static string? Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
    }
}
