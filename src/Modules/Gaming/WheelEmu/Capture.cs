namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// "Set": waits for the next control used on any controller, as EmuWheel's Configurator does. A button pressed, a hat
/// pushed, or an axis moved most of the way (a pedal pressed, the wheel turned, a shifter or handbrake pulled).
/// </summary>
internal static class Capture
{
    /// <summary>What was used, and for an axis whether it rested near its top (a pedal that reads full when released).</summary>
    public sealed record Result(InputRef Input, bool RestsHigh);

    public static Task<Result?> NextAsync(IntPtr window, TimeSpan timeout, CancellationToken cancel) => Task.Run(() =>
    {
        var open = new List<(ControllerInfo Info, GameControllers.Reader Reader, ControllerState Base, ControllerState Now)>();
        try
        {
            foreach (var c in GameControllers.List())
                if (GameControllers.Open(c.Instance, window) is { } r)
                    open.Add((c, r, new ControllerState(), new ControllerState()));
            // where everything rests: read a few times, as some devices report nothing on the first poll
            for (int i = 0; i < 5; i++)
            {
                foreach (var o in open) o.Reader.Read(o.Base);
                Thread.Sleep(20);
            }
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end && !cancel.IsCancellationRequested)
            {
                foreach (var (info, reader, b, now) in open)
                {
                    if (!reader.Read(now)) continue;
                    InputRef Ref(InputRef.InputKind kind, int index, int direction) => new()
                    {
                        Device = info.Instance, Product = info.Product, DeviceName = info.Name, Kind = kind, Index = index, Direction = direction,
                    };
                    for (int i = 0; i < now.Buttons.Length; i++)
                        if (now.Buttons[i] && !b.Buttons[i]) return new Result(Ref(InputRef.InputKind.Button, i, 0), false);
                    for (int i = 0; i < 4; i++)
                        if (now.Povs[i] >= 0 && b.Povs[i] < 0)
                        {
                            int dir = (int)Math.Round(now.Povs[i] / 9000.0) % 4;
                            return new Result(Ref(InputRef.InputKind.Pov, i, dir), false);
                        }
                    for (int i = 0; i < 8; i++)
                    {
                        if (double.IsNaN(now.Axes[i]) || double.IsNaN(b.Axes[i])) continue;
                        double moved = now.Axes[i] - b.Axes[i];
                        if (Math.Abs(moved) > 0.4) return new Result(Ref(InputRef.InputKind.Axis, i, Math.Sign(moved)), b.Axes[i] > 0.6);
                    }
                }
                Thread.Sleep(10);
            }
            return null;
        }
        finally
        {
            foreach (var o in open) o.Reader.Dispose();
        }
    }, cancel);
}
