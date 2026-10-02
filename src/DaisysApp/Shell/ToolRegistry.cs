using DaisysApp.Tools.AudioLevel;
using DaisysApp.Tools.UsbMonitor;

namespace DaisysApp.Shell;

public static class ToolRegistry
{
    /// <summary>The tools, in tab order. Settings is always the last tab and is added by the shell.</summary>
    public static IReadOnlyList<ITool> CreateAll() =>
    [
        new AudioLevelTool(),
        new UsbMonitorTool(),
    ];
}
