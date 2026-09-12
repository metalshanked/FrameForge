# Third-party components

FrameForge is an independently developed application. Its third-party components are listed below.

- **.NET 8.0.31 runtime and Windows Desktop runtime**, Microsoft and contributors. License and third-party notices are supplied in `licenses`. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf.
- **NAudio 2.2.1**, Mark Heath and contributors, MIT license. The license is in `licenses\NAudio.txt`. Source: https://github.com/naudio/NAudio.
- **Windows SDK .NET projections / C#/WinRT**, Microsoft and contributors, MIT license. Source: https://github.com/microsoft/CsWinRT. Uses OCR functionality supplied by the user's Windows installation.
- **FFmpeg** is invoked as an external executable and is not distributed in this package. Its own license depends on the build installed; the tested Gyan build includes GPL components. Project and license information: https://ffmpeg.org/legal.html. Install builds separately from a trusted distributor and preserve their license requirements if repackaging them.

The self-contained runtime may contain additional third-party components described in the supplied .NET notices.

- **NSIS 3.12**, the NSIS contributors. The installer and uninstaller use NSIS; its component licenses are included in `licenses/NSIS.txt`. Source: https://github.com/NSIS-Dev/nsis. The compiler is a development tool and is not part of the portable app.
