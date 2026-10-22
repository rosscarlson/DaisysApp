using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DaisysApp.Logging;

namespace DaisysApp.Applets.Gaming.IRacing;

/// <summary>
/// Export: the page as it is now, as one web page to share (nothing live, no files beside it). The top, every card and
/// graph are pictures of themselves (drawn at twice the size, so they're sharp), in the page's colours; the filters are
/// a line of text; the tables are HTML tables with every row, one after another, each folded open or shut.
/// </summary>
internal sealed partial class IRacingView
{
    private void ExportPage()
    {
        if (!built || data.Races.Count == 0 && data.Career.Count == 0) { syncStatus.Text = T("Nothing to export yet."); return; }
        string who = data.Name.Length > 0 ? data.Name : "iRacing";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("Export"),
            FileName = Safe($"iRacing - {who} - {DateTime.Now:yyyy-MM-dd}") + ".html",
            Filter = T("Web page") + " (*.html)|*.html",
            DefaultExt = ".html",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            UpdateLayout();
            File.WriteAllText(dialog.FileName, PageHtml(who), new UTF8Encoding(false));
            syncStatus.Text = F("Exported to {0}", Path.GetFileName(dialog.FileName));
            try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            Log.Here.Error("iRacing: export failed", ex);
            syncStatus.Text = F("It couldn't be exported: {0}", ex.Message);
        }
    }

    private static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private string PageHtml(string who)
    {
        string window = Css("WindowBrush", "#202020"), card = Css("CardBrush", "#2B2B2B"), border = Css("CardBorderBrush", "#383838"),
            text = Css("TextBrush", "#FFFFFF"), secondary = Css("TextSecondaryBrush", "#A0A0A0");
        double width = Math.Ceiling(Math.Max(dashboard.ActualWidth, 600));
        var h = new StringBuilder();
        h.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>{E("iRacing · " + who)}</title>\n<style>\n");
        h.Append($"body{{background:{window};color:{text};font-family:'Segoe UI',system-ui,sans-serif;font-size:14px;margin:0;padding:20px}}\n");
        h.Append($".page{{max-width:{width.ToString(CultureInfo.InvariantCulture)}px;margin:0 auto}}\n");
        h.Append("img{display:block;width:100%;height:auto;margin:0 0 12px}\n");
        h.Append($".card{{background:{card};border:1px solid {border};border-radius:8px;padding:16px;margin:0 0 12px}}\n");
        h.Append(".card img{margin:0}\n");
        h.Append($".sec{{color:{secondary}}}\nh2{{font-size:14px;margin:0 0 6px}}\n");
        h.Append("details{margin:8px 0 0}summary{cursor:pointer;font-weight:600;padding:6px 0}\n");
        h.Append($".scroll{{max-height:560px;overflow:auto;border:1px solid {border};border-radius:6px}}\n");
        h.Append("table{border-collapse:collapse;width:100%;font-size:12.5px}\n");
        h.Append($"th,td{{padding:4px 8px;border-bottom:1px solid {border};text-align:left;white-space:nowrap}}\n");
        h.Append($"th{{position:sticky;top:0;background:{card}}}td.n{{text-align:right}}\n");
        h.Append("footer{margin:16px 0 0;font-size:12px;text-align:center}\n</style></head><body><div class=\"page\">\n");

        // the top (without its buttons), and the filters
        if (Png(headerLeft) is { } top) h.Append($"<div class=\"card\"><img alt=\"{E(who)}\" src=\"data:image/png;base64,{top}\"></div>\n");
        h.Append($"<div class=\"card sec\">{E(FiltersText())}</div>\n");

        // every card and pair of cards, as it looks
        foreach (var child in dashboard.Children.OfType<FrameworkElement>())
        {
            if (child == tablesCard || child.Visibility != Visibility.Visible) continue;
            if (Png(child) is { } png) h.Append($"<img alt=\"\" src=\"data:image/png;base64,{png}\">\n");
        }

        // the tables, every row
        h.Append("<div class=\"card\"><h2>").Append(E(T("Tables"))).Append("</h2>\n");
        foreach (var (title, grid) in new[]
        {
            (T("Tracks"), tracksGrid), (T("Layouts"), layoutsGrid), (T("Cars"), carsGrid), (T("Car classes"), classesGrid), (T("Series"), seriesGrid),
            (T("Seasons"), seasonsGrid), (T("Years"), yearsGrid), (T("Every race"), racesGrid),
            (T("iRacing's totals") + " · " + T("Category"), careerGrid), (T("iRacing's totals") + " · " + T("Year"), yearlyGrid),
        })
            Table(h, title, grid);
        h.Append("</div>\n");
        h.Append($"<footer class=\"sec\">{E(F("Exported from Daisy's App on {0:f}", DateTime.Now))}</footer>\n</div></body></html>\n");
        return h.ToString();
    }

    /// <summary>The filters chosen, as words.</summary>
    private string FiltersText()
    {
        string Picked(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content as string ?? "";
        var parts = new List<string>
        {
            T("Category") + ": " + Picked(category), T("Track") + ": " + Picked(trackFilter), T("Car") + ": " + Picked(carFilter),
            T("Time") + ": " + Picked(range), T("Sessions") + ": " + Picked(session),
        };
        if (official.IsChecked == true) parts.Add(T("Official races only"));
        if (shown.Text.Length > 0) parts.Add(shown.Text);
        return string.Join(" · ", parts);
    }

    private static void Table(StringBuilder h, string title, DataGrid grid)
    {
        var columns = grid.Columns.OfType<DataGridTextColumn>().Select(c => (Header: c.Header?.ToString() ?? "", Binding: c.Binding as Binding)).ToList();
        var rows = grid.Items.Cast<object>().ToList();
        if (rows.Count == 0) return;
        h.Append($"<details><summary>{E(title)} <span class=\"sec\">({rows.Count.ToString("N0", CultureInfo.CurrentCulture)})</span></summary><div class=\"scroll\"><table><thead><tr>");
        foreach (var c in columns) h.Append("<th>").Append(E(c.Header)).Append("</th>");
        h.Append("</tr></thead><tbody>\n");
        var props = new Dictionary<(Type, string), System.Reflection.PropertyInfo?>();
        foreach (var row in rows)
        {
            h.Append("<tr>");
            foreach (var c in columns)
            {
                object? v = null;
                if (c.Binding?.Path?.Path is { Length: > 0 } path)
                {
                    var key = (row.GetType(), path);
                    if (!props.TryGetValue(key, out var prop)) props[key] = prop = row.GetType().GetProperty(path);
                    v = prop?.GetValue(row);
                }
                string s = v == null ? "" : c.Binding?.StringFormat is { Length: > 0 } f ? string.Format(CultureInfo.CurrentCulture, f, v) : Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
                bool number = v is int or long or double or float or decimal;
                h.Append(number ? "<td class=\"n\">" : "<td>").Append(E(s)).Append("</td>");
            }
            h.Append("</tr>\n");
        }
        h.Append("</tbody></table></div></details>\n");
    }

    /// <summary>An element as it's drawn now, as a PNG (base64), at twice its size; null if it isn't showing.</summary>
    private static string? Png(FrameworkElement e)
    {
        var size = new Size(e.ActualWidth, e.ActualHeight);
        if (size.Width < 1 || size.Height < 1) return null;
        const double scale = 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // (through a brush, from the element's own place in its parent: a brush draws it there)
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
            dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(e), size) },
                null, new Rect(size));
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        png.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>A theme brush as a CSS colour.</summary>
    private string Css(string key, string fallback) =>
        TryFindResource(key) is SolidColorBrush { Color: var c } ? (c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})") : fallback;

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
