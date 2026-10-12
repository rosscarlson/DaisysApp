using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// What the virtual webcam shows while it's muted: black, with a large red outline in the middle and the message
/// centred in it. The camera keeps running, so Discord and the rest don't think it's gone; they just get this picture.
/// </summary>
internal static class MutedFrame
{
    public static readonly string[] Lines = [T("VIDEO MUTED"), "FUCK OFF"];

    /// <summary>The picture, <paramref name="w"/>×<paramref name="h"/>, as top-down BGR (softcam's format).</summary>
    public static byte[] Render(int w, int h)
    {
        byte[]? bgr = null;
        Exception? error = null;
        // drawn with WPF, which wants an STA thread (the camera's own thread isn't one)
        var thread = new Thread(() =>
        {
            try { bgr = Draw(w, h); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return bgr ?? throw error ?? new InvalidOperationException("The muted picture wasn't drawn.");
    }

    private static byte[] Draw(int w, int h)
    {
        var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        red.Freeze();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            // the outline: most of the picture, in the middle
            double bw = w * 0.72, bh = h * 0.58, stroke = Math.Max(4, Math.Min(w, h) / 60.0);
            var box = new Rect((w - bw) / 2, (h - bh) / 2, bw, bh);
            var pen = new Pen(red, stroke);
            pen.Freeze();
            dc.DrawRectangle(null, pen, box);

            // the two lines, as big as fit the box (the first larger), centred together
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
            FormattedText Text(string s, double size, Brush brush) =>
                new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, Math.Max(4, size), brush, 1.0);
            double room = bw - stroke * 6;
            double size1 = bh * 0.24, size2 = bh * 0.17;
            var first = Text(Lines[0], size1, Brushes.White);
            var second = Text(Lines[1], size2, red);
            // too wide (a portrait picture): both smaller by the same amount, so the first stays the bigger
            double k = Math.Min(1, room / Math.Max(first.WidthIncludingTrailingWhitespace, second.WidthIncludingTrailingWhitespace));
            if (k < 1)
            {
                first = Text(Lines[0], size1 * k, Brushes.White);
                second = Text(Lines[1], size2 * k, red);
            }
            double gap = bh * 0.04, total = first.Height + gap + second.Height, top = box.Top + (bh - total) / 2;
            dc.DrawText(first, new Point(box.Left + (bw - first.WidthIncludingTrailingWhitespace) / 2, top));
            dc.DrawText(second, new Point(box.Left + (bw - second.WidthIncludingTrailingWhitespace) / 2, top + first.Height + gap));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var bgra = new byte[w * h * 4];
        bmp.CopyPixels(bgra, w * 4, 0);
        var bgr = new byte[w * h * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            bgr[j] = bgra[i];
            bgr[j + 1] = bgra[i + 1];
            bgr[j + 2] = bgra[i + 2];
        }
        return bgr;
    }
}
