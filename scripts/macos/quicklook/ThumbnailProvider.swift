import AppKit
import QuickLookThumbnailing

public final class ThumbnailProvider: QLThumbnailProvider {
    public override func provideThumbnail(
        for request: QLFileThumbnailRequest,
        _ handler: @escaping (QLThumbnailReply?, Error?) -> Void
    ) {
        let size = request.maximumSize
        let ext = request.fileURL.pathExtension.uppercased()
        let reply = QLThumbnailReply(contextSize: size) { context in
            let graphics = NSGraphicsContext(cgContext: context, flipped: false)
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = graphics
            NSColor(calibratedRed: 0.075, green: 0.09, blue: 0.14, alpha: 1).setFill()
            NSBezierPath(roundedRect: NSRect(origin: .zero, size: size), xRadius: 18, yRadius: 18).fill()
            let title = ext.isEmpty ? "STCH" : ext
            let attributes: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: min(size.width, size.height) * 0.20, weight: .bold),
                .foregroundColor: NSColor(calibratedRed: 0.30, green: 0.50, blue: 1, alpha: 1)
            ]
            let text = NSAttributedString(string: title, attributes: attributes)
            let textSize = text.size()
            text.draw(at: NSPoint(x: (size.width - textSize.width) / 2, y: (size.height - textSize.height) / 2))
            NSGraphicsContext.restoreGraphicsState()
            return true
        }
        reply.extensionBadge = ext
        handler(reply, nil)
    }
}
