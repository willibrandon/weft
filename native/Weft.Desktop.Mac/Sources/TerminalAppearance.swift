import AppKit

/// Resolves app colors independently of shell prompt configuration.
@MainActor
enum TerminalAppearance {
    static var background: NSColor { color("terminalBackground", fallback: 0x13161a) }
    static var foreground: NSColor { color("terminalForeground", fallback: 0xe6e6e6) }
    static var cursor: NSColor { color("terminalCursor", fallback: 0x80a7c2) }

    private static func color(_ key: String, fallback: Int) -> NSColor {
        let text = UserDefaults.standard.string(forKey: key)?.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        let rgb = text.flatMap { $0.count == 6 ? Int($0, radix: 16) : nil } ?? fallback
        return NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255,
                       green: CGFloat((rgb >> 8) & 255) / 255, blue: CGFloat(rgb & 255) / 255, alpha: 1)
    }
}
