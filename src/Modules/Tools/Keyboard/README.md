# Keyboard

*A tool of the [Tools](../README.md) module (since 4.21.1): its sub-tab, switched on or off in Settings → General →
Modules (the Keyboard tick under Tools). Its code is in this folder.*

Any key on the keyboard made to send something else: another key, a key with Ctrl, Shift, Alt or Win (Ctrl + C), or
nothing. It goes by what the key sends, so it works for keys other tools can't name: the **Copilot key** (which sends
Win + Shift + F23) as **Right Ctrl**, never Copilot; or an F key that a keyboard's F Lock switch turns into a media
key (Microsoft's Sculpt), made F2 (Rename) again while the other media keys stay media keys.

The tab lists the remaps: each key, what it sends instead, a box to switch it off, **Edit** and **Delete** (Confirm?).
**On / Off** switches them all. **Add…** opens a small window:

1. **Press the key.** Nothing reaches Windows until every key is let go (so the Copilot key opens nothing). It shows
   what the key sent: the key, with the modifiers it came with.
2. **Keep it** (nothing changes), or pick what it sends instead in **Make it send**: a key from the list (letters,
   modifiers left or right, F1–F24, media keys, the number pad…), or **Press it** and press the key; the Win, Ctrl,
   Alt and Shift chips are held with it. **Nothing** makes the key do nothing. **Save**.

A key added again replaces its remap. A remapped key sends what it's made to send for as long as it's held (its
repeats too), so the Copilot key as Right Ctrl works as Ctrl does: held with C, it's Ctrl + C. A key remapped without
modifiers keeps the ones you hold (Ctrl with a key made F2 is Ctrl + F2).

**Limits.** Windows doesn't let a program send keys to one run as administrator: there, a remapped key does nothing
(unless Daisy's App runs as administrator too). It works while Daisy's App runs (not at the sign-in screen). A key
that sends nothing Windows can see (some keyboards' own keys go only to their makers' programs) shows nothing in the
first step and can't be remapped.

Settings: `%APPDATA%\DaisysApp\Keyboard.json`. Each change is in the Tools log ("Keyboard: … sends …").

## How it works

A low-level keyboard hook (`WH_KEYBOARD_LL`) on a thread of its own, in only while a remap is on or a key is being
pressed for the window. A remapped key is kept from Windows and what it's made to send is sent (`SendInput`), down
while it's down, up when it's let go. Keys are matched by their code and whether they're extended (Enter or the
number pad's), by scan code where Windows has no code for them.

A key that comes with modifiers (the Copilot key) matches when they're held, the one needing the most winning. Win
is what such keys start with, so when a remap needs Win, a Win press is kept back for 60 ms: if the rest of the
remapped key follows, Windows never sees the Win (no Start menu, no Copilot); if anything else comes, or the time is
up, the Win goes on first, as it came (Win + Shift + S, Win alone opening Start, still work). Modifiers already sent
are let go of before the new key goes down, after a key no one uses (0xE8, as AutoHotkey), so a Win or Alt let go of
alone opens nothing. Keys it sends carry a mark, so the hook passes its own by. Keys still held when the PC is locked
are forgotten.

## Code

- `KeyboardApplet.cs` — the sub-tab: the list, On / Off, Add…, Edit, Delete.
- `RemapWizard.cs` — the Add / Edit window: press the key, then what it sends instead.
- `Remapper.cs` — the hook and its thread: matching, sending, Win kept back, capturing a key for the window.
- `KeyNames.cs` — `KeyCombo` (a key with its modifiers), `Remap`, the keys' names and the list to pick from.
- `KeyboardSettings.cs` — `Keyboard.json`.
