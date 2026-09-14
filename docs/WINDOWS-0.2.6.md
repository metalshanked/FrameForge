# Windows 0.2.6

This update applies to the native Windows app and installer.

- **Open capture folder** explicitly opens a new File Explorer window at the library directory. This avoids a Windows shell handoff that could silently show no window. Paths are resolved before launch, and launch errors appear in the app.
- **Delete recent captures:** each card has an accessible × button and a right-click Delete capture action. The editable project or recording and its PNG move together into `%LOCALAPPDATA%\FrameForge\Deleted Captures`. Undo delete restores them; multiple deletions can be undone during the current app session.
- Deleting the open capture stops autosave and clears the editor. Other exports remain unchanged. Locked companion files roll back the move, and Undo refuses to overwrite files that already exist.
- Right-click **Open capture folder → Open deleted captures** to recover earlier deleted files or remove them permanently in File Explorer. Deleted files continue to use disk space until removed.
- Completed captures show and raise the editor, restore a minimized window, preserve maximized layout, and keep automatic clipboard copy. The editor is not left permanently on top of other apps.

The installer upgrades the existing per-user installation and preserves the capture library and preferences. Package output is `dist/FrameForge-Setup-0.2.6.exe`; a portable executable is in `dist/portable/FrameForge.exe`. Public release links are updated when this installer is published.
