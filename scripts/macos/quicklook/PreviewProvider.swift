import AppKit
import CoreGraphics
import QuickLookUI

public final class PreviewProvider: NSViewController, QLPreviewingController {
    public override func loadView() {
        let imageView = NSImageView(frame: NSRect(x: 0, y: 0, width: 1200, height: 800))
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.wantsLayer = true
        imageView.layer?.backgroundColor = NSColor.white.cgColor
        self.view = imageView
    }

    public func preparePreviewOfFile(at url: URL, completionHandler handler: @escaping (Error?) -> Void) {
        let size = CGSize(width: 1200, height: 800)
        let ext = url.pathExtension.lowercased()
        let image: CGImage?
        if ext == "step" || ext == "stp" {
            image = loadStepMesh(url: url).flatMap { renderStepMeshToImage($0, size: size) }
                ?? renderStepToImage(url: url, size: size)
        } else {
            image = renderFileToImage(url: url, size: size)
        }

        guard let image, let imageView = view as? NSImageView else {
            handler(NSError(
                domain: "PathstitchQuickLook",
                code: 1,
                userInfo: [NSLocalizedDescriptionKey: "No renderable geometry was found in \(url.lastPathComponent)."]
            ))
            return
        }
        imageView.image = NSImage(cgImage: image, size: size)
        handler(nil)
    }
}
