#!/usr/bin/env python3
"""Build self-contained FrameForge desktop previews. All output stays in the repository."""
import platform
import argparse, hashlib, io, json, os, pathlib, plistlib, shutil, struct, subprocess, tarfile, zipfile
ROOT = pathlib.Path(__file__).resolve().parents[1]
VERSION = "0.3.0-preview.1"
RIDS = ("win-x64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64")

def tar_bytes(entries):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w:gz") as archive:
        for path, name, mode in entries:
            data = path.read_bytes() if isinstance(path, pathlib.Path) else path
            info = tarfile.TarInfo(name)
            info.size, info.mode, info.mtime = len(data), mode, 0
            archive.addfile(info, io.BytesIO(data))
    return stream.getvalue()

def deb_package(stage, rid, target):
    arch = "amd64" if rid == "linux-x64" else "arm64"
    control = f"""Package: frameforge-desktop
Version: 0.3.0~preview.1
Section: graphics
Priority: optional
Architecture: {arch}
Maintainer: FrameForge contributors <57646596+metalshanked@users.noreply.github.com>
Depends: libc6 (>= 2.28), libgcc-s1, libgssapi-krb5-2, libstdc++6, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libssl3 | libssl3t64, libicu70 | libicu72 | libicu74 | libicu76 | libicu78, xdg-desktop-portal, python3, python3-gi, python3-gst-1.0, gir1.2-gstreamer-1.0, gir1.2-gst-plugins-base-1.0, gstreamer1.0-pipewire, gstreamer1.0-plugins-base, gstreamer1.0-plugins-good, gstreamer1.0-plugins-bad, gstreamer1.0-plugins-ugly, gstreamer1.0-libav, pulseaudio-utils, ffmpeg, tesseract-ocr, tesseract-ocr-eng
Recommends: xdg-desktop-portal-gtk
Description: FrameForge screen capture and annotation
 Local screen capture, annotation, scrolling image stitching, video recording,
 and offline OCR, using native desktop sharing permission dialogs.
"""
    desktop = """[Desktop Entry]
Type=Application
Name=FrameForge
Comment=Capture, annotate, and explain
Exec=/opt/frameforge-desktop/FrameForge.Desktop
Icon=frameforge-desktop
Terminal=false
Categories=Graphics;Utility;
StartupWMClass=FrameForge.Desktop
"""
    entries = [(p, "opt/frameforge-desktop/" + p.relative_to(stage).as_posix(),
                0o755 if p.name in ("FrameForge.Desktop", "frameforge-native") or p.suffix == ".so" else 0o644)
               for p in sorted(stage.rglob("*")) if p.is_file()]
    entries += [(desktop.encode(), "usr/share/applications/frameforge-desktop.desktop", 0o644),
                (ROOT / "assets/FrameForge.png", "usr/share/icons/hicolor/256x256/apps/frameforge-desktop.png", 0o644)]
    members = [("debian-binary", b"2.0\n"),
               ("control.tar.gz", tar_bytes([(control.encode(), "./control", 0o644)])),
               ("data.tar.gz", tar_bytes(entries))]
    with target.open("wb") as archive:
        archive.write(b"!<arch>\n")
        for name, data in members:
            archive.write(f"{name + '/':<16}{0:<12}{0:<6}{0:<6}{'100644':<8}{len(data):<10}\x60\n".encode("ascii"))
            archive.write(data)
            if len(data) % 2: archive.write(b"\n")

