# FrameForge 0.2.0 verification

Tested September 11, 2026 on the available Windows x64 desktop.

## Application regression suite

**59 passed, 0 failed** on the final self-contained single-file executable. The build completed without compiler errors. [Raw results](verification/test-results-0.2.0.txt).

Coverage includes annotation rendering, undo/redo, project round trips, crop/resize, redaction, image export, Windows OCR, FFmpeg encoding/trimming/GIF export, native screen capture, enabled selection overlays, cancellation and cleanup, screenshot auto-copy, clipboard preservation on cancellation, scrolling alignment, real recording with pause/resume, and decodable loopback audio.

The new checks verify preferences persistence, quoting of the startup executable with the `--background` option, close-to-tray behavior, restoring the current document, and explicit exit removing the tray icon. The first run exposed a retained tray-visible state after disposal; explicit visibility cleanup fixed it, and the complete suite passed on the rebuilt binary.

Live scrolling grew a 951-pixel viewport to 1,371 pixels. Five frame alignments matched measured scrolling within two pixels.

Tests used isolated data under `artifacts/tests` and generated sample content. GUI tests ran on an unlocked desktop with the test editor activated. Earlier real mouse/keyboard checks for region selection, shortcuts, tactile feedback, and automatic clipboard copying are retained under `docs/verification` with their original version labels.

## Distribution

- App version: 0.2.0; Windows x64, .NET 8 self-contained.
- Portable EXE: 78,304,068 bytes; includes app/runtime dependencies and embedded license notices.
- NSIS installer: per-user setup, optional desktop/startup choices, Start-menu shortcuts, and uninstall support.
- Recording continues to use separately installed FFmpeg.
- Release executables are unsigned; signing and automatic updates are not implemented.

## Limits

Passing these checks does not establish comprehensive feature coverage or broad production readiness. Microphone quality, webcam hardware, long recordings, audio/video synchronization under load, mixed-DPI multi-monitor transitions, HDR, and third-party application coverage still need broader testing. Audio decoding verifies stream validity, not audible fidelity. Scrolling requires stable overlap and currently supports downward capture.

## Installer and desktop integration

**13 package checks passed.** The installed EXE matched the portable SHA256, and the installed copy passed all 34 noninteractive app checks. Fresh installation wrote the expected version, location, Start-menu target, and licenses while leaving startup disabled. Upgrade preserved a user-created file and an existing startup opt-in, updating its executable path. Uninstall removed its executable, shortcuts, and registry entries while preserving user files and application test data.

These install/upgrade/uninstall checks used the installer's /TESTMODE option: installation files and shortcuts stayed under project artifacts, and registry entries stayed under the isolated FrameForge.PackageTest key. Normal user Start-menu and startup settings were not changed. [Package results](verification/package-test-results-0.2.0.txt).

A separate process/UI smoke check verified quiet background startup, relaunch handoff to the same original process, icon rendering, Preferences defaults, embedded license access, and explicit Exit ending the process. A complete Windows sign-out/sign-in was not performed. [Desktop observations](verification/desktop-integration-0.2.0.txt).