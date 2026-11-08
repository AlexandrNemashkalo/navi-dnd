"""Build the art review page and sync pinned spell bindings; never inspect player saves.

Run: python scripts/audit-art-catalog.py
Requires Pillow. Output: artifacts/art-catalog-review (not a runtime catalog).
"""
import hashlib
import io
import json
import re
from collections import Counter
from pathlib import Path
from PIL import Image
from art_review_metadata import load_reviews, apply_review
from art_visual_metadata import load_annotations, apply_annotation
from art_requirement_bindings import load_bindings, save_bindings, attach_bindings, apply_reviewed_usages
from art_change_history import update_history
from art_own_assets import load_own_assets
from art_spell_assets import load_spell_assets, merge_spell_bindings
from art_external_assets import load_external_assets
from art_catalog_names import entry_names, add_russian_names

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/art-catalog-review'
OUT.mkdir(parents=True, exist_ok=True)

def dump(name, value):
    target=OUT/name
    temporary=target.with_suffix(target.suffix+'.new')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(target)

def proposed_usage(path):
    # Routing rules informed by the contact-sheet survey. Not semantic approval.
    if path.startswith('dcss/monster/nonliving/test_spawner'):
        return ['excluded_test_asset']
    if path.startswith(('dcss/monster/panlord/', 'dcss/emissaries/')) or re.search(r'_(top|bottom)(?:_|\.)', path):
        return ['layer']
    if path.startswith('dcss/monster/tentacles/'):
        return ['segment']
    if path.startswith('dcss/monster/statues/'):
        return ['prop']
    if path.startswith('dcss/monster/'):
        return ['map_actor']
    if path.startswith(('dcss/player/felids/', 'dcss/player/transform/')):
        return ['map_actor']
    if path.startswith('dcss/player/'):
        return ['layer']
    if path.startswith('dcss/item/'):
        return ['item']
    if path.startswith('dcss/dungeon/'):
        if any('/'+part+'/' in path for part in ('floor','wall','water')):
            return ['terrain_tile']
        return ['prop_or_overlay']
    if path.startswith('dcss/effect/'):
        return ['effect']
    if path.startswith('dcss/gui/spells/components/'):
        return ['composition_component']
    if path.startswith('dcss/gui/spells/'):
        return ['spell']
    if path.startswith('dcss/misc/'):
        return ['overlay_or_ui']
    return ['ui']

assets = []
audit_path = OUT / 'pixel-audit.json'
technical = {r['path']: r for r in json.loads(audit_path.read_text(encoding='utf-8'))} if audit_path.exists() else {}
for path in sorted((ROOT / 'src/icons/dcss').rglob('*')):
    if path.suffix.lower() not in {'.png', '.svg', '.jpg', '.webp'}:
        continue
    try:
        source_bytes=path.read_bytes()
    except FileNotFoundError:
        continue  # The review server may remove a file during this snapshot.
    relative = path.relative_to(ROOT / 'src/icons').as_posix()
    pixel = relative.startswith('dcss/')
    kind = ('layer' if relative.startswith('dcss/player/') else
            'creature_sprite' if relative.startswith('dcss/monster/') else
            'item' if relative.startswith('dcss/item/') else
            'environment' if relative.startswith('dcss/dungeon/') else
            'effect_or_ui' if relative.startswith(('dcss/gui/', 'dcss/effect/', 'dcss/misc/')) else
            'unclassified')
    record = dict(id=relative.rsplit('.', 1)[0], path=relative,
                  pack='dcss' if pixel else 'game-icons', inferredKind=kind,
                  label=re.sub(r'[-_]', ' ', path.stem),
                  framing='unknown', gender='unknown', description=None,
                  status='unreviewed', metadataSource='filename_only',
                  proposedUsages=proposed_usage(relative),
                  sha256=hashlib.sha256(source_bytes).hexdigest(),
                  preview='../../src/icons/' + relative)
    if relative in technical:
        record.update(technical[relative])
    elif path.suffix.lower() != '.svg':
        with Image.open(io.BytesIO(source_bytes)) as im:
            alpha = im.convert('RGBA').getchannel('A')
            record.update(width=im.width, height=im.height,
                          hasTransparency=alpha.getextrema()[0] < 255,
                          visibleBounds=alpha.getbbox())
    assets.append(record)

imported_manifest = OUT / 'imported-assets.json'
portrait_manifest = OUT / 'craftpix-portraits/manifest.json'
if imported_manifest.exists():
    assets.extend(json.loads(imported_manifest.read_text(encoding='utf-8')))
