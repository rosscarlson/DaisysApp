using System.Windows;
using System.Windows.Threading;
using DaisysApp.Theming;

namespace DaisysApp.Applets.Resizer;

/// <summary>Creates or edits a group (name and shortcut). Profiles join a group from the profile editor.</summary>
public partial class GroupEditorWindow : Window
{
    private readonly ResizerService service;
    private readonly ResizeGroup group;
    private readonly bool isNew;
    private readonly DispatcherTimer deleteConfirm = new() { Interval = TimeSpan.FromSeconds(3) };

    public GroupEditorWindow(ResizerService service, ResizeGroup? existing)
    {
        this.service = service;
        isNew = existing == null;
        group = existing?.Clone() ?? new ResizeGroup();
        InitializeComponent();
        Title = isNew ? "New group" : $"Group — {group.Name}";
        HeaderText.Text = isNew ? "New group" : group.Name;
        NameBox.Text = isNew ? "" : group.Name;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        deleteConfirm.Tick += (_, _) => { deleteConfirm.Stop(); DeleteButton.Content = "Delete"; };
        Shortcut.Attach(service.SuspendHotkeys, service.ResumeHotkeys);
        Shortcut.Value = group.Shortcut;
        Shortcut.Changed += UpdateShared;
        int members = service.Members(group).Count;
        MembersText.Text = isNew
            ? "To add profiles, open a profile and choose this group."
            : $"{members} profile{(members == 1 ? "" : "s")} in this group. To add or remove one, open the profile and change its group.";
        UpdateShared();
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => ThemeManager.ApplyTitleBar(this);

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        SaveButton.IsEnabled = NameBox.Text.Trim().Length > 0;

    private void UpdateShared()
    {
        var others = service.SharedWith(Shortcut.Value, group.Uuid);
        SharedText.Text = others.Count > 0
            ? $"Also used by: {string.Join(", ", others)}."
            : "Applies every profile in the group whose program is running.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var g = group.Clone();
        g.Name = NameBox.Text.Trim();
        g.Shortcut = Shortcut.Value;
        service.SaveGroup(g);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!deleteConfirm.IsEnabled)
        {
            DeleteButton.Content = "Click to confirm";
            deleteConfirm.Start();
            return;
        }
        deleteConfirm.Stop();
        service.DeleteGroup(group);
        DialogResult = true;
    }
}
