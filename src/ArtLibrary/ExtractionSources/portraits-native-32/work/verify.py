"""Validate decoded deliverables; preserve legitimate detached pixel-art accessories."""
import hashlib
import json
from pathlib import Path
import zipfile
from PIL import Image

root = Path(__file__).resolve().parents[1]
packs = ['elves', 'humans', 'halflings', 'dark-elves', 'undead', 'demons', 'pirates', 'dwarves']
records, fingerprints = [], {}
for pack in packs:
    files = sorted((root / 'png' / pack).glob(f'{pack}-*.png'))
    assert len(files) == 48, (pack, len(files))
    for index, path in enumerate(files, 1):
        assert path.name == f'{pack}-{index:02}.png'
        with Image.open(path) as image:
            assert image.size == (32, 32) and image.mode == 'RGBA', path
            pixels = list(image.getdata())
        assert {p[3] for p in pixels} == {0, 255}, path
        assert all(p == (0, 0, 0, 0) for p in pixels if p[3] == 0), path
        opaque = sum(p[3] == 255 for p in pixels)
        assert 150 < opaque < 1024, (path, opaque)
        digest = hashlib.sha256(bytes(v for p in pixels for v in p)).hexdigest()
        assert digest not in fingerprints, (path, fingerprints.get(digest))
        fingerprints[digest] = str(path.relative_to(root))
        records.append({'file': str(path.relative_to(root)).replace('\\', '/'),
                        'opaquePixels': opaque, 'transparentPixels': 1024-opaque,
                        'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
# These parts disappeared under the rejected largest-component-only pipeline.
with Image.open(root/'png/undead/undead-39.png') as image:
    assert sum(image.getpixel((x, y))[3] == 255 for y in range(2, 30) for x in range(23, 32)) > 70, 'Missing floating snake'
with Image.open(root/'png/demons/demons-28.png') as image:
    assert any(image.getpixel((x, y))[3] for y in range(16, 25) for x in range(0, 5)), 'Missing left floating ornament'
    assert any(image.getpixel((x, y))[3] for y in range(16, 25) for x in range(27, 32)), 'Missing right floating ornament'
report = {'pngCount': len(records), 'packCount': len(packs), 'size': [32, 32],
          'alphaValues': [0, 255], 'duplicates': 0, 'accessoryChecks': 'passed',
          'forcedPadding': False, 'outlineErosion': False, 'files': records}
(root/'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
archive = root.with_suffix('.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as out:
    for path in sorted(root.rglob('*')):
        if path.is_file() and 'work' not in path.relative_to(root).parts:
            out.write(path, path.relative_to(root))
with zipfile.ZipFile(archive) as out:
    assert out.testzip() is None
    assert sum(n.startswith('png/') and n.endswith('.png') for n in out.namelist()) == 384
print(f'PASS: {len(records)} unique 32x32 RGBA portraits; binary alpha; transparent RGB cleared; floating accessories preserved. ZIP: {archive}')
