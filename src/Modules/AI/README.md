# AI

*A module of [Daisy's App](../../../README.md): the **AI** tab (it was Claude Code before 1.8.0; its tabs, settings and
sessions carried over). Switch it on or off in Settings → General → Applets. Its code is in this folder, and it's
installed to `modules\AI\` (the wrapper offers it too).*

AI sessions in tabs, side by side: chat, and let the AI work on code in a folder (reading, editing and writing files,
running commands, searching), drawn with live gauges rather than lines of text. Each tab picks **which AI** when it
starts:

- **Claude Code**: the [Claude Code](https://docs.claude.com/en/docs/claude-code/overview) CLI itself, signed in as it
  is on this PC (your own Claude account). Everything it does in a terminal, it does here.
- **Any other model with an OpenAI-compatible API**, through the module's own agent: Google Gemini, OpenAI,
  OpenRouter, Ollama or LM Studio on this PC, or any other ([Providers](#providers)).

So when Claude's limit is reached, you can carry on in a Gemini (or local) tab beside it. A session stopped by a limit
also [carries on by itself](#when-a-limit-is-reached) once the limit resets.

## Providers

**Providers…** (when a tab starts, or Settings → AI → Providers) sets up the models besides Claude Code. Add one from a
preset:

| Preset | Address | Key |
|---|---|---|
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | free from Google AI Studio (**Get a key**) |
| OpenAI | `https://api.openai.com/v1` | from platform.openai.com |
| OpenRouter | `https://openrouter.ai/api/v1` | from openrouter.ai (many models, one key) |
| Ollama (on this PC) | `http://localhost:11434/v1` | none |
| LM Studio (on this PC) | `http://localhost:1234/v1` | none |
| Custom | any OpenAI-compatible API | if it needs one |

Each provider has:

