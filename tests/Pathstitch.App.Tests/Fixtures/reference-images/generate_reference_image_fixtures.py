from __future__ import annotations

import random
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SIZE = (64, 64)


def save(image: Image.Image, mask: Image.Image, name: str) -> None:
    image.save(ROOT / f"{name}.png", format="PNG", optimize=False)
    mask.save(ROOT / f"{name}-mask.png", format="PNG", optimize=False)


def trace_canvas() -> tuple[Image.Image, Image.Image, ImageDraw.ImageDraw, ImageDraw.ImageDraw]:
    image = Image.new("RGBA", SIZE, "white")
    mask = Image.new("L", SIZE, 0)
    return image, mask, ImageDraw.Draw(image), ImageDraw.Draw(mask)


def make_logo() -> None:
    image, mask, draw, truth = trace_canvas()
    draw.rounded_rectangle((10, 10, 53, 53), radius=8, fill="black")
    draw.rectangle((22, 18, 41, 45), fill="white")
    draw.rectangle((18, 22, 45, 41), fill="white")
    truth.rounded_rectangle((10, 10, 53, 53), radius=8, fill=255)
    truth.rectangle((22, 18, 41, 45), fill=0)
    truth.rectangle((18, 22, 45, 41), fill=0)
    save(image, mask, "logo-clean")


def make_antialiased() -> None:
    scale = 4
    large_size = (SIZE[0] * scale, SIZE[1] * scale)
    large_mask = Image.new("L", large_size, 0)
    draw = ImageDraw.Draw(large_mask)
    points = [(4, 47), (15, 20), (27, 39), (40, 14), (59, 42)]
    draw.line([(x * scale, y * scale) for x, y in points], fill=255, width=5 * scale, joint="curve")
    alpha = large_mask.resize(SIZE, Image.Resampling.LANCZOS)
    image = Image.new("RGBA", SIZE, "white")
    image.paste(Image.new("RGBA", SIZE, "black"), mask=alpha)
    truth = alpha.point(lambda value: 255 if value >= 128 else 0)
    save(image, truth, "antialiased-line-art")


def make_holes_and_islands() -> None:
    image, mask, draw, truth = trace_canvas()
    draw.rectangle((6, 6, 43, 55), fill="black")
    draw.ellipse((15, 16, 34, 39), fill="white")
    draw.ellipse((48, 10, 59, 21), fill="black")
    truth.rectangle((6, 6, 43, 55), fill=255)
    truth.ellipse((15, 16, 34, 39), fill=0)
    truth.ellipse((48, 10, 59, 21), fill=255)
    save(image, mask, "holes-and-islands")


def make_noisy_scan() -> None:
    image, mask, draw, truth = trace_canvas()
    polygon = [(9, 52), (17, 13), (33, 8), (52, 18), (55, 48), (37, 56)]
    draw.polygon(polygon, fill="black")
    truth.polygon(polygon, fill=255)
    randomizer = random.Random(1729)
    for _ in range(80):
        x = randomizer.randrange(SIZE[0])
        y = randomizer.randrange(SIZE[1])
        if mask.getpixel((x, y)) == 0:
            shade = randomizer.randrange(0, 90)
            draw.point((x, y), fill=(shade, shade, shade, 255))
    save(image, mask, "noisy-scan")


def make_border_touching() -> None:
    image, mask, draw, truth = trace_canvas()
    draw.polygon([(0, 15), (29, 15), (45, 32), (29, 49), (0, 49)], fill="black")
    truth.polygon([(0, 15), (29, 15), (45, 32), (29, 49), (0, 49)], fill=255)
    save(image, mask, "border-touching")


def make_transparency() -> None:
    image = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    mask = Image.new("L", SIZE, 0)
    draw = ImageDraw.Draw(image)
    truth = ImageDraw.Draw(mask)
    draw.ellipse((14, 12, 50, 52), fill=(20, 20, 20, 255))
    truth.ellipse((14, 12, 50, 52), fill=255)
    draw.point((4, 4), fill=(0, 0, 0, 8))
    draw.point((59, 58), fill=(0, 0, 0, 10))
    save(image, mask, "transparency")


def make_flat_background() -> None:
    image = Image.new("RGBA", SIZE, (225, 238, 247, 255))
    mask = Image.new("L", SIZE, 0)
    ImageDraw.Draw(image).ellipse((14, 10, 50, 54), fill=(170, 35, 55, 255))
    ImageDraw.Draw(mask).ellipse((14, 10, 50, 54), fill=255)
    save(image, mask, "flat-background-object")


def gradient_pixel(x: int, y: int) -> tuple[int, int, int, int]:
    return 245 - x // 2, 238 - y // 3, 230 - (x + y) // 5, 255


def make_gradient_background() -> None:
    image = Image.new("RGBA", SIZE)
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            image.putpixel((x, y), gradient_pixel(x, y))
    mask = Image.new("L", SIZE, 0)
    ImageDraw.Draw(image).rounded_rectangle((17, 13, 48, 52), radius=7, fill=(30, 45, 70, 255))
    ImageDraw.Draw(mask).rounded_rectangle((17, 13, 48, 52), radius=7, fill=255)
    save(image, mask, "gradient-background-object")


def make_hairlike_decision_gate() -> None:
    image = Image.new("RGBA", SIZE)
    randomizer = random.Random(991)
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            base = gradient_pixel(x, y)
            noise = randomizer.randrange(-8, 9)
            image.putpixel((x, y), tuple(max(0, min(255, value + noise)) for value in base[:3]) + (255,))
    mask = Image.new("L", SIZE, 0)
    draw = ImageDraw.Draw(image)
    truth = ImageDraw.Draw(mask)
    draw.ellipse((20, 11, 44, 38), fill=(115, 70, 45, 255))
    truth.ellipse((20, 11, 44, 38), fill=255)
    strands = [[(22 + offset, 28), (18 + offset, 55), (15 + offset, 63)] for offset in range(0, 22, 3)]
    for strand in strands:
        draw.line(strand, fill=(55, 35, 25, 255), width=2)
        truth.line(strand, fill=255, width=2)
    save(image, mask, "photo-hair-synthetic-decision-gate")


def main() -> None:
    ROOT.mkdir(parents=True, exist_ok=True)
    make_logo()
    make_antialiased()
    make_holes_and_islands()
    make_noisy_scan()
    make_border_touching()
    make_transparency()
    make_flat_background()
    make_gradient_background()
    make_hairlike_decision_gate()


if __name__ == "__main__":
    main()
