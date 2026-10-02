using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Tools.UsbMonitor;

public partial class UsbMonitorSettingsView : UserControl
{
    private readonly UsbMonitorTool tool;
    private readonly UsbMonitorSettings settings;
    private bool updating;

    public UsbMonitorSettingsView(UsbMonitorTool tool, UsbMonitorSettings settings)
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

        LogFileText.Text = tool.LogFilePath is { } path ? "Current file: " + path
                         : settings.LogToFile ? "The log file couldn't be started."
                         : "Logging is off.";
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
            LogFileText.Text = "Couldn't open the log folder: " + ex.Message;
        }
    }
}
