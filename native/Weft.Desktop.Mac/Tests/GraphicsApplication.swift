import AppKit

extension MacSmoke {
    /// Runs the caller's existing graphics workload without writing to its checkout.
    static func exerciseGraphicsApp(executable: String, app: String, path: String) async throws {
        let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { controller.close() }
        controller.window?.setContentSize(NSSize(width: 1710, height: 920))
        controller.showWindow(nil)
        _ = try await wait(controller) { !$0.blocks.isEmpty && text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
        let command = "'" + app.replacingOccurrences(of: "'", with: "'\\''") + "'"
        for mode in ["kitty", "sixel"] {
            controller.terminal.insertText("\(command) --render \(mode) --seed 123 --frames 1200\r", replacementRange: NSRange(location: NSNotFound, length: 0))
            try await Task.sleep(for: .seconds(2))
            let begin = ContinuousClock.now
            let cpu = cpuMilliseconds()
            var previous: [DesktopImage] = []
            var draws: [Double] = []
            var maximumPlacements = 0
            var maximumTextures = 0
            var maximumBytes = 0
            var footprints: [Double] = []
            while milliseconds(begin.duration(to: .now)) < 12000 {
                if milliseconds(begin.duration(to: .now)) >= Double(footprints.count + 1) * 1000 {
                    footprints.append(Double(try footprintBytes()) / 1_048_576)
                }
                if let block = controller.terminal.frameData?.blocks.first, block.images != previous {
                    previous = block.images
                    maximumPlacements = max(maximumPlacements, block.images.count)
                    maximumTextures = max(maximumTextures, block.textures.count)
                    maximumBytes = max(maximumBytes, block.textures.reduce(0) { $0 + $1.data.count })
                    let draw = ContinuousClock.now
                    controller.terminal.displayIfNeeded()
                    draws.append(milliseconds(draw.duration(to: .now)))
                }
                try await Task.sleep(for: .milliseconds(2))
            }
            let elapsed = milliseconds(begin.duration(to: .now))
            draws.sort()
            guard draws.count > 1 else { throw SmokeFailure.failed("\(mode) did not animate") }
            print(String(format: "%@ animation: %.1f observed frames/s; draw p95 %.1f ms; client CPU %.1f%% of one core; resident %.1f MiB; up to %d placements, %d textures, %.1f KiB texture data/frame",
                         mode, Double(draws.count) * 1000 / elapsed, draws[min(draws.count - 1, draws.count * 95 / 100)],
                         (cpuMilliseconds() - cpu) * 100 / elapsed, Double(try residentBytes()) / 1_048_576,
                         maximumPlacements, maximumTextures, Double(maximumBytes) / 1024))
            print("\(mode) physical footprint by second (MiB): " + footprints.map { String(format: "%.1f", $0) }.joined(separator: ", "))
            for sample in 0..<2 {
                let bitmap = try capture(controller.terminal)
                try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path + ".\(mode)-\(sample).png"))
                if let block = controller.terminal.frameData?.blocks.first {
                    print("\(mode) sample \(sample): \(block.images.count) images, \(block.width)×\(block.height) cells, \(controller.terminal.cellWidth)×\(controller.terminal.cellHeight) points")
                    let summary = block.images.map { "\($0.format) \($0.pixelWidth)×\($0.pixelHeight) at \($0.x),\($0.y) size \($0.width),\($0.height) clip \($0.clipX),\($0.clipY),\($0.clipWidth),\($0.clipHeight)" }.joined(separator: "\n")
                    try (summary + "\n\n" + text(controller.terminal.frameData!)).write(toFile: path + ".\(mode)-\(sample).txt", atomically: true, encoding: .utf8)
                }
                try await Task.sleep(for: .seconds(1))
            }
            controller.terminal.insertText("q", replacementRange: NSRange(location: NSNotFound, length: 0))
            _ = try await wait(controller) { text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
        }
    }
}
