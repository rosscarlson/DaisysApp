namespace DaisysApp.Shared;

/// <summary>
/// The rule for a module a game switches on (Button2Key, Button2Key Remote, ForzaWheel, Mini Mirror): a game starting
/// switches it on if it was off, and the game ending switches it off only if the game switched it on, so a state set
/// by hand is never overridden (<see cref="OffEvenIfByHand"/>: Button2Key's choice, the game ending switches it off
/// however it was switched on). Written once here (since Core 0.55: each module had its own copy).
/// </summary>
public sealed class GameSwitch
{
    private readonly Func<bool> isOn;
    private readonly Func<bool, bool> set;

    /// <param name="isOn">Whether it's on now.</param>
    /// <param name="set">Switches it on or off for the game; false if it couldn't (it then doesn't count as the game's).</param>
    public GameSwitch(Func<bool> isOn, Func<bool, bool> set)
    {
        this.isOn = isOn;
        this.set = set;
    }

    public GameSwitch(Func<bool> isOn, Action<bool> set) : this(isOn, on => { set(on); return true; }) { }

    /// <summary>The game ending switches it off even if it was switched on by hand.</summary>
    public bool OffEvenIfByHand { get; init; }

    /// <summary>On because a game switched it on (and not touched since).</summary>
    public bool OnByGame { get; private set; }

    /// <summary>Whether a game is running, as last told.</summary>
    public bool GameRunning { get; private set; }

    /// <summary>A game started (true) or the last one ended (false). Returns what it did: true on, false off, null nothing.</summary>
    public bool? Game(bool running)
    {
        if (running == GameRunning) return null;
        GameRunning = running;
        if (running)
        {
            if (isOn()) return null;
            OnByGame = set(true);
            return OnByGame ? true : null;
        }
        bool was = OnByGame;
        OnByGame = false;
        if ((was || OffEvenIfByHand) && isOn()) { set(false); return false; }
        return null;
    }

    /// <summary>Switched on or off by hand (a button, a shortcut): the game no longer owns it.</summary>
    public void ByHand() => OnByGame = false;

    /// <summary>Forgets that a game was running (watching was switched off), so it counts as starting when it's seen again.</summary>
    public void Forget() => GameRunning = false;

    /// <summary>Remembered across a restart (a module that saves "on by game").</summary>
    public void Restore(bool onByGame) => OnByGame = onByGame;
}
