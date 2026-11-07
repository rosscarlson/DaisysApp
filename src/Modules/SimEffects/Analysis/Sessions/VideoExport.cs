using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DaisysApp.Applets.SimEffects.InputOverlay;
using Vortice.MediaFoundation;

namespace DaisysApp.Applets.SimEffects.Analysis.Sessions;

/// <summary>
/// (since 4.9.0) A race's video exported with its telemetry drawn on, as an onboard video: one block at the bottom in
/// the middle (Overlays' Dash dial: the rpm, gear and speed; and its Inputs trace, as Overlays → Inputs is set: the
/// pedals, the wheel and the speeds) and the lap and its time at the top right, lined up as the video is synced. Read
/// as <see cref="VideoFrames"/> reads it (at most 3840 across), drawn with WPF on a thread of its own, and written as
/// H.264 with its sound (AAC) by Media Foundation's sink writer. Before and after the race the video is as it was.
/// </summary>
internal static class VideoExport
{
    private const int MaxWidth = 3840;
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");

    /// <summary>Writes <paramref name="target"/>; <paramref name="progress"/> (0–1) from the export's thread.</summary>
    public static Task Run(string source, string target, SessionData data, RaceVideoLink link, Action<double> progress, CancellationToken cancel)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { Export(source, target, data, link, progress, cancel); done.SetResult(); }
            catch (OperationCanceledException) { done.SetCanceled(); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "Analysis-Export" };
        // (WPF draws the overlay: a thread of its own, single-threaded)
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void Export(string source, string target, SessionData data, RaceVideoLink link, Action<double> progress, CancellationToken cancel)
    {
        MediaFactory.MFStartup(true).CheckError();
        var (device, manager) = VideoFrames.Card();
        IMFSourceReader? video = null, sound = null;
        IMFSinkWriter? writer = null;
        bool finished = false;
        try
        {
            int w, h;
            double fps;
            try { video = VideoFrames.Open(source, manager, MaxWidth, out w, out h, out fps); }
            catch when (manager != null) { video = VideoFrames.Open(source, null, MaxWidth, out w, out h, out fps); }
            double duration = 0;
            try { duration = Convert.ToUInt64(video.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration).Value) / 1e7; } catch { }
            sound = OpenSound(source);

            if (File.Exists(target)) File.Delete(target);
            var made = Writer(target, w, h, fps, sound, hardware: true);
            if (made.Writer == null) made = Writer(target, w, h, fps, sound, hardware: false);
            (writer, int videoOut, int soundOut) = made;
            if (writer == null) throw new InvalidOperationException(T("The video couldn't be written"));
            if (soundOut < 0) { sound?.Dispose(); sound = null; }

            var overlay = new Overlay(data, link, w, h);
            var frame = new byte[w * h * 4];
            long nextVideo = 0, nextSound = sound == null ? long.MaxValue : 0;
            bool videoEnd = false, soundEnd = sound == null;
            while (!videoEnd || !soundEnd)
            {
                cancel.ThrowIfCancellationRequested();
                if (!videoEnd && (soundEnd || nextVideo <= nextSound))
                {
                    var sample = video.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
                    if ((flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0) { sample?.Dispose(); videoEnd = true; continue; }
                    if (sample == null) continue;
                    using (sample)
                    {
                        long length = sample.SampleDuration > 0 ? sample.SampleDuration : (long)(1e7 / fps);
                        VideoFrames.Copy(sample, w, h, frame);
                        overlay.Draw(frame, time / 1e7);
                        using var output = Sample(frame, time, length);
                        writer.WriteSample(videoOut, output);
                        nextVideo = time + length;
                    }
                    if (duration > 0) progress(Math.Clamp(time / 1e7 / duration, 0, 1));
                }
                else
                {
                    var sample = sound!.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out long time);
                    if ((flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0) { sample?.Dispose(); soundEnd = true; continue; }
                    if (sample == null) continue;
                    using (sample)
                    {
                        writer.WriteSample(soundOut, sample);
                        nextSound = time + Math.Max(1, sample.SampleDuration);
                    }
                }
            }
            writer.Finalize();
            finished = true;
            progress(1);
        }
        finally
        {
            writer?.Dispose();
            video?.Dispose();
            sound?.Dispose();
            manager?.Dispose();
            device?.Dispose();
            // (stopped, or failed: no half-written file left)
            if (!finished) try { File.Delete(target); } catch { }
        }
    }

