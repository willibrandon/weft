import AppKit
import CoreText

/// Draws terminal snapshots and translates native text, pointer, and clipboard input.
/// Terminal state and escape-sequence interpretation belong to the shared client core.
@MainActor
final class TerminalView: NSView, @MainActor NSTextInputClient {
    var send: ((DesktopCommand) -> Void)?
    var resized: ((Int, Int) -> Void)?
    var runAction: ((String, String) -> Void)?
    private(set) var frameData: DesktopFrame?
    private var font = TerminalFont.preferred()
    private var lastGrid = NSSize.zero
    /// A press only records an anchor; selection starts after deliberate pointer movement.
    private var selectionAnchor: (block: DesktopBlock, index: Int, point: NSPoint)?
    private var selectionDragged = false
    private var selectionBlock: String?
    private var selectionPoint: NSPoint?
    private var selectionScroll: Timer?
    private var pendingSelections: Set<String> = []
    private let images = TerminalImages()
    private var scrollers: [String: TerminalScrollView] = [:]
    private var wheelRemainders: [String: CGFloat] = [:]
    private var tracking: NSTrackingArea?
    private var pointerBlock: String?
    private var lastAccessibilityText = ""
    private var accessibleText: TerminalText?
    private var resizing: (block: String, edge: String, origin: NSPoint, sent: Int)?
    private var marked = NSAttributedString(string: "")
    private var markedSelection = NSRange(location: 0, length: 0)
    private var markedBlock: String?
    private var cursorTimer: Timer?
    private var cursorLit = true
    private var background = TerminalAppearance.background
    private var foreground = TerminalAppearance.foreground
    private var styledFonts: [Int: NSFont] = [:]
    private(set) var cellWidth: CGFloat = 1
    private(set) var cellHeight: CGFloat = 1

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override var isOpaque: Bool { true }
    var columns: Int { min(500, max(4, Int(bounds.width / cellWidth))) }
    var rows: Int { min(300, max(4, Int(bounds.height / cellHeight))) }
    private var activeBlock: DesktopBlock? { frameData?.blocks.first(where: { $0.active }) ?? frameData?.blocks.first }
    private var displayedBlocks: [DesktopBlock] { frameData?.blocks ?? [] }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        updateFontMetrics()
        wantsLayer = true
        layerContentsRedrawPolicy = .duringViewResize
        clipsToBounds = true
        setAccessibilityElement(true)
        setAccessibilityRole(.textArea)
        setAccessibilityLabel("Terminal")
    }

    required init?(coder: NSCoder) { fatalError("Use init(frame:)") }

    /// A closed window cannot be reused; release snapshots and drawable surfaces before AppKit releases the window.
    func releaseResources() {
        cursorTimer?.invalidate()
        cursorTimer = nil
        NotificationCenter.default.removeObserver(self)
        NSWorkspace.shared.notificationCenter.removeObserver(self)
        endSelectionGesture()
        send = nil
        resized = nil
        runAction = nil
        frameData = nil
        pendingSelections.removeAll()
        accessibleText = nil
        lastAccessibilityText = ""
        setAccessibilityValue("")
        setAccessibilitySelectedText("")
        images.removeAll()
        scrollers.values.forEach { $0.removeFromSuperview() }
        scrollers.removeAll()
        wheelRemainders.removeAll()
        marked = NSAttributedString(string: "")
        layer?.contents = nil
        wantsLayer = false
    }

    /// Applies a coalesced frame without changing the text covered by an existing selection.
    func update(_ frame: DesktopFrame) {
        let previous = frameData
        if hasMarkedText(), markedBlock != frame.blocks.first(where: { $0.active })?.id {
            unmarkText()
            inputContext?.discardMarkedText()
        }
        frameData = frame
        let oldCursor = previous?.blocks.first(where: \.active)
        let cursor = activeBlock
        updateCursorBlink(reset: oldCursor?.id != cursor?.id || oldCursor?.cursorX != cursor?.cursorX
            || oldCursor?.cursorY != cursor?.cursorY || oldCursor?.cursorShape != cursor?.cursorShape)
        if let selectionBlock, !frame.blocks.contains(where: { $0.id == selectionBlock }) { endSelectionGesture() }
        updateScrollers()
        invalidateChanges(from: previous, to: frame)
        let selectionText = selectedText()
        if accessibilitySelectedText() != selectionText { setAccessibilitySelectedText(selectionText) }
        accessibleText = activeBlock.map(TerminalText.init)
        let text = accessibleText?.value ?? ""
        if text != lastAccessibilityText {
            lastAccessibilityText = text
            setAccessibilityValue(text)
            NSAccessibility.post(element: self, notification: .valueChanged)
        }
        if previous?.blocks.first(where: \.active)?.selection != activeBlock?.selection {
            NSAccessibility.post(element: self, notification: .selectedTextChanged)
        }
    }

    /// Preserves unchanged rows in the backing layer, including when only the prompt or caret changes.
    private func invalidateChanges(from previous: DesktopFrame?, to frame: DesktopFrame) {
        guard let previous, previous.blocks.count == frame.blocks.count else {
            needsDisplay = true
            return
        }
        for (old, block) in zip(previous.blocks, frame.blocks) {
            guard old.id == block.id, old.x == block.x, old.y == block.y,
                  old.width == block.width, old.height == block.height,
                  old.title == block.title, old.active == block.active, old.viewVersion == block.viewVersion,
                  old.searchQuery == block.searchQuery, old.images == block.images, old.selection == block.selection else {
                needsDisplay = true
                return
            }
            let rect = contentRect(block)
            for row in 0..<block.height {
                let start = row * block.width
                let range = start..<(start + block.width)
                if !old.cells[range].elementsEqual(block.cells[range]) {
                    setNeedsDisplay(NSRect(x: rect.minX, y: rect.minY + CGFloat(row) * cellHeight,
                                           width: rect.width, height: cellHeight))
                }
            }
            if old.cursorX != block.cursorX || old.cursorY != block.cursorY
                || old.cursorVisible != block.cursorVisible || old.cursorShape != block.cursorShape {
                for value in [old, block] where value.active && value.cursorVisible {
                    setNeedsDisplay(NSRect(x: rect.minX + CGFloat(value.cursorX) * cellWidth,
                                           y: rect.minY + CGFloat(value.cursorY) * cellHeight,
                                           width: cellWidth, height: cellHeight))
                }
            }
        }
    }

    override func layout() {
        super.layout()
        let grid = NSSize(width: columns, height: rows)
        if grid != lastGrid {
            lastGrid = grid
            resized?(columns, rows)
        }
        if hasMarkedText() { inputContext?.invalidateCharacterCoordinates() }
    }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        needsDisplay = true
        if hasMarkedText() { inputContext?.invalidateCharacterCoordinates() }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NotificationCenter.default.removeObserver(self)
        NSWorkspace.shared.notificationCenter.removeObserver(self)
        if let window {
            NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(cursorFocusChanged),
                name: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification, object: nil)
            for name in [NSWindow.didBecomeKeyNotification, NSWindow.didResignKeyNotification,
                         NSWindow.didChangeOcclusionStateNotification] {
                NotificationCenter.default.addObserver(self, selector: #selector(cursorFocusChanged), name: name, object: window)
            }
        }
        updateCursorBlink(reset: true)
    }

    override func becomeFirstResponder() -> Bool {
        let accepted = super.becomeFirstResponder()
        if accepted { updateCursorBlink(reset: true, focused: true) }
        return accepted
    }

    override func resignFirstResponder() -> Bool {
        let accepted = super.resignFirstResponder()
        if accepted { updateCursorBlink(reset: true, focused: false) }
        return accepted
    }

    @objc private func cursorFocusChanged(_ notification: Notification) {
        updateCursorBlink(reset: true)
    }

    /// Blink only the focused, visible caret; output repainting does not restart its clock.
    /// DECSCUSR's even styles request a steady cursor, and composition remains continuously visible.
    private func updateCursorBlink(reset: Bool, focused: Bool? = nil) {
        let cursor = activeBlock
        let blinking = cursor?.cursorVisible == true && cursor?.selection == nil
            && [0, 1, 3, 5].contains(cursor?.cursorShape ?? 0) && !hasMarkedText()
            && window?.isKeyWindow == true && window?.occlusionState.contains(.visible) == true
            && (focused ?? (window?.firstResponder === self))
            && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        if reset || !blinking {
            cursorTimer?.invalidate()
            cursorTimer = nil
            if !cursorLit { cursorLit = true; invalidateCursor() }
        }
        guard blinking, cursorTimer == nil else { return }
        let timer = Timer(timeInterval: 0.6, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.cursorLit.toggle()
                self.invalidateCursor()
            }
        }
        timer.tolerance = 0.05
        cursorTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func invalidateCursor() {
        guard let block = activeBlock else { return }
        let rect = contentRect(block)
        setNeedsDisplay(NSRect(x: rect.minX + CGFloat(block.cursorX) * cellWidth,
                               y: rect.minY + CGFloat(block.cursorY) * cellHeight,
                               width: cellWidth, height: cellHeight))
    }

    func changeFontSize(by amount: CGFloat) {
        setFont(NSFontManager.shared.convert(font, toSize: min(32, max(9, font.pointSize + amount))))
    }

    func refreshSettings() {
        background = TerminalAppearance.background
        foreground = TerminalAppearance.foreground
        needsDisplay = true
    }

    @objc func showFonts(_ sender: Any?) {
        NSFontManager.shared.setSelectedFont(font, isMultiple: false)
        NSFontManager.shared.orderFrontFontPanel(sender)
    }

    @objc func changeFont(_ sender: Any?) {
        let candidate = NSFontManager.shared.convert(font)
        let narrow = ("i" as NSString).size(withAttributes: [.font: candidate]).width
        let wide = ("M" as NSString).size(withAttributes: [.font: candidate]).width
        guard abs(narrow - wide) < 0.01 else { NSSound.beep(); return }
        setFont(candidate)
    }

    private func setFont(_ value: NSFont) {
        font = TerminalFont.withFallback(value)
        updateFontMetrics()
        styledFonts.removeAll()
        TerminalFont.save(font)
        needsLayout = true
        needsDisplay = true
    }

    private func updateFontMetrics() {
        cellWidth = ceil(("M" as NSString).size(withAttributes: [.font: font]).width)
        cellHeight = ceil(font.ascender - font.descender + font.leading) + 3
    }

    override func draw(_ dirtyRect: NSRect) {
        background.setFill()
        dirtyRect.fill()
        for block in displayedBlocks {
            let rect = contentRect(block)
            let dirtyRows = Set((0..<block.height).filter {
                needsToDraw(NSRect(x: rect.minX, y: rect.minY + CGFloat($0) * cellHeight,
                                   width: rect.width, height: cellHeight))
            })
            let matches = searchCells(block)
            NSGraphicsContext.saveGraphicsState()
            rect.intersection(bounds).clip()
            for (index, cell) in block.cells.enumerated() where dirtyRows.contains(index / block.width) {
                let reverse = cell.attributes & 32 != 0
                let selected = isSelected(block, index)
                let matched = matches.contains(index)
                // The canvas already contains the default background.
                guard reverse || cell.background != nil || selected || matched else { continue }
                let x = index % block.width
                let y = index / block.width
                let cellRect = NSRect(x: rect.minX + CGFloat(x) * cellWidth,
                                      y: rect.minY + CGFloat(y) * cellHeight,
                                      width: cellWidth, height: cellHeight)
                let bg = color(reverse ? cell.foreground : cell.background, fallback: reverse ? foreground : background)
                (selected ? NSColor.selectedTextBackgroundColor
                    : matched ? NSColor.systemYellow.withAlphaComponent(0.35) : bg).setFill()
                cellRect.fill()
            }
            drawImages(block, behindText: true, rect: rect)
            for (index, cell) in block.cells.enumerated() where !cell.text.isEmpty && cell.attributes & 64 == 0
                && dirtyRows.contains(index / block.width)
                && cell.text.unicodeScalars.first?.value != 0x10eeee
                && (cell.text != " " || cell.attributes & 136 != 0) {
                let reverse = cell.attributes & 32 != 0
                var fg = color(reverse ? cell.background : cell.foreground, fallback: reverse ? background : foreground)
                if cell.attributes & 2 != 0 { fg = fg.withAlphaComponent(0.55) }
                let drawingFont = styledFont(cell.attributes)
                var attributes: [NSAttributedString.Key: Any] = [
                    .font: drawingFont, .foregroundColor: fg
                ]
                if cell.attributes & 8 != 0 { attributes[.underlineStyle] = NSUnderlineStyle.single.rawValue }
                if cell.attributes & 128 != 0 { attributes[.strikethroughStyle] = NSUnderlineStyle.single.rawValue }
                let point = NSPoint(x: rect.minX + CGFloat(index % block.width) * cellWidth,
                                    y: rect.minY + CGFloat(index / block.width) * cellHeight + 1)
                let wide = index % block.width < block.width - 1 && block.cells[index + 1].text.isEmpty
                NSGraphicsContext.saveGraphicsState()
                NSRect(x: point.x, y: point.y - 1, width: cellWidth * (wide ? 2 : 1), height: cellHeight).clip()
                if !drawSymbol(cell.text, font: drawingFont, color: fg, point: point) {
                    (cell.text as NSString).draw(at: point, withAttributes: attributes)
                }
                NSGraphicsContext.restoreGraphicsState()
            }
            drawImages(block, behindText: false, rect: rect)
            if block.active && block.cursorVisible && block.selection == nil && cursorLit {
                let cursor = NSRect(x: rect.minX + CGFloat(block.cursorX) * cellWidth,
                                    y: rect.minY + CGFloat(block.cursorY) * cellHeight, width: cellWidth, height: cellHeight)
                TerminalAppearance.cursor.setFill()
                if block.cursorShape == 3 || block.cursorShape == 4 {
                    NSRect(x: cursor.minX, y: cursor.maxY - 2, width: cellWidth, height: 2).fill()
                } else if block.cursorShape == 5 || block.cursorShape == 6 {
                    NSRect(x: cursor.minX, y: cursor.minY, width: 2, height: cellHeight).fill()
                } else {
                    TerminalAppearance.cursor.withAlphaComponent(0.6).setFill()
                    cursor.fill()
                }
                if hasMarkedText() {
                    marked.draw(at: cursor.origin)
                }
            }
            NSGraphicsContext.restoreGraphicsState()
            drawChrome(block, rect: rect)
        }
    }

    private func drawImages(_ block: DesktopBlock, behindText: Bool, rect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        let textures = Dictionary(uniqueKeysWithValues: block.textures.map { ($0.cacheKey, $0) })
        for placement in block.images where (placement.layer < 0) == behindText {
            guard let texture = textures[placement.textureKey], let image = images.image(texture) else { continue }
            context.saveGState()
            context.clip(to: NSRect(x: rect.minX + placement.clipX * cellWidth, y: rect.minY + placement.clipY * cellHeight,
                                   width: placement.clipWidth * cellWidth, height: placement.clipHeight * cellHeight))
            let destination = NSRect(x: rect.minX + placement.x * cellWidth, y: rect.minY + placement.y * cellHeight,
                                     width: placement.width * cellWidth, height: placement.height * cellHeight)
            // Core Graphics images use an upward axis; the terminal view uses downward rows.
            context.translateBy(x: destination.minX, y: destination.maxY)
            context.scaleBy(x: 1, y: -1)
            context.interpolationQuality = .none
            context.draw(image, in: NSRect(origin: .zero, size: destination.size))
            context.restoreGState()
        }
    }

    private func styledFont(_ attributes: Int) -> NSFont {
        let key = attributes & 5
        if let cached = styledFonts[key] { return cached }
        var traits: NSFontTraitMask = []
        if key & 1 != 0 { traits.insert(.boldFontMask) }
        if key & 4 != 0 { traits.insert(.italicFontMask) }
        let result = key == 0 ? font : NSFontManager.shared.convert(font, toHaveTrait: traits)
        styledFonts[key] = result
        return result
    }

    private func drawSymbol(_ text: String, font: NSFont, color: NSColor, point: NSPoint) -> Bool {
        guard text.unicodeScalars.count == 1, let scalar = text.unicodeScalars.first,
              scalar.properties.generalCategory == .privateUse,
              let context = NSGraphicsContext.current?.cgContext else { return false }
        let resolved = CTFontCreateForString(font, text as CFString, CFRange(location: 0, length: text.utf16.count))
        var characters = Array(text.utf16)
        var glyphs = [CGGlyph](repeating: 0, count: characters.count)
        guard CTFontGetGlyphsForCharacters(resolved, &characters, &glyphs, characters.count),
              let glyph = glyphs.first, glyph != 0,
              let path = CTFontCreatePathForGlyph(resolved, glyph, nil) else { return false }
        context.saveGState()
        context.translateBy(x: point.x, y: point.y + font.ascender)
        context.scaleBy(x: 1, y: -1)
        context.setFillColor(color.cgColor)
        context.addPath(path)
        // Preserve counters in symbol outlines whose nested contours share a winding direction.
        context.drawPath(using: .eoFill)
        context.restoreGState()
        return true
    }

    private func drawChrome(_ block: DesktopBlock, rect: NSRect) {
        let pane = NSRect(x: rect.minX - 5, y: rect.minY - cellHeight + 3,
                          width: rect.width + 10, height: rect.height + cellHeight + 1)
        NSColor.white.withAlphaComponent(block.active ? 0.16 : 0.07).setStroke()
        let border = NSBezierPath(roundedRect: pane, xRadius: 6, yRadius: 6)
        border.lineWidth = 1
        border.stroke()
        let style = NSMutableParagraphStyle()
        style.lineBreakMode = .byTruncatingMiddle
        let inspecting = block.viewVersion != 0
        let titleRect = NSRect(x: rect.minX + 5, y: pane.minY + 3,
                              width: max(0, rect.width - (inspecting ? 145 : 10)), height: cellHeight - 4)
        (block.title as NSString).draw(in: titleRect, withAttributes: [
            .font: NSFont.systemFont(ofSize: 10),
            .foregroundColor: NSColor.secondaryLabelColor, .paragraphStyle: style
        ])
        if inspecting {
            let label = block.selection != nil ? "Selection · Resume ↓" : "History · Resume ↓"
            (label as NSString).draw(in: NSRect(x: rect.maxX - 135, y: pane.minY + 3,
                                               width: 130, height: cellHeight - 4), withAttributes: [
                .font: NSFont.systemFont(ofSize: 10), .foregroundColor: NSColor.secondaryLabelColor
            ])
        }
    }

    private func contentRect(_ block: DesktopBlock) -> NSRect {
        NSRect(x: CGFloat(block.x) * cellWidth, y: CGFloat(block.y) * cellHeight,
               width: CGFloat(block.width) * cellWidth, height: CGFloat(block.height) * cellHeight)
    }

    private func color(_ rgb: Int?, fallback: NSColor) -> NSColor {
        guard let rgb else { return fallback }
        return NSColor(calibratedRed: CGFloat((rgb >> 16) & 255) / 255,
                       green: CGFloat((rgb >> 8) & 255) / 255, blue: CGFloat(rgb & 255) / 255, alpha: 1)
    }

    private func isSelected(_ block: DesktopBlock, _ index: Int) -> Bool {
        guard let selection = block.selection else { return false }
        return index >= selection.start && index <= selection.end
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        clearSelection()
        selectionDragged = false
        let point = convert(event.locationInWindow, from: nil)
        if let edge = resizeEdge(point) {
            resizing = (edge.block, edge.edge, point, 0)
            return
        }
        if let block = displayedBlocks.first(where: {
            let rect = contentRect($0)
            return NSRect(x: rect.maxX - 135, y: rect.minY - cellHeight, width: 135, height: cellHeight).contains(point)
        }) { resume(block.id); return }
        guard let block = displayedBlocks.reversed().first(where: { contentRect($0).contains(point) }) else { return }
        if !block.active { send?(DesktopCommand(operation: "focus", target: block.id)) }
        let index = cellIndex(point, in: block)
        if event.modifierFlags.contains(.command), let link = block.cells[index].link { openLink(link); return }
        if block.mouseTracking && block.scrollOffset == 0 && !event.modifierFlags.contains(.shift) {
            clearSelection()
            pointerBlock = block.id
            sendMouse(event, block: block, action: "down", button: 1)
            return
        }
        selectionDragged = event.clickCount > 1
        if event.clickCount == 1 {
            selectionAnchor = (block, index, point)
            return
        }
        beginSelection(block, index: index, kind: event.clickCount >= 3 ? "line" : "word")
        selectionPoint = point
    }

    override func mouseDragged(with event: NSEvent) {
        if let drag = resizing {
            let point = convert(event.locationInWindow, from: nil)
            let cells = Int((drag.edge == "right" ? point.x - drag.origin.x : point.y - drag.origin.y)
                / (drag.edge == "right" ? cellWidth : cellHeight))
            if cells != drag.sent {
                send?(DesktopCommand(operation: "resizePane", target: drag.block, text: drag.edge, x: cells - drag.sent))
                resizing?.sent = cells
            }
            return
        }
        if let pointerBlock, let block = frameData?.blocks.first(where: { $0.id == pointerBlock }) {
            sendMouse(event, block: block, action: "drag", button: 1)
            return
        }
        let point = convert(event.locationInWindow, from: nil)
        if let anchor = selectionAnchor {
            let distance = hypot(point.x - anchor.point.x, point.y - anchor.point.y)
            guard distance >= 3 else { return }
            beginSelection(anchor.block, index: anchor.index, kind: "cell")
            selectionAnchor = nil
        }
        guard let selectionBlock, let block = frameData?.blocks.first(where: { $0.id == selectionBlock }) else { return }
        selectionDragged = true
        selectionPoint = point
        extendSelection(block, point: point)
        let outside = point.y < contentRect(block).minY || point.y >= contentRect(block).maxY
        if outside && !block.alternateScreen && selectionScroll == nil {
            let timer = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.scrollSelection() }
            }
            selectionScroll = timer
            RunLoop.main.add(timer, forMode: .common)
        } else if !outside {
            selectionScroll?.invalidate()
            selectionScroll = nil
        }
    }

    override func mouseUp(with event: NSEvent) {
        resizing = nil
        endSelectionGesture()
        if let pointerBlock, let block = frameData?.blocks.first(where: { $0.id == pointerBlock }) {
            sendMouse(event, block: block, action: "up", button: 1)
        }
        pointerBlock = nil
        if !selectionDragged { clearSelection() }
    }

    private func beginSelection(_ block: DesktopBlock, index: Int, kind: String) {
        selectionBlock = block.id
        pendingSelections.insert(block.id)
        send?(DesktopCommand(operation: "select", target: block.id, text: kind, x: index % block.width, y: index / block.width))
    }

    private func extendSelection(_ block: DesktopBlock, point: NSPoint) {
        let index = cellIndex(point, in: block)
        send?(DesktopCommand(operation: "select", target: block.id, text: "extend", x: index % block.width, y: index / block.width))
    }

    /// Autoscroll runs only during an active drag and extends the selection in the same ordered command queue.
    private func scrollSelection() {
        guard let selectionBlock, let point = selectionPoint,
              let block = frameData?.blocks.first(where: { $0.id == selectionBlock }), !block.alternateScreen else {
            endSelectionGesture()
            return
        }
        let rect = contentRect(block)
        let distance = point.y < rect.minY ? rect.minY - point.y : rect.maxY - point.y
        let amount = distance > 0 ? min(4, max(1, Int(distance / cellHeight))) : max(-4, min(-1, Int(distance / cellHeight)))
        guard (amount > 0 && block.scrollOffset < block.historyLines) || (amount < 0 && block.scrollOffset > 0) else { return }
        send?(DesktopCommand(operation: "scroll", target: block.id, y: amount))
        extendSelection(block, point: point)
    }

    private func endSelectionGesture() {
        selectionScroll?.invalidate()
        selectionScroll = nil
        selectionAnchor = nil
        selectionBlock = nil
        selectionPoint = nil
    }

    override func viewWillMove(toWindow newWindow: NSWindow?) {
        if newWindow == nil { endSelectionGesture() }
        super.viewWillMove(toWindow: newWindow)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.activeInKeyWindow, .inVisibleRect, .mouseMoved], owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseMoved(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        if let edge = resizeEdge(point) {
            (edge.edge == "right" ? NSCursor.resizeLeftRight : NSCursor.resizeUpDown).set()
            return
        }
        NSCursor.iBeam.set()
        guard let block = frameData?.blocks.last(where: { contentRect($0).contains(point) }),
              block.mouseTracking, !event.modifierFlags.contains(.shift) else { return }
        sendMouse(event, block: block, action: "move", button: 0)
    }

    private func resizeEdge(_ point: NSPoint) -> (block: String, edge: String)? {
        let blocks = frameData?.blocks ?? []
        for left in blocks {
            let first = contentRect(left)
            for right in blocks where left.id != right.id {
                let second = contentRect(right)
                if abs(first.maxX - second.minX) <= cellWidth * 2.5 && first.minX < second.minX,
                   abs(point.x - (first.maxX + second.minX) / 2) < 5,
                   point.y >= max(first.minY, second.minY), point.y <= min(first.maxY, second.maxY) {
                    return (left.id, "right")
                }
                if abs(first.maxY - second.minY) <= cellHeight * 2.5 && first.minY < second.minY,
                   abs(point.y - (first.maxY + second.minY - cellHeight) / 2) < 5,
                   point.x >= max(first.minX, second.minX), point.x <= min(first.maxX, second.maxX) {
                    return (left.id, "down")
                }
            }
        }
        return nil
    }

    override func rightMouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard let block = displayedBlocks.last(where: { contentRect($0).contains(point) }) else { return }
        if block.mouseTracking && !event.modifierFlags.contains(.shift) {
            pointerBlock = block.id
            sendMouse(event, block: block, action: "down", button: 3)
            return
        }
        let menu = NSMenu()
        for (title, id) in [("Copy", "copy"), ("Paste", "paste"), ("Find…", "find"),
                            ("Split Right", "splitRight"), ("Split Below", "splitBelow"),
                            ("Rename Pane…", "renameBlock"), ("Close Pane…", "closeBlock")] {
            let item = NSMenuItem(title: title, action: #selector(contextAction(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = [id, block.id]
            if id == "copy" { item.isEnabled = !selectedText().isEmpty }
            menu.addItem(item)
        }
        menu.autoenablesItems = false
        NSMenu.popUpContextMenu(menu, with: event, for: self)
    }

    override func rightMouseUp(with event: NSEvent) {
        if let pointerBlock, let block = frameData?.blocks.first(where: { $0.id == pointerBlock }) {
            sendMouse(event, block: block, action: "up", button: 3)
        }
        pointerBlock = nil
    }

    @objc private func contextAction(_ item: NSMenuItem) {
        guard let action = item.representedObject as? [String], action.count == 2 else { return }
        if action[0] == "copy" { copy(nil) }
        else if action[0] == "paste" { paste(to: action[1]) }
        else { runAction?(action[0], action[1]) }
    }

    private func searchCells(_ block: DesktopBlock) -> Set<Int> {
        guard !block.searchQuery.isEmpty else { return [] }
        var result = Set<Int>()
        for row in 0..<block.height {
            let start = row * block.width
            let cells = block.cells[start..<min(start + block.width, block.cells.count)]
            let text = cells.map(\.text).joined() as NSString
            var range = NSRange(location: 0, length: text.length)
            while range.length > 0 {
                let match = text.range(of: block.searchQuery, options: .caseInsensitive, range: range)
                if match.location == NSNotFound { break }
                var offset = 0
                for (column, cell) in cells.enumerated() {
                    let length = cell.text.utf16.count
                    if offset < NSMaxRange(match) && offset + max(1, length) > match.location { result.insert(start + column) }
                    offset += length
                }
                range = NSRange(location: NSMaxRange(match), length: text.length - NSMaxRange(match))
            }
        }
        return result
    }

    /// Converts AppKit points to block-local cells; the core encodes the negotiated mouse protocol.
    private func sendMouse(_ event: NSEvent, block: DesktopBlock, action: String, button: Int) {
        let index = cellIndex(convert(event.locationInWindow, from: nil), in: block)
        let modifiers = (event.modifierFlags.contains(.shift) ? 1 : 0)
            | (event.modifierFlags.contains(.option) ? 2 : 0) | (event.modifierFlags.contains(.control) ? 4 : 0)
        send?(DesktopCommand(operation: "mouse", target: block.id, text: action,
                             x: index % block.width, y: index / block.width, button: button, modifiers: modifiers))
    }

    override func scrollWheel(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard let block = displayedBlocks.last(where: { contentRect($0).contains(point) }) else { return }
        let movement = event.scrollingDeltaY / (event.hasPreciseScrollingDeltas ? cellHeight : 1)
        let accumulated = (wheelRemainders[block.id] ?? 0) + movement
        let wholeLines = accumulated.rounded(.towardZero)
        let lines = Int(min(120, max(-120, wholeLines)))
        wheelRemainders[block.id] = accumulated - wholeLines
        guard lines != 0 else { return }
        if block.mouseTracking && block.scrollOffset == 0 && !event.modifierFlags.contains(.shift) {
            for _ in 0..<abs(lines) { sendMouse(event, block: block, action: "down", button: lines > 0 ? 4 : 5) }
        } else if block.alternateScreen {
            for _ in 0..<abs(lines) { send?(DesktopCommand(operation: "key", target: block.id, text: lines > 0 ? "Up" : "Down")) }
        } else {
            send?(DesktopCommand(operation: "scroll", target: block.id, y: lines))
        }
    }

    private func updateScrollers() {
        let blocks = frameData?.blocks ?? []
        for id in scrollers.keys.filter({ id in !blocks.contains(where: { $0.id == id }) }) {
            scrollers.removeValue(forKey: id)?.removeFromSuperview()
            wheelRemainders.removeValue(forKey: id)
        }
        for block in blocks {
            let scroller = scrollers[block.id] ?? TerminalScrollView(frame: contentRect(block))
            if scrollers[block.id] == nil {
                let id = block.id
                scroller.onScroll = { [weak self] offset in
                    self?.send?(DesktopCommand(operation: "scrollTo", target: id, y: offset))
                }
                addSubview(scroller)
                scrollers[block.id] = scroller
            }
            scroller.update(block, rect: contentRect(block), rowHeight: cellHeight)
        }
    }

    /// Returns a pane to live output and releases any retained selection.
    func resume(_ id: String? = nil) {
        clearSelection()
        if let target = id ?? activeBlock?.id { scrollers[target]?.resume() }
        send?(DesktopCommand(operation: "live", target: id ?? activeBlock?.id))
    }

    func clearSelection() {
        endSelectionGesture()
        for block in displayedBlocks where block.selection != nil || pendingSelections.contains(block.id) {
            send?(DesktopCommand(operation: "select", target: block.id, text: "clear"))
        }
        pendingSelections.removeAll()
    }

    private func openLink(_ text: String) {
        guard let url = URL(string: text), let scheme = url.scheme?.lowercased(),
              ["https", "http", "mailto"].contains(scheme) else { NSSound.beep(); return }
        NSWorkspace.shared.open(url)
    }

    private func cellIndex(_ point: NSPoint, in block: DesktopBlock) -> Int {
        let rect = contentRect(block)
        let x = min(block.width - 1, max(0, Int((point.x - rect.minX) / cellWidth)))
        let y = min(block.height - 1, max(0, Int((point.y - rect.minY) / cellHeight)))
        return y * block.width + x
    }

    @objc func copy(_ sender: Any?) {
        guard !selectedText().isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(selectedText(), forType: .string)
    }

    private func selectedText() -> String {
        displayedBlocks.first(where: { $0.selection != nil })?.selection?.text ?? ""
    }

    @objc func paste(_ sender: Any?) {
        paste(to: activeBlock?.id)
    }

    private func paste(to target: String?) {
        guard let text = NSPasteboard.general.string(forType: .string) else { return }
        resume(target)
        send?(DesktopCommand(operation: "paste", target: target, text: text))
    }

    override func selectAll(_ sender: Any?) {
        guard let block = activeBlock, !block.cells.isEmpty else { return }
        pendingSelections.insert(block.id)
        send?(DesktopCommand(operation: "select", target: block.id, text: "all"))
    }

    /// Preserves terminal control keys and delegates composed text and dead keys to AppKit.
    override func keyDown(with event: NSEvent) {
        updateCursorBlink(reset: true)
        if (activeBlock?.viewVersion ?? 0) != 0 || activeBlock.map({ pendingSelections.contains($0.id) }) == true { resume() }
        if hasMarkedText() { interpretKeyEvents([event]); return }
        let special: [UInt16: String] = [
            36: "Enter", 76: "Enter", 48: "Tab", 51: "BSpace", 53: "Escape",
            123: "Left", 124: "Right", 125: "Down", 126: "Up",
            115: "Home", 119: "End", 116: "PageUp", 121: "PageDown", 117: "Delete",
            122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6",
            98: "F7", 100: "F8", 101: "F9", 109: "F10", 103: "F11", 111: "F12"
        ]
        if let key = special[event.keyCode] {
            var prefix = ""
            if event.modifierFlags.contains(.control) { prefix += "C-" }
            if event.modifierFlags.contains(.option) { prefix += "M-" }
            if event.modifierFlags.contains(.shift) { prefix += "S-" }
            send?(DesktopCommand(operation: "key", target: activeBlock?.id, text: prefix + key))
        } else if event.modifierFlags.contains(.control), let text = event.characters {
            send?(DesktopCommand(operation: "text", target: activeBlock?.id, text: text))
        } else {
            interpretKeyEvents([event])
        }
    }

    func insertText(_ string: Any, replacementRange: NSRange) {
        updateCursorBlink(reset: true)
        let text = (string as? NSAttributedString)?.string ?? (string as? String ?? "")
        let committed = replacingMarkedText(with: text, range: replacementRange)
        unmarkText()
        send?(DesktopCommand(operation: "text", target: activeBlock?.id, text: committed))
    }

    override func doCommand(by selector: Selector) {
        let commands = ["insertNewline:": "Enter", "deleteBackward:": "BSpace",
                        "insertTab:": "Tab", "cancelOperation:": "Escape",
                        "moveLeft:": "Left", "moveRight:": "Right", "moveUp:": "Up", "moveDown:": "Down"]
        if let key = commands[NSStringFromSelector(selector)] {
            send?(DesktopCommand(operation: "key", target: activeBlock?.id, text: key))
        }
    }

    func setMarkedText(_ string: Any, selectedRange: NSRange, replacementRange: NSRange) {
        if !hasMarkedText() { markedBlock = activeBlock?.id }
        let text = (string as? NSAttributedString)?.string ?? (string as? String ?? "")
        let base = validMarkedRange(replacementRange) ? replacementRange.location : 0
        marked = NSAttributedString(string: replacingMarkedText(with: text, range: replacementRange), attributes: [.font: font, .foregroundColor: foreground,
                                                               .backgroundColor: background, .underlineStyle: 1])
        let location = min(text.utf16.count, max(0, selectedRange.location == NSNotFound ? 0 : selectedRange.location))
        markedSelection = NSRange(location: base + location, length: min(max(0, selectedRange.length), text.utf16.count - location))
        updateCursorBlink(reset: true)
        needsDisplay = true
    }

    /// Composition is local until committed; replacement offsets use UTF-16, as required by AppKit.
    private func replacingMarkedText(with text: String, range: NSRange) -> String {
        guard hasMarkedText(), validMarkedRange(range) else { return text }
        return (marked.string as NSString).replacingCharacters(in: range, with: text)
    }

    private func validMarkedRange(_ range: NSRange) -> Bool {
        range.location != NSNotFound && range.location >= 0 && range.length >= 0
            && range.location <= marked.length && range.length <= marked.length - range.location
    }

    func unmarkText() {
        guard hasMarkedText() else { return }
        marked = NSAttributedString(string: "")
        markedBlock = nil
        markedSelection = NSRange(location: 0, length: 0)
        updateCursorBlink(reset: true)
        needsDisplay = true
    }
    func hasMarkedText() -> Bool { marked.length > 0 }
    func markedRange() -> NSRange { hasMarkedText() ? NSRange(location: 0, length: marked.length) : NSRange(location: NSNotFound, length: 0) }
    func selectedRange() -> NSRange { hasMarkedText() ? markedSelection : NSRange(location: 0, length: 0) }
    func validAttributesForMarkedText() -> [NSAttributedString.Key] { [.underlineStyle, .foregroundColor, .backgroundColor] }
    func attributedSubstring(forProposedRange range: NSRange, actualRange: NSRangePointer?) -> NSAttributedString? {
        guard validMarkedRange(range) else { return nil }
        actualRange?.pointee = range
        return marked.attributedSubstring(from: range)
    }
    func characterIndex(for point: NSPoint) -> Int {
        guard hasMarkedText() else { return NSNotFound }
        let rect = firstRect(forCharacterRange: markedRange(), actualRange: nil)
        guard rect.contains(point) else { return NSNotFound }
        let relative = max(0, point.x - rect.minX)
        for index in 0..<marked.length {
            let prefix = marked.attributedSubstring(from: NSRange(location: 0, length: index + 1))
            if prefix.size().width > relative { return (marked.string as NSString).rangeOfComposedCharacterSequence(at: index).location }
        }
        return marked.length
    }
    func firstRect(forCharacterRange range: NSRange, actualRange: NSRangePointer?) -> NSRect {
        let range = validMarkedRange(range) ? range : NSRange(location: 0, length: 0)
        actualRange?.pointee = range
        guard let block = activeBlock, let window else { return .zero }
        let prefix = marked.attributedSubstring(from: NSRange(location: 0, length: range.location)).size().width
        let width = max(cellWidth, marked.attributedSubstring(from: range).size().width)
        let rect = NSRect(x: CGFloat(block.x + block.cursorX) * cellWidth + prefix,
                          y: CGFloat(block.y + block.cursorY) * cellHeight, width: width, height: cellHeight)
        return window.convertToScreen(convert(rect, to: nil))
    }

    override func accessibilityNumberOfCharacters() -> Int { accessibleText?.value.utf16.count ?? 0 }
    override func accessibilityVisibleCharacterRange() -> NSRange {
        NSRange(location: 0, length: accessibilityNumberOfCharacters())
    }
    override func accessibilityInsertionPointLineNumber() -> Int { activeBlock?.cursorY ?? 0 }
    override func accessibilitySelectedTextRange() -> NSRange {
        guard let text = accessibleText else { return NSRange(location: NSNotFound, length: 0) }
        guard let selection = activeBlock?.selection else { return text.cursor }
        return text.range(from: selection.start, through: selection.end)
    }
    override func accessibilitySelectedTextRanges() -> [NSValue]? {
        let range = accessibilitySelectedTextRange()
        return range.location == NSNotFound ? [] : [NSValue(range: range)]
    }
    /// Accessibility selection highlights terminal output without moving the process-owned cursor.
    override func setAccessibilitySelectedTextRange(_ range: NSRange) {
        guard let block = activeBlock, let text = accessibleText, text.substring(range) != nil else { return }
        if range.length == 0 { clearSelection(); return }
        guard let first = text.cells.firstIndex(where: { NSIntersectionRange($0, range).length > 0 }),
              let last = text.cells.lastIndex(where: { NSIntersectionRange($0, range).length > 0 }) else { return }
        pendingSelections.insert(block.id)
        send?(DesktopCommand(operation: "select", target: block.id, text: "cell", x: first % block.width, y: first / block.width))
        send?(DesktopCommand(operation: "select", target: block.id, text: "extend", x: last % block.width, y: last / block.width))
    }
    override func setAccessibilitySelectedTextRanges(_ ranges: [NSValue]?) {
        if let range = ranges?.first?.rangeValue { setAccessibilitySelectedTextRange(range) }
    }
    override func isAccessibilitySelectorAllowed(_ selector: Selector) -> Bool {
        if selector == #selector(setAccessibilityValue(_:)) || selector == #selector(setAccessibilitySelectedText(_:)) { return false }
        return super.isAccessibilitySelectorAllowed(selector)
    }
    override func setAccessibilityFocused(_ focused: Bool) {
        if focused { window?.makeFirstResponder(self) }
    }
    override func accessibilityLine(for index: Int) -> Int { accessibleText?.line(at: index) ?? NSNotFound }
    override func accessibilityRange(forLine line: Int) -> NSRange {
        guard let text = accessibleText, text.rows.indices.contains(line) else { return NSRange(location: NSNotFound, length: 0) }
        return text.rows[line]
    }
    override func accessibilityString(for range: NSRange) -> String? { accessibleText?.substring(range) }
    override func accessibilityAttributedString(for range: NSRange) -> NSAttributedString? {
        accessibleText?.substring(range).map { NSAttributedString(string: $0) }
    }
    override func accessibilityRange(for index: Int) -> NSRange {
        guard let text = accessibleText?.value as NSString?, index >= 0, index < text.length else {
            return NSRange(location: NSNotFound, length: 0)
        }
        return text.rangeOfComposedCharacterSequence(at: index)
    }
    override func accessibilityRange(for point: NSPoint) -> NSRange {
        guard let window, let block = activeBlock, let text = accessibleText else { return NSRange(location: NSNotFound, length: 0) }
        let local = convert(window.convertPoint(fromScreen: point), from: nil)
        guard contentRect(block).contains(local) else { return NSRange(location: NSNotFound, length: 0) }
        return text.cells[cellIndex(local, in: block)]
    }
    override func accessibilityFrame(for range: NSRange) -> NSRect {
        guard let window, let block = activeBlock, let text = accessibleText, text.substring(range) != nil else { return .zero }
        var rect = NSRect.null
        for (index, cell) in text.cells.enumerated() where NSIntersectionRange(cell, range).length > 0
            || (range.length == 0 && cell.location == range.location) {
            rect = rect.union(NSRect(x: CGFloat(block.x + index % block.width) * cellWidth,
                                     y: CGFloat(block.y + index / block.width) * cellHeight, width: cellWidth, height: cellHeight))
            if range.length == 0 { break }
        }
        return rect.isNull ? .zero : window.convertToScreen(convert(rect, to: nil))
    }
}
