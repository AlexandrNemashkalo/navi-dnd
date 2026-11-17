"""Render MapRenderingTests' real ANSI map rows without launching a terminal.

Requires Pillow. This reviews the game's glyphs/colors, not terminal rasterization or UI input.
"""
import argparse
import json
import re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parents[1] / "artifacts/map-render")
parser.add_argument("--font", type=Path, help="Monospaced TrueType/OpenType font; detects common system fonts by default")
args = parser.parse_args()
font_path = args.font or next((path for path in map(Path, [
    "C:/Windows/Fonts/consola.ttf", "/System/Library/Fonts/Menlo.ttc",
    "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
]) if path.is_file()), None)
if font_path is None:
    parser.error("No monospaced font found; supply --font PATH.")
font = ImageFont.truetype(str(font_path), 14)
files = sorted(args.directory.glob("*.json"))
if not files:
    raise SystemExit("Run MapRenderingTests first.")
cell_w, cell_h, panel_w, panel_h = 8, 16, 720, 580
image = Image.new("RGB", (panel_w * 2, panel_h * ((len(files) + 1) // 2)), (18, 20, 24))
draw = ImageDraw.Draw(image)
escape = re.compile(r"\x1b\[([0-9;]*)m")
for index, file in enumerate(files):
    origin_x, origin_y = (index % 2) * panel_w + 12, (index // 2) * panel_h + 12
    draw.text((origin_x, origin_y), file.stem + " / actual renderer", font=font, fill=(230, 230, 230))
    fg, bg = (220, 220, 220), (0, 0, 0)
    for row_index, row in enumerate(json.loads(file.read_text(encoding="utf-8"))):
        column, offset = 0, 0
        while offset < len(row):
            match = escape.match(row, offset)
            if match:
                values = [int(value) for value in match[1].split(";") if value]
                if len(values) == 5 and values[:2] == [38, 2]:
                    fg = tuple(values[2:])
                elif len(values) == 5 and values[:2] == [48, 2]:
                    bg = tuple(values[2:])
                elif values == [39]:
                    fg = (220, 220, 220)
                elif values == [49]:
                    bg = (0, 0, 0)
                else:
                    raise ValueError(f"Unsupported color sequence: {match[0]!r}")
                offset = match.end()
                continue
            char = row[offset]
            if ord(char) < 32:
                raise ValueError(f"Unexpected control character: {char!r}")
            x, y = origin_x + column * cell_w, origin_y + 30 + row_index * cell_h
            draw.rectangle((x, y, x + cell_w - 1, y + cell_h - 1), fill=bg)
            draw.text((x, y - 1), char, font=font, fill=fg)
            column += 1
            offset += 1
image.save(args.directory / "maps.png")
print(args.directory / "maps.png")
