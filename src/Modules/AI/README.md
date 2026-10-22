# AI

*A module of [Daisy's App](../../../README.md): the **AI** tab (it was Claude Code before 1.8.0; its tabs, settings and
sessions carried over). Switch it on or off in Settings → General → Modules. Its code is in this folder, and it's
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
- **Don't ask**, or **Allow everything** (Settings → AI): nothing is asked.

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

- **Claude Code.** When your plan's limit stops a turn, Claude Code says so in a message of its own (*You've hit your
  session limit · resets 9:20am (America/New_York)*; before 1.11.1 that wasn't noticed, as the turn isn't an error).
  The tab then waits:
  - a slate-blue breathing orb and dot, "Waiting for the limit", and a countdown;
  - at the time the message gave (in its time zone, turned into this PC's), a minute after (or, if it gave none, when
    the plan's usage says, or in half an hour), it sends **the message** ("Please continue where you left off.", which
    you can change), and Claude picks up where it was. It goes up to 10 minutes early if the plan's usage already says
    the limit's gone;
  - **Stop** cancels the wait;
  - the wait is kept across restarts (`ResumeAt`); the log says when it was planned and when it carried on.
  - It's the same if Claude Code ends at the limit.
- **Other providers.** A rate limit (429), or an overloaded server, is waited out and the same request sent again, as
  long as the service says (`Retry-After`, or Gemini's `retryDelay`) or longer each time (20 seconds, doubling, up to
  15 minutes). Nothing is lost, and Stop ends the wait.

## What it needs

