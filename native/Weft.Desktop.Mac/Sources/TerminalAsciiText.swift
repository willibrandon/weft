import AppKit
import CoreText

/// Batches simple glyphs at terminal cell positions, retaining only the view's current font variants.
/// Complex text and glyphs that need individual clipping use the normal AppKit text path.
@MainActor
final class TerminalAsciiText {
    private var fonts: [NSFont: (glyphs: [CGGlyph], bounds: [CGRect])] = [:]
    private var glyphs: [CGGlyph] = []
    private var positions: [CGPoint] = []
    private var font: NSFont?
    private var color: NSColor?
    private var clip = CGRect.zero

    /// Adds a glyph that fits its cell, flushing the previous row or style before changing either.
    /// Coordinates belong to the terminal's flipped view; Core Text receives an upward baseline.
    func append(_ text: String, font: NSFont, color: NSColor, point: CGPoint,
                cellWidth: CGFloat, cellHeight: CGFloat, context: CGContext) -> Bool {
        guard text.utf8.count == 1, let character = text.utf8.first, character >= 33, character <= 126 else { return false }
        if fonts[font] == nil {
            let characters = (33...126).map { UniChar($0) }
            var glyphs = [CGGlyph](repeating: 0, count: characters.count)
            _ = CTFontGetGlyphsForCharacters(font, characters, &glyphs, characters.count)
            var bounds = [CGRect](repeating: .zero, count: glyphs.count)
            _ = CTFontGetBoundingRectsForGlyphs(font, .horizontal, glyphs, &bounds, glyphs.count)
            fonts[font] = (glyphs, bounds)
        }
        guard let cached = fonts[font] else { return false }
        let index = Int(character) - 33
        let bounds = cached.bounds[index]
        guard cached.glyphs[index] != 0, bounds.minX >= 0, bounds.maxX <= cellWidth,
              bounds.maxY <= font.ascender + 1, font.ascender - bounds.minY <= cellHeight - 1 else { return false }
        let cell = CGRect(x: point.x, y: point.y - 1, width: cellWidth, height: cellHeight)
        if self.font != font || self.color != color || clip.minY != cell.minY {
            flush(in: context)
        }
        if glyphs.isEmpty {
            self.font = font
            self.color = color
            clip = cell
        } else {
            clip = clip.union(cell)
        }
        glyphs.append(cached.glyphs[index])
        positions.append(CGPoint(x: point.x, y: -point.y - font.ascender))
        return true
    }

    /// Draws the pending row without changing glyph advances or shaping across terminal cells.
    func flush(in context: CGContext) {
        guard !glyphs.isEmpty, let font, let color else { return }
        context.saveGState()
        context.clip(to: clip)
        context.scaleBy(x: 1, y: -1)
        context.textMatrix = .identity
        context.setFillColor(color.cgColor)
        CTFontDrawGlyphs(font, glyphs, positions, glyphs.count, context)
        context.restoreGState()
        glyphs.removeAll(keepingCapacity: true)
        positions.removeAll(keepingCapacity: true)
    }

    /// Releases font lookups when the user changes fonts or closes the window.
    func removeAll() {
        fonts.removeAll()
        glyphs.removeAll()
        positions.removeAll()
        font = nil
        color = nil
    }
}
