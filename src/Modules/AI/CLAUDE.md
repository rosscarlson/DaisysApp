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
  pushed a `v*` tag (`PushesRelease`); Brief Replies adds `AiSettings.BriefNote` on the host only (`WithoutBrief`).
  **Auto-Compact (3.36.3, 🗜):** `AutoCompact()` from `Result` after `WriteNotes`/`CompactIfReleased`; `compactDue` is
  set only by a non-slash bubble (so no loop); `bubble: false` + `quietCompact` leave only `Compacting()`'s line.
  Since 3.36.11 (the user: "only after replies where you publish to github") also needs `published` (`Publishes`: any
  `git push`, `gh release create|upload|edit`, `gh pr create|merge`), cleared each time AutoCompact runs.
  Local sessions only. Buttons left to right: 💬 🔊 🗜 Search (the user's order).
- **Tokens (3.14.0–3.22.1):** almost all usage is context read from cache each call; so the app writes REQUESTS.md
  itself, read-first skips CLAUDE.md for Claude Code, the notes turn runs only after Edit/Write (`filesEdited`), each
  CLAUDE.md is kept under ~8 KB. The rest is the user's: shorter sessions, fewer docs, Brief Replies.
- **Images in the box (3.18.7–3.18.8):** WPF undo serializes deleted content with XamlWriter, which throws on a decoded
  PNG or the internal `ImageChip`: `RichInput.HoldUndo()` keeps undo off while the box holds an image.
- **Module Docs (3.8.6, 3.10.1):** named "Tab → Sub-tab" from README headings in the window's tab order
  (`AppNavigation.TabsShown`, the tab's shown name `ShownTab.Named`: the user's AI tab is renamed "Claude"); the new
  tab panel waits up to 10 s for `TabsShown`.
- **In-session dot (3.24.9):** `AccountsUsage.InUse(ids)`, fed by `AiView.ShowInUse`; sent as `inUse` (API.md).
- **Voice (3.25.2):** `--say text` → shell sends `say:`+text on FileHandoff's pipe → Core `Speech.Say` → `Voice.Say`
  (SAPI COM via `dynamic`, a new SpVoice per text on an STA thread; OneCore voices via SpObjectTokenCategory
  `Speech_OneCore`, which works). Told by `--append-system-prompt` (`Voice.Prompt`, ~40 tokens). Tested in DaisysDev
  (--say spoken, ~1.1 s per call warm); the Settings combo not seen on screen.
- **Narrator voices (3.36.9; "local and free", the user):** no Microsoft API; SAPI and WinRT don't list them. Adapter
  v0.2.11 (27 Oct 2026; MIT, third party, uses keys extracted from system files, so a Windows or voice update can break
  it; its notes: HD "not fully supported", so it may not be the full HD model, unverified by ear) registered on PLEX from
  `%LOCALAPPDATA%\NaturalVoiceSAPIAdapter\v0.2.11\x64` (`regsvr32`, a UAC prompt; `/u` undoes it). Never
  bundle it. Ava is package `MicrosoftWindows.Voice.en-US.AvaHD.1`; Narrator's pick is `HKCU\...\Narrator\NoRoam`
  `SpeechVoice`, equal to the SAPI description. Tokens come through `TokenEnums`, which `SpObjectTokenCategory` includes.
  Tested: WAV differs from David's and Zira's, ~0.5 s for a 2.45 s line; real `Voice` class in `%TEMP%\voiceharness`
  (spoke, Names 150 ms, no errors), SpMMAudioOut device 0. Not in DaisysDev (running, maybe the other session's).
- **Install Narrator's Voices (3.36.12; the user: all clients on Narrator's voice, by a button):** `VoiceAdapter.cs`:
  `ModuleTasks` elevated downloads v0.2.11 by URL + pinned SHA-256 (update both to move on), unpacks its arch's folder to
  Program Files (HKLM COM server, so not a user-writable folder), `regsvr32 /s`. So "never bundle" holds: it's fetched
  on the user's click. Button only while `Voice.Narrator()` is null. Tested on GAMER11: the task through real UAC (exit
  0, Ava listed, installed 3.36.9 moved to it on `--say`). The button itself not clicked (hidden here now). ARM64 not run.
