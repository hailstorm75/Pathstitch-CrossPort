import AppKit
import Foundation

private enum PathstitchMacBridge {
    static let appGroupIdentifier = "group.com.pathstitch.crossport"
    static let dxfPreviewKey = "quicklook.preview.enabled.dxf"
    static let stepPreviewKey = "quicklook.preview.enabled.step"
    static let stchPreviewKey = "quicklook.preview.enabled.stch"
}

private final class PathstitchDockIconState {
    private static var choice = "automatic"
    private static var appearanceObservation: NSKeyValueObservation?

    static func apply(_ rawChoice: String) -> Bool {
        switch rawChoice.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "light":
            choice = "light"
        case "dark":
            choice = "dark"
        default:
            choice = "automatic"
        }

        appearanceObservation = nil
        if choice == "automatic" {
            appearanceObservation = NSApp.observe(
                \NSApplication.effectiveAppearance,
                options: [.new]
            ) { _, _ in
                DispatchQueue.main.async {
                    _ = refresh()
                }
            }
        }
        return refresh()
    }

    private static func refresh() -> Bool {
        let useDarkIcon: Bool
        switch choice {
        case "light":
            useDarkIcon = false
        case "dark":
            useDarkIcon = true
        default:
            useDarkIcon = NSApp.effectiveAppearance.bestMatch(
                from: [.darkAqua, .aqua]
            ) == .darkAqua
        }

        let resourceName = useDarkIcon ? "AppIconDark" : "AppIconLight"
        guard
            let path = Bundle.main.path(forResource: resourceName, ofType: "png"),
            let image = NSImage(contentsOfFile: path)
        else {
            return false
        }

        NSApp.applicationIconImage = image
        return true
    }
}

@_cdecl("pathstitch_set_quicklook_preferences")
public func pathstitchSetQuickLookPreferences(
    _ dxfEnabled: Int32,
    _ stepEnabled: Int32,
    _ stchEnabled: Int32
) -> Int32 {
    guard let defaults = UserDefaults(
        suiteName: PathstitchMacBridge.appGroupIdentifier
    ) else {
        return 0
    }

    defaults.set(dxfEnabled != 0, forKey: PathstitchMacBridge.dxfPreviewKey)
    defaults.set(stepEnabled != 0, forKey: PathstitchMacBridge.stepPreviewKey)
    defaults.set(stchEnabled != 0, forKey: PathstitchMacBridge.stchPreviewKey)
    return defaults.synchronize() ? 1 : 0
}

@_cdecl("pathstitch_get_quicklook_preference")
public func pathstitchGetQuickLookPreference(
    _ format: Int32
) -> Int32 {
    guard let defaults = UserDefaults(
        suiteName: PathstitchMacBridge.appGroupIdentifier
    ) else {
        return -1
    }

    let key: String
    switch format {
    case 0:
        key = PathstitchMacBridge.dxfPreviewKey
    case 1:
        key = PathstitchMacBridge.stepPreviewKey
    case 2:
        key = PathstitchMacBridge.stchPreviewKey
    default:
        return -1
    }
    guard defaults.object(forKey: key) != nil else { return -1 }
    return defaults.bool(forKey: key) ? 1 : 0

@_cdecl("pathstitch_apply_app_icon")
public func pathstitchApplyAppIcon(
    _ rawChoice: UnsafePointer<CChar>?
) -> Int32 {
    guard let rawChoice else {
        return 0
    }

    let choice = String(cString: rawChoice)
    let applied: Bool
    if Thread.isMainThread {
        applied = PathstitchDockIconState.apply(choice)
    } else {
        applied = DispatchQueue.main.sync {
            PathstitchDockIconState.apply(choice)
        }
    }
    return applied ? 1 : 0
}