def package(rid, dotnet, skip_build, native_helper=None):
    work = ROOT / "artifacts/desktop" / rid
    output = ROOT / "dist/desktop-preview"
    output.mkdir(parents=True, exist_ok=True)
    if not skip_build:
        if work.exists():
            work.resolve().relative_to((ROOT / 'artifacts/desktop').resolve())
            shutil.rmtree(work)
        subprocess.run([dotnet, "publish", str(ROOT / "src/FrameForge.Desktop"),
                        "-c", "Release", "-r", rid, "--self-contained", "true",
                        "-p:PublishSingleFile=false", "-p:DebugType=None", "-p:DebugSymbols=false",
                        "-o", str(work)], cwd=ROOT, check=True)
    if not (work / ("FrameForge.Desktop.exe" if rid.startswith("win") else "FrameForge.Desktop")).is_file():
        raise RuntimeError(f"No published executable at {work}")
    if rid.startswith("osx"):
        helper = work / "native/frameforge-native"
        helper.parent.mkdir(parents=True, exist_ok=True)
        if native_helper:
            shutil.copy2(native_helper, helper)
        elif platform.system() == "Darwin":
            target = ("arm64" if rid.endswith("arm64") else "x86_64") + "-apple-macos14.0"
            subprocess.run(["xcrun", "swiftc", "-swift-version", "5", "-parse-as-library", "-O",
                            "-target", target, str(ROOT / "src/FrameForge.Desktop/native/frameforge-macos.swift"),
                            "-o", str(helper)], check=True)
        else:
            raise RuntimeError("Mac packages require a native helper from macOS CI; pass --native-helper PATH.")
        helper.chmod(0o755)
    for debug_symbol in work.rglob("*.pdb"):
        debug_symbol.resolve().relative_to(work.resolve())
        debug_symbol.unlink()
    for name in ("LICENSE", "THIRD-PARTY-NOTICES.md"):
        shutil.copy2(ROOT / name, work / name)
    shutil.copy2(ROOT / "docs/CROSS-PLATFORM.md", work / "README-FIRST.md")
    shutil.copy2(ROOT / "assets/FrameForge.png", work / "FrameForge.png")
    # Bundle directory is disposable build output, never a source or user-data path.
    if rid.startswith("osx"):
        bundle_root = ROOT / "artifacts/desktop-bundles" / rid
        app = bundle_root / "FrameForge.app"
        if app.exists():
            app.resolve().relative_to((ROOT / 'artifacts/desktop-bundles').resolve())
            shutil.rmtree(app)
        app.mkdir(parents=True, exist_ok=True)
        macos = app / "Contents/MacOS"
        resources = app / "Contents/Resources"
        resources.mkdir(parents=True, exist_ok=True)
        shutil.copytree(work, macos, dirs_exist_ok=True)
        info = {"CFBundleName": "FrameForge", "CFBundleDisplayName": "FrameForge",
                "CFBundleIdentifier": "app.frameforge.desktop", "CFBundleExecutable": "FrameForge.Desktop",
                "CFBundlePackageType": "APPL", "CFBundleShortVersionString": "0.3.0",
                "CFBundleVersion": "0.3.0.1", "CFBundleIconFile": "FrameForge.icns",
                "NSHighResolutionCapable": True, "LSMinimumSystemVersion": "14.0",
                "NSScreenCaptureUsageDescription": "FrameForge captures your selected screen for local screenshots and recordings.",
                "NSMicrophoneUsageDescription": "FrameForge records your microphone only when you choose microphone audio."}
        (app / "Contents/Info.plist").write_bytes(plistlib.dumps(info))
        png = (ROOT / "assets/FrameForge.png").read_bytes()
        size = struct.unpack(">I", png[16:20])[0]
        kind = {256: b"ic08", 512: b"ic09", 1024: b"ic10"}[size]
        resources.joinpath("FrameForge.icns").write_bytes(b"icns" + struct.pack(">I", len(png) + 16) + kind + struct.pack(">I", len(png) + 8) + png)
        if platform.system() == "Darwin":
            # Ad-hoc signing makes local bundles internally consistent; it is not notarization.
            subprocess.run(["codesign", "--force", "--deep", "--sign", "-", str(app)], check=True)
        source, prefix = app, "FrameForge.app"
    else:
        source, prefix = work, "FrameForge"
    extension = ".zip" if rid.startswith("win") else ".tar.gz"
    artifact = output / f"FrameForge-Desktop-{VERSION}-{rid}{extension}"
    entries = [(p, prefix + "/" + p.relative_to(source).as_posix(),
                0o755 if p.name in ("FrameForge.Desktop", "frameforge-native") or p.suffix in (".so", ".dylib") else 0o644)
               for p in sorted(source.rglob("*")) if p.is_file()]
    if extension == ".zip":
        with zipfile.ZipFile(artifact, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for path, name, mode in entries:
                info = zipfile.ZipInfo(name)
                info.create_system, info.external_attr, info.compress_type = 3, (0o100000 | mode) << 16, zipfile.ZIP_DEFLATED
                archive.writestr(info, path.read_bytes())
    else:
        artifact.write_bytes(tar_bytes(entries))
    results = [artifact]
    if rid.startswith("osx") and platform.system() == "Darwin":
        dmg = output / f"FrameForge-Desktop-{VERSION}-{rid}.dmg"
        applications = bundle_root / "Applications"
        if not applications.exists():
            applications.symlink_to("/Applications", target_is_directory=True)
        subprocess.run(["hdiutil", "create", "-ov", "-volname", "FrameForge",
                        "-srcfolder", str(bundle_root), "-format", "UDZO", str(dmg)], check=True)
        results.append(dmg)
    if rid.startswith("linux"):
        deb = output / f"FrameForge-Desktop-{VERSION}-{rid}.deb"
        deb_package(work, rid, deb)
        results.append(deb)
    for path in results:
        digest = hashlib.file_digest(path.open("rb"), "sha256").hexdigest()
        path.with_suffix(path.suffix + ".sha256").write_text(f"{digest}  {path.name}\n", encoding="utf-8")
        print(f"Packaged {path.name} ({path.stat().st_size:,} bytes)", flush=True)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", choices=RIDS, required=True)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--native-helper", type=pathlib.Path)
    options = parser.parse_args()
    package(options.rid, options.dotnet, options.skip_build, options.native_helper)
