# FrameForge user guide

## Capture and edit

Completed region, window, full-screen, and scrolling screenshots are automatically copied to the Windows clipboard and opened in the editor. Paste immediately with **Ctrl+V** in an app that accepts images. The status bar confirms **Capture copied to clipboard**. Cancelling a capture keeps the previous clipboard contents. If the clipboard is temporarily unavailable, your capture stays open and you can retry with **Copy image**. After adding annotations, use **Copy image** to copy the edited version.

- **Region:** drag over any visible part of the desktop. A cyan dashed border, black-and-white outline, corner markers, and live pixel dimensions show the exact selected area. Escape, right-click, the visible Cancel button, or Alt+F4 cancels. Switching to another app also cancels. An idle overlay closes after two minutes.
- **Window:** choose a visible window; FrameForge brings it forward and captures its visible bounds.
- **Screen:** captures the entire virtual desktop, including multiple monitors.
- **Delay:** choose 0, 3, 5, or 10 seconds; optionally include the cursor. Escape, the countdown's Cancel button, or closing the countdown cancels the capture.
- **Scrolling:** select the scrolling content, excluding fixed headers, footers, and scrollbars. Use **Auto scroll**, or scroll down manually and use **Add after 2s**. Keep roughly 30–70% of the previous content visible. **Finish** opens the stitched image in the editor. Capture currently scrolls downward only.

Draw arrows, lines, boxes, ellipses, pen strokes, text, callouts, highlights, numbered steps, blur, pixelation, and opaque redaction. Use **Select** to move an annotation or resize with its lower-right handle; double-click a text annotation to edit it. Use the right panel to change properties, then **Apply style to selected**. Hold Shift while drawing for equal dimensions. Arrow keys move a selected annotation; Shift+arrow moves it farther.

**Crop**, **Resize**, and **Rotate** flatten the current annotations into the image. Undo restores the earlier image and layers. Undo history holds up to 40 operations and is kept for the current open document only.

**Copy image** puts bitmap and PNG formats on the Windows clipboard. **Export image** writes PNG, JPEG, BMP, or TIFF. **Save project** preserves the original image and editable annotations in `.ffg` format. Images can also be pasted, dragged into the editor, or printed.

Use opaque **Redact**, then export an image when sharing sensitive content. Blur is a visual effect, not secure redaction. Editable `.ffg` projects intentionally retain the original pixels, including anything covered by annotations.

## Recording and OCR

**Record screen** offers 15 / 30 / 60 fps, a cursor toggle, default-speaker system audio, default microphone input, and optional webcam picture-in-picture. Click **Find cameras** to enumerate DirectShow cameras. Select an area and wait for the countdown.

The floating panel provides **Pause**, **Resume**, and **Stop & save**. The panel is excluded from capture on supported Windows versions. Recordings are saved as H.264/AAC MP4 files in the library. The video window plays recordings, saves a copy, trims a section, and exports animated GIFs at 12 fps with a maximum width of 960 pixels.

**Extract text · OCR** runs Windows OCR locally against the current rendered image. You can edit the recognized text, copy it, or save a `.txt` file. At least one Windows OCR language must be installed; English recognition passed the included tests. Large images are reduced to the Windows OCR engine's maximum dimensions before recognition.

## Shortcuts

| Shortcut | Action |
|---|---|
| Ctrl + tilde/backtick key | Region capture; Shift is not required |
| Ctrl+Shift+3 | All screens |
| Ctrl+Shift+4 | Choose a window |
| Ctrl+Shift+5 | Record a region |
| Ctrl+Shift+6 | Scrolling capture |
| Ctrl+Shift+F10 | Cancel capture / stop recording / finish scrolling |
| Ctrl+O / Ctrl+V | Open / paste |
| Ctrl+C | Copy rendered image |
| Ctrl+S / Ctrl+E | Save project / export image |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+D / Delete | Duplicate / remove selected annotation |
| V / A / R / T / K | Select / arrow / rectangle / text / crop |

Global shortcuts work while FrameForge is running. Click **Shortcuts…** in the left sidebar or **Keyboard shortcuts…** in the tray menu, click the desired action, and press a combination. **Save shortcuts** applies it immediately and saves it across restarts. **Restore defaults** fills in the defaults above; **Cancel** keeps your previous settings. Duplicate bindings and combinations already owned by another app are reported without saving the invalid set. Ctrl or Alt is required; Escape remains reserved for cancellation. Capture shortcuts are temporarily paused while this panel is open so that recording a combination does not trigger a capture.

Shortcut preferences are in `%LOCALAPPDATA%\FrameForge\shortcuts.json`. Closing the editor keeps FrameForge in the system tray by default. Double-click the tray icon to reopen it, or use **Exit FrameForge** to quit. **Preferences** controls close-to-tray behavior and optional startup at sign-in.

## Local library

Captures and edits are automatically saved to `%LOCALAPPDATA%\FrameForge\Library`. The recent strip searches the titles of the 100 most recent images and recordings. Click **Open capture folder** to open this location in Windows File Explorer and manage your saved images, editable projects, and recordings. Opening an external image or project creates an autosaved library copy; the external file changes only when you explicitly save to it.

Logs are in `%LOCALAPPDATA%\FrameForge\Logs`. Failed recording sessions retain recovery files in `%LOCALAPPDATA%\FrameForge\Temp`; successful sessions remove their intermediate files. FrameForge has no account, cloud upload, telemetry, or network service.



Windows can redirect this data location when FrameForge is launched by a packaged desktop app. **Open capture folder** resolves the physical folder before opening Explorer; its tooltip and status bar show that location. Captures remain on the local PC. No cloud storage integration is involved.
