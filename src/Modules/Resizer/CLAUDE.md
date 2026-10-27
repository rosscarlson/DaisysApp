# Session notes: Resizer

Past work on this part of Daisy's App, moved out of the root CLAUDE.md on 2026-10-26 (Claude Code reads this file only when it works in this folder). Entries touching several modules are in each one's notes. Keep it short: condense what's done.

- **3.0.1–3.0.5 (all released, condensed):** Notepad without colours, pop-out to its own Alt+Tab window that reopens out (`NotepadApplet` `holder`, `NotepadSettings.PoppedOut`/`PopBounds`); Notepad Copy/Cut as plain text only (CommandBindings); AI Activity legend a 2-column Grid; Android 1.1.4 (passphrase masked while typed via `setSingleLine()` before `inputType`; full-screen ✕ fades after 3 s, back on a 3 s hold) on the 3.0.3 release; Resizer's "Ways to apply a profile" card and other texts removed (3.0.4 because another session took 3.0.3); Core `Shell/QuickButtons.cs` (0.29) with a notepad icon after the icon-only Update (3.0.5). Commit 367ce90 (3.0.1).
