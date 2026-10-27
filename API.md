# Daisy's App's remote API

How other devices use Daisy's App over the network: another PC's Daisy's App, Daisy's App Mobile
(`D:\OneDrive\Code\Android\DaisysApp`), the Button2Key Remote Android app (`clients/android`), and test scripts.
**This file is the one description of the protocol.** Modules' READMEs describe what their features do and link here
for the messages.

There are two services, each on its own port:

| Service | Port | Used by | Server code |
|---|---|---|---|
| [**Remote control**](#remote-control) | 38863 (Settings → AI → Remote control) | AI sessions, and every module's [channel](#channels) (Tools → Notepad) | AI's `Remote.cs` (`RemoteWire`, `RemoteServer`), Core's `Shell/RemoteChannels.cs` |
| [**Button2Key Remote**](#button2key-remote) | 38870 (Gaming → Button2Key Remote) | ButtScreens on phones and tablets | `Butt2KeyRemote/ButtServer.cs`, `ButtWire.cs` |

**Rules for new features:**
- A new remote feature goes on **remote control**: either a new op (an AI question), or a **channel** of its own for a
  module. It doesn't get a new port or new crypto.
- It goes through the shared code on each side, never a copy:
  - Windows: `RemoteWire` / `RemoteChannels.Connect` / `RemoteChannels.Serve`.
  - Daisy's App Mobile: `core/remote/` (`Wire`, `Api.ask`, `Api.channel`).
- Each op is documented here, with the version it came in.
- Ops are only ever added, never changed. An old server answers an op it doesn't know with an `error` saying "…can't
  do that yet: update it there." A client can look for that and fall back.

## Remote control

### Connecting

1. TCP to the port. Each frame is a 4-byte little-endian length, then that many bytes (at most 64 MB).
2. The server's first frame is plain JSON: `{"app": "DaisysApp", "v": 1, "salt": "<base64, 16 bytes>"}`.
3. The key is PBKDF2-HMAC-SHA256 of the passphrase (UTF-8) with that salt: 150,000 rounds, 32 bytes.
4. Every frame after that, both ways, is AES-256-GCM: the 12-byte nonce, the 16-byte tag, then the ciphertext of one
   JSON message (no associated data). Java's and Swift's GCM put the tag after the ciphertext, so move it.
   - The nonce is a little-endian `uint32` direction (1 = client to server, 2 = server to client), then a
     little-endian `uint64` count of frames sent that way, from 0.
   - Each side checks the other's count, so frames can't be replayed or reordered.
5. The client sends `{"op": "hello", "pc": "<its name>"}`. The server answers `{"op": "welcome", "pc": "<its name>"}`.
   With the wrong passphrase the hello doesn't decrypt, and the server closes the connection (after a pause, so
   guessing is slow).

A server takes many connections at once. Problems come back as `{"op": "error", "message"}`, worded for the user.

### Questions

One connection can ask any number of these, one after another; each gets one reply.

| Ask | Reply |
|---|---|
| `{"op": "list"}` | `{"op": "sessions", "pc", "folder", "providers", "sessions", "accounts", "account"}`. `folder` is its last working folder. `providers` is `[{id, name}]`, where id `""` is Claude Code. `sessions` is `[{id, name, folder, provider, started, state, who, account, accountName}]`. `accounts` (since 3.2.1) is its Claude accounts, `[{id, name, about, email, plan, usage}]`, and `account` is the id of Claude Code's own sign-in (the one to offer first). An account's `usage` (since 3.15.2; absent when it hasn't been read there yet) is `{windows, blocked, blockedUntil, read}`, as the `usage` message below, with `read` when it was read (Unix seconds): show each account with it, as the PC's own list does. A session's `account`/`accountName` is the account it runs on (absent for other AIs). |
| `{"op": "files", "folder"}` | `{"op": "files", "folder", "exists", "md", "docs"}`: **Project Docs** and **Module Docs** for a new session's read-first. `md` is the folder's own `.md` files. `docs` (since 3.4.0) is its subfolders' README.md files, `[{path, name}]`, up to 5 deep, skipping bin/obj/artifacts/node_modules and the like. `path` is relative to the folder, with `/`. `name` is the module's folder name (`Parent/Folder` when two share a name). It's the same code the PC's own new tab uses (`SessionView.MarkdownFiles`, `SessionView.ModuleDocs`). |
| `{"op": "dirs", "path"}` | `{"op": "dirs", "path", "parent", "dirs", "places"}`: a folder's subfolders (not hidden or system ones), and `places` `[{name, path}]` (Home, Documents, Desktop, drives). A `path` of `""` gives the places only. |
| `{"op": "mkdir", "parent", "name"}` | `{"op": "made", "path"}` |
| `{"op": "new", "name", "folder", "provider", "read", "account"}` | `{"op": "created", "id"}`. `read` is the docs to read first: paths relative to the folder, with `/` (the `md` and `docs` above). `account` (since 3.2.1, Claude Code only) is one of `accounts`' ids; leave it out for Claude Code's own sign-in. An id the PC doesn't have is an error. Then open the session with `attach`. |
| `{"op": "usage"}` | `{"op": "usage", …}` as below, for Claude Code's own sign-in on that PC, without opening a session (since 3.5.4). |

### A session

`{"op": "attach", "id"}` turns the connection into that session's. The server sends:
- `{"op": "attached", "id", "name", "folder", "provider", "who", "model", "mode", "state", "doing", "pc", "context", "history", "accounts", "account", "ownAccount"}`:
  - `history` is the last 300 transcript lines (Claude Code's own JSON lines), with long texts cut short.
  - `accounts` and `account` (since 3.5.2, Claude Code only) are as in `list`, with `account` the one this session
    runs on, and `ownAccount` (since 3.15.2) the id of Claude Code's own sign-in there.
- Then any questions it's waiting on, as `event`s.
- `{"op": "usage", "windows", "blocked", "blockedUntil", "account", "sessionAccount", "accounts", "ownAccount"}` when it opens and whenever it
  changes:
  - `windows` is `[{key, label, used, resets}]`, where `used` is 0–1 and `resets` is in Unix seconds.
  - `blockedUntil` is in Unix seconds.
  - `account` is that account's name. `sessionAccount` (since 3.5.2) is its id.
  - `accounts` and `ownAccount` (since 3.15.2, Claude Code only) are every account with its usage, as in `attached`,
    sent again when any of them changes.
  - The usage is the plan of the account the session runs on.
- `{"op": "event", "msg"}` for everything the session does. `msg` is Claude Code's stream-json (`stream_event`,
  `assistant`, `user`, `system`, `result`, `control_request` for a question), plus the PC's own messages:
  - `daisy_user` `{text, queued, images}`: a message sent, from any PC.
  - `daisy_note` `{text, icon, error}`.
  - `daisy_answered` `{request_id, allow}`: a question answered somewhere.
  - `daisy_state` `{state}`.
- `{"op": "closed"}` when its tab is closed there.

The client sends:

| Message | |
|---|---|
| `{"op": "send", "text", "images"}` | A message. `images` is `[{media_type, data (base64)}]`. |
| `{"op": "interrupt"}` | Stop. |
| `{"op": "answer", "request_id", "allow", "answers"}` | A question answered. `answers` is for Claude's own questions (AskUserQuestion): `{question: "label, label"}`. |
| `{"op": "control", "subtype", "value"}` | `set_model` (`""`, `opus`, `sonnet`, `haiku`, `fable` or a model id), `set_permission_mode` (`manual`, `acceptEdits`, `auto`, `plan`, `bypassPermissions`), `set_account` (since 3.5.2: one of `accounts`' ids; the session moves to it from its next message, as its own Claude Account box does it). |

The Claude account is chosen the same way everywhere:
- To start a session on an account: `new` with `account`.
- To change it on a running session: `control` `set_account`.
- To see which one it's on: `attached.account`, then `usage.sessionAccount` when it changes.

The PC's own tabs use the same calls.

States are the PC's `SessionState`: `Idle`, `Starting`, `Thinking`, `Writing`, `Tool`, `Waiting` (asking you),
`Error`, `Offline`, `Paused` (waiting for a limit).

## Channels

A module's own conversation on the same port and passphrase. After the welcome, the client's first message is
`{"op": "channel", "name"}`, and from then on the connection belongs to that module until it closes. A PC without the
module (or with it off) answers with an `error`.
- Windows: `RemoteChannels.Serve(name, handler)` on the server, `RemoteChannels.Connect(pc, name)` on the client.
- Daisy's App Mobile: `Api.channel(pc, name)`.

### `notepad` (Tools → Notepad, since 3.0.0)

The host's note is shared live. Changes are merged with operational transformation, so text typed on two PCs at once
all stays, once.

| From | Message |
|---|---|
| a notepad | `{op: "join", cid}`, then `{op: "edit", note, base, seq, parts}` (`parts`: `{k: n}` keep, `{d: n}` remove, `{i: runs}` put in), `{op: "new", id, name, doc}` (a tab, its id and name made there), `{op: "close", id}`, `{op: "rename", id, name}` |
| the host | `{op: "state", you, seq, notes: [{id, name, v, doc}]}` on joining (`seq`: the last change of yours it applied), `{op: "edit", note, v, id, by, parts}`, `{op: "new", id, name, v, doc}`, `{op: "close", id}` and `{op: "rename", id, name}` to everyone (closing the last note adds a new empty one; a note without a name gets "Notes n"), `{op: "stale"}`, `{op: "who", pcs}` |

- Each notepad has its own id (`cid`) and numbers its changes (`seq`). The host keeps the last number it applied from
  each, and says it when that notepad connects again. So a change whose answer was lost with the connection is
  neither lost nor put in twice.
- A tab added while the host can't be reached is sent as `new` when it's back. One closed then is sent as `close`,
  and one renamed then as `rename`.
- An edit without `note` (3.0.5's notepads) goes to the first note. A `state` without `notes` (3.0.5's host) is one
  note, `main`, the id that 3.0.5's single note carries on with in the first tab.
- A run is `{t: text, f: formatting (1 bold, 2 italic, 4 underline), a: PC}`.

## Button2Key Remote

The older service, on its own port. It's kept as it is because devices already speak it. `tools/buttclient.py` is a
working Python client (finding PCs, signing in, fetching every ButtScreen, pressing a button) to check a new client
against, and `clients/android` is a whole one in Kotlin (also built into Daisy's App Mobile).

**Finding PCs.** Send `{"op":"discover"}` as a UDP broadcast to the port (38870 unless changed). Each PC with
Button2Key Remote on answers, to the address it came from:
`{"op":"here","app":"DaisysApp","service":"Butt2KeyRemote","v":1,"pc":"PLEX","port":38870}`. Or let the user type the
address and port.

**Connecting.** As remote control, with these differences:
- Frames are at most 1 MB.
- The PC's first frame is
  `{"app":"DaisysApp","service":"Butt2KeyRemote","v":1,"pc":"PLEX","salt":"<base64, 16 bytes>","iterations":150000}`.
  Use the iteration count it gives.
- Direction 1 is from the device and 2 is from the PC.
- On Android, PBKDF2 is `PBKDF2WithHmacSHA256`. On iOS it's CommonCrypto's `CCKeyDerivationPBKDF`.

The device's first message is its hello: `{"op":"hello","device":"Pixel 8","platform":"Android 15","app":"Butt2Key
Remote 1.0","width":1080,"height":2400}` (its screen in pixels, the way it's held). With the wrong passphrase it
doesn't decrypt, and the PC closes the connection (after a pause). With the right one the PC answers
`{"op":"welcome","pc":"PLEX","v":1}`, then the state.

**Messages from the PC**

| Message | |
|---|---|
| `{"op":"state","pc","on","bigButt","current":{"name","count","screens":[{"index","id","name"}]},"bigButts":[{"name","screens":[ids]}],"screens":[{"id","name","width","height","columns","rows","version"}]}` | Sent after the welcome and whenever anything changes: a ButtScreen edited (its `version` goes up), or the BigButt in use switching, by a game or by hand. `current` is the BigButt in use: its `count` of ButtScreens and which ones, in order. Show the first and let the user move from one to the next (a swipe, say); when `current` changes, start again at its first. `bigButts` lists every BigButt, for a picker (`use`). With no BigButts at all, `bigButt` and `current.name` are `""`, `count` is 0 and `bigButts` is empty: say so. |
| `{"op":"screen","screen":{…},"icons":{name: path},"images":{file: base64 PNG}}` | A ButtScreen's layout, the answer to `get`. Keep it until its `version` changes. |
| `{"op":"show","screen":id}` | A Show a ButtScreen button was pressed on this device: show that one. |
| `{"op":"pong"}` | The answer to `ping`. |
| `{"op":"error","message"}` | Something it couldn't do (worded for the user). |

**Messages from the device**

| Message | |
|---|---|
| `{"op":"get","id":id}` | A ButtScreen's layout. |
| `{"op":"down","screen":id,"button":id}` / `{"op":"up",…}` | A button pressed and let go. Send both, for holds, repeats and long presses. |
| `{"op":"tap","screen":id,"button":id}` | A press and let-go in one, for a client that only knows taps. |
| `{"op":"use","bigButt":name}` | Use that BigButt (as picking it in the tab). |
| `{"op":"state"}` | The state again. |
| `{"op":"ping"}` | Send one at least every 10 seconds. A device silent for 30 seconds is dropped, and the buttons it held are let go (as they are when it disconnects). |

**Drawing a ButtScreen.** `width` × `height` are its pixels. Scale the whole thing to fit the device's screen, keeping
its shape, and every size below scales with it.
- **The grid.** It's `columns` × `rows` cells, with `gap` pixels between them and round the edge. A cell is
  `cw = (width - gap × (columns + 1)) / columns` wide and `ch = (height - gap × (rows + 1)) / rows` high.
- **A button.** One at `column`, `row` (from 0) covering `columnSpan` × `rowSpan` cells is at
  `x = gap + column × (cw + gap)`, `y = gap + row × (ch + gap)`. It's `columnSpan × cw + (columnSpan - 1) × gap` wide
  and `rowSpan × ch + (rowSpan - 1) × gap` high.
- **The screen's `background`** fills it.
- **A button's fields:**
  - `id`.
  - `label`: may have line breaks. It's `""` when its text is hidden (by Show Text or Titles).
  - `icon` (a name in `icons`) or `image` (a file in `images`).
  - `shows`: the ButtScreen a Show a ButtScreen button shows, for drawing it as a link. The PC still sends `show` when
    it's pressed.
  - `style`:
    - `rounded`: corner radius `cornerRadius`, given with the screen; square otherwise.
    - `background`.
    - `outline` and `outlineWidth` (pixels, drawn inside the button).
    - `font`, and `fontFamily` (`sans-serif`, `serif` or `monospace`) for when the device hasn't that font.
    - `fontSize` (pixels), `fontColor` and `bold`.
- **Colours** are `#AARRGGBB`.
- **Icon or picture.** It's centred: 45% of the button's smaller side with a label under it (with a gap of 4% of
  that side between them), and 55% without one. The label is centred and wraps.
- **Icons** are SVG path data on a 24 × 24 grid, filled with the screen's `iconColor`. On Android use
  `PathParser.createPathFromPathData`; on iOS, a small SVG path parser. Pictures keep their own colours.
- **Pressed.** Show a button as pressed while it's held (the PC's pop-outs lighten it).

## Clients

| Client | Where | Shared networking code |
|---|---|---|
| Another PC's Daisy's App | AI's `RemoteAgent`, `RemoteClient`; modules via `RemoteChannels.Connect` | `Remote.cs` `RemoteWire` |
| Daisy's App Mobile | `D:\OneDrive\Code\Android\DaisysApp` (CC Remote; Button2Key Remote built in from `clients/android`) | `core/remote/Wire.kt`, `Api.kt` |
| Button2Key Remote for Android | `clients/android` | `Wire.kt`, `Remote.kt` |
| Python | `tools/buttclient.py` (Button2Key Remote) | |
