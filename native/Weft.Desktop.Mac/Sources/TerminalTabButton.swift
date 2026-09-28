import AppKit
import CoreText

/// Draws a restrained tab background while preserving the full native button hit target.
@MainActor
final class TerminalTabButton: NSButton {
    var selected = false { didSet { if selected != oldValue { needsDisplay = true } } }
    var close: (() -> Void)?
    private var hovered = false
    private let closeButton = NSButton(image: NSImage(systemSymbolName: "xmark", accessibilityDescription: "Close Tab")!, target: nil, action: nil)
    override var isEnabled: Bool {
        didSet {
            closeButton.isEnabled = isEnabled
            closeButton.isHidden = !hovered || !isEnabled
        }
    }

    override init(frame: NSRect) {
        super.init(frame: frame)
        closeButton.target = self
        closeButton.action = #selector(closeTab)
        closeButton.isBordered = false
        closeButton.imageScaling = .scaleProportionallyDown
        closeButton.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 10, weight: .medium)
        closeButton.isHidden = true
        closeButton.toolTip = "Close Tab"
        addSubview(closeButton)
    }

    required init?(coder: NSCoder) { fatalError("Use init(frame:)") }

    override func layout() {
        super.layout()
        closeButton.frame = NSRect(x: max(0, bounds.maxX - 28), y: (bounds.height - 22) / 2, width: 22, height: 22)
    }

    @objc private func closeTab() { close?() }

    override func accessibilityCustomActions() -> [NSAccessibilityCustomAction]? {
        [NSAccessibilityCustomAction(name: "Close Tab") { [weak self] in
            self?.close?()
            return self != nil
        }]
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self))
    }

    override func mouseEntered(with event: NSEvent) {
        hovered = true
        closeButton.isHidden = !isEnabled
        needsDisplay = true
    }
    override func mouseExited(with event: NSEvent) {
        hovered = false
        closeButton.isHidden = true
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        let outline = NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 6, yRadius: 6)
        NSColor.white.withAlphaComponent(selected ? 0.12 : hovered ? 0.07 : 0.035).setFill()
        outline.fill()
        NSColor.white.withAlphaComponent(selected ? 0.16 : 0.09).setStroke()
        outline.lineWidth = 1 / (window?.backingScaleFactor ?? 2)
        outline.stroke()

        let color = isEnabled ? (contentTintColor ?? .labelColor) : .disabledControlTextColor
        if let icon = image?.withSymbolConfiguration(.init(paletteColors: [color])) {
            let scale = min(16 / icon.size.width, 16 / icon.size.height)
            let size = NSSize(width: icon.size.width * scale, height: icon.size.height * scale)
            icon.draw(in: NSRect(x: 11, y: bounds.midY - size.height / 2, width: size.width, height: size.height),
                      from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        }
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let attributes: [NSAttributedString.Key: Any] = [.font: font ?? NSFont.systemFont(ofSize: 12), .foregroundColor: color]
        let titleLine = CTLineCreateWithAttributedString(NSAttributedString(string: title, attributes: attributes))
        let ellipsis = CTLineCreateWithAttributedString(NSAttributedString(string: "…", attributes: attributes))
        let line = CTLineCreateTruncatedLine(titleLine, max(0, bounds.width - 66), .middle, ellipsis) ?? titleLine
        // Center visible glyphs, without the bezel offsets used by NSButtonCell.
        let ink = CTLineGetBoundsWithOptions(line, .useGlyphPathBounds)
        context.saveGState()
        if isFlipped {
            context.translateBy(x: 0, y: bounds.height)
            context.scaleBy(x: 1, y: -1)
        }
        context.clip(to: CGRect(x: 35, y: 0, width: max(0, bounds.width - 66), height: bounds.height))
        context.textMatrix = .identity
        context.textPosition = CGPoint(x: 35, y: bounds.midY - ink.midY)
        CTLineDraw(line, context)
        context.restoreGState()
    }
}
