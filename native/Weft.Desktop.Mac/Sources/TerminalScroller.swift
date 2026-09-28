import AppKit

/// Marks native thumb tracking so arriving frames leave pointer control with AppKit.
@MainActor
final class TerminalScroller: NSScroller {
    private(set) var trackingKnob = false
    var onInteraction: (() -> Void)?

    override class var isCompatibleWithOverlayScrollers: Bool { true }

    override func mouseDown(with event: NSEvent) {
        onInteraction?()
        super.mouseDown(with: event)
    }

    /// Keeps delayed terminal frames from moving the thumb away from the pointer during a drag.
    override func trackKnob(with event: NSEvent) {
        trackingKnob = true
        defer { trackingKnob = false }
        super.trackKnob(with: event)
    }
}
