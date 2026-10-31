using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace DaisysApp.Applets.Tools.Notepad;

/// <summary>
/// Settings → Tools → Notepad: the Notepad as Windows' default app for text files (the types ticked). <b>Make
/// default…</b> notes each type's app first (once, so it's what was there before Daisy's App), lists the Notepad for
/// them and opens Windows' Default apps at its page; <b>Restore</b> takes it off them, and each type whose app
/// Windows didn't put back has <b>Choose…</b> (Windows' Open with) beside the app it had.
/// </summary>
internal sealed class DefaultAppView : UserControl
{
    private readonly NotepadSettings settings;
    private readonly StackPanel rows = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock result = Ui.Secondary();

    public DefaultAppView(NotepadSettings settings)
    {
        this.settings = settings;
        if (settings.DefaultTypes.Count == 0) settings.DefaultTypes = DefaultApp.Usual.ToList();
        var card = new StackPanel();
        card.Children.Add(Ui.Header(T("Default app")));
        var types = new WrapPanel();
        foreach (var t in DefaultApp.Types)
        {
            var c = new CheckBox { Content = t, IsChecked = settings.DefaultTypes.Contains(t), Margin = new Thickness(0, 0, 14, 4) };
            c.Click += (_, _) =>
            {
                settings.DefaultTypes.Remove(t);
                if (c.IsChecked == true) settings.DefaultTypes.Add(t);
                settings.Save();
                Refresh();
            };
            types.Children.Add(c);
        }
        card.Children.Add(types);
        var make = Ui.IconButton("\uE8A5", T("Make default…"), T("Lists the Notepad for these types and opens Windows' Default apps, to pick it for each one"), (_, _) => Make(), accent: true);
        var restore = Ui.IconButton("\uE7A7", T("Restore"), T("Takes the Notepad off them; Choose… puts back an app Windows didn't"), (_, _) => Restore());
        restore.Margin = new Thickness(8, 0, 0, 0);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { make, restore, result } });
        card.Children.Add(rows);
        Content = Ui.Card(card);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private void Make()
    {
        try
        {
            // (each type's app before Daisy's App, kept until it's back)
            foreach (var t in settings.DefaultTypes)
                if (!settings.TypesBefore.ContainsKey(t) && !DefaultApp.IsOurs(t)) settings.TypesBefore[t] = DefaultApp.NameOf(t);
            settings.Save();
            DefaultApp.Register(settings.DefaultTypes);
            DefaultApp.OpenWindowsSettings();
            Show(T("In Windows: pick it for each type"), false);
        }
        catch (Exception ex) { Show(ex.Message, true); }
        Refresh();
    }

    private void Restore()
    {
        try
        {
            DefaultApp.Unregister();
            Show(T("Done"), false);
        }
        catch (Exception ex) { Show(ex.Message, true); }
        Refresh();
    }

    private void Show(string text, bool bad)
    {
        result.Text = text;
        result.SetResourceReference(TextBlock.ForegroundProperty, bad ? "ErrorTextBrush" : "TextSecondaryBrush");
    }

    /// <summary>Each type's app now, and before (with Choose… while it isn't back).</summary>
    public void Refresh()
    {
        rows.Children.Clear();
        bool registered = DefaultApp.Registered;
        // (when the Notepad isn't listed, a type whose app is back needn't be remembered)
        if (!registered)
            foreach (var (t, before) in settings.TypesBefore.ToList())
                if (DefaultApp.NameOf(t) == before || (before.Length == 0 && !DefaultApp.IsOurs(t))) settings.TypesBefore.Remove(t);
        settings.Save();
        foreach (var t in DefaultApp.Types.Where(t => settings.DefaultTypes.Contains(t) || settings.TypesBefore.ContainsKey(t)))
        {
            string now = DefaultApp.IsOurs(t) && registered ? DefaultApp.AppName : DefaultApp.NameOf(t);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock { Text = t, Width = 60, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = now.Length > 0 ? now : "—", MinWidth = 180, VerticalAlignment = VerticalAlignment.Center });
            if (settings.TypesBefore.TryGetValue(t, out var was) && was != now)
            {
                var before = Ui.Secondary(F("Before: {0}", was.Length > 0 ? was : "—"));
                before.VerticalAlignment = VerticalAlignment.Center;
                before.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(before);
                if (!registered)
                {
                    var choose = new Button { Content = T("Choose…"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = T("Windows' Open with, for this type") };
                    choose.Click += (_, _) =>
                    {
                        DefaultApp.OpenWith(t, Window.GetWindow(this) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero);
                        Refresh();
                    };
                    row.Children.Add(choose);
                }
            }
            rows.Children.Add(row);
        }
    }
}
