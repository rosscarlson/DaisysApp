# Terminal

*A tool of the [Tools](../README.md) module (since 3.1.7): its sub-tab, switched on or off in Settings → General →
Modules (the Terminal tick under Tools). Its code is in this folder.*

CMD, PowerShell and WSL sessions in tabs, each one the real shell in Windows Terminal's own control, looking as
Windows' console does.

## Using it

- **+** opens a session of the default kind (**Settings → Tools → Terminal → New sessions**: CMD to start with); the
  arrow beside it opens any kind: **CMD**, **PowerShell** (Windows PowerShell) or **WSL** (your default distribution,
  in its home folder). A shell starts in your user folder.
- **Tabs** are named after their kind (**CMD 1**, **PowerShell 2**…). **Right-click** a tab to edit its name in place
  (Enter keeps it, Esc or clicking away doesn't). **✕** closes one, ending its shell (a second click: **Confirm?**);
  the last tab has no ✕.
- **The look:** Windows' console defaults, from Console Properties (`HKCU\Console`): its 16 colours (Campbell unless
  changed), the default text and background colours, the font (Cascadia Mono on Windows 11, Consolas before, or the
  one set there) at its size, and the cursor. The same for all three kinds, as in Windows Terminal (Windows
  PowerShell's old blue background is the classic console's, set per program, and isn't used).
- **Copy and paste**, as in the console: select with the mouse, then **Ctrl+C** (or right-click) copies it; with
  nothing selected Ctrl+C goes to the shell (to stop a command). **Ctrl+V** (or right-click) pastes; a paste of
  several lines into bash is pasted as one (bracketed paste), not run line by line.
- **Exit:** a shell that exits normally (`exit`) closes its tab (the last one is followed by a new one). One that
  ends with an error code says so in the terminal, and **Enter** starts it again there.
- **Next start:** the tabs come back (names and kinds), each a new shell, started when its tab is first shown. What
  was in a terminal isn't kept.

The settings are in `%APPDATA%\DaisysApp\Terminal.json` (the default kind, the tabs and the one showing).

## Code

| File | |
|---|---|
| `TerminalApplet.cs` | The sub-tab and its Settings page (the kind + opens) |
| `TerminalView.cs` | The tabs (+, the kind menu, right-click rename, ✕ with Confirm?), a `TerminalControl` per session made when it's first shown (theme, then its connection, once it's on screen), and copy / paste: the control has none, so `ComponentDispatcher.ThreadFilterMessage` watches the terminal's window for Ctrl+C / Ctrl+V and the right button |
| `ShellConnection.cs` | The shell on a Windows pseudo console (ConPTY: `CreatePseudoConsole`, a process with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`, `STARTF_USESTDHANDLES` so a parent's console isn't inherited), its output read as UTF-8 on a thread, typing written to it, resizes, bracketed paste tracked from the shell's own `ESC[?2004h/l`; exit 0 raises `Exited`, another code is shown and Enter restarts |
| `Shells.cs` | The three kinds (command lines, WSL missing), the console's look from `HKCU\Console`, and `FindNative`: the control's native `Microsoft.Terminal.Control.dll` is in `runtimes\<arch>\native\`, which isn't searched for a module loaded into the app, so a `DllImportResolver` loads it from there |
| `TerminalSettings.cs` | `Terminal.json` |

The control is NuGet's `CI.Microsoft.Terminal.Wpf` (a repackage of Windows Terminal's WPF control, the one Visual
Studio's terminal uses; its DLLs are signed by Microsoft). Its native debug files (80 MB each) aren't copied
(`Tools.csproj`).
