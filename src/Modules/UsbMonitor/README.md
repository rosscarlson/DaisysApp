# USB Monitor

*A module of [Daisy's App](../../../README.md): the USB Monitor tab. Switch it on or off in Settings → General → Applets. Its code is in this folder; it's installed to `modules\UsbMonitor\` next to `DaisysApp.exe`.*

Logs device connect/disconnect activity in real time, including devices that fail to enumerate ("Unknown USB Device
(Device Descriptor Request Failed)"). It runs for as long as the app does, whichever tab is open and while the window
is hidden in the tray.

- New events appear at the top; click a column header to sort, drag a header edge to resize. **Event** says
  Connected, Disconnected or Status changed; **Status** (third column) is the device's own state (OK, or a problem code
  such as Code 43). Double-click a row for every property that could be read, on one page, with copy buttons.
- A disconnected device shows the identity it had when it connected (details are cached when first seen). Missing
  fields are left blank; an event is never dropped.
- **Log file:** `%LOCALAPPDATA%\DaisysApp\logs\usbmon_<yyyy-MM-dd_HHmmss>.log`, tab-delimited, one per launch, flushed
  on every event. Turn it off in Settings → USB Monitor.

**How detection works** — three layers, so a flaky device is caught however badly it misbehaves:

1. A hidden top-level window registers for device-interface arrival/removal (`RegisterDeviceNotification`, all
   interface classes — so an occasional non-USB device, such as a Bluetooth pairing, can also appear).
2. On `DBT_DEVNODES_CHANGED` (debounced ~400 ms) the USB device tree is snapshotted through SetupAPI/CfgMgr32 and
   diffed against the previous snapshot. This path is USB-only and catches devices that never register an interface.
3. The same diff runs every 3 seconds regardless, so a missed notification can't hide a plug/unplug.

Events are de-duplicated (same device and transition within ~1.5 s), though one physical plug can still produce
several rows: a hub, its composite parent and each child interface.
