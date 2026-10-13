using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Renamer;

/// <summary>
/// Renamer, a sub-tab of the Tools tab: many files (and folders) renamed at once, like Bulk Rename Utility: its
/// numbered panels (RegEx, Name, Replace, Case, Remove, Move/Copy, Add, Auto Date, Folder Name, Numbering, Extension),
/// a live preview of every new name, and Undo.
/// </summary>
public sealed class RenamerApplet : IApplet
{
    private readonly RenamerSettings settings = RenamerSettings.Load();
    private readonly RenamerView view;

    public RenamerApplet() => view = new RenamerView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() { }

    public void SaveSettings() => settings.Save();

    public void Dispose() { }
}
