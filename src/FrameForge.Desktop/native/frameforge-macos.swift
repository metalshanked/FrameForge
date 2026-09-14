import Foundation
import AppKit
import ScreenCaptureKit
import AVFoundation
import Vision
import ApplicationServices
import ImageIO
import UniformTypeIdentifiers

enum Failure: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let text) = self { return text }; return nil }
}
let outputLock = NSLock()
func emit(_ event: String, _ values: [String: Any] = [:]) {
    var result = values
    result["event"] = event
    guard let data = try? JSONSerialization.data(withJSONObject: result) else { return }
    outputLock.lock()
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write(Data([10]))
    outputLock.unlock()
}
func png(_ image: CGImage) throws -> Data {
    let data = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
        throw Failure.message("Could not create the screenshot.")
    }
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else { throw Failure.message("Could not encode the screenshot.") }
    return data as Data
}
func options() -> [String: String] {
    var result: [String: String] = [:]
    let args = Array(CommandLine.arguments.dropFirst())
    if let first = args.first { result["mode"] = first }
    var i = 1
    while i < args.count {
        if args[i].hasPrefix("--") && i + 1 < args.count { result[String(args[i].dropFirst(2))] = args[i+1]; i += 2 }
        else { i += 1 }
    }
    return result
}
func content() async throws -> SCShareableContent {
    do { return try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true) }
    catch { throw Failure.message("Allow Screen Recording for FrameForge in System Settings → Privacy & Security, then reopen it. " + error.localizedDescription) }
}
func sources() async throws {
    let available = try await content()
    let displays: [[String: Any]] = available.displays.enumerated().map { index, display in
        ["id": "display:\(display.displayID)", "name": "Display \(index + 1) · \(display.width) × \(display.height)"]
    }
    let windows: [[String: Any]] = available.windows.filter {
        $0.frame.width > 50 && $0.frame.height > 50 && $0.owningApplication?.bundleIdentifier != "app.frameforge.desktop"
    }.map { window in
        ["id": "window:\(window.windowID)", "name": (window.owningApplication?.applicationName ?? "Window") + " — " + (window.title ?? "Untitled")]
    }
    emit("sources", ["items": displays + windows])
}
func recognize(_ path: String, language: String) throws {
    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    request.usesLanguageCorrection = true
    let mapping = ["eng": "en-US", "fra": "fr-FR", "deu": "de-DE", "spa": "es-ES", "ita": "it-IT", "por": "pt-BR", "jpn": "ja-JP", "kor": "ko-KR", "chi_sim": "zh-Hans", "chi_tra": "zh-Hant"]
    if language == "auto" { request.automaticallyDetectsLanguage = true }
    else {
        let selected = language.split(separator: "+").map { mapping[String($0)] ?? String($0) }
        let supported = try request.supportedRecognitionLanguages()
        guard selected.allSatisfy({ supported.contains($0) }) else {
            throw Failure.message("That OCR language is unavailable on this Mac. Supported codes: " + supported.joined(separator: ", "))
        }
        request.recognitionLanguages = selected
    }
    try VNImageRequestHandler(url: URL(fileURLWithPath: path)).perform([request])
    let text = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
    emit("ocr", ["text": text])
}

final class Capture: NSObject, SCStreamOutput, SCStreamDelegate, AVCaptureAudioDataOutputSampleBufferDelegate {
    let queue = DispatchQueue(label: "app.frameforge.capture")
    let imageContext = CIContext()
    var stream: SCStream?
    var writer: AVAssetWriter?
    var video: AVAssetWriterInput?
    var systemAudio: AVAssetWriterInput?
    var microphoneAudio: AVAssetWriterInput?
    var microphone: AVCaptureSession?
    var origin = CGPoint.zero
    var logicalSize = CGSize.zero
    var pixelSize = CGSize.zero
    var lastImage: CGImage?
    var recording = false
    var paused = false
    var stopping = false
    var started = false
    var firstTime: CMTime?
    var pauseStart: CMTime?
    var totalPause = CMTime.zero
    var lastVideoTime = CMTime.zero
    var lastVideoSample: CMSampleBuffer?
    var useHostClock = true
    var output: URL?
    var hasBothAudio = false
    var ready = false

