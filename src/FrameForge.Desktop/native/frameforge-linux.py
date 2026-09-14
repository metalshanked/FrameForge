#!/usr/bin/env python3
"""Local portal/PipeWire capture. Only the desktop portal grants screen/input access."""
import argparse
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import threading
import uuid
import xml.etree.ElementTree as ET

def emit(event, **values):
    print(json.dumps(dict(event=event, **values), ensure_ascii=False), flush=True)

try:
    import gi
    gi.require_version("Gst", "1.0")
    from gi.repository import Gio, GLib, Gst
    Gst.init(None)
except (ImportError, ValueError) as error:
    emit("error", message="Install FrameForge's Linux recording dependencies: Python GObject, GStreamer, PipeWire and the desktop portal. " + str(error))
    sys.exit(1)

SERVICE = "org.freedesktop.portal.Desktop"
DESKTOP = "/org/freedesktop/portal/desktop"
CAST = "org.freedesktop.portal.ScreenCast"
REMOTE = "org.freedesktop.portal.RemoteDesktop"

class Portal:
    def __init__(self):
        self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        self.session = None
        self.fd = None
        self.subscriptions = []
        self.pending = None

    def call(self, interface, method, signature, values):
        return self.bus.call_sync(SERVICE, DESKTOP, interface, method,
                                  GLib.Variant(signature, values), None, Gio.DBusCallFlags.NONE, 10000, None)

    def request(self, interface, method, signature, values):
        token = "ff" + uuid.uuid4().hex
        values[-1]["handle_token"] = GLib.Variant("s", token)
        name = self.bus.get_unique_name().lstrip(":").replace(".", "_")
        path = DESKTOP + "/request/" + name + "/" + token
        loop, answer = GLib.MainLoop(), []
        def response(*args):
            answer.append(args[-1].unpack())
            if loop.is_running():
                loop.quit()
        subscription = self.bus.signal_subscribe(SERVICE, "org.freedesktop.portal.Request", "Response",
                                                  path, None, Gio.DBusSignalFlags.NONE, response)
        timeout = GLib.timeout_add_seconds(120, lambda: (loop.quit(), False)[1])
        self.pending = path
        try:
            returned = self.call(interface, method, signature, tuple(values)).unpack()[0]
            if returned != path:
                raise RuntimeError("This desktop portal returned an unsupported request handle.")
            if not answer:
                loop.run()
            if not answer:
                raise RuntimeError("The desktop permission request timed out.")
            code, result = answer[0]
            if code != 0:
                raise RuntimeError("Screen sharing was canceled or declined. Your existing capture is unchanged.")
            return result
        finally:
            GLib.source_remove(timeout)
            self.bus.signal_unsubscribe(subscription)
            try:
                self.bus.call_sync(SERVICE, path, "org.freedesktop.portal.Request", "Close",
                                   None, None, Gio.DBusCallFlags.NONE, 1000, None)
            except GLib.Error:
                pass
            self.pending = None

    def require(self, pointer=False):
        try:
            xml = self.call("org.freedesktop.DBus.Introspectable", "Introspect", "()", ()).unpack()[0]
            interfaces = {item.get("name") for item in ET.fromstring(xml).findall("interface")}
        except GLib.Error as error:
            raise RuntimeError("The Linux desktop sharing service is unavailable. Sign in to a desktop with its matching XDG portal backend.") from error
        required = [CAST, REMOTE] if pointer else [CAST]
        if any(name not in interfaces for name in required):
            feature = "automatic scrolling with pointer control" if pointer else "screen recording"
            raise RuntimeError("This Linux session does not provide " + feature + ". Use a desktop with a compatible XDG portal backend. WSLg may support the editor without screen sharing.")

    def open(self, pointer, cursor, closed):
        self.require(pointer)
        interface = REMOTE if pointer else CAST
        result = self.request(interface, "CreateSession", "(a{sv})",
                              [{"session_handle_token": GLib.Variant("s", "ff" + uuid.uuid4().hex)}])
        self.session = result["session_handle"]
        self.subscriptions.append(self.bus.signal_subscribe(
            SERVICE, "org.freedesktop.portal.Session", "Closed", self.session, None,
            Gio.DBusSignalFlags.NONE, lambda *_: closed()))
        if pointer:
            self.request(REMOTE, "SelectDevices", "(oa{sv})",
                         [self.session, {"types": GLib.Variant("u", 2)}])
        # Use advertised cursor modes; older backends need the default hidden cursor.
        options = {"types": GLib.Variant("u", 1 if pointer else 3), "multiple": GLib.Variant("b", False)}
        try:
            modes = self.call("org.freedesktop.DBus.Properties", "Get", "(ss)",
                              (CAST, "AvailableCursorModes")).unpack()[0]
            wanted = 2 if cursor else 1
            if modes & wanted:
                options["cursor_mode"] = GLib.Variant("u", wanted)
        except GLib.Error:
            pass
        self.request(CAST, "SelectSources", "(oa{sv})", [self.session, options])
        result = self.request(interface, "Start", "(osa{sv})", [self.session, "", {}])
        if pointer and result.get("devices", 0) & 2 == 0:
            raise RuntimeError("Automatic scrolling needs pointer access in the desktop sharing dialog.")
        streams = result.get("streams", [])
        if len(streams) != 1:
            raise RuntimeError("Choose one screen or window for this capture.")
        self.node, self.properties = streams[0]
        reply, descriptors = self.bus.call_with_unix_fd_list_sync(
            SERVICE, DESKTOP, CAST, "OpenPipeWireRemote", GLib.Variant("(oa{sv})", (self.session, {})),
            GLib.VariantType.new("(h)"), Gio.DBusCallFlags.NONE, 10000, None, None)
        self.fd = descriptors.get(reply.unpack()[0])
        return self.node, self.fd

    def scroll(self, x, y, amount, width, height):
        logical = self.properties.get("size", (width, height))
        x = max(0, min(width, x)) * logical[0] / width
        y = max(0, min(height, y)) * logical[1] / height
        self.call(REMOTE, "NotifyPointerMotionAbsolute", "(oa{sv}udd)", (self.session, {}, self.node, x, y))
        self.call(REMOTE, "NotifyPointerAxis", "(oa{sv}dd)", (self.session, {}, 0.0, float(amount)))
        self.call(REMOTE, "NotifyPointerAxis", "(oa{sv}dd)",
                  (self.session, {"finish": GLib.Variant("b", True)}, 0.0, 0.0))

    def close(self):
        for subscription in self.subscriptions:
            self.bus.signal_unsubscribe(subscription)
        self.subscriptions.clear()
        if self.session:
            try:
                self.bus.call_sync(SERVICE, self.session, "org.freedesktop.portal.Session", "Close",
                                   None, None, Gio.DBusCallFlags.NONE, 2000, None)
            except GLib.Error:
                pass
            self.session = None
        if self.fd is not None:
            os.close(self.fd)
            self.fd = None

