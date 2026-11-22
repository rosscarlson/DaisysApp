using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DaisysApp.Applets.SimEffects.Profiles;
using DaisysApp.Shared;
using DaisysApp.Shared.Hotkeys;
using DaisysApp.Shell;

namespace DaisysApp.Applets.SimEffects.Recordings;

/// <summary>
/// SimEffects → Recordings (since 4.20.0): the sessions recorded with Record (beside Play, at the right of the sub-tabs:
/// <see cref="RecordButton"/>, <see cref="SimRecorder"/>), played back as if the game were running
/// (<see cref="SimPlayback"/>) with the video of it, so the Shaker and the rest can be tuned while watching.
/// </summary>
public sealed class RecordingsApplet : IApplet
{
    private readonly RecordingsView view = new();

    public RecordingsApplet()
    {
        Current = this;
        SimRecorder.Follow();
    }

    /// <summary>The one made (null while Recordings is switched off: no Record button).</summary>
    internal static RecordingsApplet? Current { get; private set; }

    public FrameworkElement View => view;
    public FrameworkElement? SettingsView => null;

    public void Start() => view.Fill();
    public void SaveSettings() { }

    public void Dispose()
    {
        SimRecorder.Stop("the app closing");
        SimPlayback.End("the app closing");
        view.Close();
        if (Current == this) Current = null;
    }

    private static Hotkey? recordKey;

    /// <summary>Record / Stop, right of Play, and its shortcut's button.</summary>
    public static (Button Record, Button Key) RecordButton()
    {
        recordKey ??= Hotkeys.Add(new HotkeySpec
        {
            Id = "SimEffects.Record", Module = T("SimEffects"), Name = T("Record / Stop"),
            Get = () => SimProfiles.Settings.RecordKey, Set = v => { SimProfiles.Settings.RecordKey = v; SimProfiles.Save(); },
        }, () => SimRecorder.Toggle("shortcut"));
        var glyph = new TextBlock { FontSize = 12 };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var b = new Button { Content = glyph, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0) };
        ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, _) => SimRecorder.Toggle("Record");
        void Show()
        {
            bool on = SimRecorder.Recording;
            string? cant = on ? null : SimRecorder.CantRecord;
            glyph.Text = on ? "" : "";
            if (on) b.SetResourceReference(Control.BackgroundProperty, "DangerBrush");
            else b.Background = Brushes.White;
            glyph.Foreground = on ? Brushes.White : Brushes.Crimson;
            b.IsEnabled = cant == null;
            string name = on ? T("Stop recording") : T("Record the session");
            b.ToolTip = cant == null ? name : $"{name}: {cant}";
            AutomationProperties.SetName(b, name);
        }
        SimRecorder.Changed += Show;
        Show();
        return (b, HotkeyWindow.Button(T("Record / Stop shortcut"), () => recordKey));
    }
}

/// <summary>The recordings in a list, and the one picked beside it: its video, playing and lining it up.</summary>
internal sealed class RecordingsView : UserControl
{
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, ItemContainerStyle = RowStyle };
    private readonly TextBlock none = Ui.Note(T("No recordings yet"));
    private readonly RecordingPlayer player = new();
    private readonly TextBlock title = Ui.Header("");
    private readonly Button rename, folder, delete;
    private bool filling;

    public RecordingsView()
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(list, T("Recordings"));
        list.SelectionChanged += (_, _) => { if (!filling) Pick(Selected); };
        list.MouseDoubleClick += (_, e) =>
        {
            if (Selected is { } r && ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is ListBoxItem) player.Play(r);
        };
        var left = new DockPanel();
        var header = Ui.Header(T("Recordings"));
        DockPanel.SetDock(header, Dock.Top);
        left.Children.Add(header);
        var rows = new Grid();
        rows.Children.Add(list);
        rows.Children.Add(none);
        left.Children.Add(rows);

        rename = SimUi.Button(T("Rename"), Rename);
        folder = SimUi.Button(T("Show Video"), () => { if (Selected is { HasVideo: true } r) Ui.ShowInExplorer(r.Video); });
        delete = SimUi.Confirm(T("Delete"), Delete, T("Its video too"));
        var right = new DockPanel();
        var head = Ui.HeaderRow(title, rename, folder, delete);
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        right.Children.Add(Analysis.Sessions.SessionsView.PopOut(T("Recording"), player, null, null, 960, 680));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(SimUi.Card(left, 0));
        var card = SimUi.Card(right, 0);
        Grid.SetColumn(card, 2);
        grid.Children.Add(card);
        Content = grid;

        SimRecorder.Saved += r => { Fill(r.Id); };
        SimPlayback.Changed += () =>
        {
            // (its length, when the app ended while it recorded, is known once it's played)
            if (SimPlayback.Current is { } r && list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == r) is { } item) item.Content = Row(r);
        };
        Pick(null);
    }

    private static readonly Style RowStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Bg" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}" Margin="0,0,0,2">
                            <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowHoverBrush}"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource RowSelectedBrush}"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        """);

    private SimRecording? Selected => (list.SelectedItem as ListBoxItem)?.Tag as SimRecording;

    /// <summary>The list again, the one with <paramref name="id"/> (or the one picked) picked.</summary>
    public void Fill(string? id = null)
    {
        // (the one playing stays the one it is)
        var playing = SimPlayback.Current;
        id = playing?.Id ?? id ?? Selected?.Id;
        filling = true;
        list.Items.Clear();
        foreach (var found in SimRecording.All())
        {
            var r = playing?.Id == found.Id ? playing : found;
            var item = new ListBoxItem { Content = Row(r), Tag = r, Padding = new Thickness(8, 6, 8, 6) };
            AutomationProperties.SetName(item, r.Name);
            list.Items.Add(item);
            if (r.Id == id) list.SelectedItem = item;
        }
        if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
        filling = false;
        none.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Pick(Selected);
    }

    private static FrameworkElement Row(SimRecording r)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string length = r.Seconds > 0 ? $"{(int)(r.Seconds / 60)}:{(int)r.Seconds % 60:00}" : "–";
        var lines = new List<string> { $"{r.Started:g} · {length}" };
        var game = r.Edition is { } e && CarNames.Versions(r.Game).FirstOrDefault(v => CarNames.Key(r.Game, v.Version) == e).Title is { } t ? t : Games.Title(r.Game);
        lines.Add(r.Car.Length > 0 ? $"{game} · {r.Car}" : game);
        foreach (var line in lines)
        {
            var text = Ui.Secondary(line);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            p.Children.Add(text);
        }
        return p;
    }

    private void Pick(SimRecording? r)
    {
        if (SimPlayback.Current is { } c && c != r) SimPlayback.End("another one picked");
        title.Text = r?.Name ?? "";
        rename.IsEnabled = delete.IsEnabled = r != null;
        folder.IsEnabled = r?.HasVideo == true;
        player.Show(r);
    }

    private void Rename()
    {
        if (Selected is not { } r || Ui.Prompt(this, T("Rename"), T("Name"), r.Name, T("Rename")) is not { Length: > 0 } name) return;
        r.Name = name;
        r.Save();
        title.Text = name;
        if (list.SelectedItem is ListBoxItem item) { item.Content = Row(r); AutomationProperties.SetName(item, name); }
    }

    private void Delete()
    {
        if (Selected is not { } r) return;
        if (SimPlayback.Current == r) SimPlayback.End("deleted");
        player.Show(null);
        r.Delete();
        Fill();
    }

    public void Close() => player.Show(null);
}
