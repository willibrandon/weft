import AppKit

/// Owns native windows and projects the shared command catalog into macOS menus.
/// Quitting releases client connections; session processes remain server-owned.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuItemValidation {
    private var windows: [TerminalWindow] = []
    private var commands: [DesktopAction] = []
    private var settings: SettingsWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.applicationIconImage = AppIcon.image()
        buildMenus()
        newWindow(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { newWindow(nil) }
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        windows.forEach { $0.disconnect() }
    }

    @objc func newWindow(_ sender: Any?) {
        let controller = TerminalWindow()
        controller.onCommands = { [weak self] commands in
            guard let self, self.commands.map(\.id) != commands.map(\.id) else { return }
            self.commands = commands
            self.buildMenus()
        }
        controller.onClose = { [weak self, weak controller] in
            self?.windows.removeAll { $0 === controller }
        }
        windows.append(controller)
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
    }

    private var active: TerminalWindow? { windows.first { $0.window === NSApp.mainWindow } }
    @objc private func showSettings(_ sender: Any?) {
        if settings == nil {
            settings = SettingsWindow(actions: commands) { [weak self] in
                self?.buildMenus()
                self?.windows.forEach { $0.refreshSettings() }
            }
        }
        settings?.showWindow(nil)
        settings?.window?.makeKeyAndOrderFront(nil)
    }
    @objc func newTab(_ sender: Any?) { active?.newTab() }
    @objc func newSession(_ sender: Any?) { active?.newSession() }
    @objc func splitRight(_ sender: Any?) { active?.splitRight() }
    @objc func splitBelow(_ sender: Any?) { active?.splitBelow() }
    @objc func zoom(_ sender: Any?) { active?.zoom() }
    @objc func largerText(_ sender: Any?) { active?.terminal.changeFontSize(by: 1) }
    @objc func smallerText(_ sender: Any?) { active?.terminal.changeFontSize(by: -1) }
    @objc func performCommand(_ sender: NSMenuItem) {
        if let id = sender.representedObject as? String { active?.performCommand(id) }
    }
    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        guard let id = item.representedObject as? String, let action = commands.first(where: { $0.id == id }) else { return true }
        return active?.canPerform(action) == true
    }
    @objc func showHelp(_ sender: Any?) {
        let alert = NSAlert()
        alert.messageText = "Weft"
        alert.informativeText = "Click + for a tab, or choose View → Commands to find an action. Scroll to read earlier output; Edit → Find searches it. Drag to select text, then copy with ⌘C. Hold Shift to select inside an app that uses the mouse. Customize shortcuts in Weft → Settings. Closing a window or quitting Weft leaves your sessions running."
        alert.addButton(withTitle: "Close")
        alert.runModal()
    }

    private func buildMenus() {
        let main = NSMenu()
        NSApp.mainMenu = main
        func menu(_ title: String) -> NSMenu {
            let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
            let submenu = NSMenu(title: title)
            item.submenu = submenu
            main.addItem(item)
            return submenu
        }
        func item(_ menu: NSMenu, _ title: String, _ action: Selector, _ key: String = "", _ target: AnyObject? = nil) {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
            item.target = target
            menu.addItem(item)
        }
        let app = menu("Weft")
        item(app, "About Weft", #selector(NSApplication.orderFrontStandardAboutPanel(_:)))
        item(app, "Settings…", #selector(showSettings(_:)), ",", self)
        app.addItem(.separator())
        item(app, "Hide Weft", #selector(NSApplication.hide(_:)), "h")
        app.addItem(.separator())
        item(app, "Quit Weft", #selector(NSApplication.terminate(_:)), "q")
        let file = menu("File")
        item(file, "New Window", #selector(newWindow(_:)), "n", self)
        file.addItem(.separator())
        item(file, "Close Window", #selector(NSWindow.performClose(_:)), "w")
        let edit = menu("Edit")
        item(edit, "Copy", #selector(TerminalView.copy(_:)), "c")
        item(edit, "Paste", #selector(TerminalView.paste(_:)), "v")
        item(edit, "Select All", #selector(NSResponder.selectAll(_:)), "a")
        for group in ["Session", "Tab", "Pane"] {
            let destination = menu(group)
            for action in commands where action.group == group { addAction(action, to: destination) }
        }
        for action in commands where action.group == "Edit" { addAction(action, to: edit) }
        let view = menu("View")
        item(view, "Larger Text", #selector(largerText(_:)), "+", self)
        item(view, "Smaller Text", #selector(smallerText(_:)), "-", self)
        for action in commands where action.group == "View" { addAction(action, to: view) }
        let window = menu("Window")
        item(window, "Minimize", #selector(NSWindow.performMiniaturize(_:)), "m")
        NSApp.windowsMenu = window
        let help = menu("Help")
        item(help, "Weft Help", #selector(showHelp(_:)), "", self)
        NSApp.helpMenu = help
    }

    private func addAction(_ action: DesktopAction, to menu: NSMenu) {
        let item = NSMenuItem(title: action.label, action: #selector(performCommand(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = action.id
        MacShortcuts.apply(action.id, to: item)
        menu.addItem(item)
    }
}
