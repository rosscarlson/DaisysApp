using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.Gaming.ButtRemote;

/// <summary>
/// Settings → Gaming → Button2Key Remote: the templates (built in and yours), with Import…, Open the folder, Make it
/// yours (a copy of a built-in one to change) and Delete (yours).
/// </summary>
internal sealed class RemoteSettingsView : UserControl
{
    private readonly StackPanel rows = new();
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

    public RemoteSettingsView()
    {
        var s = new StackPanel();
        s.Children.Add(Secondary(T("A template is a game's buttons, in groups, each with its label, icon and keys. The Butt Wizard (in the tab, beside New ButtScreen) lets you tick the groups and buttons you want, and lays them out for you to change as you like.")));
        s.Children.Add(Secondary(F("Yours are JSON files in {0}: put one there, or Import… it. One of yours with a built-in one's name takes its place; Make it yours copies a built-in one there, to change. The format is in the module's README.", TemplateStore.Folder)));
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var import = Small(T("Import…"), Import);
        import.SetResourceReference(StyleProperty, "AccentButton");
        tools.Children.Add(import);
        tools.Children.Add(Small(T("Open the folder"), () =>
        {
            Directory.CreateDirectory(TemplateStore.Folder);
            Process.Start(new ProcessStartInfo(TemplateStore.Folder) { UseShellExecute = true });
        }));
        tools.Children.Add(Small(T("Refresh"), Fill));
        s.Children.Add(tools);
        s.Children.Add(result);
        rows.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(rows);

        var card = new StackPanel();
        var h = new TextBlock { Text = T("ButtScreen templates") };
        h.SetResourceReference(StyleProperty, "CardHeader");
        card.Children.Add(h);
        card.Children.Add(s);
        var b = new Border { Child = card };
        b.SetResourceReference(StyleProperty, "Card");
        Content = b;
        IsVisibleChanged += (_, _) => { if (IsVisible) Fill(); };
    }

    private void Fill()
    {
        rows.Children.Clear();
        var all = TemplateStore.LoadAll();
        foreach (var problem in TemplateStore.Problems)
        {
            var t = new TextBlock { Text = F("Not used: {0}", problem), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            rows.Children.Add(t);
        }
        foreach (var t in all)
        {
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var template = t;
            if (t.BuiltIn)
                tools.Children.Add(Small(T("Make it yours"), () =>
                {
                    if (TemplateStore.MakeYours(template) is { } file) Say(F("Copied to {0}: change it there (it takes the built-in one's place).", file), false);
                    Fill();
                }, T("Copies it to your folder, to change")));
            else
                tools.Children.Add(RemoteView.ConfirmButton(T("Delete"), () => { TemplateStore.Delete(template); Fill(); }, F("Deletes {0}", template.File!)));
            DockPanel.SetDock(tools, Dock.Right);
            line.Children.Add(tools);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(Secondary(F("{0} · {1} · {2}", P(t.ButtonCount, "{0} button", "{0} buttons"), P(t.Groups.Count, "{0} group", "{0} groups"),
                t.BuiltIn ? T("built in") : F("yours ({0})", Path.GetFileName(t.File!)))));
            line.Children.Add(text);
            rows.Children.Add(line);
        }
    }

    private void Import()
    {
        var pick = new Microsoft.Win32.OpenFileDialog { Title = T("Import a template"), Filter = T("Templates (*.json)") + "|*.json", Multiselect = true };
        if (pick.ShowDialog(Window.GetWindow(this)) != true) return;
        var done = new List<string>();
        var problems = new List<string>();
        foreach (var file in pick.FileNames)
        {
            if (TemplateStore.Import(file, out var why) is { } t) done.Add(t.Name);
            else problems.Add(F("{0}: {1}", Path.GetFileName(file), why));
        }
        Say(string.Join("\n", (done.Count > 0 ? new[] { F("Imported: {0}", string.Join(", ", done)) } : Array.Empty<string>()).Concat(problems)), problems.Count > 0);
        Fill();
    }

    private void Say(string text, bool problem)
    {
        result.Text = text;
        result.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        result.SetResourceReference(TextBlock.ForegroundProperty, problem ? "ErrorTextBrush" : "SuccessBrush");
    }

    private static TextBlock Secondary(string text) => Ui.Note(text, 4);

    private static Button Small(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 5, 5), ToolTip = tip };
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }
}
