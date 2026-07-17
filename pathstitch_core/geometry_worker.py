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
CAPABILITIES = ["step-import", "step-combine", "brep-topology", "exact-curves", "pcurves", "projection", "unfold", "distortion"]


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


def _document_id(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()[:24]


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
    data = lambda: {"scalars": values, "poles": [], "knots": [], "multiplicities": [], "weights": []}
    if curve.GetType() == GeomAbs_Line:
        line = curve.Line()
        loc, direction = line.Location(), line.Direction()
        values = {"originX": loc.X(), "originY": loc.Y(), "originZ": loc.Z(), "directionX": direction.X(), "directionY": direction.Y(), "directionZ": direction.Z()}
        return "line", first, last, data()
    if curve.GetType() == GeomAbs_Circle:
        circle = curve.Circle()
        center, axis = circle.Location(), circle.Axis().Direction()
        values = {"radius": circle.Radius(), "centerX": center.X(), "centerY": center.Y(), "centerZ": center.Z(), "axisX": axis.X(), "axisY": axis.Y(), "axisZ": axis.Z()}
        return "circle", first, last, data()
    if curve.GetType() == GeomAbs_BSplineCurve:
        spline = curve.BSpline()
        values = {"degree": float(spline.Degree()), "periodic": 1.0 if spline.IsPeriodic() else 0.0, "rational": 1.0 if spline.IsRational() else 0.0}
        result = data()
        result["poles"] = [{"x": spline.Pole(i).X(), "y": spline.Pole(i).Y(), "z": spline.Pole(i).Z()} for i in range(1, spline.NbPoles() + 1)]
        result["knots"] = [float(spline.Knot(i)) for i in range(1, spline.NbKnots() + 1)]
        result["multiplicities"] = [int(spline.Multiplicity(i)) for i in range(1, spline.NbKnots() + 1)]
        result["weights"] = [float(spline.Weight(i)) for i in range(1, spline.NbPoles() + 1)]
        return "bspline", first, last, result
    return "other", first, last, data()


def _pcurve_info(curve):
    name = curve.DynamicType().Name().replace("Geom2d_", "").lower() if hasattr(curve, "DynamicType") else type(curve).__name__.replace("Geom2d_", "").lower()
    data = {"scalars": {}, "poles": [], "knots": [], "multiplicities": [], "weights": []}
    try:
        if "bspline" in name:
            from OCC.Core.Geom2d import Geom2d_BSplineCurve
            spline = Geom2d_BSplineCurve.DownCast(curve)
            data["scalars"] = {"degree": float(spline.Degree()), "periodic": 1.0 if spline.IsPeriodic() else 0.0, "rational": 1.0 if spline.IsRational() else 0.0}
            data["poles"] = [{"x": spline.Pole(i).X(), "y": spline.Pole(i).Y(), "z": 0.0} for i in range(1, spline.NbPoles() + 1)]
            data["knots"] = [float(spline.Knot(i)) for i in range(1, spline.NbKnots() + 1)]
            data["multiplicities"] = [int(spline.Multiplicity(i)) for i in range(1, spline.NbKnots() + 1)]
            data["weights"] = [float(spline.Weight(i)) for i in range(1, spline.NbPoles() + 1)]
        elif "line" in name:
            from OCC.Core.Geom2d import Geom2d_Line
            line = Geom2d_Line.DownCast(curve).Lin2d()
            loc, direction = line.Location(), line.Direction()
            data["scalars"] = {"originX": loc.X(), "originY": loc.Y(), "directionX": direction.X(), "directionY": direction.Y()}
        elif "circle" in name:
            from OCC.Core.Geom2d import Geom2d_Circle
            circle = Geom2d_Circle.DownCast(curve).Circ2d()
            center = circle.Location()
            data["scalars"] = {"centerX": center.X(), "centerY": center.Y(), "radius": circle.Radius()}
    except Exception:
        pass
    return name, data


def _extract_topology(path):
    from OCC.Core.BRep import BRep_Tool
    from OCC.Core.BRepGProp import brepgprop
    from OCC.Core.GProp import GProp_GProps
    from OCC.Core.TopAbs import TopAbs_FACE, TopAbs_EDGE, TopAbs_WIRE, TopAbs_SHELL
    from OCC.Core.TopExp import TopExp_Explorer
    from OCC.Core.TopTools import TopTools_IndexedMapOfShape
    from OCC.Core.TopoDS import topods
    from pathstitch_core.step_ops import load_step_shape, get_solid_bodies

    source_hash = _document_id(path)
    shape = load_step_shape(path)
    body_shapes = get_solid_bodies(shape)
    bodies = []
    for body_index, body in enumerate(body_shapes):
        body_id = f"{source_hash}:body:{body_index}"
        face_map, edge_map, wire_map, shell_map = TopTools_IndexedMapOfShape(), TopTools_IndexedMapOfShape(), TopTools_IndexedMapOfShape(), TopTools_IndexedMapOfShape()
        for shape_type, target in ((TopAbs_FACE, face_map), (TopAbs_EDGE, edge_map), (TopAbs_WIRE, wire_map), (TopAbs_SHELL, shell_map)):
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
        shells = []
        for shell_index in range(1, shell_map.Size() + 1):
            shell = topods.Shell(shell_map.FindKey(shell_index))
            shell_faces = []
            explorer = TopExp_Explorer(shell, TopAbs_FACE)
            while explorer.More():
                face_number = face_map.FindIndex(explorer.Current())
                if face_number > 0:
                    shell_faces.append(face_ids[face_number])
                explorer.Next()
            shells.append({"id": f"{body_id}:shell:{shell_index - 1}", "orientation": _orientation(shell.Orientation()), "faceIds": list(dict.fromkeys(shell_faces))})
        wires = []
        from OCC.Core.BRepTools import BRepTools_WireExplorer
        for wire_index in range(1, wire_map.Size() + 1):
            wire = topods.Wire(wire_map.FindKey(wire_index))
            ordered_edges = []
            explorer = BRepTools_WireExplorer(wire)
            while explorer.More():
                oriented_edge = explorer.Current()
                edge_number = edge_map.FindIndex(oriented_edge)
                if edge_number > 0:
                    ordered_edges.append({"edgeId": edge_ids[edge_number], "orientation": _orientation(oriented_edge.Orientation())})
                explorer.Next()
            wires.append({"id": f"{body_id}:wire:{wire_index - 1}", "orientation": _orientation(wire.Orientation()), "edges": ordered_edges})
        edges = []
        for edge_index in range(1, edge_map.Size() + 1):
            edge = topods.Edge(edge_map.FindKey(edge_index))
            curve_kind, first, last, curve_data = _curve_info(edge)
            pcurves = []
            for face_index in range(1, face_map.Size() + 1):
                if face_ids[face_index] not in adjacency[edge_index]:
                    continue
                try:
                    value = BRep_Tool.CurveOnSurface(edge, topods.Face(face_map.FindKey(face_index)))
                    if isinstance(value, (tuple, list)) and len(value) >= 3 and value[0] is not None:
                        pcurve_kind, pcurve_data = _pcurve_info(value[0])
                        pcurves.append({"faceId": face_ids[face_index], "firstParameter": float(value[-2]), "lastParameter": float(value[-1]), "curveKind": pcurve_kind, "curveData": pcurve_data})
                except Exception:
                    pass
            edges.append({"id": edge_ids[edge_index], "orientation": _orientation(edge.Orientation()), "curveKind": curve_kind, "firstParameter": first, "lastParameter": last, "curveData": curve_data, "adjacentFaceIds": adjacency[edge_index], "pcurves": pcurves})
        bodies.append({"id": body_id, "orientation": _orientation(body.Orientation()), "shells": shells, "faces": faces, "wires": wires, "edges": edges})
    return {"documentId": source_hash, "sourceUnits": _source_units(path), "linearTolerance": 1e-6, "angularTolerance": 1e-9, "bodies": bodies, "diagnostics": [] if bodies else ["No solid, shell, or face bodies were transferred."]}


def _resolve_stable_references(path, payload, operation):
    """Validate document-scoped IDs and adapt them to the legacy index API."""
    topology = _extract_topology(path)
    document_id = topology["documentId"]
    requested_document = payload.get("document_id")
    if requested_document and requested_document != document_id:
        raise ValueError("Stable topology references belong to a different STEP document.")

    bodies = {body["id"]: index for index, body in enumerate(topology["bodies"])}
    faces = {
        face["id"]: (body_index, face_index)
        for body_index, body in enumerate(topology["bodies"])
        for face_index, face in enumerate(body["faces"])
    }

    body_ids = list(payload.get("visible_body_ids") or [])
    if body_ids:
        unknown = [value for value in body_ids if value not in bodies]
        if unknown:
            raise ValueError(f"Unknown stable body reference: {unknown[0]}")
        payload["visible_bodies"] = [bodies[value] for value in body_ids]
    else:
        body_ids = [topology["bodies"][index]["id"] for index in payload.get("visible_bodies") or []
                    if 0 <= index < len(topology["bodies"])]

    face_ids = list(payload.get("face_ids") or [])
    if operation in ("project", "distortion"):
        face_id = payload.get("face_id")
        if face_id:
            if face_id not in faces:
                raise ValueError(f"Unknown stable face reference: {face_id}")
            payload["face_body_index"], payload["face_index"] = faces[face_id]
            face_ids = [face_id]
        elif payload.get("face_index") is not None:
            body_index = payload.get("face_body_index", payload.get("body_index", 0))
            face_index = payload["face_index"]
            if not (0 <= body_index < len(topology["bodies"]) and
                    0 <= face_index < len(topology["bodies"][body_index]["faces"])):
                raise ValueError("Projection face index cannot be mapped to a stable topology reference.")
            face_id = topology["bodies"][body_index]["faces"][face_index]["id"]
            payload["face_id"] = face_id
            face_ids = [face_id]
    elif operation in ("unfold", "unfold_faces"):
        if face_ids:
            unknown = [value for value in face_ids if value not in faces]
            if unknown:
                raise ValueError(f"Unknown stable face reference: {unknown[0]}")
            payload["faces"] = [
                {"body_index": faces[value][0], "face_index": faces[value][1]}
                for value in face_ids
            ]
        elif payload.get("whole_body"):
            face_ids = [face["id"] for body in topology["bodies"] for face in body["faces"]]
        else:
            for item in payload.get("faces") or []:
                body_index, face_index = item.get("body_index"), item.get("face_index")
                if (body_index is not None and face_index is not None and
                        0 <= body_index < len(topology["bodies"]) and
                        0 <= face_index < len(topology["bodies"][body_index]["faces"])):
                    face_ids.append(topology["bodies"][body_index]["faces"][face_index]["id"])
            payload["face_ids"] = face_ids

    if not body_ids:
        body_ids = list(dict.fromkeys(value.rsplit(":face:", 1)[0] for value in face_ids))
    provenance = {"documentId": document_id, "bodyIds": body_ids, "faceIds": face_ids, "edgeIds": []}
    return topology, provenance


def _typed_geometry(operation, data, provenance):
    curves = []
    if operation == "project":
        role = data.get("projection_mode", "projection")
        items = [dict(item, role=role) for item in data.get("exact_curves") or []]
    else:
        items = data.get("geometry") or []

    for index, item in enumerate(items):
        kind = item.get("kind", "polyline")
        geometry = item.get("geometry") or {"scalars": {}, "poles": [], "knots": [], "multiplicities": [], "weights": []}
        approximation = item.get("display_approximation") or item.get("points") or []
        if kind == "circle":
            center = item.get("center") or [0.0, 0.0]
            if "radius" in item:
                geometry["scalars"] = {"centerX": float(center[0]), "centerY": float(center[1]), "radius": float(item.get("radius", 0.0))}
        curves.append({
            "id": f"{provenance['documentId']}:{operation}:curve:{index}",
            "kind": kind,
            "role": item.get("role", operation),
            "closed": bool(item.get("closed", False)),
            "geometry": geometry,
            "displayApproximation": ({"method": "polyline-sampling", "points": [{"x": float(point[0]), "y": float(point[1])} for point in approximation]}
                                     if approximation else None),
            "provenance": provenance,
        })
    loops = [{
        "id": f"{provenance['documentId']}:{operation}:loop:{index}",
        "curveIds": [curve["id"]], "closed": True, "provenance": provenance,
    } for index, curve in enumerate(curves) if curve["closed"]]
    if operation == "unfold" and not loops:
        boundary_ids = [curve["id"] for curve in curves if "seam_cut" in curve["role"]]
        if boundary_ids:
            loops.append({"id": f"{provenance['documentId']}:{operation}:loop:0",
                          "curveIds": boundary_ids, "closed": True, "provenance": provenance})
    uses_canonical_approximation = any(curve["kind"] in ("polyline", "other") for curve in curves)
    return {
        "documentId": provenance["documentId"], "operation": operation,
        "curves": curves, "loops": loops, "provenance": provenance,
        "isApproximation": uses_canonical_approximation,
        "approximationReason": "The source operation could not retain an exact analytic curve for every boundary." if uses_canonical_approximation else None,
    }


def _attach_source_edge_provenance(topology, operation, data, provenance):
    face_ids = set(provenance["faceIds"])
    body_ids = set(provenance["bodyIds"])
    edge_ids = []
    if operation == "unfold":
        edge_ids = [edge_id for body in topology["bodies"] for face in body["faces"]
                    if face["id"] in face_ids for edge_id in face["edgeIds"]]
    elif operation == "project" and data.get("projection_mode") == "silhouette":
        edge_ids = [edge["id"] for body in topology["bodies"] if body["id"] in body_ids for edge in body["edges"]]
    provenance["edgeIds"] = list(dict.fromkeys(edge_ids))


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
        try:
            topology = _extract_topology(path)
        except Exception as exc:
            return _error("invalid-input", "The STEP document could not be parsed or transferred.", str(exc), False)
        if not topology["bodies"]:
            return _error("geometry-not-found", "The STEP document did not contain transferable bodies, shells, or faces.")
        from pathstitch_core.step_ops import op_list_bodies
        viewport = op_list_bodies({"input": path})
        if viewport.get("status") != "ok":
            return _error("backend-failure", viewport.get("message", "Viewport tessellation failed."))
        return {"ok": True, "topology": topology, "viewport": viewport["data"]}
    if operation == "combine":
        from pathstitch_core.step_ops import op_combine_steps
        result = op_combine_steps(payload)
    elif operation == "project":
        from pathstitch_core.step_ops import op_project_edges
        try:
            topology, provenance = _resolve_stable_references(payload.get("input"), payload, operation)
        except ValueError as exc:
            return _error("invalid-input", str(exc))
        result = op_project_edges(payload)
    elif operation in ("unfold", "unfold_faces"):
        from pathstitch_core.net_unfold import op_unfold_connected
        from pathstitch_core.step_ops import op_unfold_faces
        try:
            topology, provenance = _resolve_stable_references(payload.get("input"), payload, operation)
        except ValueError as exc:
            return _error("invalid-input", str(exc))
        result = op_unfold_connected(payload) if operation == "unfold" else op_unfold_faces(payload)
    elif operation == "distortion":
        from pathstitch_core.step_ops import op_face_distortion
        try:
            _resolve_stable_references(payload.get("input"), payload, operation)
        except ValueError as exc:
            return _error("invalid-input", str(exc))
        result = op_face_distortion(payload)
    else:
        return _error("invalid-input", f"Unknown operation: {operation}")
    if result.get("status") != "ok":
        return _error("backend-failure", result.get("message", f"{operation} failed."))
    if operation in ("project", "unfold", "unfold_faces"):
        _attach_source_edge_provenance(topology, operation, result.get("data") or {}, provenance)
        result.setdefault("data", {})["typedGeometry"] = _typed_geometry(operation, result.get("data") or {}, provenance)
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