    func start(_ args: [String: String]) async throws {
        recording = args["mode"] == "record"
        if !recording {
            let trusted = AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary)
            guard trusted else { throw Failure.message("Automatic scrolling needs Accessibility access. Enable FrameForge in System Settings → Privacy & Security → Accessibility, then try again.") }
        }
        let available = try await content()
        let selection = (args["source"] ?? "").split(separator: ":")
        let filter: SCContentFilter
        if selection.first == "window", let id = selection.last.flatMap({ UInt32($0) }),
           let window = available.windows.first(where: { $0.windowID == id }) {
            filter = SCContentFilter(desktopIndependentWindow: window)
            origin = window.frame.origin
        } else {
            let id = selection.last.flatMap { UInt32($0) }
            guard let display = available.displays.first(where: { $0.displayID == id }) ?? available.displays.first else {
                throw Failure.message("No display is available.")
            }
            let excluded = available.applications.filter { $0.bundleIdentifier == "app.frameforge.desktop" }
            filter = SCContentFilter(display: display, excludingApplications: excluded, exceptingWindows: [])
            origin = display.frame.origin
        }
        let config = SCStreamConfiguration()
        logicalSize = filter.contentRect.size
        let scale = CGFloat(filter.pointPixelScale)
        config.width = max(2, Int(ceil(logicalSize.width * scale)) / 2 * 2)
        config.height = max(2, Int(ceil(logicalSize.height * scale)) / 2 * 2)
        pixelSize = CGSize(width: config.width, height: config.height)
        config.minimumFrameInterval = CMTime(value: 1, timescale: recording ? 30 : 4)
        config.queueDepth = 4
        config.pixelFormat = kCVPixelFormatType_32BGRA
        config.showsCursor = args["cursor"] != "false"
        let audioMode = args["audio"] ?? "none"
        let wantsSystem = recording && ["system", "both"].contains(audioMode)
        let wantsMicrophone = recording && ["microphone", "both"].contains(audioMode)
        config.capturesAudio = wantsSystem
        config.excludesCurrentProcessAudio = true
        config.sampleRate = 48000
        config.channelCount = 2
        if recording {
            guard let path = args["output"], !FileManager.default.fileExists(atPath: path) else {
                throw Failure.message("Choose a new recording filename.")
            }
            output = URL(fileURLWithPath: path)
            try FileManager.default.createDirectory(at: output!.deletingLastPathComponent(), withIntermediateDirectories: true)
            let assetWriter = try AVAssetWriter(outputURL: output!, fileType: .mp4)
            let input = AVAssetWriterInput(mediaType: .video, outputSettings: [
                AVVideoCodecKey: AVVideoCodecType.h264,
                AVVideoWidthKey: config.width, AVVideoHeightKey: config.height,
                AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: 8000000, AVVideoExpectedSourceFrameRateKey: 30]
            ])
            input.expectsMediaDataInRealTime = true
            guard assetWriter.canAdd(input) else { throw Failure.message("H.264 recording is unavailable on this Mac.") }
            assetWriter.add(input)
            writer = assetWriter
            video = input
            func audioInput() throws -> AVAssetWriterInput {
                let input = AVAssetWriterInput(mediaType: .audio, outputSettings: [
                    AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 48000, AVNumberOfChannelsKey: 2, AVEncoderBitRateKey: 160000
                ])
                input.expectsMediaDataInRealTime = true
                guard assetWriter.canAdd(input) else { throw Failure.message("The audio encoder is unavailable.") }
                assetWriter.add(input)
                return input
            }
            if wantsSystem { systemAudio = try audioInput() }
            if wantsMicrophone {
                guard await AVCaptureDevice.requestAccess(for: .audio) else { throw Failure.message("Microphone access was declined. Enable it in System Settings or choose recording without a microphone.") }
                guard let device = AVCaptureDevice.default(for: .audio) else { throw Failure.message("No microphone is available.") }
                let session = AVCaptureSession()
                let input = try AVCaptureDeviceInput(device: device)
                let output = AVCaptureAudioDataOutput()
                guard session.canAddInput(input), session.canAddOutput(output) else { throw Failure.message("This microphone could not be opened.") }
                session.addInput(input)
                session.addOutput(output)
                output.setSampleBufferDelegate(self, queue: queue)
                microphoneAudio = try audioInput()
                microphone = session
            }
            hasBothAudio = wantsSystem && wantsMicrophone
            guard assetWriter.startWriting() else { throw assetWriter.error ?? Failure.message("Recording could not start.") }
        }
        let capture = SCStream(filter: filter, configuration: config, delegate: self)
        try capture.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        if wantsSystem { try capture.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue) }
        stream = capture
        try await capture.startCapture()
        if let session = microphone { DispatchQueue.global().async { session.startRunning() } }
    }

    func append(_ sample: CMSampleBuffer, to input: AVAssetWriterInput, isVideo: Bool) {
        guard !paused, !stopping, CMSampleBufferDataIsReady(sample), let writer = writer else { return }
        let timestamp = CMSampleBufferGetPresentationTimeStamp(sample)
        if firstTime == nil {
            guard isVideo else { return }
            firstTime = timestamp
            writer.startSession(atSourceTime: .zero)
        }
        let offset = firstTime! + totalPause
        if timestamp < offset { return }
        var count = 0
        CMSampleBufferGetSampleTimingInfoArray(sample, entryCount: 0, arrayToFill: nil, entriesNeededOut: &count)
        var timing = Array(repeating: CMSampleTimingInfo(duration: .invalid, presentationTimeStamp: .invalid, decodeTimeStamp: .invalid), count: count)
        let result = timing.withUnsafeMutableBufferPointer {
            CMSampleBufferGetSampleTimingInfoArray(sample, entryCount: count, arrayToFill: $0.baseAddress, entriesNeededOut: nil)
        }
        guard result == noErr else { return }
        for index in timing.indices {
            timing[index].presentationTimeStamp = timing[index].presentationTimeStamp - offset
            if timing[index].decodeTimeStamp.isValid { timing[index].decodeTimeStamp = timing[index].decodeTimeStamp - offset }
        }
        var adjusted: CMSampleBuffer?
        let copied = timing.withUnsafeBufferPointer {
            CMSampleBufferCreateCopyWithNewTiming(allocator: kCFAllocatorDefault, sampleBuffer: sample, sampleTimingEntryCount: count, sampleTimingArray: $0.baseAddress!, sampleBufferOut: &adjusted)
        }
        guard copied == noErr, let adjusted = adjusted, input.isReadyForMoreMediaData else { return }
        if !input.append(adjusted) { emit("error", ["message": writer.error?.localizedDescription ?? "A recording frame could not be written."]) }
        if isVideo { lastVideoTime = timestamp - offset; lastVideoSample = sample }
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        if type == .screen {
            guard let buffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
            if let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
               let status = attachments.first?[.status] as? Int, status != SCFrameStatus.complete.rawValue { return }
            if !recording {
                let image = CIImage(cvPixelBuffer: buffer)
                lastImage = imageContext.createCGImage(image, from: image.extent)
            }
            if recording, let input = video { append(sampleBuffer, to: input, isVideo: true) }
            if !ready {
                ready = true
                emit("ready", ["width": Int(pixelSize.width), "height": Int(pixelSize.height)])
            }
        } else if type == .audio, let input = systemAudio { append(sampleBuffer, to: input, isVideo: false) }
    }
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        if let input = microphoneAudio { append(sampleBuffer, to: input, isVideo: false) }
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) {
        emit("error", ["message": "Screen sharing ended: " + error.localizedDescription])
    }

    func command(_ command: [String: Any]) async throws -> Bool {
        let action = command["command"] as? String ?? ""
        let id = command["id"] as? Int ?? 0
        if action == "stop" {
            try await stop()
            emit("stopped", ["id": id])
            return false
        }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            queue.async {
                do {
                    switch action {
                    case "pause":
                        guard self.recording else { throw Failure.message("No recording is active.") }
                        if !self.paused { self.pauseStart = CMClockGetTime(CMClockGetHostTimeClock()); self.paused = true }
                    case "resume":
                        if self.paused, let start = self.pauseStart { self.totalPause = self.totalPause + CMClockGetTime(CMClockGetHostTimeClock()) - start; self.paused = false; self.pauseStart = nil }
                    case "frame":
                        guard let path = command["path"] as? String, let image = self.lastImage else { throw Failure.message("No screenshot frame is available yet.") }
                        try png(image).write(to: URL(fileURLWithPath: path), options: .atomic)
                    case "scroll":
                        guard !self.recording, let x = command["x"] as? Double, let y = command["y"] as? Double, let amount = command["amount"] as? Double, x.isFinite, y.isFinite, amount.isFinite else { throw Failure.message("Invalid scrolling request.") }
                        let position = CGPoint(x: self.origin.x + x * self.logicalSize.width / self.pixelSize.width,
                                               y: self.origin.y + y * self.logicalSize.height / self.pixelSize.height)
                        let source = CGEventSource(stateID: .combinedSessionState)
                        CGEvent(mouseEventSource: source, mouseType: .mouseMoved, mouseCursorPosition: position, mouseButton: .left)?.post(tap: .cghidEventTap)
                        let event = CGEvent(scrollWheelEvent2Source: source, units: .pixel, wheelCount: 1, wheel1: -Int32(max(-2000, min(2000, amount))), wheel2: 0, wheel3: 0)
                        event?.location = position
                        event?.post(tap: .cghidEventTap)
                    default: throw Failure.message("Unsupported capture command.")
                    }
                    emit(action, ["id": id])
                    continuation.resume()
                } catch { continuation.resume(throwing: error) }
            }
        }
        return true
    }

    func stop() async throws {
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            queue.async { self.stopping = true; continuation.resume() }
        }
        try await stream?.stopCapture()
        if let session = microphone { session.stopRunning() }
        guard let writer = writer else { return }
        guard firstTime != nil else { writer.cancelWriting(); throw Failure.message("The recording contained no video frames.") }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            queue.async {
                // Retain the last picture to cover an idle desktop and the final frame interval.
                // ScreenCaptureKit emits no complete frames while the screen is unchanged.
                if self.useHostClock, let last = self.lastVideoSample, let input = self.video, input.isReadyForMoreMediaData {
                    let end = self.paused ? (self.pauseStart ?? CMClockGetTime(CMClockGetHostTimeClock())) : CMClockGetTime(CMClockGetHostTimeClock())
                    var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 30), presentationTimeStamp: end, decodeTimeStamp: .invalid)
                    var final: CMSampleBuffer?
                    if CMSampleBufferCreateCopyWithNewTiming(allocator: kCFAllocatorDefault, sampleBuffer: last, sampleTimingEntryCount: 1, sampleTimingArray: &timing, sampleBufferOut: &final) == noErr, let final = final {
                        self.stopping = false; self.paused = false
                        self.append(final, to: input, isVideo: true)
                        self.stopping = true
                    }
                }
                writer.endSession(atSourceTime: self.lastVideoTime + CMTime(value: 1, timescale: 30))
                self.video?.markAsFinished(); self.systemAudio?.markAsFinished(); self.microphoneAudio?.markAsFinished()
                continuation.resume()
            }
        }
        await writer.finishWriting()
        guard writer.status == .completed else { throw writer.error ?? Failure.message("The recording could not be finalized.") }
        if hasBothAudio, let output = output { try await mixAudio(output) }
    }
    func mixAudio(_ source: URL) async throws {
        let asset = AVURLAsset(url: source)
        let composition = AVMutableComposition()
        let duration = try await asset.load(.duration)
        let range = CMTimeRange(start: .zero, duration: duration)
        for type in [AVMediaType.video, .audio] {
            for track in try await asset.loadTracks(withMediaType: type) {
                guard let target = composition.addMutableTrack(withMediaType: type, preferredTrackID: kCMPersistentTrackID_Invalid) else { throw Failure.message("Audio mixing could not start.") }
                let trackRange = try await track.load(.timeRange)
                let available = CMTimeRangeGetIntersection(range, otherRange: trackRange)
                if available.duration > .zero { try target.insertTimeRange(available, of: track, at: available.start) }
            }
        }
        let mixed = source.deletingLastPathComponent().appendingPathComponent(UUID().uuidString + ".mp4")
        guard let export = AVAssetExportSession(asset: composition, presetName: AVAssetExportPresetHighestQuality) else { throw Failure.message("The recording was saved, but audio mixing is unavailable.") }
        export.outputURL = mixed; export.outputFileType = .mp4; export.shouldOptimizeForNetworkUse = true
        let audioMix = AVMutableAudioMix()
        audioMix.inputParameters = composition.tracks(withMediaType: .audio).map {
            let parameter = AVMutableAudioMixInputParameters(track: $0)
            parameter.setVolume(0.7, at: .zero)
            return parameter
        }
        export.audioMix = audioMix
        await export.export()
        guard export.status == .completed else { try? FileManager.default.removeItem(at: mixed); throw export.error ?? Failure.message("Audio mixing failed; the original recording was kept.") }
        _ = try FileManager.default.replaceItemAt(source, withItemAt: mixed)
    }
}

