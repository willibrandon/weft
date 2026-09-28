import AppKit
import CryptoKit
import ImageIO

/// Exercises the production image cache with native captures read through real files.
@MainActor
enum ImageCacheQualification {
    static func run(_ paths: [String]) throws {
        var textures: [DesktopTexture] = []
        for path in paths {
            let data = try Data(contentsOf: URL(fileURLWithPath: path))
            guard let source = CGImageSourceCreateWithData(data as CFData, nil),
                  let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
                  let width = properties[kCGImagePropertyPixelWidth] as? Int,
                  let height = properties[kCGImagePropertyPixelHeight] as? Int else {
                throw SmokeFailure.failed("Could not read native capture: \(path)")
            }
            let key = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            textures.append(DesktopTexture(key: key, data: data, byteLength: data.count,
                                           format: 100, pixelWidth: width, pixelHeight: height))
        }
        guard let first = textures.first, let last = textures.last, first.key != last.key else {
            throw SmokeFailure.failed("Image cache qualification needs distinct native captures")
        }
        let cache = TerminalImages()
        cache.retain(Set(textures.map(\.cacheKey)))
        var lastCost = 0
        var decodedBytes = 0
        for texture in textures {
            try autoreleasepool {
                guard let image = cache.image(texture), cache.image(texture) === image else {
                    throw SmokeFailure.failed("The same terminal image was decoded more than once")
                }
                lastCost = image.bytesPerRow * image.height
                decodedBytes += lastCost
            }
            guard cache.retainedBytes <= 32 * 1024 * 1024, cache.count <= 256 else {
                throw SmokeFailure.failed("Native image cache exceeded its resource limits")
            }
        }
        cache.retain([last.cacheKey])
        guard cache.count == 1, cache.retainedBytes == lastCost else {
            throw SmokeFailure.failed("Replacing a frame retained obsolete images")
        }
        cache.retain([])
        guard cache.count == 0, cache.retainedBytes == 0 else {
            throw SmokeFailure.failed("An empty frame retained native images")
        }
        _ = cache.image(first)
        cache.removeAll()
        guard cache.count == 0, cache.retainedBytes == 0 else {
            throw SmokeFailure.failed("Closing a view retained native images")
        }
        print("Image cache passed reuse, resource limits, frame replacement, deletion, and closure with \(decodedBytes) decoded bytes from real capture files.")
    }
}
