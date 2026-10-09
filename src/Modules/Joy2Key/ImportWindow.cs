using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;
using Microsoft.Win32;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Brings JoyToKey's profiles in, in three steps: the folder they're in, which of them, and what came across (and what
/// couldn't). Each becomes a Joy 2 Key profile of the same name.
/// </summary>
internal sealed class ImportWindow : Window
{
    private readonly Joy2KeySettings settings;
    private readonly IReadOnlyList<J2KProfile> existing;
    private readonly ContentControl page = new();
    private readonly Button back = new() { MinWidth = 90 }, next = new() { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox folder = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox replace = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<(CheckBox Box, JoyToKeyImport.Result Result)> found = new();
    private int step;

    /// <summary>The profiles brought in (already saved).</summary>
    public List<J2KProfile> Imported { get; } = new();

    public ImportWindow(Joy2KeySettings settings, IReadOnlyList<J2KProfile> existing)
    {
        this.settings = settings;
        this.existing = existing;
        Title = T("Import from JoyToKey");
        Width = 620;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        next.SetResourceReference(StyleProperty, "AccentButton");
        back.Content = T("Back");
        back.Click += (_, _) => Show(step - 1);
        next.Click += (_, _) => Next();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        var dock = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(page);
        Content = dock;

        folder.Text = settings.ImportFolder is { } f && Directory.Exists(f) ? f : JoyToKeyImport.DefaultFolder() ?? "";
        replace.Content = T("Replace my profiles that have the same name (otherwise the new ones get a number)");
        Show(0);
    }

    private void Show(int n)
    {
        step = n;
        back.Visibility = n == 1 ? Visibility.Visible : Visibility.Collapsed;
        next.Content = n switch { 0 => T("Next"), 1 => T("Import"), _ => T("Done") };
        page.Content = n switch { 0 => FolderPage(), 1 => PickPage(), _ => page.Content };
    }

    private void Next()
    {
        switch (step)
        {
            case 0:
                if (!Directory.Exists(folder.Text)) { MessageBox.Show(this, T("That folder doesn't exist."), Title); return; }
                found.Clear();
                foreach (var (file, name) in JoyToKeyImport.Find(folder.Text))
                {
                    try
                    {
                        var r = JoyToKeyImport.Read(file, name);
                        found.Add((new CheckBox { IsChecked = true }, r));
                    }
                    catch (Exception ex) { Logging.ErrorLog.Write("Reading JoyToKey profile " + file, ex); }
                }
                if (found.Count == 0) { MessageBox.Show(this, T("There are no JoyToKey profiles (.cfg files) in that folder or its subfolders."), Title); return; }
                settings.ImportFolder = folder.Text;
                settings.Save();
                Show(1);
                break;
            case 1:
                if (!found.Any(f => f.Box.IsChecked == true)) { MessageBox.Show(this, T("Tick at least one profile."), Title); return; }
                Import();
                step = 2;
                Show(2);
                break;
            default:
                DialogResult = Imported.Count > 0;
                break;
        }
    }

    private FrameworkElement FolderPage()
    {
        var p = new StackPanel();
        p.Children.Add(Heading(T("Step 1 of 3: where are they?")));
        p.Children.Add(Secondary(T("JoyToKey keeps its profiles (.cfg files) in a folder of their own, Documents\\JoyToKey unless you moved it. In JoyToKey, Settings → Preferences shows where. Subfolders are looked in too.")));
        var browse = new Button { Content = T("Browse…"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = T("The folder with JoyToKey's profiles") };
            if (Directory.Exists(folder.Text)) dlg.InitialDirectory = folder.Text;
            if (dlg.ShowDialog(this) == true) folder.Text = dlg.FolderName;
        };
        var line = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        line.Children.Add(browse);
        line.Children.Add(folder);
        p.Children.Add(line);
        return p;
    }

    private FrameworkElement PickPage()
    {
        var p = new DockPanel();
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(Heading(T("Step 2 of 3: which ones?")));
        top.Children.Add(Secondary(F("{0} has these profiles. Tick the ones to bring in.", folder.Text)));
        var all = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        var allButton = new Button { Content = T("All"), Padding = new Thickness(10, 2, 10, 2) };
        allButton.Click += (_, _) => found.ForEach(f => f.Box.IsChecked = true);
        var noneButton = new Button { Content = T("None"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        noneButton.Click += (_, _) => found.ForEach(f => f.Box.IsChecked = false);
        all.Children.Add(allButton);
        all.Children.Add(noneButton);
        top.Children.Add(all);
        p.Children.Add(top);
        DockPanel.SetDock(replace, Dock.Bottom);
        p.Children.Add(replace);

        var list = new StackPanel();
        foreach (var (box, r) in found)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = r.Profile.Name, FontWeight = FontWeights.SemiBold });
            string detail = P(r.Assigned, "{0} assignment", "{0} assignments") + " · " + P(r.Profile.Devices.Count, "{0} controller", "{0} controllers");
            if (existing.Any(e => e.Name.Equals(r.Profile.Name, StringComparison.OrdinalIgnoreCase))) detail += " · " + T("you have a profile of this name");
            if (r.Skipped.Count > 0) detail += " · " + P(r.Skipped.Count, "{0} thing can't come across", "{0} things can't come across");
            text.Children.Add(Secondary(detail));
            box.Content = text;
            box.Margin = new Thickness(0, 0, 0, 8);
            box.VerticalContentAlignment = VerticalAlignment.Top;
            list.Children.Add(box);
        }
        p.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return p;
    }

    private void Import()
    {
        var names = existing.ToList();
        var report = new StackPanel();
        foreach (var (box, r) in found.Where(f => f.Box.IsChecked == true))
        {
            var profile = r.Profile;
            var same = names.FirstOrDefault(e => e.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            if (same != null && replace.IsChecked != true) profile.Name = ProfileStore.FreeName(profile.Name, names);
            else if (same != null) names.Remove(same);
            ProfileStore.Save(profile);
            names.Add(profile);
            Imported.Add(profile);

            report.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            report.Children.Add(Secondary(P(r.Assigned, "{0} assignment brought in", "{0} assignments brought in")));
            foreach (var s in r.Skipped.Distinct().Take(30))
            {
                var t = Secondary("• " + s);
                t.Margin = new Thickness(10, 0, 0, 0);
                report.Children.Add(t);
            }
            if (r.Skipped.Distinct().Count() > 30) report.Children.Add(Secondary("  …"));
        }

        var p = new DockPanel();
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(Heading(T("Step 3 of 3: done")));
        top.Children.Add(Secondary(T("JoyToKey numbers controllers 1, 2…: each profile's are linked to whichever are plugged in, in that order. To tie one to a particular controller, pick it at the top of its box in the profile.")));
        p.Children.Add(top);
        p.Children.Add(new ScrollViewer { Content = report, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        page.Content = p;
    }

    private static TextBlock Heading(string s) => new() { Text = s, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    private static TextBlock Secondary(string s)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }
}
