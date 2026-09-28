import AppKit

enum SmokeFailure: Error {
    case failed(String)
}

@main
@MainActor
struct MacSmoke {
    static func main() {
        if CommandLine.arguments.dropFirst().first == "--image-cache" {
            do {
                try ImageCacheQualification.run(Array(CommandLine.arguments.dropFirst(2)))
                exit(0)
            } catch {
                FileHandle.standardError.write(Data(("Image cache qualification failed: \(error)\n").utf8))
                exit(1)
            }
        }
        let application = NSApplication.shared
        let recording = CommandLine.arguments.contains("--record-output")
        application.setActivationPolicy(recording || CommandLine.arguments.contains("--accessibility-window") ? .regular : .accessory)
        Task {
            do {
                if CommandLine.arguments.contains("--graphics-app") {
                    try await exerciseGraphicsApp(executable: CommandLine.arguments[1], app: CommandLine.arguments.last!, path: CommandLine.arguments[2])
                    exit(0)
                }
                if CommandLine.arguments.contains("--accessibility-window") {
                    let controller = TerminalWindow(executablePath: CommandLine.arguments[1], autosaveName: nil)
                    controller.showWindow(nil)
                    _ = try await wait(controller) { !$0.blocks.isEmpty }
                    controller.terminal.insertText("printf '\\033[2J\\033[HAccessible terminal café 日本語\\n'\r", replacementRange: NSRange(location: NSNotFound, length: 0))
                    _ = try await wait(controller) { text($0).contains("Accessible terminal café 日本語") }
                    controller.window?.makeKeyAndOrderFront(nil)
                    controller.window?.makeFirstResponder(controller.terminal)
                    NSApp.activate(ignoringOtherApps: true)
                    print("Accessibility window ready")
                    fflush(stdout)
                    try await Task.sleep(for: .seconds(45))
                    controller.close()
                    exit(0)
                }
                if CommandLine.arguments.contains("--qualify") {
                    try await exerciseWorkload(executable: CommandLine.arguments[1], report: CommandLine.arguments[2])
                    exit(0)
                }
                if CommandLine.arguments.contains("--crash-client") {
                    let controller = TerminalWindow(executablePath: CommandLine.arguments[1], autosaveName: nil)
                    _ = try await wait(controller) { !$0.blocks.isEmpty }
                    print("Crash client attached")
                    fflush(stdout)
                    try await Task.sleep(for: .seconds(30))
                    controller.close()
                    throw SmokeFailure.failed("Crash test did not terminate its client")
                }
                if CommandLine.arguments.contains("--reattach-only") {
                    let controller = TerminalWindow(executablePath: CommandLine.arguments[1], autosaveName: nil)
                    _ = try await wait(controller) { !$0.blocks.isEmpty }
                    controller.close()
                    exit(0)
                }
                if CommandLine.arguments.contains("--measure") {
                    let executable = CommandLine.arguments[1]
                    TerminalFont.register(in: URL(fileURLWithPath: executable).deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/Fonts"))
                    let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
                    let frame = try await wait(controller) { !$0.blocks.isEmpty }
                    controller.close()
                    try await exerciseClientResources(executable: executable, blockID: frame.blocks[0].id)
                    exit(0)
                }
                if recording {
                    if #available(macOS 14.0, *) {
                        try await OutputRecording.run(executable: CommandLine.arguments[1], image: CommandLine.arguments[2])
                        exit(0)
                    }
                    throw SmokeFailure.failed("Native output recording requires macOS 14 or newer")
                }
                try await exercise(executable: CommandLine.arguments[1], image: CommandLine.arguments[2])
                print("Native app smoke test passed: ABI, fonts, shell, input, graphics, history, scrollbar clicks and dragging, wheel and precise scrolling, selection, Find, Commands, narrow layout, tabs, splits, sessions, and reattach.")
                exit(0)
            } catch {
                FileHandle.standardError.write(Data(("Native smoke test failed: \(error)\n").utf8))
                exit(1)
            }
        }
        application.run()
    }

