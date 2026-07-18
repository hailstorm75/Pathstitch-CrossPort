import AppKit
import CoreGraphics
import Foundation

private func inkPixelCount(_ image: CGImage) -> Int {
    let width = image.width
    let height = image.height
    var pixels = [UInt8](repeating: 255, count: width * height * 4)
    let result = pixels.withUnsafeMutableBytes { buffer -> Int in
        guard let base = buffer.baseAddress,
              let context = CGContext(
                data: base,
                width: width,
                height: height,
                bitsPerComponent: 8,
                bytesPerRow: width * 4,
                space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
              ) else { return 0 }
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        let bytes = buffer.bindMemory(to: UInt8.self)
        var count = 0
        for offset in stride(from: 0, to: bytes.count, by: 4) {
            if bytes[offset] < 245 || bytes[offset + 1] < 245 || bytes[offset + 2] < 245 {
                count += 1
            }
        }
        return count
    }
    return result
}

private func writePNG(_ image: CGImage, to url: URL) throws {
    let bitmap = NSBitmapImageRep(cgImage: image)
    guard let data = bitmap.representation(using: .png, properties: [:]) else {
        throw NSError(domain: "PathstitchPreviewSmoke", code: 1)
    }
    try data.write(to: url)
}

@main
private enum PreviewGeometrySmoke {
    static func main() throws {
        guard CommandLine.arguments.count == 5 else {
            throw NSError(domain: "PathstitchPreviewSmoke", code: 2)
        }
        let dxf = URL(fileURLWithPath: CommandLine.arguments[1])
        let step = URL(fileURLWithPath: CommandLine.arguments[2])
        let stch = URL(fileURLWithPath: CommandLine.arguments[3])
        let output = URL(fileURLWithPath: CommandLine.arguments[4], isDirectory: true)
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

        let dxfEntities = DXFParser.parse(url: dxf)
        guard !dxfEntities.isEmpty,
              let dxfImage = renderFileToImage(url: dxf, size: CGSize(width: 512, height: 512)) else {
            throw NSError(domain: "PathstitchPreviewSmoke", code: 3)
        }
        guard let mesh = loadStepMesh(url: step), mesh.vertexCount > 2, mesh.indices.count >= 3,
              let stepImage = renderStepMeshToImage(mesh, size: CGSize(width: 512, height: 512)) else {
            throw NSError(domain: "PathstitchPreviewSmoke", code: 4)
        }
        guard !stchEmbeddedDXF(url: stch).isEmpty,
              let stchImage = renderFileToImage(url: stch, size: CGSize(width: 512, height: 512)) else {
            throw NSError(domain: "PathstitchPreviewSmoke", code: 5)
        }
        let dxfInk = inkPixelCount(dxfImage)
        let stepInk = inkPixelCount(stepImage)
        let stchInk = inkPixelCount(stchImage)
        guard dxfInk > 100, stepInk > 100, stchInk > 100 else {
            throw NSError(domain: "PathstitchPreviewSmoke", code: 6)
        }
        try writePNG(dxfImage, to: output.appendingPathComponent("dxf-preview.png"))
        try writePNG(stepImage, to: output.appendingPathComponent("step-preview.png"))
        try writePNG(stchImage, to: output.appendingPathComponent("stch-preview.png"))
        print("preview geometry ok: dxfEntities=\(dxfEntities.count) dxfInk=\(dxfInk) stepVertices=\(mesh.vertexCount) stepTriangles=\(mesh.indices.count / 3) stepInk=\(stepInk) stchInk=\(stchInk)")
    }
}
