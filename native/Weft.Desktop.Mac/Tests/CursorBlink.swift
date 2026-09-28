import AppKit

extension MacSmoke {
    /// Samples actual caret pixels after shell requests, including focus and steady-style behavior.
    static func exerciseCursorBlink(_ controller: TerminalWindow) async throws {
        let previousApplication = NSWorkspace.shared.frontmostApplication
        let previousPolicy = NSApp.activationPolicy()
        NSApp.setActivationPolicy(.accessory)
        defer {
            NSApp.setActivationPolicy(previousPolicy)
            previousApplication?.activate(options: [])
        }
        let view = controller.terminal
        view.insertText("printf '\\033[2J\\033[H\\033[1 q\\033[?25hCURSOR\\055READY\\n'; IFS= read -r reply\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        let ready = try await wait(controller) { text($0).contains("CURSOR-READY") }
        guard ready.blocks.first?.cursorShape == 1 else {
            throw SmokeFailure.failed("Shell requested blinking block, received cursor style \(ready.blocks.first?.cursorShape ?? -1)")
        }
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        let focusDeadline = Date().addingTimeInterval(2)
        while controller.window?.isKeyWindow != true && Date() < focusDeadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        controller.window?.makeFirstResponder(nil)
        controller.window?.makeFirstResponder(view)
        guard controller.window?.isKeyWindow == true else { throw SmokeFailure.failed("Cursor test needs a key window") }
        let lit = try cursorPixel(view)
        if !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            let deadline = Date().addingTimeInterval(2)
            while try colorDifference(lit, cursorPixel(view)) < 0.08 && Date() < deadline {
                try await Task.sleep(for: .milliseconds(40))
            }
            guard try colorDifference(lit, cursorPixel(view)) > 0.08 else {
                throw SmokeFailure.failed("Focused cursor did not blink")
            }
        }
        controller.window?.makeFirstResponder(nil)
        let unfocused = try cursorPixel(view)
        try await Task.sleep(for: .milliseconds(750))
        guard try colorDifference(unfocused, cursorPixel(view)) < 0.01 else {
            throw SmokeFailure.failed("Unfocused cursor kept blinking")
        }
        controller.window?.makeFirstResponder(view)
        view.insertText("\rprintf '\\033[2 qSTEADY\\055READY\\n'; IFS= read -r reply\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { $0.blocks.first?.cursorShape == 2 }
        let steady = try cursorPixel(view)
        try await Task.sleep(for: .milliseconds(750))
        guard try colorDifference(steady, cursorPixel(view)) < 0.01 else {
            throw SmokeFailure.failed("A terminal-requested steady cursor blinked")
        }
        view.insertText("\rprintf '\\033[0 q'\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { $0.blocks.first?.cursorShape == 0 }
        print("Cursor pixels honor blinking, focus loss, and terminal-requested steady styles.")
    }

    private static func cursorPixel(_ view: TerminalView) throws -> NSColor {
        guard let block = view.frameData?.blocks.first(where: \.active) else { throw SmokeFailure.failed("Missing active caret") }
        let bitmap = try capture(view)
        let x = Int((CGFloat(block.x + block.cursorX) + 0.5) * view.cellWidth * CGFloat(bitmap.pixelsWide) / view.bounds.width)
        let y = Int((CGFloat(block.y + block.cursorY) + 0.5) * view.cellHeight * CGFloat(bitmap.pixelsHigh) / view.bounds.height)
        guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.sRGB) else { throw SmokeFailure.failed("Missing caret pixel") }
        return color
    }

    private static func colorDifference(_ first: NSColor, _ second: NSColor) -> CGFloat {
        abs(first.redComponent - second.redComponent) + abs(first.greenComponent - second.greenComponent)
            + abs(first.blueComponent - second.blueComponent)
    }
}
