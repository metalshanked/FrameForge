# FrameForge 0.2.3 verification

Tested September 11, 2026 on the available Windows x64 desktop.

**Application/UI regression: 64 passed, 0 failed. Final installer checks: 13 passed, 0 failed.** The final installed single-file executable also passed all 37 noninteractive application checks.

## Changes

Buttons use flat surfaces, thin borders, subtle hover tint, a small uniform press response, and a brief release tint. The raised shadows and vertical offset are removed. Keyboard focus remains visible, and the motion effects respect the Windows client-area animation setting.

Control heights, padding, and gaps are consistent. Sidebars provide more room, tool icons and captions align in columns, footer controls line up with the main sidebar rows, and capture search aligns with its folder button. Recording/scrolling controls use equal-width rows. Preferences, resize, recording settings, and video export forms fit their content. Resize fields now have explicit width/height labels.

Shortcut settings use the shared input style and place their action row outside the scrolling form. Forward keyboard navigation starts in the shortcut fields.

[Detailed UI / UX review](UI-UX-REVIEW-0.2.3.md)

## Verification

- Inspected the minimum supported window-size render and the live editor; important controls remain reachable and the pinned footer remains visible.
- The 64-check suite covers document editing/export, enabled states, shortcuts, OCR, clipboard, capture overlays and cancellation, real recording/loopback audio, live scrolling alignment, and tray behavior.
- Real Windows input verified Preferences, recording settings, and resize layouts. Their controls and bottom actions fit without clipping.
- In the final packaged executable, inspected the corrected shortcut input spacing, verified Tab reaches the region-shortcut field first, and scrolled the form while its bottom actions stayed fixed. Cancel preserved the existing shortcuts.
- The full UI suite preceded the final shortcut-only layout corrections; those corrections were checked in the final packaged app. The final installed executable passed its noninteractive suite.
- The installer suite passed after the final rebuild, including matching executable hashes, version/location, shortcuts/licenses, startup opt-in, upgrade preservation, and safe uninstall.
- Development UI reviews used isolated project data. The final app was reopened with the existing user library and left running.

[Application/UI results](verification/test-results-0.2.3.txt)  
[Final installer results](verification/package-test-results-0.2.3.txt)

## Packages

- Portable: dist/portable/FrameForge.exe
- Installer: dist/FrameForge-Setup-0.2.3.exe
- Portable SHA256: 73D6A55EFE0E0E520245070C57E12F4DF592FD4EF763B9A10FA28ABCFFD9A43B
- Installer SHA256: 8A7FBD5F7A444AF63AFF625830FD36770B756194B029657709A4665828D88F85

## Scope

Reduced-motion behavior was reviewed in source; the Windows setting was not changed. Additional DPI configurations, touch input, and high-contrast sessions were not separately tested. The earlier release limitations regarding hardware coverage, scrolling capture, signing, and FFmpeg remain applicable. No new cloud integration was added or used.
