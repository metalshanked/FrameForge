# FrameForge 0.2.4 verification

Tested September 11, 2026 on the available Windows x64 desktop.

**Installed application checks: 37 passed, 0 failed. Installer checks: 13 passed, 0 failed.**

## Change

The recent-captures ScrollViewer reserves 16 DIP below its preview cards. Its hover scrollbar occupies that space instead of overlapping the cards. The space remains reserved while the scrollbar is hidden, preventing hover-driven layout movement.

## Verification

- Reproduced the original overlap in 0.2.3 with the existing capture library.
- Inspected the final 0.2.4 portable executable using real Windows input. Preview cards and their titles remain fully visible above the scrollbar when hovered.
- Dragged the horizontal thumb to reveal older captures, then dragged back to the latest captures. The scrollbar stayed below the cards in both positions.
- Moved focus to capture search; the scrollbar hid and the card positions remained unchanged.
- The final installer passed all 13 packaging checks, including executable hash identity, version/location, startup opt-in, shortcuts/licenses, upgrade preservation, and safe uninstall. Its installed executable passed all 37 noninteractive application checks.
- The updated portable app was left running with the existing user library. Captures were preserved.

[Installer results](verification/package-test-results-0.2.4.txt)

## Packages

- Portable: dist/portable/FrameForge.exe
- Installer: dist/FrameForge-Setup-0.2.4.exe
- Portable SHA256: 60545896AE5B5C7C06493EE51EE0763FF372BAC97218CAD3F1C883B606CF4264
- Installer SHA256: C6F92F993F45395F65AC1857CCECE125A44766A4C07847DB7522FBF39F4FF4F8

## Scope

This release changes capture-tray spacing only. The full interactive capture/recording suite was not rerun; the targeted live hover/drag checks and installed noninteractive suite cover this update. Additional DPI configurations were not separately tested. Earlier release limitations remain applicable.
