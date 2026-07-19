#!/usr/bin/env python3
"""Regenerate deterministic sewing-hole centers from the original Python engine."""

from __future__ import annotations

import argparse
import json
import sys
import tempfile
from pathlib import Path

import ezdxf

REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT))

from pathstitch_core.dxf_ops import op_add_holes  # noqa: E402


def parameters(**overrides):
    value = {
        "diameter": 1.0,
        "pitch": 5.0,
        "margin": 2.0,
        "cornerMode": "Continuous",
        "cornerClearance": 2.0,
        "avoidanceEnabled": False,
        "avoidanceClearance": 3.0,
        "avoidPathIds": [],
        "symmetricDistribution": True,
        "distributionMode": "Pitch",
        "count": 5,
        "variableSpacingEnabled": False,
        "variableSpacingMin": 4.0,
        "variableSpacingMax": 5.0,
        "pattern": "Single",
        "side": "Left",
        "saddleSpacing": 3.0,
        "offsetCornerFillet": False,
        "proximityFilterEnabled": False,
        "cornerInterpolationEnabled": True,
        "lineProximityFilterEnabled": False,
        "lineProximityThreshold": 1.0,
        "proximityFilterDistance": 3.0,
    }
    value.update(overrides)
    return value


def line(entity_id, start, end, selected=False, keepout=False):
    return {
        "id": entity_id,
        "type": "LINE",
        "points": [start, end],
        "closed": False,
        "selected": selected,
        "keepout": keepout,
    }


def polyline(entity_id, points, closed, selected=False):
    return {"id": entity_id, "type": "LWPOLYLINE", "points": points, "closed": closed, "selected": selected}


def circle(entity_id, center, radius, selected=False):
    return {"id": entity_id, "type": "CIRCLE", "center": center, "radius": radius, "closed": True, "selected": selected}


CASES = [
    {
        "name": "count-open-line",
        "coordinateTolerance": 0.001,
        "entities": [line("source", [0, 0], [20, 0], True)],
        "parameters": parameters(distributionMode="Count", count=5),
    },
    {
        "name": "line-proximity-threshold",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            line("crossing", [10.75, -5], [10.75, 5]),
        ],
        "parameters": parameters(lineProximityFilterEnabled=True, lineProximityThreshold=1.0),
    },
    {
        "name": "nearly-parallel-line",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            line("parallel", [0, 2.75], [20, 2.75]),
        ],
        "parameters": parameters(lineProximityFilterEnabled=True, lineProximityThreshold=1.0),
    },
    {
        "name": "tagged-keepout",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            line("keepout", [10.75, -5], [10.75, 5], keepout=True),
        ],
        "parameters": parameters(
            avoidanceEnabled=True,
            avoidanceClearance=1.0,
            avoidPathIds=["keepout"],
        ),
    },
    {
        "name": "hole-radius-obstacle",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            line("nearby", [10.25, -5], [10.25, 5]),
        ],
        "parameters": parameters(),
    },
    {
        "name": "closed-obstacle",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            polyline("obstacle", [[8, 1], [12, 1], [12, 3], [8, 3]], True),
        ],
        "parameters": parameters(),
    },
    {
        "name": "existing-circle",
        "coordinateTolerance": 0.001,
        "entities": [
            line("source", [0, 0], [20, 0], True),
            circle("hardware", [10, 2], 0.5),
        ],
        "parameters": parameters(proximityFilterDistance=1.0),
    },
    {
        "name": "acute-mitre",
        "coordinateTolerance": 0.1,
        "entities": [polyline("source", [[0, 0], [10, 0], [12, 8]], False, True)],
        "parameters": parameters(pitch=4.0, cornerMode="IncludeCorners"),
    },
    {
        "name": "acute-round",
        "coordinateTolerance": 0.1,
        "entities": [polyline("source", [[0, 0], [10, 0], [12, 8]], False, True)],
        "parameters": parameters(pitch=4.0, cornerMode="IncludeCorners", offsetCornerFillet=True),
    },
    {
        "name": "outer-sharp-count",
        "coordinateTolerance": 0.001,
        "entities": [polyline("source", [[0, 0], [10, 0], [10, 10]], False, True)],
        "parameters": parameters(
            margin=2.0,
            cornerMode="Continuous",
            distributionMode="Count",
            count=3,
            side="Right",
            offsetCornerFillet=False,
        ),
    },
    {
        "name": "outer-round-count",
        "coordinateTolerance": 0.1,
        "entities": [polyline("source", [[0, 0], [10, 0], [10, 10]], False, True)],
        "parameters": parameters(
            margin=2.0,
            cornerMode="Continuous",
            distributionMode="Count",
            count=3,
            side="Right",
            offsetCornerFillet=True,
        ),
    },
    {
        "name": "self-near-proximity",
        "coordinateTolerance": 0.1,
        "entities": [
            polyline("source", [[0, 0], [20, 0], [20, 4], [4, 4], [4, 8], [20, 8]], False, True)
        ],
        "parameters": parameters(
            pitch=2.0,
            margin=1.0,
            cornerMode="IncludeCorners",
            proximityFilterEnabled=True,
            proximityFilterDistance=1.5,
        ),
    },
]


