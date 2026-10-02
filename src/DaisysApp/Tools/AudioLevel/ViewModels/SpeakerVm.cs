using System.ComponentModel;
using System.Runtime.CompilerServices;
using DaisysApp.Tools.AudioLevel.Audio;

namespace DaisysApp.Tools.AudioLevel.ViewModels;

public sealed class SpeakerVm(SpeakerDef def) : INotifyPropertyChanged
{
    private bool isSelected;
    private bool isSounding;
    private bool canTrim;
    private double trimDb;
    private double? micDb;
    private double? deltaDb;

    public SpeakerDef Def { get; } = def;
    public int Channel => Def.Channel;
    public bool IsLfe => Def.IsLfe;
    public string Short => Def.Short;
    public string Name => Def.Name;
    public double Left => Def.X * SpeakerLayout.CanvasWidth - SpeakerLayout.TileWidth / 2;
    public double Top => Def.Y * SpeakerLayout.CanvasHeight - SpeakerLayout.TileHeight / 2;
    public string ToolTip => CanTrim
        ? $"{Def.Name} — output channel {Def.Channel + 1}. Level {TrimText}."
        : $"{Def.Name} — output channel {Def.Channel + 1}. This device doesn't expose a volume for this channel.";

    public bool IsSelected
    {
        get => isSelected;
        set { if (isSelected != value) { isSelected = value; OnChanged(); } }
    }

    public bool IsSounding
    {
        get => isSounding;
        set { if (isSounding != value) { isSounding = value; OnChanged(); } }
    }

    public bool CanTrim
    {
        get => canTrim;
        set { if (canTrim != value) { canTrim = value; OnChanged(); OnChanged(nameof(TrimText)); OnChanged(nameof(ToolTip)); } }
    }

    /// <summary>Windows channel volume in dB (0 = full). Setting it (from the knob) raises <see cref="TrimChangedByUser"/>.</summary>
    public double TrimDb
    {
        get => trimDb;
        set
        {
            value = Math.Round(value, 1);
            if (trimDb == value) return;
            trimDb = value;
            RaiseTrim();
            TrimChangedByUser?.Invoke(this);
        }
    }

    public string TrimText => CanTrim ? FormatDb(TrimDb) : "—";

    private double trimMin = -40, trimMax = 0;
    public double TrimMin { get => trimMin; set { if (trimMin != value) { trimMin = value; OnChanged(); } } }
    public double TrimMax { get => trimMax; set { if (trimMax != value) { trimMax = value; OnChanged(); } } }

    /// <summary>Last mic level measured while this speaker played alone, and the signal level it was measured at.</summary>
    public double? MicDb => micDb;
    public double MicSignalDb { get; private set; }
    public bool HasReading => micDb != null;

    /// <summary>Mic level minus the locked reference (null when there's no reference or no reading).</summary>
    public double? DeltaDb
    {
        get => deltaDb;
        set
        {
            double? v = value is double d ? Math.Round(d, 1) : null;
            if (deltaDb == v) return;
            deltaDb = v;
            OnChanged();
            OnChanged(nameof(DeltaText));
            OnChanged(nameof(IsMatched));
        }
    }

    public string MicText => micDb is double m ? (Math.Round(m, 1)).ToString("0.0").Replace('-', '−') : "";
    public string DeltaText => deltaDb is double d ? "Δ" + (Math.Abs(d) < 0.05 ? "0.0" : d.ToString("+0.0;−0.0")) : "";
    public bool IsMatched => deltaDb is double d && Math.Abs(d) <= 0.5;

    public void SetMicReading(double db, double signalDb)
    {
        MicSignalDb = signalDb;
        bool had = micDb != null;
        if (micDb is double old && Math.Abs(old - db) < 0.01) return;
        micDb = db;
        OnChanged(nameof(MicDb));
        OnChanged(nameof(MicText));
        if (!had) OnChanged(nameof(HasReading));
    }

    public void ClearMicReading()
    {
        if (micDb == null) return;
        micDb = null;
        OnChanged(nameof(MicDb));
        OnChanged(nameof(MicText));
        OnChanged(nameof(HasReading));
        DeltaDb = null;
    }

    /// <summary>Updates the value read back from Windows without treating it as a user change.</summary>
    public void SetTrimFromSystem(double db)
    {
        db = Math.Round(db, 1);
        if (trimDb == db) return;
        trimDb = db;
        RaiseTrim();
    }

    public static string FormatDb(double db) => (Math.Abs(db) < 0.05 ? "0.0" : db.ToString("+0.0;−0.0")) + " dB";

    public event Action<SpeakerVm>? TrimChangedByUser;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseTrim()
    {
        OnChanged(nameof(TrimDb));
        OnChanged(nameof(TrimText));
        OnChanged(nameof(ToolTip));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
