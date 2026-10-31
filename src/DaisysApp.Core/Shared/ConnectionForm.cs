using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DaisysApp.Shared;

/// <summary>
/// What a form for connecting to a server has whatever the server is (Zabbix, Home Assistant): label / field rows,
/// Test connection and its result. Every change is saved straight away; the connection is made again a moment after
/// the last one. Used on the Settings page, the setup wizard's step and the tab before it's set up. A form adds its
/// rows, then calls <see cref="Finish"/>.
/// </summary>
public abstract class ConnectionForm : StackPanel
{
    private readonly Grid rows = new();
    private readonly Button test = new() { Content = T("Test connection"), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140, Margin = new Thickness(0, 4, 0, 0) };
    private readonly DispatcherTimer reconnect = new() { Interval = TimeSpan.FromSeconds(1.5) };
    protected readonly TextBlock Status = Ui.Note("", 8);
    /// <summary>The result of the last test, until something changes.</summary>
    protected string? Tested { get; private set; }
    /// <summary>The fields are being filled in from the settings: their changes are not the user's.</summary>
    protected bool Loading { get; set; } = true;

    /// <summary>Raised after a successful test.</summary>
    public event Action? Connected;

    protected ConnectionForm()
    {
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    /// <summary>A row: the label (none if empty) and the field; returns the label, for a row that comes and goes.</summary>
    protected TextBlock Row(string label, FrameworkElement field, string? tip = null)
    {
        int r = rows.RowDefinitions.Count;
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 8) };
        if (label.Length == 0) l.Visibility = Visibility.Collapsed;
        field.Margin = new Thickness(0, 0, 0, 8);
        field.HorizontalAlignment = HorizontalAlignment.Left;
        if (tip != null) field.ToolTip = tip;
        AutomationProperties.SetName(field, label.Length > 0 ? label : tip ?? "");
        Grid.SetRow(l, r);
        Grid.SetRow(field, r);
        Grid.SetColumn(field, 1);
        rows.Children.Add(l);
        rows.Children.Add(field);
        return l;
    }

    /// <summary>Call last in the form's constructor: the rows are in, the fields filled.</summary>
    protected void Finish()
    {
        Children.Add(rows);
        Children.Add(test);
        Children.Add(Status);
        Fill();
        reconnect.Tick += (_, _) => { reconnect.Stop(); Reconnect(); };
        test.Click += async (_, _) => await TestAsync();
        Loaded += (_, _) => { Watch(false); Watch(true); Fill(); };
        Unloaded += (_, _) =>
        {
            Watch(false);
            if (reconnect.IsEnabled) { reconnect.Stop(); Reconnect(); }
        };
    }

    /// <summary>The settings as they are, each time it shows (another copy of the form may have changed them).</summary>
    private void Fill()
    {
        if (IsKeyboardFocusWithin) return;
        Loading = true;
        FillFields();
        ShowState();
        Loading = false;
    }

    /// <summary>A field changed: <paramref name="apply"/> puts it in the settings, and the test must be done again.</summary>
    protected void Changed(Action apply)
    {
        if (Loading) return;
        apply();
        Unconfirm();
        Tested = null;
        reconnect.Stop();
        reconnect.Start();
        ShowState();
    }

    /// <summary>Connects with what's filled in; on success it's marked as set up and the service connects.</summary>
    public async Task<bool> TestAsync()
    {
        test.IsEnabled = false;
        Status.Text = T("Connecting…");
        try
        {
            Tested = await Connect();
            Status.Text = Tested;
            reconnect.Stop();
            Reconnect();
            Connected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Tested = OwnError(ex) ? ex.Message : F("Couldn't connect: {0}", ex.Message);
            Status.Text = Tested;
            return false;
        }
        finally { test.IsEnabled = true; }
    }

    /// <summary>The fields from the settings.</summary>
    protected abstract void FillFields();
    /// <summary>Shows how the connection is in <see cref="Status"/> (<see cref="Tested"/>, if there is a result).</summary>
    protected abstract void ShowState();
    /// <summary>Starts or stops following the service's changes.</summary>
    protected abstract void Watch(bool on);
    /// <summary>The settings aren't known to work any more: saves them so.</summary>
    protected abstract void Unconfirm();
    /// <summary>Tests what's filled in and saves it as set up; returns what to say. Throws if it can't.</summary>
    protected abstract Task<string> Connect();
    protected abstract void Reconnect();
    /// <summary>Whether the error is the client's own, its message already a sentence for the user.</summary>
    protected abstract bool OwnError(Exception ex);
}