def require_plugins(record):
    needed = ["pipewiresrc", "videoconvert", "videorate", "queue", "pngenc", "appsink"]
    if record:
        needed += ["videoscale", "x264enc", "h264parse", "mp4mux", "filesink", "pulsesrc", "audioconvert", "audioresample", "audiomixer", "avenc_aac"]
    missing = [name for name in needed if not Gst.ElementFactory.find(name)]
    if missing:
        raise RuntimeError("Missing GStreamer components: " + ", ".join(missing) + ". Install the dependencies listed in FrameForge's Linux setup guide.")

def quote(value):
    return '"' + str(value).replace("\\", "\\\\").replace('"', '\\"') + '"'

def audio_inputs(mode, test):
    if mode == "none":
        return ""
    devices = []
    if mode in ("system", "both"):
        if test:
            devices.append("audiotestsrc is-live=true wave=sine")
        else:
            result = subprocess.run(["pactl", "get-default-sink"], capture_output=True, text=True, timeout=5, check=True)
            sink = result.stdout.strip()
            if not sink or "\n" in sink:
                raise RuntimeError("The default system audio output is unavailable.")
            devices.append("pulsesrc device=" + quote(sink + ".monitor"))
    if mode in ("microphone", "both"):
        devices.append("audiotestsrc is-live=true wave=triangle" if test else "pulsesrc")
    chains = " audiomixer name=mix ! audioconvert ! audioresample ! audio/x-raw,rate=48000,channels=2 ! avenc_aac bitrate=160000 ! queue ! mux. "
    for source in devices:
        chains += source + " ! queue ! audioconvert ! audioresample ! audio/x-raw,rate=48000,channels=2 ! mix. "
    return chains

