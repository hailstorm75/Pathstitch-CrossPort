import hashlib
import tempfile
import unittest
from pathlib import Path

import ezdxf

from pathstitch_core.dxf_ops import op_convert_binary_dxf


ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "tests" / "Pathstitch.App.Tests" / "Fixtures" / "binary-dxf" / "r2010-unicode-ocs.dxf"
SIGNATURE = b"AutoCAD Binary DXF"


class BinaryDxfConversionTests(unittest.TestCase):
    def test_binary_to_ascii_preserves_semantics_and_source_bytes(self):
        source_hash = hashlib.sha256(FIXTURE.read_bytes()).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "converted.dxf"
            result = op_convert_binary_dxf({"input": str(FIXTURE), "output": str(output)})

            self.assertEqual("ok", result["status"], result)
            self.assertFalse(output.read_bytes().startswith(SIGNATURE))
            self.assertEqual(source_hash, hashlib.sha256(FIXTURE.read_bytes()).hexdigest())
            document = ezdxf.readfile(output, errors="strict")
            self.assertEqual("AC1024", document.dxfversion)
            self.assertEqual(4, document.units)
            entities = list(document.modelspace())
            self.assertEqual(["LINE", "TEXT"], [entity.dxftype() for entity in entities])
            self.assertEqual("Größe 東京", entities[0].dxf.layer)
            self.assertEqual("opaque-payload", entities[0].get_xdata("VENDOR_TEST")[0].value)
            self.assertEqual("Größe 東京", entities[1].dxf.text)
            self.assertEqual((0.0, 0.0, -1.0), tuple(entities[1].dxf.extrusion))

    def test_conversion_rejects_ascii_and_source_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            ascii_path = Path(directory) / "ascii.dxf"
            document = ezdxf.new("R2010")
            document.saveas(ascii_path)
            result = op_convert_binary_dxf({"input": str(ascii_path), "output": str(Path(directory) / "out.dxf")})
            self.assertEqual("error", result["status"])
            self.assertIn("not an AutoCAD Binary DXF", result["message"])

            overwrite = op_convert_binary_dxf({"input": str(FIXTURE), "output": str(FIXTURE)})
            self.assertEqual("error", overwrite["status"])
            self.assertIn("cannot overwrite", overwrite["message"])


if __name__ == "__main__":
    unittest.main()