# Hardware: USB Monitor

*A tool in [Daisy's App](../../../../README.md)'s [Hardware](../README.md) tab: its **USB Monitor** sub-tab. Switch Hardware on or off in Settings → General → Applets. Its code is in this folder, part of the Hardware module (installed to `modules\Hardware\` next to `DaisysApp.exe`).*

Logs device connect/disconnect activity in real time, including devices that fail to enumerate ("Unknown USB Device
(Device Descriptor Request Failed)"). It runs for as long as the app does, whichever tab is open and while the window
is hidden in the tray.

- New events appear at the top; click a column header to sort, drag a header edge to resize. **Event** says
  Connected, Disconnected or Status changed; **Status** (third column) is the device's own state (OK, or a problem code
  such as Code 43). Double-click a row for every property that could be read, on one page, with copy buttons.
- **Probable Device** (the first row of the details page) says in a word or two what the device is (*UPS
  (CyberPower)*, *Audio device*, *Unknown, most likely …*); click it to open, across the page, what it is and where
  it's plugged in in plain English, worked out from the live USB device tree when the page opens:
  - **What it is:** the kind of device (from its USB class codes and its parts': audio, webcam, storage, keyboard,
    UPS…), the name the device reports itself (often more telling than Windows' "USB Composite Device") and the
    brand from its vendor ID. A hub's vendor ID is its chip maker, and some vendors (JMTek, C-Media, Terminus,
    Genesys…) mostly make chips for no-name products, which it says. For a device that couldn't say what it is, the
    devices that worked on the same port before.
  - **Where it's plugged in:** each hub between it and the PC with the port it's in, then the PC's port and its USB
    controller (built into the motherboard, an extra controller chip or USB card, a graphics card's USB-C, or
    Thunderbolt). Two hubs of the same chip joined together are most likely one bigger hub (a 7-port hub is
    usually two 4-port chips), so its printed port numbers won't match; a hub that shares a container with another
    device is built into it (a monitor, dock or keyboard).
  - **To find it:** what else is plugged into the same hub (both chips of a two-chip hub) and next to that hub, so it
    can be found among many devices.
  - **Why it fails** (a problem): what the failure means (Device Descriptor Request Failed, Invalid Configuration
    Descriptor, Port Reset Failed, Set Address Failed, a USB 3 link failure, or the problem code), the usual causes,
    and how often it has dropped out.

  **Copy all to clipboard** includes it.
- **Problems** (a Windows problem code such as Code 43, or a device that appears but can't be read) show **bold red**,
  with what the code means and the device it most likely is in **Likely device**. A device that fails its USB
  handshake often reports almost nothing (just its class, driver key and location), so it's matched against the
  devices known to have worked: plugged into the same port (its location path), the same port on the same hub,
  the same vendor and product, serial number or container. The details page, the module log and the log file have
  the full analysis.
- **Filter and sort:** right-click a column header for Sort A to Z / Z to A and a tick box for every value in that
  column (Show all, Hide all); a filtered column's header shows a filter mark, and **Reset filters** shows every row
  again. Left-clicking a header sorts as before.
- A disconnected device shows the identity it had when it connected (details are cached when first seen). Missing
  fields are left blank; an event is never dropped.
- **Log file:** `%LOCALAPPDATA%\DaisysApp\logs\usbmon_<yyyy-MM-dd_HHmmss>.log`, tab-delimited, one per launch, flushed
  on every event. Turn it off in Settings → Hardware → USB Monitor.

**How detection works** — three layers, so a flaky device is caught however badly it misbehaves:

1. A hidden top-level window registers for device-interface arrival/removal (`RegisterDeviceNotification`, all
   interface classes — so an occasional non-USB device, such as a Bluetooth pairing, can also appear).
2. On `DBT_DEVNODES_CHANGED` (debounced ~400 ms) the USB device tree is snapshotted through SetupAPI/CfgMgr32 and
   diffed against the previous snapshot. This path is USB-only and catches devices that never register an interface.
3. The same diff runs every 3 seconds regardless, so a missed notification can't hide a plug/unplug.

Events are de-duplicated (same device and transition within ~1.5 s), though one physical plug can still produce
several rows: a hub, its composite parent and each child interface.

## Devices

The **Devices** sub-tab lists every USB device known: each one the USB Monitor has seen, and the ones Windows
remembers (unplugged devices it installed before, with Windows' own last connect and disconnect times). For each:
its status (Connected, Disconnected or the problem; **bold red** unless it's connected and fine), name, maker,
VID and PID, last connected, last disconnected, last working, class, serial number, location, first seen and where
it's known from. The same right-click filtering and sorting as the event list; double-click (or right-click → Show
details) for its details page: the times, every property as last seen, and anything that was different when it
last worked. Right-click → Forget this device removes it. Kept in `%APPDATA%\DaisysApp\UsbDevices.json`; it's what
a problem device is matched against.

**Moving from USB Mon.** On first run, USB Monitor takes its column widths from the standalone USB Mon app
(`%LOCALAPPDATA%\USBMon\settings.json`) it started as, if it's on this PC.

## Code

| File / folder | What it is |
|---|---|
| `UsbMonitorApplet.cs`, `UsbMonitorView.xaml(.cs)` | The USB Monitor sub-tab: the live event list |
| `Capture/DeviceWatcher.cs` | Combines device notifications, device-node snapshots and a periodic poll, so even devices that fail to enumerate are caught |
| `Capture/DeviceNotificationWindow.cs`, `DeviceSnapshot.cs`, `NativeMethods.cs` | The hidden window for WM_DEVICECHANGE; reading device properties (SetupAPI / CfgMgr32) |
| `Models/` | A device's snapshot (`DeviceInfo`), an event (`DeviceRecord`), and the known devices (`KnownDevice`, `KnownDeviceStore`: `UsbDevices.json`, and matching a problem to a device) |
| `Models/DeviceExplainer.cs`, `UsbVendors.cs` | The details page's **Probable Device** row: walks the USB tree from the device up to the controller; the common USB and PCI vendor IDs |
| `Logging/EventLogger.cs` | The per-launch log file |
| `ViewModels/` | An event row, and the column filters |
| `Devices/` | The Devices sub-tab (`DevicesApplet`, `DevicesView`) |
| `DeviceDetailsWindow.xaml(.cs)` | Every property of one event's (or known) device, with the **Probable Device** row |
| `UsbMonitorSettings.cs`, `UsbMonitorSettingsView.xaml(.cs)` | `UsbMonitor.json`, and Settings → Hardware → USB Monitor |
