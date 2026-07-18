import Foundation

enum PathstitchQuickLookPreferences {
    static let appGroupIdentifier = "group.com.pathstitch.crossport"

    private static let keys = [
        "dxf": "quicklook.preview.enabled.dxf",
        "step": "quicklook.preview.enabled.step",
        "stp": "quicklook.preview.enabled.step",
        "stch": "quicklook.preview.enabled.stch",
    ]

    static func isEnabled(for url: URL) -> Bool {
        guard
            let key = keys[url.pathExtension.lowercased()],
            let defaults = UserDefaults(suiteName: appGroupIdentifier)
        else {
            return true
        }

        guard defaults.object(forKey: key) != nil else {
            return true
        }
        return defaults.bool(forKey: key)
    }
}
