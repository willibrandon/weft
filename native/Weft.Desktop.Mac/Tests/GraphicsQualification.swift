import AppKit

extension MacSmoke {
    /// Runs an animation inside a real shell and observes both its bridge bytes and AppKit pixels.
    static func exerciseGraphics(_ controller: TerminalWindow) async throws {
        let command = "printf '\\033[2J\\033[H\\033_Ga=T,f=32,s=1,v=1,i=77,c=4,r=3,q=2;/wAA/w==\\033\\\\\\033_Ga=f,i=77,f=32,s=1,v=1,z=120,q=2;AP8A/w==\\033\\\\\\033_Ga=a,i=77,r=1,z=120,c=1,s=3,v=1,q=2\\033\\\\'\r"
        controller.terminal.insertText(command, replacementRange: NSRange(location: NSNotFound, length: 0))
        let frame = try await wait(controller, pollEvery: .milliseconds(5)) {
            $0.blocks.first?.textures.contains(where: { Array($0.data.prefix(4)) == [0, 255, 0, 255] }) == true
        }
        guard let block = frame.blocks.first, let texture = block.textures.first(where: { Array($0.data.prefix(4)) == [0, 255, 0, 255] }),
              let placement = block.images.first(where: { $0.textureKey == texture.cacheKey }) else {
            throw SmokeFailure.failed("Animated terminal image did not advance")
        }
        let captured = try capture(controller.terminal)
        guard let bitmap = captured.converting(to: .sRGB, renderingIntent: .default) else {
            throw SmokeFailure.failed("Could not normalize the animation's color profile")
        }
        let scaleX = CGFloat(bitmap.pixelsWide) / controller.terminal.bounds.width
        let scaleY = CGFloat(bitmap.pixelsHigh) / controller.terminal.bounds.height
        let x = Int((CGFloat(block.x) + placement.x + 0.5) * controller.terminal.cellWidth * scaleX)
        let y = Int((CGFloat(block.y) + placement.y + 0.5) * controller.terminal.cellHeight * scaleY)
        guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.sRGB), color.greenComponent - color.redComponent > 0.5 else {
            try bitmap.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: CommandLine.arguments[2] + ".animation.png"))
            throw SmokeFailure.failed("Animation pixel at \(x),\(y) is \(String(describing: bitmap.colorAt(x: x, y: y))); placement \(placement.x),\(placement.y)")
        }
        controller.terminal.insertText("printf '\\033_Ga=d,d=a,q=2\\033\\\\'\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { $0.blocks.first?.images.isEmpty == true }
        let red = Data((0..<(15 * 9)).flatMap { _ in [UInt8(255), 0, 0, 255] }).base64EncodedString()
        controller.terminal.insertText("printf '\\033[2J\\033[H\\033_Ga=T,f=32,s=15,v=9,X=3,Y=4,q=2;\(red)\\033\\\\'\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        let native = try await wait(controller) { $0.blocks.first?.images.contains(where: { $0.pixelWidth == 15 }) == true }
        guard let nativeImage = native.blocks[0].images.first,
              abs(nativeImage.width - 1.5) < 0.001, abs(nativeImage.height - 0.45) < 0.001 else {
            throw SmokeFailure.failed("Native image dimensions rounded to whole cells")
        }
        let inside = try graphicsPixel(controller.terminal, block: native.blocks[0], x: 1.05, y: 0.425)
        let outside = try graphicsPixel(controller.terminal, block: native.blocks[0], x: 2.1, y: 0.425)
        guard inside.redComponent - inside.greenComponent > 0.5,
              outside.redComponent - outside.greenComponent < 0.2 else {
            throw SmokeFailure.failed("Native image pixels escaped their fractional placement")
        }
        controller.terminal.insertText("printf '\\033_Ga=d,d=a,q=2\\033\\\\\\033[2J\\033[H\\033_Ga=T,f=32,s=2,v=1,x=1,w=1,c=4,r=2,X=3,Y=4,q=2;/wAA/wD/AP8=\\033\\\\'\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        let crop = try await wait(controller) { $0.blocks.first?.images.contains(where: { abs($0.clipWidth - 3.7) < 0.001 }) == true }
        let cropped = try graphicsPixel(controller.terminal, block: crop.blocks[0], x: 2, y: 1)
        guard cropped.greenComponent - cropped.redComponent > 0.5 else { throw SmokeFailure.failed("Cropped image sampled the wrong source pixel") }
        let corners = Data([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255]).base64EncodedString()
        controller.terminal.insertText("printf '\\033_Ga=d,d=a,q=2\\033\\\\\\033[2J\\033[H\\033_Ga=T,f=32,s=2,v=2,c=4,r=4,q=2;\(corners)\\033\\\\'\r",
                                       replacementRange: NSRange(location: NSNotFound, length: 0))
        let upright = try await wait(controller) { $0.blocks.first?.images.contains(where: { $0.pixelHeight == 2 }) == true }
        let top = try graphicsPixel(controller.terminal, block: upright.blocks[0], x: 1, y: 1)
        let bottom = try graphicsPixel(controller.terminal, block: upright.blocks[0], x: 1, y: 3)
        guard top.redComponent - top.blueComponent > 0.5, bottom.blueComponent - bottom.redComponent > 0.5 else {
            throw SmokeFailure.failed("Terminal raster rows were inverted")
        }
        controller.terminal.insertText("printf '\\033_Ga=d,d=a,q=2\\033\\\\'\r", replacementRange: NSRange(location: NSNotFound, length: 0))
        _ = try await wait(controller) { $0.blocks.first?.images.isEmpty == true }
        print("Graphics pixels passed animation, deletion, cropping, offsets, row orientation, and fractional native sizing.")
    }

    private static func graphicsPixel(_ view: TerminalView, block: DesktopBlock, x: CGFloat, y: CGFloat) throws -> NSColor {
        guard let bitmap = try capture(view).converting(to: .sRGB, renderingIntent: .default),
              let color = bitmap.colorAt(
                x: Int((CGFloat(block.x) + x) * view.cellWidth * CGFloat(bitmap.pixelsWide) / view.bounds.width),
                y: Int((CGFloat(block.y) + y) * view.cellHeight * CGFloat(bitmap.pixelsHigh) / view.bounds.height))?.usingColorSpace(.sRGB) else {
            throw SmokeFailure.failed("Could not sample the terminal raster")
        }
        return color
    }
}
