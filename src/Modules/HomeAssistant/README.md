# HA (Home Assistant)

*A module of [Daisy's App](../../../README.md): the **HA** tab. Switch it on or off in Settings → General → Modules.
Its code is in this folder; it's installed to `modules\HomeAssistant\`. New in 3.12.0.*

Your [Home Assistant](https://www.home-assistant.io/)'s devices, laid out as Network → Zabbix lays out its hosts: a
box per device, in a box per area (or floor, or type), each with a quick control, and a window per device with
everything it can do. It controls your devices; it doesn't administer Home Assistant (no automations, integrations
or settings are changed).

## Setting it up

It needs Home Assistant's address and a **long-lived access token**: in Home Assistant, click your name (bottom left)
→ **Security** → **Long-lived access tokens** → **Create token**, and copy it.

- The **address**: `http://homeassistant.local:8123`, an IP address (`192.168.1.20`; no port means Home Assistant's
  own 8123), or an `https://` address (a reverse proxy, Nabu Casa). **Accept a certificate Windows doesn't trust** for
  a self-signed one.
- **Test connection** signs in and counts the entities.

The setup wizard asks for it the first time the app starts with this module (after installing, or updating to 3.12),
with **Skip for now** (it's then in Settings → HA) and **Switch off HA** (the module off, no tab: Settings → General →
Modules brings it back). Switched on later, it asks in its own window. Until it's set up, the tab shows the same form.

A user that isn't an administrator can use it, but Home Assistant may not give such a user its areas and devices:
then each entity is a box of its own, with no area.

## The tab

- **The bar**: Home Assistant's version, how many devices are shown and on, and when something last changed (in red
  when it isn't connected, or for 15 seconds after Home Assistant refused something); a **search** (a name, an area, a
  maker or model, an entity id), **By area / By floor / By type**, an **area** and a **type** filter, **On only**, and
  **Refresh** (connects again and reads everything).
- **Groups**: one box per area (floors and types the same way), with coloured counts: yellow on, grey off, red
  unavailable. **Drag a group's title** onto another to put it there; each way of grouping keeps its own order. Areas
  come by name, floors by level, types lights first, sensors last; the ones without an area or floor last. Groups
  pack into one grid, wrapping onto the next row, as Zabbix's do (Settings → HA → **Display Grid Size**).
- **A device's box**: a dot (a light's own colour while it's on, yellow for anything else on, grey off, green for
  sensors, red when it can't be reached; the box gets an edge of the same colour while it's on), its name and the
  **quick control** for its main entity (the most important one it has: a light before a switch before its
  sensors): **ON / OFF** (a lock: Unlocked / Locked; a cover or valve: Open / Closed), **Activate** for a scene,
  **Run** for a script, **Press** for a button. Under it, what that's doing (*On · 70%*, *Heating · 20.5° → 21.5°*,
  *Playing: artist – title*), a row for each of its other controls (up to three), and up to four sensor values
  (all of them in the tooltip).
- **Click a box** (anywhere but its controls) for the device's window; right-click for **Open** and **Hide**.

