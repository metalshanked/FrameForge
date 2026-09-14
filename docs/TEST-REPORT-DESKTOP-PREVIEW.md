# Desktop preview verification

Version: 0.3.0-preview.1. This report applies to the new Avalonia application, not the existing Windows WPF release.

## Completed checks

- **52 shared-core regression checks on Windows x64** and **52 on Ubuntu 26.04 x64 under WSL**. Coverage includes version-1 project loading/saving, all annotation renderers, undo/redo, opaque redaction including fractional pixel boundaries, crop/resize/rotation, PNG/JPEG/WebP export, malformed input, downward scrolling overlap, duplicate/incompatible frames, atomic file writes, literal process arguments and cancellation.
- The WSL environment lacked ICU. An Ubuntu libicu78 package was extracted under the project's ignored .local/linux-deps folder and supplied through LD_LIBRARY_PATH for these checks. The Debian app packages declare ICU as a dependency. This was not a full Linux desktop GUI test.
- Windows packaged-app UI: launch, flat control layout, Escape cancellation back to the editor, completed region capture, automatic native clipboard copy, paste with identical source PNG bytes and unchanged 262 × 175 dimensions, arrow drawing, and normal Exit with project persistence.
- The region selector has a visible Cancel/Escape control and taskbar entry. Focus loss and a two-minute timeout also cancel it; the latter paths are implemented but were not independently timed in the final UI smoke test.
- Dependency audit of the desktop application, including transitive dependencies, returned no known vulnerable packages. Uses Tmds.DBus 0.95.1 rather than the earlier vulnerable version found during initial restore.
- Compiled using the project-local .NET 10 SDK with no compiler warnings in the final publish runs. The shipped runtime target remains .NET 8.

- **Native GitHub CI passed on all five targets:** Windows x64, macOS Apple Silicon and Intel, and Ubuntu 24.04 x64 and ARM64. Each runner executed the 52 shared-core checks, built its self-contained package, and verified the archive. Both Linux runners also inspected their Debian package metadata.
- [Verified CI run](https://github.com/metalshanked/FrameForge/actions/runs/34798123097), source commit 1a4d4d0c2aa5e9b161bcf77b925cbdd473a1b91a. The initial Linux ARM64 run exposed an upstream SkiaSharp native startup failure; the pinned 3.119.4 dependency passed the native rerun. See [upstream fix](https://github.com/mono/SkiaSharp/pull/3494).
- **90 package checks passed** across the five target archives in native CI. These verify archive integrity, checksums, executable architecture, runtime target, notices, macOS bundle metadata/icon, executable permissions, and exclusion of development/user files.

## Packages and remaining acceptance

The packaging verifier checks each runtime archive's checksum, executable architecture, licenses, runtime configuration and exclusion of personal/development data. It also checks macOS bundle metadata/icon and Unix executable permissions. Debian packages are additionally inspected with Ubuntu's package tools. Package verification results are written to dist/desktop-preview/PACKAGE-CHECKS.txt.

macOS GUI launch, Screen Recording permissions, native selector behavior and hotkeys have **not** been tested on a Mac. Linux portal screenshots, global shortcuts, tray behavior, recording, and distribution-specific GUI dependencies still require testing on the user's Linux desktop. Intel/ARM64 binaries being present is not proof of hardware compatibility.

The workflow in .github/workflows/desktop.yml runs native builds, shared-core checks and package verification. The successful run linked above does not exercise interactive screen capture or OS permission dialogs.

Video-only recording, OCR, and video conversion are implemented in the preview but were not exercised end-to-end in this UI smoke test. Their native acceptance steps and current feature gaps are listed in CROSS-PLATFORM.md. There is no claim of full feature parity or production readiness.

All development files, packages, test captures and tool dependencies created for this work remain under Y:/frameforge. The temporary WSL mount aliases the same network share; it does not copy the project elsewhere. Test screenshots and generated projects are excluded from Git.
