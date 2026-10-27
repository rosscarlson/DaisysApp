# Tools → Notepad

*A tool of the [Tools](../README.md) module (since 3.0.0): its sub-tab, switched on or off in Settings → General →
Modules.*

Notes in tabs, shared between your PCs' Daisy's Apps, for keeping ideas: a plain notepad like Windows' own, with
**Bold**, *Italic* and Underline. One Daisy's App hosts the notes and the others connect to it; whatever is typed on any
of them shows on all of them at once. (Text from other PCs was in their colour in 3.0.0; since 3.0.1 there are no
colours.) Since 3.0.6 it has tabs, draws `---` as a line, and can be Windows' app for text files.

## Using it

- **Host** (the box at the top left): **This PC** hosts the note here, or pick another PC to use the note it hosts.
  The PCs listed are the ones in **Settings → AI → Remote control** (the notepad uses that connection: its port,
  passphrase and list of PCs, so it needs no port of its own). To host, this PC's remote control has to be on
  (**Let another PC use the sessions here**); to connect, the hosting PC has to be in the list with its passphrase.
  **Add Server…** (at the bottom of the box while this PC is the host; since 3.3.3) adds one there without going to
  Settings: the AI module's Add a PC window (name, address, port, passphrase, Test), and the notepad connects to it
  straight away.
- **Status** (beside it): *Hosting*, with the PCs connected, *Not shared: AI remote control is off*, *Connected*, or
  why it couldn't connect (it tries again every 5 seconds).
- **Tabs** (since 3.0.6): **+** (or Ctrl+T) adds one, **✕** (or Ctrl+W, since 3.8.6) closes one (a tab with something in it asks **Confirm?**
  first, so a second Ctrl+W closes it; the last tab has no ✕). Tabs are shared like the text: one added or closed on any PC is added or closed on
  every PC, in the host's order; which tab shows is each PC's own. A tab is named **Notes 1**, **Notes 2**… (the lowest
  number free; a file's tab, the file's name); right-click it to edit its name in place (Enter keeps it, Esc or clicking away doesn't), and it's renamed on
  every PC (since 3.1.3; 3.1.3 asked in a window; before, a tab's title was its first line).
  Nothing is saved anywhere but the host (and each PC's copy, for while it's away).
- **A line of dashes** (`---`, three or more and nothing else) is drawn as a line across the page (to the right edge
  of what shows, with Word wrap off too). It's still dashes in the note, so it's the same on every PC.
- **B**, **I**, **U** (or Ctrl+B, Ctrl+I, Ctrl+U) format the selection; the font, its size and **Word wrap** are this
  PC's own (they aren't shared).
- **Pop out** (the button at the top right) moves the notepad into a window of its own, in the taskbar and Alt+Tab; the
  tab then shows **Back in the tab**, and so does the button. Closing the window puts it back. If it's out when the app
  closes, it opens out again (where it was) at the next start. The **notepad** icon at the main window's top right
  (after Update, since 3.0.5) pops it out too, or brings its window to the front if it's out already.
- Pasting pastes plain text, as in Notepad, and Copy and Cut copy plain text only (since 3.0.2), so it pastes elsewhere
  with no formatting.
- **Opening files** (since 3.0.6): with the Notepad as Windows' app for a type (below), opening a file of it opens it
  in a new tab, popped out (or its tab, if it's open). The tab is shared like any other; on the PC that opened it,
  **Save** (the disk icon, shown for a file's tab) or Ctrl+S writes it back, as it was (UTF-8 with or without BOM,
  UTF-16, or Windows-1252; CRLF or LF). Its title starts with **•** while it's changed since it was saved. Files over
  1 MB, or with nul characters, aren't opened.
- Undo (Ctrl+Z) works on your own typing, and **Ctrl+Shift+Z** (or Ctrl+Y) redoes what was undone, step by step, until
  something new is typed (since 3.1.5); another PC's change, or switching tabs, clears both (as it redraws the note).
- While the host can't be reached, you can keep typing: when it's back, what you typed and what changed there both
  stay. Changing the host starts from that host's note (this PC's own hosted note is kept for when it hosts again).

## Default app (Settings → Tools → Notepad)

Tick the types (`.txt`, `.log`, `.md`, `.ini`, `.cfg`, `.conf` and `.nfo` at first; `.json`, `.xml`, `.yaml`, `.yml` and
`.csv` too if you like), then:

- **Make default…** notes the app each type has now (once: so it's always what you had before Daisy's App), lists
  *Daisy's App Notepad* as an app for them, and opens Windows' **Default apps** at its page, where you pick it for each
  type (or **Set default**, where Windows offers it). Windows doesn't let any program make itself the default: the
  choice is signed for the user, and on Windows 11 its UCPD driver guards it, so the last click is always in Windows.
- **Restore** takes *Daisy's App Notepad* off every type. A type Windows then gives its own app shows it; a type whose
  app before was another (Sublime Text, say) shows **Before:** and **Choose…**, Windows' Open with for that type, to
  pick it again with **Always**. Each type is forgotten once its app is the one it had.

The list shows each type's app now, and before, while that differs. Everything is this user's (HKCU), no admin.

## How it works

The host has the notes, each with its id and version number. Every change is sent as what's kept, removed and put in (the parts a
diff finds, never text that stayed the same), made on the version of that note that PC has. The host applies one made
on its latest version, numbers it, and sends it to every PC, the one that made it too, as its answer; one made on an older
version is turned back (*stale*), and that PC sends again on what it has since received. Each PC has one change on its
way at a time. A change from another PC arriving while this one has changes of its own unsent is moved over them
(operational transformation), so text typed on two PCs at once all stays, once.

Each notepad has its own id, and numbers its changes; the host keeps the last number it applied from each, and says it
when that notepad connects again, so a change whose answer was lost with the connection is neither lost nor put in
twice. Each character keeps the PC that typed it (in the note, not shown).

The messages (channel `notepad` on the AI module's encrypted connection) are in
[API.md](../../../../API.md#notepad-tools--notepad-since-300).

## Code

`NotepadApplet.cs` (the sub-tab; serves the `notepad` channel; the popped-out window; opens the files Core's
`Shell/OpenFiles` hands it), `NotepadView.cs` (the page: the host picker, the formatting buttons, the tabs and the
note; the connection to the host, each tab's note in the box and back, files opened and saved, and `Rules`: the
lines drawn over the box for `---`), `NoteServer.cs` (the hosted notes: its PCs, their changes, the versions, tabs
added and closed), `DefaultApp.cs` (Windows' registration as an app for text files, each type's app, Open with),
`DefaultAppView.cs` (Settings → Tools → Notepad), `NoteText.cs` (the note as characters, and
its changes: the diff (Myers'), applying one, and moving one over another), `NotepadSettings.cs` (`Notepad.json`: the
host, the look, popped out and where, the hosted notes and the tabs here, the default app's types and their apps
before; 3.0.5's one note is read into the first tab).

**Daisy's App Mobile** (`D:\OneDrive\Code\Android\DaisysApp`, its `notepad/` module, since 0.3) is a notepad too: it
joins a host as another PC does (it can't host). Its `NoteText.kt` is a line-for-line copy of `NoteText.cs`, and
its client side follows this `NotepadView`'s: a change to either (or to the messages) needs the same change there.
