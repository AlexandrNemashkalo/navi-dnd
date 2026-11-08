"""Publish only the explicitly inspected generated icons listed below."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
rows=[
    ('ivory-soap-v1', [('equipment','Soap')], 'A simple ivory bar of soap with a shallow top indentation.'),
    ('wooden-ladder-v1', [('equipment','Ladder (10-foot)')], 'A complete wooden ladder with six rungs; physical length remains an item property.'),
    ('brass-bell-v1', [('equipment','Bell')], 'A complete hand bell with a wooden handle and visible clapper.'),
    ('leather-waterskin-v1', [('equipment','Waterskin')], 'A complete soft leather water container with cork and loop.'),
    ('iron-crowbar-v1', [('equipment','Crowbar')], 'A complete hooked iron pry bar with a flat tip.'),
    ('green-bedroll-v1', [('equipment','Bedroll')], 'A complete rolled blanket secured with two leather straps.'),
    ('leather-quiver-v1', [('equipment','Quiver'),('magic_item','QUIVER OF EHLONNA')], 'A leather quiver holding arrows; magical capacity remains an item property.'),
    ('plains-horizon-v3', [('biome_view','Plains'),('biome_view','Grass')], 'Open grassy plains shown toward the horizon; a landscape view, not a tactical ground texture.'),
    ('sea-horizon-v1', [('biome_view','Sea'),('biome_view','Ocean')], 'Open blue water and a distant horizon; a landscape view, not a tactical water texture.'),
]
document=load_bindings(ROOT)
known={(b['category'],b['name']) for b in document['bindings']}
requirements={(r['category'],r['name']) for r in json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))}
review_path=LIB/'runtime-review.json'
review=json.loads(review_path.read_text(encoding='utf-8'))
reviewed={x['assetId'] for x in review['assets']}
added=0
for key,targets,reason in rows:
    path=LIB/'Sources/own-art'/(key+'.provenance.json')
    if not path.exists():continue
    asset=json.loads(path.read_text(encoding='utf-8'))
    assert (asset['width'],asset['height'])==(32,32)
    for category,name in targets:
        if (category,name) not in requirements:raise ValueError('Unknown requirement '+name)
        if (category,name) in known:continue
        document['bindings'].append(dict(category=category,name=name,
            candidates=[dict(assetId=asset['id'],sha256=asset['sha256'],usage=asset['usage'],
                match='visual_representation',sourcePriority=0,reason=reason)],
            selectionPolicy=dict(mode='balanced_variants',preserveAssignment=True,requiredChecks=['usage','source_hash','subject_facts']),
            note='Original generated artwork, explicitly inspected after conversion to exact 32x32.'))
        known.add((category,name));added+=1
    if asset['id'] not in reviewed:
        review['assets'].append(dict(assetId=asset['id'],sha256=asset['sha256'],
            method='Inspected final 32x32 silhouette and identifying details at nearest-neighbor enlargement.',
            sheet=key+'.png'))
save_bindings(ROOT,document)
review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Added generated correspondences:',added)
