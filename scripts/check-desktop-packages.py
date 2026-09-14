#!/usr/bin/env python3
"""Verify package integrity, target architecture, bundle metadata, permissions, and notices."""
import argparse, hashlib, io, json, pathlib, plistlib, struct, tarfile, zipfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/"dist/desktop-preview"
VERSION="0.3.0-preview.1"
RIDS=("win-x64","osx-arm64","osx-x64","linux-x64","linux-arm64")
parser=argparse.ArgumentParser()
parser.add_argument("--rid", choices=RIDS)
options=parser.parse_args()
checks=0
def check(value,description):
    global checks
    if not value: raise RuntimeError(description)
    checks+=1
for rid in ((options.rid,) if options.rid else RIDS):
    extension=".zip" if rid.startswith("win") else ".tar.gz"
    path=OUT/f"FrameForge-Desktop-{VERSION}-{rid}{extension}"
    with path.open("rb") as stream: digest=hashlib.file_digest(stream,"sha256").hexdigest()
    check(path.with_suffix(path.suffix+".sha256").read_text().split()[0]==digest,rid+" checksum")
    if extension==".zip":
        with zipfile.ZipFile(path) as z:
            check(z.testzip() is None,rid+" zip CRC")
            contents={n:z.read(n) for n in z.namelist()}
            modes={n:(z.getinfo(n).external_attr>>16)&0o777 for n in z.namelist()}
    else:
        with tarfile.open(path) as t:
            members=[m for m in t.getmembers() if m.isfile()]
            contents={m.name:t.extractfile(m).read() for m in members}
            modes={m.name:m.mode for m in members}
    prefix="FrameForge.app/Contents/MacOS/" if rid.startswith("osx") else "FrameForge/"
    executable=prefix+("FrameForge.Desktop.exe" if rid.startswith("win") else "FrameForge.Desktop")
    binary=contents[executable]
    check(all(".." not in pathlib.PurePosixPath(n).parts and not n.startswith("/") for n in contents),rid+" archive paths")
    check(not any(n.endswith((".ffg",".log",".pdb",".pyc")) or "/.local/" in n or "/.git/" in n for n in contents),rid+" excludes development and user data")
    for name in ("LICENSE","THIRD-PARTY-NOTICES.md","README-FIRST.md","licenses/Avalonia.txt","licenses/SkiaSharp.txt","licenses/SkiaSharp-third-party.txt","licenses/Tmds.DBus.txt","licenses/MicroCom.txt","licenses/HarfBuzzSharp.txt","licenses/HarfBuzzSharp-third-party.txt"):
        check(prefix+name in contents,rid+" includes "+name)
    check(json.loads(contents[prefix+"FrameForge.Desktop.runtimeconfig.json"])["runtimeOptions"]["tfm"]=="net8.0",rid+" runtime target")
    if rid.startswith("win"):
        offset=struct.unpack_from("<I",binary,60)[0]
        check(binary[:2]==b"MZ" and struct.unpack_from("<H",binary,offset+4)[0]==0x8664,"Windows x64 executable")
    elif rid.startswith("osx"):
        check(binary[:4]==b"\xcf\xfa\xed\xfe","Mac Mach-O header")
        check(struct.unpack_from("<I",binary,4)[0]==(0x100000c if rid.endswith("arm64") else 0x1000007),rid+" CPU architecture")
        info=plistlib.loads(contents["FrameForge.app/Contents/Info.plist"])
        check(info["CFBundleIdentifier"]=="app.frameforge.desktop" and info["CFBundleExecutable"]=="FrameForge.Desktop",rid+" bundle identity")
        check(contents["FrameForge.app/Contents/Resources/FrameForge.icns"][:4]==b"icns",rid+" app icon")
        check(modes[executable]&0o111!=0,rid+" executable permission")
    else:
        check(binary[:4]==b"\x7fELF" and binary[4]==2,rid+" 64-bit ELF header")
        check(struct.unpack_from("<H",binary,18)[0]==(183 if rid.endswith("arm64") else 62),rid+" CPU architecture")
        check(modes[executable]&0o111!=0,rid+" executable permission")
        deb=OUT/f"FrameForge-Desktop-{VERSION}-{rid}.deb"
        with deb.open("rb") as stream: digest=hashlib.file_digest(stream,"sha256").hexdigest()
        check(deb.with_suffix(".deb.sha256").read_text().split()[0]==digest,rid+" Debian checksum")
print(f"{checks} package checks passed.")
(OUT/(f"PACKAGE-CHECKS-{options.rid}.txt" if options.rid else "PACKAGE-CHECKS.txt")).write_text(f"{checks} package checks passed: archive integrity, executable architecture, runtime target, licenses, bundle identity and executable permissions. Native GUI acceptance is separate.\n")
