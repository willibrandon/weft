import AppKit
import ScreenCaptureKit

/// Records a bounded native-window redraw trace against an isolated real Nushell process.
@available(macOS 14.0, *)
@MainActor
enum OutputRecording {
    static func run(executable: String, image: String) async throws {
        TerminalFont.register(in: URL(fileURLWithPath: executable).deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("Resources/Fonts"))
        let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { controller.close() }
        controller.showWindow(nil)
        _ = try await MacSmoke.wait(controller) { MacSmoke.text($0).contains("$") }
        let directory = FileManager.default.currentDirectoryPath.replacingOccurrences(of: "'", with: "'\\''")
        controller.terminal.insertText("cd '\(directory)'; exec nu --no-history\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        try await Task.sleep(for: .seconds(2))
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        guard let window = controller.window,
              let captured = content.windows.first(where: { $0.windowID == window.windowNumber }) else {
            throw SmokeFailure.failed("No native window available for the output recording")
        }
        let filter = SCContentFilter(desktopIndependentWindow: captured)
        let configuration = SCStreamConfiguration()
        configuration.width = Int(window.frame.width)
        configuration.height = Int(window.frame.height)
        configuration.showsCursor = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 60)
        let output = OutputFrames()
        let stream = SCStream(filter: filter, configuration: configuration, delegate: nil)
        try stream.addStreamOutput(output, type: .screen, sampleHandlerQueue: .main)
        try await stream.startCapture()
        let start = ContinuousClock.now
        var nextCommand = 0.0
        var index = 0
        var log = "frame,elapsed_ms,visible_characters,cursor_visible\n"
        while start.duration(to: .now) < .seconds(6) {
            let elapsed = MacSmoke.milliseconds(start.duration(to: .now))
            if elapsed >= nextCommand {
                for character in "ls\r" {
                    controller.terminal.insertText(String(character), replacementRange: NSRange(location: NSNotFound, length: 0))
                    try await Task.sleep(for: .milliseconds(40))
                }
                nextCommand += 1000
            }
            let frame = controller.terminal.frameData
            let count = frame.map { MacSmoke.text($0).filter { !$0.isWhitespace }.count } ?? 0
            log += "\(index),\(elapsed),\(count),\(frame?.blocks.first?.cursorVisible ?? false)\n"
            index += 1
            try await Task.sleep(for: .milliseconds(2))
        }
        try await stream.stopCapture()
        try log.write(toFile: image + ".output.csv", atomically: true, encoding: .utf8)
        try output.save(to: image)
    }
}
