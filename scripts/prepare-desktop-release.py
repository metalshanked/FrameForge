#!/usr/bin/env python3
"""Stage verified CI packages in a GitHub draft prerelease; never publish it."""
import hashlib, json, os, pathlib, subprocess, sys, time, zipfile
ROOT = pathlib.Path(__file__).resolve().parents[1]
REPO = os.environ.get("GITHUB_REPOSITORY", "metalshanked/FrameForge")
CONFIG = ROOT / "releases/desktop-preview.json"

def gh(*args, binary=False):
    result = subprocess.run(["gh", *args], cwd=ROOT, check=True, stdout=subprocess.PIPE)
    return result.stdout if binary else result.stdout.decode("utf-8")

def api(path):
    return json.loads(gh("api", f"repos/{REPO}/{path}"))

def find_release(tag, attempts=1):
    for attempt in range(attempts):
        release = next((r for r in api("releases?per_page=100") if r["tag_name"] == tag), None)
        if release:
            return release
        if attempt + 1 < attempts:
            time.sleep(2)
    return None

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def main():
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    version, commit, run_id = config["version"], config["sourceCommit"], config["runId"]
    tag = "v" + version
    if "preview" not in version or config["prerelease"] is not True:
        raise RuntimeError("This workflow prepares preview drafts only.")
    verify_only = "--verify-only" in sys.argv
    if not verify_only:
        run = api(f"actions/runs/{run_id}")
        if run["head_sha"] != commit or run["conclusion"] != "success":
            raise RuntimeError("Source run is not successful at the expected commit.")
    expected = {a["id"]: a for a in config["artifacts"]}
    actual = None if verify_only else {a["id"]: a for a in api(f"actions/runs/{run_id}/artifacts")["artifacts"]}
    staging = ROOT / "artifacts/release-draft"
    staging.mkdir(parents=True, exist_ok=True)
    output = ROOT / "dist/desktop-preview"
    output.mkdir(parents=True, exist_ok=True)
    packages = []
    for artifact_id, item in expected.items():
        if actual is not None:
            found = actual.get(artifact_id)
            if not found or found["name"] != item["name"] or found["digest"] != item["sha256"] or found["expired"]:
                raise RuntimeError(f"Artifact metadata mismatch: {artifact_id}")
        rid = item["name"].removeprefix("FrameForge-desktop-")
        extensions = [".zip"] if rid == "win-x64" else [".tar.gz", ".dmg" if rid.startswith("osx") else ".deb"]
        names = [f"FrameForge-Desktop-{version}-{rid}{ext}" for ext in extensions]
        required = names + [name + ".sha256" for name in names]
        archive = ROOT / f"artifacts/ci-{commit[:7]}/{rid}.zip" if verify_only else staging / f"{rid}.zip"
        if not verify_only:
            with archive.open("wb") as target:
                subprocess.run(["gh", "api", f"repos/{REPO}/actions/artifacts/{artifact_id}/zip"], cwd=ROOT, check=True, stdout=target)
        if "sha256:" + digest(archive) != item["sha256"]:
            raise RuntimeError(f"Artifact digest mismatch: {rid}")
        with zipfile.ZipFile(archive) as bundle:
            for name in required:
                # Read only the exact package names, never extract arbitrary ZIP paths.
                data = bundle.read("dist/desktop-preview/" + name)
                (output / name).write_bytes(data)
        for name in names:
            path = output / name
            expected_hash = (output / (name + ".sha256")).read_text().split()[0].lower()
            if digest(path) != expected_hash:
                raise RuntimeError(f"Package checksum mismatch: {name}")
            packages.append(path)
    if len(expected) != 5 or len(packages) != 9:
        raise RuntimeError("The complete five-platform package set is required.")
    subprocess.run([sys.executable, str(ROOT / "scripts/check-desktop-packages.py")], cwd=ROOT, check=True)
    (output / "SHA256SUMS.txt").write_text("".join(f"{digest(p)}  {p.name}\n" for p in sorted(packages)), encoding="ascii")
    (output / "BUILD-INFO.json").write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
    if verify_only:
        print("Verified all nine packages from five pinned CI archives; no GitHub mutations.")
        return
    notes = ROOT / "docs/releases/0.3.0-preview.1.md"
    release = find_release(tag)
    if release:
        if not release["draft"] or release["target_commitish"] != commit:
            raise RuntimeError("Refusing to replace a published release or a different source commit.")
    else:
        gh("release", "create", tag, "--repo", REPO, "--target", commit, "--draft", "--prerelease",
           "--title", "FrameForge 0.3.0-preview.1 — macOS and Linux preview", "--notes-file", str(notes))
        release = find_release(tag, attempts=10)
        if release is None:
            raise RuntimeError("GitHub created the draft but has not exposed it yet; rerun to resume safely.")
    gh("release", "edit", tag, "--repo", REPO, "--draft", "--prerelease", "--notes-file", str(notes))
    assets = packages + [output / "SHA256SUMS.txt", output / "BUILD-INFO.json"]
    existing = {a["name"]: a for a in api(f'releases/{release["id"]}')["assets"]}
    pending = []
    for path in assets:
        old = existing.get(path.name)
        if old:
            if old.get("digest") != "sha256:" + digest(path):
                raise RuntimeError(f"An existing draft asset differs: {path.name}")
        else:
            pending.append(str(path))
    if pending:
        gh("release", "upload", tag, "--repo", REPO, *pending)
    verified = api(f'releases/{release["id"]}')
    remote = {a["name"]: a for a in verified["assets"]}
    if not verified["draft"] or not verified["prerelease"]:
        raise RuntimeError("Release unexpectedly left draft-preview status.")
    for path in assets:
        if remote.get(path.name, {}).get("digest") != "sha256:" + digest(path):
            raise RuntimeError(f"Uploaded asset verification failed: {path.name}")
    print(f"Verified {len(assets)} assets in draft release {verified['html_url']}. Nothing was published.")
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a", encoding="utf-8") as stream:
            stream.write(f"Prepared **{tag}** as a draft prerelease with {len(assets)} verified assets.\n\nSource: `{commit}`. Publication remains a separate action.\n")

if __name__ == "__main__":
    main()
