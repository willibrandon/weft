import AppKit

extension MacSmoke {
    /// Samples real input delivery, idle CPU, and resident memory while repeatedly releasing native clients.
    static func exerciseClientResources(executable: String, blockID: String) async throws {
        var times: [Double] = []
        var resident: [UInt64] = []
        var input: [Double] = []
        var idle = 0.0
        var initialPaint = 0.0
        for iteration in 0..<5 {
            let start = ContinuousClock.now
            let controller = TerminalWindow(executablePath: executable, autosaveName: nil)
            if iteration == 0 { controller.showWindow(nil) }
            let frame = try await wait(controller, pollEvery: .milliseconds(1)) {
                $0.blocks.contains(where: { $0.id == blockID })
                    && $0.blocks.first(where: { $0.active })?.cells.map(\.text).joined().trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("$") == true
            }
            times.append(milliseconds(start.duration(to: .now)))
            if iteration == 0 {
                let target = frame.blocks.first(where: { $0.active }) ?? frame.blocks[0]
                // Login prompts can wrap when the attached window resizes an existing split.
                // Establish the cursor after that resize, independent of the host's prompt.
                controller.terminal.send?(DesktopCommand(operation: "text", target: target.id,
                    text: "PS1='$ '; printf '\\033[2J\\033[H'\r"))
                let ready = try await wait(controller) {
                    $0.blocks.first(where: { $0.id == target.id })?.cells.map(\.text).joined()
                        .trimmingCharacters(in: .whitespacesAndNewlines) == "$"
                }
                let block = ready.blocks.first(where: { $0.id == target.id })!
                // Typing starts after the prompt is painted, as it does for a person.
                // Keep cold window drawing separate from the steady input distribution.
                guard controller.window?.isVisible == true else { throw SmokeFailure.failed("The measured terminal window is not visible") }
                controller.window?.displayIfNeeded()
                controller.terminal.displayIfNeeded()
                initialPaint = milliseconds(start.duration(to: .now))
                for _ in 0..<100 {
                    let beginning = ContinuousClock.now
                    controller.terminal.insertText("x", replacementRange: NSRange(location: NSNotFound, length: 0))
                    _ = try await wait(controller, pollEvery: .milliseconds(1)) { $0.blocks.first(where: { $0.id == block.id })?.cursorX == block.cursorX + 1 }
                    controller.terminal.displayIfNeeded()
                    input.append(milliseconds(beginning.duration(to: .now)))
                    controller.terminal.send?(DesktopCommand(operation: "key", target: block.id, text: "BSpace"))
                    _ = try await wait(controller, pollEvery: .milliseconds(1)) { $0.blocks.first(where: { $0.id == block.id })?.cursorX == block.cursorX }
                }
                let before = cpuMilliseconds()
                try await Task.sleep(for: .milliseconds(750))
                idle = cpuMilliseconds() - before
            }
            controller.close()
            try await Task.sleep(for: .milliseconds(100))
            resident.append(try residentBytes())
        }
        let report: [String: Any] = [
            "initialPaintMilliseconds": initialPaint,
            "inputPaintMilliseconds": input,
            "warmAttachMilliseconds": times,
            "idleCpuMilliseconds": idle,
            "residentAfterCloseBytes": resident
        ]
        let reportPath = URL(fileURLWithPath: CommandLine.arguments[2]).deletingLastPathComponent()
            .appendingPathComponent("qualification-resources.json")
        try JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys]).write(to: reportPath)
        times.sort()
        input.sort()
        print(String(format: "Client sample: warm attach p50 %.1f ms; initial paint %.1f ms; input to AppKit paint p50 %.1f ms, p95 %.1f ms; idle CPU %.1f ms / 750 ms; resident after first/fifth close %.1f / %.1f MiB",
                     times[2], initialPaint, input[49], input[94], idle, Double(resident[0]) / 1_048_576, Double(resident[4]) / 1_048_576))
        if CommandLine.arguments.contains("--profile-memory") {
            print("Memory profile ready: pid \(ProcessInfo.processInfo.processIdentifier)")
            fflush(stdout)
            try await Task.sleep(for: .seconds(30))
        }
        try PerformanceLimits.client(attach: times[2], input: input[94], idle: idle, resident: resident)
    }

    static func cpuMilliseconds() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        return Double(usage.ru_utime.tv_sec + usage.ru_stime.tv_sec) * 1_000
            + Double(usage.ru_utime.tv_usec + usage.ru_stime.tv_usec) / 1_000
    }

    static func residentBytes() throws -> UInt64 {
        var info = mach_task_basic_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info_data_t>.size / MemoryLayout<natural_t>.size)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { throw SmokeFailure.failed("Could not measure client resident memory") }
        return info.resident_size
    }

    /// Separates memory charged to the process from resident shared mappings and reusable allocations.
    static func footprintBytes() throws -> UInt64 {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<natural_t>.size)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { throw SmokeFailure.failed("Could not measure client physical footprint") }
        return info.phys_footprint
    }
}
