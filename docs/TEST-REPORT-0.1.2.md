# FrameForge 0.1.2 — verification report

Tested September 11, 2026 on the available Windows x64 desktop. This report describes the packaged build.

**Regression suite: 54 passed, 0 failed.** Build: zero compiler errors. NuGet's vulnerability-data lookup was unavailable because network access to its service index failed; dependencies were unchanged and restored from the local cache. Separate Windows input checks passed as described below. The package includes .NET 8.0.31 and uses the existing FFmpeg 8.1.2 installation. The automated suite uses isolated data and synthetic test content; real-input selection tests captured empty wallpaper regions.

| Area | Evidence |
|---|---|
| Automatic clipboard | Screenshot completion opens the editor and copies pixel-identical content; cancellation leaves the clipboard and open image unchanged |
| Button feedback | Shared button template supplies hover, pressed, click-highlight, keyboard-focus, and disabled states; real clicks visibly changed the button and selected-tool outline |
| Shortcut configuration | Ctrl+tilde default and physical-key recognition; Shift recorded separately; invalid and duplicate combinations rejected; custom profile reload and corrupt-file fallback |
| Selection outline | Production outline renderer checked on a white background; black/white/cyan border, corner markers, and dimensions visually inspected in the synthetic render |
| Overlay lifecycle | All native overlay windows enabled; cancellation via routed Escape, token, window close, timeout, and switching away; repeated cancellation leaves no overlays |
| Image model | 96-DPI normalization, annotation undo/redo, project save/load with matching rendered bytes |
| Annotation tools | Every annotation type renders; opaque redaction exports black pixels; later blur does not reveal the original pixels underneath |
| Destructive edits | Crop clamping and layer restoration with Undo; resize and Undo |
| Exports | PNG, JPEG, BMP, TIFF reopen at the expected dimensions |
| Scrolling math | Exact 240-pixel displacement found; stitched pixels match the reference; duplicate and unrelated frames detected |
| Live scrolling | Final run extended a 951-pixel viewport to 1,287 pixels. Four stitched frames matched measured window scroll movement within 2 pixels; the result was also visually checked for repeated or missing rows |
| OCR | Windows OCR recognized the sample's “Capture. Explain. Share.” and supporting text |
| Video pipeline | H.264/AAC sample encoded; trimmed MP4 decoded; GIF contained 12 frames |
| Editor interface | Sample opened from the welcome button; Undo and Redo buttons worked; image copied and pasted through the app |
| Native capture | App window enumerated; GDI window-area screenshot matched physical dimensions; picker returned the specified 300 × 200 pixel rectangle |
| Actual recording | Two screen-recording segments created through pause/resume, joined, and decoded successfully |
| System audio | Loopback recording generated an audio track that FFmpeg decoded successfully |

The full assertions are in `verification\test-results.txt`. `verification\editor.png` is a screenshot of the running test editor. `verification\scrolling-live.png` shows the live scrolling result. `verification\selection-outline.png` is a synthetic render of the production outline. The source includes the test runner, and `test.ps1` includes a process watchdog.

## Separate Windows input checks

The 0.1.2 checks used the Computer Use Windows input API: clicked Box and observed its click highlight, selected outline, and Rectangle selected status; started a capture with Ctrl+tilde; dragged from logical (300, 180) to (700, 380); observed a 700 × 350 pixel image and the automatic-copy confirmation; then clicked Paste without clicking Copy. The editor opened the matching 700 × 350 image. The final rebuild after these checks only added test-runner foreground synchronization and diagnostics; product behavior was unchanged. See `verification\feedback-and-clipboard-checks.txt`.

The earlier 0.1.1 checks below also used the Windows input API, rather than invoking picker or button methods inside the app. These capture and shortcut paths remain included:

- Opened Keyboard shortcuts, focused the region field, pressed Ctrl plus the physical backtick/tilde key without Shift, and saved. The dialog remained open while recording the combination and closed successfully on Save.
- Restarted FrameForge and verified the preferred shortcut persisted. Pressing Ctrl+tilde opened the region selector.
- A fast drag from logical coordinates (1505, 160) to (1625, 460) on empty wallpaper created a 210 × 525 pixel capture, consistent with the desktop's 175% scale. The editor opened the image and saved it to the local library.
- Started another capture and clicked the visible Cancel button. The overlay disappeared, and the previous image remained in the editor.
- Started another capture and pressed Escape. The overlay disappeared, and the previous image remained in the editor. Window enumeration showed only the editor afterward.

See `verification\desktop-input-checks.txt` for the earlier build hash and observations. The input API completes a drag as one gesture; the outline was inspected through the production renderer's synthetic image rather than a screenshot taken while holding the mouse button.

## Corrections made during testing

- Background test launches initially lost foreground focus to a window outside the test process, dismissing the capture overlays through the existing switch-away cancellation path. The UI suite now waits for foreground activation before starting those checks. With the test window activated through Computer Use, all 54 checks passed, including native input enablement on both detected display overlays.
- Removed an offscreen modal dialog that disabled every capture overlay, preventing mouse and Escape input. The previous geometry-only picker test called internal methods and missed this bug. The replacement uses modeless overlays coordinated by an asynchronous session, and the suite now checks native window enablement as well as teardown.
- Replaced polling of the system cursor during selection with each WPF mouse event's position converted to screen coordinates. A real fast-drag test initially returned no image because mouse-down could read the cursor's final position. The same gesture produced the expected capture after this correction.
- Replaced a failing WPF OLE clipboard flush path with Win32 clipboard writes, retries, and native PNG/DIB reads. Both Copy and Paste then passed.
- Run real screen tests on the interactive desktop: a sandboxed process could not obtain a usable screen DC. The ordinary desktop tests passed.
- Made blur and pixelation operate on the composited layers so that they cannot accidentally expose original pixels beneath an earlier redaction.
- Explicitly activate the selected window during automatic scrolling so scrolling does not depend solely on the user's inactive-window wheel setting.
- Replaced sparse overlap matching that confused repeated rows with exhaustive vertical candidate search and dense, edge-weighted comparison. The earlier height-only live test was insufficient; a new assertion checks every accepted overlap against the test window's actual scroll displacement. The corrected capture was also visually inspected.

## What this report does not prove

This is not production certification or a guarantee of comprehensive feature coverage. The checks do not validate every pointer interaction, every third-party clipboard consumer, device or application, sustained recording load, audio/video synchronization, audible audio quality, actual microphone/webcam operation, every display arrangement, or every OCR language. The visible recording controls are implemented; the recording test directly exercises their underlying recorder rather than driving every control with physical mouse input. Print-dialog behavior was not tested with a physical printer.

See `README.md` for known limits and explicitly unimplemented features. Microphone and webcam support should be tested with the intended hardware before relying on those paths.
