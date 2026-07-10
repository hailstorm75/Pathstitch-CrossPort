"""Versioned, STEP/B-rep-only Pathstitch geometry worker.

Protocol v1 uses 4-byte big-endian length-prefixed UTF-8 JSON frames. No OCC or
Python implementation types cross the boundary. The worker writes protocol frames
to a duplicate of stdout and redirects fd 1 to stderr before importing OCC.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import re
import struct
import sys
import traceback
from typing import Any, Dict

PROTOCOL_VERSION = 1
CAPABILITIES = ["step-import", "brep-topology", "exact-curves", "pcurves", "projection", "unfold", "distortion"]


def _read_exact(stream, count):
    data = bytearray()
    while len(data) < count:
        chunk = stream.read(count - len(data))
        if not chunk:
            return None
        data.extend(chunk)
    return bytes(data)


def _write(stream, value):
    data = json.dumps(value, separators=(",", ":"), allow_nan=False).encode("utf-8")
    stream.write(struct.pack(">I", len(data)))
    stream.write(data)
    stream.flush()


def _error(code, message, diagnostic=None, retryable=False):
    return {"ok": False, "error": {"code": code, "message": message, "diagnostic": diagnostic, "retryable": retryable}}


def _orientation(value):
    return {0: "forward", 1: "reversed", 2: "internal", 3: "external"}.get(int(value), "unknown")


def _source_units(path):
    try:
        text = open(path, "r", encoding="ascii", errors="ignore").read(262144).upper()
        if "SI_UNIT(.MILLI.,.METRE.)" in text:
            return "mm"
        if "SI_UNIT($,.METRE.)" in text:
            return "m"
        if "CONVERSION_BASED_UNIT('INCH'" in text or "CONVERSION_BASED_UNIT(\"INCH\"" in text:
            return "in"
    except Exception:
        pass
    return "unknown"


def _surface_info(face):
    from OCC.Core.BRepAdaptor import BRepAdaptor_Surface
    from OCC.Core.GeomAbs import GeomAbs_Plane, GeomAbs_Cylinder, GeomAbs_Cone, GeomAbs_BSplineSurface
    surface = BRepAdaptor_Surface(face, True)
    kind = surface.GetType()
    values: Dict[str, float] = {}
    if kind == GeomAbs_Plane:
        plane = surface.Plane()
        loc, normal = plane.Location(), plane.Axis().Direction()
        values = {"originX": loc.X(), "originY": loc.Y(), "originZ": loc.Z(), "normalX": normal.X(), "normalY": normal.Y(), "normalZ": normal.Z()}
        return "plane", values
    if kind == GeomAbs_Cylinder:
        cylinder = surface.Cylinder()
        loc, axis = cylinder.Location(), cylinder.Axis().Direction()
        values = {"radius": cylinder.Radius(), "originX": loc.X(), "originY": loc.Y(), "originZ": loc.Z(), "axisX": axis.X(), "axisY": axis.Y(), "axisZ": axis.Z()}
        return "cylinder", values
    if kind == GeomAbs_Cone:
        cone = surface.Cone()
        loc, axis = cone.Location(), cone.Axis().Direction()
        values = {"referenceRadius": cone.RefRadius(), "semiAngle": cone.SemiAngle(), "originX": loc.X(), "originY": loc.Y(), "originZ": loc.Z(), "axisX": axis.X(), "axisY": axis.Y(), "axisZ": axis.Z()}
        return "cone", values
    if kind == GeomAbs_BSplineSurface:
        bspline = surface.BSpline()
        return "bspline", {"uDegree": float(bspline.UDegree()), "vDegree": float(bspline.VDegree()), "uPoles": float(bspline.NbUPoles()), "vPoles": float(bspline.NbVPoles())}
    return "other", values


def _curve_info(edge):
    from OCC.Core.BRepAdaptor import BRepAdaptor_Curve
    from OCC.Core.GeomAbs import GeomAbs_Line, GeomAbs_Circle, GeomAbs_BSplineCurve
    curve = BRepAdaptor_Curve(edge)
    first, last = float(curve.FirstParameter()), float(curve.LastParameter())
    values: Dict[str, float] = {}
    if curve.GetType() == GeomAbs_Line:
        line = curve.Line()
        loc, direction = line.Location(), line.Direction()
        values = {"originX": loc.X(), "originY": loc.Y(), "originZ": loc.Z(), "directionX": direction.X(), "directionY": direction.Y(), "directionZ": direction.Z()}
        return "line", first, last, values
    if curve.GetType() == GeomAbs_Circle:
        circle = curve.Circle()
        center, axis = circle.Location(), circle.Axis().Direction()
        values = {"radius": circle.Radius(), "centerX": center.X(), "centerY": center.Y(), "centerZ": center.Z(), "axisX": axis.X(), "axisY": axis.Y(), "axisZ": axis.Z()}
        return "circle", first, last, values
    if curve.GetType() == GeomAbs_BSplineCurve:
        spline = curve.BSpline()
        values = {"degree": float(spline.Degree()), "poles": float(spline.NbPoles()), "knots": float(spline.NbKnots())}
        return "bspline", first, last, values
    return "other", first, last, values


def _extract_topology(path):
    from OCC.Core.BRep import BRep_Tool
    from OCC.Core.BRepGProp import brepgprop
    from OCC.Core.GProp import GProp_GProps
    from OCC.Core.TopAbs import TopAbs_FACE, TopAbs_EDGE, TopAbs_WIRE
    from OCC.Core.TopExp import TopExp_Explorer
    from OCC.Core.TopTools import TopTools_IndexedMapOfShape
    from OCC.Core.TopoDS import topods
    from pathstitch_core.step_ops import load_step_shape, get_solid_bodies

    source_hash = hashlib.sha256(open(path, "rb").read()).hexdigest()[:24]
    shape = load_step_shape(path)
    body_shapes = get_solid_bodies(shape)
    bodies = []
    for body_index, body in enumerate(body_shapes):
        body_id = f"{source_hash}:body:{body_index}"
        face_map, edge_map, wire_map = TopTools_IndexedMapOfShape(), TopTools_IndexedMapOfShape(), TopTools_IndexedMapOfShape()
        for shape_type, target in ((TopAbs_FACE, face_map), (TopAbs_EDGE, edge_map), (TopAbs_WIRE, wire_map)):
            explorer = TopExp_Explorer(body, shape_type)
            while explorer.More():
                target.Add(explorer.Current())
                explorer.Next()
        face_ids = {index: f"{body_id}:face:{index - 1}" for index in range(1, face_map.Size() + 1)}
        edge_ids = {index: f"{body_id}:edge:{index - 1}" for index in range(1, edge_map.Size() + 1)}
        adjacency = {index: [] for index in range(1, edge_map.Size() + 1)}
        faces = []
        for face_index in range(1, face_map.Size() + 1):
            face = topods.Face(face_map.FindKey(face_index))
            edge_refs, wire_refs = [], []
            edge_exp = TopExp_Explorer(face, TopAbs_EDGE)
            while edge_exp.More():
                edge_number = edge_map.FindIndex(edge_exp.Current())
                if edge_number > 0:
                    edge_refs.append(edge_ids[edge_number])
                    adjacency[edge_number].append(face_ids[face_index])
                edge_exp.Next()
            wire_exp = TopExp_Explorer(face, TopAbs_WIRE)
            while wire_exp.More():
                wire_number = wire_map.FindIndex(wire_exp.Current())
                if wire_number > 0:
                    wire_refs.append(f"{body_id}:wire:{wire_number - 1}")
                wire_exp.Next()
            surface_kind, surface_parameters = _surface_info(face)
            props = GProp_GProps()
            brepgprop.SurfaceProperties(face, props)
            faces.append({"id": face_ids[face_index], "orientation": _orientation(face.Orientation()), "surfaceKind": surface_kind, "surfaceParameters": surface_parameters, "wireIds": list(dict.fromkeys(wire_refs)), "edgeIds": list(dict.fromkeys(edge_refs)), "area": float(props.Mass())})
        edges = []
        for edge_index in range(1, edge_map.Size() + 1):
            edge = topods.Edge(edge_map.FindKey(edge_index))
            curve_kind, first, last, curve_parameters = _curve_info(edge)
            pcurves = []
            for face_index in range(1, face_map.Size() + 1):
                if face_ids[face_index] not in adjacency[edge_index]:
                    continue
                try:
                    value = BRep_Tool.CurveOnSurface(edge, topods.Face(face_map.FindKey(face_index)))
                    if isinstance(value, (tuple, list)) and len(value) >= 3 and value[0] is not None:
                        pcurves.append({"faceId": face_ids[face_index], "firstParameter": float(value[-2]), "lastParameter": float(value[-1]), "curveKind": type(value[0]).__name__.replace("Geom2d_", "").lower()})
                except Exception:
                    pass
            edges.append({"id": edge_ids[edge_index], "orientation": _orientation(edge.Orientation()), "curveKind": curve_kind, "firstParameter": first, "lastParameter": last, "curveParameters": curve_parameters, "adjacentFaceIds": adjacency[edge_index], "pcurves": pcurves})
        bodies.append({"id": body_id, "orientation": _orientation(body.Orientation()), "faces": faces, "edges": edges})
    return {"documentId": source_hash, "sourceUnits": _source_units(path), "linearTolerance": 1e-6, "angularTolerance": 1e-9, "bodies": bodies, "diagnostics": [] if bodies else ["No solid, shell, or face bodies were transferred."]}


def _dispatch(operation, payload):
    if operation == "handshake":
        try:
            from OCC.Core.Standard import Standard_Version
            backend_version = str(Standard_Version())
        except Exception:
            backend_version = "pythonocc-core"
        return {"ok": True, "protocol": {"protocolVersion": PROTOCOL_VERSION, "backend": "packaged-pythonocc-occt", "backendVersion": backend_version, "capabilities": CAPABILITIES}}
    if operation == "import":
        path = payload.get("sourcePath")
        if not path or not os.path.isfile(path):
            return _error("source-unavailable", "STEP source file is unavailable.")
        topology = _extract_topology(path)
        from pathstitch_core.step_ops import op_list_bodies
        viewport = op_list_bodies({"input": path})
        if viewport.get("status") != "ok":
            return _error("backend-failure", viewport.get("message", "Viewport tessellation failed."))
        return {"ok": True, "topology": topology, "viewport": viewport["data"]}
    if operation == "project":
        from pathstitch_core.step_ops import op_project_edges
        result = op_project_edges(payload)
    elif operation == "unfold":
        from pathstitch_core.net_unfold import op_unfold_connected
        result = op_unfold_connected(payload)
    elif operation == "distortion":
        from pathstitch_core.step_ops import op_face_distortion
        result = op_face_distortion(payload)
    else:
        return _error("invalid-input", f"Unknown operation: {operation}")
    if result.get("status") != "ok":
        return _error("backend-failure", result.get("message", f"{operation} failed."))
    return {"ok": True, "data": result.get("data", result)}


def serve():
    real_stdout = os.dup(1)
    os.dup2(2, 1)
    frame_out = os.fdopen(real_stdout, "wb")
    frame_in = sys.stdin.buffer
    while True:
        header = _read_exact(frame_in, 4)
        if header is None:
            return
        size = struct.unpack(">I", header)[0]
        raw = _read_exact(frame_in, size)
        if raw is None:
            return
        request_id = None
        try:
            request = json.loads(raw.decode("utf-8"))
            request_id = request.get("id")
            if request.get("protocolVersion") != PROTOCOL_VERSION:
                response = _error("protocol-mismatch", f"Worker requires protocol {PROTOCOL_VERSION}.")
            else:
                response = _dispatch(request.get("operation"), request.get("payload") or {})
        except Exception as exc:
            response = _error("backend-failure", str(exc), traceback.format_exc(), True)
        response["id"] = request_id
        response["protocolVersion"] = PROTOCOL_VERSION
        _write(frame_out, response)


if __name__ == "__main__":
    serve()
