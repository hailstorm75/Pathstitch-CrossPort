"""Portable OCCT parity checks for OBJ/STL routing and connected mesh nets."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import ezdxf

from pathstitch_core.geometry_worker import _extract_topology
from pathstitch_core.net_unfold import op_unfold_connected


_TWO_TRIANGLE_OBJ = """\
v 0 0 0
v 10 0 0
v 10 10 0
v 0 10 0
f 1 2 3
f 1 3 4
"""

_TWO_TRIANGLE_STL = """\
solid square
facet normal 0 0 1
outer loop
vertex 0 0 0
vertex 10 0 0
vertex 10 10 0
endloop
endfacet
facet normal 0 0 1
outer loop
vertex 0 0 0
vertex 10 10 0
vertex 0 10 0
endloop
endfacet
endsolid square
"""


class MeshWorkerParityTests(unittest.TestCase):
    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory(prefix="pathstitch-mesh-parity-")
        root = Path(self._temporary.name)
        self.obj_path = root / "square.obj"
        self.stl_path = root / "square.stl"
        self.obj_path.write_text(_TWO_TRIANGLE_OBJ, encoding="utf-8")
        self.stl_path.write_text(_TWO_TRIANGLE_STL, encoding="ascii")

    def tearDown(self):
        self._temporary.cleanup()

    def _unfold(self, name, **overrides):
        payload = {
            "input": str(self.obj_path),
            "output": str(Path(self._temporary.name) / name),
            "whole_body": True,
            "mode": "radial",
            "decoration": "none",
        }
        payload.update(overrides)
        result = op_unfold_connected(payload)
        self.assertEqual("ok", result.get("status"), result)
        output = ezdxf.readfile(result["data"]["output"])
        self.assertEqual(4, output.header["$INSUNITS"])
        self.assertEqual(1, output.header["$MEASUREMENT"])
        return result["data"]

    def test_obj_and_stl_import_share_stable_sewn_topology(self):
        for path in (self.obj_path, self.stl_path):
            first = _extract_topology(str(path))
            second = _extract_topology(str(path))
            self.assertEqual(first["documentId"], second["documentId"])
            body = self.assert_single(first["bodies"])
            self.assertEqual(2, len(body["faces"]))
            self.assertEqual(5, len(body["edges"]))
            self.assertEqual([1, 1, 1, 1, 2], sorted(len(edge["adjacentFaceIds"]) for edge in body["edges"]))

    def test_connected_obj_net_and_manual_cut_change_patch_graph(self):
        connected = self._unfold("connected.dxf")
        self.assertEqual(1, connected["patches"])
        self.assertEqual(2, connected["faces_unfolded"])
        self.assertEqual(1, connected["fold_edges"])

        cut = self._unfold(
            "cut.dxf",
            forced_seams=[{"body_index": 0, "edge_index": 3}],
        )
        self.assertEqual(2, cut["patches"])
        self.assertEqual(0, cut["fold_edges"])

    def test_global_and_per_edge_decorations_reach_mesh_output_layers(self):
        forced = [{"body_index": 0, "edge_index": 3}]
        tabs = self._unfold(
            "tabs.dxf",
            decoration="tabs",
            tab_height=2.0,
            forced_seams=forced,
        )
        tab_layers = {entity.dxf.layer for entity in ezdxf.readfile(tabs["output"]).modelspace()}
        self.assertIn("GLUE_TABS", tab_layers)

        holes = self._unfold(
            "holes.dxf",
            decoration="none",
            hole_diameter=1.0,
            hole_spacing=3.0,
            hole_margin=1.0,
            forced_seams=forced,
            seam_decorations=[{"body_index": 0, "edge_index": 3, "decoration": "holes"}],
        )
        hole_layers = {entity.dxf.layer for entity in ezdxf.readfile(holes["output"]).modelspace()}
        self.assertIn("SEW_HOLES", hole_layers)

    def assert_single(self, values):
        self.assertEqual(1, len(values))
        return values[0]


if __name__ == "__main__":
    unittest.main()