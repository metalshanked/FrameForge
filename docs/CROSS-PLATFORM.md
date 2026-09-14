# FrameForge desktop preview

Version **0.3.0-preview.1** introduces a separate Avalonia application for Windows, macOS, and Linux. The Windows WPF app and its 0.2.5 installer remain available. This preview is an active port, **not full feature parity or a stable cross-platform release**.

## Packages and installation

Build output is in `dist/desktop-preview`. Packages include the .NET runtime; end users do not need the SDK.

- **macOS Apple Silicon:** `osx-arm64.tar.gz`. **Intel Mac:** `osx-x64.tar.gz`. Extract the archive and move FrameForge.app to Applications. Target macOS 14 or newer. These local preview bundles are unsigned and not notarized; macOS acceptance, permissions, and launch behavior require native validation before public distribution.
- **Linux x64 / ARM64:** `linux-x64.deb` / `linux-arm64.deb` for Debian/Ubuntu. Install with your package manager. Portable `.tar.gz` archives are also provided: extract, then run `FrameForge/FrameForge.Desktop`. The archive preserves executable permissions. An interactive desktop, glibc, ICU, OpenSSL, X11/XWayland, Fontconfig, and a desktop portal backend are required.
- **Windows x64:** extract the `win-x64.zip` and run `FrameForge.Desktop.exe`. Keep the files together. This preview is a separate application from the WPF Windows installer.

All features operate locally. FFmpeg and Tesseract are optional external tools, not included or automatically downloaded. FFmpeg enables video recording/export; Tesseract and the requested language data enable OCR. On Debian/Ubuntu the package manager can install `ffmpeg tesseract-ocr tesseract-ocr-eng`. On macOS separately install those tools through your preferred package manager. Configure `FRAMEFORGE_FFMPEG` and `FRAMEFORGE_TESSERACT` when their executables are not on PATH. The Mac app also searches /opt/homebrew/bin and /usr/local/bin.

## Feature coverage

| Feature | Desktop preview behavior |
|---|---|
| Editor and projects | Shared C#/Skia editor on all targets; reads/writes the existing version-1 .ffg schema |
| Annotations | Arrow, line, rectangle, ellipse, pen, text, callout, highlight, numbered step, blur, pixelate, opaque redact |
| Editing | Move/delete marks, undo/redo, crop, resize, rotate; PNG/JPEG/WebP export |
| Windows screenshots | Screen containing the editor; region outline, dimensions, Escape/right-click/cancel button, focus-loss cancellation and two-minute timeout |
| macOS screenshots | Native screencapture selection; Screen Recording permission required. Screen button captures the main display |
| Linux screenshots | XDG Screenshot portal on X11 and Wayland; desktop controls screen/window/region choices and consent |
| Clipboard | Completed captures automatically copy; edited image copy and paste through the native clipboard |
| Scrolling | Manual overlapping captures or stitch selected image files in filename order; downward content only, confidence checks, 80 MP limit |
| Recording | FFmpeg screen recording on Windows, macOS and Linux X11; **video only** |
| Video export | Trim to MP4 or export a GIF using FFmpeg |
| OCR | Offline Tesseract, configurable installed language (default eng) |
| Global capture shortcut | Opt-in in Preferences; Ctrl + physical tilde/backtick by default. Windows RegisterHotKey, macOS Carbon hotkeys, Linux GlobalShortcuts portal |
| Tray/menu bar | Open, capture/stop, exit. Close-to-tray is opt-in; Linux tray visibility depends on desktop support |

The desktop preview does **not** yet provide Wayland recording, automatic scrolling, system/microphone audio, webcam overlay, automatic sign-in startup, resize handles for individual annotations, or complete Windows feature parity. Linux desktop portals may not offer every selection mode or the GlobalShortcuts interface. The app reports unavailable operations instead of taking over input. Native Wayland rendering is not enabled; the UI uses XWayland on Wayland desktops.

Text is entered in the right-hand annotation field before placing a text or callout mark. Select a mark to move it; Delete removes it. Shift constrains equal sides. Escape cancels a draft. Closing saves the current project locally. When reopening an external project, edits are kept in the preview library; use Save editable project to export a chosen file.

Editable projects retain the original image. Use opaque Redact and export a flattened image when hiding sensitive information. Blur and pixelation are visual effects, not secure redaction.

## Local data

The preview has a separate data folder to keep its development work distinct from the Windows release:

- Windows: %LOCALAPPDATA%/FrameForge.Desktop
- macOS: ~/Library/Application Support/FrameForge Desktop
- Linux: ${XDG_DATA_HOME:-~/.local/share}/frameforge-desktop

The library holds automatic editable projects and recordings. Images are exported to the location you choose. `FRAMEFORGE_DESKTOP_DATA` overrides the data folder for testing. The app never uploads screenshots, recordings, OCR text, or projects.

## Build and checks

Use the **.NET 10 SDK** (needed by current Avalonia analyzers), with .NET 8 available for running the portable checks. The application targets .NET 8. Python 3.11+ is used only for packaging.

```sh
dotnet build src/FrameForge.Desktop -c Release
dotnet run --project tests/FrameForge.Core.Checks -c Release -- artifacts/desktop-checks
python scripts/package-desktop.py --rid osx-arm64
python scripts/package-desktop.py --rid osx-x64
python scripts/package-desktop.py --rid linux-x64
python scripts/package-desktop.py --rid linux-arm64
python scripts/package-desktop.py --rid win-x64
```

The packager accepts `--dotnet PATH` and `--skip-build`. Run builds sequentially because they share project intermediates. It creates runtime-specific archives, Mac .app bundles with the original FrameForge icon, Debian packages, and SHA256 files. No developer tools or personal capture data are packaged.

The GitHub desktop workflow builds on Windows, macOS, and Linux, runs the shared-core checks on each native runner, and creates packages as workflow artifacts. It does not publish stable releases automatically. Compiling a runtime package does not prove that its screen-capture permissions or hardware work.

## Native acceptance checklist

On both a Mac and Linux desktop, record OS version, CPU architecture, display scaling, and X11/Wayland session where applicable.

1. Launch from the actual packaged app, open a PNG and an existing Windows .ffg, annotate, undo/redo, export, close and reopen.
2. Capture a region; check the outline/system selector and exact pixel dimensions. Cancel with Escape. Deny screen permission once; verify a usable error and responsive editor.
3. Paste the captured image into another app. Copy an annotated version and confirm the exported pixels.
4. Enable Ctrl + tilde in Preferences. Test from another app and from the tray/menu bar. Choose a conflicting shortcut and verify recovery.
5. Record at least 30 seconds on macOS or X11, stop, play the file, trim and export a GIF. Confirm Wayland recording reports its current limit.
6. OCR a known sample using eng; try a missing language and a missing tool. Verify helpful messages and cancellation.
7. Capture a downward scrolling page with overlap. Check seams, repeated rows, duplicate frames, and incompatible selections.
8. Test multiple monitors, Retina/mixed scaling, tray availability, normal quit, and relaunch. Confirm no overlay can trap the desktop.

Do not describe macOS/Linux support as production-ready until these checks have been completed on the actual systems.

## Platform references

- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [XDG Screenshot portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Screenshot.html)
- [XDG GlobalShortcuts portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html)
- [FFmpeg capture devices](https://ffmpeg.org/ffmpeg-devices.html)
