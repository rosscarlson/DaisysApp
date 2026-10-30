# Tools → Notepad

*A tool of the [Tools](../README.md) module (since 3.0.0): its sub-tab, switched on or off in Settings → General →
Modules.*

Notes in tabs, shared between your PCs' Daisy's Apps, for keeping ideas: a plain notepad like Windows' own, with
**Bold**, *Italic* and Underline. [Daisy's Server](../../../../../DaisysServer/README.md) keeps the notes (since 3.26.5
only it: before, one Daisy's App hosted them for the others); whatever is typed on any of your clients shows on all of
them at once. (Text from other PCs was in their colour in 3.0.0; since 3.0.1 there are no
colours.) Since 3.0.6 it has tabs, draws `---` as a line, and can be Windows' app for text files.

## Using it

- **Daisy's Server** (since 3.26.4): set it up in **Settings → General → Daisy's Server** (address, port, the token
  the server gave, user, password; **Connect**; since 3.26.5 there, for every module that uses the server). It keeps
  each user's notes, sealed on their PCs and phones with a key from their password, so the server can't read them.
  **Change password** there changes it on the server (the notes stay); **Forget** forgets it here. The tabs you have
  go up to the server the first time (a user with no notes there yet, or tabs last in step with another server or,
  before 3.26.5, a PC host). Not set up, the notes stay on this PC.
- **Status** (top left): *Connected to (server)*, with the other clients connected, *Daisy's Server isn't set up*, or
  why it couldn't connect (Core tries again every 5 seconds).
- **Tabs** (since 3.0.6): **+** (or Ctrl+T) adds one, **✕** (or Ctrl+W, since 3.8.6) closes one (a tab with something in it asks **Confirm?**
  first, so a second Ctrl+W closes it; the last tab has no ✕). Tabs are shared like the text: one added or closed on any PC is added or closed on
  every PC, in the server's order; which tab shows is each PC's own. A tab is named **Notes 1**, **Notes 2**… (the lowest
  number free; a file's tab, the file's name); right-click it to edit its name in place (Enter keeps it, Esc or clicking away doesn't), and it's renamed on
  every PC (since 3.1.3; 3.1.3 asked in a window; before, a tab's title was its first line).
  Nothing is saved anywhere but the server (and each PC's copy, for while it's away).
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
- While the server can't be reached, you can keep typing: when it's back, what you typed and what changed there both
  stay.

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

The server has the notes, each with its id and version number. Every change is sent as what's kept, removed and put in (the parts a
diff finds, never text that stayed the same), made on the version of that note that PC has. The server applies one made
on its latest version, numbers it, and sends it to every PC, the one that made it too, as its answer; one made on an older
version is turned back (*stale*), and that PC sends again on what it has since received. Each PC has one change on its
way at a time. A change from another PC arriving while this one has changes of its own unsent is moved over them
(operational transformation), so text typed on two PCs at once all stays, once.

Each notepad has its own id, and numbers its changes; the server keeps the last number it applied from each, and says it
when that notepad connects again, so a change whose answer was lost with the connection is neither lost nor put in
twice. Each character keeps the PC that typed it (in the note, not shown).

The messages are in [API.md](../../../../API.md#notepad-tools--notepad-since-300), sealed for Daisy's Server's
`notepad` module (its PROTOCOL.md). Before 3.26.5 one Daisy's App hosted them on the AI module's remote control.

## Code

`NotepadApplet.cs` (the sub-tab; the popped-out window; opens the files Core's `Shell/OpenFiles` hands it),
`NotepadView.cs` (the page: the status, the formatting buttons, the tabs and the note; each tab's note in the box and
back, files opened and saved, and `Rules`: the lines drawn over the box for `---`; `ServerNoteLink`: the notes on
Daisy's Server through Core's `ServerLink.Keep` (Core opens and keeps the channel; this seals what goes and opens what
comes, with snapshots)), `DefaultApp.cs` (Windows' registration as an app for text files, each type's app, Open with),
`DefaultAppView.cs` (Settings → Tools → Notepad), `NoteText.cs` (the note as characters, and its changes: the diff
(Myers'), applying one, and moving one over another; tab ids and names), `NotepadSettings.cs` (`Notepad.json`: the
look, popped out and where, the tabs here and the server they were in step with, the default app's types and their
apps before; 3.0.5's one note is read into the first tab). No network code of its own: the connection is Core's.

**Daisy's App Mobile** (`D:\OneDrive\Code\Android\DaisysApp`, its `notepad/` module, since 0.3) is a notepad too: it
joined a PC host as another PC did; since 3.26.5 there's none, so it needs Daisy's Server (the brief in Downloads,
`DaisysServer-Android-Brief.md`). Its `NoteText.kt` is a line-for-line copy of `NoteText.cs`, and
its client side follows this `NotepadView`'s: a change to either (or to the messages) needs the same change there.
