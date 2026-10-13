# Tools: Disk Usage

*A tool in [Daisy's App](../../../../README.md)'s [Tools](../README.md) tab: its **Disk Usage** sub-tab, which shows what takes the space on a drive or in a folder, like [WizTree](https://diskanalyzer.com/) and WinDirStat. Its code is in this folder, part of the Tools module.*

## Using it

Pick a drive from the list at the top (each with how full it is), or type, paste or **Browse…** to any folder, then
**Scan** (or Enter). **Stop** stops a scan.

- **The folder tree**: the scanned drive or folder, then everything in it, **largest first**: each entry's share of its
  folder (a bar), its size and size on disk, how many files and folders it holds, and when anything in it last
  changed. Click a column's header to sort by it (again for the other way); folders and files are sorted within each
  folder. Click the arrow, double-click, or press → and ← to open and close a folder; double-click a file to open it.
  A folder with thousands of entries shows its 2,000 largest, then *… and N smaller entries*.
- **File types**: every extension in the scan, largest first, with its total size, share and number of files; the
  biggest twelve get the colours the treemap uses. Click one to pick it out on the treemap (**Show all types** again).
- **The treemap** (under the tree; **Treemap** at the top hides it for more rows): every file as a rectangle sized by
  its size, inside its folder's, coloured by its type and shaded like a cushion so each folder's files stand out
  together. Point at one for its path and size; click to find it in the tree; right-click for its menu. The entry
  selected in the tree is outlined. Drag the bars between the parts to resize them.
- **Right-click** anything for: **Open**, **Show in Explorer**, **Copy the path**, **Scan just this folder**, and
  **Delete…**, which asks first and moves it to the Recycle Bin (on drives without one, it says it's deleted for
  good); the sizes above it are taken off at once. Delete on the keyboard does the same.

## Fast scan

With **Fast scan** on (the default), an NTFS drive on this PC isn't read folder by folder: Disk Usage reads the
drive's **file table** (the MFT) straight from the disk, as WizTree does, one long read with every file's and folder's
name, folder, size and size on disk. A whole drive with two million files takes a few seconds.

Reading a drive's sectors needs administrator rights, so Windows asks each time (unless Daisy's App is already
running as administrator): a second copy of the app reads the table and hands the result back through a temporary
file (Core's `ModuleTasks`), and the app itself stays as it is. Say no, turn **Fast scan** off, or scan a drive that
isn't NTFS (a USB stick, a network share), and it reads folders instead, several at once (still much faster than
WinDirStat).

The two ways can differ a little: the file table counts a file with several names (hard links, e.g. in
`Windows\WinSxS`) once, as the drive does; reading folders counts it in each. Files OneDrive keeps only online take no
space on the disk, and their size on disk says so.

## Code

`DiskUsageApplet.cs` (the sub-tab), `DiskUsageView.cs` (the page), `DiskUsageModel.cs` (the tree's rows and the file
types), `Treemap.cs` (the squarified treemap with van Wijk's cushion shading, drawn into a bitmap on a background
thread), `DiskTree.cs` (the scan as compact arrays: millions of entries without an object each, numbered so a folder's
entries sit together, largest first), `MftReader.cs` (the file table), `Scanner.cs` (which way to read, and
`FileTableTask`, run as administrator), `DiskUsageSettings.cs` (`DiskUsage.json`).
