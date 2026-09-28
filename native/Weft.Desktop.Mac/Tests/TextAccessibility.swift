import AppKit

extension MacSmoke {
    /// Queries AppKit's text contract against Unicode received from the real terminal process.
    static func exerciseAccessibility(_ view: TerminalView) throws {
        guard view.isAccessibilityElement(), view.accessibilityRole() == .textArea,
              let value = view.accessibilityValue() as? String else { throw SmokeFailure.failed("Terminal has no accessible text element") }
        let text = value as NSString
        let wide = text.range(of: "界")
        let combining = text.range(of: "é")
        guard wide.location != NSNotFound, combining.location != NSNotFound,
              view.accessibilityNumberOfCharacters() == text.length,
              view.accessibilityString(for: combining) == "é",
              view.accessibilityRange(for: combining.location + 1) == combining,
              view.accessibilityLine(for: wide.location) == 0 else {
            throw SmokeFailure.failed("Accessible Unicode ranges do not match the terminal text")
        }
        let rect = view.accessibilityFrame(for: wide)
        guard abs(rect.width - view.cellWidth * 2) < 0.5,
              view.accessibilityRange(for: NSPoint(x: rect.maxX - view.cellWidth / 2, y: rect.midY)) == wide,
              view.accessibilityString(for: view.accessibilityRange(forLine: 0))?.contains("Native AppKit terminal") == true,
              view.accessibilityString(for: NSRange(location: text.length + 1, length: 1)) == nil else {
            throw SmokeFailure.failed("Accessible text geometry or line boundaries are incorrect")
        }
    }

    static func exerciseAccessibleSelection(_ controller: TerminalWindow) async throws {
        let view = controller.terminal
        guard let value = view.accessibilityValue() as? NSString,
              let cursor = view.frameData?.blocks.first?.cursorX else { throw SmokeFailure.failed("No accessible terminal") }
        let range = value.range(of: "界é")
        view.setAccessibilitySelectedTextRange(range)
        _ = try await wait(controller) { $0.blocks.first?.selection?.text == "界é" }
        guard view.accessibilitySelectedTextRange() == range, view.frameData?.blocks.first?.cursorX == cursor,
              !view.isAccessibilitySelectorAllowed(#selector(NSView.setAccessibilityValue(_:))) else {
            throw SmokeFailure.failed("Accessible selection moved the caret or allowed output editing")
        }
        view.setAccessibilitySelectedTextRange(NSRange(location: 0, length: 0))
        _ = try await wait(controller) { $0.blocks.first?.selection == nil }
        view.resume()
    }
}
