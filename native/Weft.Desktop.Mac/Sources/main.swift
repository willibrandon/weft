import AppKit

// Packaging renders the product's vector icon without constructing NSApplication.
if CommandLine.arguments.count == 3 && CommandLine.arguments[1] == "--render-icon" {
    do {
        let directory = URL(fileURLWithPath: CommandLine.arguments[2], isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        for size in [16, 32, 128, 256, 512] {
            for scale in [1, 2] {
                let pixels = size * scale
                let icon = AppIcon.image(size: CGFloat(pixels))
                guard let tiff = icon.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
                      let png = bitmap.representation(using: .png, properties: [:]) else { exit(1) }
                let name = "icon_\(size)x\(size)" + (scale == 2 ? "@2x" : "") + ".png"
                try png.write(to: directory.appendingPathComponent(name))
            }
        }
        exit(0)
    } catch {
        FileHandle.standardError.write(Data((error.localizedDescription + "\n").utf8))
        exit(1)
    }
}

// The GUI must never act as the server bootstrap executable. Reject unexpected
// arguments before creating NSApplication or a window, even in a broken bundle.
if CommandLine.arguments.dropFirst().contains(where: { !$0.hasPrefix("-psn_") }) {
    FileHandle.standardError.write(Data("Weft is the desktop app, not the server executable.\n".utf8))
    exit(64)
}

let application = NSApplication.shared
TerminalFont.register()
let delegate = AppDelegate()
application.delegate = delegate
application.setActivationPolicy(.regular)
application.run()
