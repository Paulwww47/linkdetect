"""Generate LinkDetect app.ico (rounded blue base + white chain, solid, emoji-like)."""
from PIL import Image, ImageDraw
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "LinkDetect" / "Assets" / "app.ico"
PREVIEW = ROOT / "src" / "LinkDetect" / "Assets" / "app-preview.png"

SOLID = (97, 141, 255)  # #618DFF solid, no gradient


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def draw_base(size):
    margin = int(size * 0.0625)
    radius = int(size * 0.22)
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    solid = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gd = ImageDraw.Draw(solid)
    box = (margin, margin, size - margin, size - margin)
    gd.rounded_rectangle(box, radius=radius, fill=SOLID + (255,))
    mask = Image.new("L", (size, size), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle(box, radius=radius, fill=255)
    base.paste(solid, (0, 0), mask)
    return base, box


def draw_chain_layer(size):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    w = max(3, int(size * 0.105))
    # rounder rings like link emoji, near-circular, thick stroke
    left = (int(size * 0.20), int(size * 0.36), int(size * 0.54), int(size * 0.64))
    right = (int(size * 0.46), int(size * 0.36), int(size * 0.80), int(size * 0.64))
    d.ellipse(left, outline=(255, 255, 255, 255), width=w)
    d.ellipse(right, outline=(255, 255, 255, 255), width=w)
    d.line([(size * 0.42, size * 0.50), (size * 0.58, size * 0.50)],
           fill=(255, 255, 255, 255), width=w)
    return layer.rotate(-45, resample=Image.BICUBIC, center=(size / 2, size / 2))


def build(size=256):
    base, _ = draw_base(size)
    chain = draw_chain_layer(size)
    base = Image.alpha_composite(base, chain)
    return base


def main():
    big = build(256)
    big.save(PREVIEW)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    sizes = [(16, 16), (32, 32), (48, 48), (256, 256)]
    big.save(OUT, sizes=sizes)
    print(f"wrote {OUT} sizes={[s[0] for s in sizes]}")
    print(f"wrote {PREVIEW}")


if __name__ == "__main__":
    main()
