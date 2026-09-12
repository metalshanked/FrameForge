# FrameForge 0.2.5 verification

Tested September 11, 2026 on the available Windows x64 desktop.

**Installed application checks: 37 passed, 0 failed. Installer checks: 13 passed, 0 failed.**

## Change

Removed competitor comparisons and affiliation disclaimers from Help, the README, third-party notices, and earlier verification reports. Existing limitations and actual dependency license notices are preserved. Help now reads its version from AppBrand.Version.

## Source and dependency review

- Scanned source, documentation, scripts, packaging, licenses, and assets for the competitor product and company names: zero matches after cleanup.
- Reviewed declared and resolved dependencies, native imports, capture/stitching/recording/OCR implementations, and the icon generator. The application uses Windows/.NET APIs, the declared NAudio packages, and separately installed FFmpeg. No competitor libraries, attributed code, or branded resources were found.
- Checked compiled 0.2.5 assembly strings in UTF-8 and both UTF-16 alignments: zero competitor-name matches. Confirmed the updated third-party notice is embedded.
- This is a local source/dependency review, not a comparison against any unavailable proprietary source code.

## Package verification

- All 13 installer checks passed, including installed/portable executable hash identity, version/location, startup opt-in, shortcuts/licenses, upgrade preservation, and safe uninstall.
- The installed single-file executable passed all 37 noninteractive application checks.
- Opened Help in the final portable app using real Windows input and verified its text ends with "FrameForge 0.2.5 · MIT licensed." No competitor references remain in the dialog.
- Left the final app running with the existing capture library. Captures and annotations were preserved.
- The full interactive capture/recording suite was not rerun for this text-only update. Prior generated binaries and test artifacts are historical outputs excluded from the source repository; the naming scan covers current source and the rebuilt release.

[Installer results](verification/package-test-results-0.2.5.txt)

## Packages

- Portable: dist/portable/FrameForge.exe
- Installer: dist/FrameForge-Setup-0.2.5.exe
- Portable SHA256: 585CDAFF13C88FFF8029C7EF70B43DED67D2A744B73C5221100DAC627F7E7003
- Installer SHA256: 56B44DF0935117B0E2902C8FA82A3F0984291624DF8DDBD781282078C6FFD087
