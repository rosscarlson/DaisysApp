using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Video.Gaming;

/// <summary>
/// Gaming's step in the app's setup wizard, while this sign-in can't count frames yet (<see cref="Permission.Granted"/>):
/// Allow (Windows' administrator prompt adds the user to Performance Log Users), then sign out and in or restart;
/// and whether to show the FPS overlay, which is off until it's switched on.
/// </summary>
internal sealed class GamingSetupPage : StackPanel
{
    private readonly GamingService service;
    private readonly TextBlock status = Text("");
    private readonly Button allow = new() { Content = T("Allow…"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 110 };

    public GamingSetupPage(GamingService service)
    {
        this.service = service;
        allow.SetResourceReference(StyleProperty, "AccentButton");
        Children.Add(new TextBlock { Text = T("Overlay: frame counting and the FPS overlay"), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        Children.Add(Text(T("Daisy's App counts each game's frames, for the FPS overlay, benchmarks and each app's performance history. To do that, Windows has to let Daisy's App read its graphics events (the way PresentMon and the NVIDIA app's FrameView do), which takes membership of the \"Performance Log Users\" group, or running as administrator.")));
        Children.Add(Text(T("Allow adds you to that group (Windows asks first), once. Windows only gives the permission to new sign-ins, so after that sign out of Windows and back in, or restart the PC.")));
        allow.Click += (_, _) => Allow();
        Children.Add(allow);
        status.Margin = new Thickness(0, 10, 0, 14);
        Children.Add(status);

        var overlay = new CheckBox { Content = T("Show the FPS overlay in games"), IsChecked = service.Settings.OverlayVisible, Margin = new Thickness(0, 4, 0, 4) };
        overlay.Checked += (_, _) => SetOverlay(true);
        overlay.Unchecked += (_, _) => SetOverlay(false);
        Children.Add(overlay);
        var note = Text(T("It starts off. You can switch it on or off at any time in Video → Overlay, or with its shortcut."));
        note.SetResourceReference(StyleProperty, "SecondaryText");
        Children.Add(note);
        ShowState();
    }

    private void ShowState()
    {
        bool pending = service.Settings.PermissionPending;
        allow.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
        status.Text = pending
            ? T("Allowed. Now sign out of Windows and back in (or restart the PC) to finish; frames are counted from then on.")
            : T("Not allowed yet: frames aren't counted, so the overlay, benchmarks and history stay empty.");
    }

    private void Allow()
    {
        string? error = Permission.AddToPerformanceLogUsers();
        if (error != null) { status.Text = error; return; }
        service.Settings.PermissionPending = true;
        service.Settings.Save();
        ShowState();
    }

    private void SetOverlay(bool on)
    {
        if (service.Settings.OverlayVisible == on) return;
        service.Settings.OverlayVisible = on;
        service.ApplyOverlaySettings();
    }

    private static TextBlock Text(string s) => new() { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13.5 };
}
