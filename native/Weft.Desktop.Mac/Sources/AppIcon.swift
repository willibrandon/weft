import AppKit

/// Draws the original woven W mark for both the running app and packaged icon sizes.
@MainActor
enum AppIcon {
    /// Returns resolution-independent artwork without creating an application or window.
    static func image(size: CGFloat = 1024) -> NSImage {
        NSImage(size: NSSize(width: size, height: size), flipped: false) { _ in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.scaleBy(x: size / 1024, y: size / 1024)
            let tile = NSBezierPath(roundedRect: NSRect(x: 96, y: 96, width: 832, height: 832), xRadius: 184, yRadius: 184)
            let shadow = NSShadow()
            shadow.shadowColor = NSColor.black.withAlphaComponent(0.22)
            shadow.shadowBlurRadius = 28
            shadow.shadowOffset = NSSize(width: 0, height: -12)
            NSGraphicsContext.saveGraphicsState()
            shadow.set()
            NSColor(srgbRed: 0.055, green: 0.10, blue: 0.13, alpha: 1).setFill()
            tile.fill()
            NSGraphicsContext.restoreGraphicsState()
            NSGradient(starting: NSColor(srgbRed: 0.13, green: 0.20, blue: 0.24, alpha: 1),
                       ending: NSColor(srgbRed: 0.055, green: 0.10, blue: 0.13, alpha: 1))?.draw(in: tile, angle: -90)
            let thread = NSColor(srgbRed: 0.46, green: 0.65, blue: 0.70, alpha: 1)
            let ivory = NSColor(srgbRed: 0.89, green: 0.91, blue: 0.89, alpha: 1)
            func stroke(_ points: [NSPoint], color: NSColor, width: CGFloat) {
                let path = NSBezierPath()
                path.move(to: points[0])
                points.dropFirst().forEach { path.line(to: $0) }
                path.lineWidth = width
                path.lineCapStyle = .round
                path.lineJoinStyle = .round
                color.setStroke()
                path.stroke()
            }
            stroke([NSPoint(x: 294, y: 512), NSPoint(x: 730, y: 512)], color: thread, width: 44)
            stroke([NSPoint(x: 276, y: 690), NSPoint(x: 386, y: 334), NSPoint(x: 512, y: 606),
                    NSPoint(x: 638, y: 334), NSPoint(x: 748, y: 690)], color: ivory, width: 50)
            stroke([NSPoint(x: 304, y: 512), NSPoint(x: 354, y: 512)], color: thread, width: 44)
            stroke([NSPoint(x: 670, y: 512), NSPoint(x: 720, y: 512)], color: thread, width: 44)
            return true
        }
    }
}
