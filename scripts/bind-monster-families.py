"""Apply reviewed anatomical analogues; excluded lookalikes stay unmapped."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT=Path(__file__).resolve().parents[1];LIB=ROOT/'src/ArtLibrary'
proposals=json.loads((ROOT/'artifacts/art-completion/monster-family-proposals.json').read_text(encoding='utf-8'))
excluded={3:'Domestic pig is not a wild boar',11:'Sheep is not a goat',13:'Turtle is not a seahorse',
    15:'Unhaired elephant is not a woolly mammoth',17:'Human skeleton lacks the minotaur skull',
    31:'Pale elf does not match the drow appearance',32:'Pale elf does not match the drow appearance',
    33:'Wrong drow appearance and priestess silhouette',43:'Purple dragon does not match red dragon colouring',
    46:'Not a clear ape-like demon silhouette',47:'Winged devil conflicts with wingless body forms',
    51:'Winged demon is not a frog-like slaad',55:'Fire vortex is not a snake',56:'Red slime is not grey ooze'}
document=load_bindings(ROOT);known={(b['category'],b['name']) for b in document['bindings']}
up=LIB/'reviewed-usages.json';usages=json.loads(up.read_text(encoding='utf-8'));seen_usages={a['assetId'] for a in usages['assets']}
rp=LIB/'runtime-review.json';review=json.loads(rp.read_text(encoding='utf-8'));seen_reviews={a['assetId'] for a in review['assets']}
added=0
for number,row in enumerate(proposals,1):
    if number in excluded:continue
    asset_id=row['assetId'];digest=row['sha256']
    if asset_id not in seen_usages:
        usages['assets'].append(dict(assetId=asset_id,sha256=digest,usages=['map_actor'],reason='Complete creature body individually inspected in the anatomy-family review.'))
        seen_usages.add(asset_id)
    if asset_id not in seen_reviews:
        review['assets'].append(dict(assetId=asset_id,sha256=digest,sheet=f'monster-families-{(number-1)//48+1}.png',position=number,
            method='Individually checked complete silhouette, body plan and defining colours; anatomically incompatible proposals excluded.'))
        seen_reviews.add(asset_id)
    for need in row['requirements']:
        key='monster',need['name']
        if key in known:continue
        document['bindings'].append(dict(category='monster',name=need['name'],
            selectionPolicy=dict(mode='balanced_variants',preserveAssignment=True,requiredChecks=['usage','source_hash','subject_facts']),
            note='Reviewed base creature form; age, scale, rank and rules abilities are supplied by the bestiary, not inferred from the illustration.',
            candidates=[dict(assetId=asset_id,sha256=digest,usage='map_actor',match='visual_representation',sourcePriority=1,
                reason='Reviewed anatomical analogue for '+need['nameEn']+'. '+row['sourceDescription'])]))
        known.add(key);added+=1
save_bindings(ROOT,document)
up.write_text(json.dumps(usages,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
rp.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(ROOT/'artifacts/art-completion/monster-family-exclusions.json').write_text(json.dumps(excluded,indent=2),encoding='utf-8')
print('Added anatomically reviewed monster variants:',added)
