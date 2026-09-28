import AppKit

/// Lets AppKit manage scrollbar appearance, geometry, and tracking for a virtual terminal viewport.
/// Cell drawing and terminal input remain in the parent view; only scrollbar hits are intercepted.
@MainActor
final class TerminalScrollView: NSScrollView {
    var onScroll: ((Int) -> Void)?
    private let document = TerminalScrollDocument()
    private let thumb = TerminalScroller(frame: NSRect(x: 0, y: 0,
        width: NSScroller.scrollerWidth(for: .regular, scrollerStyle: NSScroller.preferredScrollerStyle), height: 100))
    private var applyingFrame = false
    private var rowHeight: CGFloat = 1
    private var historyLines = 0
    private var viewportRows = 1
    private var requestedOffset: Int?
    private var displayedOffset = 0
    private var followingFrame = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        drawsBackground = false
        contentView.drawsBackground = false
        hasVerticalScroller = true
        hasHorizontalScroller = false
        autohidesScrollers = true
        automaticallyAdjustsContentInsets = false
        scrollerKnobStyle = .light
        verticalScrollElasticity = .none
        horizontalScrollElasticity = .none
        verticalScroller = thumb
        thumb.target = self
        thumb.action = #selector(navigateHistory)
        thumb.onInteraction = { [weak self] in self?.followingFrame = false }
        documentView = document
        thumb.setAccessibilityLabel("Terminal history")
        contentView.postsBoundsChangedNotifications = true
        NotificationCenter.default.addObserver(self, selector: #selector(scrolled),
            name: NSView.boundsDidChangeNotification, object: contentView)
    }

    required init?(coder: NSCoder) { fatalError("Use init(frame:)") }

    /// Mirrors the retained row count and position without undoing an unacknowledged pointer movement.
    func update(_ block: DesktopBlock, rect: NSRect, rowHeight: CGFloat) {
        applyingFrame = true
        defer { applyingFrame = false }
        let reveal = displayedOffset != block.scrollOffset || (historyLines == 0 && block.historyLines > 0)
        displayedOffset = block.scrollOffset
        if block.scrollOffset == requestedOffset || historyLines != block.historyLines || self.rowHeight != rowHeight {
            requestedOffset = nil
        }
        self.rowHeight = rowHeight
        historyLines = block.historyLines
        viewportRows = block.height
        // Only the gutter scrolls. Moving a transparent clip view over the canvas
        // would invalidate terminal rows that have not changed.
        let width = min(rect.width, NSScroller.scrollerWidth(for: .regular, scrollerStyle: scrollerStyle) + 2)
        let gutter = NSRect(x: rect.maxX - width, y: rect.minY, width: width, height: rect.height)
        if frame != gutter { frame = gutter }
        isHidden = historyLines == 0 || block.alternateScreen
        verticalLineScroll = rowHeight
        verticalPageScroll = rowHeight
        guard !thumb.trackingKnob, requestedOffset == nil else { return }
        let extent = NSRect(x: 0, y: 0, width: contentSize.width,
                            height: CGFloat(historyLines + block.height) * rowHeight)
        if document.frame != extent { document.frame = extent }
        let position = NSPoint(x: 0, y: CGFloat(historyLines - block.scrollOffset) * rowHeight)
        if contentView.bounds.origin != position { contentView.scroll(to: position) }
        reflectScrolledClipView(contentView)
        if reveal { flashScrollers() }
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        guard let hit = super.hitTest(point), hit === thumb || hit.isDescendant(of: thumb) else { return nil }
        return hit
    }

    /// Explicit navigation takes ownership until the next gesture, including delayed page-animation callbacks.
    func resume() {
        applyingFrame = true
        followingFrame = true
        requestedOffset = nil
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0
            contentView.animator().setBoundsOrigin(NSPoint(x: 0, y: CGFloat(historyLines) * rowHeight))
        }
        reflectScrolledClipView(contentView)
        applyingFrame = false
    }

    override func scrollWheel(with event: NSEvent) {
        followingFrame = false
        super.scrollWheel(with: event)
    }

    /// Native thumb tracking supplies positions; page clicks step through the virtual document directly.
    /// An animated clip-view page transition must not outlive a later terminal navigation or drag.
    @objc private func navigateHistory(_ sender: NSScroller) {
        let current = requestedOffset ?? displayedOffset
        let offset: Int
        switch sender.hitPart {
        case .decrementPage: offset = min(historyLines, current + max(1, viewportRows - 1))
        case .incrementPage: offset = max(0, current - max(1, viewportRows - 1))
        case .decrementLine: offset = min(historyLines, current + 1)
        case .incrementLine: offset = max(0, current - 1)
        case .knob, .knobSlot: offset = min(historyLines, max(0, Int((Double(historyLines) * (1 - sender.doubleValue)).rounded())))
        default: return
        }
        followingFrame = false
        requestedOffset = offset
        applyingFrame = true
        contentView.scroll(to: NSPoint(x: 0, y: CGFloat(historyLines - offset) * rowHeight))
        reflectScrolledClipView(contentView)
        applyingFrame = false
        onScroll?(offset)
    }

    @objc private func scrolled() {
        guard !applyingFrame else { return }
        if followingFrame {
            applyingFrame = true
            contentView.scroll(to: NSPoint(x: 0, y: CGFloat(historyLines - displayedOffset) * rowHeight))
            reflectScrolledClipView(contentView)
            applyingFrame = false
            return
        }
        let offset = min(historyLines, max(0, historyLines - Int((contentView.bounds.minY / rowHeight).rounded())))
        guard requestedOffset != offset else { return }
        requestedOffset = offset
        onScroll?(offset)
    }
}
