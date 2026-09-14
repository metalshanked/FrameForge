# Third-party components

FrameForge is an independently developed application. Its third-party components are listed below.

- **.NET 8.0.31 runtime and Windows Desktop runtime**, Microsoft and contributors. License and third-party notices are supplied in `licenses`. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf.
- **NAudio 2.2.1**, Mark Heath and contributors, MIT license. The license is in `licenses\NAudio.txt`. Source: https://github.com/naudio/NAudio.
- **Windows SDK .NET projections / C#/WinRT**, Microsoft and contributors, MIT license. Source: https://github.com/microsoft/CsWinRT. Uses OCR functionality supplied by the user's Windows installation.
- **FFmpeg** is invoked as an external executable and is not distributed in this package. Its own license depends on the build installed; the tested Gyan build includes GPL components. Project and license information: https://ffmpeg.org/legal.html. Install builds separately from a trusted distributor and preserve their license requirements if repackaging them.

The self-contained runtime may contain additional third-party components described in the supplied .NET notices.

- **NSIS 3.12**, the NSIS contributors. The installer and uninstaller use NSIS; its component licenses are included in `licenses/NSIS.txt`. Source: https://github.com/NSIS-Dev/nsis. The compiler is a development tool and is not part of the portable app.

## Cross-platform desktop preview

The Avalonia desktop preview additionally redistributes:

- **Avalonia 12.1.2** (including its desktop backends, Fluent theme, and remote protocol), MIT. License: licenses/Avalonia.txt. Source: https://github.com/AvaloniaUI/Avalonia.
- **SkiaSharp 3.119.0** and its native assets, MIT plus the bundled third-party notices. See licenses/SkiaSharp.txt and licenses/SkiaSharp-third-party.txt. Source: https://github.com/mono/SkiaSharp.
- **HarfBuzzSharp 8.3.1.3**, including native assets. See licenses/HarfBuzzSharp.txt and licenses/HarfBuzzSharp-third-party.txt. Source: https://github.com/mono/SkiaSharp and https://github.com/harfbuzz/harfbuzz.
- **Tmds.DBus 0.95.1**, MIT. See licenses/Tmds.DBus.txt. Source: https://github.com/tmds/Tmds.DBus.
- **MicroCom.Runtime 0.11.6**, MIT. See licenses/MicroCom.txt. Source: https://github.com/kekekeks/MicroCom.
- **.NET 8** self-contained runtime, under the existing .NET licenses and notices in licenses/. The .NET 10 SDK is a development tool and is not bundled.

**Tesseract** is an optional separately installed OCR executable and is not redistributed. Source and licensing: https://github.com/tesseract-ocr/tesseract (Apache 2.0). FFmpeg remains an optional external dependency as described above. Operating system screen capture tools and desktop portal services are supplied by the user's operating system.
