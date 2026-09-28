import AppKit

extension MacSmoke {
    /// Closing an inactive tab must preserve the active tab and keep its target through confirmation.
    static func exerciseTabClose(_ controller: TerminalWindow) async throws {
        guard let window = controller.window, let frame = controller.terminal.frameData,
              let strip = window.toolbar?.items.first(where: { $0.itemIdentifier.rawValue == "tabs" })?.view,
              let inactive = descendants(strip).compactMap({ $0 as? TerminalTabButton }).first(where: { !$0.selected }),
              let close = descendants(inactive).compactMap({ $0 as? NSButton }).first(where: { $0.toolTip == "Close Tab" }),
              let enter = NSEvent.enterExitEvent(with: .mouseEntered, location: .zero, modifierFlags: [], timestamp: 0,
                windowNumber: window.windowNumber, context: nil, eventNumber: 0, trackingNumber: 0, userData: nil) else {
            throw SmokeFailure.failed("No inactive tab close control")
        }
        inactive.mouseExited(with: enter)
        guard close.isHidden else { throw SmokeFailure.failed("Tab close button is visible without hover") }
        inactive.mouseEntered(with: enter)
        inactive.layoutSubtreeIfNeeded()
        guard !close.isHidden, abs(close.frame.midY - inactive.bounds.midY) < 0.5 else {
            throw SmokeFailure.failed("Hover close control is missing or vertically misaligned")
        }
        let point = close.convert(NSPoint(x: close.bounds.midX, y: close.bounds.midY), to: inactive.superview)
        guard inactive.hitTest(point) === close else { throw SmokeFailure.failed("Tab selection intercepts the close button") }
        close.performClick(nil)
        guard controller.terminal.frameData?.activeTab == frame.activeTab,
              let content = window.attachedSheet?.contentView,
              let cancel = descendants(content).compactMap({ $0 as? NSButton }).first(where: { $0.title == "Cancel" }) else {
            throw SmokeFailure.failed("Closing an inactive tab changed focus or omitted confirmation")
        }
        cancel.performClick(nil)
        let deadline = ContinuousClock.now + .seconds(3)
        while window.attachedSheet != nil && ContinuousClock.now < deadline { try await Task.sleep(for: .milliseconds(10)) }
        guard window.attachedSheet == nil, controller.terminal.frameData?.tabs.count == 2 else {
            throw SmokeFailure.failed("Cancel closed a tab or left its confirmation open")
        }
        close.performClick(nil)
        guard let confirmation = window.attachedSheet?.contentView,
              let end = descendants(confirmation).compactMap({ $0 as? NSButton }).first(where: { $0.title == "End Processes and Close" }) else {
            throw SmokeFailure.failed("Tab close confirmation cannot be accepted")
        }
        end.performClick(nil)
        _ = try await wait(controller) { $0.tabs.count == 1 && $0.activeTab == frame.activeTab && $0.blocks.count == 2 }
    }
}
