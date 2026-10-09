using System.Windows;
using System.Windows.Controls;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>Asks for one line of text (a profile's name).</summary>
internal sealed class TextPrompt : Window
{
    private readonly TextBox box;

    public string Value => box.Text.Trim();

    public TextPrompt(string title, string label, string value)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        box = new TextBox { Text = value };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = T("OK"), MinWidth = 90, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        ok.Click += (_, _) => { if (Value.Length > 0) DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = T("Cancel"), MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}
