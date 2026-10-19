using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Picking a folder on another PC (remote control), for a new session there: its drives and the user's folders at the
/// left, the folder's subfolders at the right (double-click to go in, Up to go back), a path box to type one, and New
/// folder to make one there. Each step asks that PC ({op: "dirs"} / {op: "mkdir"}).
/// </summary>
internal sealed class RemoteFolderWindow : Window
{
    private readonly RemoteSettings remote;
    private readonly ListBox places = new() { Width = 190, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ListBox dirs = new();
    private readonly TextBox path = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button up = new() { Content = T("Up"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button pick = new() { Content = T("Use this folder"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox newName = new() { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel newRow = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private string current = "";
    private string? parent;
    private bool busy;

    /// <summary>The folder picked there (null: cancelled).</summary>
    public string? Folder { get; private set; }

    public RemoteFolderWindow(RemoteSettings remote, string start, string pc)
    {
        this.remote = remote;
        Title = F("A folder on {0}", pc);
        Width = 760;
        Height = 560;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        // the path, and Up
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(up, Dock.Right);
        top.Children.Add(up);
        top.Children.Add(path);
        path.ToolTip = T("A folder there: type it and press Enter to go to it");
        path.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = GoAsync(path.Text.Trim()); } };
        up.Click += (_, _) => _ = GoAsync(parent ?? "");

        // the places, and the subfolders
        places.SelectionChanged += (_, _) => { if (places.SelectedItem is ListBoxItem { Tag: string p }) _ = GoAsync(p); };
        // (and clicked again, once it's been left)
        places.PreviewMouseLeftButtonUp += (_, _) => { if (places.SelectedItem is ListBoxItem { Tag: string p } && !SamePath(p, current)) _ = GoAsync(p); };
        dirs.MouseDoubleClick += (_, _) => Enter();
        dirs.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Enter(); } };
        var middle = new DockPanel();
        DockPanel.SetDock(places, Dock.Left);
        middle.Children.Add(places);
        middle.Children.Add(dirs);

        // a new folder
        var create = new Button { Content = T("Make it"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        var cancelNew = new Button { Content = T("Cancel"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        newRow.Children.Add(new TextBlock { Text = T("New folder's name:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        newRow.Children.Add(newName);
        newRow.Children.Add(create);
        newRow.Children.Add(cancelNew);
        create.Click += (_, _) => _ = MakeAsync();
        newName.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = MakeAsync(); }
            else if (e.Key == Key.Escape) { e.Handled = true; newRow.Visibility = Visibility.Collapsed; }
        };
        cancelNew.Click += (_, _) => newRow.Visibility = Visibility.Collapsed;

        // the buttons
        var newFolder = new Button { Content = T("New folder…"), Padding = new Thickness(12, 4, 12, 4), ToolTip = T("Make a folder in this one, there") };
        newFolder.Click += (_, _) =>
        {
            if (current.Length == 0) { Say(T("Pick a drive or folder first."), true); return; }
            newName.Text = "";
            newRow.Visibility = Visibility.Visible;
            newName.Focus();
        };
        var cancel = new Button { Content = T("Cancel"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        pick.SetResourceReference(StyleProperty, "AccentButton");
        pick.Click += (_, _) =>
        {
            // a subfolder picked in the list, or the folder showing
            string chosen = dirs.SelectedItem is ListBoxItem { Tag: string sub } && current.Length > 0 ? System.IO.Path.Combine(current, sub) : current;
            if (chosen.Length == 0) { Say(T("Pick a drive or folder first."), true); return; }
            Folder = chosen;
            DialogResult = true;
        };
        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(pick);
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { newFolder } });

        var bottom = new StackPanel();
        bottom.Children.Add(newRow);
        bottom.Children.Add(status);
        bottom.Children.Add(buttons);
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(middle);
        Content = root;

        Loaded += async (_, _) =>
        {
            await GoAsync(start);
            // a folder that isn't there (any more): the drives and the user's folders
            if (current.Length == 0 && start.Length > 0) await GoAsync("");
        };
    }

    private void Say(string text, bool error)
    {
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void Enter()
    {
        if (dirs.SelectedItem is ListBoxItem { Tag: string sub } && current.Length > 0) _ = GoAsync(System.IO.Path.Combine(current, sub));
    }

    /// <summary>Shows a folder there ("": the drives and the user's folders only).</summary>
    private async Task GoAsync(string folder)
    {
        if (busy) return;
        busy = true;
        Say(T("Asking…"), false);
        try
        {
            var reply = await RemoteClient.AskAsync(remote, new JsonObject { ["op"] = "dirs", ["path"] = folder });
            current = reply["path"]?.GetValue<string>() ?? "";
            parent = reply["parent"]?.GetValue<string>();
            path.Text = current;
            up.IsEnabled = current.Length > 0;
            if (places.Items.Count == 0)
                foreach (var p in (reply["places"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    places.Items.Add(new ListBoxItem { Content = p["name"]?.GetValue<string>() ?? "?", Tag = p["path"]?.GetValue<string>() ?? "", ToolTip = p["path"]?.GetValue<string>() });
            dirs.Items.Clear();
            foreach (var d in (reply["dirs"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0) ?? Enumerable.Empty<string>())
                dirs.Items.Add(new ListBoxItem { Content = "\U0001F4C1  " + d, Tag = d });
            Say(current.Length == 0 ? T("Pick a drive or folder at the left.") : dirs.Items.Count == 0 ? T("No folders in this one.") : T("Double-click a folder to go in."), false);
        }
        catch (Exception ex) { Say(ex is RemoteException ? ex.Message : F("It couldn't connect: {0}", ex.Message), true); }
        finally { busy = false; }
    }

    /// <summary>Makes the folder named in the box, in the folder showing, and goes into it.</summary>
    private async Task MakeAsync()
    {
        string name = newName.Text.Trim();
        if (name.Length == 0 || current.Length == 0 || busy) return;
        try
        {
            var reply = await RemoteClient.AskAsync(remote, new JsonObject { ["op"] = "mkdir", ["parent"] = current, ["name"] = name });
            newRow.Visibility = Visibility.Collapsed;
            await GoAsync(reply["path"]?.GetValue<string>() ?? current);
        }
        catch (Exception ex) { Say(ex is RemoteException ? ex.Message : F("It couldn't connect: {0}", ex.Message), true); }
    }
}
