import AppKit

/// Keeps session tabs reachable through native buttons and horizontal overflow scrolling.
@MainActor
final class TabStripView: NSView {
    var select: ((String) -> Void)?
    var close: ((String) -> Void)?
    private let scroll = NSScrollView()
    private let document = NSView()
    private var buttons: [TerminalTabButton] = []
    private var ids: [String] = []
    private var selectedId: String?

    override init(frame: NSRect) {
        super.init(frame: frame)
        clipsToBounds = true
        scroll.drawsBackground = false
        scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .overlay
        scroll.horizontalScrollElasticity = .none
        scroll.verticalScrollElasticity = .none
        scroll.documentView = document
        addSubview(scroll)
        setAccessibilityLabel("Tabs")
    }

    required init?(coder: NSCoder) { fatalError("Use init(frame:)") }

    func update(_ tabs: [DesktopTab], active: String?, enabled: Bool) {
        let nextIds = tabs.map(\.id)
        if ids != nextIds {
            buttons.forEach { $0.removeFromSuperview() }
            ids = nextIds
            buttons = tabs.enumerated().map { index, tab in
                let button = TerminalTabButton(frame: .zero)
                button.title = tab.name
                button.target = self
                button.action = #selector(choose(_:))
                button.close = { [weak self] in self?.close?(tab.id) }
                button.tag = index
                button.isBordered = false
                button.image = NSImage(systemSymbolName: "terminal", accessibilityDescription: nil)
                button.imagePosition = .imageLeft
                button.imageScaling = .scaleProportionallyDown
                button.font = .systemFont(ofSize: 12, weight: .medium)
                button.alignment = .left
                button.lineBreakMode = .byTruncatingMiddle
                button.focusRingType = .none
                document.addSubview(button)
                return button
            }
        }
        for (index, button) in buttons.enumerated() {
            button.title = tabs[index].name
            button.toolTip = tabs[index].name
            button.setAccessibilityLabel(tabs[index].name)
            button.selected = tabs[index].id == active
            button.setAccessibilityValue(button.selected ? "Selected" : "")
            button.contentTintColor = button.selected ? .labelColor : .secondaryLabelColor
            button.isEnabled = enabled
        }
        needsLayout = true
        layoutSubtreeIfNeeded()
        if selectedId != active, let index = ids.firstIndex(of: active ?? "") {
            document.scrollToVisible(buttons[index].frame)
        }
        selectedId = active
    }

    override func layout() {
        super.layout()
        scroll.frame = bounds
        let width = max(130, min(220, bounds.width / CGFloat(max(1, buttons.count))))
        document.frame = NSRect(x: 0, y: 0, width: max(bounds.width, CGFloat(buttons.count) * width), height: bounds.height)
        for (index, button) in buttons.enumerated() {
            button.frame = NSRect(x: CGFloat(index) * width, y: 1, width: width - 4, height: max(0, bounds.height - 2))
        }
    }

    @objc private func choose(_ sender: NSButton) {
        guard ids.indices.contains(sender.tag) else { return }
        select?(ids[sender.tag])
    }
}
