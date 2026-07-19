"""Regenerate portable PSD parser fixtures with locked psd-tools runtime."""

from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image
from psd_tools import PSDImage
from psd_tools.api.layers import Group, PixelLayer


OUTPUT = Path(__file__).with_name("psd")


def generate_flattened() -> None:
    header = struct.pack(
        ">4sH6sHIIHHIIIH",
        b"8BPS",
        1,
        bytes(6),
        3,
        1,
        2,
        8,
        3,
        0,
        0,
        0,
        0,
    )
    (OUTPUT / "flattened-2x1.psd").write_bytes(
        header + bytes((255, 0, 0, 255, 0, 0))
    )


def generate_layered() -> None:
    document = PSDImage.new("RGBA", (8, 4), color=(0, 0, 0, 0))
    group = Group.new(document, "Hidden Group")
    group.visible = False
    PixelLayer.frompil(
        Image.new("RGBA", (2, 2), (255, 0, 0, 255)),
        group,
        name="Ink",
        top=0,
        left=0,
    )
    PixelLayer.frompil(
        Image.new("RGBA", (2, 2), (0, 255, 0, 255)),
        document,
        name="Ink",
        top=0,
        left=4,
    )
    document.save(OUTPUT / "layered-hidden-group.psd")


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    generate_flattened()
    generate_layered()
    (OUTPUT / "malformed-truncated.psd").write_bytes(b"8BPS")


if __name__ == "__main__":
    main()