    static func exercise(executable: String, image: String) async throws {
        let resources = URL(fileURLWithPath: executable).deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/Fonts")
        TerminalFont.register(in: resources)
        guard let font = NSFont(name: "CascadiaMonoNF-Regular", size: 14),
              "\u{e0b2}\u{e0b0}\u{f179}\u{f015}\u{f017}".unicodeScalars.allSatisfy({ font.coveredCharacterSet.contains($0) }) else {
            throw SmokeFailure.failed("Bundled font is missing prompt symbols")
        }
        guard weft_abi_version() == 4 else { throw SmokeFailure.failed("ABI mismatch") }
        var length: Int32 = 0
        guard weft_poll(0, &length) == nil && length == -1 else { throw SmokeFailure.failed("Invalid handle was accepted") }
        let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { controller.close() }
        controller.showWindow(nil)
        let initial = try await wait(controller) { $0.blocks.count == 1 && text($0).contains("$") }
        let blockId = initial.blocks[0].id
        controller.terminal.insertText("printf '\\033[2J\\033[HNative AppKit terminal: \\033[38;2;12;200;160m界é\\033[0m\\nPrompt symbols:     \\nweft-native-ok\\n'", replacementRange: NSRange(location: NSNotFound, length: 0))
        guard let enter = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [],
            timestamp: 0, windowNumber: controller.window!.windowNumber, context: nil,
            characters: "\r", charactersIgnoringModifiers: "\r", isARepeat: false, keyCode: 36) else {
            throw SmokeFailure.failed("Could not create native key event")
        }
        controller.terminal.keyDown(with: enter)
        let rendered = try await wait(controller) { frame in
            text(frame).contains("weft-native-ok") && frame.blocks.flatMap(\.cells).contains { $0.text == "界" && $0.foreground == 0x0cc8a0 }
        }
        try assertSymbolCounter(controller.terminal, frame: rendered)
        try exerciseAccessibility(controller.terminal)
        try await exerciseAccessibleSelection(controller)
        try await exercisePointerSelection(controller, frame: rendered)
        try await exercisePartialRedraw(controller)
        controller.window?.setContentSize(NSSize(width: 1200, height: 800))
        controller.window?.contentView?.layoutSubtreeIfNeeded()
        _ = try await wait(controller) { $0.blocks[0].width > initial.blocks[0].width }

