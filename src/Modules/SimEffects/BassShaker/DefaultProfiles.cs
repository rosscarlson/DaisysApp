namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// The profile each game starts with: the effects its telemetry gives well, in the order they matter most for a rig
/// (crashes and impacts first, the engine last), with settings that suit it. Forza Horizon and Motorsport each get a
/// port of their own, so both can be set up in their games at once.
/// </summary>
public static class DefaultProfiles
{
    private static readonly EffectKind[] Order =
    {
        EffectKind.Crashes, EffectKind.Impacts, EffectKind.Curbs, EffectKind.RoadFeel, EffectKind.OffRoad,
        EffectKind.GearShift, EffectKind.WheelSlip, EffectKind.Abs, EffectKind.RevLimiter, EffectKind.Engine,
    };

    public static IEnumerable<ShakerProfile> All() => Games.All.Select(For);

    public static ShakerProfile For(Game game)
    {
        var p = new ShakerProfile
        {
            Name = Games.Title(game),
            Game = game,
            Port = game == Game.ForzaMotorsport ? 5301 : 8000,
            Programs = Games.Programs(game),
        };
        foreach (var kind in Order)
        {
            if (EffectKinds.Of(kind).NotIn.Contains(game)) continue;
            // Motorsport is on tracks: no off-road to speak of
            if (kind == EffectKind.OffRoad && game == Game.ForzaMotorsport) continue;
            p.Effects.Add(EffectKinds.Create(kind, game));
        }
        return p;
    }
}
