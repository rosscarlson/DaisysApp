using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// Opens when the TV took our key but never showed its "Allow USB debugging?" prompt: steps on the TV, one at a time
/// (Next, Quit), to turn ADB Debugging off and on again (which brings the prompt back), the last one connecting again.
/// </summary>
internal static class PromptWizard
{
    private static Window? open;

    private static readonly (string Title, string Text)[] steps =
    {
        ("No prompt on the TV", "Your Fire TV didn't show its prompt to allow this PC. Turning ADB Debugging off and on again fixes it. Pick up the TV's remote."),
        ("Settings", "On the Home screen, open Settings (the gear on the right)."),
        ("My Fire TV", "Open My Fire TV, then Developer Options.\n\nNo Developer Options? Open About, press OK on the device's name 7 times, then go back."),
        ("ADB Debugging off", "Select ADB Debugging to turn it Off."),
        ("ADB Debugging on", "Select ADB Debugging again to turn it On."),
        ("Home", "Press Home on the remote, so the Home screen shows (not an app or the screensaver)."),
        ("Connect", "Press Connect, then on the TV tick Always allow from this computer and press OK."),
    };

    /// <summary>Shows it (once at a time), near <paramref name="near"/>; the last step's Connect calls <paramref name="connect"/>.</summary>
    public static void Open(FrameworkElement near, Action connect)
    {
        if (open != null)
        {
            open.Activate();
            return;
        }
        var owner = Window.GetWindow(near);
        int at = 0;
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
        var count = new TextBlock { Margin = new Thickness(0, 2, 0, 12) };
        count.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, MinHeight = 80 };
        var quit = new Button { Content = T("Quit"), MinWidth = 90, IsCancel = true };
        var next = new Button { MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        next.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        AutomationProperties.SetName(quit, T("Quit"));
        var w = new Window
        {
            Title = T("Fire TV"), Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(22, 18, 22, 18),
                Children =
                {
                    title, count, text,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { quit, next } },
                },
            },
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        w.SourceInitialized += (_, _) => Theming.ThemeManager.ApplyTitleBar(w);
        void Step()
        {
            title.Text = T(steps[at].Title);
            count.Text = F("Step {0} of {1}", at + 1, steps.Length);
            text.Text = T(steps[at].Text);
            bool last = at == steps.Length - 1;
            next.Content = last ? T("Connect") : T("Next");
            AutomationProperties.SetName(next, (string)next.Content);
        }
        next.Click += (_, _) =>
        {
            if (at < steps.Length - 1)
            {
                at++;
                Step();
                return;
            }
            w.Close();
            connect();
        };
        quit.Click += (_, _) => w.Close();
        w.Closed += (_, _) => open = null;
        Step();
        open = w;
        w.Show();
    }
}
