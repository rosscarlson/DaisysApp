using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DaisysApp.Applets.AudioTools.Levels;

/// <summary>Levels' part of Settings → Audio Tools: how much one volume shortcut press changes the volume.</summary>
internal sealed class LevelsSettingsCard : Border
{
    private readonly AudioLevelsSettings settings;
    private readonly TextBox step = new() { Width = 50, VerticalContentAlignment = VerticalAlignment.Center };

    public LevelsSettingsCard(AudioLevelsSettings settings)
    {
        this.settings = settings;
        SetResourceReference(StyleProperty, "Card");
        var panel = new StackPanel();
        var header = new TextBlock { Text = T("Levels") };
        header.SetResourceReference(StyleProperty, "CardHeader");
        panel.Children.Add(header);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = T("Each shortcut press changes the volume by"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        step.Text = settings.Step.ToString(CultureInfo.CurrentCulture);
        System.Windows.Automation.AutomationProperties.SetName(step, T("Each shortcut press changes the volume by"));
        step.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };
        step.LostKeyboardFocus += (_, _) => Apply();
        line.Children.Add(step);
        panel.Children.Add(line);

        var hint = new TextBlock { Text = T("The volume up / down shortcuts are set on the Levels tool: click a device's or app's name there."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        hint.SetResourceReference(StyleProperty, "SecondaryText");
        panel.Children.Add(hint);
        Child = panel;
    }

    private void Apply()
    {
        if (int.TryParse(step.Text.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.CurrentCulture, out int s) && s is >= 1 and <= 100)
        {
            settings.Step = s;
            settings.Save();
        }
        step.Text = settings.Step.ToString(CultureInfo.CurrentCulture);
    }
}
