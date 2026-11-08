"""Publish only individually inspected full-body source-name monster proposals."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
proposals=json.loads((ROOT/'artifacts/art-completion/monster-proposals.json').read_text(encoding='utf-8'))
assets={a['id']:a for a in json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))}
# These source names depict a different anatomy or an incorporeal creature in DCSS.
excluded={'Guardian Naga','Lamia','Lemure','Revenant'}
document=load_bindings(ROOT);known={(b['category'],b['name']) for b in document['bindings']}
usage_path=LIB/'reviewed-usages.json';usages=json.loads(usage_path.read_text(encoding='utf-8'));reviewed_usages={a['assetId']:a for a in usages['assets']}
review_path=LIB/'runtime-review.json';review=json.loads(review_path.read_text(encoding='utf-8'));reviewed={a['assetId'] for a in review['assets']}
requirements={(r['category'],r['name']) for r in json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))}
added=0
for index, row in enumerate(proposals,1):
    if row['nameEn'] in excluded:continue
    asset=assets.get(row['assetId'])
    if not asset or asset['sha256']!=row['sha256']:continue
    if asset['id'] not in reviewed_usages:
        usages['assets'].append(dict(assetId=asset['id'],sha256=asset['sha256'],usages=['map_actor'],
            reason='Individually inspected complete creature silhouette; source-name anatomy matches '+row['nameEn']+'.'))
    if asset['id'] not in reviewed:
        review['assets'].append(dict(assetId=asset['id'],sha256=asset['sha256'],sheet='monster-exact-review.png',position=index,
            method='Inspected complete native-32 creature; incompatible source-name lookalikes excluded.'))
    for category,name in [('monster',row['name']),('creature_priority',row['nameEn'].lower())]:
        if (category,name) in known or (category,name) not in requirements:continue
        document['bindings'].append(dict(category=category,name=name,
            selectionPolicy=dict(mode='balanced_variants',preserveAssignment=True,requiredChecks=['usage','source_hash','subject_facts']),
            note='Complete creature-form representation. D&D rules, scale and abilities come only from the bestiary.',
            candidates=[dict(assetId=asset['id'],sha256=asset['sha256'],usage='map_actor',match='visual_representation',
                sourcePriority=1,reason='Individually reviewed complete '+row['nameEn']+' silhouette; not a spell, portrait, statue or body fragment.')]))
        known.add((category,name));added+=1
save_bindings(ROOT,document)
usage_path.write_text(json.dumps(usages,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Added reviewed creature correspondences:',added)
