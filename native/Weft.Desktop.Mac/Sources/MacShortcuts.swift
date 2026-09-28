import AppKit

/// Applies macOS defaults and user overrides to actions from the shared catalog.
struct MacShortcuts {
    static let defaults: [String: String] = [
        "newTab": "cmd+t", "commands": "cmd+shift+p", "find": "cmd+f",
        "splitRight": "cmd+d", "splitBelow": "cmd+shift+d",
        "previousTab": "cmd+shift+[", "nextTab": "cmd+shift+]"
    ]
    private static let reserved: Set<String> = ["cmd+q", "cmd+w", "cmd+n", "cmd+h", "cmd+m", "cmd+,",
                                               "cmd+c", "cmd+v", "cmd+a", "cmd++", "cmd+-"]

    static func value(_ id: String) -> String {
        let custom = UserDefaults.standard.dictionary(forKey: "commandShortcuts") as? [String: String] ?? [:]
        return custom[id] ?? defaults[id] ?? ""
    }

    static func display(_ id: String) -> String {
        let text = value(id)
        return text.replacingOccurrences(of: "control+", with: "⌃").replacingOccurrences(of: "option+", with: "⌥")
            .replacingOccurrences(of: "cmd+", with: "⌘").replacingOccurrences(of: "shift+", with: "⇧").uppercased()
    }

    /// Rejects collisions before persisting a shortcut; an empty value explicitly disables it.
    static func save(_ value: String, for id: String, actions: [DesktopAction]) -> String? {
        if !value.isEmpty {
            guard value.hasPrefix("cmd+"), value.split(separator: "+").last?.count == 1 else { return "Include the Command key." }
            if reserved.contains(value) { return "That shortcut belongs to a standard Mac command." }
            if let conflict = actions.first(where: { $0.id != id && self.value($0.id) == value }) {
                return "That shortcut is already used by \(conflict.label)."
            }
        }
        var custom = UserDefaults.standard.dictionary(forKey: "commandShortcuts") as? [String: String] ?? [:]
        custom[id] = value
        UserDefaults.standard.set(custom, forKey: "commandShortcuts")
        return nil
    }

    /// Applies a `cmd+shift+p` style value; an empty override disables the shortcut.
    /// Command is required so ordinary terminal control keys remain available.
    static func apply(_ id: String, to item: NSMenuItem) {
        let text = value(id)
        guard !text.isEmpty else { return }
        let parts = text.lowercased().split(separator: "+").map(String.init)
        guard let key = parts.last, key.count == 1, parts.contains("cmd") else { return }
        var modifiers: NSEvent.ModifierFlags = [.command]
        if parts.contains("shift") { modifiers.insert(.shift) }
        if parts.contains("option") { modifiers.insert(.option) }
        if parts.contains("control") { modifiers.insert(.control) }
        item.keyEquivalent = key
        item.keyEquivalentModifierMask = modifiers
    }
}
