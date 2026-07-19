"""Shared DXF length-unit contract.

Generated geometry uses millimetres. INSUNITS codes follow Autodesk's published
0-24 table; U.S. survey units use the exact historical 1200/3937 metre foot.
"""

from __future__ import annotations

from typing import Any

import ezdxf

US_SURVEY_FOOT_TO_MM = (1200.0 / 3937.0) * 1000.0

INSUNITS_TO_MM = {
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

INSUNITS_NAME = {
    0: "unitless",
    1: "inches",
    2: "feet",
    3: "miles",
    4: "millimetres",
    5: "centimetres",
    6: "metres",
    7: "kilometres",
    8: "microinches",
    9: "mils",
    10: "yards",
    11: "angstroms",
    12: "nanometres",
    13: "microns",
    14: "decimetres",
    15: "decametres",
    16: "hectometres",
    17: "gigametres",
    18: "astronomical units",
    19: "light years",
    20: "parsecs",
    21: "US survey feet",
    22: "US survey inches",
    23: "US survey yards",
    24: "US survey miles",
}


def declare_millimeter_units(doc: Any) -> Any:
    """Marks a generated DXF as metric with millimetre drawing coordinates."""
    doc.header["$INSUNITS"] = 4
    doc.header["$MEASUREMENT"] = 1
    return doc


def require_millimeter_dxf(doc: Any) -> Any:
    """Rejects append targets that would mix millimetres with another unit."""
    try:
        insunits = int(doc.header.get("$INSUNITS", 0))
    except (TypeError, ValueError):
        insunits = 0
    if insunits != 4:
        raise ValueError(
            "Cannot append millimetre geometry to a DXF whose $INSUNITS is not millimetres (code 4)."
        )
    return declare_millimeter_units(doc)


def new_millimeter_dxf(*args: Any, **kwargs: Any) -> Any:
    """Creates a DXF with Pathstitch's canonical millimetre contract."""
    return declare_millimeter_units(ezdxf.new(*args, **kwargs))