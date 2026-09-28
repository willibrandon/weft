import AppKit

/// Edits app appearance and command shortcuts without changing shell configuration.
@MainActor
final class SettingsWindow: NSWindowController {
    private let actions: [DesktopAction]
    private let changed: () -> Void
    private let command = NSPopUpButton()
    private let shortcut = NSTextField(labelWithString: "")
    private let message = NSTextField(wrappingLabelWithString: "")

    init(actions: [DesktopAction], changed: @escaping () -> Void) {
        self.actions = actions
        self.changed = changed
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 360),
                              styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "Weft Settings"
        window.isReleasedWhenClosed = false
        window.center()
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.edgeInsets = NSEdgeInsets(top: 22, left: 24, bottom: 22, right: 24)
        stack.frame = window.contentView!.bounds
        stack.autoresizingMask = [.width, .height]
        window.contentView = stack
        for (name, key, color) in [("Background", "terminalBackground", TerminalAppearance.background),
                                   ("Text", "terminalForeground", TerminalAppearance.foreground),
                                   ("Cursor", "terminalCursor", TerminalAppearance.cursor)] {
            let well = NSColorWell()
            well.color = color
            well.identifier = .init(key)
            well.target = self
            well.action = #selector(changeColor(_:))
            well.setAccessibilityLabel(name)
            well.widthAnchor.constraint(equalToConstant: 60).isActive = true
            stack.addArrangedSubview(NSStackView(views: [NSTextField(labelWithString: name), well]))
        }
        stack.addArrangedSubview(NSTextField(labelWithString: "Choose a terminal font in View → Choose Font."))
        command.addItems(withTitles: actions.map(\.label))
        command.target = self
        command.action = #selector(selectedCommand)
        command.setAccessibilityLabel("Command to customize")
        stack.addArrangedSubview(NSStackView(views: [command, shortcut]))
        let recorder = ShortcutRecorder(frame: .zero)
        recorder.recorded = { [weak self] value in self?.saveShortcut(value) }
        let clear = NSButton(title: "Remove Shortcut", target: self, action: #selector(clearShortcut))
        let restore = NSButton(title: "Restore Defaults", target: self, action: #selector(restoreShortcuts))
        stack.addArrangedSubview(NSStackView(views: [recorder, clear, restore]))
        message.font = .systemFont(ofSize: 11)
        message.textColor = .secondaryLabelColor
        stack.addArrangedSubview(message)
        selectedCommand()
    }

    required init?(coder: NSCoder) { fatalError("Use init(actions:changed:)") }

    @objc private func selectedCommand() {
        guard actions.indices.contains(command.indexOfSelectedItem) else { return }
        shortcut.stringValue = MacShortcuts.display(actions[command.indexOfSelectedItem].id)
        if shortcut.stringValue.isEmpty { shortcut.stringValue = "No shortcut" }
        message.stringValue = "Shortcuts use Command so terminal control keys remain available."
    }

    private func saveShortcut(_ value: String) {
        guard actions.indices.contains(command.indexOfSelectedItem) else { return }
        if let error = MacShortcuts.save(value, for: actions[command.indexOfSelectedItem].id, actions: actions) {
            message.stringValue = error
            NSSound.beep()
        } else { selectedCommand(); changed() }
    }
    @objc private func clearShortcut() { saveShortcut("") }
    @objc private func restoreShortcuts() {
        UserDefaults.standard.removeObject(forKey: "commandShortcuts")
        selectedCommand()
        changed()
    }
    @objc private func changeColor(_ sender: NSColorWell) {
        guard let key = sender.identifier?.rawValue, let color = sender.color.usingColorSpace(.sRGB) else { return }
        let value = String(format: "#%02x%02x%02x", Int(color.redComponent * 255), Int(color.greenComponent * 255), Int(color.blueComponent * 255))
        UserDefaults.standard.set(value, forKey: key)
        changed()
    }
}
