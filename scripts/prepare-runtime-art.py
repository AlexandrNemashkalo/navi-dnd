"""Package explicitly reviewed native 32px art and named bindings for the game.

The review catalog is an input to this offline build, never a runtime dependency.
Spells keep their dedicated colored-Braille pipeline and cannot enter item searches.
"""
import hashlib
import json
import re
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
LIBRARY = ROOT / 'src/ArtLibrary'
REVIEW = ROOT / 'artifacts/art-catalog-review'


def prepare():
    assets = {a['id']: a for a in json.loads((REVIEW / 'assets.json').read_text(encoding='utf-8'))}
    requirements = json.loads((REVIEW / 'requirements.json').read_text(encoding='utf-8'))
    approved = {a['assetId']: a['sha256'] for a in json.loads((LIBRARY / 'runtime-review.json').read_text(encoding='utf-8'))['assets']}
    images, bindings, engine_aliases, credits = {}, [], {}, {}
    furniture_source = (ROOT/'src/NaviDnD.Data/Models/Furniture.cs').read_text(encoding='utf-8-sig')
    furniture_icons = dict(re.findall(r'new FurnitureKind\("([^"]+)"[^\n]*?"([^"\n]+/[^"\n]+)"', furniture_source))
    databases = {}
    for requirement in requirements:
        category = requirement['category']
        if category == 'spell':
            continue
        # Candidate order is an authored priority list; never search filenames at runtime.
        candidates = [c for c in requirement.get('candidates', [])
                      if approved.get(c['assetId']) == c['sha256'] and c['assetId'] in assets]
        candidates = [c for c in candidates if assets[c['assetId']].get('humanReview', {}).get('status')
                      not in ('rejected', 'needs_generation')]
        if not candidates:
            continue
        candidate = candidates[0]
        asset = assets[candidate['assetId']]
        source = (REVIEW / asset['preview']).resolve()
        raw = source.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != candidate['sha256'] or asset.get('semanticStatus') != 'visually_described':
            raise ValueError('Changed or unreviewed artwork: ' + asset['id'])
        with Image.open(source) as image:
            if image.size != (32, 32):
                raise ValueError('Runtime art must be native 32x32: ' + asset['id'])
        relative = 'Runtime/Images/' + digest + '.png'
        target = LIBRARY / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists():
            target.write_bytes(raw)
        elif target.read_bytes() != raw:
            raise ValueError('Modified packaged artwork: ' + relative)
        usage = candidate['usage']
        if usage not in ('item', 'map_actor', 'portrait', 'prop', 'terrain_tile', 'place_view'):
            raise ValueError('Unsupported reviewed usage: ' + usage)
        images[digest] = dict(id=digest, path=relative, sha256=digest, width=32, height=32,
                             sourceAssetId=asset['id'], description=asset.get('descriptionEn', ''),
                             provenance=asset.get('provenance', {}))
        provenance = asset.get('provenance', {})
        credits.setdefault(asset['pack'], provenance)
        license_file = provenance.get('licenseFile')
        if license_file:
            original = (LIBRARY/license_file).resolve()
            if not original.is_relative_to(LIBRARY.resolve()) or not original.is_file():
                raise ValueError('Missing or unsafe source license: '+asset['id'])
            license_target = LIBRARY/'Runtime/Licenses'/asset['pack']/'LICENSE.txt'
            license_target.parent.mkdir(parents=True, exist_ok=True)
            license_target.write_bytes(original.read_bytes())
        names = list(dict.fromkeys(n for n in [requirement.get('nameEn'), requirement['name'], requirement.get('nameRu')] if n))
        categories = {'item'} if usage == 'item' else {'creature'} if usage in ('map_actor', 'portrait') else {'feature'}
        categories.add(category)
        source_ref = requirement.get('source', '')
        details = {}
        if '#' in source_ref:
            filename, selector = source_ref.split('#', 1)
            if filename not in databases:
                databases[filename] = json.loads((ROOT / filename).read_text(encoding='utf-8-sig'))
            collection, index = selector.split('/')
            details = databases[filename][collection][int(index)].get('en', {})
        kind = str(details.get('type', '')).casefold()
        if usage == 'item':
            if 'weapon' in kind:
                categories.add('weapon')
            if 'armor' in kind or 'armour' in kind:
                categories.add('armor')
            for token in ('shield', 'helmet', 'cloak', 'boots', 'gloves', 'potion', 'scroll', 'ring', 'amulet', 'wand', 'staff', 'book', 'food'):
                if re.search(r'\b' + token + r'\b', requirement['name'], re.I) or token in kind:
                    categories.add(token)
        bindings.append(dict(key=requirement['id'], names=names, categories=sorted(categories),
                             usage=usage, imageId=digest, match=candidate['match']))
        if category == 'furniture' and requirement['name'] in furniture_icons:
            engine_aliases[furniture_icons[requirement['name']]] = digest
    portraits = []
    for entry in json.loads((LIBRARY/'portrait-gallery.json').read_text(encoding='utf-8'))['portraits']:
        asset = assets.get(entry['assetId'])
        if not asset or asset.get('humanReview', {}).get('status') in ('rejected', 'needs_generation'):
            continue
        if asset['sha256'] != entry['sha256'] or asset.get('framing') not in ('head', 'head_shoulders') or 'portrait' not in asset['usages']:
            raise ValueError('Changed portrait identity or framing: '+entry['assetId'])
        raw = (REVIEW/asset['preview']).read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != entry['sha256']:
            raise ValueError('Changed portrait bytes: '+entry['assetId'])
        with Image.open(REVIEW/asset['preview']) as image:
            if image.size != (32,32):
                raise ValueError('Portrait is not native 32px: '+entry['assetId'])
        relative = 'Runtime/Images/'+digest+'.png'
        target_image = LIBRARY/relative
        if target_image.exists() and target_image.read_bytes() != raw:
            raise ValueError('Modified packaged portrait: '+digest)
        target_image.write_bytes(raw)
        images[digest] = dict(id=digest, path=relative, sha256=digest, width=32, height=32,
            sourceAssetId=asset['id'], description=asset.get('descriptionEn',''), provenance=asset.get('provenance',{}))
        credits.setdefault(asset['pack'], asset.get('provenance',{}))
        portraits.append(dict(imageId=digest, framing=asset['framing'], group=entry['group']))
    payload = dict(schemaVersion=1, ruleSet='Dnd5e', images=sorted(images.values(), key=lambda x: x['id']),
                   bindings=bindings, engineAliases=engine_aliases, portraits=portraits)
    payload['revision'] = hashlib.sha256(json.dumps(payload, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
    target = LIBRARY / 'RuleSets/Dnd5e/runtime-pictures.json'
    text = json.dumps(payload, ensure_ascii=False, indent=2) + '\n'
    if not target.exists() or target.read_text(encoding='utf-8') != text:
        target.write_text(text, encoding='utf-8')
    attribution = '\n\n'.join(pack+'\n'+json.dumps(info,ensure_ascii=False,indent=2) for pack,info in sorted(credits.items()))
    (LIBRARY/'Runtime/ATTRIBUTION.txt').write_text('NaviDnD: source credits for bundled reference illustrations.\n\n'+attribution+'\n',encoding='utf-8')
    print(f'Packaged {len(images)} reviewed images for {len(bindings)} named requirements. Spells remain separate.')


if __name__ == '__main__':
    prepare()
