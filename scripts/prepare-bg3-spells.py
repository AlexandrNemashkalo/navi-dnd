"""Prepare named spell-only BG3 icons from the user's archive (Windows/macOS).

Requires Pillow and py7zr. Names/atlas positions are pinned in the source manifest;
no network or model calls are needed for regeneration. Edited files are preserved.
"""
import argparse
import hashlib
from io import BytesIO
import json
from pathlib import Path
import re
import unicodedata
from spell_art_processing import adjust_lines, dominant_line_color, pixel_icon

from PIL import Image
import py7zr
from icon_to_braille import convert_icon, build_background, pixel_data, clean_line_palette

ROOT = Path(__file__).resolve().parents[1]
RULES = ROOT / 'src/ArtLibrary/RuleSets/Dnd5e'
OUTPUT = ROOT / 'src/ArtLibrary/Sources/bg3-spells'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def png(image):
    buffer = BytesIO()
    image.save(buffer, format='PNG')
    return buffer.getvalue()


def normalize_name(name):
    return ' '.join(unicodedata.normalize('NFKC', name).lower().replace('ё', 'е')
                    .replace('’', "'").replace('‘', "'").replace('ʼ', "'").split())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, default=ROOT / 'Icon pack-3125-1-1696894685.7z')
    args = parser.parse_args()
    sources = json.loads((RULES / 'bg3-spell-sources.json').read_text(encoding='utf-8'))
    if sources['schemaVersion'] != 2:
        raise ValueError('Use the pinned source manifest with stable icon IDs.')
    if sources['defaultRenderMode'] not in ('braille', 'pixels'):
        raise ValueError('Unknown spell art render mode.')
    if digest(args.archive.read_bytes()) != sources['archiveSha256']:
        raise ValueError('Archive differs from the pinned BG3 icon pack.')
    staging = ROOT / 'artifacts/bg3-spells-source'
    sheets = {1: 'Icons/Icons_Skills.png', 2: 'Icons/Icons_Skills2.png'}
    with py7zr.SevenZipFile(args.archive) as archive:
        archive.extract(path=staging, targets=list(sheets.values()))
    atlases = {n: Image.open(staging / path).convert('RGBA') for n, path in sheets.items()}
    manifest_path = RULES / 'spell-icons.json'
    previous = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {}
    previous_by_name = {i['name']: i for i in previous.get('icons', [])}
    database = json.loads((ROOT / 'src/GameData/DnD5e_spells_BD.dtn').read_text(encoding='utf-8-sig'))['allSpells']
    canonical = {s['en']['name'].strip(): s['en'] for s in database}
    primary_names = {}
    for spell in database:
        for text in spell.values():
            if isinstance(text, dict) and isinstance(text.get('name'), str):
                primary_names.setdefault(normalize_name(text['name']), set()).add(spell['en']['name'].strip())
    icons, writes = [], []
    for entry in sources['icons']:
        name = entry['name']
        if name not in canonical:
            raise ValueError(f'Unknown canonical spell: {name}')
        slug = entry['id']
        if not re.fullmatch(r'[a-z0-9]+(?:-[a-z0-9]+)*', slug):
            raise ValueError(f'Invalid stable icon ID: {slug}')
        aliases = sorted({text[key].strip() for spell in database if spell['en']['name'].strip() == name
                          for lang, text in spell.items() if isinstance(text, dict)
                          for key in ('name', 'nic') if isinstance(text.get(key), str) and text[key].strip()})
        # A synonym must never claim another spell's primary translated name (e.g. Gust/Gust of Wind).
        aliases = [alias for alias in aliases if name in primary_names.get(normalize_name(alias), {name})]
        profile = dict(mode='lines', inset=5, threshold=115, minComponent=3, cleanPalette=True, backgroundFeather=6)
        profile.update(entry.get('processing', {}))
        if entry.get('sourceFile'):
            supplemental = (ROOT/'src/ArtLibrary'/entry['sourceFile']).resolve()
            if not supplemental.is_relative_to((ROOT/'src/ArtLibrary/Sources/bg3-spells-supplemental').resolve()):
                raise ValueError('Supplemental spell source is outside its source folder.')
            if digest(supplemental.read_bytes()) != entry['sourceSha256']:
                raise ValueError('Changed supplemental spell source: ' + str(supplemental))
            original = Image.open(supplemental).convert('RGBA')
            if original.size != (64, 64):
                raise ValueError('Supplemental spell sources must be native 64x64.')
        else:
            x, y = entry['tile'] % 32 * 64, entry['tile'] // 32 * 64
            original = atlases[entry['sheet']].crop((x, y, x+64, y+64))
        mask, colors, mode = convert_icon(original, mode=profile['mode'], inset=profile['inset'],
                                         threshold=profile['threshold'], min_component=profile['minComponent'])
        mask, colors = adjust_lines(mask, colors, profile)
        mask, colors = clean_line_palette(mask, colors)
        background = build_background(original, mask, inset=profile['inset'])
        pixels = pixel_icon(colors, background)
        dots = sum(p == 0 for p in pixel_data(mask))
        if not 20 <= dots <= 1800:
            raise ValueError(f'Unusable stroke coverage: {name}: {dots}/4096')
        record = dict(name=name, id=slug, aliases=aliases, nameColor=dominant_line_color(colors),
                      usage='spell', school=canonical[name]['school'].strip(),
                      width=64, height=64, dots=dots,
                      sourceSheet=entry['sourceFile'] if entry.get('sourceFile') else sheets[entry['sheet']],
                      sourceTile=entry.get('tile'), referenceUrl=entry['referenceUrl'],
                      pixelWidth=32, pixelHeight=32, processing=profile)
        kind = entry.get('mappingKind', 'exact')
        if kind not in ('exact', 'thematic'):
            raise ValueError('Unknown spell mapping kind: ' + kind)
        if kind == 'thematic' and (not entry.get('sourceName') or not entry.get('mappingReason')):
            raise ValueError('Thematic spell bindings require a reviewed source and reason.')
        record['spellMapping'] = dict(kind=kind, sourceName=entry.get('sourceName', name),
            reason=entry.get('mappingReason', 'Exact named spell icon pinned in spell-icons.json.'))
        if entry.get('sourceFile'):
            record['sourceFile'], record['sourceSha256'] = entry['sourceFile'], entry['sourceSha256']
        if entry.get('variant'):
            record['variant'] = entry['variant']
        prior = previous_by_name.get(name, {})
        for field, image, suffix in [('original', original, '.original.png'), ('mask', mask, '.mask.png'),
                                      ('color', colors, '.color.png'), ('background', background, '.background.png'),
                                      ('pixel', pixels, '.pixel.png')]:
            file = slug + suffix
            raw = png(image)
            path = OUTPUT / file
            if path.exists() and path.read_bytes() != raw and digest(path.read_bytes()) != prior.get(field+'Sha256'):
                raise ValueError(f'Edited image preserved: {path}')
            record[field+'File'] = file
            record[field+'Sha256'] = digest(raw)
            writes.append((path, raw))
        icons.append(record)
    covered = {i['name'] for i in icons}
    alias_owners = {}
    for icon in icons:
        for alias in icon['aliases'] + [icon['name'], icon['id']]:
            key = normalize_name(alias)
            previous_owner = alias_owners.setdefault(key, icon['id'])
            if previous_owner != icon['id']:
                raise ValueError(f'Ambiguous spell alias: {alias}: {previous_owner}/{icon["id"]}')
    missing = [dict(name=name, school=info['school'].strip(), reason='No pinned BG3 icon yet; keep the existing spell picture.')
               for name, info in canonical.items() if name not in covered]
    manifest = dict(schemaVersion=2, ruleSet='Dnd5e', usage='spell',
                    defaultRenderMode=sources['defaultRenderMode'],
                    origin=sources['origin'], sourceUrl=sources['sourceUrl'], archiveSha256=sources['archiveSha256'],
                    license=sources['license'], icons=icons)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for path, raw in writes:
        if not path.exists() or path.read_bytes() != raw:
            path.write_bytes(raw)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    report = dict(catalogEntries=len(database), totalSpells=len(canonical), prepared=len(icons),
                  exact=sum(i['spellMapping']['kind']=='exact' for i in icons),
                  thematic=sum(i['spellMapping']['kind']=='thematic' for i in icons),
                  fallbackCount=len(missing), fallback=missing)
    (RULES/'spell-icon-coverage.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'Prepared {len(icons)} fixed spell bindings (Braille + 32px PNG + title RGB); {len(missing)} retain existing spell pictures.')


if __name__ == '__main__':
    main()
