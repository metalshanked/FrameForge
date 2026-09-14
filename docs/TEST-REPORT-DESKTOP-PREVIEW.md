# Desktop preview verification

Version: **0.3.0-preview.1**, unchanged during this development cycle. The Avalonia desktop app is separate from the Windows WPF installer.

## Automated checks

- **68 shared-core checks per target** cover projects, annotation rendering, undo/redo, redaction, image transforms/export, scrolling overlap and rejection, atomic writes, process cancellation/literal arguments, recoverable capture deletion, restoration conflicts/path boundaries, and annotation resizing.
- **3 single-instance checks** cover activation from separate process sessions, delivery of a capture request to the existing app, and restart after the primary process is killed. A real WSL test exposed a session-scoped mutex bug; the shared lock now spans sessions and handles an abandoned owner.
- **9 native Linux media checks** exercise a real GStreamer pipeline using synthetic frames and mixed audio: H.264 MP4, stereo AAC, pause/resume, duration, full decoding, overwrite protection, and odd-sized window encoding without audio.
- **7 native Mac checks** exercise offline Vision OCR, pause/resume timestamp handling, H.264 decoding, MP4 trimming, and animated GIF conversion using synthetic input.
- **118 package checks** across Windows (16), both Macs (24 each), and both Linux targets (27 each) verify checksums, architecture, runtime/notices, helper inclusion/permissions, Mac metadata/DMGs, and Debian metadata/dependencies.

All five targets passed the [native CI run](https://github.com/metalshanked/FrameForge/actions/runs/34890935586) at source **d104fb0693ae6071ae76c427b16b35a4221a8564**, including the Mac screenshot-permission preflight and Linux odd-window recording fixes. Targets are Windows x64, macOS Apple Silicon and Intel, and Ubuntu x64 and ARM64. All downloaded artifact digests and package checksums were verified locally; the complete set passed 118 package checks.

The C# application builds with zero warnings/errors using .NET 10 SDK and targets .NET 8. Swift 5 compilation reports concurrency-annotation warnings from AVFoundation and the queue-managed capture object. The native media checks do not replace real microphone, display, and permission testing.

## Physical Intel Mac acceptance

Tested the DMG from source **c2ac8aa3e0376eedaa59b0845038fac5a35f839a** on an Intel Mac running **macOS 15.7.9**, with a 1920 × 1080 display at native scaling:

- Verified the transferred DMG checksum, installed its app bundle in the user's Applications directory, and verified its ad-hoc code signature.
- All **7 native Mac checks passed on the actual Mac**.
- Launched the installed app with a separate test library and opened a synthetic 900 × 540 editable project.
- OCR in the actual interface returned the expected “FRAMEFORGE LOCAL OCR TEST” text.
- Copy image populated the native clipboard with PNG and other image formats; Paste reopened the rendered image at **900 × 540** and saved a new project/PNG pair.
- Open capture folder visibly opened the correct library in Finder.
- Escape canceled the native region selector and restored the editor with the existing project intact.
- After the user granted capture permission and the app reopened, a full-screen **1920 × 1080** capture succeeded, returned to the editor, saved its project/PNG pair, and automatically populated the clipboard. The user also accepted macOS's additional direct-access prompt.
- Region capture through the remote mouse selector returned cancellation; successful region selection has not been established.
- Recording source enumeration succeeded on this earlier build, but an actual recording has not yet passed.
- The final **d104fb0** Intel DMG was then verified and installed, retaining the earlier app and test library. It launches with the correct native FrameForge menu name.
- The final app's changed ad-hoc signature invalidated the earlier capture grant: screenshot preflight and native recording source enumeration both reported denied access. No security settings were changed by automation. Exact-build capture/recording retesting and stable distribution signing remain release blockers.
- Apple Developer Technical Support [confirms](https://developer.apple.com/forums/thread/819406) that ad-hoc signing can make each build appear to be a new app for capture permission. A stable Apple signing identity is needed for reliable permission continuity across updates.

No claim is made that microphone/system-audio synchronization, automatic scrolling, global shortcuts, startup, or mixed-display behavior has passed on this Mac yet. Apple Silicon packages pass native automated tests, but there is no physical Apple Silicon desktop acceptance result.

## Ubuntu WSL acceptance

Ubuntu 26.04 x64 under WSLg was used on the Windows development machine:

- Installed runtime/media dependencies through Ubuntu's package manager.
- Launched the Linux packaged app and native file picker with an isolated capture library.
- Passed native media checks and the single-instance regression, including different Unix sessions and forced-exit recovery.
- Inspected the session's portal interfaces: FileChooser is available; Screenshot, ScreenCast, RemoteDesktop, and GlobalShortcuts are absent.
- Recording preflight returned the intended explanation that this session cannot share the screen.

WSLg is useful for Linux UI, file/process integration, OCR/media, and package checks. It does **not** establish production screen-sharing, pointer-control, global-shortcut, or tray behavior on GNOME/KDE and other Linux desktops. A compatible desktop session remains necessary for those acceptance checks.

## Distribution status and data

Mac installers are ad-hoc signed and **not Developer ID signed or notarized**. Public download/Gatekeeper behavior requires distribution signing work. Workflow artifacts are previews; no stable cross-platform release is published automatically.

The full remaining acceptance checklist is in CROSS-PLATFORM.md. Source, local packages, and test artifacts on Windows stay under Y:/frameforge; WSL mounts that same project. The Mac has its installed app and an isolated test folder. Synthetic test captures, real test captures, machine connection details, and SSH credentials are excluded from Git.