What a device is comes from Home Assistant's own device and entity registries: a device with its entities, or an
entity on its own (helpers, scenes, scripts, automations, people). Left out of the boxes: entities Home Assistant
hides, its configuration and diagnostic entities (they're in the window under More), and kinds with nothing to show
(zones, updates, the sun, calendars, timers, text and date inputs, groups…). A device with only sensors shows unless
Settings → HA → **Show devices with only sensors** is off. A media player (a device whose main entity is one) shows
only when it's in an area (since 3.12.1: Plex makes one for every person a library is shared with, none in an area);
Settings → HA → **Show media players without an area** shows them all.

Everything is live: Home Assistant sends each change as it happens, and the tab draws only the boxes that changed
(and nothing while it isn't showing). Areas, floors and devices added or renamed in Home Assistant show a couple of
seconds later.

## A device's window

One per device (clicking its box again brings it to the front), live while it's open:

- Each entity that can be controlled, in a panel: its name and state, ON / OFF (or Run / Press), the **shortcuts**
  button (a joystick), and what its kind has:

  | Kind | In its panel |
  |---|---|
  | Light | Brightness, Warmth (colour temperature), a row of colours and white, Effect |
  | Fan | Speed, Oscillate, Preset |
  | Cover, valve | Position; Open, Stop, Close |
  | Thermostat (climate) | Target temperature; Mode, Preset, Fan |
  | Media player | Volume; previous, play / pause, next; Mute, Source, Sound |
  | Lock | Lock, Unlock, Open (when it has one) |
  | Vacuum, lawn mower | Start, Pause, Stop, Return to base / Dock |
  | Humidifier | Humidity, Mode |
  | Number, input number | Value (its range and step) |
  | Select, input select | Option |
  | Automation | ON / OFF, Run its actions |
  | Script | Run, Stop |

  A slider sends its value a moment after it stops moving (one call, not one per step), and isn't moved by Home
  Assistant's answer while you're dragging it.
- **Readings**: its sensors and their values.
- **More** (folded): its configuration and diagnostic entities, with their controls.
- **Hide** takes it off the tab; **Settings → HA → Hidden** lists what's hidden, each with **Show**.

Alarm panels show their state only (arming needs a code: not in this first version).

## Shortcuts

Anything that switches on and off, or has a level (brightness, speed, position, volume, target temperature,
humidity, a number), can have shortcuts: the joystick button in its panel opens Core's shortcut window with **On /
off**, and **Up** / **Down** for a level. Each can be a key combination or a controller / wheel / button box button,
works anywhere (the tab closed, the app in the tray), and is in **Settings → Shortcuts** under HA. Up / Down move one
step (10% for brightness, position and speed or the fan's own step, 5% for volume, the thermostat's own step, a
number's step); presses in quick succession add up before Home Assistant has answered. Brightness down to 0 turns a
light off; up from off turns it on. Only entities given a shortcut are registered.

## Settings → HA

- **Home Assistant**: the address, the token (kept encrypted for your Windows account), the certificate checkbox and
  Test connection. A change connects again a moment after the last keystroke.
- **Display Grid Size**: columns (0 = as many ~250-wide boxes as fit) and rows to fit the window's height (0 = as tall
  as they need), as Zabbix's.
- **Hidden**: **Show devices with only sensors**, **Show media players without an area** (off by default), and every
  hidden device with **Show**.

`%APPDATA%\DaisysApp\HomeAssistant.json` keeps it all. Its log is `HomeAssistant-<date>.log`: connecting (version,
how many entities, devices and areas), every service call it makes (`light.turn_on light.kitchen {"brightness_pct":
40}`) and what Home Assistant refused.

## Code

Home Assistant's [WebSocket API](https://developers.home-assistant.io/docs/api/websocket), over .NET's own
`ClientWebSocket`: no packages.

| File | What it does |
|---|---|
| `HaApplet.cs` | The applet (`HomeAssistant`, tab "HA", Order 47): settings, service, shortcuts, view; `SetupPage` / `SetupRequired` / `NeedsOnboarding` until it's set up or skipped |
| `HaSettings.cs` | `HomeAssistant.json`: `Address`, `IgnoreCertificate`, `TokenProtected` (DPAPI), `Configured`, `SetupSkipped`, `GroupBy`, `GroupOrder` (per way of grouping), `Hidden` (`HiddenThing` key + name), `ShowSensorsOnly`, `ShowMediaWithoutArea`, `GridColumns` / `GridRows`, `Keys` (entity id → `EntityKeys`); `BaseUri` / `WebSocketUri` (no port on http → 8123) |
| `HaClient.cs` | One connection: `auth_required` → `auth` → `auth_ok`, commands with ids answered by `result` (`SendAsync`, `CommandAsync`, `SubscribeAsync`, `CallServiceAsync`), messages read whole across frames on its own task, `Event` / `Closed`; `HaException` for what to tell the user |
| `HaModels.cs` | `HaEntity` (state, attributes, `ValueText`), `HaArea`, `HaFloor`, `HaDevice`, `HaRegistryEntry`, `HaThing` (a box: device or lone entity, `Shown` / `All`, `Main`, `Type`), `Domains` (kinds skipped, main-entity ranking, type names, state words, `CanToggle` / `IsPress` / `IsOn`, `LevelOf` + `SetLevel`, `Toggle`, `Summary`), `Level` |
| `HaService.cs` | Connected while the module runs (again after 5 s, doubling to a minute; a ping every 30 s): subscribes to `state_changed` and the registries' events, reads areas, floors, devices, the entity registry and `get_states`, builds the `Things`; events queued and handled on the UI thread every 150 ms (`Changed` with the entities changed, or null); `Call` (logged; refusals in `Problem`), `Toggle`, `Press`, `SetLevel`, `Step` (quick presses add up) |
| `HaHotkeys.cs` | Core's `Hotkeys`: `HA.Toggle:` / `HA.Up:` / `HA.Down:` + entity id, from `Keys`; `For` makes an entity's for its window |
| `Views/HaView.cs` | The tab (as Zabbix's `ZabbixView`): the bar, `Section`s packed into one grid, group drag, filters, partial redraws |
| `Views/ThingBox.cs` | A device's box, and `QuickControl` (ON / OFF, Activate / Run / Press, or a value) |
| `Views/ThingWindow.cs` | A device's window: `EntityPanel`s, Readings, More, Hide |
| `Views/EntityPanel.cs` | One entity's controls by kind: sliders (sent 300 ms after the last move), drop-downs, chips, buttons, colours |
| `Views/ConnectionForm.cs`, `HaSetupPanel.cs`, `HaSettingsView.cs` | The form (tab, wizard, Settings), the wizard's step and first-run window (Skip, Switch off), Settings → HA |
| `Views/HaUi.cs` | Chips and small buttons (the rest is Core's `Shared/Ui`) |

Tested (3.12.0) against a fake Home Assistant (a Python WebSocket server speaking the same API: auth, registries,
states, events, service calls, dropped connections): addresses, a wrong token, nothing listening, devices built from
the registries (hidden, configuration and diagnostic entities, skipped kinds, an area from the entity), levels and
summaries, every quick action and the service call it makes, quick steps adding up, a change made outside arriving
live, shortcuts registered, pressed and unbound, reconnecting after a drop; in a test copy of the app: the wizard's
step, the tab by area and by type, a device's window (brightness slider → one call, state back), the shortcut window,
Hide and Show, Settings → HA and Settings → Shortcuts. Not tested against a real Home Assistant.
