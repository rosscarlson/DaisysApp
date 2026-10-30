# Session notes: AI

Claude Code reads this file only when it works in this folder. Keep it under about 8 KB: what each release did is in
`CHANGELOG.md` and git (`git log -- src/Modules/AI`); here only what a session can't get from the code.

## Decisions and how things work

- **Accounts (3.2.1):** a session on another account than Claude Code's own runs with `CLAUDE_CONFIG_DIR` =
  `%LOCALAPPDATA%\DaisysApp\Claude accounts\<id>` (`ClaudeAccounts.FolderFor`/`Prepare`: junctions to every `~/.claude`
  folder but `backups`, `.claude.json` with the account's oauthAccount, credentials kept). The global sign-in is never
  switched: a running claude.exe re-reads `.credentials.json` when its mtime changes and rewrites `~/.claude.json`
  `oauthAccount`, so a global swap moved every session. Switching account = the same session resumed (`--resume`, full
  history) on the other account; `UseAccount` → `Restart(carryOn: false)`, and while waiting for a limit it only
  updates the usage (3.9.1). `/usage` (`claude -p /usage`) asks no model: no tokens.
- **Local and remote draw the same (3.15.3):** the same controls, fed by a local source or a mirror of the server
  (`PlanUsage.Mirror`/`ApplyRemote`, `AccountsUsage.ShowRemote`); the server sends a snapshot only when its JSON
  changed. Protocol: `API.md` only.
- **Mid-turn messages (3.1.1):** a message sent during a tool step is read after it, in the same turn (checked with
  `claude -p` stream-json); Context and Ctrl+Enter use it.
- **/compact (3.16.4):** stream-json gives `status: compacting`, then `compact_result`, a new `init`, `compact_boundary`
  with `pre_tokens`/`post_tokens`. Compact after Release sends `/compact` after the notes' turn of a request that
  pushed a `v*` tag (`PushesRelease`); Brief Replies adds `AiSettings.BriefNote` on the host only, stripped by
  `WithoutBrief` everywhere it's read back.
- **Tokens (3.14.0, 3.14.1, 3.22.1):** measured on this repo's transcripts (25 sessions, 2026-10-28): almost all usage
  is the context read from cache on every call (≈ 40k tokens at a session's first call, 170k on average, up to 520k
  in long ones); output ≈ 1/5; cache writes ≈ 1/5; notes turns ≈ 6%, half of them after a request that changed no
  file. So: the app writes REQUESTS.md itself (`RequestLog.cs`); read-first skips CLAUDE.md for Claude Code (it loads
  it); the notes turn runs only after a request that used Edit/Write/MultiEdit/NotebookEdit (`filesEdited`, set in
  `Assistant` outside the notes turn, subagents too); the notes message keeps each CLAUDE.md under ~8 KB; the docs
  boxes show each file's size in tokens (bytes / 4). The biggest lever left is the user's: shorter sessions (Compact
  after Release, a new tab per task), fewer docs ticked, Brief Replies.
- **Images in the box (3.18.7–3.18.8):** WPF undo serializes deleted content with XamlWriter, which throws on a decoded
  PNG or the internal `ImageChip`: `RichInput.HoldUndo()` keeps undo off while the box holds an image.
- **Module Docs (3.8.6, 3.10.1):** named "Tab → Sub-tab" from README headings in the window's tab order
  (`AppNavigation.TabsShown`, the tab's shown name `ShownTab.Named`: the user's AI tab is renamed "Claude"); the new
  tab panel waits up to 10 s for `TabsShown`.
- **In-session dot (3.24.9):** `AccountsUsage.InUse(ids)` (static, fed by `AiView.ShowInUse` on every StateChanged,
  tab close, start) = accounts of local Claude tabs not Offline (Paused counts); sent to other PCs as `inUse` in each
  account (API.md), read into `RemoteAccount.InUse`. Built only.
- **Voice (3.25.2):** `--say text` → shell sends `say:`+text on FileHandoff's pipe → Core `Speech.Say` → `Voice.Say`
  (SAPI COM via `dynamic`, a new SpVoice per text on an STA thread; OneCore voices via SpObjectTokenCategory
  `Speech_OneCore`, which works). Told by `--append-system-prompt` (`Voice.Prompt`, ~40 tokens). Tested in DaisysDev
  (--say spoken, ~1.1 s per call warm); the Settings combo not seen on screen.
- **Speak (3.26.6; 🎤 right of Send since 3.26.7, no word, `sendRow` 90 wide):** `Dictation.cs`, System.Speech 8.0 (`SpeechRecognitionEngine`, `DictationGrammar`, default
  mic, `RecognizeAsync(Multiple)`); the recognizer on this PC is the desktop `MS-1033-80-DESK` (OneCore's DNN one
  needs WinRT). Phrases go in at the caret; the user fixes and sends. Claude takes no audio, so local was the only way.
  Checked: a TTS clip recognized word for word (PS, same API); not on screen (DaisysDev's AI tab off, its exe 3.25.1).
- **Files dropped (3.26.7):** non-image files dropped on `RichInput` → `FilesDropped` → chips (`attached`,
  `ShowAttached`) above the box; sent as paths (`Attachments.Note`) appended to `sent` only, the bubble shows
  `Attachments.Line`. Remote: `RemoteAgent.Send(text, images, files)` puts `files` [{name, data}] on `send`; the server
  `Attachments.Save`s them (two weeks) and calls `Send(..., files: paths)`. Not inline chips in the rich text: undo's
  XAML serializing of UI elements (see Images) made that fragile. A new remote tab's first message (`sendOverride`)
  doesn't take files: they stay for the next. Harness checked the round trip only.
- **Tables and code blocks (unreleased, after 3.26.4; not published, user said "just fix"):** an empty Code text
  colour (Theme) gave `Look.Brush` null, set as the Paragraph's Foreground = invisible text (a reply's table showed as
  a blank block). Now Foreground is set only when non-null, and Markdown tables are a FlowDocument `Table`
  (`Markdown.Table`: header bold, `ControlBorderBrush` lines). Built only; in `docs/UNTESTED.md`. Next: see it on
  screen, then release when the user asks.
- **Title bubbles:** `AiCoding:<session id>` per working session, `TitleBadges.All` puts the AI's last (3.18.2).

## Testing

- DaisysDev (`%TEMP%\DaisysDev`, `%APPDATA%\DaisysDev`): back up `AI.json` first and restore it. UIA by name only, no
  keys or mouse to its window; `%TEMP%\tokui.ps1 -Click "AI|+" -Shot x.png -List` (finds the window by handle; the
  older `v315.ps1` lookup by process fails now). Posted mouse messages don't reach WPF.
- DaisysDev2 as a second PC on localhost for remote tabs; Python clients of the wire in `%TEMP%` (`renametest`,
  `accteditiles.py`), passphrase from DaisysDev's AI.json (DPAPI).
- Reflection harnesses on the built DLL for logic (`%TEMP%\accttest`, `promptstest`, `reqlogharness`, `imgsel`).

## Not tested yet (needs a real turn, a limit or a second account)

- 3.22.1 (released: tag, Release run passed, 20 assets): no notes' turn after a question; the notes' turn after an edit; the sizes on another PC's new tab.
- A real second Claude account: sign-in in its folder, a turn, token renewal, `/usage` there, a switch by click in
  the app, amber/red lines, "Back in 1h 01m @ 2:10PM" (3.23.3; `PlanUsage.InAt`, under a day).
- 3.23.3 (released, built only): Enter = new line in the box (RichTextBox default), Alt+Enter (`Key.System`/`SystemKey`)
  or Send sends; Ctrl+Enter (send now, mid-turn) kept, ask if the user wants it gone. "Resets in" kept (user wrote "Reset").
- 3.23.5 (released, built only): Compact after Release / Brief Replies side by side on a new tab (`options` horizontal).
- The account switch while waiting for a limit; several Coding bubbles and their clicks; Compacting on screen;
  Compact after Release / Brief Replies in a real turn; the ✕ on a pasted image by mouse; Notepad Ctrl+W; a remote
  tab reopened by Ctrl+Shift+T; Show All on a session over 600 messages.

## Offered, waiting on the user

- Speak with a local Whisper model (≈75–150 MB download on first use), if Windows' dictation proves too inaccurate.
- A per-session Keep session notes switch (the setting is global).
- Daisy's App Mobile: handle `daisy_name` (3.16.2) and send `compact_after_release`/`brief_replies` on `new` (3.16.5).
- Not started: a block above "Claude Account" listing active sessions on every remote PC (needs a server op listing
  running sessions, asked of each of `RemoteSettings.Pcs`).
