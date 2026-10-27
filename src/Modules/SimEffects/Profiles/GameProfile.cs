namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>A game's name, port and programs: the Shaker's effects profile (<see cref="BassShaker.ShakerProfile"/>)
/// keeps them for its game's default effects; the module's profiles are <see cref="SimProfile"/>s.</summary>
public class GameProfile
{
    public string Name { get; set; } = "";
    public Game Game { get; set; }
    public int Port { get; set; } = 8000;
    public List<string> Programs { get; set; } = new();
}

public static class GameProfiles
{
    /// <summary>A game's profile as it starts: its name, programs, and a port of its own for each Forza.</summary>
    public static T For<T>(Game game) where T : GameProfile, new() => new()
    {
        Name = Games.Title(game),
        Game = game,
        Port = game == Game.ForzaMotorsport ? 5301 : 8000,
        Programs = Games.Programs(game),
    };
}