func selfTest(_ directory: String) async throws {
    let root = URL(fileURLWithPath: directory)
    try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    let image = NSImage(size: NSSize(width: 900, height: 160))
    image.lockFocus()
    NSColor.white.setFill(); NSRect(x: 0, y: 0, width: 900, height: 160).fill()
    ("FRAMEFORGE LOCAL OCR TEST" as NSString).draw(at: NSPoint(x: 30, y: 60), withAttributes: [.font: NSFont.systemFont(ofSize: 42), .foregroundColor: NSColor.black])
    image.unlockFocus()
    var imageRect = NSRect(x: 0, y: 0, width: 900, height: 160)
    guard let cgImage = image.cgImage(forProposedRect: &imageRect, context: nil, hints: nil) else { throw Failure.message("OCR test image creation failed.") }
    let imagePath = root.appendingPathComponent("ocr.png")
    try png(cgImage).write(to: imagePath)
    let request = VNRecognizeTextRequest(); request.recognitionLevel = .accurate; request.recognitionLanguages = ["en-US"]
    try VNImageRequestHandler(url: imagePath).perform([request])
    let text = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: " ")
    guard text.contains("FRAMEFORGE"), text.contains("OCR") else { throw Failure.message("Native OCR did not recognize the test text: " + text) }

    let capture = Capture()
    capture.recording = true
    capture.useHostClock = false
    let output = root.appendingPathComponent("timeline-" + UUID().uuidString + ".mp4")
    let writer = try AVAssetWriter(outputURL: output, fileType: .mp4)
    let input = AVAssetWriterInput(mediaType: .video, outputSettings: [AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: 320, AVVideoHeightKey: 240])
    input.expectsMediaDataInRealTime = true
    writer.add(input); capture.writer = writer; capture.video = input; capture.output = output
    guard writer.startWriting() else { throw writer.error ?? Failure.message("Test writer did not start.") }
    for index in 0..<60 {
        if index == 30 {
            _ = try await capture.command(["command": "pause", "id": 1])
            guard capture.paused else { throw Failure.message("Pause state failed.") }
            _ = try await capture.command(["command": "resume", "id": 2])
            guard !capture.paused else { throw Failure.message("Resume state failed.") }
            capture.totalPause = CMTime(value: 2, timescale: 1)
        }
        var pixels: CVPixelBuffer?
        guard CVPixelBufferCreate(kCFAllocatorDefault, 320, 240, kCVPixelFormatType_32BGRA,
                                  [kCVPixelBufferCGImageCompatibilityKey: true, kCVPixelBufferCGBitmapContextCompatibilityKey: true] as CFDictionary, &pixels) == kCVReturnSuccess, let pixels = pixels else { throw Failure.message("Test pixel allocation failed.") }
        CVPixelBufferLockBaseAddress(pixels, [])
        if let base = CVPixelBufferGetBaseAddress(pixels) { memset(base, index < 30 ? 80 : 180, CVPixelBufferGetBytesPerRow(pixels) * 240) }
        CVPixelBufferUnlockBaseAddress(pixels, [])
        var description: CMVideoFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: pixels, formatDescriptionOut: &description)
        let timestamp = CMTime(value: Int64(300 + index + (index >= 30 ? 60 : 0)), timescale: 30)
        var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 30), presentationTimeStamp: timestamp, decodeTimeStamp: .invalid)
        var sample: CMSampleBuffer?
        guard let description = description, CMSampleBufferCreateReadyWithImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: pixels, formatDescription: description, sampleTiming: &timing, sampleBufferOut: &sample) == noErr, let sample = sample else { throw Failure.message("Test frame creation failed.") }
        var waits = 0
        while !input.isReadyForMoreMediaData && waits < 500 { try await Task.sleep(nanoseconds: 10_000_000); waits += 1 }
        guard input.isReadyForMoreMediaData else { throw Failure.message("Test video encoder stalled.") }
        capture.append(sample, to: input, isVideo: true)
    }
    try await capture.stop()
    let asset = AVURLAsset(url: output)
    let duration = try await asset.load(.duration).seconds
    guard duration > 1.9 && duration < 2.1 else { throw Failure.message("Paused time was not removed: \(duration)") }
    let tracks = try await asset.loadTracks(withMediaType: .video)
    guard tracks.count == 1 else { throw Failure.message("Encoded test video is missing.") }
    let generator = AVAssetImageGenerator(asset: asset)
    _ = try generator.copyCGImage(at: CMTime(value: 1, timescale: 1), actualTime: nil)
    let trimmed = root.appendingPathComponent("trim-" + UUID().uuidString + ".mp4")
    try await convertVideo(["input": output.path, "output": trimmed.path, "start": "0.25", "duration": "1", "gif": "false"])
    let trimDuration = try await AVURLAsset(url: trimmed).load(.duration).seconds
    guard trimDuration > 0.9 && trimDuration < 1.1 else { throw Failure.message("Native video trimming failed.") }
    let gif = root.appendingPathComponent("animation-" + UUID().uuidString + ".gif")
    try await convertVideo(["input": output.path, "output": gif.path, "start": "0", "duration": "1", "gif": "true"])
    guard let animated = CGImageSourceCreateWithURL(gif as CFURL, nil), CGImageSourceGetCount(animated) >= 10 else { throw Failure.message("Native animated GIF export failed.") }
    try "7 native macOS checks passed: offline OCR, pause, resume, pause-free timeline, H.264 decoding, MP4 trimming, and animated GIF export.\n".write(to: root.appendingPathComponent("results.txt"), atomically: true, encoding: .utf8)
    emit("checked", ["checks": 7])
}


