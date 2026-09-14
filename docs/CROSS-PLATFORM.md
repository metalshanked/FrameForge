# FrameForge desktop preview

Version **0.3.0-preview.1** is the Avalonia application for macOS, Linux, and Windows. The existing Windows WPF installer remains separate. The version stays unchanged during this development cycle. This is a preview until native desktop acceptance is complete.

## Installation

Packages include the .NET runtime; end users do not need the SDK.

- **macOS 14+:** choose the Apple Silicon (osx-arm64) or Intel (osx-x64) DMG, open it, and drag FrameForge to Applications. A tar.gz containing the same app is also available. Native builds are ad-hoc signed for internal consistency. They are **not Developer ID signed or notarized**, so Gatekeeper may block downloaded previews. Distribution signing and real Mac permission tests remain release work.
- **Debian/Ubuntu Linux:** install the matching linux-x64 or linux-arm64 .deb with the system package manager. For example: `sudo apt install ./FrameForge-Desktop-0.3.0-preview.1-linux-x64.deb`. It declares the required media, OCR, and desktop libraries. A portable tar.gz is available; extract it and run FrameForge/FrameForge.Desktop after installing equivalent dependencies.
- **Windows x64:** extract win-x64.zip and run FrameForge.Desktop.exe with the other files alongside it. The established Windows WPF installer is the recommended Windows application while this port is being tested.

Linux requires an interactive X11 or Wayland desktop with a compatible XDG desktop portal backend. The app renders through X11/XWayland. Portal capabilities vary between GNOME, KDE, wlroots, and other desktops. WSLg is useful for UI and media checks but does not establish full Linux desktop support.

## Features

| Feature | Implementation and limits |
|---|---|
| Screenshots | Windows region selector; macOS native screen, window, and region capture; Linux Screenshot portal with the selection modes offered by the desktop |
| Clipboard | Completed image captures copy automatically; copy edited image and paste an image |
| Editor | Arrow, line, rectangle, ellipse, pen, text, callout, highlight, numbered step, blur, pixelate, and opaque redact |
| Editing | Move and resize marks with handles; duplicate/delete; double-click text to edit; undo/redo; crop, image resize, rotate; PNG/JPEG/WebP export |
| Projects | Version-1 .ffg projects, automatic saving, and rendered PNG companions in the library |
| Recent captures | Search, previews, open, delete with recovery, and Undo delete; captures removed through the app stay in Deleted Captures until the user removes them |
| Automatic scrolling | macOS ScreenCaptureKit plus Accessibility permission; Linux ScreenCast and RemoteDesktop portals with pointer control granted for the session. Select content, scroll down, stop or cancel. Stops on repeated/incompatible frames and retains partial results |
| Manual scrolling | Overlapping captures or stitch image files in filename order; useful where automatic control is unavailable. Downward content, confidence checks, 80 MP limit |
| macOS recording | ScreenCaptureKit screen/window capture, optional system audio and microphone, pause/resume, and MP4 output |
| Linux recording | PipeWire through the ScreenCast portal on compatible X11/Wayland desktops, GStreamer MP4, optional PulseAudio/PipeWire system audio and microphone, pause/resume |
| Windows preview recording | FFmpeg video-only recording; use the Windows WPF application for its existing recording features |
| Video export | Trim MP4 and export GIF; native macOS services, FFmpeg on Linux/Windows |
| OCR | Offline Apple Vision on macOS; local Tesseract and installed language data on Linux/Windows |
| Shortcuts | Opt-in global capture shortcut, default Ctrl + physical tilde/backtick; Windows RegisterHotKey, macOS Carbon, Linux GlobalShortcuts portal |
| Background operation | Optional tray/menu bar, close-to-tray, launch at sign-in, and single-instance activation |

Linux recording needs Python GI, GStreamer, PipeWire, an H.264 encoder/parser, AAC support, and PulseAudio-compatible audio services. The Debian package declares these dependencies; they are installed by the package manager, not downloaded by FrameForge. Portable installations must provide them. FFmpeg enables Linux video conversion; Tesseract enables Linux OCR. Set FRAMEFORGE_FFMPEG and FRAMEFORGE_TESSERACT if these tools are outside PATH. macOS recording, OCR, and conversion use its native services without these tools.

System audio depends on the selected audio output and the OS session. Linux automatic scrolling requires a portal backend implementing RemoteDesktop pointer control. Linux global shortcuts need the GlobalShortcuts portal. If an interface or permission is unavailable, the operation must leave a responsive editor and explain the problem. A webcam overlay and complete Windows feature parity are not implemented.

