# FrameForge 0.2.1 verification

Tested September 11, 2026 on the available Windows x64 desktop.

**Application regression suite: 61 passed, 0 failed. Installer checks: 13 passed, 0 failed.** The installed single-file app also passed its 34 noninteractive checks. Both release builds completed successfully.

## UI changes and verification

- The supported minimum window size keeps Open capture folder, capture search, Preferences, and Exit within the window. The compact render was inspected for layout.
- Image commands are disabled before an image is open and enabled after loading the sample.
- Real Windows input verified sidebar wheel scrolling, grabbing the thin thumb, the fixed sidebar footer, capture search/no-results/clear behavior, the File Explorer folder action, and selection command availability after drawing and Escape.
- Compiled accessibility output includes names for search, capture delay, annotation text, and palette colors.
- Save project, Copy image, Export image, zoom, and capture controls provide clearer descriptions. Text-box redo retains normal text editing behavior.

See the [UI / UX review](UI-UX-REVIEW-0.2.1.md) for findings and verification scope.

## Regression coverage

The suite covers shortcut persistence and validation, selection outline rendering, tray preferences and lifecycle, image normalization, annotation rendering, undo/redo, project round trips, crop/resize, exported redaction, image export, Windows OCR, FFmpeg encoding/trimming/GIF export, native screen capture, enabled capture overlays, cancellation cleanup, automatic clipboard copying, preservation on cancellation, real pause/resume recording, decodable loopback audio, and live scrolling alignment.

Live scrolling grew a 951-pixel viewport to 1,287 pixels, with four frame alignments matching measured scrolling within two pixels. Tests used isolated project data and synthetic content.

[Full app results](verification/test-results-0.2.1.txt).

## Packaging

The installed executable matched the portable SHA256. Fresh installation registered the expected version/location, shortcuts, and licenses while leaving startup opt-in. Upgrade preserved user-created files and the startup preference. Uninstall removed its executable, shortcuts, and registry entries while preserving user files and app test data.

Installation checks used /TESTMODE under project artifacts with isolated registry keys. The test did not install the app into the user's normal Programs directory or change real startup preferences.

[Package results](verification/package-test-results-0.2.1.txt).

Portable executable SHA256: `DC45B194E2E31AA15343A87067E929ABB1C28CAF668222E2662F74C695B6EF27`.

## Remaining limits

High-contrast scrollbar fallback is implemented but was not tested in a separate high-contrast Windows session. Touch scrolling, additional keyboard layouts, mixed-DPI/HDR displays, microphone quality, webcam hardware, long recordings, and audio/video synchronization under load still need broader testing. Audio decoding verifies stream validity, not audible fidelity. Scrolling depends on stable overlap and supports downward capture.

This early release is unsigned. FFmpeg remains a separate dependency; automatic updates are not implemented, and feature coverage remains incomplete.