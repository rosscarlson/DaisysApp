namespace DaisysApp.Applets.Gaming.WheelEmu;

/// <summary>
/// "Set": waits for the next control used on any controller, as EmuWheel's Configurator does. A button pressed, a hat
/// pushed, or an axis moved most of the way (a pedal pressed, the wheel turned, a shifter or handbrake pulled).
/// </summary>
internal static class Capture
{
    /// <summary>What was used, and for an axis whether it rested near its top (a pedal that reads full when released).</summary>
    public sealed record Result(InputRef Input, bool RestsHigh);

    /// <summary>
    /// The next control used. Controls in <paramref name="ignore"/> (bound to something else) are passed over, so a wheel
    /// that's touched while binding a button doesn't take it; with <paramref name="axesOnly"/> (steering, the combined
    /// pedals) only an axis counts. Where everything rests is taken from good readings first, and an axis counts only
    /// once it moves most of the way from there (an axis that never moves, as some wheels have, never does).
    /// </summary>
    public static Task<Result?> NextAsync(IntPtr window, TimeSpan timeout, IReadOnlyCollection<InputRef> ignore, bool axesOnly, CancellationToken cancel) => Task.Run(() =>
    {
        var open = new List<(ControllerInfo Info, GameControllers.Reader Reader, ControllerState Base, ControllerState Now)>();
        bool Ignored(Guid device, InputRef.InputKind kind, int index, int direction) => ignore.Any(r =>
            r.Device == device && r.Kind == kind && r.Index == index && (kind != InputRef.InputKind.Pov || r.Direction == direction));
        try
        {
            foreach (var c in GameControllers.List())
                if (GameControllers.Open(c.Instance, window) is { } r)
                    open.Add((c, r, new ControllerState(), new ControllerState()));
            // where everything rests: three good readings in a row (some devices answer nothing at first); a device
            // that never gives one is left out rather than compared with nothing
            var good = new int[open.Count];
            for (int tries = 0; tries < 25 && good.Any(g => g < 3); tries++)
            {
                for (int k = 0; k < open.Count; k++)
                    if (good[k] < 3) good[k] = open[k].Reader.Read(open[k].Base) ? good[k] + 1 : 0;
                Thread.Sleep(20);
            }
            for (int k = open.Count - 1; k >= 0; k--)
                if (good[k] < 3) { open[k].Reader.Dispose(); open.RemoveAt(k); }

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
                    if (!axesOnly)
                    {
                        for (int i = 0; i < now.Buttons.Length; i++)
                            if (now.Buttons[i] && !b.Buttons[i] && !Ignored(info.Instance, InputRef.InputKind.Button, i, 0))
                                return new Result(Ref(InputRef.InputKind.Button, i, 0), false);
                        for (int i = 0; i < 4; i++)
                            if (now.Povs[i] >= 0 && b.Povs[i] < 0)
                            {
                                int dir = (int)Math.Round(now.Povs[i] / 9000.0) % 4;
                                if (!Ignored(info.Instance, InputRef.InputKind.Pov, i, dir)) return new Result(Ref(InputRef.InputKind.Pov, i, dir), false);
                            }
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        if (double.IsNaN(now.Axes[i]) || double.IsNaN(b.Axes[i]) || Ignored(info.Instance, InputRef.InputKind.Axis, i, 0)) continue;
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
