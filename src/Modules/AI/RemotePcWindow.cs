using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// One of the other PCs this one uses the sessions of (remote control), added or changed: its name here, its name or IP
/// address, port and passphrase, and Test (connects and says how many sessions are open there). Save keeps it.
/// </summary>
internal sealed class RemotePcWindow : Window
{
    private readonly TextBox name = new();
    private readonly TextBox address = new() { ToolTip = T("The other PC's name or IP address (its Settings → AI → Remote control shows them)") };
    private readonly TextBox port = new() { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly PasswordBox pass = new() { ToolTip = T("Kept encrypted for this Windows user") };
    private readonly TextBlock tested = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public RemotePcWindow(RemotePc pc, bool adding)
    {
        Title = adding ? T("Add a PC") : F("{0} (remote control)", pc.Label);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        name.Text = pc.Name;
        address.Text = pc.Address;
        port.Text = pc.Port.ToString();
        pass.Password = pc.Passphrase;

        var s = new StackPanel { Margin = new Thickness(20) };
        s.Children.Add(Row(T("Name here"), name, T("What it's called in this PC's tabs (empty: its address)")));
        s.Children.Add(Row(T("Its name or address"), address));
        s.Children.Add(Row(T("Its port"), port));
        s.Children.Add(Row(T("Its passphrase"), pass));
        var test = new Button { Content = T("Test"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, _) =>
        {
            var x = new RemotePc();
            if (Read(x) is { } why) { Tell(why, true); return; }
            Tell(F("Connecting to {0}…", x.Target), false);
            test.IsEnabled = false;
            try
            {
                var reply = await RemoteClient.AskAsync(x, new JsonObject { ["op"] = "list" });
                int n = (reply["sessions"] as JsonArray)?.Count ?? 0;
                Tell(F("Connected to {0}: {1} open there.", reply["pc"]?.GetValue<string>() ?? x.Address, P(n, "{0} session", "{0} sessions")), false);
            }
            catch (Exception ex) { Tell(ex is RemoteException ? ex.Message : F("It couldn't connect: {0}", ex.Message), true); }
            test.IsEnabled = true;
        };
        s.Children.Add(test);
        s.Children.Add(tested);

        var save = new Button { Content = T("Save"), Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += (_, _) =>
        {
            if (Read(pc) is { } why) { Tell(why, true); return; }
            DialogResult = true;
        };
        var cancel = new Button { Content = T("Cancel"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        s.Children.Add(buttons);
        Content = s;
        Loaded += (_, _) => (adding ? address : name).Focus();
    }

    /// <summary>The boxes into pc; the reason they can't be, or null.</summary>
    private string? Read(RemotePc pc)
    {
        if (address.Text.Trim().Length == 0) return T("Set the other PC's name or address.");
        if (!int.TryParse(port.Text.Trim(), out int p) || p is <= 0 or >= 65536) return T("The port is a number from 1 to 65535.");
        pc.Name = name.Text.Trim();
        pc.Address = address.Text.Trim();
        pc.Port = p;
        pc.Passphrase = pass.Password;
        return null;
    }

    private void Tell(string text, bool error)
    {
        tested.Text = text;
        tested.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private static FrameworkElement Row(string label, FrameworkElement control, string? tip = null)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        if (tip != null) control.ToolTip = tip;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }
}
