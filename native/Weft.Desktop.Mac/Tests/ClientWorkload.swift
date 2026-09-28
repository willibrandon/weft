import AppKit

extension MacSmoke {
    /// Exercises concurrent real PTY output while a second pane remains interactive.
    static func exerciseWorkload(executable: String, report: String) async throws {
        let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { controller.close() }
        controller.showWindow(nil)
        _ = try await wait(controller) { !$0.blocks.isEmpty }
        controller.terminal.send?(DesktopCommand(operation: "newSession", text: "Qualification"))
        let initial = try await wait(controller) { $0.title == "Qualification" && $0.blocks.count == 1 && text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
        let originalTab = initial.activeTab!
        let originalBlock = initial.blocks[0].id
        let baseline = try residentBytes()
        var tabs: [String] = []
        var blocks: [String] = []
        for count in 1...15 {
            controller.newTab()
            let frame = try await wait(controller) { $0.tabs.count == initial.tabs.count + count && $0.blocks.count == 1 && text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
            tabs.append(frame.activeTab!)
            blocks.append(frame.blocks[0].id)
        }
        controller.selectTab(originalTab)
        _ = try await wait(controller) { $0.activeTab == originalTab && $0.blocks.count == 1 && text($0).trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") }
        controller.splitRight()
        let split = try await wait(controller) { $0.blocks.count == 2 && $0.blocks.contains(where: {
            $0.active && $0.id != originalBlock && $0.cells.map(\.text).joined().trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$")
        }) }
        let inputBlock = split.blocks.first(where: { $0.id != originalBlock })!
        controller.terminal.setMarkedText("日本語", selectedRange: NSRange(location: 3, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        controller.terminal.send?(DesktopCommand(operation: "focus", target: originalBlock))
        _ = try await wait(controller) { $0.blocks.first(where: { $0.active })?.id == originalBlock }
        guard !controller.terminal.hasMarkedText() else { throw SmokeFailure.failed("Composition followed focus into another pane") }
        controller.terminal.send?(DesktopCommand(operation: "focus", target: inputBlock.id))
        _ = try await wait(controller) { $0.blocks.first(where: { $0.active })?.id == inputBlock.id }
        let manyTabs = try residentBytes()
        for id in blocks {
            controller.terminal.send?(DesktopCommand(operation: "text", target: id,
                text: "awk 'BEGIN {for(i=1;i<=2000;i++) printf \"hidden %06d 0123456789abcdefghijklmnopqrstuvwxyz\\n\",i}'; printf 'HIDDEN\\055DONE\\n'\r"))
        }
        controller.terminal.send?(DesktopCommand(operation: "text", target: originalBlock,
            text: "for frame in $(seq 1 120); do awk -v f=\"$frame\" 'BEGIN {for(i=1;i<=30;i++) printf \"visible %03d %03d 0123456789abcdefghijklmnopqrstuvwxyz\\n\",f,i}'; sleep .02; done; printf 'OUTPUT\\055DONE\\n'\r"))
        _ = try await wait(controller) { $0.blocks.first(where: { $0.id == originalBlock })?.cells.map(\.text).joined().contains("visible 001") == true }
        var latency: [Double] = []
        var drawing: [Double] = []
        let started = ContinuousClock.now
        for _ in 0..<40 {
            let start = ContinuousClock.now
            controller.terminal.insertText("x", replacementRange: NSRange(location: NSNotFound, length: 0))
            _ = try await wait(controller, pollEvery: .milliseconds(1)) { $0.blocks.first(where: { $0.id == inputBlock.id })?.cursorX == inputBlock.cursorX + 1 }
            let painting = ContinuousClock.now
            controller.terminal.displayIfNeeded()
            drawing.append(milliseconds(painting.duration(to: .now)))
            latency.append(milliseconds(start.duration(to: .now)))
            controller.terminal.send?(DesktopCommand(operation: "key", target: inputBlock.id, text: "BSpace"))
            _ = try await wait(controller, pollEvery: .milliseconds(1)) { $0.blocks.first(where: { $0.id == inputBlock.id })?.cursorX == inputBlock.cursorX }
        }
        _ = try await wait(controller) { $0.blocks.first(where: { $0.id == originalBlock })?.cells.map(\.text).joined().contains("OUTPUT-DONE") == true }
        let elapsed = milliseconds(started.duration(to: .now))
        let loaded = try residentBytes()
        var switching: [Double] = []
        for (index, tab) in tabs.enumerated() {
            let start = ContinuousClock.now
            controller.selectTab(tab)
            _ = try await wait(controller, pollEvery: .milliseconds(1)) {
                $0.activeTab == tab && $0.blocks.first?.id == blocks[index] && text($0).contains("HIDDEN-DONE")
            }
            controller.terminal.displayIfNeeded()
            switching.append(milliseconds(start.duration(to: .now)))
        }
        controller.selectTab(originalTab)
        _ = try await wait(controller) { $0.activeTab == originalTab && $0.blocks.count == 2 && ($0.blocks.first(where: { $0.id == originalBlock })?.historyLines ?? 0) > 100 }
        let scrollStart = ContinuousClock.now
        controller.terminal.send?(DesktopCommand(operation: "scrollTo", target: originalBlock, y: Int(Int32.max)))
        _ = try await wait(controller, pollEvery: .milliseconds(1)) { $0.blocks.first(where: { $0.id == originalBlock })?.scrollOffset ?? 0 > 0 }
        controller.terminal.displayIfNeeded()
        let scroll = milliseconds(scrollStart.duration(to: .now))
        controller.terminal.resume(originalBlock)
        for tab in tabs { controller.terminal.send?(DesktopCommand(operation: "closeTab", target: tab)) }
        controller.terminal.send?(DesktopCommand(operation: "closeBlock", target: inputBlock.id))
        _ = try await wait(controller) { $0.tabs.count == initial.tabs.count && $0.blocks.count == 1 }
        controller.close()
        try await Task.sleep(for: .milliseconds(500))
        let released = try residentBytes()
        latency.sort(); drawing.sort(); switching.sort()
        let metrics: [String: Double] = [
            "tabs": 16, "hiddenOutputLines": 30_000, "visibleOutputLines": 3600,
            "workloadMilliseconds": elapsed, "inputPaintP50Milliseconds": latency[20],
            "inputPaintP95Milliseconds": latency[37], "drawP95Milliseconds": drawing[37],
            "switchP95Milliseconds": switching[14], "historyMilliseconds": scroll,
            "clientBaselineMiB": Double(baseline) / 1_048_576, "clientEmptyTabsMiB": Double(manyTabs) / 1_048_576,
            "clientOutputMiB": Double(loaded) / 1_048_576, "clientAfterCloseMiB": Double(released) / 1_048_576
        ]
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(metrics).write(to: URL(fileURLWithPath: report))
        print(String(format: "Workload passed: 16 tabs, 33,600 output lines; input paint p50 %.1f ms, p95 %.1f ms; tab switch p95 %.1f ms; client resident %.1f / %.1f / %.1f / %.1f MiB (initial/empty tabs/output/closed)",
                     latency[20], latency[37], switching[14], metrics["clientBaselineMiB"]!, metrics["clientEmptyTabsMiB"]!, metrics["clientOutputMiB"]!, metrics["clientAfterCloseMiB"]!))
        // Bitmap allocations belong to rendering qualification, after the workload memory sample.
        let windows = TerminalWindow(executablePath: executable, autosaveName: nil)
        defer { windows.close() }
        windows.showWindow(nil)
        _ = try await wait(windows) { $0.blocks.count == 1 }
        try await exerciseWindows(windows, executable: executable)
    }
}
