using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace DaisysApp.Applets.Tools.DiskUsage;

/// <summary>One line of the folder tree: an entry at its depth, or "… and N more" for a folder's smallest entries.</summary>
internal sealed class TreeRow(DiskTree tree, int node, int depth, bool expanded) : INotifyPropertyChanged
{
    public DiskTree Tree { get; } = tree;
    public int Node { get; } = node;
    public int Depth { get; } = depth;
    public bool Expanded { get; set; } = expanded;

    /// <summary>For a "… and N more" line: the folder, and how many of its entries it stands for.</summary>
    public int MoreOf { get; init; } = -1;
    public int MoreCount { get; init; }
    public long MoreSize { get; init; }
    public long MoreAlloc { get; init; }

    public bool IsMore => MoreOf >= 0;
    public bool IsDir => !IsMore && Tree.IsDir(Node);

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public string Expander => IsDir && Tree.ChildCount[Node] > 0 ? (Expanded ? "" : "") : "";
    public string Icon => IsMore ? "" : IsDir ? (Tree.IsLink(Node) ? "" : "") : "";
    public string Name => IsMore ? P(MoreCount, "… and {0} smaller entry", "… and {0} smaller entries") : Tree.Name(Node);

    public long SizeValue => IsMore ? MoreSize : Tree.Size[Node];
    public long AllocValue => IsMore ? MoreAlloc : Tree.Alloc[Node];

    /// <summary>Of its folder's size (the root's: of the drive's used space when it's a drive).</summary>
    public double Fraction
    {
        get
        {
            int parent = IsMore ? MoreOf : Node == 0 ? -1 : Tree.Parent[Node];
            long of = parent >= 0 ? Tree.Size[parent] : Tree.VolumeTotal - Tree.VolumeFree;
            return of > 0 ? Math.Clamp((double)SizeValue / of, 0, 1) : 0;
        }
    }

    public double BarWidth => Math.Max(Fraction > 0 ? 1 : 0, Fraction * 70);
    public string PercentText => Sizes.Percent(Fraction);
    public string SizeText => Sizes.Bytes(SizeValue);
    public string AllocText => Sizes.Bytes(AllocValue);
    public string FilesText => IsMore ? "" : IsDir ? Sizes.Count(Tree.Files[Node]) : "";
    public string FoldersText => IsMore ? "" : IsDir ? Sizes.Count(Tree.Folders[Node]) : "";
    public string ModifiedText => IsMore || Tree.Modified[Node] <= 0 ? "" : DateTime.FromFileTimeUtc(Tree.Modified[Node]).ToLocalTime().ToString("g");
    public string Tip => IsMore ? "" : Tree.PathOf(Node);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Its numbers changed (a delete under it): show them again.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>A collection that takes many items at once with one change notice (thousands of rows for a big folder).</summary>
internal sealed class BulkCollection<T> : ObservableCollection<T>
{
    public void InsertRange(int index, IReadOnlyCollection<T> items)
    {
        if (items.Count == 0) return;
        ((List<T>)Items).InsertRange(index, items);
        Reset();
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0) return;
        ((List<T>)Items).RemoveRange(index, count);
        Reset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        ((List<T>)Items).AddRange(items);
        Reset();
    }

    private void Reset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>One file type (extension) in the scan: its colour on the treemap, total size and number of files.</summary>
internal sealed class FileType
{
    public required string Extension { get; init; }
    public long Size { get; set; }
    public long Alloc { get; set; }
    public int Files { get; set; }
    public uint Color { get; set; } = 0x9A9A9A;
    public double Fraction { get; set; }

    public string Name => Extension.Length == 0 ? T("(no extension)") : "." + Extension;
    public System.Windows.Media.Brush Swatch => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(Color >> 16), (byte)(Color >> 8), (byte)Color));
    public string SizeText => Sizes.Bytes(Size);
    public string PercentText => Sizes.Percent(Fraction);
    public string FilesText => Sizes.Count(Files);
    public double BarWidth => Math.Max(Fraction > 0 ? 1 : 0, Fraction * 40);
}

/// <summary>The scan's file types, largest first, and each file's type (for the treemap's colours).</summary>
internal sealed class FileTypes
{
    public static readonly uint[] Palette =
    [
        0x3D7CF6, 0xE8483D, 0x3DC46A, 0xF2C230, 0x9C5BD9, 0x2EC3CF,
        0xF2853D, 0xE85DA8, 0x8FCB3D, 0x5F6FE8, 0xC9A27A, 0x4FB3A0,
    ];
    public const uint Other = 0x9A9A9A, FolderColor = 0x8A8A8A;

    public List<FileType> Types { get; } = new();
    public int[] TypeOf = []; // per entry: index into Types, −1 for folders and deleted ones

    public static FileTypes Of(DiskTree t, CancellationToken cancel = default)
    {
        var r = new FileTypes { TypeOf = new int[t.Count] };
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var gone = new bool[t.Count];
        for (int i = 0; i < t.Count; i++)
        {
            if ((i & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
            gone[i] = t.IsDeleted(i) || (i > 0 && gone[t.Parent[i]]);
            if (gone[i] || t.IsDir(i)) { r.TypeOf[i] = -1; continue; }
            var name = t.NameSpan(i);
            int dot = name.LastIndexOf('.');
            string ext = dot > 0 && dot < name.Length - 1 && name.Length - dot <= 16 ? name[(dot + 1)..].ToString().ToLowerInvariant() : "";
            if (!index.TryGetValue(ext, out int k))
            {
                k = r.Types.Count;
                index[ext] = k;
                r.Types.Add(new FileType { Extension = ext });
            }
            var ft = r.Types[k];
            ft.Size += t.Size[i];
            ft.Alloc += t.Alloc[i];
            ft.Files++;
            r.TypeOf[i] = k;
        }
        // largest first, renumbered; the biggest get colours
        var order = r.Types.Select((x, i) => (x, i)).OrderByDescending(p => p.x.Size).ToList();
        var renumber = new int[order.Count];
        for (int k = 0; k < order.Count; k++) renumber[order[k].i] = k;
        r.Types.Clear();
        long total = Math.Max(1, order.Sum(p => p.x.Size));
        foreach (var (x, _) in order)
        {
            x.Color = r.Types.Count < Palette.Length ? Palette[r.Types.Count] : Other;
            x.Fraction = (double)x.Size / total;
            r.Types.Add(x);
        }
        for (int i = 0; i < r.TypeOf.Length; i++) if (r.TypeOf[i] >= 0) r.TypeOf[i] = renumber[r.TypeOf[i]];
        return r;
    }

    public uint ColorOf(int node) => node < TypeOf.Length && TypeOf[node] >= 0 ? Types[TypeOf[node]].Color : FolderColor;
}
