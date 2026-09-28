import AppKit
import ImageIO

/// Keeps only current-frame rasters, with hard limits on decoded bytes and image count.
/// ImageIO receives in-memory data only; terminal output cannot name a file to open.
@MainActor
final class TerminalImages {
    private let byteLimit = 32 * 1024 * 1024
    private let countLimit = 256
    private var cache: [String: (image: CGImage, cost: Int, used: UInt64)] = [:]
    private var clock: UInt64 = 0
    /// Accounted decoded raster bytes, excluding the native compositor's own storage.
    private(set) var retainedBytes = 0
    var count: Int { cache.count }

    /// Animation replaces images continuously; obsolete providers must release their frame buffers immediately.
    func retain(_ keys: Set<String>) {
        for key in cache.keys where !keys.contains(key) { remove(key) }
    }

    func removeAll() {
        cache.removeAll(keepingCapacity: false)
        retainedBytes = 0
    }

    func image(_ raster: DesktopTexture) -> CGImage? {
        let key = raster.cacheKey
        clock &+= 1
        if var existing = cache[key] {
            existing.used = clock
            cache[key] = existing
            return existing.image
        }
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
        let cost = bitmap.bytesPerRow * bitmap.height
        guard cost <= byteLimit else { return bitmap }
        while cache.count >= countLimit || retainedBytes + cost > byteLimit {
            guard let oldest = cache.min(by: { $0.value.used < $1.value.used })?.key else { break }
            remove(oldest)
        }
        cache[key] = (bitmap, cost, clock)
        retainedBytes += cost
        return bitmap
    }

    private func remove(_ key: String) {
        if let old = cache.removeValue(forKey: key) { retainedBytes -= old.cost }
    }
}
