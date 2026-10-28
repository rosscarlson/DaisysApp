using System.Windows;
using System.Windows.Controls;

namespace DaisysApp.Applets.Support.Networking;

/// <summary>Traceroute → a hop double-clicked: its figures, then everything public about its address (<see cref="IpDetailsView"/>).</summary>
internal static class HopWindow
{
    public static void Open(Hop hop, FrameworkElement owner)
    {
        var w = Ui.Dialog(F("Hop {0} · {1}", hop.Number, hop.Address), owner, 900, 780);
        w.ResizeMode = ResizeMode.CanResize;
        var fields = new Ui.Fields();
        fields.Add(T("Address"), hop.Address);
        if (hop.Others.Count > 0) fields.Add(T("Also answered"), string.Join(", ", hop.Others), T("Other routers at this distance (the route changes, or shares the load)"));
        fields.Add(T("Name"), hop.Name);
        fields.Add(T("Loss"), hop.Sent > 0 ? F("{0} ({1} sent)", hop.LossText, hop.Sent) : "");
        fields.Add(T("Times"), F("last {0} · average {1} · best {2} · worst {3}", hop.LastText, hop.AvgText, hop.BestText, hop.WorstText));
        fields.Add(T("Jitter"), hop.StDevText);
        if (hop.Destination) fields.Add(T("This is"), T("the host traced to"));
        var stats = Ui.Card(new StackPanel { Children = { Ui.Header(F("Hop {0}", hop.Number)), fields } }, new Thickness(0));
        w.Content = Ui.Page(new StackPanel { Margin = new Thickness(16), Children = { stats, new IpDetailsView(hop.Address, hop.Name.Length > 0 ? hop.Name : null) } });
        w.Show();
    }
}
