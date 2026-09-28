import AppKit

/// Records one Command-based shortcut while focused, then restores ordinary key routing.
@MainActor
final class ShortcutRecorder: NSButton {
    var recorded: ((String) -> Void)?
    private var recording = false
    override var acceptsFirstResponder: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        title = "Record Shortcut"
        bezelStyle = .rounded
        target = self
        action = #selector(beginRecording)
    }

    required init?(coder: NSCoder) { fatalError("Use init(frame:)") }

    @objc private func beginRecording() {
        recording = true
        title = "Press shortcut…"
        window?.makeFirstResponder(self)
    }

    override func resignFirstResponder() -> Bool {
        recording = false
        title = "Record Shortcut"
        return super.resignFirstResponder()
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard recording else { return super.performKeyEquivalent(with: event) }
        keyDown(with: event)
        return true
    }

    override func keyDown(with event: NSEvent) {
        guard recording else { super.keyDown(with: event); return }
        if event.keyCode == 53 { window?.makeFirstResponder(nil); return }
        guard event.modifierFlags.contains(.command),
              let key = event.characters(byApplyingModifiers: [])?.lowercased(), key.count == 1,
              key.unicodeScalars.allSatisfy({ $0.value >= 32 && $0.value < 0xf700 }) else { NSSound.beep(); return }
        var value = "cmd+"
        if event.modifierFlags.contains(.control) { value += "control+" }
        if event.modifierFlags.contains(.option) { value += "option+" }
        if event.modifierFlags.contains(.shift) { value += "shift+" }
        recorded?(value + key)
        window?.makeFirstResponder(nil)
    }
}
