# FrameForge 0.2.2 verification

Tested September 11, 2026 on the available Windows x64 desktop.

**37 application checks passed; 13 installer checks passed.** The installed single-file executable also passed all 37 application checks. Development, portable, and installer builds completed successfully.

## Folder-opening fix

The original button passed the logical LocalAppData library path to the Windows shell. When FrameForge inherited a packaged launcher's filesystem context, Windows redirected its data into that launcher's local cache. Explorer could not see the logical destination and reported that it was unavailable.

The app now opens a filesystem handle, resolves its physical path, and launches Windows Explorer explicitly with that path as one argument. This supports redirected folders, ordinary drive paths, and UNC paths without hardcoding a username or launcher package. File handoff to an external video player uses the same path resolution. The folder tooltip and status show the resolved location, and status no longer claims that a request alone proves Explorer opened.

## Real Windows verification

- Reproduced the old folder button failure and Explorer's unavailable-location message.
- Confirmed the redirected library contained the same existing saved files.
- Clicked Open capture folder in the rebuilt development app and observed a visible Explorer window containing the existing captures.
- Closed the diagnostic Explorer windows to establish a clean baseline.
- Reopened the final portable 0.2.2 executable, clicked its folder button, and observed a new visible Library window containing all 14 existing files. Explorer was not manually activated after the click.
- Left the final app and its capture-folder window open.

The existing capture files were preserved. No cloud storage service was used.

## Automated checks

Three new checks cover file identity through resolved directory and file paths containing spaces and Unicode, plus an explicit failure for a missing destination. Existing checks cover shortcuts, preferences, selection outlines, document edits and export, scrolling alignment, OCR, and FFmpeg encoding/trimming/GIF export.

[Application results](verification/test-results-0.2.2.txt)

The installer checks cover file/hash identity, version and install location, startup opt-in, shortcuts and licenses, installed self-tests, upgrade preservation, and safe uninstall. These tests used isolated directories and test registry entries; they did not change the user's real startup settings.

[Installer results](verification/package-test-results-0.2.2.txt)

## Release files

- Portable executable: dist/portable/FrameForge.exe
- Installer: dist/FrameForge-Setup-0.2.2.exe
- Portable SHA256: 730FAAF39C4C5AEC64F0CE9166F936AB6B78F5CBAAD9D3780B40C6E60C9800AA
- Installer SHA256: 3C3F91EDE6ECA6F76681826B73F20926BFDB1C56FBE5F8504D88086120982C27

## Scope

This release reran the noninteractive application suite and installer suite, plus the real folder-opening flow. The broader capture-overlay, live-recording, scrolling, and layout UI suite from 0.2.1 was not rerun for this focused path fix; its results remain in TEST-REPORT-0.2.1.md. External video-player UI was not separately exercised.

The historical UI review now explicitly identifies why the earlier isolated-folder test missed this bug. General early-release limits from the previous report remain applicable.