**For Claude Code tabs**, the **Claude Code CLI** installed on this PC; it signs in the way the CLI is signed in, with
**your own Claude account** (or whatever the CLI uses), so nothing is set up again here. Other providers need only
their setup in [Providers](#providers) (Ollama and LM Studio, the program running on this PC). The **Claude Account**
box (the Claude Code box before 2.5.1), at the bottom of the tab's dashboard (the right column), shows who's signed
in, the list of your [accounts](#accounts) (by their names here; the email in the tooltip) and **Usage** beside it (the
"Signed in as" line shows only when there's a problem, or no list). It isn't on a [remote](#remote-control) tab: it's
this PC's account. A problem (not installed, not signed in) also shows as one line at the top of the tab; **✕** clears it,
until it's a different problem (or all is well and a new one comes).

- **Sign in** (when it isn't signed in) runs `claude auth login` in a console window, which opens the browser; the
  box checks again by itself once it's done.

It's checked when the tab first shows, and again whenever the tab shows while there's a problem.

If Claude Code isn't installed, the box (and the line at the top) says so.

- **How to install** opens Anthropic's setup page.
- **Find claude.exe…** points at the program, if it's somewhere unusual. It's looked for in
  `%USERPROFILE%\.local\bin`, npm's folder, then PATH.

## Accounts

You can keep **more than one Claude account** and switch Claude Code between them: when one's limit is reached, say,
carry on with another. One is in use at a time, for every Claude Code tab.

- **The Claude Account box** (bottom right) lists them, the one in use chosen: pick another and Claude Code switches
  to it at once. Then:
  - every Claude Code session here starts on it with its next message: Claude Code is closed for each tab (a tab
    that's working finishes its turn first) and resumed with the session's history, as **End process** does;
  - a session waiting for the old account's limit carries on at once;
  - the plan's usage is read again, for the new account (the readings and pace so far were the other one's).
- **Settings → AI → Accounts** lists them by kind, each with a name of your own here (empty: its email), its email
  and plan, and ✕ to remove it (not the one in use). **Add an account…** asks what kind of account first: only
  **Claude (for Claude Code)** so far, but the list is made to take more (the keys of other AIs are
  [Providers](#providers)). For Claude, it asks how it signs in (a **Claude subscription** or the **Anthropic
  Console**), a name here and the email (filled in on the sign-in page), and **Sign in…** keeps the account in use,
  runs `claude auth login` in a console window (which opens the browser: sign in with the other account, signing out
  of claude.ai there first if it's signed in as someone else) and waits; once Claude Code has the new sign-in, it's
  added and in use.
- **The account Claude Code is signed in to is added by itself** (when the tab checks who's signed in), and signing in
  some other way (Sign in, or a terminal) makes that one the account in use.

**How it works** (`Accounts.cs`): Claude Code keeps one sign-in, in its folder (`~/.claude`, or `CLAUDE_CONFIG_DIR`):
`.credentials.json` (its tokens) and, in `~/.claude.json`, `oauthAccount` (who it is) and `primaryApiKey` (a Console
sign-in). Each account's copy of those is kept in `AI.json` (`Accounts`, `ActiveAccount`), encrypted for this Windows
user (DPAPI), and kept again each time it's checked and before switching away (Claude Code renews its tokens as it
goes). Switching puts the other one's in their place (the rest of `.claude.json` left as it is). Everything else (the
sessions and their history, settings, CLAUDE.md files, skills, MCP servers) stays where it is, shared by every
account. An account kept by another Windows user (or on another PC) can't be read here: add it again.

## Sessions

Each tab is a session with one AI.

- **+** opens a new one. Its first line has the session's **Name** (*Session 1*, *Session 2*… to start with; it's the
  tab's name) and **Which AI?** (Claude Code, a provider, or [another PC](#remote-control); **Providers…** sets them
  up; the one used last to start with) and its **Permissions** (as the dashboard's, below; the ones used last to start
  with). Under them, **Recently closed**: the tabs you've closed (once something was sent in them), the newest at
  the top, with the folder and when it was closed; five show, and the list scrolls for more (the last 30 are kept).
  **Click one** for **Open** (this tab becomes it again, as it was: its name, AI, folder, model, permissions and
  totals, with its conversation shown, ready to carry on) or **Remove** (off the list; the session itself is kept).
  A session on another PC is listed too (*on PC name*): it carried on there, and **Open** connects to it again if it's
  still open there. Then **where it should work**: a folder, typed or picked with **Browse…** (the
  last one used to start with; the first time, `Documents\AI`, made for it). Every session works in a folder.
  - **Read first:** the folder's `.md` files (README and CLAUDE first), each with a tick box. Tick the ones that
    describe the project, and they're sent ahead of your first message: their contents (or, for very long ones, where
    to read them), then *My request*, so the AI reads them before anything you wrote. The chat shows a line *Read
    first: README.md, …* above your bubble.
  - **Previous Sessions:** that folder's earlier sessions with the AI picked (Claude Code's, made here or in a terminal;
    or the agent's conversations, for a provider), by name or first message, newest first; three show, and the list
    scrolls for more. **Click one** for **Open** (carry on with it in this tab) or **Delete…** (after asking: its
    transcript is deleted from this PC; not one open in another tab).

  The next three are for Claude Code.
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
- **Write** in the box at the bottom (the text starts at its top left). **Enter** sends, and **Shift+Enter** starts a
  new line. Slash commands work too, for example `/compact` (with a provider: `/compact`, `/clear` and `/rename`).
  - **The queue.** What you send while it's busy (or paused, or waiting for a limit) waits above the box, and is sent
    when it's done, one at a time: each once the turn before has finished (session notes wait until the queue's
    empty). Each waiting message has **Send now** and **✕** (take it out). **Ctrl+Enter** sends at once instead: Claude
    reads it as soon as it can, in the middle of what it's doing. If a turn ends with an error or Stop, the queue waits
    for you: send something, or **Send now**.
  - **It's rich text.** Formatting pasted in is kept (from Word, Outlook, a web page, an editor, or the conversation
    itself): headings, **bold**, *italic*, `code`, code blocks, lists, links and tables; Ctrl+B and Ctrl+I work as you
    type. It's shown as the conversation shows Claude's text, and sent as Markdown, which every model reads. Code
    copied from an editor becomes a code block, its indentation kept; text with no formatting is pasted as it is.
    Notepad's text (which it copies as rich text in its own monospaced font, Consolas) is read as writing, not code
    (since 2.1.2: it became a code block, in black on the code background).
  - **Images** can be pasted in (a screenshot, an image copied from a browser or an app, or image files copied in
    Explorer) or dropped on the box. Each shows where it is as a thumbnail, and goes with the message, as the image
    itself (up to 1568 pixels a side; a big photo as a JPEG), with *[image 1]*… in the text where it was. Claude Code
    and the providers' models that read images see it; your bubble shows it (click to open it full size).
- **Stop** (or **Esc**) stops what it's doing.
- **Pause** (above Stop, while it works; Pause, Stop and Send are stacked at the right of the box, Send at the bottom) stops it without cutting anything short: it finishes the step it's on (the
  command, edit or reply), and stops there, before the next ("Pausing…" until then). The tab then says *Paused*, and
  **Resume** sends the carry-on message (Settings → AI's, *Please continue where you left off.*) and it picks up where
  it was. Handy when you're near your plan's limit. Messages sent while it's paused wait in the queue; Ctrl+Enter
  sends one at once (and that carries on too).
- **Rename:** click the name of the tab that's showing, type, and press Enter (Esc cancels). A Claude Code session is
  renamed too. If it's running, that happens at once (`/rename`); if not, at its next start (`--name`). It's the name
  `claude --resume` and the CLI's session list show.
- **The tab** shows the provider's name after its own, when it isn't Claude Code.
- **✕** closes the tab and ends its process. A Claude Code session is kept in Claude Code: `claude --resume <id>`
  picks it up, or so does the saved-sessions list in a new tab. A provider's conversation stays in
  `%LOCALAPPDATA%\DaisysApp\AI sessions\` (Settings → General → Storage cleans it up).
- **Search** (🔍 right of the tabs, or **Ctrl+F**): see [Searching the conversations](#searching-the-conversations).
- **The tab's dot** shows what the session is doing, in the same colours as the orb (below). It blinks amber when the
  session needs you, so one working in the background can be seen. If you aren't looking at that tab when it asks (a
  question, or permission), a **Windows notification** says so, with the tab's name (clicking it opens the window),
  and the taskbar button flashes.

Tabs are kept between runs. A tab's conversation so far is read back when it's first shown: from Claude Code's own
transcript, or the provider conversation the agent kept. A session's process starts with its first message, and keeps running between messages until the tab
closes, you click **End process**, or the app closes.

## Session notes

**Keep session notes** (Settings → AI, off to start with) keeps a record in the session's folder to pick the work up
from if a session ends:

- When a request is done (not after a slash command, an error or a limit), the tab sends **the message** (which you
  can change; **Back to the usual message** restores it; the 2.0.0 default, still in use, became the new one in
  2.0.1).
- **In the chat it's one line**, not a bubble: "📝 Bringing the session notes (CLAUDE.md) up to date", then, when
  it's done, "📝 Session notes brought up to date: CLAUDE.md, README.md ▸". What the notes' turn did (its cards, its
  reply, its summary) is under it, closed: click the line to open it (▾), and again to close it. A permission or a
  question in it opens it by itself. The line goes **above the reply** it follows, so that reply (after a release,
  say) stays at the bottom where you read it, rather than being pushed up by the notes. It's the same when the
  conversation is read back, and on a PC using the session by remote control.
- The AI keeps a **Session notes** section in `CLAUDE.md` in the folder (made if it isn't there): what was asked,
  what it did and which files changed, decisions and why, where things stand, and what's next. It keeps it current
  and short rather than a growing log. If the project's `README.md` describes something that changed, it updates that
  too.
- **Your requests, word for word,** go in `REQUESTS.md` in the folder (since 2.0.1), under the date: a log, never
  condensed, to search later (as well as 🔍 Search, which reads the conversations Claude Code still keeps).
- The notes' own turn doesn't ask for notes again. Several messages sent while it's busy get one update at the end.
- **Claude Code reads `CLAUDE.md`** by itself when it starts in that folder, and the module's agent is told to read
  it, so a new session (with any AI) starts from the notes.
- Not in your user folder or a drive's root: Claude Code reads a `CLAUDE.md` in every folder above the one it works
  in, so notes there would reach every project under it. The tab says so once instead.
- It's one more (short) turn per request, so it uses a little more of your plan, and with **Ask before acting** the
  edit to `CLAUDE.md` is asked about like any other.

## Remote control

Use the sessions on one PC from another (`Remote.cs`): open a session running there, or start a new one there. What
it does shows on both PCs as it happens, and either can write to it, answer its questions, stop it, or change its
model and permissions. It's switched on in **Settings → AI → Remote control**, on both PCs, with the same passphrase:

- **The server** (the PC the sessions run on): **Let another PC use the sessions here**, its **port** (38863 to start
  with) and the **passphrase** (8 characters or more). It says when it's listening, on which of this PC's names and
  addresses, and how many PCs are connected. The first time, Windows asks whether to let Daisy's App through its
  firewall: allow it on private networks.
- **The client** (the PC you're at): **Use other PCs' sessions from here**, and the list of PCs to connect to (since
  2.4.0; one before, which became the first in the list). **Add a PC…** and **Edit…** open a small window: its
  **Name** (what its tabs show here; empty: its address), **IP Address** (or its name), **Port** and **Passphrase**
  (each server's own), and **Test** (beside Save), which connects and says how many sessions are open there. ✕ removes one (its
  tabs here stop connecting). A changed address or port carries its tabs with it (`RemotePcWindow.cs`).

A server takes several clients at once, and a client can have tabs on several servers. On the client, **+** (a new
tab) → **Which AI?** → **Another PC: …** (one line for each PC in the list). It connects, and shows:

- **Its sessions** (each with its state's dot, name, AI and folder): click one to open it in this tab. Its
  conversation so far comes with it (the last 300 messages, long results cut short), then everything as it happens.
  A question it's asking shows here too; whichever PC answers first, the other's buttons go.
- **Or start a new one there:** which AI there (that PC's Claude Code or providers), the folder there (its last one to
  start with; typed, or picked with **Browse…**), and that folder's `.md` files to **read first**. Write below and
  send: the session starts there, with the name in the Name box, and opens a tab there as well.
- **Browse…** opens that PC's folders (`RemoteFolderWindow.cs`): its drives and your Home, Documents and Desktop
  there at the left, the folder's subfolders at the right (double-click to go in, **Up** to go back, or type a path
  and press Enter). **New folder…** makes one in the folder showing, and goes into it. **Use this folder** takes the
  subfolder picked in the list, or else the folder showing. Both PCs need 1.11.6 or later for it.

A remote tab shows *⇄ PC name* after its name; its dashboard shows the folder and the AI there. It's kept between
runs, and connects again when it's shown (or, if the connection drops, when you send again). **Closing it here only
disconnects:** the session carries on there, even mid-task (it isn't stopped, and nothing asks), and you can pick it
back up from **Recently closed** on a new tab, or from **Its sessions**; a question it asked meanwhile shows when you
do. Closing the tab there tells this PC. The session runs as the server's settings say:
its **Allow everything**, carrying on after a limit and session notes are the server's. **Its dashboard's plan
meters are the server's** (since 2.6.0): the server sends its plan's usage when the session opens and again as it
changes (`{op: "usage"}`: each window, whether the limit's reached, and its account's name, shown under the meters as
*On PC · account*), never this PC's; an older server doesn't, and the meters are left out. This PC's Claude Account box
and usage details don't show on a remote tab. Export is for sessions on this PC.

**What it sends.** Each message goes in a frame of its own, but what's queued goes out together after a moment
(40 ms), so a reply streaming in (a message every few words) is a few writes a second, not two packets per message.
The AI log notes, when a connection ends, how much it sent and received (*disconnected (… ; sent 1.2 MB, received 3 KB)*).

**Security.** The two PCs connect over TCP. The server sends a random salt; both make a key from the passphrase with
it (PBKDF2-SHA256), and everything after is encrypted and checked with AES-GCM, each message numbered so none can be
replayed or reordered. A PC without the passphrase is turned down (after a pause, so guessing is slow), and nothing
it sends is read. The passphrase is kept encrypted for this Windows user (DPAPI). Anyone with the passphrase can have
the AI read, change and run things on the server (as each session's permissions say), so use a long one, and only on
networks you trust (at home, or over a VPN).

**Other modules use the same connection** (since 3.0.0; Core's `Shell/RemoteChannels.cs`): the same port, passphrase
and list of PCs, so nothing else needs a port of its own. A module serves a channel by name, and a PC asks for it with
`{op: "channel", name}` as its first message; from then on the connection is that module's until it closes. The AI
module fills in the transport when it starts (`RemoteChannels.Connect`, `Pcs`, `Serving`). Tools → Notepad's
`notepad` is the first. A PC without that module (or with it off) answers with an error saying so.

## Searching the conversations

**🔍 Search** (right of the session tabs), or **Ctrl+F**, opens a search bar above the conversation (`ChatSearch.cs`):

- **The words to look for**, in any case; it searches as you type (Enter at once, Esc closes it).
- **Whose words**: **Everything**, **What I said** (your messages) or **The replies** (the AI's text). Tool calls and
  what came back aren't searched.
- **Where**: **This session** (the tab showing), or **All sessions**: every Claude Code session on this PC (made here
  or in a terminal, in any folder) and every provider conversation the agent kept.

It reads the whole conversation from its file (Claude Code's transcript, or the agent's), not just what the tab
shows, so older messages are found too. The results are listed newest first (up to 200), each with who said it, when,
the session's name (for all sessions) and the words around the match, marked. **Click one** to go to it: its tab is
shown (opened in a new tab if it isn't open, as a saved session would be), and the message is scrolled to and
blinks. A message older than what the tab shows (its last 300) opens out in full in the result instead.

**💬 Everything I've said** (in the search bar, since 2.9.0; `SaidWindow.cs`) opens a window with everything you've
written: this session's messages at the top, and every session's in the same project (its folder) at the bottom,
oldest first, with each pane's message and word count. A splitter between them sets their sizes. **Ctrl+F** (or the
Find box) finds words in both: every match marked, how many (*3 of 27 · 5 in this session, 22 in the project*), Enter /
F3 for the next and Shift+Enter / Shift+F3 for the previous, **Match case** and **Whole word**.

**A sound when it asks you something** (since 2.9.0): Claude's questions, or asking to go on, play Windows' messaging
notification sound, whether you're looking or not. Settings → AI turns it off.

## Your plan's usage

The app keeps track of your Claude plan's limits for as long as it runs, whether or not this tab is open
(`PlanUsage.cs`, `UsagePanel.cs`). It gets them two ways:

- **It asks Claude Code.** Every 10 minutes (3 when you're close) it runs `claude -p /usage`, which asks no model, so
  it uses none of your plan. That gives each window's use and reset time, and what's been using it.
- **Each request's rate-limit report.** While a session runs, each request updates the figures, to the minute.

**In a Claude Code session's dashboard** (its Usage card) this 5 hours and this week each show a bar, how much is
used and the countdown to its reset (for a session on another PC, that PC's: see [Remote control](#remote-control)).
There was a line at the top of the tab saying the same; it went in 2.6.0. (With nothing to show, why is in the
Claude Account box.)

**Usage** (in the Claude Account box) opens the details, at the top of the tab (not on a remote tab); **Hide usage**
there, or **Hide details** at the bottom of it, closes it:

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
    back 2:40 AM") when you're out. Only while a Claude Code session here is working (since 2.0.2): once it's done,
    the bubble goes (the dashboard and the tray menu still show the usage).
  - **Check every** 5–60 minutes.

The tray menu (AI) shows both windows with their countdowns, and a Claude Code session's dashboard plan meters show
the time to each reset. The readings and warnings are kept in `AI.json` (`Usage`).

## The conversation

The same for any provider (below, "Claude" is whichever AI the tab uses).

- **Your messages** are bubbles on the right, their formatting shown (as Markdown) and their images under them.
- **Claude's text** streams in as it's written, then shows as Markdown (headings, lists, code blocks, tables,
  `code`, **bold**, links). It can be selected and copied. It's what Claude tells you, so it stands out: at the left
  edge, with an accent-coloured bar and a tint behind it, while the tool cards and the notes (a turn's summary, errors)
  are indented under it. When it was written is at its bottom right, in small type (with the date, if it wasn't
  today; read back from Claude Code's transcript for earlier replies; the agent's transcripts don't keep it). Only
  Claude's text has it.
- **Thinking**: a chip shows how long Claude thought and how many tokens it used. The thoughts themselves aren't
  shown, as in the CLI. It stays at the left edge.
- **Each tool call is a card** in its kind's colour, with what it does in one line (the file, from the session's folder;
  the command; the search) and its status and time. While its input streams in, the card shows how much has arrived.
  Cards start closed, showing only that title line (▸; one line: a longer title, such as a command with no description, ends in …); click one to open its details (▾), and again to close it:
  - a **command**'s command line (the title shows what it's for, or the command);
  - an **edit** as red (removed) and green (added) lines;
  - a **new file**'s first lines;
  - an agent's prompt;
  - what came back (or the error).

  A failed step opens by itself (not a command's: its ✗ shows), and so does a command it asks permission to run. A
  **to-do list** shows as a checklist: done ticked, the one in
  progress bold. The steps an **agent** (Task) takes are listed under its card.
- **Permission:** when Claude asks to use a tool, its card shows the question with three buttons:
  - **Allow**.
  - **Allow all this session**: the rest of the session's requests are allowed without asking.
  - **Always allow everything**: switches on **Allow everything** (Settings → AI): nothing is asked again, in any
    session, until it's switched off.
  - **Deny**: Claude is told no, and asks you what you'd like instead.

  What it asks about is set by the session's **permissions** (below). Claude Code itself lets reading and safe commands
  through without asking.
- **Claude's questions** (its AskUserQuestion tool): its card shows each question, with its options (pick one, or
  several where it says so; each with what it means) and **Other** for your own words, then **Send**, or **Skip** to
  let it carry on without. They're always shown, even with **Allow everything** on (only you can answer them), and
  with remote control on either PC (both need 1.11.10 or later).
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

  Beside it: what it's doing ("PowerShell: List files in the working folder", two lines at most; the rest in its
  tooltip) and how long the turn has taken. The box keeps its height as they change.
- **Activity**: the speed Claude is writing at (tokens a second, over the last 3 seconds), the tokens written this
  turn, and the **activity ribbon**. That's the turn's steps as coloured blocks, each as wide as it took: thinking
  purple, writing blue, then each tool in its kind's colour (looking teal, changing green, running orange, the web
  violet, agents pink, plans yellow). Point at one for what it was and how long. Under it, how long the turn has
  spent on each, a line each: *● Thinking 12 s*, *● Reading 3.1 s*, *● Waiting*, *● Web Access*, *● Other Tools*…
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
  - **Allow Everything**: the same switch as Settings → AI's ([below](#settings--ai)), here too, for every session.
    While it's on, its name is red (since 2.0.1; there was a red line under it before).
  - **Compact**;
  - **Export**, to carry on with it on another PC (Claude Code);
  - **Sess ID** (copy icon): copies the session id, for `claude --resume <id>` in a terminal;
  - **Process** (red, with a red ✕): ends the process.

  A new session starts with the model and permissions used last.

## Settings → AI

The cards are in two columns, four rows (Accounts and Providers; Permissions and When a limit is reached; Session
notes and Remote control; How the conversation looks), with the **Preview** under them, the whole width. The cards
have no explanations under their headers (since 3.0.0): this guide is where they're explained.

**Accounts:** the accounts kept, and **Add an account…** ([above](#accounts)).

**Providers:** the ones set up, and **Providers…** ([above](#providers)).

**Permissions:**

- **Allow everything: never ask (dangerously skip permissions)**, off to start with: nothing is ever asked, in any
  session, with any AI. It's Claude Code's `--dangerously-skip-permissions`:
  - every session runs in its bypass mode, and anything that would still ask is allowed;
  - it can also be switched on from a question (**Always allow everything**) or a session's dashboard (**Allow
    everything, in every session**), and off from either place;
  - it takes effect at once: running sessions switch (`set_permission_mode`), and a question already showing is
    answered;
  - while it's on, each session's dashboard shows **Allow Everything** in red and its own Permissions setting is greyed out.

  Every Claude Code session is started with `--allow-dangerously-skip-permissions` so it can switch without
  restarting. The agent's sessions simply stop asking.

**When a limit is reached:** carry on by itself when the limit resets, and the message it sends then
([above](#when-a-limit-is-reached)).

**Session notes:** keep them, and the message that asks for them ([above](#session-notes)).

**Remote control:** this PC as the server (on, port, passphrase, and whether it's listening) and as the client (the
list of other PCs, each with its name, address, port and passphrase, and **Test**) ([above](#remote-control)).

**How the conversation looks**, shown in the preview. The open tabs follow at once.

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

It's kept in `AI.json` (`Accounts`, `ActiveAccount`, `Providers`, `SkipPermissions` (allow everything), `AutoResume`, `ResumeMessage`, `Remote`, `Look`). The code is
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
  AskUserQuestion comes the same way (even when everything's allowed); it's answered with allow and the tool's input
  plus `answers` (question → the chosen label, several joined with ", ", or your own words), or deny for Skip.
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
  - the accounts (their sign-ins encrypted) and the one in use;
  - the providers (their keys encrypted);
  - the plan usage readings and warnings, the look, allow everything, carrying on after a limit;
  - `ClaudePath`, if you pointed at claude.exe.
- **Claude Code's sessions** are its own, in `~/.claude/projects\`.
- **A provider's conversations** are in `%LOCALAPPDATA%\DaisysApp\AI sessions\<id>.json`.
- **Its log** is `AI-<date>.log` (`ClaudeCode-<date>.log` before 1.8.0).

| Code | What it is |
|---|---|
| `AiApplet.cs` | The module: the tab, its Settings page, the plan usage watched from the start, the tray menu |
| `AiView.cs` | The tab: who's signed in (the Claude Account box), the usage details, the session tabs (rename, close, the dots), Search |
| `ChatSearch.cs` | Searching the conversations: this session's or every one's, what you said or the replies, going to a result |
| `SaidWindow.cs` | Everything I've said: this session's and the project's messages, with Ctrl+F find |
| `SessionView.cs` | One session: the conversation, the box, the dashboard, a new tab's start panel (name, which AI, recently closed, folder, read first, previous sessions, another PC), carrying on after a limit, session notes, its side of remote control |
| `RichInput.cs` | The box you write in: rich text, pasted formatting (RTF, HTML, XAML) and images, sent as Markdown with the images |
| `Remote.cs` | Remote control: the encrypted connection, the server (this PC's sessions for another, its folders to browse, its plan's usage), and a remote tab's agent |
| `RemotePcWindow.cs` | Adding or changing one of the other PCs (remote control): its name, address, port, passphrase, Test |
| `RemoteFolderWindow.cs` | Picking (or making) a folder on the other PC for a new session there |
| `IAgent.cs` | What a session talks to: Claude Code or the agent, both in Claude Code's stream-json |
| `ClaudeProcess.cs` | A Claude Code session: the CLI run with stream-json both ways, permission answers, the Windows job |
| `ApiAgent.cs` | A provider's session: the agent, its tools, waiting out rate limits, the conversation kept |
| `Accounts.cs` | The accounts: their kinds, what's kept of each, and Claude Code's sign-in kept and switched |
| `AccountWindows.cs` | Add an account… (its kind, then signing in) |
| `ClaudeCli.cs` | The CLI on this PC: claude.exe, sign-in, transcripts, saved sessions, export and import |
| `ProvidersWindow.cs` | Providers…: the presets, keys, models, Test |
| `PlanUsage.cs`, `UsagePanel.cs` | Your Claude plan's usage: `/usage` and the rate-limit reports, pace, warnings, the details panel and chart, and the snapshot sent to a remote tab |
| `ChatItems.cs` | The conversation's pieces: Claude's text (Markdown), your bubbles, notes, the session notes' one line (`NotesGroup`), the thinking chip, tool cards |
| `Gauges.cs` | The orb, the context ring, the meters, the activity ribbon, the colours |
| `Look.cs`, `AiSettingsView.cs`, `AiSettings.cs` | The look, Settings → AI, and what's saved |
