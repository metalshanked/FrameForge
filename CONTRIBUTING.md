# Contributing to FrameForge

Build on Windows with the .NET 8 SDK using `scripts/build.ps1`. Keep changes focused and describe the behavior before and after the change.

Run `scripts/test.ps1` for noninteractive checks. Capture, keyboard, window-lifecycle, and recording changes also need `scripts/test.ps1 -Ui` on an unlocked desktop and a focused manual check of the affected workflow. FFmpeg is required for video tests. Never use a personal capture library as test input.

For bug reports, include Windows version, monitor scaling/layout, the app version, steps to reproduce, expected behavior, and actual behavior. Remove sensitive pixels, text, filenames, and device information before attaching a capture or log.

Source belongs in `src/FrameForge`. Keep generated executables, test captures, caches, SDKs, credentials, and personal configuration out of Git. The existing `.gitignore` excludes the build and local-tool directories.

The icon is original vector artwork in `assets/FrameForge.svg`. Rebuild ICO/PNG assets with:

```powershell
dotnet run --project tools/IconBuilder -- assets
```

Build a release with `scripts/package.ps1` and NSIS 3.x. Test install, upgrade, tray exit, relaunch, and uninstall before distributing it. The installer supports `/TESTMODE` to place its registry entries under `HKCU\Software\FrameForge.PackageTest` and shortcuts inside the chosen installation directory; use a disposable directory under `artifacts` and keep `/D=...` last. Test mode does not change the app's data directory, so set `FRAMEFORGE_DATA` to an isolated location before running an installed test copy.

Contributions are provided under the project's MIT license. Preserve third-party notices and document any new dependencies.
## Versioning

Keep the current version during feature work, bug fixes, and acceptance testing. Change it only when preparing an intentional release; do not increment versions for each edit or rebuild.
