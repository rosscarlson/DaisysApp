namespace DaisysApp.Applets.SimEffects.Profiles;

/// <summary>A tool's profile for a game: its name, the game (and its port, for Forza), and the programs that pick it
/// with Game Switching on. The Shaker's adds its effects (<see cref="BassShaker.ShakerProfile"/>).</summary>
public class GameProfile
{
    public string Name { get; set; } = "";
    public Game Game { get; set; }
    public int Port { get; set; } = 8000;
    /// <summary>Program names (ForzaHorizon5.exe…): with Game Switching on, one running picks this profile.</summary>
    public List<string> Programs { get; set; } = new();
}

/// <summary>What a tool with profiles keeps (in its own settings file, under these names).</summary>
public interface IProfileSettings<T> where T : GameProfile
{
    List<T> Profiles { get; }
    /// <summary>The profile in use, by name.</summary>
    string? Active { get; set; }
    /// <summary>A profile's program running switches to that profile.</summary>
    bool GameSwitching { get; set; }
    /// <summary>With Game Switching: the tool switches on while a profile's program runs (and off when it closes, if
    /// it switched it on).</summary>
    bool SwitchOnWithGame { get; set; }
}

public static class GameProfiles
{
    /// <summary>The profile in use: the one named, else the first.</summary>
    public static T? Active<T>(this IProfileSettings<T> s) where T : GameProfile =>
        s.Profiles.FirstOrDefault(p => p.Name == s.Active) ?? s.Profiles.FirstOrDefault();

    /// <summary>A game's profile as it starts: its name, programs, and a port of its own for each Forza (so both can
    /// be set up in their games at once).</summary>
    public static T For<T>(Game game) where T : GameProfile, new() => new()
    {
        Name = Games.Title(game),
        Game = game,
        Port = game == Game.ForzaMotorsport ? 5301 : 8000,
        Programs = Games.Programs(game),
    };

    /// <summary>A profile for each game.</summary>
    public static List<T> Defaults<T>() where T : GameProfile, new() => Games.All.Select(For<T>).ToList();
}
