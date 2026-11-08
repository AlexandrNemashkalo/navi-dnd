"""Import four small BG3 samples, with binary geometry and separate line colors."""
import hashlib
import json
from pathlib import Path
import py7zr
from PIL import Image
from icon_to_braille import convert_icon, build_background, preview_html

ROOT = Path(__file__).resolve().parents[1]
archive = ROOT / 'Icon pack-3125-1-1696894685.7z'
staging = ROOT / 'artifacts/bg3-icon-pack'
folder = ROOT / 'src/ArtLibrary/Sources/own-art'
runtime = ROOT / 'src/icons/bg3-lines'
runtime.mkdir(parents=True, exist_ok=True)
members = [('axe', 'tile001.png', 'A double-bladed axe drawn with thin light strokes.', 140),
           ('thrust', 'tile002.png', 'A slender thrusting blade drawn with thin light strokes.', 140),
           ('lightning', 'tile040.png', 'A figure surrounded by a sweeping blue lightning stroke.', 115),
           ('anchor', 'tile028.png', 'A golden anchor drawn with luminous yellow strokes.', 115)]
with py7zr.SevenZipFile(archive) as pack:
    pack.extract(path=staging, targets=['Icons/cutsprites/' + name for _, name, _, _ in members])
archive_digest = hashlib.sha256(archive.read_bytes()).hexdigest()


def check_existing(path, expected):
    if path.exists() and hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise ValueError(f'Edited file preserved: {path}')


for key, name, description, threshold in members:
    source = staging / 'Icons/cutsprites' / name
    original = folder / 'bg3-originals' / name
    original.parent.mkdir(parents=True, exist_ok=True)
    if original.exists() and original.read_bytes() != source.read_bytes():
        raise ValueError(f'Edited original preserved: {original}')
    original.write_bytes(source.read_bytes())
    image = Image.open(source).convert('RGBA')
    assert image.size == (64, 64)
    mask, colors, _ = convert_icon(image, mode='lines', inset=5, threshold=threshold)
    background = build_background(image, mask, inset=5)
    target = folder / f'bg3-{key}-lines-64.png'
    color_target = target.with_name(target.stem + '.color.png')
    background_target = target.with_name(target.stem + '.background.png')
    metadata = target.with_suffix('.provenance.json')
    prior = json.loads(metadata.read_text(encoding='utf-8')) if metadata.exists() else {}
    output = runtime / f'{key}.png'
    color_output = runtime / f'{key}.color.png'
    background_output = runtime / f'{key}.background.png'
    for path, digest in [(target, prior.get('sha256')), (output, prior.get('sha256')),
                         (color_target, prior.get('colorSha256')), (color_output, prior.get('colorSha256')),
                         (background_target, prior.get('backgroundSha256')), (background_output, prior.get('backgroundSha256'))]:
        check_existing(path, digest)
    mask.save(target)
    colors.save(color_target)
    background.save(background_target)
    output.write_bytes(target.read_bytes())
    color_output.write_bytes(color_target.read_bytes())
    background_output.write_bytes(background_target.read_bytes())
    previews = ROOT / 'artifacts/braille-preview'
    previews.mkdir(parents=True, exist_ok=True)
    (previews / f'{key}.html').write_text(preview_html(image, mask, colors, background), encoding='utf-8')
    info = dict(id=f'own-art:bg3-{key}-lines', file=target.name,
                origin='BG3 Icon Pack by starpest; original artwork credited to Larian.',
                sourceKind='bg3_line_mask', sourcePath=original.relative_to(ROOT).as_posix(),
                sourceUrl='https://www.nexusmods.com/baldursgate3/mods/3125',
                archiveSha256=archive_digest,
                member='Icons/cutsprites/' + name,
                license='Original game assets; no independent redistribution license established.',
                subject=f'BG3 / {key} / braille line mask', usage='item', width=64, height=64,
                sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                colorFile=color_target.name, colorSha256=hashlib.sha256(color_target.read_bytes()).hexdigest(),
                backgroundFile=background_target.name, backgroundSha256=hashlib.sha256(background_target.read_bytes()).hexdigest(),
                styleId='fantasy.braille.lines.64', framing='isolated_symbol',
                descriptionEn=description, artApproved=False,
                processing=f'icon_to_braille.py --mode lines --size 64 --inset 5 --threshold {threshold}; original line RGB retained; neutral background with restrained line hue and 6px edge feather, fitted to the full picture panel independently of the symbol.')
    metadata.write_text(json.dumps(info, indent=2) + '\n', encoding='utf-8')
print('Imported four 64x64 masks, original line colors and neutral backgrounds; original cuts retained.')
