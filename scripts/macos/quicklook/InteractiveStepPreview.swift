import AppKit
import SceneKit
import simd

func makeInteractiveStepPreview(mesh: StepMeshData, frame: NSRect) -> SCNView? {
    let vertexCount = mesh.vertexCount
    guard vertexCount > 0,
          mesh.interleaved.count >= vertexCount * 6,
          mesh.indices.count >= 3,
          mesh.indices.allSatisfy({ Int($0) < vertexCount }) else {
        return nil
    }

    var rawPositions: [SIMD3<Float>] = []
    rawPositions.reserveCapacity(vertexCount)
    var minimum = SIMD3<Float>(repeating: .greatestFiniteMagnitude)
    var maximum = SIMD3<Float>(repeating: -.greatestFiniteMagnitude)
    for index in 0..<vertexCount {
        let base = index * 6
        let position = SIMD3<Float>(
            mesh.interleaved[base],
            mesh.interleaved[base + 1],
            mesh.interleaved[base + 2])
        rawPositions.append(position)
        minimum = simd_min(minimum, position)
        maximum = simd_max(maximum, position)
    }

    let center = (minimum + maximum) * 0.5
    let extent = maximum - minimum
    let maximumExtent = max(extent.x, max(extent.y, extent.z))
    let scale: Float = maximumExtent > 1e-6 ? 2.0 / maximumExtent : 1.0
    let positions = rawPositions.map { position -> SCNVector3 in
        let normalized = (position - center) * scale
        return SCNVector3(
            CGFloat(normalized.x),
            CGFloat(normalized.y),
            CGFloat(normalized.z))
    }
    let normals = smoothPreviewNormals(
        positions: rawPositions,
        indices: mesh.indices,
        creaseDegrees: 50).map {
            SCNVector3(CGFloat($0.x), CGFloat($0.y), CGFloat($0.z))
        }

    let vertexSource = SCNGeometrySource(vertices: positions)
    let normalSource = SCNGeometrySource(normals: normals)
    let element = SCNGeometryElement(indices: mesh.indices, primitiveType: .triangles)
    let geometry = SCNGeometry(sources: [vertexSource, normalSource], elements: [element])
    let material = SCNMaterial()
    material.diffuse.contents = NSColor(white: 0.78, alpha: 1.0)
    material.lightingModel = .blinn
    material.isDoubleSided = true
    geometry.materials = [material]

    let modelNode = SCNNode(geometry: geometry)
    modelNode.eulerAngles = SCNVector3(-CGFloat.pi / 7, CGFloat.pi / 5, 0)

    let scene = SCNScene()
    scene.rootNode.addChildNode(modelNode)
    addPreviewStudioLighting(to: scene)

    let cameraNode = SCNNode()
    let camera = SCNCamera()
    camera.fieldOfView = 35
    camera.zNear = 0.01
    camera.zFar = 100
    cameraNode.camera = camera
    cameraNode.position = SCNVector3(0, 0, 4)
    scene.rootNode.addChildNode(cameraNode)

    let sceneView = SCNView(frame: frame)
    sceneView.autoresizingMask = [.width, .height]
    sceneView.scene = scene
    sceneView.pointOfView = cameraNode
    sceneView.allowsCameraControl = true
    sceneView.autoenablesDefaultLighting = false
    sceneView.backgroundColor = .white
    sceneView.antialiasingMode = .multisampling4X
    return sceneView
}

