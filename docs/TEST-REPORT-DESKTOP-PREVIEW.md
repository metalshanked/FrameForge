# Desktop preview verification

Version: **0.3.0-preview.1**, unchanged during this development cycle. This report applies to the Avalonia app; the existing Windows WPF installer is separate.

## Current development checks

- **68 shared-core checks passed on Windows x64.** Coverage includes project persistence, annotations, undo/redo, redaction, image transforms/export, scrolling overlap and rejection, atomic writes, process cancellation/literal arguments, recoverable capture deletion, restoration conflicts/path boundaries, and annotation resizing.
- The updated desktop app builds with **zero warnings and zero errors** using .NET 10 SDK, targeting .NET 8.
- **7 native Linux media checks passed on Ubuntu 26.04 x64 under WSL.** A synthetic source exercised H.264 MP4 recording, mixed stereo AAC audio, pause/resume, finalized output, full playback decoding, and refusal to overwrite an existing recording. These checks do not capture a real screen or test portal consent.
- Ubuntu runtime/media dependencies were installed through its package manager. The project is accessed by a temporary mount of Y:/frameforge; source and app/test artifacts remain under that project folder.
- **All five native targets passed** the new build workflow at source 5a791594da961366834f633c99697b0260823c30: [native media and package CI](https://github.com/metalshanked/FrameForge/actions/runs/34852969861). Both Mac helpers compiled and passed native OCR/video/GIF checks, both Linux architectures passed native media checks, and every target passed the 68 shared checks.
- **118 package checks passed** across Windows (16), both Macs (24 each), and both Linux architectures (27 each). Mac DMGs and Debian packages were created successfully. Later Linux error-message changes are being validated separately.
- The Linux package launched in WSLg as a native Ubuntu application with an isolated library. Portal inspection confirmed that this WSL session provides FileChooser but lacks Screenshot, ScreenCast, RemoteDesktop, and GlobalShortcuts. Recording preflight returned the intended clear unavailable-service message, and the 7 media checks still passed.
- Swift 5 native compilation reports concurrency-annotation warnings from AVFoundation and the queue-managed capture object; the C# builds are warning-free. Real Mac microphone/system-audio synchronization and permission behavior still require hardware testing.

## Earlier preview baseline

The earlier preview passed 52 shared checks on Windows and WSL, Windows UI smoke checks for launch/region capture/Escape/clipboard/annotation/exit, and an application dependency audit. All five native GitHub targets passed shared checks and packaging at source commit 1a4d4d0c2aa5e9b161bcf77b925cbdd473a1b91a:

[Earlier CI run](https://github.com/metalshanked/FrameForge/actions/runs/34798123097)

Those checks predate native recording/audio, automatic scrolling, recoverable library deletion, resize handles, and sign-in startup. They are retained as baseline history only.

## Current validation workflow

The workflow builds Windows x64, macOS Apple Silicon and Intel, and Ubuntu x64 and ARM64. It runs shared core checks, packages each target, verifies checksums/architecture/runtime/notices, and verifies the native helpers. Mac packages include an ad-hoc signed app and a DMG; they are not Developer ID signed or notarized. Linux package checks include Debian metadata and media dependencies.

Synthetic Linux media checks use GStreamer test sources. Synthetic Mac checks exercise local Vision OCR, video timestamp handling across a pause, MP4 decoding metadata, trimming, and animated GIF conversion. Neither substitutes for screen permissions or physical audio/display checks.

## Remaining desktop acceptance

The checklist in CROSS-PLATFORM.md covers packaged launch, screen/window/region capture, cancellation and permission denial, clipboard, shortcuts, tray/startup, capture deletion/restoration, recording with real audio sources, automatic scrolling, OCR languages, and mixed display scaling.

WSLg is a valid local Linux UI/media environment, but its portal capabilities differ from a normal Linux desktop. ScreenCast, RemoteDesktop pointer control, and GlobalShortcuts still require a compatible desktop session. macOS interactive acceptance requires access to the user's Mac and approval of its system permission prompts.

No production readiness or full feature parity is claimed before those checks pass. Test screenshots, recordings, projects, and machine-specific data are excluded from Git.
