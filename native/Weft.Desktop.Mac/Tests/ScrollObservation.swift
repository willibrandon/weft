import AppKit

/// Records what the native tracking loop displayed before the pointer was released.
@MainActor
final class ScrollObservation {
    var updatedWhileDragging = false
    var thumbValue = 0.0
    let drag: NSEvent
    let release: NSEvent

    init(drag: NSEvent, release: NSEvent) {
        self.drag = drag
        self.release = release
    }
}
