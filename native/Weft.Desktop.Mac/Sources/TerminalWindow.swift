import AppKit

/// Coordinates one session attachment, its terminal surface, and window-scoped sheets.
/// Commands capture their target before confirmation so later focus changes cannot retarget them.
@MainActor
final class TerminalWindow: NSWindowController, NSWindowDelegate, NSToolbarDelegate, NSMenuDelegate, NSMenuItemValidation, NSSearchFieldDelegate {
    let terminal = TerminalView(frame: .zero)
    private let connection = NativeConnection()
    private let tabStrip = TabStripView(frame: NSRect(x: 0, y: 0, width: 440, height: 30))
    private let sessionPicker = NSPopUpButton(frame: .zero, pullsDown: true)
    private let status = NSTextField(labelWithString: "Connecting…")
    private var statusHeight: NSLayoutConstraint!
    private var tabWidth: NSLayoutConstraint!
    private var latest: DesktopFrame?
    private var sessionMenuKey: [String] = []
    private var commandPanel: CommandPanel?
    private weak var commandPreviousFocus: NSResponder?
    private var actionsItem: NSMenuToolbarItem?
    private var catalogKey: [String] = []
    private let emptyButton = NSButton(title: "New Session…", target: nil, action: nil)
    private let findField = NSSearchField()
    private let findCount = NSTextField(labelWithString: "")
    private let findBar = NSStackView()
    private var findHeight: NSLayoutConstraint!
    private var findTarget: String?
    var onClose: (() -> Void)?
    var onCommands: (([DesktopAction]) -> Void)?
    var commands: [DesktopAction] { latest?.commands ?? [] }

