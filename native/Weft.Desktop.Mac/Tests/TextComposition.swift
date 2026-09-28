import AppKit

extension MacSmoke {
    /// Verifies marked text stays local and a partial composition replacement commits exact UTF-8 bytes to a PTY.
    static func exerciseComposition(_ controller: TerminalWindow) async throws {
        guard let runtime = ProcessInfo.processInfo.environment["WEFT_SOCKET_DIR"] else {
            throw SmokeFailure.failed("Composition test requires an isolated runtime")
        }
        let file = URL(fileURLWithPath: runtime).deletingLastPathComponent().appendingPathComponent("composition.bin")
        let committed = "é界😀"
        let view = controller.terminal
        view.insertText("stty raw -echo; printf 'IME\\055READY'; dd bs=1 count=\(committed.utf8.count) of='\(file.path)' 2>/dev/null; stty sane; printf 'IME\\055DONE\\n'\r",
                        replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { text($0).contains("IME-READY") }
        view.setMarkedText("取り消し", selectedRange: NSRange(location: 4, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        view.unmarkText()
        view.setMarkedText("输入", selectedRange: NSRange(location: 0, length: 2), replacementRange: NSRange(location: NSNotFound, length: 0))
        view.setMarkedText("", selectedRange: NSRange(location: 0, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        try await Task.sleep(for: .milliseconds(50))
        guard !view.hasMarkedText(), try Data(contentsOf: file).isEmpty else {
            throw SmokeFailure.failed("Cancelled composition sent text to the process")
        }
        view.setMarkedText("ab", selectedRange: NSRange(location: 2, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        view.setMarkedText("界😀", selectedRange: NSRange(location: 1, length: 2), replacementRange: NSRange(location: 1, length: 1))
        var actual = NSRange(location: NSNotFound, length: 0)
        guard view.hasMarkedText(), view.markedRange() == NSRange(location: 0, length: 4),
              view.selectedRange() == NSRange(location: 2, length: 2),
              view.attributedSubstring(forProposedRange: view.markedRange(), actualRange: &actual)?.string == "a界😀",
              actual == view.markedRange() else { throw SmokeFailure.failed("Marked text replacement or UTF-16 selection is incorrect") }
        let first = view.firstRect(forCharacterRange: NSRange(location: 0, length: 1), actualRange: nil)
        let next = view.firstRect(forCharacterRange: NSRange(location: 1, length: 1), actualRange: nil)
        guard next.minX > first.minX, next.height > 0 else { throw SmokeFailure.failed("Composition candidates do not follow the marked text") }
        try await Task.sleep(for: .milliseconds(100))
        guard try Data(contentsOf: file).isEmpty else { throw SmokeFailure.failed("Uncommitted text reached the process") }
        view.insertText("é", replacementRange: NSRange(location: 0, length: 1))
        _ = try await wait(controller) { text($0).contains("IME-DONE") }
        guard !view.hasMarkedText(), try Data(contentsOf: file) == Data(committed.utf8) else {
            throw SmokeFailure.failed("Committed composition changed on its way to the PTY")
        }
    }
}
