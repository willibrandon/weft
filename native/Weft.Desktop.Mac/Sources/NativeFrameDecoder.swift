import Foundation

/// Decodes the ABI's bounded metadata followed by raw textures, in block and texture order.
/// Takes ownership even on failure. Texture slices keep the allocation alive until their last image is released.
enum NativeFrameDecoder {
    static func decode(_ buffer: UnsafeMutableRawPointer, length: Int) throws -> DesktopFrame {
        let owner = Data(bytesNoCopy: buffer, count: length, deallocator: .custom { pointer, _ in weft_free(pointer) })
        defer { withExtendedLifetime(owner) {} }
        guard length >= 4 else { throw corrupt() }
        let metadataLength = Int(UInt32(littleEndian: buffer.loadUnaligned(as: UInt32.self)))
        guard metadataLength <= length - 4 else { throw corrupt() }
        var frame = try JSONDecoder().decode(DesktopFrame.self,
            from: Data(bytesNoCopy: buffer + 4, count: metadataLength, deallocator: .none))
        var offset = 4 + metadataLength
        for block in frame.blocks.indices {
            for texture in frame.blocks[block].textures.indices {
                let count = frame.blocks[block].textures[texture].byteLength
                guard count >= 0, count <= length - offset else { throw corrupt() }
                frame.blocks[block].textures[texture].data = owner[offset..<(offset + count)]
                offset += count
            }
        }
        guard offset == length else { throw corrupt() }
        return frame
    }

    private static func corrupt() -> DecodingError {
        .dataCorrupted(.init(codingPath: [], debugDescription: "Invalid native frame length"))
    }
}
