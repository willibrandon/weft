import AppKit
import ImageIO

/// Decodes bounded terminal rasters once and evicts them under memory pressure.
/// ImageIO receives in-memory data only; terminal output cannot name a file to open.
@MainActor
final class TerminalImages {
    private let cache = NSCache<NSString, CGImage>()

    init() {
        cache.totalCostLimit = 32 * 1024 * 1024
        cache.countLimit = 256
    }

    func removeAll() { cache.removeAllObjects() }

    func image(_ raster: DesktopTexture) -> CGImage? {
        let key = "\(raster.format):\(raster.pixelWidth):\(raster.pixelHeight):\(raster.key)" as NSString
        if let existing = cache.object(forKey: key) { return existing }
        guard raster.pixelWidth > 0, raster.pixelHeight > 0,
              raster.pixelWidth <= 4096, raster.pixelHeight <= 4096,
              raster.pixelWidth * raster.pixelHeight <= 4_194_304,
              raster.data.count <= 16 * 1024 * 1024 else { return nil }
        let bitmap: CGImage?
        if raster.format == 100 {
            guard let source = CGImageSourceCreateWithData(raster.data as CFData, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
                  properties[kCGImagePropertyPixelWidth] as? Int == raster.pixelWidth,
                  properties[kCGImagePropertyPixelHeight] as? Int == raster.pixelHeight else { return nil }
            bitmap = CGImageSourceCreateImageAtIndex(source, 0, [kCGImageSourceShouldCacheImmediately: true] as CFDictionary)
        } else {
            let bytes = raster.format == 24 ? 3 : 4
            guard [24, 32].contains(raster.format), raster.data.count == raster.pixelWidth * raster.pixelHeight * bytes,
                  let provider = CGDataProvider(data: raster.data as CFData) else { return nil }
            bitmap = CGImage(width: raster.pixelWidth, height: raster.pixelHeight, bitsPerComponent: 8,
                             bitsPerPixel: bytes * 8, bytesPerRow: raster.pixelWidth * bytes,
                             space: CGColorSpace(name: CGColorSpace.sRGB)!,
                             bitmapInfo: CGBitmapInfo(rawValue: bytes == 4 ? CGImageAlphaInfo.last.rawValue : CGImageAlphaInfo.none.rawValue),
                             provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        }
        guard let bitmap else { return nil }
        cache.setObject(bitmap, forKey: key, cost: raster.pixelWidth * raster.pixelHeight * 4)
        return bitmap
    }
}
