# Session notes: AI

Claude Code reads this file only when it works in this folder. Keep it under about 8 KB: what each release did is in
`CHANGELOG.md` and git (`git log -- src/Modules/AI`); here only what a session can't get from the code.

## Decisions and how things work

- **Accounts (3.2.1):** another account than Claude Code's own runs with `CLAUDE_CONFIG_DIR` =
  `%LOCALAPPDATA%\DaisysApp\Claude accounts\<id>` (`ClaudeAccounts.Prepare`: junctions to `~/.claude` folders but
  `backups`, its own `.claude.json` oauthAccount). Never swap the global sign-in: a running claude.exe re-reads
  `.credentials.json` on mtime change, so it moved every session. A switch = the session resumed on the other account
  (`UseAccount` → `Restart(carryOn: false)`). `/usage` (`claude -p /usage`) costs no tokens.
- **Local and remote draw the same (3.15.3):** the same controls, fed locally or by a mirror of the server
  (`PlanUsage.Mirror`/`ApplyRemote`); snapshots only when changed. Protocol: `API.md` only.
- **Mid-turn messages (3.1.1):** a message sent during a tool step is read after it, in the same turn.
- **/compact (3.16.4):** stream-json: `status: compacting`, `compact_result`, a new `init`, `compact_boundary` with
  `pre_tokens`/`post_tokens`. Compact after Release: `/compact` after the notes' turn of a request that pushed a `v*`
  tag (`PushesRelease`). Brief Replies: `AiSettings.BriefNote` on the host only (`WithoutBrief`). **Auto-Compact
  (3.36.3, 🗜):** `AutoCompact()` from `Result`; `compactDue` only from a non-slash bubble (no loop); since 3.36.11
  (the user: "only after replies where you publish to github") also needs `published` (`Publishes`: `git push`,
  `gh release create|upload|edit`, `gh pr create|merge`). Local only. Buttons: 💬 🔊 🗜 Search (the user's order).
- **Compacting (3.37.8–3.37.9):** the ✂ line counts up m:ss (`compactStart`, `CompactingText()`, refreshed in `Tick`,
  which runs while `Busy`; the dashboard and doing line too while `doing` starts "Compacting the conversation ").
  Read back, a transcript line with `isCompactSummary` ("This session is being continued…") is a `FoldedNote`
  (ChatItems: one line, a click opens an `AssistantText`), not a bubble, in `DrawHistory` (remote tabs too). Live,
  the summary never showed. Both built only (`docs/UNTESTED.md`).
- **Tokens (3.14.0–3.22.1):** usage is mostly cached context: the app writes REQUESTS.md itself, read-first skips
  CLAUDE.md, the notes turn runs only after Edit/Write (`filesEdited`), each CLAUDE.md under ~8 KB.
- **Undo (3.37.11; was off with an image since 3.18.7):** WPF undo's XamlWriter throws on a decoded PNG or
  `ImageChip`, so `RichInput` keeps its own (`IsUndoEnabled` false): a `State` per step (each block by
  `XamlWriter.Save`, image containers swapped for a placeholder TextBlock meanwhile, the bitmaps kept), steps closed
  by `Flush` (700 ms idle, Enter, paste, image, Undo). `Clear()` (send) is a step. Harness `%TEMP%\undotest`.
- **Box buttons (3.37.12):** Context; Pause ⏸/▶ and Stop ■ in a `UniformGrid` (`pauseRow`; a collapsed one leaves
  the other the width); `compactButton` (`/compact`) once `Info.Started`, disabled while `compactingLine`; Send + 🎤.
  Ctrl+Shift+1…9 = `tabs[n-1]` (`AiView.TabNumber`, keypad too). Built only.
- **Module Docs (3.8.6):** "Tab → Sub-tab" from README headings in tab order (`AppNavigation.TabsShown`,
  `ShownTab.Named`: the user's AI tab is "Claude"); waits up to 10 s for `TabsShown`.
- **In-session dot (3.24.9):** `AccountsUsage.InUse(ids)` from `AiView.ShowInUse`; sent as `inUse`.
- **Voice (3.25.2):** `--say text` → FileHandoff's pipe `say:` → Core `Speech.Say` → `Voice.Say` (SAPI via `dynamic`,
  a new SpVoice per text on an STA thread; OneCore voices via `Speech_OneCore`). Told by `--append-system-prompt`
  (`Voice.Prompt`; since 3.36.4, the user: "as little as absolutely necessary": a few words, one short sentence).
- **Narrator voices (3.36.9/3.36.12; "local and free", the user):** NaturalVoiceSAPIAdapter v0.2.11 (MIT, third
  party; a Windows update can break it), never bundled: `VoiceAdapter.cs` fetches it on a click, elevated, URL +
  pinned SHA-256 (update both to move on), `regsvr32 /s`. Button only while `Voice.Narrator()` is null. The button
  itself not clicked; ARM64 not run.
- **Conversation Mode, mute (3.35.3):** `FeedShows` decides (set in `Add`/`StartNotes`, all at once in `ShowFeed`).
  "Everything except the replies" (the user); their bubbles, errors and questions stay (a hidden permission would
  stall silently). Toggles use Checked/Unchecked (UIA's Toggle raises no Click). `Voice.Speak` async so
  `Voice.Muted` can purge it. **Doing line (3.37.5–3.37.7):** `doingLine` under `chat` (`activity` is the
  dashboard's grid), `ShowDoing()` from `SetState(state, what, brief)` and `ShowFeed`, while `Busy`. The user asked:
  left edge, one line with … (full text in the tooltip), a Bash/PowerShell with no `description` says only the tool
  name (`brief`). Not seen in a real turn.
- **Hold to talk (3.36.12; not tried by the user):** the package's `modules\AI\System.Speech.dll` is a stub; the real
  one is in `runtimes\win\lib\net8.0`, tried first by `AppletCatalog.FindDependency`. `Dictation` records, then
  recognizes (desktop `MS-1033-80-DESK`). Heard text goes through `Dictated` + `SendInput`; `AiSettings.VoiceNote` on
  the host, stripped by `WithoutBrief`; the app reads `result` out (`speakReply`), not `--say`. A mic opening cost
  1.6 s of UI in `AudioLevelsView.Refresh` with Voicemeeter: filtered in `DeviceService`. Fixture: DaisysDev,
  `TalkKey` F24, `VoiceMuted`, `keybd_event` 0x87 (it really sends what the mic hears).
- **Files dropped (3.26.7):** chips (`attached`) above the box, sent as paths (`Attachments.Note`) in `sent` only.
  Remote: `files` [{name, data}] on `send`, the server `Attachments.Save`s them (two weeks). A new remote tab's first
  message (`sendOverride`) doesn't take files.
- **Tables (3.30.1):** a null Paragraph Foreground draws nothing (set only when non-null); FlowDocument `Table`.
- **Antigravity (3.29.0, `AntigravityAgent.cs`):** Gemini on the user's Google AI plan (Gemini CLI lost those plans in
  June 2026), so a preset runs `agy.exe` (`%LOCALAPPDATA%\agy\bin`). No control channel: Stop/mode/model = restart
  with `--conversation`. Tested: `%TEMP%\agyharness`, `%TEMP%\agyscripts\drive.py`.

## Testing

- DaisysDev (`%TEMP%\DaisysDev`, `%APPDATA%\DaisysDev`): back up `AI.json` first. UIA by name only, no keys or mouse
  to its window: `%TEMP%\tokui.ps1 -Click "AI|+" -Shot x.png -List`. Posted mouse messages don't reach WPF.
- DaisysDev2 as a second PC on localhost for remote tabs; Python wire clients in `%TEMP%` (`renametest`,
  `accteditiles.py`), passphrase from DaisysDev's AI.json (DPAPI). Reflection harnesses on the built DLL
  (`%TEMP%\accttest`, `promptstest`, `reqlogharness`, `imgsel`, `mdharness`, `voiceharness`).

## Not tested yet (needs a real turn, a limit or a second account)

- In a real turn: Auto-Compact, Conversation Mode/voice, the doing line, Compact after Release, Brief Replies, the
  compacting counter and folded summary on screen; Alt+Enter/Enter (3.23.3).
- A real second Claude account (sign-in, `/usage`, a switch, a switch while waiting for a limit); mouse-only bits;
  Ctrl+Shift+T on a remote tab; Show All over 600 messages.

## Offered, waiting on the user

- Antigravity: Providers' Install, Sign in, Fetch, a Workspace account untried; maybe an account picker, plan usage.
- A local Whisper model for speech, if Windows' dictation proves too inaccurate.
- A per-session Keep session notes switch (the setting is global).
- Daisy's App Mobile: handle `daisy_name` (3.16.2), send `compact_after_release`/`brief_replies` on `new` (3.16.5).
- Not started: a block above "Claude Account" listing active sessions on every remote PC (needs a server op listing
  running sessions, asked of each of `RemoteSettings.Pcs`).
