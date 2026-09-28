import AppKit

/// Gives AppKit the history's extent without allocating or drawing offscreen terminal rows.
@MainActor
final class TerminalScrollDocument: NSView {
    override var isFlipped: Bool { true }
}