func convertVideo(_ args: [String: String]) async throws {
    guard let input = args["input"], let output = args["output"],
          let start = Double(args["start"] ?? ""), let requested = Double(args["duration"] ?? ""),
          start.isFinite, requested.isFinite, start >= 0, requested > 0 else { throw Failure.message("Choose a valid video start time and duration.") }
    let source = URL(fileURLWithPath: input), target = URL(fileURLWithPath: output)
    guard source.standardizedFileURL != target.standardizedFileURL, !FileManager.default.fileExists(atPath: output) else { throw Failure.message("Export to a new file to preserve your recording.") }
    let asset = AVURLAsset(url: source)
    let total = try await asset.load(.duration).seconds
    guard total.isFinite, start < total else { throw Failure.message("The start time is past the end of this recording.") }
    let duration = min(requested, total - start)
    if args["gif"] == "true" {
        guard duration <= 120 else { throw Failure.message("Choose a clip of up to two minutes for GIF export.") }
        let frames = max(1, Int(ceil(duration * 12)))
        guard let destination = CGImageDestinationCreateWithURL(target as CFURL, UTType.gif.identifier as CFString, frames, nil) else { throw Failure.message("Could not create the GIF.") }
        CGImageDestinationSetProperties(destination, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)
        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: 960, height: 960)
        for frame in 0..<frames {
            let time = CMTime(seconds: start + Double(frame)/12, preferredTimescale: 600)
            let image = try generator.copyCGImage(at: time, actualTime: nil)
            CGImageDestinationAddImage(destination, image, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: 1.0/12]] as CFDictionary)
        }
        guard CGImageDestinationFinalize(destination) else { throw Failure.message("The GIF could not be finalized.") }
    } else {
        guard let export = AVAssetExportSession(asset: asset, presetName: AVAssetExportPresetHighestQuality) else { throw Failure.message("This video cannot be exported.") }
        export.outputURL = target; export.outputFileType = .mp4; export.shouldOptimizeForNetworkUse = true
        export.timeRange = CMTimeRange(start: CMTime(seconds: start, preferredTimescale: 600), duration: CMTime(seconds: duration, preferredTimescale: 600))
        await export.export()
        guard export.status == .completed else { throw export.error ?? Failure.message("Video export failed.") }
    }
    emit("converted")
}

@main struct FrameForgeNative {
    static func main() async {
        do {
            let args = options()
            switch args["mode"] {
            case "check": emit("ready", ["platform": "macos"])
            case "sources": try await sources()
            case "convert": try await convertVideo(args)
            case "self-test": try await selfTest(args["output"] ?? "native-mac-checks")
            case "ocr":
                guard let path = args["input"] else { throw Failure.message("Choose an image for OCR.") }
                try recognize(path, language: args["language"] ?? "eng")
            case "record", "scroll":
                let capture = Capture()
                try await capture.start(args)
                let commands = AsyncStream<[String: Any]> { continuation in
                    DispatchQueue.global().async {
                        while let line = readLine() {
                            guard line.utf8.count < 16384, let data = line.data(using: .utf8), let command = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { break }
                            continuation.yield(command)
                        }
                        continuation.yield(["command": "stop", "id": 0])
                        continuation.finish()
                    }
                }
                for await command in commands {
                    if try await capture.command(command) == false { break }
                }
            default: throw Failure.message("Unknown native capture operation.")
            }
        } catch {
            emit("error", ["message": error.localizedDescription])
            exit(1)
        }
    }
}
