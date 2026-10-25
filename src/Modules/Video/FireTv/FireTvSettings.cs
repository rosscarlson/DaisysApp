using DaisysApp.Settings;

namespace DaisysApp.Applets.Video.FireTv;

/// <summary>Saved in %APPDATA%\DaisysApp\FireTv.json.</summary>
public sealed class FireTvSettings
{
    private const string FileName = "FireTv";

    /// <summary>The address typed or picked last (ip or ip:port).</summary>
    public string Address { get; set; } = "";

    /// <summary>Installed apps not ticked for removal (Plex, F1 TV and YouTube TV at first).</summary>
    public List<string> Keep { get; set; } = new(Catalog.DefaultKeep);

    /// <summary>What was changed on each device (by serial number), so Restore All can put it back.</summary>
    public Dictionary<string, DeviceChanges> Devices { get; set; } = new();

    public sealed class DeviceChanges
    {
        public string Name { get; set; } = "";
        /// <summary>Apply has run on it: its ticks show it as it is (before, what's advised to go starts unticked).</summary>
        public bool Applied { get; set; }
        /// <summary>Package → "uninstall" (for user 0, back with install-existing), "disable" or "gone" (a user's app, deleted).</summary>
        public Dictionary<string, string> Packages { get; set; } = new();
        /// <summary>"global/private_dns_mode" → its value before ("null" when it had none).</summary>
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    public DeviceChanges For(string serial, string name)
    {
        if (!Devices.TryGetValue(serial, out var d)) Devices[serial] = d = new DeviceChanges();
        d.Name = name;
        return d;
    }

    public static FireTvSettings Load() => JsonStore.Load<FireTvSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
