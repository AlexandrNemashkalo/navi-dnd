"""Import explicitly staged preview extractions; no downloads or image resizing.

Keeps these distinct from original licensed PNG packs. Repeated runs preserve
manual edits and deletion tombstones. Intermediate extraction versions are not
imported alongside the final portraits-native-32 collection.
"""
import hashlib
import io
import json
from pathlib import Path
from PIL import Image
from art_visual_metadata import load_annotations, apply_annotation

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/art-catalog-review'
LIBRARY = ROOT / 'src/ArtLibrary'


def read(path, default):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else default


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.new')
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)


def immutable(path, raw):
    if path.exists():
        if path.read_bytes() != raw:
            raise ValueError(f'Edited output preserved: {path}')
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)


def main():
    original = read(OUT / 'imported-assets.json', [])
    previous = read(OUT / 'prepared-assets.json', read(LIBRARY / 'prepared-assets.json', []))
    known = {}
    for asset in original + previous:
        file = OUT / asset['path']
        if file.exists():
            with Image.open(file) as image:
                known[hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest()] = asset['id']
    prior_report = read(LIBRARY / 'prepared-import-report.json', {})
    assets, packs, aliases, skipped = [], [], [], list(prior_report.get('skipped', []))
    for collection in ('portraits-native-32', 'items-native-32', 'items-native-32-batch2'):
        folder = ROOT / 'artifacts' / collection
        if not folder.exists():
            for asset in (a for a in previous if a['provenance']['collection'] == collection):
                source = LIBRARY / 'Sources' / asset['pack'] / asset['provenance']['member']
                raw = source.read_bytes()
                if hashlib.sha256(raw).hexdigest() != asset['sourceSha256']:
                    raise ValueError(f'Permanent source changed: {source}')
                deleted = read(OUT / 'deletions.json', read(LIBRARY / 'deletions.snapshot.json', {}))
                if asset['id'] not in deleted:
                    immutable(OUT / asset['path'], raw)
                assets.append(asset)
            packs.extend(p for p in read(LIBRARY / 'prepared-packs.json', []) if p['collection'] == collection)
            continue
        if collection == 'portraits-native-32':
            sources = read(folder / 'sources.json', [])
            groups = [dict(p, kind='portrait', label=p['id'], sourceUrl=p['reference']) for p in sources]
            rows = read(folder / 'verification.json', {})['files']
            for row in rows:
                row['pack'] = Path(row['file']).parent.name
                row['number'] = int(Path(row['file']).stem.rsplit('-', 1)[1])
        else:
            manifest = read(folder / 'manifest.json', {})
            groups, rows = manifest['packs'], manifest['icons']
        for group in groups:
            # Same halfling artwork as the explicitly excluded 64px source pack.
            if collection == 'portraits-native-32' and group['id'] == 'halflings':
                entry = dict(collection=collection, pack=group['id'], reason='excluded_64px_source_family')
                if entry not in skipped:
                    skipped.append(entry)
                continue
            pack_id = f"prepared-{collection}-{group['id']}"
            members = []
            for row in (r for r in rows if r['pack'] == group['id']):
                relative = Path(row['file'])
                if relative.is_absolute() or '..' in relative.parts or relative.suffix != '.png':
                    raise ValueError('Unsafe staged PNG path')
                source = folder / relative
                if not source.resolve().is_relative_to(folder.resolve()) or source.is_symlink():
                    raise ValueError('Staged PNG outside collection')
                raw = source.read_bytes()
                digest = hashlib.sha256(raw).hexdigest()
                with Image.open(io.BytesIO(raw)) as image:
                    if image.size != (32, 32):
                        raise ValueError(f'Only native output 32x32 accepted: {source}')
                    rgba = image.convert('RGBA')
                    alpha = rgba.getchannel('A')
                    if alpha.getbbox() is None:
                        raise ValueError(f'Empty PNG: {source}')
                    if group['kind'] != 'skill' and alpha.getextrema()[0] == 255:
                        raise ValueError(f'Background remains in item/portrait: {source}')
                    pixels = hashlib.sha256(rgba.tobytes()).hexdigest()
                    expected = digest if collection == 'portraits-native-32' else pixels
                    if row.get('sha256') != expected:
                        raise ValueError(f'Staged PNG changed since manifest: {source}')
                    bounds = alpha.getbbox()
                    transparent = alpha.getextrema()[0] < 255
                asset_id = f"{pack_id}:icon-{row['number']:03d}"
                member = relative.name
                permanent = LIBRARY / 'Sources' / pack_id / member
                immutable(permanent, raw)
                members.append(dict(member=member, path=permanent.relative_to(ROOT).as_posix(),
                                    pathBase='workspace', sha256=digest, available=True))
                if pixels in known and known[pixels] != asset_id:
                    aliases.append(dict(id=asset_id, duplicateOf=known[pixels], source=source.relative_to(ROOT).as_posix()))
                    continue
                known[pixels] = asset_id
                deleted = read(OUT / 'deletions.json', read(LIBRARY / 'deletions.snapshot.json', {}))
                working = OUT / 'imported' / pack_id / 'icons' / member
                if asset_id not in deleted:
                    immutable(working, raw)
                kind = group['kind']
                usage = 'spell' if kind == 'skill' else kind
                label = group.get('label', group['id'])
                if '?' in label:
                    label = group['id']
                traits = dict(sourceQuality='preview_extraction', collection=group['id'])
                if kind == 'portrait':
                    traits.update(framing='head_shoulders', gender='unknown', species='unknown')
                assets.append(dict(id=asset_id, pack=pack_id, label=f"{label} / {row['number']:02d}",
                    path=working.relative_to(OUT).as_posix(), preview=working.relative_to(OUT).as_posix(),
                    sha256=digest, sourceSha256=digest, width=32, height=32, sourceWidth=None, sourceHeight=None,
                    hasTransparency=transparent, visibleBounds=bounds, usages=[usage], proposedUsages=[usage],
                    inferredKind=kind, styleId=f"fantasy.pixel.{'skill-framed' if kind == 'skill' else kind}.32",
                    identityFamily=asset_id, variantFamily=asset_id, framing='head_shoulders' if kind == 'portrait' else 'unknown',
                    gender='unknown', species='unknown', descriptionEn=None, traits=traits,
                    technicalStatus='passed', semanticStatus='needs_individual_description', artStatus='needs_in_game_review',
                    metadataSource='staged_manifest', transform='none_on_import',
                    provenance=dict(member=member, collection=collection, stagedPath=source.relative_to(ROOT).as_posix(),
                        sourceUrl=group['sourceUrl'], page=group.get('page'), sourceKind='preview_extraction',
                        licenseStatus='not_recorded_in_staged_collection', originalPaletteRecovered=False)))
            # Retain provenance/manifests independently of ignored staging folders.
            write(LIBRARY / 'Sources' / pack_id / 'provenance.json', group)
            packs.append(dict(id=pack_id, sourceKind='preview_extraction', sourceFiles=members,
                              collection=collection, sourceUrl=group['sourceUrl']))
    annotations = load_annotations(ROOT)
    assets = [apply_annotation(a, annotations) for a in assets]
    write(OUT / 'prepared-assets.json', assets)
    write(LIBRARY / 'prepared-assets.json', assets)
    write(OUT / 'prepared-packs.json', packs)
    write(LIBRARY / 'prepared-packs.json', packs)
    report = dict(candidates=len(assets), aliases=aliases, skipped=skipped)
    write(OUT / 'prepared-import-report.json', report)
    write(LIBRARY / 'prepared-import-report.json', report)
    print(f'Prepared candidates: {len(assets)}; duplicate aliases: {len(aliases)}; excluded groups: {len(skipped)}')


if __name__ == '__main__':
    main()
