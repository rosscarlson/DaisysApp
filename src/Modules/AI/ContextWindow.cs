using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// Context (above Pause, while it works): more to know about what it's doing now, written in a small box and sent at
/// once, so it's read in the middle of the turn (between two steps) and the work carries on with it.
/// </summary>
internal sealed class ContextWindow : Window
{
    private readonly TextBox box = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 4, MaxLines = 10, Height = 110,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>What was written (once Send was pressed).</summary>
    public string Text => box.Text.Trim();

    public ContextWindow()
    {
        Title = T("Context");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var s = new StackPanel { Margin = new Thickness(16) };
        box.ToolTip = T("Ctrl+Enter sends");
        s.Children.Add(box);
        var send = new Button { Content = T("Send"), Padding = new Thickness(16, 4, 16, 4) };
        send.SetResourceReference(StyleProperty, "AccentButton");
        send.Click += (_, _) => Done();
        var cancel = new Button { Content = T("Cancel"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(send);
        buttons.Children.Add(cancel);
        s.Children.Add(buttons);
        Content = s;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Done(); }
        };
        Loaded += (_, _) => box.Focus();
    }

    private void Done()
    {
        if (Text.Length > 0) DialogResult = true;
    }
}
