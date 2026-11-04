using System.Windows;
using System.Windows.Threading;
using DaisysApp.Theming;
using DaisysApp.Shared;
using DaisysApp.Shared.Hotkeys;

namespace DaisysApp.Applets.Video.Resizer;

/// <summary>Creates or edits a group (name and shortcut). Profiles join a group from the profile editor.</summary>
public partial class GroupEditorWindow : Window
{
    private readonly ResizerService service;
    private readonly ResizeGroup group;
    private readonly bool isNew;

    public GroupEditorWindow(ResizerService service, ResizeGroup? existing)
    {
        this.service = service;
        isNew = existing == null;
        group = existing?.Clone() ?? new ResizeGroup();
        InitializeComponent();
        Title = isNew ? T("New group") : F("Group — {0}", group.Name);
        HeaderText.Text = isNew ? T("New group") : group.Name;
        NameBox.Text = isNew ? "" : group.Name;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Arm(DeleteButton, Delete);
        if (!isNew) ShortcutHost.Content = HotkeyWindow.Button(T("Shortcut"), () => service.GroupHotkey(group.Uuid));
        SharedText.Text = isNew ? T("Once it's saved: the joystick button on its row.") : T("Applies every profile in the group whose program is running.");
        int members = service.Members(group).Count;
        MembersText.Text = isNew
            ? T("To add profiles, open a profile and choose this group.")
            : P(members, "{0} profile in this group. To add or remove one, open the profile and change its group.", "{0} profiles in this group. To add or remove one, open the profile and change its group.");
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var g = group.Clone();
        g.Name = NameBox.Text.Trim();
        g.Shortcut = service.Data.Groups.FirstOrDefault(x => x.Uuid == g.Uuid)?.Shortcut;
        service.SaveGroup(g);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete()
    {
        service.DeleteGroup(group);
        DialogResult = true;
    }
}
