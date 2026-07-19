"""Deterministic PSD import fixture matrix.

Exercises layer flattening, duplicate names, inherited visibility, placement,
vector smart-object routing, empty-layer removal, and flattened fallback without
depending on developer-owned Photoshop files.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

from pathstitch_core import dxf_ops


_PNG = (
    b"\x89PNG\r\n\x1a\n"
    b"\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00"
    b"\x1f\x15\xc4\x89\x00\x00\x00\rIDAT\x08\xd7c\xf8\xcf\xc0\xf0\x1f\x00\x05\x00"
    b"\x01\xff\x89\x99=\x1d\x00\x00\x00\x00IEND\xaeB\x60\x82"
)


class _Image:
    def __init__(self, width: int, height: int, mode: str = "RGBA") -> None:
        self.width = width
        self.height = height
        self.mode = mode

    def convert(self, mode: str) -> "_Image":
        return _Image(self.width, self.height, mode)

    def save(self, path: str) -> None:
        Path(path).write_bytes(_PNG)


class _SmartObject:
    filetype = "svg"
    data = b'<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0L10 0L10 10Z"/></svg>'


class _Layer:
    def __init__(self, spec: dict) -> None:
        self.name = spec.get("name", "")
        self.visible = bool(spec.get("visible", True))
        self.bbox = tuple(spec.get("bbox", [0, 0, 0, 0]))
        self._children = [_Layer(child) for child in spec.get("children", [])]
        image = spec.get("image", [0, 0])
        self._image = _Image(int(image[0]), int(image[1]))
        self.smart_object = _SmartObject() if spec.get("smartSvg") else None

    def __iter__(self):
        return iter(self._children)

    def is_group(self) -> bool:
        return bool(self._children)

    def composite(self) -> _Image:
        return self._image


class _Document:
    def __init__(self, case: dict) -> None:
        self.width = int(case["canvas"][0])
        self.height = int(case["canvas"][1])
        self._children = [_Layer(layer) for layer in case["layers"]]
        self._composite = _Image(
            int(case["composite"][0]),
            int(case["composite"][1]),
            mode="RGB",
        )

    def __iter__(self):
        return iter(self._children)

    def composite(self) -> _Image:
        return self._composite


class _PSDImage:
    document: _Document | None = None

    @classmethod
    def open(cls, _path: str) -> _Document:
        if cls.document is None:
            raise RuntimeError("PSD fixture document was not configured")
        return cls.document


class PsdFixtureMatrixTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        fixture_path = Path(
            os.environ.get(
                "PATHSTITCH_PSD_FIXTURE_MATRIX",
                Path(__file__).resolve().parents[1]
                / "tests"
                / "Pathstitch.App.Tests"
                / "Fixtures"
                / "psd-import-parity-cases.json",
            )
        )
        cls.fixture_path = fixture_path
        cls.matrix = json.loads(fixture_path.read_text(encoding="utf-8"))
        if cls.matrix.get("schemaVersion") != 1:
            raise AssertionError("Unsupported PSD fixture-matrix schema")

    def test_real_layered_fixture(self) -> None:
        try:
            import psd_tools  # noqa: F401
        except ImportError:
            self.skipTest("psd-tools is unavailable in this development runtime")

        source = self.fixture_path.parent / "psd" / "layered-hidden-group.psd"
        with tempfile.TemporaryDirectory() as directory:
            result = dxf_ops.op_parse_psd(
                {"input": os.fspath(source), "out_dir": directory}
            )

            self.assertEqual("ok", result["status"], result)
            data = result["data"]
            self.assertEqual([8.0, 4.0], [data["canvas_width"], data["canvas_height"]])
            self.assertEqual(2, len(data["layers"]))
            hidden, visible = data["layers"]
            self.assertEqual(
                ["Ink", "raster", False, -3.0, 1.0, 2.0, 2.0],
                [
                    hidden["name"],
                    hidden["kind"],
                    hidden["visible"],
                    hidden["center_x"],
                    hidden["center_y"],
                    hidden["width_px"],
                    hidden["height_px"],
                ],
            )
            self.assertEqual(
                ["Ink 2", "raster", True, 1.0, 1.0, 2.0, 2.0],
                [
                    visible["name"],
                    visible["kind"],
                    visible["visible"],
                    visible["center_x"],
                    visible["center_y"],
                    visible["width_px"],
                    visible["height_px"],
                ],
            )
            self.assertTrue(Path(hidden["png_path"]).is_file())
            self.assertTrue(Path(visible["png_path"]).is_file())
    def test_real_flattened_fixture(self) -> None:
        try:
            import psd_tools  # noqa: F401
        except ImportError:
            self.skipTest("psd-tools is unavailable in this development runtime")

        source = self.fixture_path.parent / "psd" / "flattened-2x1.psd"
        with tempfile.TemporaryDirectory() as directory:
            result = dxf_ops.op_parse_psd(
                {"input": os.fspath(source), "out_dir": directory}
            )

        self.assertEqual("ok", result["status"], result)
        data = result["data"]
        self.assertEqual([2.0, 1.0], [data["canvas_width"], data["canvas_height"]])
        layer = self.assert_single(data["layers"])
        self.assertEqual("Layer 1", layer["name"])
        self.assertEqual("raster", layer["kind"])
        self.assertEqual([2.0, 1.0], [layer["width_px"], layer["height_px"]])
        self.assertEqual([0.0, 0.0], [layer["center_x"], layer["center_y"]])

    def test_real_malformed_fixture_fails_cleanly(self) -> None:
        try:
            import psd_tools  # noqa: F401
        except ImportError:
            self.skipTest("psd-tools is unavailable in this development runtime")

        source = self.fixture_path.parent / "psd" / "malformed-truncated.psd"
        with tempfile.TemporaryDirectory() as directory:
            result = dxf_ops.op_parse_psd(
                {"input": os.fspath(source), "out_dir": directory}
            )

        self.assertEqual("error", result["status"])
        self.assertIn("Failed to parse PSD", result["message"])

    def assert_single(self, values: list[dict]) -> dict:
        self.assertEqual(1, len(values))
        return values[0]

    def test_fixture_matrix(self) -> None:
        psd_tools = types.ModuleType("psd_tools")
        psd_tools.PSDImage = _PSDImage
        vector_entities = [
            {
                "type": "LWPOLYLINE",
                "vertices": [[0.0, 0.0], [10.0, 0.0], [10.0, 10.0]],
                "closed": True,
            }
        ]

        for case in self.matrix["cases"]:
            with self.subTest(case=case["name"]), tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / "fixture.psd"
                source.write_bytes(b"8BPS")
                output = Path(directory) / "output"
                _PSDImage.document = _Document(case)

                with (
                    patch.dict(sys.modules, {"psd_tools": psd_tools}),
                    patch.object(
                        dxf_ops,
                        "_psd_svg_to_entities",
                        return_value=vector_entities,
                    ),
                ):
                    result = dxf_ops.op_parse_psd(
                        {"input": os.fspath(source), "out_dir": os.fspath(output)}
                    )

                self.assertEqual("ok", result["status"], result)
                data = result["data"]
                self.assertEqual(case["canvas"], [data["canvas_width"], data["canvas_height"]])
                self.assertEqual(
                    case["composite"],
                    [data["composite_width"], data["composite_height"]],
                )
                self.assertTrue(Path(data["composite_png_path"]).is_file())
                self.assertEqual(len(case["expected"]), len(data["layers"]))

                for expected, actual in zip(case["expected"], data["layers"], strict=True):
                    self.assertEqual(expected["name"], actual["name"])
                    self.assertEqual(expected["kind"], actual["kind"])
                    self.assertEqual(expected["visible"], actual["visible"])
                    if actual["kind"] == "raster":
                        self.assertEqual(
                            expected["center"],
                            [actual["center_x"], actual["center_y"]],
                        )
                        self.assertEqual(
                            expected["size"],
                            [actual["width_px"], actual["height_px"]],
                        )
                        self.assertTrue(Path(actual["png_path"]).is_file())
                    else:
                        self.assertEqual(expected["entityCount"], len(actual["entities"]))


if __name__ == "__main__":
    unittest.main()
