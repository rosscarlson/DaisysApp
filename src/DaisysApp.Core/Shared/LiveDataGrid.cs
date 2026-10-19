using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace DaisysApp.Shared;

/// <summary>
/// A DataGrid whose rows change all the time (a process list redrawn every second). It shows only itself to UI
/// Automation (screen readers, Voice Access and the like), not its rows and cells: with a program like that running
/// anywhere on the PC, WPF otherwise rebuilds a peer for every cell after each update (measured: half of what the UI
/// thread did with Performance showing). It looks the same as any DataGrid (the theme's DataGrid style).
/// </summary>
public class LiveDataGrid : DataGrid
{
    public LiveDataGrid() => SetResourceReference(StyleProperty, typeof(DataGrid));

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(LiveDataGrid owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;
        protected override string GetClassNameCore() => nameof(DataGrid);
    }
}