elif portrait_manifest.exists():
    for portrait in json.loads(portrait_manifest.read_text(encoding='utf-8')):
        assets.append(dict(portrait, inferredKind='portrait', label=portrait['descriptionEn'],
                           preview='craftpix-portraits/' + portrait['path'],
                           metadataSource='visual_contact_sheet',status='described'))

prepared_manifest = OUT / 'prepared-assets.json'
if prepared_manifest.exists():
    assets.extend(json.loads(prepared_manifest.read_text(encoding='utf-8')))
reviews = load_reviews(ROOT)
assets.extend(load_own_assets(ROOT))
assets.extend(load_spell_assets(ROOT))
assets.extend(load_external_assets(ROOT))
annotations = load_annotations(ROOT)
assets = [apply_review(apply_annotation(a, annotations), reviews) for a in assets]

deleted_path = OUT / 'deletions.json'
deleted = json.loads(deleted_path.read_text(encoding='utf-8')) if deleted_path.exists() else {}
assets = [a for a in assets if a['id'] not in deleted]

requirements = []
sources = [('DnD5e_equipment_BD.dtn', 'itemsList', 'equipment'),
           ('DnD5e_magic_equipment_BD.dtn', 'itemsList', 'magic_item'),
           ('DnD5e_monsters_BD.dtn', 'dataList', 'monster'),
           ('DnD5e_spells_BD.dtn', 'allSpells', 'spell')]
for filename, key, kind in sources:
    data = json.loads((ROOT / 'src/GameData' / filename).read_text(encoding='utf-8-sig'))
    for index, item in enumerate(data[key]):
        name, name_ru = entry_names(item)
        requirements.append(dict(id=f'dnd5e:{kind}:{index}', category=kind,
                                 name=name, nameRu=name_ru, source=f'src/GameData/{filename}#{key}/{index}',
                                 status='needs_mapping', assetId=None,
                                 note='Reference entry; not necessarily a separate image.'))
options = json.loads((ROOT / 'src/NaviDnD/Prompts/Dnd5e/heroOptions.json').read_text(encoding='utf-8-sig'))
for race in options['races']:
    for gender in ('male', 'female'):
        requirements.append(dict(id=f"dnd5e:portrait:{race['name']}:{gender}",
                                 category='portrait', name=f"{race['name']} / {gender}",
                                 source='heroOptions.json', status='needs_mapping', assetId=None,
                                 requiredFraming='face_or_bust', variantsTarget=4))
for cls in options['classes']:
    requirements.append(dict(id=f"dnd5e:class-look:{cls['name']}", category='class_look',
                             name=cls['name'], source='heroOptions.json', status='needs_mapping', assetId=None,
                             note='Clothing/gear preference; does not prove a portrait character class.'))
world_source = (ROOT / 'src/NaviDnD.Data/Models/World.cs').read_text(encoding='utf-8-sig')
places_section = world_source.split('public static class WorldPlaceTypes', 1)[1].split('public class WorldKingdom', 1)[0]
for key, value in re.findall(r'public const string (\w+) = "([^"]+)";', places_section):
    requirements.append(dict(id=f'engine:place:{value}', category='place_view', name=value,
                             source='WorldPlaceTypes', status='needs_art_direction',assetId=None,
                             note='A coherent scene/vignette, not an individual floor tile or item.'))
biome_section = world_source.split('public static class WorldBiomes', 1)[1].split('public static readonly', 1)[0]
for key, code in re.findall(r"(\w+)\s*=\s*'([^']+)'", biome_section):
    requirements.append(dict(id=f'engine:biome:{key}', category='biome_view', name=key,
                             code=code, source='WorldBiomes', status='needs_art_direction',assetId=None))
furniture_source = (ROOT / 'src/NaviDnD.Data/Models/Furniture.cs').read_text(encoding='utf-8-sig')
for key in re.findall(r'new FurnitureKind\("([^"]+)"', furniture_source):
    requirements.append(dict(id=f'engine:furniture:{key}',category='furniture',name=key,
                             source='FurnitureCatalog',status='needs_mapping',assetId=None))
terrain_source = (ROOT / 'src/NaviDnD.Data/Models/Terrain.cs').read_text(encoding='utf-8-sig')
for label in dict.fromkeys(re.findall(r'''new TerrainKind\('(?:\\.|[^'\\])',\s*"([^"]+)"''',terrain_source)):
    requirements.append(dict(id=f'engine:terrain:{label}',category='terrain',name=label,
                             source='TerrainCatalog',status='needs_mapping',assetId=None))
