import AppKit
import QuickLookUI

public final class PreviewProvider: NSViewController, QLPreviewingController {
    public override func loadView() {
        let scrollView = NSScrollView(frame: NSRect(x: 0, y: 0, width: 760, height: 560))
        scrollView.hasVerticalScroller = true
        let textView = NSTextView(frame: scrollView.bounds)
        textView.isEditable = false
        textView.isSelectable = true
        textView.font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
        textView.textColor = .labelColor
        textView.backgroundColor = .textBackgroundColor
        scrollView.documentView = textView
        self.view = scrollView
    }

    public func preparePreviewOfFile(at url: URL, completionHandler handler: @escaping (Error?) -> Void) {
        guard let scrollView = view as? NSScrollView,
              let textView = scrollView.documentView as? NSTextView else {
            handler(NSError(domain: "PathstitchQuickLook", code: 1))
            return
        }

        let ext = url.pathExtension.lowercased()
        let attributes = try? FileManager.default.attributesOfItem(atPath: url.path)
        let bytes = (attributes?[.size] as? NSNumber)?.int64Value ?? 0
        var preview = "\(url.lastPathComponent)\n\(bytes) bytes\n\n"
        if ext == "dxf" || ext == "step" || ext == "stp" {
            let data = (try? Data(contentsOf: url, options: .mappedIfSafe)) ?? Data()
            let limited = data.prefix(256 * 1024)
            preview += String(data: limited, encoding: .utf8) ?? "Binary model data"
        } else {
            preview += "Pathstitch project archive\nOpen in Pathstitch to edit its 2D and 3D workspaces."
        }
        textView.string = preview
        handler(nil)
    }
}
