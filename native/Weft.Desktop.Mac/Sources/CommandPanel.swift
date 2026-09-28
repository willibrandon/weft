import AppKit

/// Searches the same actions shown in menus without consuming terminal keystrokes.
/// The owning window controls dismissal and restores the previous input focus.
@MainActor
final class CommandPanel: NSPanel, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSWindowDelegate {
    private let search = NSSearchField()
    private let table = NSTableView()
    private let actions: [DesktopAction]
    private var filtered: [DesktopAction]
    private let run: (String) -> Void
    private let dismiss: () -> Void

    init(actions: [DesktopAction], run: @escaping (String) -> Void, dismiss: @escaping () -> Void) {
        self.actions = actions
        filtered = actions
        self.run = run
        self.dismiss = dismiss
        super.init(contentRect: NSRect(x: 0, y: 0, width: 480, height: 370),
                   styleMask: [.titled, .closable], backing: .buffered, defer: false)
        title = "Commands"
        isReleasedWhenClosed = false
        delegate = self
        let root = NSView()
        contentView = root
        search.placeholderString = "Search commands"
        search.delegate = self
        search.setAccessibilityLabel("Search commands")
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        table.addTableColumn(NSTableColumn(identifier: .init("command")))
        table.headerView = nil
        table.rowHeight = 30
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.doubleAction = #selector(execute)
        table.setAccessibilityLabel("Matching commands")
        scroll.documentView = table
        let close = NSButton(title: "Close", target: self, action: #selector(closePanel))
        let execute = NSButton(title: "Run", target: self, action: #selector(execute))
        execute.keyEquivalent = "\r"
        let buttons = NSStackView(views: [close, execute])
        buttons.spacing = 8
        for view in [search, scroll, buttons] { view.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(view) }
        NSLayoutConstraint.activate([
            search.topAnchor.constraint(equalTo: root.topAnchor, constant: 16),
            search.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            search.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            scroll.topAnchor.constraint(equalTo: search.bottomAnchor, constant: 8),
            scroll.leadingAnchor.constraint(equalTo: search.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: search.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: buttons.topAnchor, constant: -12),
            buttons.trailingAnchor.constraint(equalTo: search.trailingAnchor), buttons.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12)
        ])
        initialFirstResponder = search
        table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
    }

    func numberOfRows(in tableView: NSTableView) -> Int { filtered.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let label = NSTextField(labelWithString: filtered[row].label)
        label.font = .systemFont(ofSize: 13)
        let shortcut = NSTextField(labelWithString: MacShortcuts.display(filtered[row].id))
        shortcut.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [label, NSView(), shortcut])
        stack.orientation = .horizontal
        return stack
    }
    func controlTextDidChange(_ notification: Notification) {
        let query = search.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        filtered = actions.filter { query.isEmpty || $0.label.localizedCaseInsensitiveContains(query) || $0.group.localizedCaseInsensitiveContains(query) }
        table.reloadData()
        if !filtered.isEmpty { table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false) }
    }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        switch NSStringFromSelector(selector) {
        case "moveDown:", "moveUp:":
            guard !filtered.isEmpty else { return true }
            let direction = NSStringFromSelector(selector) == "moveDown:" ? 1 : -1
            let row = min(filtered.count - 1, max(0, table.selectedRow + direction))
            table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
            table.scrollRowToVisible(row)
            return true
        case "insertNewline:": execute(); return true
        case "cancelOperation:": closePanel(); return true
        default: return false
        }
    }
    override func cancelOperation(_ sender: Any?) { closePanel() }
    func windowShouldClose(_ sender: NSWindow) -> Bool { closePanel(); return false }
    @objc private func closePanel() { dismiss() }
    @objc private func execute() {
        guard filtered.indices.contains(table.selectedRow) else { return }
        let id = filtered[table.selectedRow].id
        dismiss()
        run(id)
    }
}