common = {
 'npc_role': 'innkeeper merchant blacksmith healer guard farmer hunter sailor priest mage noble beggar bandit cultist scholar bard guide child elder',
 'creature_priority': 'goblin kobold orc hobgoblin bugbear wolf rat spider skeleton zombie ghoul ghost troll ogre mimic slime dragon',
 'prop_priority': 'chest door key torch lantern rope backpack bed table barrel altar trap campfire tent cart boat',
}
for category, words in common.items():
    for word in words.split():
        requirements.append(dict(id=f'curated:{category}:{word}', category=category,
                                 name=word, source='editorial_starter_list',
                                 status='needs_mapping', assetId=None,
                                 note='Suggested priority, not measured gameplay frequency.'))
add_russian_names(ROOT, requirements, assets)
apply_reviewed_usages(ROOT, assets)
bindings = merge_spell_bindings(ROOT, load_bindings(ROOT), assets)
attach_bindings(requirements, assets, bindings)
save_bindings(ROOT, bindings)
summary = dict(assets=len(assets), packs=dict(Counter(a['pack'] for a in assets)),
               inferredKinds=dict(Counter(a['inferredKind'] for a in assets)),
               requirements=dict(Counter(r['category'] for r in requirements)),
               describedPortraits=sum(a.get('semanticStatus') == 'visually_described' and 'portrait' in a.get('usages',[]) for a in assets),
               runtimeApprovedAssets=0, confirmedPortraitCoverage=None,
               proposedUsages=dict(Counter(u for a in assets for u in a.get('proposedUsages',a.get('usages',[])))))
summary['dcssDetailedReview'] = dict(
    described=sum(a['pack']=='dcss' and a.get('semanticStatus')=='visually_described' for a in assets),
    pending=sum(a['pack']=='dcss' and a.get('semanticStatus')!='visually_described' for a in assets),
    removalSuggestions=sum(a.get('retentionReview',{}).get('recommendation')=='review_removal' for a in assets),
    oversizedParts=sum(a.get('composition',{}).get('mode')=='oversized_part' for a in assets))
summary['requirementMapping']=dict(withCandidates=sum(bool(r['candidates']) for r in requirements),
    candidateLinks=sum(len(r['candidates']) for r in requirements),runtimeAssignments=0)
spell_art = [a for a in assets if a['pack'] == 'bg3-spells']
summary['spellArt'] = dict(total=len(spell_art),
    exact=sum(a['spellMapping']['kind'] == 'exact' for a in spell_art),
    thematic=sum(a['spellMapping']['kind'] == 'thematic' for a in spell_art))
summary['requirementMapping']['byCategory'] = {}
for category in sorted(summary['requirements']):
    rows = [r for r in requirements if r['category'] == category]
    summary['requirementMapping']['byCategory'][category] = dict(
        total=len(rows), withCandidates=sum(bool(r['candidates']) for r in rows),
        withoutCandidates=sum(not r['candidates'] for r in rows),
        fixedSpellIcons=sum(r.get('selectionPolicy', {}).get('mode') == 'fixed_spell_icon'
                            and bool(r['candidates']) for r in rows))
survey_path=OUT/'visual-survey.json'
if survey_path.exists():
    summary['visualSurvey']=json.loads(survey_path.read_text(encoding='utf-8'))
dependencies_path=OUT/'dependencies.json'
if dependencies_path.exists():
    dependency_report=json.loads(dependencies_path.read_text(encoding='utf-8'))
    uses={r['path']:r['usedBy'] for r in dependency_report['references']}
    for asset in assets:
        asset['usedBy']=uses.get(asset['path'],[])
    summary['missingReferencedFiles']=dependency_report['missingFiles']
dump('assets.json', assets)
dump('requirements.json', requirements)
dump('summary.json', summary)
# Inline data works under file:// in Windows and macOS without a server or fetch.
sheets_path = OUT / 'contact-sheets/index.json'
sheets = json.loads(sheets_path.read_text(encoding='utf-8')) if sheets_path.exists() else []
changes = update_history(ROOT/'src/ArtLibrary/change-history.json', requirements)
payload = json.dumps(dict(assets=assets, requirements=requirements, summary=summary, sheets=sheets, changes=changes), ensure_ascii=False).replace('<', '\\u003c')
page = (ROOT / 'scripts/art-review-template.html').read_text(encoding='utf-8')
page = page.replace('RENDER_CODE', (ROOT / 'scripts/art-review-render.js').read_text(encoding='utf-8'))
temporary=OUT/'index.html.new'
temporary.write_text(page.replace('PAYLOAD', payload), encoding='utf-8')
temporary.replace(OUT/'index.html')
print(json.dumps(summary, indent=2))
