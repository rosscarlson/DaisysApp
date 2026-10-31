using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.SimEffects.Lights;

/// <summary>The shift lights' cards, the same on Notify and Fanatec (one set of settings): the shift points (the
/// profile in use's) and the cars with their redlines.</summary>
internal static class LightsCards
{
    /// <summary>The shift points of the SimEffects profile in use: drawn again when another is used.</summary>
    public static FrameworkElement ShiftPoints(LightsRunner runner, Action? changed = null)
    {
        var holder = new ContentControl { Content = ShiftPointsCard(runner, changed) };
        Profiles.SimProfiles.Changed += () => holder.Content = ShiftPointsCard(runner, changed);
        return holder;
    }

    private static Border ShiftPointsCard(LightsRunner runner, Action? changed)
    {
        var s = runner.Settings;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header(T("Shift Points")));
        string Pct(double v) => $"{v:0}%";
        void Set(Action a) { a(); s.Save(); changed?.Invoke(); }
        panel.Children.Add(SimUi.SliderRow(T("First Light"), 30, 99, 1, s.StartAt, Pct, v => Set(() => s.StartAt = (int)v), T("% of the redline")));
        panel.Children.Add(SimUi.SliderRow(T("Shift"), 50, 100, 1, s.ShiftAt, Pct, v => Set(() => s.ShiftAt = (int)v), T("% of the redline: every light on")));
        panel.Children.Add(SimUi.SliderRow(T("Flash"), 50, 105, 1, s.FlashAt, Pct, v => Set(() => s.FlashAt = (int)v), T("% of the redline: over-rev")));
        panel.Children.Add(SimUi.SliderRow(T("Flash Rate"), 1, 15, 1, s.FlashRate, v => F("{0}/s", v), v => Set(() => s.FlashRate = (int)v)));
        var own = new CheckBox { Content = T("iRacing's own shift lights"), IsChecked = s.UseGameLights, Margin = new Thickness(0, 8, 0, 0),
            ToolTip = T("Each car's own first light, shift and flash RPM, instead of the % above") };
        own.Click += (_, _) => Set(() => s.UseGameLights = own.IsChecked == true);
        panel.Children.Add(own);
        return SimUi.Card(panel);
    }

    /// <summary>The cars seen, newest first: each one's redline and where it came from, your own, and Forget.</summary>
    public sealed class Cars : ContentControl
    {
        private readonly LightsRunner runner;
        private readonly StackPanel rows = new();
        private string shownFor = "?";

        public Cars(LightsRunner runner)
        {
            this.runner = runner;
            var panel = new StackPanel();
            panel.Children.Add(Ui.HeaderRow(T("Cars"), null));
            panel.Children.Add(rows);
            Content = SimUi.Card(panel);
            IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
        }

        /// <summary>Drawn again when the car being driven changes (the tab's timer asks).</summary>
        public void Tick()
        {
            if (runner.State.CarId != shownFor && IsVisible) Render();
        }

        public void Render()
        {
            var state = runner.State;
            shownFor = state.CarId;
            rows.Children.Clear();
            List<(string Id, CarRedline Car)> cars;
            lock (runner.Settings.Gate) cars = runner.Settings.Cars.Select(c => (c.Key, c.Value)).OrderByDescending(c => c.Value.Seen).ToList();
            if (cars.Count == 0)
            {
                rows.Children.Add(Ui.Note(T("None yet: each car you drive is added")));
                return;
            }
            foreach (var (id, car) in cars.Take(40)) rows.Children.Add(Row(id, car, state));
        }

        private FrameworkElement Row(string id, CarRedline car, RevState state)
        {
            bool now = id == state.CarId;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = car.Name.Length > 0 ? car.Name : id, FontWeight = now ? FontWeights.Bold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = id });
            string what = now && state.Redline > 0
                ? F("Redline {0:N0} ({1})", state.Redline, From(state.From))
                : car.Learned > 0 ? F("Highest held {0:N0}", car.Learned) : T("Not at the limiter yet");
            name.Children.Add(Ui.Note(what));
            g.Children.Add(name);
            var yours = new TextBox { Width = 70, Text = car.Yours is { } y ? y.ToString("0") : "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
                ToolTip = T("Your redline for it (RPM); empty: worked out") };
            yours.LostKeyboardFocus += (_, _) =>
            {
                float? v = float.TryParse(yours.Text, out float f) && f > 500 ? f : null;
                if (v != car.Yours) { runner.SetYours(id, v); Render(); }
            };
            Grid.SetColumn(yours, 1);
            g.Children.Add(yours);
            var forget = SimUi.Confirm(T("Forget"), () => { runner.Forget(id); Render(); }, T("What it learned about this car"));
            forget.Margin = new Thickness(0);
            Grid.SetColumn(forget, 2);
            g.Children.Add(forget);
            return g;
        }

        private static string From(RedlineFrom f) => f switch
        {
            RedlineFrom.Yours => T("yours"),
            RedlineFrom.GameLights => T("iRacing's"),
            RedlineFrom.Learned => T("learned"),
            RedlineFrom.Game => T("the game's maximum"),
            _ => T("unknown"),
        };
    }

    /// <summary>The status line's text: what the lights follow, and the car's numbers.</summary>
    public static string Describe(LightsRunner runner)
    {
        var s = runner.State;
        if (runner.Demoing) return F("Demo: {0} · {1:N0} RPM", s.GearText, s.Rpm);
        if (!s.Live) return runner.Status;
        return F("{0} · {1} · {2:N0} RPM, shift at {3:N0}", runner.Status, s.Car.Length > 0 ? s.Car : T("a car"), s.Rpm, s.Shift);
    }
}
