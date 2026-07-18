import AppKit
import CoreGraphics
import QuickLookUI

public final class PreviewProvider: NSViewController, QLPreviewingController {
    public override func loadView() {
        let root = NSView(frame: NSRect(x: 0, y: 0, width: 1200, height: 800))
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor.white.cgColor
        self.view = root
    }

    public func preparePreviewOfFile(
        at url: URL,
        completionHandler handler: @escaping (Error?) -> Void) {
        let size = CGSize(width: 1200, height: 800)
        let ext = url.pathExtension.lowercased()
        guard PathstitchQuickLookPreferences.isEnabled(for: url) else {
            handler(NSError(
                domain: "PathstitchQuickLook",
                code: 2,
                userInfo: [NSLocalizedDescriptionKey:
                    "Pathstitch previews are disabled for ." + ext + " files."]
            ))
            return
        }

        let isAccessing = url.startAccessingSecurityScopedResource()
        let isStep = ext == "step" || ext == "stp"
        let mesh = isStep ? loadStepMesh(url: url) : nil
        let fallbackImage: CGImage?
        if let mesh {
            fallbackImage = renderStepMeshToImage(mesh, size: size)
                ?? renderStepToImage(url: url, size: size)
        } else if isStep {
            fallbackImage = renderStepToImage(url: url, size: size)
        } else {
            fallbackImage = renderFileToImage(url: url, size: size)
        }
        if isAccessing {
            url.stopAccessingSecurityScopedResource()
        }

        guard mesh is not nil || fallbackImage is not nil else {
            handler(NSError(
                domain: "PathstitchQuickLook",
                code: 1,
                userInfo: [NSLocalizedDescriptionKey:
                    "No renderable geometry was found in " + url.lastPathComponent + "."]
            ))
            return
        }

        DispatchQueue.main.async {
            if let mesh,
               let sceneView = makeInteractiveStepPreview(mesh: mesh, frame: self.view.bounds) {
                self.replaceContent(with: sceneView)
                handler(nil)
                return
            }
            guard let fallbackImage else {
                handler(NSError(
                    domain: "PathstitchQuickLook",
                    code: 3,
                    userInfo: [NSLocalizedDescriptionKey:
                        "Interactive STEP preview could not be initialized."]
                ))
                return
            }
            let imageView = NSImageView(frame: self.view.bounds)
            imageView.autoresizingMask = [.width, .height]
            imageView.imageScaling = .scaleProportionallyUpOrDown
            imageView.image = NSImage(cgImage: fallbackImage, size: size)
            self.replaceContent(with: imageView)
            handler(nil)
        }
    }

    private func replaceContent(with content: NSView) {
        view.subviews.forEach { $0.removeFromSuperview() }
        content.frame = view.bounds
        content.autoresizingMask = [.width, .height]
        view.addSubview(content)
    }
}