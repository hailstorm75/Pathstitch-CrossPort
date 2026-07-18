import CryptoKit
import Foundation

enum PathstitchQuickLookRuntimeProbe {
    static let nonceKey = "quicklook.runtimeProbe.nonce"
    private static let evidenceDirectory = "Library/Application Support/Pathstitch/NativeEvidence"

    static func fixtureSha256IfRequested(_ url: URL) -> String? {
        guard activeNonce() != nil,
              let data = try? Data(contentsOf: url, options: .mappedIfSafe)
        else {
            return nil
        }
        return SHA256.hash(data: data)
            .map { String(format: "%02x", $0) }
            .joined()
    }

    static func record(
        providerKind: String,
        bundleIdentifier: String,
        fileURL: URL,
        fixtureSha256: String?,
        rendered: Bool,
        interactiveSceneKit: Bool,
        sceneViewInstalled: Bool,
        cameraControlEnabled: Bool,
        fallbackImageInstalled: Bool,
        vertexCount: Int,
        triangleCount: Int
    ) {
        guard let nonce = activeNonce(),
              let fixtureSha256,
              fixtureSha256.range(of: "^[0-9a-f]{64}$", options: .regularExpression) != nil,
              let defaults = UserDefaults(
                suiteName: PathstitchQuickLookPreferences.appGroupIdentifier),
              let container = FileManager.default.containerURL(
                forSecurityApplicationGroupIdentifier:
                    PathstitchQuickLookPreferences.appGroupIdentifier)
        else {
            return
        }

        let observedPreferences: [String: Bool] = [
            "dxf": defaults.bool(forKey: "quicklook.preview.enabled.dxf"),
            "step": defaults.bool(forKey: "quicklook.preview.enabled.step"),
            "stch": defaults.bool(forKey: "quicklook.preview.enabled.stch"),
        ]
        let payload: [String: Any] = [
            "schemaVersion": 1,
            "status": "passed",
            "providerKind": providerKind,
            "bundleIdentifier": bundleIdentifier,
            "processIdentifier": ProcessInfo.processInfo.processIdentifier,
            "nonce": nonce,
            "fixture": fileURL.lastPathComponent,
            "fixtureSha256": fixtureSha256,
            "observedPreferences": observedPreferences,
            "rendered": rendered,
            "interactiveSceneKit": interactiveSceneKit,
            "sceneViewInstalled": sceneViewInstalled,
            "cameraControlEnabled": cameraControlEnabled,
            "fallbackImageInstalled": fallbackImageInstalled,
            "vertexCount": vertexCount,
            "triangleCount": triangleCount,
            "generatedUtc": ISO8601DateFormatter().string(from: Date()),
        ]

        do {
            let directory = container.appendingPathComponent(
                evidenceDirectory,
                isDirectory: true)
            try FileManager.default.createDirectory(
                at: directory,
                withIntermediateDirectories: true)
            let destination = directory.appendingPathComponent(
                "\(nonce)-\(providerKind).json")
            let data = try JSONSerialization.data(
                withJSONObject: payload,
                options: [.prettyPrinted, .sortedKeys])
            try data.write(to: destination, options: .atomic)
        } catch {
            // Native evidence collector fails closed when attestation is absent.
        }
    }

    private static func activeNonce() -> String? {
        guard
            let defaults = UserDefaults(
                suiteName: PathstitchQuickLookPreferences.appGroupIdentifier),
            let nonce = defaults.string(forKey: nonceKey),
            nonce.range(of: "^[0-9a-f]{32}$", options: .regularExpression) != nil
        else {
            return nil
        }
        return nonce
    }
}

