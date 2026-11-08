"""Convert an opaque image to a fixed pixel grid and limited palette.

Usage: python scripts/pixelize-art.py input.png output.png --size 32 --colors 16
"""
import argparse
from pathlib import Path
from PIL import Image


def pixelize(source, size=32, colors=16):
    with Image.open(source) as image:
        rgba = image.convert('RGBA')
        if rgba.getchannel('A').getextrema() != (255, 255):
            raise ValueError('This converter requires an opaque image.')
        # Integrate each cell instead of sampling a single arbitrary source pixel.
        small = rgba.convert('RGB').resize((size, size), Image.Resampling.BOX)
        return small.quantize(colors=colors, method=Image.Quantize.MEDIANCUT,
                              dither=Image.Dither.NONE).convert('RGBA')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--size', type=int, default=32)
    parser.add_argument('--colors', type=int, default=16)
    args = parser.parse_args()
    if args.size < 1 or not 1 <= args.colors <= 256:
        parser.error('Size must be positive; colors must be between 1 and 256.')
    if args.output.exists():
        parser.error('Output already exists; choose a new filename.')
    result = pixelize(args.source, args.size, args.colors)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    result.save(args.output)
