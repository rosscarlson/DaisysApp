namespace DaisysApp.Applets.SimEffects.BassShaker;

/// <summary>
/// A kind of transducer: the range of frequencies it shakes well, which every effect sent to it is kept within (below
/// it the amplifier works for nothing, above it the shaker hums instead of shaking), and the one it shakes hardest at
/// (its test tone). Ranges are from the makers' figures, narrowed to where each is strong; a device's own Low / High
/// can be changed after.
/// </summary>
public sealed record TransducerType(string Id, string Name, int MinHz, int MaxHz, int PeakHz)
{
    public static readonly TransducerType[] All =
    {
        new("buttkicker-gamer", "ButtKicker Gamer 2 / Gamer Pro", 10, 80, 35),
        new("buttkicker-advance", "ButtKicker Advance / Simrig", 8, 100, 30),
        new("buttkicker-lfe", "ButtKicker LFE", 8, 100, 30),
        new("buttkicker-mini", "ButtKicker Mini LFE", 12, 100, 40),
        new("dayton-bst1", "Dayton Audio BST-1 / BST-1 Pro", 20, 80, 40),
        new("dayton-bst2", "Dayton Audio BST-2", 20, 100, 40),
        new("dayton-bst300", "Dayton Audio BST-300EX", 20, 80, 40),
        new("dayton-tt25", "Dayton Audio TT25 Puck", 30, 120, 50),
        new("dayton-tt-mini", "Dayton Audio Mini Puck (TT10 / TT20)", 40, 150, 60),
        new("aura-pro", "AuraSound AST-2B-4 Pro Bass Shaker", 15, 80, 40),
        new("aura-bass", "AuraSound Bass Shaker", 20, 80, 40),
        new("clark-tst", "Clark Synthesis TST239 / TST429", 10, 120, 35),
        new("sub", "Subwoofer", 20, 120, 45),
        new("generic", "Other bass shaker", 20, 100, 40),
    };

    public static TransducerType Of(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[^1];
}