    /// <summary>The file's sound as 16-bit stereo at 48 kHz (what the AAC encoder takes), or null without any.</summary>
    private static IMFSourceReader? OpenSound(string source)
    {
        IMFSourceReader? reader = null;
        try
        {
            reader = MediaFactory.MFCreateSourceReaderFromURL(source, null);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
            using var pcm = MediaFactory.MFCreateMediaType();
            pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
            pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
            pcm.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
            pcm.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192000u);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, pcm);
            return reader;
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Info($"Analysis: the race video exported without sound ({ex.Message})");
            reader?.Dispose();
            return null;
        }
    }

    /// <summary>The MP4 written: H.264 from BGRA frames, and AAC from the sound (-1: none). Null if it couldn't start.</summary>
    private static (IMFSinkWriter? Writer, int Video, int Sound) Writer(string target, int w, int h, double fps, IMFSourceReader? sound, bool hardware)
    {
        IMFSinkWriter? writer = null;
        try
        {
            using (var attrs = MediaFactory.MFCreateAttributes(1))
            {
                if (hardware) attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
                writer = MediaFactory.MFCreateSinkWriterFromURL(target, null, attrs);
            }
            ulong size = ((ulong)w << 32) | (uint)h, rate = ((ulong)(uint)Math.Round(fps * 1000) << 32) | 1000u, square = (1UL << 32) | 1u;
            int videoOut;
            using (var output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                output.Set(MediaTypeAttributeKeys.Subtype, H264);
                // (about 12 Mbit/s for 1080p at 60)
                output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)Math.Clamp(w * (double)h * fps * 0.1, 2e6, 40e6));
                output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
                output.Set(MediaTypeAttributeKeys.FrameSize, size);
                output.Set(MediaTypeAttributeKeys.FrameRate, rate);
                output.Set(MediaTypeAttributeKeys.PixelAspectRatio, square);
                videoOut = writer.AddStream(output);
            }
            using (var input = MediaFactory.MFCreateMediaType())
            {
                input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
                input.Set(MediaTypeAttributeKeys.FrameSize, size);
                input.Set(MediaTypeAttributeKeys.FrameRate, rate);
                input.Set(MediaTypeAttributeKeys.PixelAspectRatio, square);
                input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(w * 4)); // top row first
                writer.SetInputMediaType(videoOut, input, null);
            }
            int soundOut = -1;
            if (sound != null)
            {
                try
                {
                    using var aac = MediaFactory.MFCreateMediaType();
                    aac.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    aac.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                    aac.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                    aac.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
                    aac.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
                    aac.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u); // 192 kbit/s
                    int s = writer.AddStream(aac);
                    using var pcm = sound.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream);
                    writer.SetInputMediaType(s, pcm, null);
                    soundOut = s;
                }
                catch (Exception ex) { Logging.Log.Here.Info($"Analysis: the race video exported without sound ({ex.Message})"); }
            }
            writer.BeginWriting();
            return (writer, videoOut, soundOut);
        }
        catch (Exception ex)
        {
            Logging.Log.Here.Info($"Analysis: the race video's export couldn't start{(hardware ? " on the graphics card" : "")}: {ex.Message}");
            writer?.Dispose();
            return (null, -1, -1);
        }
    }

    private static IMFSample Sample(byte[] frame, long time, long length)
    {
        var buffer = MediaFactory.MFCreateMemoryBuffer(frame.Length);
        buffer.Lock(out IntPtr p, out _, out _);
        System.Runtime.InteropServices.Marshal.Copy(frame, 0, p, frame.Length);
        buffer.Unlock();
        buffer.CurrentLength = frame.Length;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = Math.Max(0, time);
        sample.SampleDuration = length;
        return sample;
    }

    /// <summary>What's drawn on each frame: the block at the bottom, the lap at the top right.</summary>
    private sealed class Overlay
    {
        /// <summary>The block's size before it's scaled to the video (as the widgets are on screen).</summary>
        private const double BlockW = 720, BlockH = 170, LapW = 250, LapH = 74;
        private static readonly Typeface Race = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.SemiCondensed);
        private static readonly Typeface RaceLight = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.SemiCondensed);
        private static readonly Brush Back = Ui.Brush(Color.FromArgb(0x99, 0x08, 0x0A, 0x0E));
        private static readonly Brush Dim = Ui.Brush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

        private readonly SessionData data;
        private readonly RaceVideoLink link;
        private readonly OverlaySettings settings;
        private readonly Grid block;
        private readonly DashView dash;
        private readonly OverlayWindow.Trace trace;
        private readonly RenderTargetBitmap blockBitmap, lapBitmap;
        private readonly byte[] blockPixels, lapPixels;
        private readonly int videoW, blockX, blockY, lapX, lapY;
        private readonly double scale;
        private readonly Queue<(DateTime At, double Speed)> speeds = new();
        /// <summary>The next sample of the log to give the trace.</summary>
        private int fed = -1;

        public Overlay(SessionData data, RaceVideoLink link, int w, int h)
        {
            this.data = data;
            this.link = link;
            // (Overlays' own settings, as Inputs is set; its background is the block's here)
            settings = OverlaySettings.Load();
            settings.Background = 0;
            videoW = w;
            scale = h / 1080.0 * 1.25;
            dash = new DashView(settings, () => 0) { Has = true, Width = BlockH, Height = BlockH };
            trace = new OverlayWindow.Trace(settings) { Margin = new Thickness(BlockH, 0, 0, 0) };
            block = new Grid { Width = BlockW, Height = BlockH, Background = Back, Children = { dash, trace } };
            dash.HorizontalAlignment = HorizontalAlignment.Left;
            block.Measure(new Size(BlockW, BlockH));
            block.Arrange(new Rect(0, 0, BlockW, BlockH));
            block.Clip = new RectangleGeometry(new Rect(0, 0, BlockW, BlockH), 10, 10);
            blockBitmap = new RenderTargetBitmap(Px(BlockW), Px(BlockH), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            lapBitmap = new RenderTargetBitmap(Px(LapW), Px(LapH), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            blockPixels = new byte[blockBitmap.PixelWidth * blockBitmap.PixelHeight * 4];
            lapPixels = new byte[lapBitmap.PixelWidth * lapBitmap.PixelHeight * 4];
            double margin = 24 * h / 1080.0;
            blockX = (w - blockBitmap.PixelWidth) / 2;
            blockY = h - blockBitmap.PixelHeight - (int)margin;
            lapX = w - lapBitmap.PixelWidth - (int)margin;
            lapY = (int)margin;
        }

        private int Px(double size) => (int)Math.Ceiling(size * scale);

        /// <summary>Draws the race's moment at <paramref name="at"/> seconds into the video on <paramref name="frame"/>.</summary>
        public void Draw(byte[] frame, double at)
        {
            double s = (at - link.VideoAt(data.Started, 0)) / SessionData.Dt;
            int i = (int)Math.Floor(s);
            if (i < 0 || i >= data.Count) return;
            Feed(i);

            var f = dash.Frame;
            f.Rpm = Value(data.Rpm[i]);
            f.MaxRpm = Value(data.MaxRpm[i]);
            f.SpeedMs = Value(data.Speed[i]);
            f.GearNo = double.IsNaN(data.Gear[i]) ? 0 : (int)data.Gear[i];
            dash.Has = !double.IsNaN(data.Rpm[i]) || !double.IsNaN(data.Speed[i]);
            trace.Speeds = speeds.Count == 0 ? null : (speeds.Last().Speed, speeds.Max(x => x.Speed), speeds.Min(x => x.Speed));
            dash.InvalidateVisual();
            trace.InvalidateVisual();
            block.UpdateLayout();
            blockBitmap.Clear();
            blockBitmap.Render(block);
            blockBitmap.CopyPixels(blockPixels, blockBitmap.PixelWidth * 4, 0);
            Blend(frame, blockPixels, blockBitmap.PixelWidth, blockBitmap.PixelHeight, blockX, blockY);

            double lap = data.LapNo[i], lapTime = data.LapTime[i];
            if (double.IsNaN(lap) || lap < 0 || double.IsNaN(lapTime)) return;
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRoundedRectangle(Back, null, new Rect(0, 0, LapW, LapH), 10, 10);
                var title = Text(F("Lap {0}", (int)lap + 1).ToUpper(CultureInfo.CurrentCulture), RaceLight, 15, Dim);
                dc.DrawText(title, new Point(LapW - 14 - title.Width, 6));
                var time = Text(Lap.LapClock(lapTime), Race, 40, Brushes.White);
                dc.DrawText(time, new Point(LapW - 14 - time.Width, LapH - 6 - time.Height));
            }
            lapBitmap.Clear();
            lapBitmap.Render(v);
            lapBitmap.CopyPixels(lapPixels, lapBitmap.PixelWidth * 4, 0);
            Blend(frame, lapPixels, lapBitmap.PixelWidth, lapBitmap.PixelHeight, lapX, lapY);
        }

        private static float Value(double v) => double.IsNaN(v) ? 0 : (float)v;

        private static FormattedText Text(string s, Typeface face, double size, Brush brush) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, 1);

        /// <summary>The log's samples up to <paramref name="i"/> into the trace and the speeds, as Overlays' own get them live.</summary>
        private void Feed(int i)
        {
            if (fed < 0 || i < fed - 1 || i - fed > SessionData.Hz * 60) fed = Math.Max(0, i - SessionData.Hz * 60);
            bool kmh = settings.Unit == SpeedUnit.Kmh;
            for (; fed <= i; fed++)
            {
                var t = data.Started.AddSeconds(fed * SessionData.Dt);
                trace.Add(t, data.Steer[fed], data.Throttle[fed], data.Brake[fed], data.Clutch[fed], data.Handbrake[fed]);
                double v = data.Speed[fed];
                if (double.IsNaN(v)) { speeds.Clear(); continue; }
                speeds.Enqueue((t, v * (kmh ? 3.6 : 2.23694)));
                var cut = t - TimeSpan.FromSeconds(Math.Clamp(settings.WindowSeconds, 1, 60));
                while (speeds.Count > 1 && speeds.Peek().At < cut) speeds.Dequeue();
            }
        }

        /// <summary>A premultiplied BGRA picture laid over the frame (BGRA) at <paramref name="x"/>, <paramref name="y"/>.</summary>
        private unsafe void Blend(byte[] frame, byte[] over, int ow, int oh, int x, int y)
        {
            int frameH = frame.Length / (videoW * 4);
            fixed (byte* dst = frame, src = over)
                for (int r = 0; r < oh; r++)
                {
                    int fy = y + r;
                    if (fy < 0 || fy >= frameH) continue;
                    for (int c = 0; c < ow; c++)
                    {
                        int fx = x + c;
                        if (fx < 0 || fx >= videoW) continue;
                        byte* s = src + (r * ow + c) * 4;
                        int a = s[3];
                        if (a == 0) continue;
                        byte* d = dst + ((long)fy * videoW + fx) * 4;
                        int keep = 255 - a;
                        d[0] = (byte)(s[0] + d[0] * keep / 255);
                        d[1] = (byte)(s[1] + d[1] * keep / 255);
                        d[2] = (byte)(s[2] + d[2] * keep / 255);
                    }
                }
        }
    }
}
