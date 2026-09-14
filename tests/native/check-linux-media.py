#!/usr/bin/env python3
"""Exercise the real Linux media pipeline with synthetic frames/audio, never the desktop."""
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/native-linux-checks"
OUT.mkdir(parents=True, exist_ok=True)
target = OUT / ("recording-" + str(time.time_ns()) + ".mp4")
python = "/usr/bin/python3"
helper = ROOT / "src/FrameForge.Desktop/native/frameforge-linux.py"
process = subprocess.Popen([python, str(helper), "record", "--test", "--audio", "both", "--output", str(target)],
                           stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
events, errors = queue.Queue(), []
def read_events():
    for line in process.stdout:
        events.put(json.loads(line))
def read_errors():
    for line in process.stderr:
        errors.append(line)
threading.Thread(target=read_events, daemon=True).start()
threading.Thread(target=read_errors, daemon=True).start()
def receive(expected):
    try:
        event = events.get(timeout=30)
    except queue.Empty:
        raise RuntimeError("Native capture did not reply. " + "".join(errors)[-4000:])
    if event["event"] != expected:
        raise RuntimeError("Unexpected capture event: " + json.dumps(event))
    return event
def send(action, ident):
    process.stdin.write(json.dumps({"command": action, "id": ident}) + "\n")
    process.stdin.flush()
    result = receive({"stop": "stopped"}.get(action, action))
    assert result["id"] == ident
try:
    ready = receive("ready")
    assert ready["width"] == 320 and ready["height"] == 240
    time.sleep(.7)
    send("pause", 1)
    time.sleep(.5)
    send("resume", 2)
    time.sleep(.7)
    send("stop", 3)
    assert process.wait(timeout=25) == 0, "".join(errors)
    metadata = json.loads(subprocess.check_output(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(target)], text=True))
    streams = metadata["streams"]
    video = next(s for s in streams if s["codec_type"] == "video")
    audio = next(s for s in streams if s["codec_type"] == "audio")
    assert video["codec_name"] == "h264" and video["width"] == 320 and video["height"] == 240
    assert audio["codec_name"] == "aac" and audio["channels"] == 2
    duration = float(metadata["format"]["duration"])
    assert .8 < duration < 3.5, duration
    subprocess.run(["ffmpeg", "-v", "error", "-xerror", "-i", str(target), "-fps_mode", "passthrough", "-enc_time_base", "-1", "-f", "null", "-"], check=True, stdout=subprocess.DEVNULL)
    # Missing dependencies/invalid requests must fail without entering a sharing session.
    bad = subprocess.run([python, str(helper), "record", "--test", "--output", str(target)], capture_output=True, text=True, timeout=10)
    assert bad.returncode != 0 and "overwrite" in bad.stdout.lower()
    # Real window dimensions can be odd; the encoder must negotiate a nearby even size.
    odd_target = OUT / ("odd-window-" + str(time.time_ns()) + ".mp4")
    odd = subprocess.Popen([python, str(helper), "record", "--test", "--test-size", "321x241", "--output", str(odd_target)],
                           stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        odd_ready = json.loads(odd.stdout.readline())
        assert odd_ready["event"] == "ready" and odd_ready["width"] == 321 and odd_ready["height"] == 241
        time.sleep(.6)
        odd_output, odd_error = odd.communicate(json.dumps({"command": "stop", "id": 1}) + "\n", timeout=25)
        assert odd.returncode == 0, odd_output + odd_error
        odd_metadata = json.loads(subprocess.check_output(["ffprobe", "-v", "error", "-show_streams", "-of", "json", str(odd_target)], text=True))
        odd_video = next(s for s in odd_metadata["streams"] if s["codec_type"] == "video")
        assert odd_video["width"] % 2 == 0 and odd_video["height"] % 2 == 0
        assert abs(odd_video["width"] - 321) <= 1 and abs(odd_video["height"] - 241) <= 1
        assert not any(s["codec_type"] == "audio" for s in odd_metadata["streams"])
        subprocess.run(["ffmpeg", "-v", "error", "-xerror", "-i", str(odd_target), "-fps_mode", "passthrough", "-enc_time_base", "-1", "-f", "null", "-"], check=True, stdout=subprocess.DEVNULL)
    finally:
        if odd.poll() is None:
            odd.kill()
            odd.wait()
    (OUT / "results.txt").write_text("9 native Linux checks passed: real GStreamer pipeline, pause/resume, H.264 dimensions, mixed AAC audio, duration, full media decoding, overwrite protection, odd window dimensions without audio, and full odd-size recording decoding.\n")
    print((OUT / "results.txt").read_text())
finally:
    if process.poll() is None:
        process.kill()
        process.wait()