- its **name** and **address**;
- its **API key**, kept encrypted for this Windows user (DPAPI);
- its **model**: **Fetch models** lists what it offers;
- its **context window**, for the context ring;
- its **longest reply**;
- its **temperature** (empty is the model's own);
- **extra instructions** added to what the agent is told.

**Test** asks it for a one-word reply. Each tab can change the model, from the provider's list, in its dashboard.

**The agent** (`ApiAgent.cs`) streams the model's reply and runs the tools it asks for, in the tab's folder:

- **Read** (numbered lines)
- **Write**
- **Edit** (an exact piece of text replaced)
- **LS**
- **Glob** (`**/*.cs`)
- **Grep** (a regular expression)
- **PowerShell** (with a time limit)

It asks first as the tab's permissions say:

- **Ask before acting**: changes and commands are asked about.
- **Accept edits**: only commands are asked about.
- **Plan only**: nothing is changed.
- **Don't ask**, or Settings → AI → Never ask: nothing is asked.

Reading and searching are never asked about. A model's thinking (a reasoning field, or `<think>` in its text) shows
as the thinking chip. `/compact` summarises the conversation to make room, and `/clear` starts it again. The
conversation is kept in `%LOCALAPPDATA%\DaisysApp\AI sessions\<id>.json`, so the tab picks up where it left off,
after a restart too.

It speaks Claude Code's stream-json (`IAgent.cs`), so the tab draws everything the same way for any provider:

- the cards and permission questions;
- the dashboard: the context ring from the provider's window, the tokens, the activity ribbon and the files changed.

Only the plan meters and the cost are Claude's alone.

## When a limit is reached

**Carry on by itself when the limit resets** (Settings → AI, on to start with):

- **Claude Code.** A turn stopped by your plan's limit leaves the tab waiting:
  - a slate-blue breathing orb and dot, "Waiting for the limit", and a countdown;
  - when the plan's usage says the limit has reset (or at the reset time it gave), it sends **the message**
    ("Please continue where you left off.", which you can change), and Claude picks up where it was;
  - **Stop** cancels the wait;
  - the wait is kept across restarts (`ResumeAt`).
- **Other providers.** A rate limit (429), or an overloaded server, is waited out and the same request sent again, as
  long as the service says (`Retry-After`, or Gemini's `retryDelay`) or longer each time (20 seconds, doubling, up to
  15 minutes). Nothing is lost, and Stop ends the wait.

## What it needs

**For Claude Code tabs**, the **Claude Code CLI** installed on this PC; it signs in the way the CLI is signed in, with
**your own Claude account** (or whatever the CLI uses), so nothing is set up again here. Other providers need only
their setup in [Providers](#providers) (Ollama and LM Studio, the program running on this PC). The top of the tab
shows who's signed in to Claude Code.

- **Sign in** runs `claude auth login` in a console window, which opens the browser.
- **Sign out** runs `claude auth logout`.
- **Check again** asks again.

If Claude Code isn't installed, the tab says so.

- **How to install** opens Anthropic's setup page.
- **Find claude.exe…** points at the program, if it's somewhere unusual. It's looked for in
  `%USERPROFILE%\.local\bin`, npm's folder, then PATH.

## Sessions

Each tab is a session with one AI.

- **+** opens a new one. It asks **Which AI?** (Claude Code, or a provider; **Providers…** sets them up; the one used
  last to start with) and **where it should work**: a folder, typed or picked with **Browse…** (the last one used to
  start with). For Claude Code it also lists that folder's **saved sessions**, the ones made here or in a terminal, by
  name or first message; pick one to carry on with it. The next three are for Claude Code too.
- **Open a session by its id** in a new tab: paste what Claude Code gave you when it closed (`claude --resume <id>`,
  or just the id) and click **Open**. It's found on this PC, in whichever folder it worked in, and opened there with
  its conversation so far. If that folder isn't on this PC, it's carried on in the tab's folder.
- **From the web or your phone** (a Claude Code session on claude.ai/code): paste its link or `session_…` id. Claude
  Code brings it here (`--teleport`) into the tab's folder, which should be a copy of the same repository; it may ask
  you to sign in to claude.ai or switch to the session's branch. Once it's here, it's an ordinary session of the tab.
  (This is Claude Code's own teleport, and the least tested part.)
- **From another PC**: there, **Session → Export…** saves the session as a file (`<name>.claude-session.zip`: its
  transcript, its agents' transcripts and the tab's name, folder, model and permissions). Here, **Import a session
  file…** in a new tab puts it where Claude Code looks for that folder's sessions (the same folder if this PC has it,
  else the tab's), and opens it. A session already open in another tab isn't opened twice.
- **Write** in the box at the bottom. **Enter** sends, and **Shift+Enter** starts a new line. Slash commands work too,
  for example `/compact` (with a provider: `/compact`, `/clear` and `/rename`). You can send while it's busy, and it
  reads the message as soon as it can.
- **Stop** (or **Esc**) stops what it's doing.
- **Rename:** click the name of the tab that's showing, type, and press Enter (Esc cancels). A Claude Code session is
  renamed too. If it's running, that happens at once (`/rename`); if not, at its next start (`--name`). It's the name
  `claude --resume` and the CLI's session list show.
- **The tab** shows the provider's name after its own, when it isn't Claude Code.
- **✕** closes the tab and ends its process. A Claude Code session is kept in Claude Code: `claude --resume <id>`
  picks it up, or so does the saved-sessions list in a new tab. A provider's conversation stays in
  `%LOCALAPPDATA%\DaisysApp\AI sessions\` (Settings → General → Storage cleans it up).
- **The tab's dot** shows what the session is doing, in the same colours as the orb (below). It blinks amber when the
  session needs you, so one working in the background can be seen. If the window isn't in front when it asks, the
  taskbar button flashes.

Tabs are kept between runs. A tab's conversation so far is read back when it's first shown: from Claude Code's own
transcript, or the provider conversation the agent kept. A session's process starts with its first message, and keeps running between messages until the tab
closes, you click **End process**, or the app closes.

## Your plan's usage

The app keeps track of your Claude plan's limits for as long as it runs, whether or not this tab is open
(`PlanUsage.cs`, `UsagePanel.cs`). It gets them two ways:

- **It asks Claude Code.** Every 10 minutes (3 when you're close) it runs `claude -p /usage`, which asks no model, so
  it uses none of your plan. That gives each window's use and reset time, and what's been using it.
- **Each request's rate-limit report.** While a session runs, each request updates the figures, to the minute.

**At the top of the tab** each window (this 5 hours, this week, and any per-model one) shows a bar, how much is used,
and a countdown to its reset. If the limit's reached, it says when you're back.

**Usage details** opens the rest:

- **Each window**:
  - its bar, with a mark for how far through the window you are in time (used further than that: you're using it
    faster than it lasts);
  - when it resets, as a day, a time and a countdown;
  - **the pace**: how much it's gone up an hour lately (the last hour for the 5-hour window, the last day for a week),
    and from that when it would reach 100% and how long that is before the reset, or how much you'd have used by
    then.
- **The last day** as a chart: this 5 hours (blue) and this week (violet), the warning level, now, the 5-hour window's
  reset, and the pace carried on to it (dashed). It builds up from the readings, which are kept for a week.
- **What's been using it**, as Claude Code reports it (the last day and week on this PC: requests, sessions, long
  sessions, large context).
- **Warnings**:
  - **Notify me** (Windows notifications, from the tray icon; on to start with):
    - when a window passes **80%**, and again at **95%** (both can be changed);
    - **when the pace will reach the limit within the hour**;
    - **when the limit's reached**, saying when it resets;
    - **when a window you were close on has reset**, so you know you can carry on.

    Each is said once per window.
  - **A bubble in the title bar:** amber ("Claude 87%") when a window is past the first level, red ("Claude: limit,
    back 2:40 AM") when you're out.
  - **Check every** 5–60 minutes.

The tray menu (AI) shows both windows with their countdowns, and a Claude Code session's dashboard plan meters show
the time to each reset. The readings and warnings are kept in `AI.json` (`Usage`).

## The conversation

The same for any provider (below, "Claude" is whichever AI the tab uses).

- **Your messages** are bubbles on the right.
- **Claude's text** streams in as it's written, then shows as Markdown (headings, lists, code blocks, tables,
  `code`, **bold**, links). It can be selected and copied.
- **Thinking**: a chip shows how long Claude thought and how many tokens it used. The thoughts themselves aren't
  shown, as in the CLI.
- **Each tool call is a card** in its kind's colour, with what it does in one line (the file, from the session's folder;
  the command; the search) and its status and time. While its input streams in, the card shows how much has arrived.
  Click a card for its details:
  - an **edit** as red (removed) and green (added) lines;
  - a **new file**'s first lines;
  - an agent's prompt;
  - what came back (or the error).

  A **command** shows its command line all the time. A **to-do list** shows as a checklist: done ticked, the one in
  progress bold. The steps an **agent** (Task) takes are listed under its card.
- **Permission:** when Claude asks to use a tool, its card shows the question with three buttons:
  - **Allow**.
  - **Allow all this session**: the rest of the session's requests are allowed without asking.
  - **Deny**: Claude is told no, and asks you what you'd like instead.

  What it asks about is set by the session's **permissions** (below). Claude Code itself lets reading and safe commands
  through without asking.
- After each turn, a line says how long it took, how many steps and how many tokens Claude wrote.

## The dashboard (right)

- **The status orb**, in the state's colour:
  - green when done;
  - purple, breathing, while thinking;
  - blue, rippling, while writing;
  - orange, with a spinning arc, while running a tool;
  - amber, blinking, when it needs you;
  - red after an error;
  - slate blue, breathing slowly, while waiting for a limit to reset ([below](#when-a-limit-is-reached));
  - grey when not running.

  Beside it: what it's doing ("PowerShell: List files in the working folder") and how long the turn has taken.
- **Activity**: the speed Claude is writing at (tokens a second, over the last 3 seconds), the tokens written this
  turn, and the **activity ribbon**. That's the turn's steps as coloured blocks, each as wide as it took: thinking
  purple, writing blue, then each tool in its kind's colour (looking teal, changing green, running orange, the web
  violet, agents pink, plans yellow). Point at one for what it was and how long.
- **Usage**:
  - **The context ring**: how much of the model's context window the conversation fills (what Claude reads each step).
    It's green, then amber, then red as it fills. Near full, Claude Code compacts it; **Compact** does it now.
  - **Tokens this session**, by kind: read from the cache, written to the cache, sent fresh, and written by Claude.
  - **Turns, and about what they'd cost at API prices** (Claude Code only). With a Claude plan, your plan's usage
    is what counts, not that.
  - **Your plan's usage** (Claude Code only): this 5 hours and this week, as Claude Code reports them, with when each
    resets (point at it). If the limit is reached, the conversation says so.
- **Files changed**: each file Claude edited or wrote this run, with how many times. Click one to show it in Explorer.
- **Session**:
  - its **folder** (click to open it), and its provider;
  - the **model**: Claude Code's default, Opus, Sonnet, Haiku or Fable; with a provider, its default or another of
    its models;
  - its **permissions**: ask before acting, accept edits (ask the rest), auto, plan only (no changes), or don't ask
    (bypass permissions). Changing either takes effect at once if the session is running.
  - **Compact**;
  - **Export…**, to carry on with it on another PC (Claude Code);
  - **Copy session id**, for `claude --resume <id>` in a terminal;
  - **End process**.

  A new session starts with the model and permissions used last.

## Settings → AI

**Providers:** the ones set up, and **Providers…** ([above](#providers)).

**Permissions:**

- **Never ask before Claude uses a tool (dangerously skip permissions)**, off to start with. It's Claude Code's
  `--dangerously-skip-permissions`:
  - every session runs in its bypass mode, and anything that would still ask is allowed;
  - you're asked to confirm when you switch it on;
  - it takes effect at once: running sessions switch (`set_permission_mode`), and a question already showing is
    answered;
  - while it's on, each session's dashboard says so in red and its own Permissions setting is greyed out.

  Every Claude Code session is started with `--allow-dangerously-skip-permissions` so it can switch without
  restarting. The agent's sessions simply stop asking.

**When a limit is reached:** carry on by itself when the limit resets, and the message it sends then
([above](#when-a-limit-is-reached)).

**How the conversation looks**, with a preview. The open tabs follow at once.

- **Colours:** the background; Claude's text; your messages and their text; code blocks and their text; the box you
  write in and its text. Each is picked from a palette or typed as `#RRGGBB`, or **Theme** to follow the app's light
  or dark theme.
- **Text:**
  - the font and its size;
  - line spacing (1–2.5× the size);
  - how wide it goes (the whole width, or up to 1800 px);
  - the code font and its size;
  - the size of the tool cards and notes (the whole card scales).
- **The box you write in:** how many lines tall (1–20; it grows to twice that before it scrolls), and its text size.
- **Back to the defaults.**

It's kept in `AI.json` (`Providers`, `SkipPermissions`, `AutoResume`, `ResumeMessage`, `Look`). The code is
`AiSettingsView.cs`, `ProvidersWindow.cs` and `Look.cs`.

## How it works

**A provider's session** is the module's agent (`ApiAgent.cs`): the conversation sent to the provider's
`/chat/completions`, streamed back (server-sent events), the tools run here, and what it does written as Claude Code's
stream-json, so the tab can't tell the difference (`IAgent.cs`).

**A Claude Code session** is the Claude Code CLI itself, run in the background as Anthropic's Agent SDK runs it, so it
does everything the CLI does: your CLAUDE.md files, settings, MCP servers, skills and hooks.

```
claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
       --permission-prompt-tool stdio (--session-id <id> | --resume <id>) --name <tab> [--model m] [--permission-mode p]
```

- **Messages to it** are JSON lines on its input.
- **Everything it does comes back as JSON lines** (`ClaudeProcess.cs`): the streaming text and tool input, whole
  messages, tool results, status, thinking tokens, renames, plan usage, and each turn's result with its tokens and cost.
- **Permission**: before a tool runs it sends a `can_use_tool` request, which the card answers with allow or deny.
- **Stop** sends `interrupt`. The model and permissions are changed with `set_model` and `set_permission_mode`.
- **Every Claude Code process this app starts** (and what those start) is in a Windows job closed with the app, so
  none keeps running unseen if the app ends.
- **The history** comes from `~/.claude/projects/<folder>/<id>.jsonl` (`ClaudeCli.cs`).

## Files

- **`%APPDATA%\DaisysApp\AI.json`** (`ClaudeCode.json` before 1.8.0, carried over by itself):
  - the tabs (each session's id, name, folder, provider, model, permissions, its token and cost totals, and when it
    carries on after a limit);
  - the tab showing;
  - the last provider, folder, model and permissions;
  - the providers (their keys encrypted);
  - the plan usage readings and warnings, the look, never ask, carrying on after a limit;
  - `ClaudePath`, if you pointed at claude.exe.
- **Claude Code's sessions** are its own, in `~/.claude/projects\`.
- **A provider's conversations** are in `%LOCALAPPDATA%\DaisysApp\AI sessions\<id>.json`.
- **Its log** is `AI-<date>.log` (`ClaudeCode-<date>.log` before 1.8.0).

| Code | What it is |
|---|---|
| `AiApplet.cs` | The module: the tab, its Settings page, the plan usage watched from the start, the tray menu |
| `AiView.cs` | The tab: who's signed in, the plan usage line, the session tabs (rename, close, the dots) |
| `SessionView.cs` | One session: the conversation, the box, the dashboard, a new tab's start panel, carrying on after a limit |
| `IAgent.cs` | What a session talks to: Claude Code or the agent, both in Claude Code's stream-json |
| `ClaudeProcess.cs` | A Claude Code session: the CLI run with stream-json both ways, permission answers, the Windows job |
| `ApiAgent.cs` | A provider's session: the agent, its tools, waiting out rate limits, the conversation kept |
| `ClaudeCli.cs` | The CLI on this PC: claude.exe, sign-in, transcripts, saved sessions, export and import |
| `ProvidersWindow.cs` | Providers…: the presets, keys, models, Test |
| `PlanUsage.cs`, `UsagePanel.cs` | Your Claude plan's usage: `/usage` and the rate-limit reports, pace, warnings, the panel and chart |
| `ChatItems.cs` | The conversation's pieces: Claude's text (Markdown), your bubbles, notes, the thinking chip, tool cards |
| `Gauges.cs` | The orb, the context ring, the meters, the activity ribbon, the colours |
| `Look.cs`, `AiSettingsView.cs`, `AiSettings.cs` | The look, Settings → AI, and what's saved |
