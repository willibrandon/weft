import AppKit
import CoreImage
import ScreenCaptureKit

/// Retains a bounded set of changed compositor frames without encoding images during capture.
@MainActor
final class OutputFrames: NSObject, SCStreamOutput {
    nonisolated private let context = CIContext()
    private var frames: [(Double, CGImage)] = []
    private var lastPixels: Data?

    nonisolated func stream(_ stream: SCStream, didOutputSampleBuffer buffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, buffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let status = attachments.first?[.status] as? Int,
              SCFrameStatus(rawValue: status) == .complete,
              let pixels = buffer.imageBuffer else { return }
        let image = CIImage(cvPixelBuffer: pixels)
        guard let copy = context.createCGImage(image, from: image.extent) else { return }
        let pixelsData = copy.dataProvider?.data as Data?
        let timestamp = buffer.presentationTimeStamp.seconds
        // The recorder explicitly delivers samples on the main queue.
        MainActor.assumeIsolated {
            guard frames.count < 180 else { return }
            guard pixelsData != lastPixels else { return }
            lastPixels = pixelsData
            frames.append((timestamp, copy))
        }
    }

    func save(to path: String) throws {
        guard !frames.isEmpty else { throw SmokeFailure.failed("Screen capture returned no complete frames") }
        var log = "frame,elapsed_ms\n"
        for (index, frame) in frames.enumerated() {
            let bitmap = NSBitmapImageRep(cgImage: frame.1)
            let name = path + String(format: ".output-%03d.png", index)
            try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: name))
            log += "\(index),\((frame.0 - frames[0].0) * 1000)\n"
        }
        try log.write(toFile: path + ".screen.csv", atomically: true, encoding: .utf8)
        print("Recorded \(frames.count) native output frames at \(path).output-*.png")
    }
}
