import Foundation

/// A grapheme, resolved style, and optional OSC 8 link supplied by the terminal core.
struct DesktopCell: Decodable, Equatable, Sendable {
    let text: String
    let foreground: Int?
    let background: Int?
    let attributes: Int
    let link: String?

    init(from decoder: Decoder) throws {
        var cell = try decoder.unkeyedContainer()
        text = try cell.decode(String.self)
        if cell.isAtEnd {
            foreground = nil
            background = nil
            attributes = 0
            link = nil
        } else {
            foreground = try cell.decodeIfPresent(Int.self)
            background = try cell.decodeIfPresent(Int.self)
            attributes = try cell.decode(Int.self)
            link = cell.isAtEnd ? nil : try cell.decodeIfPresent(String.self)
            guard cell.isAtEnd else {
                throw DecodingError.dataCorruptedError(in: cell, debugDescription: "Unexpected terminal cell field")
            }
        }
    }
}

/// One visible terminal viewport, including its history position and input modes.
struct DesktopBlock: Decodable, Sendable {
    let id: String
    let title: String
    let active: Bool
    let x: Int
    let y: Int
    let width: Int
    let height: Int
    let cursorX: Int
    let cursorY: Int
    let cursorVisible: Bool
    let cursorShape: Int
    let cells: [DesktopCell]
    let historyLines: Int
    let scrollOffset: Int
    let alternateScreen: Bool
    let mouseTracking: Bool
    /// Zero denotes live output; other values identify a retained history snapshot.
    let viewVersion: Int64
    let searchQuery: String
    let searchMatches: Int
    let images: [DesktopImage]
    var textures: [DesktopTexture]
    let selection: DesktopSelection?
}

/// Cell bounds are relative to the visible viewport; text includes selected history outside it.
struct DesktopSelection: Decodable, Equatable, Sendable {
    let start: Int
    let end: Int
    let text: String
}

/// A raster placement resolved by the terminal core; all drawing bounds use cell units.
struct DesktopImage: Decodable, Equatable, Sendable {
    let key: String
    let format: Int
    let pixelWidth: Int
    let pixelHeight: Int
    let x: Double
    let y: Double
    let width: Double
    let height: Double
    let clipX: Double
    let clipY: Double
    let clipWidth: Double
    let clipHeight: Double
    let layer: Int
    var textureKey: String { "\(format):\(pixelWidth):\(pixelHeight):\(key)" }
}

/// Shared pixel storage for every placement of the same terminal image.
struct DesktopTexture: Decodable, Equatable, Sendable {
    let key: String
    var data = Data()
    let byteLength: Int
    let format: Int
    let pixelWidth: Int
    let pixelHeight: Int
    var cacheKey: String { "\(format):\(pixelWidth):\(pixelHeight):\(key)" }

    private enum CodingKeys: String, CodingKey {
        case key, byteLength, format, pixelWidth, pixelHeight
    }
}

/// Server-owned tab metadata used by navigation and command enablement.
struct DesktopTab: Decodable, Sendable {
    let id: String
    let name: String
    let blocks: Int
    let synchronized: Bool
}

/// A durable session that can be selected without restarting its processes.
struct DesktopSession: Decodable, Sendable {
    let id: String
    let name: String
}

/// The latest coalesced client state, decoded from the versioned native bridge.
struct DesktopFrame: Decodable, Sendable {
    let connected: Bool
    let title: String
    let activeTab: String?
    let error: String?
    let tabs: [DesktopTab]
    var blocks: [DesktopBlock]
    let sessions: [DesktopSession]
    let activeSession: String?
    let commands: [DesktopAction]
}

/// A platform-neutral command description; shortcuts are supplied by ``MacShortcuts``.
struct DesktopAction: Decodable, Sendable {
    let id: String
    let label: String
    let group: String
    let scope: String
    let destructive: Bool
}

/// An ordered request whose target uses a stable session, tab, or block identity.
struct DesktopCommand: Encodable {
    let operation: String
    var target: String? = nil
    var text: String? = nil
    var width: Int = 0
    var height: Int = 0
    var x: Int = 0
    var y: Int = 0
    var button: Int = 0
    var modifiers: Int = 0
}