## Permissions and use

On macOS, allow Screen Recording for capture/recording, Microphone only when selecting microphone audio, and Accessibility for automatic scrolling. FrameForge uses the normal system permission prompts. On Linux, the desktop portal asks which screen/window to share; automatic scrolling additionally asks for pointer control.

Select a mark to move or resize it. Double-click a text, callout, or step mark to edit its text. Delete removes the selected mark. Shift constrains equal sides while drawing. Escape cancels a draft or current capture operation. The scrolling progress window includes Stop and keep; cancellation keeps usable frames already captured.

Closing saves the current editable project. Opening an external project keeps subsequent edits in the FrameForge library; Save editable project exports a chosen file. Use Open capture folder to browse saved projects and PNGs. Removing the current capture clears the editor so autosave cannot recreate it. Undo delete restores its files and refuses to overwrite newer files.

Editable projects retain the original image. To hide sensitive information, use opaque Redact and export a flattened image. Blur and pixelation are visual effects, not secure redaction.

## Local data

The desktop app keeps a separate data folder:

- Windows: %LOCALAPPDATA%/FrameForge.Desktop
- macOS: ~/Library/Application Support/FrameForge Desktop
- Linux: XDG_DATA_HOME/frameforge-desktop, default ~/.local/share/frameforge-desktop

Library contains editable projects, rendered PNGs, and recordings. Deleted Captures contains recoverable deletions. FRAMEFORGE_DESKTOP_DATA overrides the root for isolated tests. FrameForge does not upload captures, recordings, OCR text, or projects.

## Build and checks

Use the **.NET 10 SDK** for current Avalonia analyzers, plus the .NET 8 runtime for the shared checks. The app targets .NET 8. Packaging uses Python 3.11+. macOS packaging requires Xcode command-line tools on a Mac to compile the Swift helper, sign the local bundle, and create a DMG.

```sh
dotnet build src/FrameForge.Desktop -c Release
dotnet run --project tests/FrameForge.Core.Checks -c Release -- artifacts/desktop-checks
python scripts/package-desktop.py --rid linux-x64
python scripts/check-desktop-packages.py --rid linux-x64
/usr/bin/python3 tests/native/check-linux-media.py
```

Other package targets are linux-arm64, osx-arm64, osx-x64, and win-x64. Build sequentially because projects share intermediates. The packager supports --dotnet PATH and --skip-build; Mac builds from another OS additionally require --native-helper PATH from a native build, and do not create a DMG there.

The GitHub workflow builds five native targets, runs shared checks, verifies packages, and runs synthetic native media checks. Mac checks also exercise Vision OCR and native video/GIF conversion. Synthetic sources do not capture the runner's screen or test permission dialogs. Workflow artifacts are previews, not automatic stable releases.

## Desktop acceptance still required

Record OS version, CPU architecture, display scaling, and X11/Wayland session as applicable.

1. Launch the packaged app. Open a PNG and Windows .ffg; move/resize/edit annotations, undo/redo, export, close and reopen.
2. Capture a region/window/screen. Check dimensions and selection outline, cancel with Escape, deny permission, and verify the editor stays usable.
3. Paste a capture into another app and copy an annotated result.
4. Test the global shortcut from another app, a conflict, tray/menu bar, optional sign-in startup, and launching a second instance.
5. Record 30 seconds with no audio, system audio, microphone, and both; pause/resume, stop, play, trim, and export a GIF. Check synchronization.
6. OCR a known sample, another installed language, unavailable language, and cancellation.
7. Automatically scroll a page. Check seams, duplicate frames, partial cancellation, and recovery after a permission denial. Test the manual fallback.
8. Delete the current and another capture, undo, restart, and verify unrelated exports are preserved.
9. Test multiple monitors and mixed scaling. Verify every capture/progress window can close without trapping the desktop.

WSL checks cover the installed Ubuntu userland, synthetic audio/video, and the UI where WSLg supports it. A successful WSL run does not validate GNOME/KDE portal behavior or real Mac hardware.

## Platform references

- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit)
- [Apple Vision](https://developer.apple.com/documentation/vision)
- [XDG Screenshot portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Screenshot.html)
- [XDG ScreenCast portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html)
- [XDG RemoteDesktop portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html)
- [XDG GlobalShortcuts portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html)
