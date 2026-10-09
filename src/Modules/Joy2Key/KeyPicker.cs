using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DaisysApp.Applets.Joy2Key;

/// <summary>
/// Up to four keys pressed together. Click it and press them (all at once, like a shortcut), or right-click it for every
/// key there is, by group: the ones a keyboard may not have (number pad, F13–F24, media…), left / right Ctrl, Shift and
/// Alt, and mouse buttons.
/// </summary>
internal sealed class KeyPicker : Border
{
    public const int MaxKeys = 4;

    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly HashSet<int> down = new();
    private bool capturing, fresh;

    public List<string> Keys { get; private set; } = new();
    public event Action? Changed;

    public KeyPicker(IEnumerable<string> keys)
    {
        Keys = keys.ToList();
        Focusable = true;
        Cursor = Cursors.Hand;
        MinHeight = 32;
        Padding = new Thickness(10, 4, 4, 4);
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "ControlBrush");
        SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
        ToolTip = T("Click, then press the keys (up to 4 together). Right-click for every key, by group.");
        System.Windows.Automation.AutomationProperties.SetName(this, T("Keys"));

        var clear = new Button { Content = "", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = T("Clear"), Focusable = false };
        clear.SetResourceReference(Control.FontFamilyProperty, "IconFont");
        clear.Click += (_, _) => Set(new List<string>());
        System.Windows.Automation.AutomationProperties.SetName(clear, T("Clear"));
        var dock = new DockPanel();
        DockPanel.SetDock(clear, Dock.Right);
        dock.Children.Add(clear);
        dock.Children.Add(text);
        Child = dock;

        MouseLeftButtonDown += (_, e) => { Focus(); StartCapture(); e.Handled = true; };
        GotKeyboardFocus += (_, _) => StartCapture();
        LostKeyboardFocus += (_, _) => StopCapture();
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        ContextMenu = new ContextMenu();
        Show();
    }

    private void StartCapture()
    {
        capturing = true;
        fresh = true;
        down.Clear();
        SetResourceReference(BorderBrushProperty, "AccentBrush");
        Show();
    }

    private void StopCapture()
    {
        capturing = false;
        down.Clear();
        SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
        Show();
    }

    private void Show()
    {
        bool empty = Keys.Count == 0;
        text.Text = capturing && empty ? T("Press keys… (right-click for more)") : KeyCatalog.Describe(Keys);
        text.SetResourceReference(TextBlock.ForegroundProperty, empty ? "TextSecondaryBrush" : "TextBrush");
    }

    private void Set(List<string> keys)
    {
        Keys = keys;
        Show();
        Changed?.Invoke();
    }

    private static readonly PropertyInfo? extendedProperty = typeof(KeyEventArgs).GetProperty("IsExtendedKey", BindingFlags.Instance | BindingFlags.NonPublic);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!capturing) return;
        e.Handled = true;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        bool extended = extendedProperty?.GetValue(e) is true;
        var def = KeyCatalog.FromVk(vk, extended);
        if (def == null || !down.Add(vk)) return;
        var keys = fresh ? new List<string>() : Keys.ToList();
        fresh = false;
        if (!keys.Contains(def.Id) && keys.Count < MaxKeys) keys.Add(def.Id);
        Set(keys);
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        down.Remove(KeyInterop.VirtualKeyFromKey(key));
        // Print Screen only ever arrives as a key-up
        if (key == Key.Snapshot && fresh) { fresh = false; Set(new List<string> { "PrintScreen" }); }
        if (down.Count == 0) fresh = true; // the next key starts a new combination
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = T("Clear"), IsEnabled = Keys.Count > 0 };
        clear.Click += (_, _) => Set(new List<string>());
        menu.Items.Add(clear);
        var last = new MenuItem { Header = T("Remove the last key"), IsEnabled = Keys.Count > 0 };
        last.Click += (_, _) => Set(Keys.Take(Keys.Count - 1).ToList());
        menu.Items.Add(last);
        menu.Items.Add(new Separator());

        var typing = new MenuItem { Header = T("Letters, numbers and symbols") };
        foreach (var group in Enum.GetValues<KeyGroup>())
        {
            var item = new MenuItem { Header = KeyCatalog.GroupName(group) };
            foreach (var k in KeyCatalog.All.Where(k => k.Group == group))
            {
                var key = new MenuItem { Header = k.Name, IsCheckable = false, IsChecked = Keys.Contains(k.Id) };
                key.Click += (_, _) => Add(k.Id);
                item.Items.Add(key);
            }
            if (group is KeyGroup.Letters or KeyGroup.Digits or KeyGroup.Symbols) typing.Items.Add(item);
            else menu.Items.Add(item);
            if (group == KeyGroup.Numpad) menu.Items.Add(typing);
        }
        return menu;
    }

    private void Add(string id)
    {
        var keys = Keys.ToList();
        if (keys.Contains(id)) keys.Remove(id);
        else if (keys.Count < MaxKeys) keys.Add(id);
        else { keys[^1] = id; }
        Set(keys);
    }
}
