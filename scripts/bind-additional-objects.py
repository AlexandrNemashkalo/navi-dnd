"""Explicit reviewed furniture, camping gear, utility and musical-item bindings."""
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
external_path=LIB/'external-assets.json'
external=json.loads(external_path.read_text(encoding='utf-8'))
multi={'shikashi-utility-32:tent','wizard-furniture-32:cauldron','wizard-furniture-32:magic_mirror',
       'wizard-furniture-32:rug_purple','wizard-furniture-32:rug_blue','wizard-furniture-32:rug_red','wizard-furniture-32:rug_green'}
for asset in external:
    if asset['id'] in multi:asset['usages']=['prop','item']
external_path.write_text(json.dumps(external,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assets={a['id']:a for a in json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))}
for a in external:
    if a['id'] in multi and a['id'] in assets:assets[a['id']]['usages']=a['usages']
document=load_bindings(ROOT)
known={(b['category'],b['name']) for b in document['bindings']}
requirements={(r['category'],r['name']) for r in json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))}
added=[]


def bind(category,names,pack,keys,usage,reason):
    candidates=[]
    for key in keys.split('|'):
        asset=assets.get(pack+':'+key)
        if not asset:continue  # Never restore a deleted PNG.
        if usage not in asset.get('usages',[]):raise ValueError('Unreviewed artwork role: '+asset['id'])
        candidates.append(dict(assetId=asset['id'],sha256=asset['sha256'],usage=usage,reason=reason,
                               match='visual_representation',sourcePriority=0))
    for name in names.split('|'):
        key=category,name
        if key not in requirements:raise ValueError('Unknown requirement: '+repr(key))
        if key in known:continue
        document['bindings'].append(dict(category=category,name=name,candidates=candidates,
            selectionPolicy=dict(mode='balanced_variants',seedScope='world_and_subject',preserveAssignment=True,
                requiredChecks=['usage','source_hash','subject_facts']),
            note='Explicit object-form match. Game facts define exact dimensions, materials, quantities and hidden powers.'))
        known.add(key);added.append(key)


F='wizard-furniture-32';U='shikashi-utility-32';I='idylwild-inventory-32'
furniture={
    'counter':('desk_oak|desk_dark|desk_ebony','Complete wooden desk/counter with a tabletop and front storage; a base-form counter illustration.'),
    'stall':('long_table_oak|long_table_dark','A simple wooden vendor table; no canopy or displayed goods are assumed.'),
    'table':('round_table_oak|round_table_dark|long_table_oak|long_table_dark','Complete freestanding wooden tables.'),
    'bed':('bed_blue|bed_green|bed_red|bed_purple','Complete side-view beds with frame, mattress and pillow.'),
    'cabinet':('wardrobe_oak|wardrobe_dark|wardrobe_ebony','Complete tall wooden cabinet with doors.'),
    'hearth':('fireplace','Complete stone fireplace with visible flame.'),
    'bench':('bench_oak|bench_dark','Complete freestanding wooden bench with two legs.'),
}
for name,(keys,reason) in furniture.items():
    bind('furniture',name,F,keys,'prop',reason)
    bind('terrain',name.capitalize(),F,keys,'prop',reason)
for name in ['bed','table']:
    keys,reason=furniture[name];bind('prop_priority',name,F,keys,'prop',reason)
bind('prop_priority','campfire',U,'campfire','prop','A complete campfire with wood and flame, not a spell panel.')
bind('prop_priority','tent',U,'tent','prop','A complete tan camping tent shown from the side.')
for name,key in [('Spyglass','spyglass'),('Magnifying glass','magnifying-glass'),('Mirror, steel','mirror'),
                 ('Manacles','manacles'),('Hourglass','hourglass'),('Hunting trap','hunting-trap')]:
    bind('equipment',name,U,key,'item','Complete recognizable '+key.replace('-',' ')+' object, on a transparent background.')
bind('equipment','Tent, two-person',U,'tent','item','Complete side-view camping tent; capacity and measurements remain equipment facts.')
bind('equipment','Paper (one sheet)|Parchment (one sheet)',I,'parchment','item','One rolled light sheet represents the writing material; marks are pictogram decoration, not actual readable content.')
bind('equipment','Sprig of mistletoe',I,'herb','item','Small green leafy herb motif; exact botanical species is supplied by the item reference.')
bind('magic_item','INSTRUMENT OF ILLUSIONS|INSTRUMENT OF SGRIBING|Cli lyre (INSTRUMENT OF THE BARDS)',U,'lyre','item','Complete stringed lyre; the reference supplies the instrument variant and powers.')
bind('magic_item','PIPES OF THE SEWERS|PIPES OF HAUNTING',U,'pan-flute|flute','item','A complete recognizable pipe instrument; sound and magical effects remain game facts.')
bind('magic_item','CHARLATAN’S DIE',U,'dice','item','Clearly visible dice with pips; luck and manipulation are not inferred.')
bind('magic_item','DIMENSIONAL SHACKLES',U,'manacles','item','A complete pair of linked metal wrist cuffs.')
bind('magic_item','MYSTERY KEY',U,'brass-key','item','A complete brass key with loop and toothed stem.')
bind('magic_item','MIRROR OF LIFE TRAPPING',F,'magic_mirror','item','Complete upright oval mirror; size and trapped creatures remain game facts.')
bind('magic_item','CARPET OF FLYING',F,'rug_purple|rug_blue|rug_red|rug_green','item','Complete coloured carpet with an ornamental center; flight is an item property.')
bind('magic_item','DUST OF DISAPPEARANCE|DUST OF DRYNESS|DUST OF SNEEZING AND CHOKING',U,'sack-powder','item','Small pile of loose powder; effects and substances are not revealed by the illustration.')
bind('magic_item','BOWL OF COMMANDING WATER ELEMENTALS',U,'mortar','item','A complete bowl-shaped vessel; accompanying pestle is decorative and not a required spell component.')

save_bindings(ROOT,document)
review_path=LIB/'runtime-review.json';review=json.loads(review_path.read_text(encoding='utf-8'))
reviewed={a['assetId'] for a in review['assets']}
for asset in external:
    if asset['pack'] not in {F,U,I} or asset['id'] in reviewed:continue
    review['assets'].append(dict(assetId=asset['id'],sha256=asset['sha256'],
        sheet='furniture-sheet.png' if asset['pack']==F else 'new-utility-review.png',
        method='Inspected transparent native 32px sprite; coordinate and object identity checked against author source.'))
review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Added explicit utility/furniture correspondences:',len(added))
