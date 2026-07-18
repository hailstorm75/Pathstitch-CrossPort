import AppKit
import CoreGraphics
import QuickLookThumbnailing

public final class ThumbnailProvider: QLThumbnailProvider {
    public override func provideThumbnail(
        for request: QLFileThumbnailRequest,
        _ handler: @escaping (QLThumbnailReply?, Error?) -> Void
    ) {
        let renderSize = CGSize(width: 1024, height: 1024)
        let ext = request.fileURL.pathExtension.lowercased()
        guard PathstitchQuickLookPreferences.isEnabled(for: request.fileURL) else {
            handler(nil, nil)
            return
        }

        let fixtureSha256 = PathstitchQuickLookRuntimeProbe.fixtureSha256IfRequested(
            request.fileURL)
        let image: CGImage?
        if ext == "step" || ext == "stp" {
            image = loadStepMesh(url: request.fileURL).flatMap {
                renderStepMeshToImage($0, size: renderSize)
            } ?? renderStepToImage(url: request.fileURL, size: renderSize)
        } else {
            image = renderFileToImage(url: request.fileURL, size: renderSize)
        }

        guard let image else {
            handler(nil, NSError(
                domain: "PathstitchThumbnail",
                code: 1,
                userInfo: [NSLocalizedDescriptionKey:
                    "No renderable geometry was found in \(request.fileURL.lastPathComponent)."]
            ))
            return
        }
        let reply = QLThumbnailReply(contextSize: request.maximumSize) { context in
            let bounds = CGRect(origin: .zero, size: request.maximumSize)
            context.setFillColor(NSColor.white.cgColor)
            context.fill(bounds)
            let scale = min(
                bounds.width / CGFloat(image.width),
                bounds.height / CGFloat(image.height))
            let target = CGSize(
                width: CGFloat(image.width) * scale,
                height: CGFloat(image.height) * scale)
            context.draw(image, in: CGRect(
                x: (bounds.width - target.width) / 2,
                y: (bounds.height - target.height) / 2,
                width: target.width,
                height: target.height
            ))
            return true
        }
        PathstitchQuickLookRuntimeProbe.record(
            providerKind: "thumbnail",
            bundleIdentifier: Bundle(for: ThumbnailProvider.self).bundleIdentifier ?? "",
            fileURL: request.fileURL,
            fixtureSha256: fixtureSha256,
            rendered: true,
            interactiveSceneKit: false,
            sceneViewInstalled: false,
            cameraControlEnabled: false,
            fallbackImageInstalled: true,
            vertexCount: 0,
            triangleCount: 0)
        handler(reply, nil)
    }
}

