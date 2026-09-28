import AppKit
import Carbon

extension MacSmoke {
    /// Translates keys with installed Apple layouts without changing the user's selected input source.
    static func exerciseKeyboardLayouts(_ controller: TerminalWindow) async throws {
        guard let runtime = ProcessInfo.processInfo.environment["WEFT_SOCKET_DIR"], let window = controller.window else {
            throw SmokeFailure.failed("Keyboard checks require a private terminal")
        }
        let cases: [(String, UInt16)] = [("com.apple.keylayout.French", 12), ("com.apple.keylayout.German", 16), ("com.apple.keylayout.Spanish-ISO", 41)]
        var translated: [(UInt16, String)] = []
        for (id, key) in cases {
            let filter = [kTISPropertyInputSourceID!: id] as CFDictionary
            let sources = TISCreateInputSourceList(filter, true).takeRetainedValue() as! [TISInputSource]
            guard let source = sources.first, let pointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else {
                throw SmokeFailure.failed("Installed Apple keyboard layout is unavailable: \(id)")
            }
            let data = Unmanaged<CFData>.fromOpaque(pointer).takeUnretainedValue()
            let layout = UnsafeRawPointer(CFDataGetBytePtr(data)!).assumingMemoryBound(to: UCKeyboardLayout.self)
            var state: UInt32 = 0
            var length = 0
            var characters = [UniChar](repeating: 0, count: 16)
            let result = UCKeyTranslate(layout, key, UInt16(kUCKeyActionDown), 0, UInt32(LMGetKbdType()),
                                        OptionBits(kUCKeyTranslateNoDeadKeysBit), &state, characters.count, &length, &characters)
            guard result == noErr, length > 0 else { throw SmokeFailure.failed("Keyboard translation failed for \(id)") }
            translated.append((key, String(utf16CodeUnits: characters, count: length)))
        }
        let expected = translated.map(\.1).joined()
        let file = URL(fileURLWithPath: runtime).deletingLastPathComponent().appendingPathComponent("keyboard.bin")
        controller.terminal.insertText("stty raw -echo; printf 'LAYOUT\\055READY'; dd bs=1 count=\(expected.utf8.count) of='\(file.path)' 2>/dev/null; stty sane; printf 'LAYOUT\\055DONE\\n'\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { text($0).contains("LAYOUT-READY") }
        for (key, value) in translated {
            guard let event = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0,
                    windowNumber: window.windowNumber, context: nil, characters: value,
                    charactersIgnoringModifiers: value, isARepeat: false, keyCode: key) else { throw SmokeFailure.failed("No keyboard event") }
            controller.terminal.keyDown(with: event)
        }
        _ = try await wait(controller) { text($0).contains("LAYOUT-DONE") }
        guard try Data(contentsOf: file) == Data(expected.utf8) else { throw SmokeFailure.failed("Translated keyboard text changed before reaching the PTY") }
        print("Apple French, German, and Spanish layout translations reached the PTY unchanged.")
    }
}
