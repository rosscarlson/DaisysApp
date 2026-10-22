using System.Windows;
using DaisysApp.Shell;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// Notepad, a sub-tab of the Tools tab: a note shared between PCs, hosted by one Daisy's App and edited on all of them
/// at once, over the AI module's remote control (Core's RemoteChannels).
/// </summary>
public sealed class NotepadApplet : IApplet
{
    private readonly NotepadSettings settings = NotepadSettings.Load();
    private readonly NotepadView view;

    public NotepadApplet() => view = new NotepadView(settings);

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start()
    {
        RemoteChannels.Serve(NotepadView.Channel, view.ServeAsync);
        view.Connect();
    }

    public void SaveSettings() => view.Save();

    public void Dispose() => view.Close();
}