class Capture:
    def __init__(self, args):
        self.args, self.portal = args, None
        self.loop, self.pipeline = GLib.MainLoop(), None
        self.png, self.width, self.height = None, 0, 0
        self.ready, self.stopping, self.failed = False, False, False
        self.lock = threading.Lock()
        self.pending_frame = None
        self.finish_id = None

    def fail(self, message):
        if not self.failed:
            self.failed = True
            emit("error", message=str(message))
        if self.loop.is_running():
            self.loop.quit()
        return False

    def sample(self, sink):
        sample = sink.emit("pull-sample")
        if sample is None:
            return Gst.FlowReturn.ERROR
        buffer = sample.get_buffer()
        ok, mapped = buffer.map(Gst.MapFlags.READ)
        if ok:
            try:
                data = bytes(mapped.data)
                # PNG IHDR gives dimensions without any display assumptions.
                import struct
                width, height = struct.unpack(">II", data[16:24])
                with self.lock:
                    self.png, self.width, self.height = data, width, height
                GLib.idle_add(self.frame_available)
            finally:
                buffer.unmap(mapped)
        return Gst.FlowReturn.OK

    def frame_available(self):
        if not self.ready:
            self.ready = True
            emit("ready", width=self.width, height=self.height)
        if self.pending_frame is not None:
            command, self.pending_frame = self.pending_frame, None
            self.save_frame(command)
        return False

    def save_frame(self, command):
        with self.lock:
            data = self.png
        if data is None:
            self.pending_frame = command
            return
        target = Path(command["path"]).resolve()
        # IPC is private to the parent; it passes a generated temporary image path.
        target.parent.mkdir(parents=True, exist_ok=True)
        temp = target.with_name(target.name + "." + uuid.uuid4().hex + ".tmp")
        try:
            temp.write_bytes(data)
            os.replace(temp, target)
        finally:
            temp.unlink(missing_ok=True)
        emit("frame", id=command["id"], width=self.width, height=self.height)

    def command(self, command):
        try:
            action = command["command"]
            identifier = command.get("id", 0)
            if action == "stop":
                self.finish_id = identifier
                self.stop()
            elif action in ("pause", "resume") and self.args.mode == "record":
                state = Gst.State.PAUSED if action == "pause" else Gst.State.PLAYING
                if self.pipeline.set_state(state) == Gst.StateChangeReturn.FAILURE:
                    raise RuntimeError("The recording could not " + action + ".")
                emit(action, id=identifier)
            elif action == "frame" and self.args.mode == "scroll":
                self.save_frame(command)
            elif action == "scroll" and self.args.mode == "scroll":
                values = [float(command[name]) for name in ("x", "y", "amount")]
                if not all(math.isfinite(v) for v in values):
                    raise ValueError("Invalid scrolling coordinates.")
                self.portal.scroll(*values, self.width, self.height)
                emit("scroll", id=identifier)
            else:
                raise ValueError("Unsupported capture command.")
        except Exception as error:
            self.fail(error)
        return False

    def read_commands(self):
        try:
            for line in sys.stdin:
                if len(line) > 16384:
                    raise ValueError("Capture command too long.")
                command = json.loads(line)
                GLib.idle_add(self.command, command)
        except Exception as error:
            GLib.idle_add(self.fail, str(error))
        # Closing the app's input pipe closes its capture session.
        GLib.idle_add(self.stop)

    def stop(self):
        if self.stopping:
            return False
        self.stopping = True
        if self.args.mode == "record" and self.pipeline:
            self.pipeline.set_state(Gst.State.PLAYING)
            self.pipeline.send_event(Gst.Event.new_eos())
            GLib.timeout_add_seconds(20, lambda: self.fail("Recording finalization timed out. The partial file was kept."))
        else:
            emit("stopped", id=self.finish_id)
            self.loop.quit()
        return False

    def bus_message(self, bus, message):
        if message.type == Gst.MessageType.ERROR:
            error, _ = message.parse_error()
            self.fail(error.message)
        elif message.type == Gst.MessageType.EOS:
            emit("stopped", id=self.finish_id)
            self.loop.quit()

    def run(self):
        require_plugins(self.args.mode == "record")
        if self.args.test:
            width, height = (int(value) for value in self.args.test_size.split("x"))
            if not 2 <= width <= 4096 or not 2 <= height <= 4096:
                raise ValueError("Synthetic test dimensions must be between 2 and 4096.")
            source = f"videotestsrc is-live=true pattern=ball ! video/x-raw,width={width},height={height},framerate=30/1"
        else:
            self.portal = Portal()
            node, fd = self.portal.open(self.args.mode == "scroll", not self.args.no_cursor,
                                        lambda: self.fail("Screen sharing was ended by the desktop."))
            source = "pipewiresrc name=source fd=" + str(fd) + " path=" + str(node) + " do-timestamp=true"
        capture = " ! videoconvert ! tee name=frames frames. ! queue leaky=downstream max-size-buffers=1 ! videorate drop-only=true ! video/x-raw,framerate=2/1 ! pngenc snapshot=false ! appsink name=snapshot emit-signals=true sync=false max-buffers=1 drop=true "
        if self.args.mode == "record":
            capture += " frames. ! queue ! videoconvert ! videoscale ! video/x-raw,format=I420,width=(int)[2,32768,2],height=(int)[2,32768,2] ! x264enc tune=zerolatency speed-preset=veryfast bitrate=6000 ! h264parse ! queue ! mux. mp4mux name=mux faststart=true ! filesink location=" + quote(self.args.output)
            capture += audio_inputs(self.args.audio, self.args.test)
        self.pipeline = Gst.parse_launch(source + capture)
        if self.portal and self.portal.properties.get("pipewire-serial"):
            source_element = self.pipeline.get_by_name("source")
            if source_element.find_property("target-object"):
                source_element.set_property("target-object", str(self.portal.properties["pipewire-serial"]))
        self.pipeline.get_by_name("snapshot").connect("new-sample", self.sample)
        bus = self.pipeline.get_bus()
        bus.add_signal_watch()
        bus.connect("message", self.bus_message)
        threading.Thread(target=self.read_commands, daemon=True).start()
        if self.pipeline.set_state(Gst.State.PLAYING) == Gst.StateChangeReturn.FAILURE:
            raise RuntimeError("Could not start the screen capture stream.")
        GLib.timeout_add_seconds(30, lambda: False if self.ready else self.fail("No screen frames arrived. Check the desktop sharing permission."))
        if self.args.test:
            GLib.timeout_add_seconds(15, self.stop)
        self.loop.run()

    def close(self):
        if self.pipeline:
            self.pipeline.set_state(Gst.State.NULL)
        if self.portal:
            self.portal.close()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("record", "scroll", "check", "portal-check"))
    parser.add_argument("--output")
    parser.add_argument("--audio", choices=("none", "system", "microphone", "both"), default="none")
    parser.add_argument("--no-cursor", action="store_true")
    parser.add_argument("--test", action="store_true", help="Synthetic media only; never opens the portal.")
    parser.add_argument("--test-size", default="320x240", help="Synthetic source dimensions; only used with --test.")
    args = parser.parse_args()
    if args.mode == "portal-check":
        portal = Portal()
        try:
            portal.require()
            emit("ready", platform="linux")
        finally:
            portal.close()
        return 0
    if args.mode == "check":
        require_plugins(True)
        emit("ready", platform="linux")
        return 0
    if args.mode == "record":
        if not args.output:
            raise ValueError("Recording requires an output file.")
        args.output = str(Path(args.output).resolve())
        if Path(args.output).exists():
            raise ValueError("Refusing to overwrite an existing recording.")
        Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    capture = Capture(args)
    signal.signal(signal.SIGINT, lambda *_: GLib.idle_add(capture.stop))
    signal.signal(signal.SIGTERM, lambda *_: GLib.idle_add(capture.stop))
    try:
        capture.run()
        return 1 if capture.failed else 0
    finally:
        capture.close()

if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        emit("error", message=str(error))
        sys.exit(1)
