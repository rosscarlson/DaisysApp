using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.UsbMonitor;

public partial class UsbMonitorSettingsView : UserControl
{
    private readonly UsbMonitorApplet tool;
    private readonly UsbMonitorSettings settings;
    private bool updating;

    public UsbMonitorSettingsView(UsbMonitorApplet tool, UsbMonitorSettings settings)
    {
        this.tool = tool;
        this.settings = settings;
        InitializeComponent();
        tool.LoggingChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        updating = true;
        LogBox.IsChecked = settings.LogToFile;
        updating = false;

        LogFileText.Text = tool.LogFilePath is { } path ? T("Current file: ") + path
                         : settings.LogToFile ? T("The log file couldn't be started.")
                         : T("Logging is off.");
    }

    private void LogBox_Changed(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        tool.SetLogging(LogBox.IsChecked == true);
        settings.Save();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            Process.Start(new ProcessStartInfo(AppPaths.LogFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogFileText.Text = T("Couldn't open the log folder: ") + ex.Message;
        }
    }
}