    /// A nil autosave name keeps temporary windows from reading or changing saved window placement.
    init(executablePath: String? = nil, autosaveName: String? = "WeftTerminalWindow") {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1080, height: 720),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "Weft"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.toolbarStyle = .unifiedCompact
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = TerminalAppearance.background
        window.minSize = NSSize(width: 520, height: 280)
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        if let autosaveName { window.setFrameAutosaveName(autosaveName) }
        let toolbar = NSToolbar(identifier: "TerminalToolbar")
        toolbar.delegate = self
        toolbar.displayMode = .iconOnly
        toolbar.allowsUserCustomization = false
        window.toolbar = toolbar
        let root = NSView()
        root.clipsToBounds = true
        window.contentView = root
        status.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        status.lineBreakMode = .byTruncatingMiddle
        status.setAccessibilityLabel("Connection status")
        for view in [terminal, status] {
            view.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(view)
        }
        statusHeight = status.heightAnchor.constraint(equalToConstant: 24)
        emptyButton.target = self
        emptyButton.action = #selector(newSession)
        emptyButton.isHidden = true
        emptyButton.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(emptyButton)
        findField.placeholderString = "Find in terminal"
        findField.delegate = self
        findField.target = self
        findField.action = #selector(findNext)
        findField.setAccessibilityLabel("Find in terminal")
        findField.widthAnchor.constraint(greaterThanOrEqualToConstant: 200).isActive = true
        findBar.orientation = .horizontal
        findBar.spacing = 8
        findBar.edgeInsets = NSEdgeInsets(top: 0, left: 12, bottom: 0, right: 12)
        findCount.font = .systemFont(ofSize: 11)
        for view in [findField, findCount, NSButton(title: "Previous", target: self, action: #selector(findPrevious)),
                     NSButton(title: "Next", target: self, action: #selector(findNext)),
                     NSButton(title: "Done", target: self, action: #selector(closeFind))] { findBar.addArrangedSubview(view) }
        findBar.translatesAutoresizingMaskIntoConstraints = false
        findBar.isHidden = true
        root.addSubview(findBar)
        findHeight = findBar.heightAnchor.constraint(equalToConstant: 0)
        NSLayoutConstraint.activate([
            findBar.topAnchor.constraint(equalTo: root.topAnchor),
            findBar.leadingAnchor.constraint(equalTo: root.leadingAnchor), findBar.trailingAnchor.constraint(equalTo: root.trailingAnchor), findHeight,
            terminal.topAnchor.constraint(equalTo: findBar.bottomAnchor, constant: 2),
            terminal.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 2),
            terminal.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -2),
            terminal.bottomAnchor.constraint(equalTo: status.topAnchor, constant: -2),
            status.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 12),
            status.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -12),
            status.bottomAnchor.constraint(equalTo: root.bottomAnchor), statusHeight,
            emptyButton.centerXAnchor.constraint(equalTo: terminal.centerXAnchor),
            emptyButton.centerYAnchor.constraint(equalTo: terminal.centerYAnchor)
        ])
        tabStrip.select = { [weak self] id in self?.selectTab(id) }
        tabStrip.close = { [weak self] id in self?.performCommand("closeTab", explicitTarget: id) }
        terminal.send = { [weak self] in self?.connection.send($0) }
        terminal.runAction = { [weak self] id, target in self?.performCommand(id, explicitTarget: target) }
        terminal.resized = { [weak self] width, height in
            self?.connection.send(DesktopCommand(operation: "resize", width: width, height: height))
        }
        connection.onFrame = { [weak self] in self?.update($0) }
        connection.onError = { [weak self] in self?.showError($0) }
        root.layoutSubtreeIfNeeded()
        connection.connect(width: terminal.columns, height: terminal.rows, executablePath: executablePath)
        window.makeFirstResponder(terminal)
    }

    required init?(coder: NSCoder) { fatalError("Use init()") }

    func toolbarDefaultItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        [.init("sessions"), .init("tabs"), .flexibleSpace, .init("newTab"), .init("actions")]
    }

    func toolbarAllowedItemIdentifiers(_ toolbar: NSToolbar) -> [NSToolbarItem.Identifier] {
        toolbarDefaultItemIdentifiers(toolbar)
    }

    func toolbar(_ toolbar: NSToolbar, itemForItemIdentifier id: NSToolbarItem.Identifier,
                 willBeInsertedIntoToolbar flag: Bool) -> NSToolbarItem? {
        let item = NSToolbarItem(itemIdentifier: id)
        switch id.rawValue {
        case "sessions":
            sessionPicker.isBordered = false
            sessionPicker.font = .systemFont(ofSize: 12, weight: .medium)
            sessionPicker.lineBreakMode = .byTruncatingMiddle
            sessionPicker.widthAnchor.constraint(lessThanOrEqualToConstant: 150).isActive = true
            sessionPicker.addItem(withTitle: "Connecting…")
            sessionPicker.menu?.delegate = self
            sessionPicker.setAccessibilityLabel("Sessions")
            sessionPicker.toolTip = "Switch or create a session"
            item.label = "Sessions"
            item.view = sessionPicker
        case "tabs":
            item.label = "Tabs"
            item.view = tabStrip
            // Tabs draw their own selection; a toolbar bezel would add a second outline.
            item.isBordered = false
            tabStrip.translatesAutoresizingMaskIntoConstraints = false
            tabWidth = tabStrip.widthAnchor.constraint(equalToConstant: 440)
            NSLayoutConstraint.activate([tabWidth, tabStrip.heightAnchor.constraint(equalToConstant: 30)])
            item.visibilityPriority = .user
        case "newTab":
            item.label = "New Tab"
            item.toolTip = "New Tab (⌘T)"
            item.image = NSImage(systemSymbolName: "plus", accessibilityDescription: "New Tab")
            item.target = self
            item.action = #selector(newTab)
            item.visibilityPriority = .user
        case "actions":
            let actions = NSMenuToolbarItem(itemIdentifier: id)
            actions.label = "Terminal Actions"
            actions.toolTip = "Terminal Actions"
            actions.image = NSImage(systemSymbolName: "ellipsis.circle", accessibilityDescription: "Terminal Actions")
            actions.menu = NSMenu()
            actionsItem = actions
            return actions
        default: return nil
        }
        return item
    }

    private func update(_ frame: DesktopFrame) {
        latest = frame
        window?.title = frame.title + " — Weft"
        terminal.update(frame)
        tabStrip.update(frame.tabs, active: frame.activeTab, enabled: frame.connected)
        updateTabWidth()
        updateSessions()
        emptyButton.isHidden = !frame.connected || frame.activeSession != nil
        if !findBar.isHidden, let block = frame.blocks.first(where: { $0.id == findTarget }) {
            findCount.stringValue = block.searchQuery.isEmpty ? "" : "\(block.searchMatches) matching lines"
        }
        let key = frame.commands.map(\.id)
        if key != catalogKey {
            catalogKey = key
            actionsItem?.menu.removeAllItems()
            for action in frame.commands where action.group == "Pane" || action.id == "commands" || action.id == "font" {
                actionsItem?.menu.addItem(menuItem(action))
            }
            onCommands?(frame.commands)
        }
        setStatus(frame.error ?? (frame.connected ? nil : "Disconnected"))
    }

    private func updateSessions() {
        guard let frame = latest else { return }
        sessionPicker.isEnabled = frame.connected
        let key = [frame.activeSession ?? "", frame.title] + frame.sessions.flatMap { [$0.id, $0.name] }
        guard key != sessionMenuKey else { return }
        sessionMenuKey = key
        sessionPicker.removeAllItems()
        sessionPicker.addItem(withTitle: frame.title)
        sessionPicker.item(at: 0)?.image = NSImage(systemSymbolName: "rectangle.stack", accessibilityDescription: nil)
        for session in frame.sessions {
            let item = NSMenuItem(title: session.name, action: #selector(selectSession(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = session.id
            item.state = session.id == frame.activeSession ? .on : .off
            sessionPicker.menu?.addItem(item)
        }
        sessionPicker.menu?.addItem(.separator())
        let create = NSMenuItem(title: "New Session…", action: #selector(newSession), keyEquivalent: "")
        create.target = self
        sessionPicker.menu?.addItem(create)
        for action in commands where action.id == "renameSession" || action.id == "closeSession" {
            sessionPicker.menu?.addItem(menuItem(action))
        }
    }

    func menuWillOpen(_ menu: NSMenu) { connection.send(DesktopCommand(operation: "sessions")) }

    func windowDidResize(_ notification: Notification) { updateTabWidth() }

    private func updateTabWidth() {
        let available = max(100, (window?.frame.width ?? 1080) - 400)
        tabWidth?.constant = min(CGFloat(max(1, latest?.tabs.count ?? 1)) * 220, available)
    }

    private func setStatus(_ message: String?) {
        status.stringValue = message ?? ""
        status.textColor = .systemRed
        status.toolTip = message
        status.isHidden = message == nil
        statusHeight.constant = message == nil ? 0 : 24
    }

    private func showError(_ message: String) { setStatus(message) }

    func selectTab(_ id: String) {
        connection.send(DesktopCommand(operation: "tab", target: id))
        window?.makeFirstResponder(terminal)
    }

    @objc private func selectSession(_ item: NSMenuItem) {
        guard let id = item.representedObject as? String, id != latest?.activeSession else { return }
        connection.send(DesktopCommand(operation: "session", target: id))
        window?.makeFirstResponder(terminal)
    }

    @objc func newSession() {
        guard let window else { return }
        let alert = NSAlert()
        alert.messageText = "New Session"
        alert.addButton(withTitle: "Create")
        alert.addButton(withTitle: "Cancel")
        let name = NSTextField(frame: NSRect(x: 0, y: 0, width: 280, height: 24))
        name.placeholderString = "Session name"
        alert.accessoryView = name
        alert.window.initialFirstResponder = name
        alert.beginSheetModal(for: window) { [weak self] result in
            guard result == .alertFirstButtonReturn else { return }
            let text = name.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            self?.connection.send(DesktopCommand(operation: "newSession", text: text.isEmpty ? nil : text))
            self?.window?.makeFirstResponder(self?.terminal)
        }
    }

    @objc func newTab() { connection.send(DesktopCommand(operation: "newTab")) }
    @objc func splitRight() { connection.send(DesktopCommand(operation: "splitRight")) }
    @objc func splitBelow() { connection.send(DesktopCommand(operation: "splitBelow")) }
    @objc func zoom() { connection.send(DesktopCommand(operation: "zoom")) }
    @objc func showFonts() { terminal.showFonts(nil) }

    /// Refreshes native menu bindings and colors after a settings change.
    func refreshSettings() {
        terminal.refreshSettings()
        window?.backgroundColor = TerminalAppearance.background
        catalogKey = []
        sessionMenuKey = []
        if let latest { update(latest) }
    }

    func menuItem(_ action: DesktopAction) -> NSMenuItem {
        let item = NSMenuItem(title: action.label, action: #selector(runMenuCommand(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = action.id
        MacShortcuts.apply(action.id, to: item)
        return item
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        guard let id = menuItem.representedObject as? String, let action = commands.first(where: { $0.id == id }) else { return latest?.connected == true }
        return canPerform(action)
    }

    @objc private func runMenuCommand(_ item: NSMenuItem) {
        if let id = item.representedObject as? String { performCommand(id) }
    }

    func canPerform(_ action: DesktopAction) -> Bool {
        if action.id == "commands" || action.id == "font" { return true }
        guard let frame = latest, frame.connected else { return false }
        switch action.scope {
        case "session": return frame.activeSession != nil
        case "tab": return frame.activeTab != nil
        case "block": return frame.blocks.contains(where: \.active)
        default: return true
        }
    }

    /// Runs a catalog action, using a context-menu target when one was explicitly selected.
    func performCommand(_ id: String, explicitTarget: String? = nil) {
        guard let action = commands.first(where: { $0.id == id }), canPerform(action) else { return }
        let target = explicitTarget ?? (action.scope == "session" ? latest?.activeSession
            : action.scope == "tab" ? latest?.activeTab : latest?.blocks.first(where: \.active)?.id)
        switch id {
        case "newSession": newSession()
        case "font": showFonts()
        case "commands": showCommands()
        case "find": showFind(target: target)
        case "live": terminal.resume(target)
        case "nextTab", "previousTab":
            guard let frame = latest, let index = frame.tabs.firstIndex(where: { $0.id == frame.activeTab }), !frame.tabs.isEmpty else { return }
            selectTab(frame.tabs[(index + (id == "nextTab" ? 1 : frame.tabs.count - 1)) % frame.tabs.count].id)
        case "renameSession", "renameTab", "renameBlock":
            let current = id == "renameSession" ? latest?.title : id == "renameTab"
                ? latest?.tabs.first(where: { $0.id == target })?.name : latest?.blocks.first(where: { $0.id == target })?.title
            rename(action, target: target, current: current ?? "")
        case "closeSession", "closeTab", "closeBlock", "sync":
            confirm(action, target: target)
        default: connection.send(DesktopCommand(operation: id, target: target))
        }
    }

    private func rename(_ action: DesktopAction, target: String?, current: String) {
        guard let window, window.attachedSheet == nil else { return }
        let alert = NSAlert()
        alert.messageText = action.label.replacingOccurrences(of: "…", with: "")
        alert.addButton(withTitle: "Rename")
        alert.addButton(withTitle: "Cancel")
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24))
        field.stringValue = current
        field.setAccessibilityLabel("Name")
        alert.accessoryView = field
        alert.window.initialFirstResponder = field
        alert.beginSheetModal(for: window) { [weak self] result in
            if result == .alertFirstButtonReturn {
                self?.connection.send(DesktopCommand(operation: action.id, target: target, text: field.stringValue))
            }
            self?.window?.makeFirstResponder(self?.terminal)
        }
    }

    private func confirm(_ action: DesktopAction, target: String?) {
        guard let window, window.attachedSheet == nil else { return }
        let alert = NSAlert()
        alert.messageText = action.id == "sync" ? "Change input broadcasting?" : action.label.replacingOccurrences(of: "…", with: "") + "?"
        alert.informativeText = action.id == "sync" ? "When enabled, typing goes to every included pane in this tab."
            : "This ends the running processes inside it. Closing the window instead keeps them running."
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: action.id == "sync" ? "Change Broadcasting" : "End Processes and Close")
        alert.beginSheetModal(for: window) { [weak self] result in
            if result == .alertSecondButtonReturn { self?.connection.send(DesktopCommand(operation: action.id, target: target)) }
            self?.window?.makeFirstResponder(self?.terminal)
        }
    }

    /// Presents one searchable command sheet and restores terminal focus when dismissed.
    func showCommands() {
        guard let window else { return }
        if let commandPanel { commandPanel.makeKeyAndOrderFront(nil); return }
        guard window.attachedSheet == nil else { return }
        commandPreviousFocus = window.firstResponder
        let panel = CommandPanel(actions: commands.filter { canPerform($0) && $0.id != "commands" },
                                 run: { [weak self] in self?.performCommand($0) }, dismiss: { [weak self] in
            guard let self, let panel = self.commandPanel else { return }
            self.window?.endSheet(panel)
            panel.orderOut(nil)
            self.commandPanel = nil
            self.window?.makeFirstResponder(self.commandPreviousFocus ?? self.terminal)
            self.commandPreviousFocus = nil
        })
        commandPanel = panel
        window.beginSheet(panel)
    }

    private func showFind(target: String?) {
        findTarget = target
        findBar.isHidden = false
        findHeight.constant = 36
        window?.makeFirstResponder(findField)
    }

    @objc private func closeFind() {
        findBar.isHidden = true
        findHeight.constant = 0
        findField.stringValue = ""
        terminal.resume(findTarget)
        findTarget = nil
        window?.makeFirstResponder(terminal)
    }
    @objc private func findNext() { search(direction: 1) }
    @objc private func findPrevious() { search(direction: -1) }
    func controlTextDidChange(_ notification: Notification) { search(direction: 0) }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if NSStringFromSelector(selector) == "cancelOperation:" { closeFind(); return true }
        return false
    }
    private func search(direction: Int) {
        terminal.clearSelection()
        connection.send(DesktopCommand(operation: "find", target: findTarget, text: String(findField.stringValue.prefix(1024)), y: direction))
    }

    func windowWillClose(_ notification: Notification) {
        connection.close()
        latest = nil
        terminal.releaseResources()
        window?.toolbar = nil
        window?.contentView = nil
        window?.delegate = nil
        window = nil
        onClose?()
    }

    func disconnect() { connection.close() }
}
