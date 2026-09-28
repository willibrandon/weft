import Foundation
import Synchronization

/// Transfers and decodes frames on one worker, retaining only the latest display for AppKit.
/// A mutex protects state and the run-loop source; only immutable frames cross to the main actor.
final class NativeFrameNotification: Sendable {
    private let handle: Int64
    private let queue = DispatchQueue(label: "Weft.frame-decoding", qos: .userInitiated)
    private let state: Mutex<State>

    private struct State {
        let source: CFRunLoopSource
        var pending = false
        var working = false
        var closed = false
        var latest: (frame: DesktopFrame?, error: String?)?
    }

    /// Runs on a .NET worker thread and schedules bounded work without waiting for frame decoding.
    static let callback: @convention(c) (UnsafeMutableRawPointer?) -> Void = { context in
        guard let context else { return }
        Unmanaged<NativeFrameNotification>.fromOpaque(context).takeUnretainedValue().schedule()
    }

    /// Common run-loop modes allow native tracking loops to receive already-decoded frames.
    static let perform: @convention(c) (UnsafeMutableRawPointer?) -> Void = { context in
        guard let context else { return }
        let connection = Unmanaged<NativeConnection>.fromOpaque(context).takeUnretainedValue()
        MainActor.assumeIsolated { connection.poll() }
    }

    @MainActor
    init?(connection: NativeConnection, handle: Int64) {
        var context = CFRunLoopSourceContext(version: 0, info: Unmanaged.passUnretained(connection).toOpaque(),
            retain: nil, release: nil, copyDescription: nil, equal: nil, hash: nil, schedule: nil, cancel: nil,
            perform: Self.perform)
        guard let source = CFRunLoopSourceCreate(nil, 0, &context) else { return nil }
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        state = Mutex(State(source: source))
        self.handle = handle
    }

    func take() -> (frame: DesktopFrame?, error: String?)? {
        state.withLock {
            let result = $0.latest
            $0.latest = nil
            return result
        }
    }

    /// Discards queued work. A decode in progress owns its buffer and cannot deliver after cancellation.
    func cancel() {
        state.withLock {
            $0.closed = true
            $0.latest = nil
            CFRunLoopSourceInvalidate($0.source)
        }
    }

    private func schedule() {
        let start = state.withLock {
            guard !$0.closed else { return false }
            $0.pending = true
            guard !$0.working else { return false }
            $0.working = true
            return true
        }
        guard start else { return }
        queue.async { self.drain() }
    }

    private func drain() {
        while true {
            let pending = state.withLock {
                guard $0.pending && !$0.closed else {
                    $0.working = false
                    return false
                }
                $0.pending = false
                return true
            }
            guard pending else { return }
            guard let result = read() else { continue }
            state.withLock {
                guard !$0.closed else { return }
                $0.latest = result
                CFRunLoopSourceSignal($0.source)
                CFRunLoopWakeUp(CFRunLoopGetMain())
            }
        }
    }

    private func read() -> (frame: DesktopFrame?, error: String?)? {
        var length: Int32 = 0
        guard let buffer = weft_poll(handle, &length) else {
            return length < 0 ? (nil, "Weft could not read the terminal display.") : nil
        }
        do {
            let frame = try NativeFrameDecoder.decode(buffer, length: Int(length))
            return (frame, nil)
        } catch {
            return (nil, "The client returned an unreadable display: " + error.localizedDescription)
        }
    }
}
