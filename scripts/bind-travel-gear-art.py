"""Reviewed travel equipment; preserve the former cart image for existing saves."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT/'src/ArtLibrary'


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


additions_path = LIB/'RuleSets/Dnd5e/runtime-additions.json'
additions = read(additions_path)
extras = [
    dict(id='curated:item:herbalism-kit', category='equipment', nameRu='Набор травника',
         names=['Herbalism kit', 'Herbalist kit', "Herbalist's kit", 'Набор травника', 'Набор для травничества']),
    dict(id='curated:item:travel-satchel', category='equipment', nameRu='Дорожная сума',
         names=['Travel satchel', 'Travel bag', 'Shoulder bag', 'Satchel', 'Дорожная сума', 'Дорожная сумка', 'Сума', 'Сумка через плечо']),
    dict(id='curated:prop_priority:cart', category='prop_priority', nameRu='Телега',
         names=['cart', 'Wooden cart', 'Handcart', 'Телега', 'Деревянная телега', 'Ручная тележка', 'Тележка'],
         engineAliases=['delapouite/old-wagon']),
    dict(id='legacy:prop:handcart', category='prop_priority', nameRu='Прежняя ручная телега',
         names=['Legacy handcart']),
]
by_id = {r['id']: r for r in additions['requirements']}
for extra in extras:
    if extra['id'] in by_id:
        by_id[extra['id']].update(extra)
    else:
        additions['requirements'].append(extra)
write(additions_path, additions)

assets = {a['id']: a for a in read(ROOT/'artifacts/art-catalog-review/assets.json')}
document = load_bindings(ROOT)
known = {(b['category'], b['name']): b for b in document['bindings']}
review_path = LIB/'runtime-review.json'
review = read(review_path)
reviewed = {a['assetId']: a for a in review['assets']}
rows = [
    ('equipment', 'Herbalism kit', 'own-art:herbalist-kit-shaded-v1', 'item'),
    ('equipment', 'Travel satchel', 'prepared-items-native-32-batch2-35-kobold-loot:icon-001', 'item'),
    ('prop_priority', 'cart', 'own-art:cart-shaded-v1', 'prop'),
    ('prop_priority', 'Legacy handcart', 'village-market-32:handcart', 'prop'),
]
for category, name, asset_id, usage in rows:
    if asset_id.startswith('own-art:'):
        asset = read(LIB/'Sources/own-art'/(asset_id.split(':')[1]+'.provenance.json'))
    else:
        asset = assets[asset_id]
    reason = asset['descriptionEn']
    entry = dict(category=category, name=name,
        candidates=[dict(assetId=asset_id, sha256=asset['sha256'], usage=usage,
            match='visual_representation', sourcePriority=0, reason=reason)],
        selectionPolicy=dict(mode='balanced_variants', preserveAssignment=True,
            requiredChecks=['usage', 'source_hash', 'subject_facts']),
        note='Inspected at 32x32 and integer enlargement: complete subject, shaded palette and readable silhouette.')
    if (category, name) in known:
        known[category, name].update(entry)
    else:
        document['bindings'].append(entry)
    if asset_id not in reviewed:
        review['assets'].append(dict(assetId=asset_id, sha256=asset['sha256'],
            method=entry['note'], sheet='artifacts/travel-gear-reviewed.png'))
save_bindings(ROOT, document)
write(review_path, review)
