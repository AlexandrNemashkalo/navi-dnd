"""Rasterize selected existing SVG silhouettes on an exact 32x32 grid.

Requires Pillow and resvg-py. Original 000000 files remain unchanged.
"""
import hashlib
import io
import json
from pathlib import Path

import resvg_py
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / 'src/ArtLibrary/Sources/own-art'
ROWS = [
    ('sea', 'lorc/at-sea.svg', 'Lorc', '#245b82', '#dce8dc', 'Circular sea symbol with two rows of waves and an open horizon.'),
    ('grass', 'delapouite/grass.svg', 'Delapouite', '#365c32', '#e3dfbf', 'Upright grass blades joined into a strong ground-level silhouette.'),
    ('hills', 'delapouite/hills.svg', 'Delapouite', '#426b3d', '#e3dfbf', 'Three overlapping rounded hills separated by light contour lines, with a sun in the corner.'),
    ('mountains', 'lorc/mountains.svg', 'Lorc', '#414e65', '#e4e5d6', 'Circular mountain symbol with angular peaks and light snow caps.'),
]


def build():
    preview = Image.new('RGB', (1024, 256))
    for index, (key, member, author, ink, paper, description) in enumerate(ROWS):
        source = ROOT / 'src/icons/000000/ffffff/1x1' / member
        # Supersampling measures coverage; a binary threshold creates hard pixel cells.
        png = resvg_py.svg_to_bytes(svg_path=str(source), width=512, height=512)
        coverage = Image.open(io.BytesIO(png)).convert('L').resize((32, 32), Image.Resampling.BOX)
        mask = coverage.point(lambda value: 255 if value < 128 else 0)
        image = Image.new('RGBA', (32, 32), paper)
        image.paste(Image.new('RGBA', (32, 32), ink), (0, 0), mask)
        destination = FOLDER / f'{key}-symbol-32x32.png'
        metadata_path = destination.with_suffix('.provenance.json')
        if destination.exists():
            previous = json.loads(metadata_path.read_text(encoding='utf-8'))
            if hashlib.sha256(destination.read_bytes()).hexdigest() != previous['sha256']:
                raise ValueError(f'Edited output preserved: {destination}')
        image.save(destination)
        metadata = dict(
            id=f'own-art:{key}-symbol-32x32', file=destination.name,
            origin=f'Pixel derivative of {member}. Icons made by {author}.',
            sourceKind='svg_derivative', sourcePath=source.relative_to(ROOT).as_posix(),
            sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
            license='CC BY 3.0', licenseRef='src/icons/license.txt', author=author,
            subject=key.title() + ' / pixel symbol', usage='biome_view',
            width=32, height=32, mode='RGBA',
            sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
            processing='SVG coverage rasterization; BOX to 32x32; binary threshold 128; two solid colors, no antialiasing.',
            artApproved=False, styleId='fantasy.pixel.landscape-symbol.32',
            framing='landscape_symbol', descriptionEn=description)
        metadata_path.write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
        assert image.size == (32, 32) and len(image.getcolors()) == 2
        preview.paste(image.convert('RGB').resize((256, 256), Image.Resampling.NEAREST), (index * 256, 0))
    target = ROOT / 'artifacts/imagegen/landscape-symbols-preview.png'
    target.parent.mkdir(parents=True, exist_ok=True)
    preview.save(target)
    print('Built four 32x32 two-color symbols; source SVGs unchanged.')


if __name__ == '__main__':
    build()
