import Foundation

/// Initial regression envelopes from the recorded Mac workloads, with headroom for host variation.
/// These guard qualification runs; they do not replace recording the measurements or their hardware context.
enum PerformanceLimits {
    static func client(attach: Double, input: Double, idle: Double, resident: [UInt64]) throws {
        try maximum(attach, 500, "Warm attach p50 (ms)")
        try maximum(input, 100, "Idle input-to-paint p95 (ms)")
        try maximum(idle, 50, "Idle client CPU over 750 ms (ms)")
        guard let first = resident.first, let last = resident.last else {
            throw SmokeFailure.failed("Client churn produced no memory samples")
        }
        try maximum(max(0, Double(last) - Double(first)) / 1_048_576, 64, "Resident growth across five closed clients (MiB)")
    }

    static func workload(_ metrics: [String: Double]) throws {
        try maximum(metrics["inputPaintP95Milliseconds"]!, 150, "Loaded input-to-paint p95 (ms)")
        try maximum(metrics["switchP95Milliseconds"]!, 500, "Tab switch p95 (ms)")
        try maximum(metrics["drawP95Milliseconds"]!, 33, "Loaded native draw p95 (ms)")
        try maximum(metrics["clientOutputMiB"]!, 512, "Loaded client resident memory (MiB)")
        try maximum(max(0, metrics["clientAfterCloseMiB"]! - metrics["clientBaselineMiB"]!), 192,
                    "Resident growth after the 16-tab workload (MiB)")
    }

    /// Compare the same 800-bird workload and viewport used for the baseline; different geometry needs a new baseline.
    static func graphics(_ metrics: [String: Double], mode: String) throws {
        guard metrics["columns"] == 165, metrics["rows"] == 43,
              metrics["cellWidth"] == 9, metrics["cellHeight"] == 20 else {
            throw SmokeFailure.failed("Graphics measurements were saved, but regression limits require 165×43 cells at 9×20 points")
        }
        let floor = mode == "kitty" ? 55.0 : 48.0
        guard let fps = metrics["framesPerSecond"], fps.isFinite, fps >= floor else {
            throw SmokeFailure.failed("\(mode) animation fell below \(floor) observed frames/s")
        }
        try maximum(metrics["drawP95Milliseconds"]!, 8, "\(mode) native draw p95 (ms)")
        try maximum(metrics["clientCpuPercent"]!, 200, "\(mode) client CPU (% of one core)")
        try maximum(metrics["residentMiB"]!, 600, "\(mode) client resident memory (MiB)")
        try maximum(metrics["peakFootprintMiB"]!, 800, "\(mode) sampled client footprint (MiB)")
    }

    private static func maximum(_ value: Double, _ limit: Double, _ label: String) throws {
        guard value.isFinite, value <= limit else {
            throw SmokeFailure.failed("\(label): \(value) exceeds \(limit)")
        }
    }
}
