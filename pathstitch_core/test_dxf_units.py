import math
import tempfile
import unittest
from pathlib import Path

import ezdxf

from pathstitch_core.dxf_ops import dxf_units_info, op_append_dxf, op_export_dxf
from pathstitch_core.dxf_units import (
    INSUNITS_NAME,
    INSUNITS_TO_MM,
    new_millimeter_dxf,
    require_millimeter_dxf,
)


US_SURVEY_FOOT_TO_MM = (1200.0 / 3937.0) * 1000.0

EXPECTED_INSUNITS_TO_MM = {
    1: 25.4,
    2: 304.8,
    3: 1_609_344.0,
    4: 1.0,
    5: 10.0,
    6: 1_000.0,
    7: 1_000_000.0,
    8: 0.000_025_4,
    9: 0.0254,
    10: 914.4,
    11: 0.000_000_1,
    12: 0.000_001,
    13: 0.001,
    14: 100.0,
    15: 10_000.0,
    16: 100_000.0,
    17: 1_000_000_000_000.0,
    18: 149_597_870_700_000.0,
    19: 9_460_730_472_580_800_000.0,
    20: 30_856_775_812_800_000_000.0,
    21: US_SURVEY_FOOT_TO_MM,
    22: US_SURVEY_FOOT_TO_MM / 12.0,
    23: US_SURVEY_FOOT_TO_MM * 3.0,
    24: US_SURVEY_FOOT_TO_MM * 5280.0,
}


class DxfUnitContractTests(unittest.TestCase):
    def test_insunits_catalog_covers_official_codes_1_through_24(self):
        self.assertEqual(set(INSUNITS_TO_MM), set(range(1, 25)))
        self.assertEqual(set(INSUNITS_NAME), set(range(25)))
        for code, expected in EXPECTED_INSUNITS_TO_MM.items():
            self.assertTrue(
                math.isclose(INSUNITS_TO_MM[code], expected, rel_tol=1e-12, abs_tol=1e-12),
                f"INSUNITS {code}: expected {expected}, got {INSUNITS_TO_MM[code]}",
            )

    def test_dxf_units_info_reports_survey_units_without_mutation(self):
        doc = ezdxf.new("R2010")
        doc.header["$INSUNITS"] = 21

        code, name, factor = dxf_units_info(doc)

        self.assertEqual(code, 21)
        self.assertEqual(name, "US survey feet")
        self.assertTrue(math.isclose(factor, US_SURVEY_FOOT_TO_MM, rel_tol=1e-12))
        self.assertEqual(doc.header["$INSUNITS"], 21)

    def test_new_millimeter_dxf_persists_canonical_headers(self):
        with tempfile.TemporaryDirectory(prefix="pathstitch-dxf-units-") as directory:
            path = Path(directory) / "metric-output.dxf"
            doc = new_millimeter_dxf("R2010")
            doc.modelspace().add_line((0, 0), (50.8, 0))
            doc.saveas(path)

            reopened = ezdxf.readfile(path)

            self.assertEqual(reopened.header["$INSUNITS"], 4)
            self.assertEqual(reopened.header["$MEASUREMENT"], 1)
            line = next(iter(reopened.modelspace()))
            self.assertTrue(math.isclose(line.dxf.end.x - line.dxf.start.x, 50.8, abs_tol=1e-12))

    def test_append_contract_accepts_millimeters_and_rejects_other_units(self):
        metric = ezdxf.new("R2010")
        metric.header["$INSUNITS"] = 4
        metric.header["$MEASUREMENT"] = 0
        self.assertIs(require_millimeter_dxf(metric), metric)
        self.assertEqual(metric.header["$MEASUREMENT"], 1)

        for code in (0, 1, 21, 25):
            other = ezdxf.new("R2010")
            other.header["$INSUNITS"] = code
            with self.assertRaisesRegex(ValueError, "not millimetres"):
                require_millimeter_dxf(other)


    def test_export_dxf_declares_internal_coordinates_as_millimeters(self):
        with tempfile.TemporaryDirectory(prefix="pathstitch-dxf-export-") as directory:
            source = Path(directory) / "source.dxf"
            output = Path(directory) / "output.dxf"
            doc = ezdxf.new("R2010")
            doc.header["$INSUNITS"] = 0
            doc.modelspace().add_line((0, 0), (50.8, 0))
            doc.saveas(source)

            result = op_export_dxf({"input": str(source), "output": str(output)})
            reopened = ezdxf.readfile(output)

            self.assertEqual(result["status"], "ok")
            self.assertEqual(reopened.header["$INSUNITS"], 4)
            self.assertEqual(reopened.header["$MEASUREMENT"], 1)
            line = next(iter(reopened.modelspace()))
            self.assertTrue(math.isclose(line.dxf.end.x - line.dxf.start.x, 50.8, abs_tol=1e-12))

    def test_append_dxf_converts_secondary_declared_units_to_primary_units(self):
        with tempfile.TemporaryDirectory(prefix="pathstitch-dxf-append-") as directory:
            primary_path = Path(directory) / "primary-inch.dxf"
            secondary_path = Path(directory) / "secondary-mm.dxf"
            output = Path(directory) / "merged.dxf"
            primary = ezdxf.new("R2010")
            primary.header["$INSUNITS"] = 1
            primary.modelspace().add_line((0, 0), (2, 0))
            primary.saveas(primary_path)
            secondary = new_millimeter_dxf("R2010")
            secondary.modelspace().add_line((0, 0), (25.4, 0))
            secondary.saveas(secondary_path)

            result = op_append_dxf({
                "primary": str(primary_path),
                "secondary": str(secondary_path),
                "output": str(output),
            })
            reopened = ezdxf.readfile(output)
            lengths = sorted(
                abs(entity.dxf.end.x - entity.dxf.start.x)
                for entity in reopened.modelspace()
                if entity.dxftype() == "LINE"
            )

            self.assertEqual(result["status"], "ok")
            self.assertEqual(reopened.header["$INSUNITS"], 1)
            self.assertEqual(len(lengths), 2)
            self.assertTrue(math.isclose(lengths[0], 1.0, rel_tol=1e-12))
            self.assertTrue(math.isclose(lengths[1], 2.0, rel_tol=1e-12))

    def test_append_dxf_rejects_unitless_input(self):
        with tempfile.TemporaryDirectory(prefix="pathstitch-dxf-append-") as directory:
            primary_path = Path(directory) / "primary.dxf"
            secondary_path = Path(directory) / "secondary.dxf"
            output = Path(directory) / "merged.dxf"
            primary = ezdxf.new("R2010")
            primary.header["$INSUNITS"] = 0
            primary.saveas(primary_path)
            secondary = new_millimeter_dxf("R2010")
            secondary.saveas(secondary_path)

            result = op_append_dxf({
                "primary": str(primary_path),
                "secondary": str(secondary_path),
                "output": str(output),
            })

            self.assertEqual(result["status"], "error")
            self.assertIn("unitless", result["message"])
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()