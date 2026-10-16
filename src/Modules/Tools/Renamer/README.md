# Tools: Renamer

*A tool in [Daisy's App](../../../../README.md)'s [Tools](../README.md) tab: its **Renamer** sub-tab, which renames many files (and folders) at once, like [Bulk Rename Utility](https://www.bulkrenameutility.co.uk/). Its code is in this folder, part of the Tools module.*

## Using it

1. **Pick a folder** at the top: type or paste its path, **Browse…**, pick a recent one, or drop a folder on the tab.
   The arrow goes up a folder; refresh (or F5) reads it again.
2. **Choose what's listed** (Bulk Rename Utility's Filters): **Show** takes names with `*` and `?`, several
   separated by `;` (e.g. `*.jpg; *.png`); the chips list **Files**, **Folders** (to rename them too), what's in
   **Subfolders**, and **Hidden** files.
3. **Tick** the entries to rename (all are ticked to start; **All**, **None**, **Invert**, or Space for the selected
   lines).
4. **Change the panels** under the list. The **New name** column shows each result as you type: blue for a new name,
   grey for unchanged, red for one that can't be used (two entries with the same new name, a name that's taken, or one
   Windows won't take: point at it for why). A panel that changes something has its title in blue.
5. **Rename**. It asks first, renames the ticked entries shown in blue and leaves the red ones. **Undo** puts the last
   rename's names back (even after closing the app). **Reset** puts every panel back.

The list's order is the numbering's order: it's sorted as Explorer sorts (`2` before `10`), and clicking a column's
header sorts by it, and the numbers follow.

## The panels

They're Bulk Rename Utility's, numbered as there and applied in that order, to the name without its extension
(except where it says):

| | Panel | What it does |
|---|---|---|
| 1 | **RegEx** | A regular expression (.NET) and what a match becomes (`$1`, `$2`… for its groups): `^(\d+)-(.*)$` → `$2 $1`. **Include the extension** to match the whole name; **Match case**. |
| 2 | **Name** | **Keep** it, **Remove** it, a **Fixed** name for all (add Numbering to tell them apart), or **Reverse** it. |
| 3 | **Replace** | Text, and what it becomes (empty removes it); **Match case**. |
| 4 | **Case** | lower, UPPER, Title (each word's first letter; not after an apostrophe), Sentence; **Except** words kept as written (`USB; DVD; of`). |
| 5 | **Remove** | The **First** and **Last** *n* characters, **From** one position **To** another, these **Chars** anywhere, whole **Words**, **Crop** everything before or after a text; and **Digits**, **Symbols**, **Accents** (é → e), **Non-English letters**, **Double spaces**, **Trim** (spaces at the ends), **Leading dots**. |
| 6 | **Move / Copy** | Copy or move the first or last *n* characters to the start, the end or a position, with a separator. |
| 7 | **Add** | A **Prefix**, text **Insert**ed at a position (negative counts from the end), a **Suffix**, and **Word space** (a space before each capital after a small letter: `HolidayPhotos` → `Holiday Photos`). |
| 8 | **Auto Date** | The date it was **Modified**, **Created**, the **Photo taken** (from a JPEG's or TIFF's details; else modified) or **Today**, as a prefix or suffix, in any format (`yyyy-MM-dd`, `yyyyMMdd_HHmmss`…), with a separator. |
| 9 | **Append Folder Name** | The folder's name as a prefix or suffix, one or more folders up, with a separator. |
| 10 | **Numbering** | A number as a prefix, a suffix, both, or at a position: **Start at**, **Step**, **Digits** (zeros in front), a separator, and a **Style**: 1 2 3, hexadecimal, a b c, A B C, I II III, i ii iii. **Start again in each folder** with Subfolders. |
| 11 | **Extension** | As it is, lower, UPPER, Title, a **New** one, an **Extra** one after it, or **Remove** it. |

The panels sit in three columns under the list, each closed to start (just its title, in blue when it changes
something): click a title to open or close it, and an open one pushes the ones under it down. Hold a title and drag
it to move the panel to another place, in any column (a line shows where it goes); the columns needn't hold the same
number. Where they are is kept, and so is what's in them.

## How it renames

Each entry first goes to a temporary name beside it, then to its new one, so names can swap (`a` → `b` and `b` → `a`)
and changes of case alone (`photo.JPG` → `photo.jpg`) work. Entries inside a folder are renamed before the folder, so
a folder and its files can be renamed together. A failure (a file open in a program) is reported and that entry keeps
its name; the rest go on. Undo renames the other way round, folders first.

**Code:** `RenamerApplet.cs` (the sub-tab), `RenamerView.cs` (the list, the panels and the preview, worked out on a
background thread), `RenameOptions.cs` (the panels' settings), `RenameEngine.cs` (a new name, and whether Windows takes
it), `RenameRunner.cs` (renaming, and Undo), `RenamerSettings.cs` (`Renamer.json`: the folder, the filters, the
panels, where they are, and the last rename).
