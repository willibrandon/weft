import AppKit

extension MacSmoke {
    /// Uses two real attachments to verify resize ownership and surviving client teardown.
    static func exerciseWindows(_ first: TerminalWindow, executable: String) async throws {
        let second = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { second.close() }
        _ = try await wait(second) { $0.blocks.count == 1 }
        // Connecting hides the new window's status line, which gives its terminal more rows at the next layout. The
        // session takes the latest client's size, so that resize must land before the first window's below.
        second.window?.contentView?.layoutSubtreeIfNeeded()
        let initial = try await wait(second) {
            $0.blocks.count == 1 && $0.blocks[0].width == second.terminal.columns - 2
                && $0.blocks[0].height == second.terminal.rows - 2
        }
        let blockID = initial.blocks[0].id
        for size in [NSSize(width: 620, height: 380), NSSize(width: 1280, height: 820), NSSize(width: 780, height: 500)] {
            first.window?.setContentSize(size)
            first.window?.contentView?.layoutSubtreeIfNeeded()
            _ = try await wait(first) { $0.blocks.first?.id == blockID && $0.blocks[0].width == first.terminal.columns - 2 }
            _ = try await wait(second) { $0.blocks.first?.width == first.terminal.frameData?.blocks.first?.width }
            first.terminal.displayIfNeeded()
            guard first.terminal.accessibilityFrame(for: first.terminal.accessibilitySelectedTextRange()).height > 0 else {
                throw SmokeFailure.failed("Resize lost accessible caret geometry")
            }
        }
        // Render the same live content at both backing scales without changing the user's display mode.
        var ink: [Int] = []
        for scale in [1, 2] {
            let view = first.terminal
            guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(view.bounds.width) * scale,
                    pixelsHigh: Int(view.bounds.height) * scale, bitsPerSample: 8, samplesPerPixel: 4,
                    hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else {
                throw SmokeFailure.failed("Could not allocate a backing-scale capture")
            }
            bitmap.size = view.bounds.size
            view.cacheDisplay(in: view.bounds, to: bitmap)
            guard let pixels = bitmap.bitmapData else { throw SmokeFailure.failed("Backing-scale capture has no pixels") }
            var count = 0
            for row in 0..<bitmap.pixelsHigh {
                for column in 0..<bitmap.pixelsWide {
                    let offset = row * bitmap.bytesPerRow + column * 4
                    if pixels[offset] > 180 && pixels[offset + 1] > 180 && pixels[offset + 2] > 180 { count += 1 }
                }
            }
            ink.append(count)
        }
        guard ink[0] > 100, Double(ink[1]) / Double(ink[0]) > 2.5, Double(ink[1]) / Double(ink[0]) < 5.5 else {
            throw SmokeFailure.failed("Terminal text did not scale with its backing bitmap: \(ink)")
        }
        if NSScreen.screens.count > 1 {
            for screen in NSScreen.screens {
                first.window?.setFrameOrigin(NSPoint(x: screen.visibleFrame.minX + 20, y: screen.visibleFrame.minY + 20))
                try await Task.sleep(for: .milliseconds(100))
                guard first.window?.backingScaleFactor == screen.backingScaleFactor else {
                    throw SmokeFailure.failed("Window did not adopt the destination display's backing scale")
                }
            }
        }
        print("Display check: \(NSScreen.screens.count) connected display(s); real cross-display movement \(NSScreen.screens.count > 1 ? "checked" : "requires another display").")
        second.close()
        first.terminal.send?(DesktopCommand(operation: "text", target: blockID, text: "printf 'WINDOW\\055SURVIVED\\n'\r"))
        _ = try await wait(first) { text($0).contains("WINDOW-SURVIVED") }
        print("Window checks passed: shared attachments, repeated resizing, 1×/2× drawing, and closing a peer.")
    }
}
