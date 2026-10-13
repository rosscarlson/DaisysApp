# Hardware: USB Monitor

*A tool in [Daisy's App](../../../../README.md)'s [Hardware](../README.md) tab: its **USB Monitor** sub-tab. Switch Hardware on or off in Settings → General → Applets. Its code is in this folder, part of the Hardware module (installed to `modules\Hardware\` next to `DaisysApp.exe`).*

Logs device connect/disconnect activity in real time, including devices that fail to enumerate ("Unknown USB Device
(Device Descriptor Request Failed)"). It runs for as long as the app does, whichever tab is open and while the window
is hidden in the tray.

- New events appear at the top; click a column header to sort, drag a header edge to resize. **Event** says
  Connected, Disconnected or Status changed; **Status** (third column) is the device's own state (OK, or a problem code
  such as Code 43). Double-click a row for every property that could be read, on one page, with copy buttons.
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
| `Logging/EventLogger.cs` | The per-launch log file |
| `ViewModels/` | An event row, and the column filters |
| `Devices/` | The Devices sub-tab (`DevicesApplet`, `DevicesView`) |
| `DeviceDetailsWindow.xaml(.cs)` | Every property of one event's device |
| `UsbMonitorSettings.cs`, `UsbMonitorSettingsView.xaml(.cs)` | `UsbMonitor.json`, and Settings → Hardware → USB Monitor |
