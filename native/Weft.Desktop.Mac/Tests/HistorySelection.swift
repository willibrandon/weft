import AppKit

extension MacSmoke {
    /// A held drag crosses the viewport without losing its anchor, and releasing it stops autoscroll.
    static func exerciseHistorySelection(_ controller: TerminalWindow) async throws {
        let view = controller.terminal
        view.resume()
        let live = try await wait(controller) { $0.blocks[0].scrollOffset == 0 && $0.blocks[0].viewVersion == 0 }
        let block = live.blocks[0]
        guard let index = block.cells.indices.first(where: { index in
            index + 18 <= block.cells.count && block.cells[index..<index + 18].map(\.text).joined() == "NATIVE-HISTORY-100"
        }) else { throw SmokeFailure.failed("No history selection anchor") }
        let start = NSPoint(x: CGFloat(block.x + index % block.width) * view.cellWidth + view.cellWidth / 2,
                            y: CGFloat(block.y + index / block.width) * view.cellHeight + view.cellHeight / 2)
        let outside = NSPoint(x: start.x, y: CGFloat(block.y) * view.cellHeight - view.cellHeight * 3)
        view.mouseDown(with: try mouseEvent(.leftMouseDown, in: view, point: start))
        view.mouseDragged(with: try mouseEvent(.leftMouseDragged, in: view, point: NSPoint(x: start.x + view.cellWidth * 5, y: start.y)))
        let anchored = try await wait(controller) { $0.blocks[0].selection != nil }
        guard anchored.blocks[0].selection?.text == "NATIVE", anchored.blocks[0].selection?.start == index else {
            throw SmokeFailure.failed("Selection started away from the pointer: expected \(index), actual \(String(describing: anchored.blocks[0].selection))")
        }
        view.mouseDragged(with: try mouseEvent(.leftMouseDragged, in: view, point: outside))
        let extended = try await wait(controller) {
            $0.blocks[0].scrollOffset >= 12 && ($0.blocks[0].selection?.text.components(separatedBy: "\n").count ?? 0) > block.height
        }
        view.mouseUp(with: try mouseEvent(.leftMouseUp, in: view, point: outside))
        try await Task.sleep(for: .milliseconds(200))
        let stopped = view.frameData?.blocks[0].scrollOffset
        try await Task.sleep(for: .milliseconds(150))
        guard view.frameData?.blocks[0].scrollOffset == stopped,
              view.accessibilitySelectedText()?.contains("NATIVE-HISTORY-099") == true,
              extended.blocks[0].selection != nil else {
            throw SmokeFailure.failed("Selection lost history or autoscroll continued after mouse release: offset \(String(describing: stopped)) -> \(String(describing: view.frameData?.blocks[0].scrollOffset)), text=\(view.accessibilitySelectedText() ?? "nil")")
        }
        view.resume()
        _ = try await wait(controller) { $0.blocks[0].selection == nil && $0.blocks[0].viewVersion == 0 }
    }
}
