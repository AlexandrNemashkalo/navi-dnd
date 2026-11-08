"""Spell-only prepared art in V2: PNG previews, never terminal Braille text."""
import hashlib
from io import BytesIO
import json
from PIL import Image


def merge_spell_bindings(root, document, assets):
    """Use the game's pinned spell names in the review catalog, preserving other bindings."""
    manifest = root / 'src/ArtLibrary/RuleSets/Dnd5e/spell-icons.json'
    if not manifest.exists():
        return document
    data = json.loads(manifest.read_text(encoding='utf-8'))
    if data['schemaVersion'] != 2 or data['ruleSet'] != 'Dnd5e' or data['usage'] != 'spell':
        raise ValueError('Invalid fixed spell bindings.')
    by_id = {a['id']: a for a in assets}
    replacements = {}
    for icon in data['icons']:
        name, asset_id = icon['name'], 'bg3-spells:' + icon['id']
        if name in replacements:
            raise ValueError('Duplicate fixed spell name: ' + name)
        asset = by_id.get(asset_id)
        candidates = []
        if asset is not None:
            if asset.get('metadataSource') != 'bg3_spell_manifest' or asset['label'] != name or asset['usages'] != ['spell']:
                raise ValueError('Spell artwork does not match its named source: ' + name)
            mapping = asset.get('spellMapping', {})
            thematic = mapping.get('kind') == 'thematic'
            candidates.append(dict(assetId=asset_id, sha256=asset['sha256'], usage='spell',
                reason=mapping.get('reason', 'Exact named spell icon pinned in spell-icons.json.'),
                match='visual_representation' if thematic else 'exact', sourcePriority=0))
        replacements[name] = dict(category='spell', name=name, selectionPolicy=dict(
            mode='fixed_spell_icon', preserveAssignment=True,
            requiredChecks=['usage', 'exact_spell_name', 'source_hash', 'art_approval']),
            note=('Thematic BG3 replacement; this is not the original BG3 icon for this spell. '
                  if asset and asset.get('spellMapping', {}).get('kind') == 'thematic' else '')
                 + 'Same fixed icon identity for pixel and Braille rendering; spell mechanics remain in game data.',
            candidates=candidates)
    # Missing/deleted pinned art stays unmapped; never substitute an older visual guess.
    bindings = [replacements.pop(b['name']) if b['category'] == 'spell' and b['name'] in replacements else b
                for b in document['bindings']]
    return dict(document, bindings=bindings + list(replacements.values()))


def load_spell_assets(root):
    manifest = root / 'src/ArtLibrary/RuleSets/Dnd5e/spell-icons.json'
    if not manifest.exists():
        return []
    data = json.loads(manifest.read_text(encoding='utf-8'))
    if data['schemaVersion'] != 2:
        raise ValueError('Expected spell bindings with both render formats.')
    folder = root / 'src/ArtLibrary/Sources/bg3-spells'
    review = root / 'artifacts/art-catalog-review'
    deletions = review / 'deletions.json'
    if not deletions.exists():
        deletions = root / 'src/ArtLibrary/deletions.snapshot.json'
    deleted = json.loads(deletions.read_text(encoding='utf-8')) if deletions.exists() else {}
    assets = []
    for entry in data['icons']:
        asset_id = 'bg3-spells:' + entry['id']
        if asset_id in deleted:
            continue
        layers = {}
        for field in ('original', 'mask', 'color', 'background', 'pixel'):
            path = folder / entry[field+'File']
            if path.parent != folder or hashlib.sha256(path.read_bytes()).hexdigest() != entry[field+'Sha256']:
                raise ValueError(f'Invalid spell art layer: {path}')
            with Image.open(path) as pixels:
                if pixels.size != ((32, 32) if field == 'pixel' else (64, 64)):
                    raise ValueError(f'Invalid spell art size: {path}')
                layers[field] = pixels.convert('RGBA')
        # PNG shows prepared strokes and the neutral background, without a glyph preview.
        preview = Image.alpha_composite(layers['background'], layers['color'])
        buffer = BytesIO()
        preview.save(buffer, format='PNG')
        raw = buffer.getvalue()
        digest = hashlib.sha256(raw).hexdigest()
        target = review / 'imported/bg3-spells/icons' / (entry['id']+'-'+digest[:12]+'.png')
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() and target.read_bytes() != raw:
            raise ValueError(f'Edited spell preview preserved: {target}')
        if not target.exists():
            target.write_bytes(raw)
        path = target.relative_to(review).as_posix()
        mapping = entry.get('spellMapping', dict(kind='exact', sourceName=entry['name'], reason='Verified named BG3 icon.'))
        thematic = mapping['kind'] == 'thematic'
        assets.append(dict(id=asset_id, pack='bg3-spells', label=entry['name'], nameColor=entry['nameColor'], spellMapping=mapping, path=path, preview=path,
            sha256=digest, sourceSha256=entry['originalSha256'], width=64, height=64, sourceWidth=64, sourceHeight=64,
            hasTransparency=True, visibleBounds=preview.getbbox(), usages=['spell'], proposedUsages=['spell'], inferredKind='spell',
            styleId='fantasy.braille.lines.64', identityFamily=asset_id, variantFamily=asset_id,
            framing='isolated_symbol', gender='unknown', species='unknown',
            descriptionEn=f"{entry['name']} spell icon; cleaned colored strokes on a neutral background. School: {entry['school']}." +
                (f" Thematic BG3 source: {mapping['sourceName']}. {mapping['reason']}" if thematic else '') +
                (f" Variant: {entry['variant']}." if entry.get('variant') else ''),
            traits={}, technicalStatus='passed', semanticStatus='visually_described' if thematic else 'named_source_match', artStatus='needs_in_game_review',
            metadataSource='bg3_spell_manifest', transform='binary_lines_original_rgb_neutral_background',
            provenance=dict(sourceKind='bg3_spell_lines',
                origin='Baldur\'s Gate 3 artwork by Larian; supplemental original icon from bg3.wiki.' if entry.get('sourceFile') else data['origin'],
                license=data['license'],
                sourcePath=(folder / entry['originalFile']).relative_to(root).as_posix(),
                member=entry['sourceSheet'] + ('#' + str(entry['sourceTile']) if entry['sourceTile'] is not None else ''), referenceUrl=entry['referenceUrl'])))
    return assets