def add_entity(modelspace, specification):
    if specification["type"] == "LINE":
        return modelspace.add_line(specification["points"][0], specification["points"][1])
    if specification["type"] == "LWPOLYLINE":
        return modelspace.add_lwpolyline(specification["points"], close=specification["closed"])
    if specification["type"] == "CIRCLE":
        return modelspace.add_circle(specification["center"], specification["radius"])
    raise ValueError(f"Unsupported entity type: {specification['type']}")


def python_arguments(case, input_path, output_path, handles, keepout_handles):
    p = case["parameters"]
    return {
        "input": str(input_path),
        "output": str(output_path),
        "handles": handles,
        "offset_distance": p["margin"],
        "hole_diameter": p["diameter"],
        "hole_spacing": p["pitch"],
        "distribution": "count" if p["distributionMode"] == "Count" else "spacing",
        "hole_count": p["count"],
        "pattern": p["pattern"].lower(),
        "side": p["side"].lower(),
        "saddle_spacing": p["saddleSpacing"],
        "corner_holes": p["cornerMode"] == "IncludeCorners",
        "offset_corner_fillet": p["offsetCornerFillet"],
        "enable_variable_spacing": p["variableSpacingEnabled"],
        "variable_spacing_min": p["variableSpacingMin"],
        "variable_spacing_max": p["variableSpacingMax"],
        "enable_proximity_filter": p["proximityFilterEnabled"],
        "enable_corner_interpolation": p["cornerInterpolationEnabled"],
        "enable_line_proximity_filter": p["lineProximityFilterEnabled"],
        "line_proximity_threshold": p["lineProximityThreshold"],
        "proximity_filter_distance": p["proximityFilterDistance"],
        "enable_avoidance": p["avoidanceEnabled"],
        "avoidance_radius": p["avoidanceClearance"],
        "keepout_handles": keepout_handles,
    }


def generate_case(case, directory):
    input_path = directory / f"{case['name']}-input.dxf"
    output_path = directory / f"{case['name']}-output.dxf"
    document = ezdxf.new("R2018")
    modelspace = document.modelspace()
    handles = []
    keepout_handles = []
    for specification in case["entities"]:
        entity = add_entity(modelspace, specification)
        if specification.get("selected"):
            handles.append(entity.dxf.handle)
        if specification.get("keepout"):
            keepout_handles.append(entity.dxf.handle)
    document.saveas(input_path)

    result = op_add_holes(python_arguments(case, input_path, output_path, handles, keepout_handles))
    if result.get("status") != "ok":
        raise RuntimeError(f"{case['name']}: {result}")

    generated = ezdxf.readfile(output_path)
    centers = [
        {
            "x": round(entity.dxf.center.x, 6),
            "y": round(entity.dxf.center.y, 6),
            "radius": round(entity.dxf.radius, 6),
        }
        for entity in generated.modelspace().query('CIRCLE[layer=="SEWING_HOLES"]')
    ]
    centers.sort(key=lambda center: (center["x"], center["y"], center["radius"]))
    return {
        "name": case["name"],
        "coordinateTolerance": case["coordinateTolerance"],
        "radiusTolerance": 0.0001,
        "entities": case["entities"],
        "parameters": case["parameters"],
        "expected": centers,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="pathstitch-sewing-goldens-") as temporary:
        directory = Path(temporary)
        payload = {
            "schemaVersion": 1,
            "source": "pathstitch_core.dxf_ops.op_add_holes",
            "cases": [generate_case(case, directory) for case in CASES],
        }
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(payload, indent=2, sort_keys=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
