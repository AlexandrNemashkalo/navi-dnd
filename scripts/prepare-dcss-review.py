"""Rebuild stable numbered DCSS review sheets without shifting prior positions."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/icons/dcss'
INDEX = ROOT / 'src/ArtLibrary/dcss-review-index.json'
OUT = ROOT / 'artifacts/art-catalog-review/dcss-details'


def main():
    index = json.loads(INDEX.read_text(encoding='utf-8')) if INDEX.exists() else []
    known = {a['id'] for s in index for a in s['assets']}
    added = []
    for path in sorted(SOURCE.rglob('*.png')):
        asset_id = path.relative_to(ROOT / 'src/icons').with_suffix('').as_posix()
        if asset_id not in known:
            added.append(dict(id=asset_id, path=path.relative_to(ROOT).as_posix(),
                              sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    for offset in range(0, len(added), 48):
        index.append(dict(sheet=f'{len(index)+1:03d}', assets=added[offset:offset+48]))
    OUT.mkdir(parents=True, exist_ok=True)
    for batch in index:
        if not batch['sheet'].isdigit() or len(batch['assets']) > 48:
            raise ValueError('Invalid frozen sheet index')
        board = Image.new('RGB', (800,672), '#34383c')
        draw = ImageDraw.Draw(board)
        for i, asset in enumerate(batch['assets']):
            path = (ROOT / asset['path']).resolve()
            if not path.is_relative_to(SOURCE.resolve()):
                raise ValueError('Review index outside DCSS')
            x, y = i % 8 * 100, i // 8 * 112
            draw.text((x+3,y+96), str(i+1), fill='white')
            if not path.exists():
                draw.text((x+3,y+35), 'removed', fill='grey'); continue
            raw = path.read_bytes()
            if hashlib.sha256(raw).hexdigest() != asset['sha256']:
                raise ValueError(f'PNG changed; prior review preserved: {path}')
            with Image.open(path) as original:
                image = original.convert('RGBA').resize((96,96),Image.Resampling.NEAREST)
                board.paste(image,(x,y),image)
        board.save(OUT / (batch['sheet'] + '.png'))
    temporary = INDEX.with_suffix('.json.new')
    temporary.write_text(json.dumps(index,indent=2),encoding='utf-8'); temporary.replace(INDEX)
    print(f'DCSS sheets: {len(index)}; newly indexed: {len(added)}. Source PNGs untouched.')


if __name__ == '__main__':
    main()
