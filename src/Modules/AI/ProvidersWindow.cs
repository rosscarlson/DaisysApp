using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DaisysApp.Theming;

namespace DaisysApp.Applets.AI;

/// <summary>
/// The AI providers besides Claude Code: each an OpenAI-compatible API the built-in agent talks to. Add one from a
/// preset (Google Gemini, OpenAI, OpenRouter, Ollama or LM Studio on this PC, or any other), with its address, API key
/// (kept encrypted for this Windows user), model (Fetch models lists them), context window, longest reply, temperature
/// and extra instructions; Test asks it for a one-word reply.
/// </summary>
internal sealed class ProvidersWindow : Window
{
    private readonly AiSettings settings;
    private readonly ListBox list = new() { Width = 230 };
    private readonly ContentControl editor = new();

    public ProvidersWindow(AiSettings settings)
    {
        this.settings = settings;
        Title = T("AI providers");
        Width = 900;
        Height = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

        var add = new Button { Content = T("Add…"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 6, 0) };
        var menu = new ContextMenu();
        foreach (var preset in Provider.Presets)
        {
            var item = new MenuItem { Header = preset.Name };
            item.Click += (_, _) => Add(preset.Preset);
            menu.Items.Add(item);
        }
        add.Click += (_, _) => { menu.PlacementTarget = add; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        var remove = new Button { Content = T("Remove"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0) };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: Provider p }) return;
            if (MessageBox.Show(this, F("Remove {0}? Tabs using it stop working until they pick another.", p.Name), Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            settings.Providers.Remove(p);
            settings.Save();
            Fill(null);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(list);
        list.SelectionChanged += (_, _) => Edit((list.SelectedItem as ListBoxItem)?.Tag as Provider);

        var intro = new TextBlock
        {
            Text = T("Claude Code uses your Claude sign-in and needs nothing here. These are the others: any service with an OpenAI-compatible API. A tab uses one when you pick it as it starts (Which AI?), and works the same way: it reads, edits and runs things in its folder, asking first as its permissions say."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        intro.SetResourceReference(StyleProperty, "SecondaryText");
        var close = new Button { Content = T("Close"), Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.SetResourceReference(StyleProperty, "AccentButton");
        close.Click += (_, _) => Close();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);
        var dock = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(intro);
        dock.Children.Add(close);
        dock.Children.Add(grid);
        Content = dock;
        Closed += (_, _) => settings.Save();
        Fill(settings.Providers.FirstOrDefault());
    }

    private void Fill(Provider? select)
    {
        list.Items.Clear();
        foreach (var p in settings.Providers)
            list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"{p.Name}\n{p.Model}", TextWrapping = TextWrapping.Wrap }, Tag = p, Padding = new Thickness(8, 6, 8, 6) });
        list.SelectedItem = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == select) ?? (list.Items.Count > 0 ? list.Items[0] : null);
        if (list.Items.Count == 0) Edit(null);
    }

    private void Add(string preset)
    {
        var x = Provider.Presets.First(p => p.Preset == preset);
        var p = new Provider { Preset = preset, Name = x.Name, BaseUrl = x.Url, Model = x.Model, ContextWindow = x.Context };
        settings.Providers.Add(p);
        settings.Save();
        Fill(p);
    }

    private void Edit(Provider? p)
    {
        if (p == null)
        {
            editor.Content = new TextBlock { Text = T("Add one: Add… then pick the kind (Gemini needs a free API key from Google AI Studio; Ollama and LM Studio run models on this PC with no key)."), TextWrapping = TextWrapping.Wrap };
            return;
        }
        var preset = Provider.Presets.FirstOrDefault(x => x.Preset == p.Preset);
        var s = new StackPanel();
        void Changed() { settings.Save(); }
        TextBox Text(string label, string value, Action<string> set, string? tip = null, bool multi = false)
        {
            var box = new TextBox { Text = value, ToolTip = tip, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 80 : 0 };
            box.TextChanged += (_, _) => { set(box.Text); Changed(); };
            s.Children.Add(Row(label, box));
            return box;
        }
        Text(T("Name"), p.Name, v => p.Name = v);
        Text(T("Address (base URL)"), p.BaseUrl, v => p.BaseUrl = v.Trim(), T("The API's base, before /chat/completions"));

        var key = new PasswordBox { Password = p.ApiKey, ToolTip = T("Kept encrypted for this Windows user") };
        key.PasswordChanged += (_, _) => { p.ApiKey = key.Password.Trim(); Changed(); };
        var keyRow = new DockPanel();
        if (preset.KeyUrl is { } url)
        {
            var get = new Button { Content = T("Get a key"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
            get.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            DockPanel.SetDock(get, Dock.Right);
            keyRow.Children.Add(get);
        }
        keyRow.Children.Add(key);
        s.Children.Add(Row(T("API key"), keyRow));

        var model = new ComboBox { IsEditable = true, Text = p.Model };
        foreach (var m in p.Models) model.Items.Add(m);
        model.LostFocus += (_, _) => { p.Model = model.Text.Trim(); Changed(); };
        model.SelectionChanged += (_, _) => { if (model.SelectedItem is string m) { p.Model = m; Changed(); } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        var fetch = new Button { Content = T("Fetch models"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        fetch.Click += async (_, _) =>
        {
            status.Text = T("Asking it for its models…");
            try
            {
                p.Models = await ApiAgent.ModelsAsync(p);
                model.Items.Clear();
                foreach (var m in p.Models) model.Items.Add(m);
                Changed();
                status.Text = P(p.Models.Count, "{0} model: pick it above.", "{0} models: pick one above.");
                model.IsDropDownOpen = true;
            }
            catch (Exception ex) { status.Text = F("It didn't answer: {0}", ex.Message); }
        };
        var modelRow = new DockPanel();
        DockPanel.SetDock(fetch, Dock.Right);
        modelRow.Children.Add(fetch);
        modelRow.Children.Add(model);
        s.Children.Add(Row(T("Model"), modelRow));

        Text(T("Context window (tokens)"), p.ContextWindow.ToString(), v => { if (int.TryParse(v, out int n) && n > 0) p.ContextWindow = n; }, T("How much the model can read at once: for the dashboard's context ring"));
        Text(T("Longest reply (tokens)"), p.MaxTokens.ToString(), v => { if (int.TryParse(v, out int n) && n >= 0) p.MaxTokens = n; }, T("0: the model's own limit"));
        Text(T("Temperature"), p.Temperature?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "", v => p.Temperature = double.TryParse(v, out double t) ? t : null, T("Empty: the model's own. Lower is steadier, higher more varied."));
        Text(T("Extra instructions"), p.Instructions, v => p.Instructions = v, T("Added to what the agent is told at the start of each session"), multi: true);

        var test = new Button { Content = T("Test"), Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        test.SetResourceReference(StyleProperty, "AccentButton");
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            status.Text = T("Asking it for a one-word reply…");
            var sw = Stopwatch.StartNew();
            try { status.Text = F("It answered “{0}” in {1:0.0} s.", await TestAsync(p), sw.Elapsed.TotalSeconds); }
            catch (Exception ex) { status.Text = F("It didn't work: {0}", ex.Message); }
            test.IsEnabled = true;
        };
        s.Children.Add(test);
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        s.Children.Add(status);
        editor.Content = s;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private static async Task<string> TestAsync(Provider p)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, p.BaseUrl.TrimEnd('/') + "/chat/completions");
        if (p.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new("Bearer", key);
        var body = new JsonObject
        {
            ["model"] = p.Model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Reply with just the word OK." }),
        };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var r = await Http.SendAsync(request);
        string text = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)r.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
        string answer = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
        answer = System.Text.RegularExpressions.Regex.Replace(answer, "<think>[\\s\\S]*?</think>", "").Trim();
        return answer.Length > 80 ? answer[..80] + "…" : answer;
    }

    private static FrameworkElement Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }
}
