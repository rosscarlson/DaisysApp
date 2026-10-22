# Tools → Notepad

*A tool of the [Tools](../README.md) module (since 3.0.0): its sub-tab, switched on or off in Settings → General →
Modules.*

One note shared between your PCs' Daisy's Apps, for keeping ideas: a plain notepad like Windows' own, with **Bold**,
*Italic* and Underline. One Daisy's App hosts the note and the others connect to it; whatever is typed on any of them
shows on all of them at once. (Text from other PCs was in their colour in 3.0.0; since 3.0.1 there are no colours.)

## Using it

- **Host** (the box at the top left): **This PC** hosts the note here, or pick another PC to use the note it hosts.
  The PCs listed are the ones in **Settings → AI → Remote control** (the notepad uses that connection: its port,
  passphrase and list of PCs, so it needs no port of its own). To host, this PC's remote control has to be on
  (**Let another PC use the sessions here**); to connect, the hosting PC has to be in the list with its passphrase.
- **Status** (beside it): *Hosting*, with the PCs connected, *Not shared: AI remote control is off*, *Connected*, or
  why it couldn't connect (it tries again every 5 seconds).
- **B**, **I**, **U** (or Ctrl+B, Ctrl+I, Ctrl+U) format the selection; the font, its size and **Word wrap** are this
  PC's own (they aren't shared).
- **Pop out** (the button at the top right) moves the notepad into a window of its own, in the taskbar and Alt+Tab; the
  tab then shows **Back in the tab**, and so does the button. Closing the window puts it back. If it's out when the app
  closes, it opens out again (where it was) at the next start.
- Pasting pastes plain text, as in Notepad, and Copy and Cut copy plain text only (since 3.0.2), so it pastes elsewhere
  with no formatting.
- Undo works on your own typing; another PC's change clears it (as it redraws the note).
- While the host can't be reached, you can keep typing: when it's back, what you typed and what changed there both
  stay. Changing the host starts from that host's note (this PC's own hosted note is kept for when it hosts again).

## How it works

The host has the note and its version number. Every change is sent as what's kept, removed and put in (the parts a
diff finds, never text that stayed the same), made on the version that PC has. The host applies one made on its
latest version, numbers it, and sends it to every PC, the one that made it too, as its answer; one made on an older
version is turned back (*stale*), and that PC sends again on what it has since received. Each PC has one change on its
way at a time. A change from another PC arriving while this one has changes of its own unsent is moved over them
(operational transformation), so text typed on two PCs at once all stays, once.

Each notepad has its own id, and numbers its changes; the host keeps the last number it applied from each, and says it
when that notepad connects again, so a change whose answer was lost with the connection is neither lost nor put in
twice. Each character keeps the PC that typed it (in the note, not shown).

The messages, on the AI module's encrypted connection (channel `notepad`):

| From | Message |
|---|---|
| a notepad | `{op: "join", cid}`, then `{op: "edit", base, seq, parts}` (`parts`: `{k: n}` keep, `{d: n}` remove, `{i: runs}` put in) |
| the host | `{op: "state", v, you, seq, doc}` on joining (`seq`: the last change of yours it applied), `{op: "edit", v, id, by, parts}` to everyone, `{op: "stale"}`, `{op: "who", pcs}` |

A run is `{t: text, f: formatting (1 bold, 2 italic, 4 underline), a: PC}`.

## Code

`NotepadApplet.cs` (the sub-tab; serves the `notepad` channel; the popped-out window), `NotepadView.cs` (the page: the host picker, the
formatting buttons and the note; the connection to the host, and the note in the box and back),
`NoteServer.cs` (the hosted note: its PCs, their changes, the versions), `NoteText.cs` (the note as characters, and
its changes: the diff (Myers'), applying one, and moving one over another), `NotepadSettings.cs` (`Notepad.json`: the
host, the look, popped out and where, the hosted note and the note here).