- **Conversation Mode, mute (3.35.3):** `SessionView.FeedShows` decides, set in `Add`/`StartNotes`, all at once in
  `ShowFeed`; a card's asking bubbles up as `ToolCard.AskingChangedEvent` to the chat. The user wrote "everything
  except the replies"; their own bubbles, errors and questions stay on purpose (a hidden permission would stall a
  session silently). The toggles use Checked/Unchecked: UIA's Toggle raises no Click. `Voice.Speak` is async with
  `WaitUntilDone` so `Voice.Muted` can purge it; the take-over lines are in `Voice.Prompt`, which since 3.36.4 (user: "as little as absolutely
  necessary") caps speech at a few words, one short sentence, no details.
- **Hold to talk (3.36.12; the user: hold, record, on release send as if typed; a keybind; answered briefly and aloud; a
  Talkativeness setting):** the 🎤 never worked: the package's `modules\AI\System.Speech.dll` is a stub throwing
  PlatformNotSupported, the real one in `runtimes\win\lib\net8.0` (the loader now tries that first; `AppletCatalog.FindDependency`).
  `Dictation` records to memory, then recognizes (engine start can't lose words; System.Speech 8.0, desktop
  `MS-1033-80-DESK`, OneCore's DNN one needs WinRT; 0.6 s for 4 s). Heard text goes through `Dictated` + `SendInput`, so
  queue and new tabs work. The note (`AiSettings.VoiceNote`) goes after the message, on the host, stripped by
  `WithoutBrief`; `op: send` has `voice`. The app reads `result` out (`speakReply`), not `--say`: no permission prompt, every
  AI, and a remote tab hears it where you are. Core: `HotkeySpec.Released`; `GetAsyncKeyState` polled while held
  (`ControllerButtons.Released` for buttons). Lesson: a mic opening fires `OnPropertyValueChanged` on the endpoint, each
  firing cost 1.6 s of UI thread in `AudioLevelsView.Refresh` with Voicemeeter (found with `dotnet-stack`): filtered in
  `DeviceService`. Fixture: DaisysDev, `TalkKey` F24 in AI.json, `VoiceMuted` so `Muted: …` logs the reply; `keybd_event`
  0x87. It really sends what the room's mic hears to the session. `%TEMP%\micharness`, `keyharness`.
  Voice and the question chime play on `AppAudio`'s output (3.26.8; notes in `src/DaisysApp/CLAUDE.md`).
- **Files dropped (3.26.7):** non-image files dropped on `RichInput` → `FilesDropped` → chips (`attached`,
  `ShowAttached`) above the box; sent as paths (`Attachments.Note`) appended to `sent` only, the bubble shows
  `Attachments.Line`. Remote: `RemoteAgent.Send(text, images, files)` puts `files` [{name, data}] on `send`; the server
  `Attachments.Save`s them (two weeks) and calls `Send(..., files: paths)`. Not inline chips in the rich text: undo's
  XAML serializing of UI elements (see Images) made that fragile. A new remote tab's first message (`sendOverride`)
  doesn't take files: they stay for the next. Harness checked the round trip only.
- **Tables (3.30.1):** a null Paragraph Foreground draws nothing (`Look.Brush("")` is null: set only when non-null);
  tables are a FlowDocument `Table` (`Markdown.Table`). Harness `%TEMP%\mdharness`.
- **Antigravity (3.29.0, `AntigravityAgent.cs`):** the user asked for Gemini sessions on their Google AI plan (Pixel
  10 Pro's AI Pro, or a Workspace account), not API credits. Gemini CLI lost those plans in June 2026, so: a Provider
  preset `antigravity` running `agy.exe` (`%LOCALAPPDATA%\agy\bin`, 1.4.12; not in the desktop app). Wire (by hand and
  the binary): `--input-format stream-json --output-format stream-json … -p ""` (-p last); in
  `{"event":"user","message":{…}}`; out `init`, `step_update` {step_index, state, step_type agent_response
  (text_delta, usage) | tool (tool_info parameters, output | error)}, `result` {status, response, denied_actions}. One
  process, many turns; no control channel or prompts (headless refuses commands → `daisy_note`), so Stop/mode/model =
  restart with `--conversation`. Saved as ApiAgent's files. Tested: `%TEMP%\agyharness`, and a DaisysDev tab over the
  remote API (`%TEMP%\agyscripts\drive.py`).

## Testing

- DaisysDev (`%TEMP%\DaisysDev`, `%APPDATA%\DaisysDev`): back up `AI.json` first and restore it. UIA by name only, no
  keys or mouse to its window; `%TEMP%\tokui.ps1 -Click "AI|+" -Shot x.png -List` (finds the window by handle; the
  older `v315.ps1` lookup by process fails now). Posted mouse messages don't reach WPF.
- DaisysDev2 as a second PC on localhost for remote tabs; Python clients of the wire in `%TEMP%` (`renametest`,
  `accteditiles.py`), passphrase from DaisysDev's AI.json (DPAPI).
- Reflection harnesses on the built DLL for logic (`%TEMP%\accttest`, `promptstest`, `reqlogharness`, `imgsel`).

## Not tested yet (needs a real turn, a limit or a second account)

- Auto-Compact (3.36.3; only after a push since 3.36.11) and Conversation Mode/voice (3.35.3) in a real turn; Compact after Release, Brief Replies,
  Compacting on screen; Alt+Enter/Enter (3.23.3); no notes' turn after a question (3.22.1).
- A real second Claude account (sign-in, renewal, `/usage`, a switch by click, a switch while waiting for a limit,
  "Back in 1h 01m @ 2:10PM"); mouse-only bits (✕ on a pasted image, Coding bubbles' clicks); Ctrl+Shift+T on a remote
  tab; Show All over 600 messages.

## Offered, waiting on the user

- Antigravity (3.29.0): not tried: Providers' Install, Sign in, Fetch, Test by hand, a Workspace account. Possible
  next: a Google account picker (agy's config folder per account), plan usage if agy reports it.
- Speak with a local Whisper model (≈75–150 MB download on first use), if Windows' dictation proves too inaccurate.
- A per-session Keep session notes switch (the setting is global).
- Daisy's App Mobile: handle `daisy_name` (3.16.2) and send `compact_after_release`/`brief_replies` on `new` (3.16.5).
- Not started: a block above "Claude Account" listing active sessions on every remote PC (needs a server op listing
  running sessions, asked of each of `RemoteSettings.Pcs`).
