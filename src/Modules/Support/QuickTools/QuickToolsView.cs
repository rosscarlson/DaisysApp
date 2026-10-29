using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DaisysApp.Applets.Tools.Shared;
using DaisysApp.Logging;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Support.QuickTools;

/// <summary>
/// The Tools sub-tab: a card of buttons per kind of job (Display, Network, System, Power, Open), each one an
/// <see cref="Actions"/> call; the last one's outcome shows at the top. Ones that can't be undone (or end the session)
/// ask Confirm? first.
/// </summary>
internal sealed class QuickToolsView : Grid
{
    private readonly TextBlock status = Ui.Secondary();

    public QuickToolsView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        status.Margin = new Thickness(4, 0, 0, 8);
        Children.Add(status);

        var cards = new WrapPanel();
        cards.Children.Add(Card(T("Display"),
            Do("", T("Reset Graphics Driver"), T("As Win+Ctrl+Shift+B"), Actions.ResetGraphics),
            Do("", T("Restart Explorer"), T("The taskbar and desktop"), Actions.RestartExplorer),
            Do("", T("Refresh Icons"), T("Rebuilds the icon cache"), Actions.RefreshIcons)));
        cards.Children.Add(Card(T("Network"),
            Do("", T("Flush DNS"), T("Clears Windows' DNS cache"), Actions.FlushDns),
            Do("", T("Renew IP"), T("Release and renew every adapter's address (as administrator)"), Actions.RenewIp, confirm: true),
            Do("", T("Reset Network"), T("Winsock and TCP/IP reset (as administrator); Windows restarts to finish"), Actions.ResetNetwork, confirm: true)));
        cards.Children.Add(Card(T("System"),
            Do("", T("Restart Audio"), T("Windows' audio services (as administrator)"), Actions.RestartAudio),
            Do("", T("Sync Clock"), T("With the time server (as administrator)"), Actions.SyncClock),
            Do("", T("Empty Recycle Bin"), T("Every drive's"), Actions.EmptyRecycleBin, confirm: true),
            Do("", T("Clear Clipboard"), null, Actions.ClearClipboard),
            Do("", T("Repair Windows"), T("DISM and SFC in a window (as administrator)"), Actions.RepairWindows)));
        cards.Children.Add(Card(T("Power"),
            Do("", T("Lock"), null, Actions.Lock),
            Do("", T("Sleep"), null, Actions.Sleep, confirm: true),
            Do("", T("Sign Out"), null, Actions.SignOut, confirm: true),
            Do("", T("Restart"), null, Actions.Restart, confirm: true),
            Do("", T("Restart to BIOS"), T("UEFI firmware settings (as administrator)"), Actions.RestartToBios, confirm: true),
            Do("", T("Advanced Startup"), T("Windows' recovery options"), Actions.AdvancedStartup, confirm: true),
            Do("", T("Shut Down"), null, Actions.ShutDown, confirm: true)));
        cards.Children.Add(Card(T("Open"),
            Do("", T("Task Manager"), null, () => Actions.Open("taskmgr.exe")),
            Do("", T("Device Manager"), null, () => Actions.Open("devmgmt.msc")),
            Do("", T("Services"), null, () => Actions.Open("services.msc")),
            Do("", T("Event Viewer"), null, () => Actions.Open("eventvwr.msc")),
            Do("", T("Disk Management"), null, () => Actions.Open("diskmgmt.msc")),
            Do("", T("Network Connections"), null, () => Actions.Open("ncpa.cpl")),
            Do("", T("System Properties"), null, () => Actions.Open("SystemPropertiesAdvanced.exe")),
            Do("", T("Programs"), T("Programs and Features"), () => Actions.Open("appwiz.cpl")),
            Do("", T("Startup Apps"), null, () => Actions.Open("ms-settings:startupapps")),
            Do("", T("Registry Editor"), null, () => Actions.Open("regedit.exe")),
            Do("", T("Control Panel"), null, () => Actions.Open("control.exe")),
            Do("", T("Admin Terminal"), T("As administrator"), Actions.AdminTerminal)));

        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetRow(scroll, 1);
        Children.Add(scroll);
    }

    private static Border Card(string title, params Button[] buttons)
    {
        var wrap = new WrapPanel();
        foreach (var b in buttons) wrap.Children.Add(b);
        return Ui.Card(new StackPanel { Children = { Ui.Header(title), wrap } }, new Thickness(0, 0, 12, 12));
    }

    /// <summary>A button that runs <paramref name="action"/> (off the window's thread) and shows what it said.</summary>
    private Button Do(string glyph, string text, string? tip, Func<string> action, bool confirm = false)
    {
        var b = Ui.IconButton(glyph, text, tip);
        b.Margin = new Thickness(0, 6, 8, 0);
        b.MinWidth = 150;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(b, text);
        async void Run()
        {
            b.IsEnabled = false;
            Show(F("{0}…", text), false);
            try
            {
                string said = await Task.Run(action);
                Show(F("{0}: {1}", text, said), false);
                Log.Here.Info($"Tools: {text}: {said}");
            }
            catch (Exception ex)
            {
                Show(F("{0}: {1}", text, ex.Message), true);
                Log.Here.Warn($"Tools: {text} failed: {ex.Message}");
            }
            finally
            {
                b.IsEnabled = true;
            }
        }
        if (confirm) ConfirmButton.Arm(b, Run);
        else b.Click += (_, _) => Run();
        return b;
    }

    private void Show(string text, bool error)
    {
        status.Text = text;
        if (error) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);
    }
}
