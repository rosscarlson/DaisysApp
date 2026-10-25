# SimEffects → AmbiLight

*A tool of [SimEffects](../README.md) (since 3.9.0).*

What's on your displays, on Govee lights behind them, as Philips' Ambilight (and SimHub's Govee support) does: the
light is split into **zones**, each placed over a part of the screens (all of them: triples and the rest), and each
zone's segments light in that part's colour, many times a second.

## Before you start

In the **Govee Home** app, open the light's settings and switch on **LAN Control** (it can take a few minutes to
appear). The light and the PC must be on the same network. Close SimHub's or Govee's own screen sync while this runs
(only one program can drive the light at a time).

## Using it

- **ON / OFF** (top left) starts or stops it. Off, the light goes back to its own scene. What it's doing shows next to
  it (*1 lights · 30 pictures a second*). **Test** (top right) lights each segment for 10 seconds: the first white,
  then red, green, blue, over and over: count them, and see which end is first.
- **Lights**: **Find** asks the network for Govee lights with LAN Control on; **Add** the ones found (with their model,
  e.g. *H61A2*). Or type its **Address** and **Add**. The light's dot is green while it answers. A light that moved to
  another address is found again by its MAC when AmbiLight is switched on. **Remove** (Confirm?).
- **Segments** (for the light picked above):
  - **Segments**: how many the light has (Govee doesn't say; **Test** shows them). Up to 255.
  - **Per Zone**: segments sharing a zone. 1 is a zone each (the finest: Govee's segments are the smallest part a
    light can colour; it can't go down to single LEDs); 10 of 100 makes 10 zones of 10.
  - **Stretch**: the light spreads what's sent over all its LEDs (on: right whatever the count; off: one colour per
    segment, which needs the right count). **Reverse**: the first segment is at the other end.
  - **Brightness**: the light's own brightness.
  - **Layout** and **Lay Out** (Confirm?): places every zone again along the displays' edges: **Top** or **Bottom**
    (left to right), **Sides and Top** or **All Around** (clockwise from the bottom left). Changing the number of
    zones lays them out again.
- **Picture**: **Frame Rate** (10–60 a second), **Smoothing** (fades from colour to colour), **Saturation** (more
  colourful than the screen), **Black Level** (darker than this is off, so a dark scene leaves the light off).
- **Displays** (right): your displays as Windows places them, with what's on them, and the picked light's zones on top,
  numbered and in their colours now. **Drag a zone** to move it; drag its edges or corners to size it. The zone you
  touched shows below: its segments and size.

## How it works

- **The pictures**: each display by Windows' desktop duplication (as Mini Mirror and OBS), on the graphics card that
  drives it. Each new frame is shrunk on the card (mipmaps) and only a picture about 300 pixels across comes back, so
  it costs well under a millisecond a picture. Where duplication can't be had (Mini Mirror already mirrors that
  display, another program holds it, a secure desktop), Windows' GDI shrinks the screen instead, and duplication is
  tried again later. Displays turned to portrait are turned back.
- **The colours**: each zone's average over its part of the small pictures (across displays where it spans them),
  pushed away from grey by Saturation, black below Black Level, smoothed; each segment gets its zone's colour.
- **The light**: Govee's LAN API, the way Govee's own PC app streams to lights for DreamView (worked out by others:
  [GoveeDreamView](https://github.com/LeoSko/GoveeDreamView), LedFx, govee2mqtt). Scan: multicast to
  239.255.255.250:4001, answers on UDP 4002; commands to the light's UDP 4003 as JSON. On: `turn` 1, `brightness`,
  then `{"msg":{"cmd":"razer","data":{"pt":"uwABsQEK"}}}` (`BB 00 01 B1 01 0A`, streaming on; `…B1 00 0B` off). Each
  picture: `razer` with `pt` = base64 of `BB, length (2 bytes, = 2 + 3 × n), B0, stretch (0/1), n, R G B × n, XOR of
  all before`. Sent when the colours change, and at least once a second; streaming on again every 30 s, and
  `devStatus` every 10 s (its answer lights the dot).

**If the light doesn't change:** LAN Control off in Govee Home; the light on another network (a guest Wi-Fi); a
firewall blocking UDP 4002/4003 for Daisy's App; SimHub or Govee's PC app streaming to it too. If **Find** says port
4002 is taken, another program listens there: add the light by its address (Govee Home → the light → settings → device
info shows its IP).

## Code

| File | |
|---|---|
| `AmbiSettings.cs` | `AmbiLight.json` (`AmbiSettings`: on, the lights, the light shown, frame rate, smoothing, saturation, black level); `AmbiLight` (address, MAC, model, segments, per zone, stretch, reverse, brightness, layout, zones; `ZoneCount`, `ZoneOf(segment)`); `AmbiZone` (a rectangle in physical pixels on the virtual screen) |
| `AmbiLightApplet.cs` | The tool: a loop on its own thread while on, testing or the tab shows (to a schedule, so 30 a second stays 30): pictures, each zone's colour, each segment's, sent while on (`Send`: on, brightness, streaming, status asks, colours when changed or each second); streaming off when switched off or closing; `EnsureZones`, `Test` |
| `ScreenSampler.cs` | `ScreenSampler`: the displays (by DXGI: place, rotation; numbered left to right), a `DisplayCapture` each, set up again when Windows' displays change; `Average(zone)`, `Thumbs()` for the tab. `DisplayCapture`: desktop duplication on the display's GPU, `CopySubresourceRegion` into a texture with mipmaps, `GenerateMips`, the level ≤ 400 pixels across read back through a staging texture and turned upright; GDI's halftone `StretchBlt` (`GdiShrink`) while duplication fails, retried after 5 s doubling to 2 minutes |
| `Govee.cs` | `GoveeLan`: the scan (from every IPv4 network the PC is on), a listener on UDP 4002 (answers: found lights, who answered), `turn`, `brightness`, `devStatus`, `razer` packets (`Packet`, `Frame`) |
| `Layouts.cs` | Zones along a path round the displays' edges (displays side by side at the same height make one strip), each the same length of it |
| `AmbiLightView.cs` | The tab (`AmbiLightView`: lights, segments, picture cards; status) and `ZoneEditor` (the displays scaled into a canvas, their pictures as `WriteableBitmap`s 3 times a second, the zones as numbered borders in their colours: move by dragging, size by edges and corners, saved on release) |