private func smoothPreviewNormals(
    positions: [SIMD3<Float>],
    indices: [UInt32],
    creaseDegrees: Float) -> [SIMD3<Float>] {
    let triangleCount = indices.count / 3
    var faceUnit = [SIMD3<Float>](
        repeating: SIMD3<Float>(0, 0, 1),
        count: triangleCount)
    var faceArea = [SIMD3<Float>](
        repeating: SIMD3<Float>(0, 0, 0),
        count: triangleCount)
    for triangle in 0..<triangleCount {
        let first = positions[Int(indices[triangle * 3])]
        let second = positions[Int(indices[triangle * 3 + 1])]
        let third = positions[Int(indices[triangle * 3 + 2])]
        let cross = simd_cross(second - first, third - first)
        faceArea[triangle] = cross
        let length = simd_length(cross)
        if length > 1e-12 {
            faceUnit[triangle] = cross / length
        }
    }

    var trianglesByVertex = [[Int]](repeating: [], count: positions.count)
    for triangle in 0..<triangleCount {
        for offset in 0..<3 {
            trianglesByVertex[Int(indices[triangle * 3 + offset])].append(triangle)
        }
    }

    func positionKey(_ position: SIMD3<Float>) -> Int64 {
        let x = Int64((position.x * 1000).rounded())
        let y = Int64((position.y * 1000).rounded())
        let z = Int64((position.z * 1000).rounded())
        return (x &* 73_856_093) ^ (y &* 19_349_663) ^ (z &* 83_492_791)
    }

    var verticesByPosition: [Int64: [Int]] = [:]
    for vertex in positions.indices where !trianglesByVertex[vertex].isEmpty {
        verticesByPosition[positionKey(positions[vertex]), default: []].append(vertex)
    }

    let threshold = cos(creaseDegrees * .pi / 180)
    var result = [SIMD3<Float>](
        repeating: SIMD3<Float>(0, 0, 1),
        count: positions.count)
    for vertices in verticesByPosition.values {
        var seen = Set<Int>()
        var triangles: [Int] = []
        for vertex in vertices {
            for triangle in trianglesByVertex[vertex] where seen.insert(triangle).inserted {
                triangles.append(triangle)
            }
        }
        guard !triangles.isEmpty else { continue }

        var parent = Array(triangles.indices)
        func root(_ index: Int) -> Int {
            var current = index
            while parent[current] != current {
                parent[current] = parent[parent[current]]
                current = parent[current]
            }
            return current
        }
        if triangles.count > 1 {
            for first in 0..<(triangles.count - 1) {
                for second in (first + 1)..<triangles.count
                    where simd_dot(
                        faceUnit[triangles[first]],
                        faceUnit[triangles[second]]) >= threshold {
                    let firstRoot = root(first)
                    let secondRoot = root(second)
                    if firstRoot != secondRoot {
                        parent[firstRoot] = secondRoot
                    }
                }
            }
        }

        var accumulated: [Int: SIMD3<Float>] = [:]
        for index in triangles.indices {
            accumulated[root(index), default: SIMD3<Float>(0, 0, 0)] +=
                faceArea[triangles[index]]
        }
        var groupNormals: [Int: SIMD3<Float>] = [:]
        for (group, normal) in accumulated {
            let length = simd_length(normal)
            groupNormals[group] = length > 1e-12
                ? normal / length
                : faceUnit[triangles[group]]
        }

        for index in triangles.indices {
            guard let normal = groupNormals[root(index)] else { continue }
            let triangle = triangles[index]
            let corners = [
                Int(indices[triangle * 3]),
                Int(indices[triangle * 3 + 1]),
                Int(indices[triangle * 3 + 2]),
            ]
            for vertex in vertices where corners.contains(vertex) {
                result[vertex] = normal
            }
        }
    }
    return result
}

private func addPreviewStudioLighting(to scene: SCNScene) {
    func light(_ type: SCNLight.LightType, white: CGFloat) -> SCNLight {
        let light = SCNLight()
        light.type = type
        light.color = NSColor(white: white, alpha: 1.0)
        return light
    }

    let ambient = SCNNode()
    ambient.light = light(.ambient, white: 0.28)
    scene.rootNode.addChildNode(ambient)

    let key = SCNNode()
    key.light = light(.directional, white: 0.85)
    key.eulerAngles = SCNVector3(-CGFloat.pi / 4, CGFloat.pi / 6, 0)
    scene.rootNode.addChildNode(key)

    let fill = SCNNode()
    fill.light = light(.directional, white: 0.40)
    fill.eulerAngles = SCNVector3(CGFloat.pi / 3, -CGFloat.pi / 3, 0)
    scene.rootNode.addChildNode(fill)
}