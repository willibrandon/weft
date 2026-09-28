import Foundation

/// Maps the visible cell grid to AppKit's UTF-16 text ranges, including wide and combining graphemes.
struct TerminalText {
    let value: String
    let cells: [NSRange]
    let rows: [NSRange]
    let cursor: NSRange

    init(_ block: DesktopBlock) {
        var value = ""
        var cells: [NSRange] = []
        var rows: [NSRange] = []
        value.reserveCapacity(block.cells.count)
        cells.reserveCapacity(block.cells.count)
        rows.reserveCapacity(block.height)
        var offset = 0
        for row in 0..<block.height {
            let start = row * block.width
            let end = start + block.width
            let content = block.cells[start..<end]
            var used = content.lastIndex(where: { !$0.text.isEmpty && $0.text != " " }).map { $0 - start + 1 } ?? 0
            if row == block.cursorY { used = max(used, block.cursorX) }
            let beginning = offset
            for column in 0..<block.width {
                let text = column < used ? block.cells[start + column].text : ""
                if column > 0 && block.cells[start + column].text.isEmpty, let previous = cells.last {
                    cells.append(previous)
                } else {
                    cells.append(NSRange(location: offset, length: text.utf16.count))
                    value += text
                    offset += text.utf16.count
                }
            }
            rows.append(NSRange(location: beginning, length: offset - beginning))
            if row + 1 < block.height { value += "\n"; offset += 1 }
        }
        self.value = value
        self.cells = cells
        self.rows = rows
        let index = min(cells.count - 1, max(0, block.cursorY * block.width + block.cursorX))
        cursor = NSRange(location: cells.isEmpty ? 0 : cells[index].location, length: 0)
    }

    func substring(_ range: NSRange) -> String? {
        let text = value as NSString
        guard range.location != NSNotFound, range.location >= 0, range.length >= 0,
              range.location <= text.length, range.length <= text.length - range.location else { return nil }
        return text.substring(with: range)
    }

    func range(from first: Int, through last: Int) -> NSRange {
        guard !cells.isEmpty, last >= 0, first < cells.count else { return NSRange(location: NSNotFound, length: 0) }
        let start = cells[max(0, first)].location
        let end = NSMaxRange(cells[min(cells.count - 1, last)])
        return NSRange(location: start, length: max(0, end - start))
    }

    func line(at index: Int) -> Int {
        guard index >= 0, index <= value.utf16.count else { return NSNotFound }
        return rows.lastIndex(where: { $0.location <= index }) ?? 0
    }
}
