namespace DaisysApp.Applets.AudioTools.Tests.Eq;

/// <summary>Per-speaker EQ: peaking filters kept in Voicemeeter's bus EQ, or in an Equalizer APO config file.</summary>
public interface IEqControl : IDisposable
{
    /// <summary>Where the EQ is kept, for the wizard, e.g. "Voicemeeter bus A1 EQ (cells 1–4 of each channel)".</summary>
    string Description { get; }

    /// <summary>What the filters can do here, with the user's choice of the most boost allowed.</summary>
    EqLimits Limits(double maxBoostDb);

    bool CanControl(int channel);

    /// <summary>The filters on a speaker channel now (the ones this app manages).</summary>
    IReadOnlyList<EqBand> Get(int channel);

    /// <summary>Replaces a speaker channel's filters; an empty list removes its EQ.</summary>
    void Set(int channel, IReadOnlyList<EqBand> bands);

    /// <summary>Everything <see cref="Set"/> can change, to put back with <see cref="Restore"/> (cancel, or an error).</summary>
    object Snapshot();

    void Restore(object snapshot);

    /// <summary>Something the user should know before the EQ is replaced, or null.</summary>
    string? Warning { get; }

    /// <summary>Everything about where the EQ is kept, as it is now, for the EQ Wizard's diagnostics.</summary>
    string Dump();
}
