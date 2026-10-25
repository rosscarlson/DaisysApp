using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DaisysApp.Shared;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>
/// The Fire TV sub-tab: the device (an address, or Find on the network), its apps by group with a tick for each that
/// stays on (on a TV never changed here, what's advised to go and the user's apps not kept start unticked), Apply (the
/// TV made as ticked: unticked ones removed, ticked removed ones put back) / Restore All, tweaks (ad-blocking DNS,
/// faster animations) applied as they're ticked, and what was done; the TV's prompt not showing opens the
/// <see cref="PromptWizard"/>.
/// </summary>
internal sealed class FireTvView : Grid
{
    private const string AdGuard = "dns.adguard-dns.com";
    private static readonly string[] animations = { "window_animation_scale", "transition_animation_scale", "animator_duration_scale" };
    private static readonly Logging.Logger log = Logging.Log.Here;

    private readonly FireTvSettings settings;
    private FireTvDevice? device;
    private List<TvApp> apps = new();
    /// <summary>Ticked: stays (or comes back) on the TV.</summary>
    private readonly HashSet<string> on = new();
    private bool busy, filling;

    private readonly TextBox address = new() { MinWidth = 170, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button connect, find, apply, restoreAll, reboot, disconnect;
    private readonly StackPanel foundList = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock deviceLine = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock deviceMore = Secondary("");
    private readonly StackPanel deviceButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox dns = new() { Content = T("Ad-blocking DNS"), ToolTip = T("AdGuard DNS as the TV's Private DNS"), Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox fast = new() { Content = T("Faster animations"), ToolTip = T("Animations at half length"), Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox showSystem = new() { Content = T("Show System"), ToolTip = T("Fire OS's other parts (ones the TV needs aren't listed)"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly StackPanel appList = new();
    private readonly TextBox done = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Padding = new Thickness(0), MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12,
    };
    private readonly CancellationTokenSource closing = new();

    public FireTvView(FireTvSettings settings)
    {
        this.settings = settings;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        address.Text = settings.Address;
        AutomationProperties.SetName(address, T("Address"));
        address.KeyDown += (_, e) => { if (e.Key == Key.Enter) Connect(address.Text); };
        connect = Button(T("Connect"), (_, _) => Connect(address.Text), accent: true, tip: T("Its IP address, from Settings → My Fire TV → About → Network"));
        find = Button(T("Find"), (_, _) => Find(), tip: T("Look for Fire TVs on this network"));
        reboot = Button(T("Restart TV"), (_, _) => Run(async () =>
        {
            Say(T("Restarting the TV"));
            await device!.RebootAsync();
            Disconnected(T("Restarting: connect again in a minute."));
        }));
        disconnect = Button(T("Disconnect"), (_, _) => Disconnected(""));
        reboot.Margin = new Thickness(0);
        deviceButtons.Children.Add(reboot);
        deviceButtons.Children.Add(disconnect);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        line.Children.Add(address);
        line.Children.Add(connect);
        line.Children.Add(find);
        var tv = new StackPanel();
        tv.Children.Add(Header(T("Fire TV")));
        tv.Children.Add(line);
        tv.Children.Add(foundList);
        tv.Children.Add(status);
        tv.Children.Add(deviceLine);
        tv.Children.Add(deviceMore);
        tv.Children.Add(deviceButtons);

        // (Checked / Unchecked, not Click: also from UI Automation; `filling` while the TV's values are shown)
        void Dns(object s, RoutedEventArgs e) => Tweak(dns.IsChecked == true, new[] { ("global", "private_dns_specifier", AdGuard), ("global", "private_dns_mode", "hostname") }, T("Ad-blocking DNS"));
        void Fast(object s, RoutedEventArgs e) => Tweak(fast.IsChecked == true, animations.Select(a => ("global", a, "0.5")).ToArray(), T("Faster animations"));
        dns.Checked += Dns;
        dns.Unchecked += Dns;
        fast.Checked += Fast;
        fast.Unchecked += Fast;
        var tweaks = new StackPanel();
        tweaks.Children.Add(Header(T("Tweaks")));
        tweaks.Children.Add(dns);
        tweaks.Children.Add(fast);

        var doneCard = new StackPanel();
        doneCard.Children.Add(Header(T("Done")));
        doneCard.Children.Add(done);

        var left = new StackPanel();
        left.Children.Add(Card(tv));
        left.Children.Add(Card(tweaks, new Thickness(0, 12, 0, 0)));
        left.Children.Add(Card(doneCard, new Thickness(0, 12, 0, 0)));
        Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        apply = new Button { Content = T("Apply"), ToolTip = T("Unticked apps removed, ticked ones put back") };
        apply.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(apply, T("Apply"));
        // (Confirm? first only when one of the user's own apps would be deleted: those can't come back)
        ConfirmButton.Arm(apply, Apply, skip: () => !Changes().Any(a => a.Theirs && a.State == AppState.Installed));
        restoreAll = ConfirmButton.Make(T("Restore All"), RestoreAll, T("Put back everything removed or changed here"));
        restoreAll.Margin = new Thickness(8, 0, 0, 0);
        showSystem.Checked += (_, _) => FillApps();
        showSystem.Unchecked += (_, _) => FillApps();
        var top = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(showSystem);
        actions.Children.Add(apply);
        actions.Children.Add(restoreAll);
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions);
        top.Children.Add(Header(T("Apps")));
        var right = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        right.Children.Add(new ScrollViewer { Content = appList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        var appsCard = Card(right, new Thickness(12, 0, 0, 0));
        appsCard.VerticalAlignment = VerticalAlignment.Stretch;
        SetColumn(appsCard, 1);
        Children.Add(appsCard);

        Show();
    }

    // ---- connecting

    private void Connect(string to)
    {
        to = to.Trim();
        if (to.Length == 0 || busy) return;
        device?.Dispose();
        device = null;
        address.Text = to;
        Run(async () =>
        {
            Status(T("Connecting…"));
            var d = await FireTvDevice.ConnectAsync(to, () => Dispatcher.Invoke(() =>
                Status(T("On the TV: tick Always allow from this computer, then OK."))), closing.Token);
            device = d;
            settings.Address = to;
            settings.Save();
            Status(d.IsAmazon ? "" : T("This isn't an Amazon device: check what you remove."), !d.IsAmazon);
            Say(F("Connected to {0} ({1})", d.Name, d.ModelName));
            await Load();
        });
    }

    private void Find()
    {
        Run(async () =>
        {
            Status(T("Looking…"));
            foundList.Children.Clear();
            var list = await Finder.FindAsync(closing.Token);
            foreach (var f in list)
            {
                var ip = f.Address.ToString();
                var text = (f.Name.Length > 0 ? f.Name + " · " : "") + ip + " · " + (f.Adb ? T("ADB on") : T("ADB off"));
                var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
                AutomationProperties.SetName(b, text);
                b.Click += (_, _) => Connect(ip);
                foundList.Children.Add(b);
            }
            Status(list.Count == 0 ? T("None found: type its IP address.") : list.Any(f => f.Adb) ? "" : T("Turn on ADB Debugging on the TV (Settings → My Fire TV → Developer Options), then Find again."));
        });
    }

    private void Disconnected(string why)
    {
        device?.Dispose();
        device = null;
        apps.Clear();
        Status(why);
        Show();
    }

    private async Task Load()
    {
        apps = await device!.AppsAsync(closing.Token);
        // (ticks show the TV as it is; on one never changed here, what's advised to go starts unticked, but your kept apps)
        bool fresh = !(settings.Devices.TryGetValue(device.Serial, out var known) && known.Applied);
        on.Clear();
        foreach (var a in apps)
        {
            if (a.State != AppState.Installed) continue;
            bool keep = a.Theirs ? settings.Keep.Contains(a.Package) || !fresh : !fresh || a.Advice != Advice.Remove;
            if (keep) on.Add(a.Package);
        }
        var mode = await device.GetSettingAsync("global", "private_dns_mode");
        var host = await device.GetSettingAsync("global", "private_dns_specifier");
        filling = true;
        dns.IsChecked = mode == "hostname" && host == AdGuard;
        bool allFast = true;
        foreach (var a in animations) allFast &= await device.GetSettingAsync("global", a) == "0.5";
        fast.IsChecked = allFast;
        filling = false;
        log.Info($"{apps.Count} packages, {apps.Count(a => a.Theirs)} installed by the user, {Changes().Count} to change{(fresh ? " (advised)" : "")}");
        Show();
    }

    // ---- apps

    /// <summary>What Apply would change: installed but unticked (to remove), or ticked but removed / disabled (to put back).</summary>
    private List<TvApp> Changes() =>
        apps.Where(a => !a.Locked && (on.Contains(a.Package) ? a.State != AppState.Installed : a.State == AppState.Installed)).ToList();

    private void Apply()
    {
        var list = Changes();
        if (list.Count == 0 || device == null) return;
        Run(async () =>
        {
            var changes = settings.For(device.Serial, device.Name);
            changes.Applied = true;
            settings.Save();
            int n = 0;
            foreach (var a in list)
            {
                if (a.State == AppState.Installed)
                {
                    Status(F("Removing {0} ({1} of {2})…", a.Name, ++n, list.Count));
                    var (how, said) = await device.RemoveAsync(a, closing.Token);
                    if (how == null)
                    {
                        Say(F("Couldn't remove {0}: {1}", a.Name, said));
                        log.Warn($"Couldn't remove {a.Package}: {said}");
                        continue;
                    }
                    changes.Packages[a.Package] = how;
                    settings.Save();
                    Say(how == "disable" ? F("Disabled {0}", a.Name) : F("Removed {0}", a.Name));
                    log.Info($"{how} {a.Package}");
                }
                else
                {
                    Status(F("Putting back {0} ({1} of {2})…", a.Name, ++n, list.Count));
                    var (ok, said) = await device.RestoreAsync(a.Package, closing.Token);
                    if (ok)
                    {
                        Forget(a.Package);
                        Say(F("Put back {0}", a.Name));
                    }
                    else Say(F("Couldn't put back {0}: {1}", a.Name, said));
                    log.Info($"Restore {a.Package}: {(ok ? "done" : said)}");
                }
            }
            Status("");
            await Load();
        });
    }

    private void RestoreAll()
    {
        if (device == null || !settings.Devices.TryGetValue(device.Serial, out var changes)) return;
        Run(async () =>
        {
            foreach (var (p, how) in changes.Packages.ToList())
            {
                var name = Catalog.Find(p)?.Name ?? p;
                if (how == "gone")
                {
                    Say(F("Install {0} again from the Appstore", name));
                    changes.Packages.Remove(p);
                    continue;
                }
                var (ok, said) = await device.RestoreAsync(p, closing.Token);
                Say(ok ? F("Put back {0}", name) : F("Couldn't put back {0}: {1}", name, said));
                if (ok) changes.Packages.Remove(p);
                settings.Save();
            }
            foreach (var (key, was) in changes.Settings.ToList())
            {
                var at = key.IndexOf('/');
                await device.PutSettingAsync(key[..at], key[(at + 1)..], was, closing.Token);
                changes.Settings.Remove(key);
            }
            settings.Save();
            Say(T("Restored"));
            await Load();
        });
    }

    private void Forget(string package)
    {
        if (device != null && settings.Devices.TryGetValue(device.Serial, out var c) && c.Packages.Remove(package)) settings.Save();
    }

    private void Tweak(bool on, (string Space, string Key, string Value)[] values, string name)
    {
        if (filling || device == null) return;
        Run(async () =>
        {
            var changes = settings.For(device.Serial, device.Name);
            foreach (var (space, key, value) in values)
            {
                var id = space + "/" + key;
                if (on)
                {
                    if (!changes.Settings.ContainsKey(id)) changes.Settings[id] = await device.GetSettingAsync(space, key, closing.Token);
                    await device.PutSettingAsync(space, key, value, closing.Token);
                }
                else
                {
                    // (back to what it was before, or Android's own default)
                    var fallback = key.EndsWith("_scale") ? "1.0" : "null";
                    var was = changes.Settings.GetValueOrDefault(id) ?? fallback;
                    if (was == value) was = fallback;
                    await device.PutSettingAsync(space, key, was, closing.Token);
                    changes.Settings.Remove(id);
                }
            }
            settings.Save();
            Say(on ? F("{0}: on", name) : F("{0}: off", name));
            log.Info($"{name} {(on ? "on" : "off")}");
        });
    }

    // ---- showing

    private void Show()
    {
        bool on = device != null;
        deviceLine.Text = on ? device!.Name : "";
        deviceMore.Text = on ? string.Join(" · ", new[] { device!.ModelName, device.FireOs, device.Android.Length > 0 ? "Android " + device.Android : "" }.Where(s => s.Length > 0)) : "";
        deviceLine.Visibility = deviceMore.Visibility = deviceButtons.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        dns.IsEnabled = fast.IsEnabled = apply.IsEnabled = restoreAll.IsEnabled = showSystem.IsEnabled = on && !busy;
        connect.IsEnabled = find.IsEnabled = address.IsEnabled = reboot.IsEnabled = disconnect.IsEnabled = !busy;
        appList.IsEnabled = !busy;
        FillApps();
        ShowApply();
    }

    private static readonly string[] groupOrder = { Catalog.Yours, Catalog.Ads, Catalog.Amazon, Catalog.Updates, Catalog.System };

    private void FillApps()
    {
        appList.Children.Clear();
        if (device == null) return;
        bool system = showSystem.IsChecked == true;
        foreach (var g in groupOrder)
        {
            var rows = apps.Where(a => a.Group == g && !a.Locked && (g != Catalog.System || system))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (rows.Count == 0) continue;
            var head = Secondary(T(g));
            head.FontWeight = FontWeights.SemiBold;
            head.Margin = new Thickness(0, appList.Children.Count == 0 ? 0 : 14, 0, 4);
            appList.Children.Add(head);
            foreach (var a in rows) appList.Children.Add(Row(a));
        }
    }

    private FrameworkElement Row(TvApp a)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tip = a.Package + (a.Note.Length > 0 ? " · " + T(a.Note) : "");
        var box = new CheckBox { Content = T(a.Name), IsChecked = on.Contains(a.Package), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        if (a.State != AppState.Installed) box.Opacity = 0.7;
        void Changed(object s, RoutedEventArgs e)
        {
            if (box.IsChecked == true) on.Add(a.Package);
            else on.Remove(a.Package);
            // (your own apps: kept or not on every TV)
            if (a.Theirs)
            {
                settings.Keep.Remove(a.Package);
                if (box.IsChecked == true) settings.Keep.Add(a.Package);
                settings.Save();
            }
            ShowApply();
        }
        box.Checked += Changed;
        box.Unchecked += Changed;
        grid.Children.Add(box);

        var note = a.State switch
        {
            AppState.Removed => T("Removed"),
            AppState.Disabled => T("Disabled"),
            _ => a.Advice == Advice.Keep ? T("Keep") : a.Note.Length > 0 ? T(a.Note) : "",
        };
        var noteText = Secondary(note);
        noteText.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(noteText, 1);
        grid.Children.Add(noteText);
        return grid;
    }

    /// <summary>Apply's caption: how many it would change.</summary>
    private void ShowApply()
    {
        // (not while it asks Confirm?)
        if (apply.Content is not string c || !c.StartsWith(T("Apply"))) return;
        int n = device == null ? 0 : Changes().Count;
        apply.Content = n > 0 ? F("Apply ({0})", n) : T("Apply");
    }

    // ---- helpers

    private async void Run(Func<Task> work)
    {
        if (busy) return;
        busy = true;
        Show();
        try { await work(); }
        catch (AdbException ex)
        {
            Status(ex.Message, true);
            log.Warn(ex.Message);
            // (after this run ends, so its Connect can run)
            if (ex.NoPrompt) _ = Dispatcher.InvokeAsync(() => PromptWizard.Open(this, () => Connect(address.Text)));
            if (device?.Adb.Closed == true) { device = null; apps.Clear(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status(ex.Message, true);
            log.Error("Fire TV", ex);
        }
        finally
        {
            busy = false;
            Show();
        }
    }

    private void Status(string text, bool problem = false)
    {
        status.Text = text;
        status.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (problem) status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else status.ClearValue(TextBlock.ForegroundProperty);
    }

    private void Say(string text)
    {
        done.AppendText((done.Text.Length > 0 ? "\n" : "") + DateTime.Now.ToString("H:mm:ss") + "  " + text);
        done.ScrollToEnd();
    }

    public void Shutdown()
    {
        closing.Cancel();
        device?.Dispose();
    }

    private static Border Card(UIElement child, Thickness? margin = null)
    {
        var b = new Border { Child = child, VerticalAlignment = VerticalAlignment.Top };
        b.SetResourceReference(StyleProperty, "Card");
        if (margin is { } m) b.Margin = m;
        return b;
    }

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "CardHeader");
        return t;
    }

    private static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(StyleProperty, "SecondaryText");
        return t;
    }

    private static Button Button(string text, RoutedEventHandler click, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(b, text);
        if (accent) b.SetResourceReference(StyleProperty, "AccentButton");
        b.Click += click;
        return b;
    }
}
