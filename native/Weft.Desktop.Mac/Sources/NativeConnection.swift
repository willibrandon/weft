import AppKit

/// Owns one opaque Native AOT client handle and delivers frames on the main actor.
/// A worker owns frame conversion and buffers; the library outlives all windows.
@MainActor
final class NativeConnection: NSObject {
    private var handle: Int64 = 0
    private var notification: NativeFrameNotification?
    private var callbackContext: UnsafeMutableRawPointer?
    var onFrame: ((DesktopFrame) -> Void)?
    var onError: ((String) -> Void)?

    /// Starts an asynchronous attachment, replacing this object's previous handle if necessary.
    /// The explicit server path is used by isolated native tests.
    func connect(width: Int, height: Int, executablePath: String? = nil) {
        close()
        guard weft_abi_version() == 4 else {
            onError?("The app and client library are incompatible. Rebuild the app bundle.")
            return
        }
        let executable = executablePath ?? Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/weft-server").path
        let path = Data(executable.utf8)
        handle = path.withUnsafeBytes {
            weft_open($0.baseAddress, Int32($0.count), Int32(width), Int32(height))
        }
        guard handle != 0 else {
            onError?("Weft could not open a client connection.")
            return
        }
        guard let wake = NativeFrameNotification(connection: self, handle: handle) else {
            close()
            onError?("Weft could not schedule terminal updates.")
            return
        }
        let context = Unmanaged.passRetained(wake).toOpaque()
        notification = wake
        callbackContext = context
        _ = weft_notify(handle, NativeFrameNotification.callback, context)
    }

    /// Queues input once; rejected or uncertain input is reported and never retried here.
    func send(_ command: DesktopCommand) {
        guard handle != 0 else { return }
        do {
            let data = try JSONEncoder().encode(command)
            guard data.count <= 1_048_576 else {
                onError?("This input is too large. Send less than 1 MB at a time.")
                return
            }
            let result = data.withUnsafeBytes { weft_send(handle, $0.baseAddress, Int32($0.count)) }
            if result != 0 {
                onError?("The input was not sent. The connection is closed or busy.")
                NSSound.beep()
            }
        } catch {
            onError?(error.localizedDescription)
        }
    }

    @objc func poll() {
        guard handle != 0, let result = notification?.take() else { return }
        if let error = result.error { onError?(error) }
        if let frame = result.frame { onFrame?(frame) }
    }

    /// Stops polling and releases the handle without terminating the session's workloads.
    func close() {
        if handle != 0 { _ = weft_notify(handle, nil, nil) }
        notification?.cancel()
        if let callbackContext { Unmanaged<NativeFrameNotification>.fromOpaque(callbackContext).release() }
        callbackContext = nil
        notification = nil
        if handle != 0 { weft_close(handle) }
        handle = 0
    }
}
