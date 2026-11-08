"""Reviewed market, workshop and harbor objects; never replace player selections."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT/'src/ArtLibrary'
external_path = LIB/'external-assets.json'
external = json.loads(external_path.read_text(encoding='utf-8'))
for asset in external:
    if asset['id'] == 'harbor-props-32:rowboat':
        asset['usages'] = ['prop', 'item']
external_path.write_text(json.dumps(external, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
assets = {a['id']: a for a in json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))}
assets['harbor-props-32:rowboat']['usages'] = ['prop', 'item']
requirements = {(r['category'], r['name']) for r in json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))}
document = load_bindings(ROOT)
known = {(b['category'], b['name']): b for b in document['bindings']}
added = []

def bind(category, name, ids, usage, reason):
    key = category, name
    if key not in requirements:
        raise ValueError('Unknown requirement '+str(key))
    candidates = []
    for asset_id in ids.split('|'):
        asset = assets[asset_id]
        if usage not in asset['usages']:
            raise ValueError('Wrong usage '+asset_id)
        candidates.append(dict(assetId=asset_id, sha256=asset['sha256'], usage=usage,
                               match='visual_representation', sourcePriority=1 if asset_id.startswith('dcss/') else 0, reason=reason))
    if key in known:
        # Only upgrade our previous explicitly documented plain-table stall approximation.
        previous = known[key]
        if name.lower() != 'stall' or not all(c['assetId'].startswith('wizard-furniture-32:long_table_') for c in previous['candidates']):
            return
        previous['candidates'] = candidates
        previous['note'] = reason
    else:
        row = dict(category=category, name=name, candidates=candidates,
                   selectionPolicy=dict(mode='balanced_variants', seedScope='world_and_subject',
                       preserveAssignment=True, requiredChecks=['usage','source_hash','subject_facts']),
                   note='Reviewed object shape; magical powers and exact materials remain reference facts.')
        document['bindings'].append(row)
        known[key] = row
    added.append(key)

for category, name in [('furniture','stall'),('terrain','Stall')]:
    bind(category,name,'village-market-32:red_awning_stall|village-market-32:blue_awning_stall|village-market-32:green_awning_stall','prop','Complete market stalls with wooden counters and striped awnings.')
bind('prop_priority','cart','village-market-32:handcart','prop','Complete wooden handcart with wheels and handles.')
bind('prop_priority','boat','harbor-props-32:rowboat|harbor-props-32:canoe|harbor-props-32:sailboat_red','prop','Complete boats in side view, not a navigation symbol.')
bind('equipment',"Scale, merchant's",'village-market-32:scales','item','Recognizable balance with two weighing pans.')
bind('equipment','Lock','workshop-tools-32:padlock|village-market-32:padlock','item','Complete padlock with shackle and keyhole.')
bind('equipment','Pot, iron','shikashi-utility-32:cauldron','item','Complete dark cooking pot with an open mouth.')
bind('magic_item','LOCK OF TRICKERY','workshop-tools-32:padlock','item','Complete padlock; its trickery is a hidden magical property.')
bind('magic_item','SADDLE OF THE CAVALIER','village-market-32:horse_saddle','item','Complete leather saddle; magical properties are not part of its silhouette.')
bind('magic_item','HORSESHOES OF SPEED','workshop-tools-32:horseshoe','item','U-shaped horseshoe with nail holes; represents the set of horseshoes.')
bind('magic_item','HORSESHOES OF A ZEPHYR','workshop-tools-32:horseshoe','item','U-shaped horseshoe with nail holes; represents the set of horseshoes.')
bind('magic_item','FOLDING BOAT','harbor-props-32:rowboat','item','Shows the complete boat in its unfolded form.')
extra_reviews = {
    'dcss/item/misc/misc_box': ('item', 'A complete grey cube with visible top, front and side faces.'),
    'dcss/monster/holy/paladin': ('map_actor', 'A complete humanoid in gold armor with a decorated chest; an armor appearance, not proof of class.'),
    'craftpix-986554:icon-038': ('item', 'A complete brown humanoid doll, with limbs and stitched features.'),
    'prepared-items-native-32-01-loot:icon-026': ('item', 'A complete pale eye with a pink iris; represents the prosthetic eye shape.'),
}
usage_path = LIB/'reviewed-usages.json'
usages = json.loads(usage_path.read_text(encoding='utf-8'))
usage_ids = {a['assetId'] for a in usages['assets']}
for asset_id, (usage, reason) in extra_reviews.items():
    asset = assets[asset_id]
    asset['usages'] = sorted(set(asset.get('usages', [])) | {usage})
    if asset_id not in usage_ids:
        usages['assets'].append(dict(assetId=asset_id, sha256=asset['sha256'], usages=[usage], reason=reason))
bind('magic_item','CUBE OF FORCE','dcss/item/misc/misc_box','item',extra_reviews['dcss/item/misc/misc_box'][1])
bind('magic_item','CUBIC GATE','dcss/item/misc/misc_box','item',extra_reviews['dcss/item/misc/misc_box'][1])
bind('magic_item','TALKING DOLL','craftpix-986554:icon-038','item',extra_reviews['craftpix-986554:icon-038'][1])
bind('magic_item','ERSATZ EYE','prepared-items-native-32-01-loot:icon-026','item',extra_reviews['prepared-items-native-32-01-loot:icon-026'][1])
bind('class_look','Paladin','dcss/monster/holy/paladin','map_actor',extra_reviews['dcss/monster/holy/paladin'][1])
for role, numbers, reason in [
    ('innkeeper', [3,6,10,20], 'Ordinary townspeople in simple clothing, offered as role illustrations; occupation is supplied by game facts.'),
    ('merchant', [27,29,31,39], 'Townspeople wearing decorative hats or collars; a role illustration, not an inferred profession or wealth.'),
    ('elder', [5,17,22,41], 'Faces with visibly grey hair or beards and age lines; no specific chronological age is assigned.'),
]:
    ids = ['prepared-portraits-native-32-humans:icon-'+str(i).zfill(3) for i in numbers]
    bind('npc_role', role, '|'.join(ids), 'portrait', reason)
    for asset_id in ids:
        extra_reviews[asset_id] = ('portrait', reason)
usage_path.write_text(json.dumps(usages, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
save_bindings(ROOT, document)
review_path = LIB/'runtime-review.json'
review = json.loads(review_path.read_text(encoding='utf-8'))
reviewed = {a['assetId'] for a in review['assets']}
for asset_id, (usage, reason) in extra_reviews.items():
    if asset_id not in reviewed:
        review['assets'].append(dict(assetId=asset_id, sha256=assets[asset_id]['sha256'],
            sheet='humans-gallery-review.png' if usage == 'portrait' else 'roles-extras-review.png', method=reason))
for asset in external:
    pack = asset['pack']
    if pack not in {'village-market-32','workshop-tools-32','harbor-props-32'} or asset['id'] in reviewed:
        continue
    review['assets'].append(dict(assetId=asset['id'], sha256=asset['sha256'],
        sheet={'village-market-32':'market-sheet.png','workshop-tools-32':'tools-sheet.png','harbor-props-32':'harbor-sheet.png'}[pack],
        method='Inspected full native 32px object and matched author filename; transparent background.'))
review_path.write_text(json.dumps(review, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('Added or improved correspondences:', len(added))
