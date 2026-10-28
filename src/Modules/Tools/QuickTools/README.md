# Tools → Tools

*A tool of the [Tools](../README.md) module of [Daisy's App](../../../../README.md), on its own sub-tab (since 3.20.2).*

Buttons for Windows' resets and admin jobs, a card per kind, to grow over time. The last one's outcome shows above the
cards (and in the Tools log). Ones marked *as administrator* run Windows' own commands with administrator rights:
Windows asks first, unless Daisy's App already runs as administrator. Ones that can't be undone or end what you're
doing ask **Confirm?** first (a second click does it).

| Card | Button | What it does |
|---|---|---|
| Display | **Reset Graphics Driver** | Presses Win+Ctrl+Shift+B for you: Windows resets the graphics driver (the screen blinks; nothing closes). For a black, frozen or stuck screen |
| | **Restart Explorer** | Ends Explorer (the taskbar, desktop and File Explorer windows) and starts it again |
| | **Refresh Icons** | Windows rebuilds its icon cache (`ie4uinit -show`) and redraws the icons |
| Network | **Flush DNS** | Clears Windows' DNS cache (as `ipconfig /flushdns`) |
| | **Renew IP** | Every adapter's address released and renewed (`ipconfig /release`, `/renew`; as administrator; Confirm?) |
| | **Reset Network** | `netsh winsock reset` and `netsh int ip reset` (as administrator; Confirm?); restart Windows to finish |
| System | **Restart Audio** | Windows' audio services (Audio Endpoint Builder and Windows Audio) stopped and started (as administrator) |
| | **Sync Clock** | The clock set from the time server now (`w32tm /resync`, as administrator) |
| | **Empty Recycle Bin** | Every drive's (Confirm?) |
| | **Clear Clipboard** | |
| | **Repair Windows** | DISM's RestoreHealth, then SFC, in a console window of their own (as administrator) |
| Power | **Lock**, **Sleep**, **Sign Out**, **Restart**, **Shut Down** | As Start's power menu (all but Lock ask Confirm?) |
| | **Restart to BIOS** | Restarts into the UEFI firmware's settings (`shutdown /r /fw`; as administrator; Confirm?) |
| | **Advanced Startup** | Restarts into Windows' recovery options (`shutdown /r /o`; Confirm?) |
| Open | Task Manager, Device Manager, Services, Event Viewer, Disk Management, Network Connections, System Properties, Programs (and Features), Startup Apps, Registry Editor, Control Panel, **Admin Terminal** (Windows Terminal as administrator, or PowerShell) | |

No settings.

## Code

In `src/Modules/Tools/QuickTools/` (the folder isn't called Tools, which is the module's):

- `QuickToolsApplet.cs`: the sub-tab (id `QuickTools`), added last by `ToolsApplet`.
- `QuickToolsView.cs`: the cards; `Do` makes a button that runs its job off the window's thread (Confirm? through
  Core's `ConfirmButton.Arm` where it asks), shows the outcome and logs it.
- `Actions.cs`: what each button does: `keybd_event` for Win+Ctrl+Shift+B, Windows' calls (`DnsFlushResolverCache`,
  `SHEmptyRecycleBin`, `LockWorkStation`, `SetSuspendState`, `ExitWindowsEx`) and commands (`Run` / `Admin`: hidden,
  with `runas` when it needs administrator rights; `shutdown` tried without them first).
