import AppKit
import CoreText

/// Resolves the user's monospaced font while retaining bundled terminal symbols as fallback.
@MainActor
enum TerminalFont {
    /// Registers bundled fonts for this process only, without changing the user's installed fonts.
    static func register(in directory: URL? = Bundle.main.resourceURL?.appendingPathComponent("Fonts")) {
        guard let directory else { return }
        for name in ["CascadiaMonoNF.ttf", "CascadiaMonoNFItalic.ttf"] {
            CTFontManagerRegisterFontsForURL(directory.appendingPathComponent(name) as CFURL, .process, nil)
        }
    }

    static func preferred() -> NSFont {
        let defaults = UserDefaults.standard
        let size = defaults.double(forKey: "terminalFontSize")
        let name = defaults.string(forKey: "terminalFontName") ?? "CascadiaMonoNF-Regular"
        return withFallback(NSFont(name: name, size: size == 0 ? 14 : size)
                            ?? NSFont.monospacedSystemFont(ofSize: 14, weight: .regular))
    }

    static func withFallback(_ font: NSFont) -> NSFont {
        let descriptor = font.fontDescriptor.addingAttributes([
            .cascadeList: [NSFontDescriptor(name: "CascadiaMonoNF-Regular", size: font.pointSize)]
        ])
        return NSFont(descriptor: descriptor, size: font.pointSize) ?? font
    }

    static func save(_ font: NSFont) {
        UserDefaults.standard.set(font.fontName, forKey: "terminalFontName")
        UserDefaults.standard.set(font.pointSize, forKey: "terminalFontSize")
    }
}