        guard let toolbar = controller.window?.toolbar,
              toolbar.visibleItems?.contains(where: { $0.itemIdentifier.rawValue == "tabs" }) == true,
              toolbar.visibleItems?.contains(where: { $0.itemIdentifier.rawValue == "newTab" }) == true else {
            throw SmokeFailure.failed("Tabs or New Tab disappeared from the toolbar")
        }
        if let view = controller.window?.contentView?.superview, let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
            view.cacheDisplay(in: view.bounds, to: bitmap)
            guard let png = bitmap.representation(using: .png, properties: [:]) else { throw SmokeFailure.failed("No native render") }
            try png.write(to: URL(fileURLWithPath: image))
        } else {
            throw SmokeFailure.failed("Could not capture the native view")
        }
        controller.terminal.insertText("for i in $(seq 1 100); do printf 'NATIVE-HISTORY-%03d\\n' \"$i\"; done\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        do {
            _ = try await wait(controller) { $0.blocks[0].historyLines > 30 && text($0).contains("NATIVE-HISTORY-100") }
        } catch {
            let capture = Process()
            capture.executableURL = URL(fileURLWithPath: executable)
            capture.arguments = ["capture", blockId, "--history", "120"]
            let pipe = Pipe()
            capture.standardOutput = pipe
            try capture.run()
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            capture.waitUntilExit()
            try data.write(to: URL(fileURLWithPath: image + ".server.txt"))
            print("History diagnostic: " + String(describing: controller.terminal.frameData?.blocks.map { ($0.width, $0.height, $0.historyLines, $0.scrollOffset) }))
            throw error
        }
        controller.terminal.send?(DesktopCommand(operation: "scrollTo", target: blockId, y: Int(Int32.max)))
        _ = try await wait(controller) {
            $0.blocks[0].scrollOffset == $0.blocks[0].historyLines && $0.blocks[0].scrollOffset > 0 && text($0).contains("weft-native-ok")
        }
        guard descendants(controller.terminal).contains(where: { $0 is NSScroller && !$0.isHidden }) else {
            throw SmokeFailure.failed("History has no native scrollbar")
        }
        try await measureScrolling(controller, blockID: blockId)
        try await exerciseScrolling(controller, image: image + ".scrollbar.png")
        try await exerciseHistorySelection(controller)
        controller.terminal.send?(DesktopCommand(operation: "scrollTo", target: blockId, y: Int(Int32.max)))
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == $0.blocks[0].historyLines }
        controller.terminal.selectAll(nil)
        _ = try await wait(controller) { $0.blocks[0].selection != nil }
        let selection = controller.terminal.accessibilitySelectedText()
        controller.terminal.send?(DesktopCommand(operation: "text", target: blockId, text: "printf 'NEW-OUTPUT\\n'\r"))
        try await Task.sleep(for: .milliseconds(150))
        guard controller.terminal.accessibilitySelectedText() == selection else {
            throw SmokeFailure.failed("Output changed the selected text")
        }
        controller.terminal.resume()
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == 0 && text($0).contains("NEW-OUTPUT") }
        try await exerciseComposition(controller)
        try await exerciseKeyboardLayouts(controller)
        controller.performCommand("find")
        guard let findField = controller.window?.contentView.flatMap({ descendants($0).compactMap { $0 as? NSSearchField }.first }) else {
            throw SmokeFailure.failed("Find is not a native search field")
        }
        findField.stringValue = "NATIVE-HISTORY-010"
        controller.controlTextDidChange(Notification(name: NSControl.textDidChangeNotification, object: findField))
        _ = try await wait(controller) { $0.blocks[0].searchMatches == 1 && text($0).contains("NATIVE-HISTORY-010") }
        guard let done = controller.window?.contentView.flatMap({ descendants($0).compactMap { $0 as? NSButton }.first(where: { $0.title == "Done" }) }) else {
            throw SmokeFailure.failed("Find has no Done button")
        }
        done.performClick(nil)
        controller.showCommands()
        let firstPanel = controller.window?.attachedSheet
        controller.showCommands()
        guard let panel = controller.window?.attachedSheet, panel === firstPanel,
              let panelRoot = panel.contentView,
              let closeCommands = descendants(panelRoot).compactMap({ $0 as? NSButton }).first(where: { $0.title == "Close" }) else {
            throw SmokeFailure.failed("Commands did not retain one dismissible panel")
        }
        closeCommands.performClick(nil)
        try await Task.sleep(for: .milliseconds(200))
        controller.terminal.insertText("printf '\\033[2J\\033[H\\033_Ga=T,f=32,s=2,v=1,c=8,r=4,q=2;/wAA/wD/AP8=\\033\\\\'\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        let graphic = try await wait(controller) { $0.blocks[0].images.contains(where: { $0.format == 32 }) }
        try assertImagePixels(controller.terminal, block: graphic.blocks[0], path: image + ".graphics.png")
        try await exerciseGraphics(controller)
        try ImageCacheQualification.run([image, image + ".graphics.png", image + ".animation.png"])
        try await exerciseCursorBlink(controller)
        controller.splitRight()
        _ = try await wait(controller) { $0.blocks.count == 2 }
        controller.newTab()
        _ = try await wait(controller) { $0.tabs.count == 2 && $0.blocks.count == 1 && $0.blocks[0].id != blockId }
        controller.selectTab(initial.activeTab!)
        _ = try await wait(controller) { $0.blocks.count == 2 && $0.blocks.contains(where: { $0.id == blockId }) }
        try await exerciseTabClose(controller)
        controller.window?.setContentSize(NSSize(width: 520, height: 340))
        controller.window?.contentView?.superview?.layoutSubtreeIfNeeded()
        _ = try await wait(controller) { $0.blocks.count == 2 && $0.blocks[0].width < initial.blocks[0].width }
        let toolbarDeadline = Date().addingTimeInterval(3)
        while toolbar.visibleItems?.isEmpty != false && Date() < toolbarDeadline {
            controller.window?.displayIfNeeded()
            try await Task.sleep(for: .milliseconds(30))
        }
        guard toolbar.visibleItems?.contains(where: { $0.itemIdentifier.rawValue == "tabs" }) == true,
              toolbar.visibleItems?.contains(where: { $0.itemIdentifier.rawValue == "newTab" }) == true else {
            throw SmokeFailure.failed("Narrow window hid tabs or New Tab: visible=\(toolbar.isVisible), window=\(String(describing: controller.window?.frame)), items=" + (toolbar.visibleItems ?? []).map { $0.itemIdentifier.rawValue }.joined(separator: ", "))
        }
        controller.newSession()
        guard let sheet = controller.window?.attachedSheet, let content = sheet.contentView,
              let name = descendants(content).compactMap({ $0 as? NSTextField }).first(where: { $0.isEditable }),
              let create = descendants(content).compactMap({ $0 as? NSButton }).first(where: { $0.title == "Create" }) else {
            throw SmokeFailure.failed("New Session did not present a usable native sheet")
        }
        name.stringValue = "Native session"
        create.performClick(nil)
        _ = try await wait(controller) { $0.title == "Native session" && $0.sessions.count == 2 && $0.blocks.count == 1 }
        guard let picker = toolbar.items.first(where: { $0.itemIdentifier.rawValue == "sessions" })?.view as? NSPopUpButton,
              let menu = picker.menu,
              let index = menu.items.firstIndex(where: { $0.representedObject as? String == initial.activeSession }) else {
            throw SmokeFailure.failed("Session menu did not contain the original session")
        }
        menu.performActionForItem(at: index)
        _ = try await wait(controller) { $0.activeSession == initial.activeSession && $0.blocks.contains(where: { $0.id == blockId }) }
        controller.close()
        let reopened = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { reopened.close() }
        _ = try await wait(reopened) { $0.tabs.count == 1 && $0.blocks.count == 2 && $0.blocks.contains(where: { $0.id == blockId }) }
        reopened.close()
    }

    static func text(_ frame: DesktopFrame) -> String { frame.blocks.flatMap(\.cells).map(\.text).joined() }

    static func descendants(_ view: NSView) -> [NSView] {
        view.subviews.flatMap { [$0] + descendants($0) }
    }

    /// Measures real bridge delivery separately from synchronous AppKit drawing.
    static func measureScrolling(_ controller: TerminalWindow, blockID: String) async throws {
        guard let block = controller.terminal.frameData?.blocks.first else { throw SmokeFailure.failed("No scrollable terminal") }
        var delivery: [Double] = []
        var drawing: [Double] = []
        for index in 0..<24 {
            let offset = 1 + index % max(1, block.historyLines - 1)
            let start = ContinuousClock.now
            controller.terminal.send?(DesktopCommand(operation: "scrollTo", target: blockID, y: offset))
            while controller.terminal.frameData?.blocks.first?.scrollOffset != offset {
                if start.duration(to: .now) > .seconds(5) { throw SmokeFailure.failed("Scrolling stalled") }
                try await Task.sleep(for: .milliseconds(1))
            }
            let frameReady = ContinuousClock.now
            controller.terminal.display()
            delivery.append(milliseconds(start.duration(to: frameReady)))
            drawing.append(milliseconds(frameReady.duration(to: .now)))
        }
        delivery.sort()
        drawing.sort()
        print(String(format: "Scroll sample: frame p50 %.1f ms, p95 %.1f ms; AppKit draw p50 %.1f ms, p95 %.1f ms",
                     delivery[12], delivery[22], drawing[12], drawing[22]))
    }

    static func milliseconds(_ duration: Duration) -> Double {
        Double(duration.components.seconds) * 1_000 + Double(duration.components.attoseconds) / 1e15
    }

    /// A click, including small pointer jitter, must leave the caret at the process cursor.
    static func exercisePointerSelection(_ controller: TerminalWindow, frame: DesktopFrame) async throws {
        let view = controller.terminal
        let block = frame.blocks[0]
        let point = NSPoint(x: CGFloat(block.x + block.width - 10) * view.cellWidth + view.cellWidth / 2,
                            y: CGFloat(block.y + 6) * view.cellHeight + view.cellHeight / 2)
        let before = try capture(view)
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: point))
        let held = try capture(view)
        guard held.bitmapData != nil, before.representation(using: .png, properties: [:]) == held.representation(using: .png, properties: [:]) else {
            throw SmokeFailure.failed("A plain pointer press hid the terminal caret or painted a selection")
        }
        let jitter = NSPoint(x: point.x + 1, y: point.y + 1)
        view.mouseDragged(with: try mouseEvent(.leftMouseDragged, in: view, point: jitter))
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: jitter))
        guard view.accessibilitySelectedText()?.isEmpty != false,
              before.representation(using: .png, properties: [:]) == (try capture(view)).representation(using: .png, properties: [:]) else {
            throw SmokeFailure.failed("A click with pointer jitter left a cursor-like selection")
        }
        let start = NSPoint(x: CGFloat(block.x) * view.cellWidth + view.cellWidth / 2,
                            y: CGFloat(block.y) * view.cellHeight + view.cellHeight / 2)
        let end = NSPoint(x: start.x + 5 * view.cellWidth, y: start.y)
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: start))
        view.mouseDragged(with: try mouseEvent(.leftMouseDragged, in: view, point: end))
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: end))
        _ = try await wait(controller) { $0.blocks[0].selection?.text == "Native" }
        guard view.accessibilitySelectedText() == "Native" else { throw SmokeFailure.failed("Dragging no longer selects terminal text") }
        guard view.accessibilityString(for: view.accessibilitySelectedTextRange()) == "Native" else {
            throw SmokeFailure.failed("Accessible selection does not match copied text")
        }
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: point))
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: point))
        _ = try await wait(controller) { $0.blocks[0].selection == nil }
        guard view.accessibilitySelectedText()?.isEmpty != false else { throw SmokeFailure.failed("A click did not clear the previous selection") }
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: start))
        view.mouseDragged(with: try mouseEvent(.leftMouseDragged, in: view, point: end))
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: end))
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: point))
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: point))
        view.insertText("printf 'selection\\055cleared\\n'\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { text($0).contains("selection-cleared") }
        guard view.accessibilitySelectedText()?.isEmpty != false else {
            throw SmokeFailure.failed("A quick click left a selection whose frame had not arrived yet")
        }
    }

    static func capture(_ view: NSView) throws -> NSBitmapImageRep {
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw SmokeFailure.failed("Could not capture native drawing") }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        return bitmap
    }

    /// Checks retained display pixels after real shell echo and erasure, without forcing a full repaint.
    static func exercisePartialRedraw(_ controller: TerminalWindow) async throws {
        let view = controller.terminal
        let ready = try await wait(controller) { text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
        let block = ready.blocks[0]
        let before = try captureLayer(view)
        view.insertText("typing", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { $0.blocks[0].cursorX == block.cursorX + 6 && text($0).contains("typing") }
        let typed = try captureLayer(view)
        guard let oldPixels = before.bitmapData, let newPixels = typed.bitmapData,
              before.bytesPerRow == typed.bytesPerRow, before.pixelsHigh == typed.pixelsHigh else {
            throw SmokeFailure.failed("No retained layer pixels")
        }
        let changed = (0..<before.pixelsHigh).filter {
            memcmp(oldPixels + $0 * before.bytesPerRow, newPixels + $0 * typed.bytesPerRow, before.bytesPerRow) != 0
        }
        guard let first = changed.first, let last = changed.last, last - first < Int(view.cellHeight) else {
            throw SmokeFailure.failed("Typing repainted or erased pixels outside the prompt row")
        }
        for _ in 0..<6 { view.send?(DesktopCommand(operation: "key", target: block.id, text: "BSpace")) }
        _ = try await wait(controller) { $0.blocks[0].cursorX == block.cursorX && !text($0).contains("typing") }
        let erased = try captureLayer(view)
        guard before.representation(using: .png, properties: [:]) == erased.representation(using: .png, properties: [:]) else {
            throw SmokeFailure.failed("Erasing input left stale text or cursor pixels in the backing layer")
        }
    }

    /// Reads the layer AppKit has painted, rather than redrawing the view into a screenshot context.
    static func captureLayer(_ view: NSView) throws -> NSBitmapImageRep {
        view.displayIfNeeded()
        guard let layer = view.layer,
              let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(view.bounds.width), pixelsHigh: Int(view.bounds.height),
                bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
            throw SmokeFailure.failed("No terminal backing layer to capture")
        }
        layer.render(in: context.cgContext)
        return bitmap
    }

    /// Exercises AppKit's own hit testing and tracking loop against the real terminal history.
    static func exerciseScrolling(_ controller: TerminalWindow, image: String) async throws {
        let previousApplication = NSWorkspace.shared.frontmostApplication
        let previousPolicy = NSApp.activationPolicy()
        NSApp.setActivationPolicy(.accessory)
        defer {
            NSApp.setActivationPolicy(previousPolicy)
            previousApplication?.activate(options: [])
        }
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        let terminal = controller.terminal
        terminal.resume()
        let live = try await wait(controller) {
            controller.window?.isKeyWindow == true && $0.blocks[0].scrollOffset == 0 && $0.blocks[0].viewVersion == 0
        }
        guard let scroller = descendants(terminal).compactMap({ $0 as? TerminalScroller }).first,
              !scroller.isHidden, scroller.isEnabled, scroller.usableParts == .allScrollerParts else {
            throw SmokeFailure.failed("History scrollbar is missing")
        }
        let knob = scroller.rect(for: .knob)
        guard knob.width > 0, knob.height > knob.width, knob.height < scroller.bounds.height,
              knob.maxY > scroller.bounds.midY else {
            throw SmokeFailure.failed("History scrollbar is not vertical with its thumb at the live end: \(knob)")
        }
        let proportion = CGFloat(live.blocks[0].height) / CGFloat(live.blocks[0].historyLines + live.blocks[0].height)
        guard abs(scroller.knobProportion - proportion) < 0.01,
              let scrollView = descendants(terminal).compactMap({ $0 as? TerminalScrollView }).first else {
            throw SmokeFailure.failed("The scrollbar does not represent retained terminal history: \(scroller.knobProportion), expected \(proportion)")
        }
        scrollView.flashScrollers()
        try await Task.sleep(for: .milliseconds(200))
        try assertScrollbarPixels(terminal, scroller: scroller, image: image)
        let thumbHit = scrollView.hitTest(scroller.convert(NSPoint(x: knob.midX, y: knob.midY), to: terminal))
        guard thumbHit === scroller || thumbHit?.isDescendant(of: scroller) == true,
              scrollView.hitTest(NSPoint(x: terminal.bounds.midX, y: scrollView.frame.midY)) == nil else {
            throw SmokeFailure.failed("Scrollbar hit testing intercepted terminal content or missed the thumb")
        }
        let pagePoint = NSPoint(x: knob.midX, y: knob.minY / 2)
        let pagePart = scroller.testPart(scroller.convert(pagePoint, to: nil))
        guard pagePart == .decrementPage || pagePart == .knobSlot else {
            throw SmokeFailure.failed("Page click hit part \(pagePart.rawValue), knob \(knob), bounds \(scroller.bounds)")
        }
        let pageUp = try mouseEvent(.leftMouseUp, in: scroller, point: pagePoint)
        NSApp.postEvent(pageUp, atStart: false)
        scroller.mouseDown(with: try mouseEvent(.leftMouseDown, in: scroller, point: pagePoint))
        _ = try await wait(controller) { $0.blocks[0].scrollOffset > 0 }

        terminal.resume()
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == 0 }
        let thumb = scroller.rect(for: .knob)
        let start = NSPoint(x: thumb.midX, y: thumb.midY)
        let middle = NSPoint(x: thumb.midX, y: scroller.bounds.midY)
        let drag = try mouseEvent(.leftMouseDragged, in: scroller, point: middle)
        let up = try mouseEvent(.leftMouseUp, in: scroller, point: middle)
        let observation = ScrollObservation(drag: drag, release: up)
        let move = Timer(timeInterval: 0.03, repeats: false) { _ in
            MainActor.assumeIsolated { NSApp.postEvent(observation.drag, atStart: false) }
        }
        let finish = Timer(timeInterval: 0.6, repeats: false) { _ in
            MainActor.assumeIsolated {
                observation.updatedWhileDragging = scroller.trackingKnob && (terminal.frameData?.blocks.first?.scrollOffset ?? 0) > 0
                observation.thumbValue = scroller.doubleValue
                NSApp.postEvent(observation.release, atStart: false)
            }
        }
        RunLoop.main.add(move, forMode: .common)
        RunLoop.main.add(finish, forMode: .common)
        scroller.mouseDown(with: try mouseEvent(.leftMouseDown, in: scroller, point: start))
        move.invalidate()
        finish.invalidate()
        guard observation.updatedWhileDragging, (0.3...0.7).contains(observation.thumbValue) else {
            throw SmokeFailure.failed("Scrollbar did not update during its native tracking loop: \(observation.updatedWhileDragging), thumb=\(observation.thumbValue)")
        }
        let middleFrame = try await wait(controller) { $0.blocks[0].scrollOffset > 0 }
        guard abs(Double(middleFrame.blocks[0].scrollOffset) / Double(live.blocks[0].historyLines) - 0.5) < 0.15 else {
            throw SmokeFailure.failed("Dragging the thumb to the middle did not reach the middle of history")
        }
        for bottom in [false, true] {
            let currentThumb = scroller.rect(for: .knob)
            let destination = NSPoint(x: currentThumb.midX, y: bottom ? scroller.bounds.maxY : scroller.bounds.minY)
            NSApp.postEvent(try mouseEvent(.leftMouseDragged, in: scroller, point: destination), atStart: false)
            NSApp.postEvent(try mouseEvent(.leftMouseUp, in: scroller, point: destination), atStart: false)
            scroller.mouseDown(with: try mouseEvent(.leftMouseDown, in: scroller,
                                                    point: NSPoint(x: currentThumb.midX, y: currentThumb.midY)))
            _ = try await wait(controller) {
                $0.blocks[0].scrollOffset == (bottom ? 0 : $0.blocks[0].historyLines)
            }
        }

        terminal.resume()
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == 0 }
        let point = terminal.convert(NSPoint(x: terminal.bounds.midX, y: terminal.bounds.midY), to: nil)
        guard let wheel = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: 3, wheel2: 0, wheel3: 0) else {
            throw SmokeFailure.failed("Could not create wheel input")
        }
        // An event without a window number reports screen coordinates; use the same local point when calling the view.
        wheel.location = CGPoint(x: point.x, y: (NSScreen.screens.first?.frame.height ?? 0) - point.y)
        guard let wheelEvent = NSEvent(cgEvent: wheel) else { throw SmokeFailure.failed("Could not decode wheel input") }
        terminal.scrollWheel(with: wheelEvent)
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == 3 }
        terminal.resume()
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == 0 }
        guard let pixels = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 1,
                                   wheel1: Int32(terminal.cellHeight / 2), wheel2: 0, wheel3: 0) else {
            throw SmokeFailure.failed("Could not create precise scrolling input")
        }
        pixels.location = wheel.location
        guard let pixelEvent = NSEvent(cgEvent: pixels), pixelEvent.hasPreciseScrollingDeltas else {
            throw SmokeFailure.failed("Precise scrolling was not preserved")
        }
        for _ in 0..<4 { terminal.scrollWheel(with: pixelEvent) }
        let expected = Int((pixelEvent.scrollingDeltaY * 4) / terminal.cellHeight)
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == expected }

        guard let fast = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: 1000, wheel2: 0, wheel3: 0),
              let reverse = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: -1, wheel2: 0, wheel3: 0) else {
            throw SmokeFailure.failed("Could not create fast wheel input")
        }
        fast.location = wheel.location
        reverse.location = wheel.location
        guard let fastEvent = NSEvent(cgEvent: fast), let reverseEvent = NSEvent(cgEvent: reverse) else {
            throw SmokeFailure.failed("Could not decode fast wheel input")
        }
        terminal.scrollWheel(with: fastEvent)
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == $0.blocks[0].historyLines }
        terminal.scrollWheel(with: reverseEvent)
        _ = try await wait(controller) { $0.blocks[0].scrollOffset == $0.blocks[0].historyLines - 1 }
    }

    static func mouseEvent(_ type: NSEvent.EventType, in view: NSView, point: NSPoint) throws -> NSEvent {
        guard let window = view.window,
              let event = NSEvent.mouseEvent(with: type, location: view.convert(point, to: nil), modifierFlags: [],
                                            timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: window.windowNumber,
                                            context: nil, eventNumber: 0, clickCount: 1, pressure: 1) else {
            throw SmokeFailure.failed("Could not create native pointer input")
        }
        return event
    }

    /// Geometry alone is insufficient: the native thumb must visibly differ from its empty track.
    static func assertScrollbarPixels(_ terminal: TerminalView, scroller: NSScroller, image: String) throws {
        guard let bitmap = terminal.bitmapImageRepForCachingDisplay(in: terminal.bounds) else {
            throw SmokeFailure.failed("Could not capture the scrollbar")
        }
        terminal.cacheDisplay(in: terminal.bounds, to: bitmap)
        try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: image))
        let knob = scroller.rect(for: .knob)
        let thumb = scroller.convert(NSPoint(x: knob.midX, y: knob.midY), to: terminal)
        let track = scroller.convert(NSPoint(x: knob.midX, y: knob.minY / 2), to: terminal)
        let scaleX = CGFloat(bitmap.pixelsWide) / terminal.bounds.width
        let scaleY = CGFloat(bitmap.pixelsHigh) / terminal.bounds.height
        guard let thumbColor = bitmap.colorAt(x: Int(thumb.x * scaleX), y: Int(thumb.y * scaleY))?.usingColorSpace(.sRGB),
              let trackColor = bitmap.colorAt(x: Int(track.x * scaleX), y: Int(track.y * scaleY))?.usingColorSpace(.sRGB),
              thumbColor.redComponent - trackColor.redComponent > 0.1 else {
            throw SmokeFailure.failed("The scrollbar thumb is not visibly distinct from the full-height track")
        }
    }

    static func assertImagePixels(_ view: TerminalView, block: DesktopBlock, path: String) throws {
        guard let placement = block.images.first, let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            throw SmokeFailure.failed("No image placement to inspect")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path))
        guard let srgb = bitmap.converting(to: .sRGB, renderingIntent: .default) else {
            throw SmokeFailure.failed("Could not normalize the captured display color profile")
        }
        let x = (CGFloat(block.x) + placement.clipX + placement.clipWidth * 0.25) * view.cellWidth
        let y = (CGFloat(block.y) + placement.clipY + placement.clipHeight * 0.5) * view.cellHeight
        let pixelX = Int(x * CGFloat(bitmap.pixelsWide) / view.bounds.width)
        let pixelY = Int(y * CGFloat(bitmap.pixelsHigh) / view.bounds.height)
        let greenX = Int((x + placement.clipWidth * view.cellWidth * 0.5) * CGFloat(bitmap.pixelsWide) / view.bounds.width)
        // Display captures are color-managed; compare both source colors within that same profile.
        guard let color = srgb.colorAt(x: pixelX, y: pixelY)?.usingColorSpace(.sRGB),
              let green = srgb.colorAt(x: greenX, y: pixelY)?.usingColorSpace(.sRGB),
              color.redComponent - color.greenComponent > 0.5,
              green.greenComponent - green.redComponent > 0.5 else {
            throw SmokeFailure.failed("Terminal image pixels were not rendered at \(pixelX),\(pixelY): \(String(describing: bitmap.colorAt(x: pixelX, y: pixelY))), \(placement)")
        }
    }

    static func assertSymbolCounter(_ view: TerminalView, frame: DesktopFrame) throws {
        guard let block = frame.blocks.first,
              let index = block.cells.firstIndex(where: { $0.text == "\u{f017}" }),
              let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            throw SmokeFailure.failed("No clock symbol to inspect")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        let scaleX = CGFloat(bitmap.pixelsWide) / view.bounds.width
        let scaleY = CGFloat(bitmap.pixelsHigh) / view.bounds.height
        let left = Int(CGFloat(block.x + index % block.width) * view.cellWidth * scaleX)
        let top = Int(CGFloat(block.y + index / block.width) * view.cellHeight * scaleY)
        let width = Int(view.cellWidth * scaleX)
        let height = Int(view.cellHeight * scaleY)
        var ink: [NSPoint] = []
        for y in top..<min(bitmap.pixelsHigh, top + height) {
            for x in left..<min(bitmap.pixelsWide, left + width) {
                if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.sRGB), color.redComponent > 0.5 {
                    ink.append(NSPoint(x: x, y: y))
                }
            }
        }
        guard let minX = ink.map(\.x).min(), let maxX = ink.map(\.x).max(),
              let minY = ink.map(\.y).min(), let maxY = ink.map(\.y).max() else {
            throw SmokeFailure.failed("Clock symbol rendered no pixels")
        }
        let coverage = CGFloat(ink.count) / ((maxX - minX + 1) * (maxY - minY + 1))
        guard coverage < 0.7 else { throw SmokeFailure.failed("Clock symbol lost its inner detail: \(coverage)") }
    }

    static func wait(_ controller: TerminalWindow, pollEvery: Duration = .milliseconds(30), line: UInt = #line, until predicate: (DesktopFrame) -> Bool) async throws -> DesktopFrame {
        let deadline = Date().addingTimeInterval(20)
        while Date() < deadline {
            if let frame = controller.terminal.frameData {
                if let error = frame.error { throw SmokeFailure.failed(error) }
                if predicate(frame) { return frame }
            }
            try await Task.sleep(for: pollEvery)
        }
        let state = controller.terminal.frameData?.blocks.map { "\($0.width)x\($0.height), cursor=\($0.cursorShape), history=\($0.historyLines), offset=\($0.scrollOffset), find=\($0.searchQuery):\($0.searchMatches)" }.joined(separator: "; ") ?? "no frame"
        throw SmokeFailure.failed("Timed out at line \(line): \(state). " + (controller.terminal.frameData.map(text) ?? "no frame"))
    }
}
