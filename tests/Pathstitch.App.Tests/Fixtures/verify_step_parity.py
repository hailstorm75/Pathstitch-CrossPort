"""Two-path STEP parity evidence: packaged protocol versus legacy pythonOCC APIs."""
from __future__ import annotations

import argparse
import copy
import json
import math
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

import ezdxf

from pathstitch_core.net_unfold import op_unfold_connected
from pathstitch_core.step_ops import op_face_distortion, op_list_bodies, op_project_edges


TOLERANCES = {"lengthAbsolute": 1e-6, "areaAbsolute": 1e-6, "distortionAbsolute": 1e-9}


class ProtocolWorker:
    def __init__(self):
        self.process = subprocess.Popen(
            [sys.executable, "-B", "-m", "pathstitch_core.geometry_worker"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.next_id = 0

    def request(self, operation, payload):
        self.next_id += 1
        raw = json.dumps({"id": self.next_id, "protocolVersion": 1,
                          "operation": operation, "payload": payload}).encode()
        self.process.stdin.write(struct.pack(">I", len(raw)) + raw)
        self.process.stdin.flush()
        size = struct.unpack(">I", self.process.stdout.read(4))[0]
        response = json.loads(self.process.stdout.read(size))
        if not response.get("ok"):
            raise RuntimeError(response.get("error"))
        return response

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=10)


def dxf_summary(path):
    entities = list(ezdxf.readfile(path).modelspace())
    total_length = 0.0
    closed_loops = 0
    counts = {}
    for entity in entities:
        kind = entity.dxftype()
        counts[kind] = counts.get(kind, 0) + 1
        if kind == "LWPOLYLINE":
            points = [(float(p[0]), float(p[1])) for p in entity.get_points("xy")]
            total_length += sum(math.dist(points[i], points[i + 1]) for i in range(len(points) - 1))
            closed = bool(entity.closed) or (len(points) > 2 and math.dist(points[0], points[-1]) <= 1e-7)
            if closed:
                if not entity.closed:
                    total_length += math.dist(points[-1], points[0])
                closed_loops += 1
        elif kind == "CIRCLE":
            total_length += 2.0 * math.pi * float(entity.dxf.radius)
            closed_loops += 1
    return {"entityCounts": counts, "closedLoops": closed_loops, "totalLength": total_length}


def assert_close(label, actual, expected, tolerance):
    deviation = abs(actual - expected)
    if deviation > tolerance:
        raise AssertionError(f"{label}: deviation {deviation} exceeds {tolerance}")
    return deviation


def compare_dxf(label, protocol_path, legacy_path):
    protocol = dxf_summary(protocol_path)
    legacy = dxf_summary(legacy_path)
    if protocol["entityCounts"] != legacy["entityCounts"] or protocol["closedLoops"] != legacy["closedLoops"]:
        raise AssertionError(f"{label}: DXF topology differs: {protocol} vs {legacy}")
    deviation = assert_close(label, protocol["totalLength"], legacy["totalLength"], TOLERANCES["lengthAbsolute"])
    return {"protocol": protocol, "legacy": legacy, "lengthDeviation": deviation}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixtures", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    fixtures = Path(args.fixtures)
    temp = Path(tempfile.mkdtemp(prefix="pathstitch-parity-"))
    worker = ProtocolWorker()
    evidence = {"schemaVersion": 1, "tolerances": TOLERANCES, "projection": {}, "unfold": [],
                "importTopology": [],
                "importAreaMaxDeviation": 0.0, "distortionMaxDeviation": 0.0,
                "acceptedDifferences": [
                    "The packaged response preserves canonical line/circle/ellipse/spline curves; the legacy compatibility artifact stores sampled non-circular display geometry as DXF LWPOLYLINE entities."
                ]}

    def compare_import(label, source, topology):
        legacy_import = op_list_bodies({"input": str(source)})["data"]
        if len(topology["bodies"]) != len(legacy_import["bodies"]):
            raise AssertionError(f"{label}: import body count differs.")
        for body_index, body in enumerate(topology["bodies"]):
            legacy_body = legacy_import["bodies"][body_index]
            if len(body["faces"]) != len(legacy_body["faces"]):
                raise AssertionError(f"{label}: import face count differs for body {body_index}.")
            if len(body["edges"]) != len(legacy_body["edges"]):
                raise AssertionError(f"{label}: import edge count differs for body {body_index}.")
            protocol_surface_kinds = [face["surfaceKind"].lower() for face in body["faces"]]
            legacy_surface_kinds = [face["type"].lower() for face in legacy_body["faces"]]
            if protocol_surface_kinds != legacy_surface_kinds:
                raise AssertionError(
                    f"{label}: import surface kinds differ for body {body_index}: "
                    f"{protocol_surface_kinds} vs {legacy_surface_kinds}")
            evidence["importTopology"].append({
                "fixture": label,
                "bodyIndex": body_index,
                "faceCount": len(body["faces"]),
                "edgeCount": len(body["edges"]),
                "surfaceKinds": protocol_surface_kinds,
            })
            for face_index, face in enumerate(body["faces"]):
                legacy_area = legacy_body["faces"][face_index]["area"]
                deviation = assert_close(
                    f"{label}: import face area", face["area"], legacy_area,
                    TOLERANCES["areaAbsolute"])
                evidence["importAreaMaxDeviation"] = max(
                    evidence["importAreaMaxDeviation"], deviation)

    try:
        box = fixtures / "box-cylinder.step"
        imported = worker.request("import", {"sourcePath": str(box)})
        topology = imported["topology"]
        compare_import("box-cylinder.step", box, topology)

        protocol_projection = temp / "protocol-projection.dxf"
        legacy_projection = temp / "legacy-projection.dxf"
        projection_payload = {"input": str(box), "output": str(protocol_projection),
                              "document_id": topology["documentId"],
                              "visible_body_ids": [body["id"] for body in topology["bodies"]],
                              "visible_bodies": [0, 1], "body_offsets": {}, "body_index": 0,
                              "plane_type": "XY", "offset": 0.0}
        protocol_result = worker.request("project", copy.deepcopy(projection_payload))["data"]
        legacy_payload = copy.deepcopy(projection_payload)
        legacy_payload.update(output=str(legacy_projection))
        for key in ("document_id", "visible_body_ids"):
            legacy_payload.pop(key, None)
        legacy_result = op_project_edges(legacy_payload)["data"]
        evidence["projection"] = compare_dxf("projection", protocol_projection, legacy_projection)
        evidence["projection"]["canonicalCurveKinds"] = sorted({
            curve["kind"] for curve in protocol_result["typedGeometry"]["curves"]})
        if protocol_result["polylines_count"] != legacy_result["polylines_count"]:
            raise AssertionError("Projection curve count differs.")

        analytic = fixtures / "analytic-multibody-hole.step"
        imported = worker.request("import", {"sourcePath": str(analytic)})
        topology = imported["topology"]
        compare_import("analytic-multibody-hole.step", analytic, topology)
        selections = [(0, "plane"), (1, "cylinder"), (2, "cone")]
        hole_face = next(face for face in topology["bodies"][3]["faces"] if len(face["wireIds"]) == 2)
        selections.append((3, hole_face["surfaceKind"]))
        for sequence, (body_index, surface_kind) in enumerate(selections):
            body = topology["bodies"][body_index]
            face = hole_face if body_index == 3 else next(face for face in body["faces"] if face["surfaceKind"] == surface_kind)
            face_index = next(index for index, value in enumerate(body["faces"]) if value["id"] == face["id"])
            protocol_path, legacy_path = temp / f"protocol-unfold-{sequence}.dxf", temp / f"legacy-unfold-{sequence}.dxf"
            common = {"input": str(analytic), "whole_body": False, "distortion_mode": "conformal",
                      "mode": "radial", "decoration": "none"}
            protocol_payload = dict(common, output=str(protocol_path), document_id=topology["documentId"],
                                    face_ids=[face["id"]], visible_body_ids=[body["id"]], faces=[])
            protocol_data = worker.request("unfold", protocol_payload)["data"]
            legacy_data = op_unfold_connected(dict(common, output=str(legacy_path),
                                                    faces=[{"body_index": body_index, "face_index": face_index}]))["data"]
            item = compare_dxf(f"unfold-{surface_kind}-{body_index}", protocol_path, legacy_path)
            for key in ("patches", "faces_unfolded", "fold_edges", "seam_edges"):
                if protocol_data[key] != legacy_data[key]:
                    raise AssertionError(f"Unfold {key} differs.")
            item.update(bodyIndex=body_index, surfaceKind=surface_kind,
                        canonicalCurveKinds=sorted({curve["kind"] for curve in protocol_data["typedGeometry"]["curves"]}))
            evidence["unfold"].append(item)

        freeform = fixtures / "bspline-edge-face.step"
        freeform_topology = worker.request("import", {"sourcePath": str(freeform)})["topology"]
        compare_import("bspline-edge-face.step", freeform, freeform_topology)
        body, face = freeform_topology["bodies"][0], freeform_topology["bodies"][0]["faces"][0]
        protocol_path, legacy_path = temp / "protocol-unfold-freeform.dxf", temp / "legacy-unfold-freeform.dxf"
        common = {"input": str(freeform), "whole_body": False, "distortion_mode": "conformal",
                  "mode": "radial", "decoration": "none"}
        protocol_data = worker.request("unfold", dict(common, output=str(protocol_path),
            document_id=freeform_topology["documentId"], face_ids=[face["id"]],
            visible_body_ids=[body["id"]], faces=[]))["data"]
        legacy_data = op_unfold_connected(dict(common, output=str(legacy_path),
                                                faces=[{"body_index": 0, "face_index": 0}]))["data"]
        item = compare_dxf("unfold-freeform", protocol_path, legacy_path)
        for key in ("patches", "faces_unfolded", "fold_edges", "seam_edges"):
            if protocol_data[key] != legacy_data[key]:
                raise AssertionError(f"Freeform unfold {key} differs.")
        item.update(bodyIndex=0, surfaceKind="bspline-boundary",
                    canonicalCurveKinds=sorted({curve["kind"] for curve in protocol_data["typedGeometry"]["curves"]}))
        evidence["unfold"].append(item)

        face_id = topology["bodies"][0]["faces"][0]["id"]
        protocol_distortion = worker.request("distortion", {"input": str(analytic),
            "document_id": topology["documentId"], "face_id": face_id,
            "body_index": 0, "face_index": 0, "distortion_mode": "conformal"})["data"]["distortion"]
        legacy_distortion = op_face_distortion({"input": str(analytic), "body_index": 0,
                                                "face_index": 0, "distortion_mode": "conformal"})["distortion"]
        if len(protocol_distortion) != len(legacy_distortion):
            raise AssertionError("Distortion sample counts differ.")
        evidence["distortionMaxDeviation"] = max(
            (assert_close("distortion", actual, expected, TOLERANCES["distortionAbsolute"])
             for actual, expected in zip(protocol_distortion, legacy_distortion)), default=0.0)

        Path(args.output).write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    finally:
        worker.close()


if __name__ == "__main__":
    main()
