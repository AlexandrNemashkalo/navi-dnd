"""Freeze a batch of exactly 100 currently missing, distinct art requirements."""
import json, re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'src/ArtLibrary/Sources/own-art/generation-batches/hundred-20261107'
requirements = json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))
creatures = 'Ape|Baboon|Badger|Boar|Camel|Cat|Deer|Draft Horse|Eagle|Elk|Goat|Hawk|Hunter Shark|Hyena|Killer Whale|Lion|Mammoth|Mule|Octopus|Owl|Panther|Pony|Quipper|Reef Shark|Rhinoceros|Riding Horse|Sea Horse|Tiger|Vulture|Warhorse|Weasel|Allosaurus|Ankylosaurus|Triceratops|Tyrannosaurus Rex|Plesiosaurus|Pteranodon|Saber-Toothed Tiger|Unicorn|Pegasus|Owlbear|Nightmare|Scarecrow|Gelatinous Cube|Gray Ooze|Black Pudding|Ochre jelly|Flameskull|Crawling Claw|Purple Worm|Rust Monster|Winter Wolf|Yeti|Death Dog|Giant Goat|Giant Hyena|Giant Octopus|Giant Owl|Giant Weasel'.split('|')
details = {
 'Unarmed strike':'One clenched bare fist in side view, thumb correctly folded over fingers, short wrist, no weapon.',
 'Block and tackle':'Two wooden pulley blocks connected by thick rope, visible wheels, lower iron hook; compact complete lifting tackle.',
 'Lance':'A long medieval cavalry lance with pointed steel tip, wooden shaft and hand guard, diagonal complete silhouette.',
 'Caltrops (bag of 20)':'A small open leather pouch beside two large iron caltrops with clearly separated sharp points.',
 "Climber's kit":'A compact climbing kit: coiled rope, one hooked piton and a small climbing hammer grouped together.',
 "Healer's kit":'An open leather medical satchel containing a rolled white bandage and a small brown medicine bottle; no modern symbols.',
 'Chalk (1 piece)':'One thick short white chalk stick, squared broken end, two cool gray shadow clusters.',
 'Ball bearings (bag of 1000)':'A small open brown pouch with five clearly separated shiny steel spheres spilling out.',
 'Reliquary':'A small closed golden reliquary casket, peaked lid, central red jewel, sturdy short feet.',
 'Signal whistle':'One small brass pea whistle with a clear mouthpiece and circular body, simple loop at rear.',
 'Sealing wax':'A dark red wax stick with one melted end beside a small round red wax seal.',
 'Abacus':'A rectangular wooden counting frame with four rods and clearly separated round colored beads.',
 'Ram, portable':'A thick horizontal wooden battering ram with an iron cap and two strong carrying handles.',
 'Totem':'A small carved wooden animal totem with owl-shaped face and a broad stable base.',
 'Whetstone':'One oblong gray sharpening stone, shallow three-quarter view, flat pale top and dark side.',
 'Tinderbox':'An open small iron tinderbox showing a flint stone and curled steel striker, no flames.',
 'Case, crossbow bolt':'A short rigid brown leather bolt case holding two short crossbow bolts with visible fletching.',
 'Case, map or scroll':'A cylindrical brown leather scroll tube with fitted lid, shoulder strap and small visible parchment edge.',
 'Emblem':'A round silver holy emblem pendant bearing a simple golden sun disk with eight rays, no writing.',
 'Lake':'Quiet blue inland lake surrounded by low green banks, distant hills and clear sky.',
 'Beach':'Curving pale sandy beach, blue sea to one side, distant sea horizon and a small cloud.',
 'Forest':'Open woodland with distinct brown trunks and green canopies, a path receding toward pale distant sky.',
 'DeepForest':'Dense ancient forest, overlapping dark trunks and heavy green canopy, small distant lit opening.',
 'Hills':'Three broad rounded green hill ridges receding toward a blue sky, grass foreground.',
 'Mountains':'Two angular gray mountain peaks with snow caps rising above a green foothill and blue sky.',
 'Peaks':'High jagged rocky alpine peaks, broad snow planes, no forest, pale blue sky.',
 'Swamp':'Still murky green water, reed clumps and a crooked tree, distant misty horizon.',
 'Desert':'Three simple golden sand dunes with large ochre shadow planes under clear pale blue sky.',
 'Tundra':'Low treeless ochre tundra with sparse gray stones, distant cold hills and gray blue sky.',
 'Snow':'A broad pristine snowy plain, cool blue shadows, distant low ridge and pale sky.',
 'SnowForest':'Three dark conifers heavily covered in white snow, snowy ground and distant pale blue horizon.',
 'capital':'A grand fantasy capital skyline with a central royal palace, several towers and a stone city wall.',
 'city':'A compact medieval city skyline with many red rooftops, stone walls and a tall central tower.',
 'town':'A modest medieval market town with three timber houses and a small bell tower beyond a road.',
 'village':'A peaceful rural village with three thatched cottages, wooden fence and green fields.',
 'castle':'A complete stone castle with two square crenellated towers and a central arched gate atop a low hill.',
 'port':'A medieval harbor with a wooden pier, one moored sailboat and waterfront warehouses, sea horizon.',
 'ruins':'Broken ancient stone arch and two fallen columns on grassy ground under open sky.',
 'dungeon':'A dark medieval stone dungeon corridor with an arched door, one wall torch and receding floor.',
 'cave':'Interior of a natural rock cave with angular stone walls and a bright distant opening, no architecture.',
 'shrine':'A small outdoor stone shrine with two pillars, simple roof and central candle altar against distant trees.',
 'Draft Horse':'A sturdy brown draft horse with heavy hooves, thick neck and plain working harness.',
 'Riding Horse':'A lean chestnut riding horse with simple saddle and bridle, four legs and full tail visible.',
 'Warhorse':'A powerful dark horse with steel forehead armor and a red saddle blanket, full body.',
 'Pony':'A short stocky tan pony with shaggy mane, short legs, no rider.',
 'Mule':'A gray mule with long upright ears, short dark mane, horse-like body and tufted tail.',
 'Quipper':'A small silver carnivorous fish in side view with large jaw, triangular dorsal fin and red eye.',
 'Hunter Shark':'A large gray predatory shark in side view, pointed snout, prominent dorsal fin and asymmetric tail.',
 'Reef Shark':'A sleek small gray reef shark in side view with white-tipped fins and clear dorsal fin.',
 'Plesiosaurus':'A marine reptile with very long curved neck, small head, broad body and four flippers, not a fish.',
 'Pteranodon':'A flying pterosaur with two broad leathery wings, long toothless beak and backward head crest.',
 'Ankylosaurus':'A low armored dinosaur with four thick legs, broad armored back and unmistakable club tail.',
 'Triceratops':'A stocky four-legged dinosaur with broad head frill and exactly three facial horns.',
 'Allosaurus':'A lean green predatory dinosaur with long counterbalancing tail, two strong hind legs and two small clawed arms.',
 'Tyrannosaurus Rex':'A bulky brown tyrannosaur with huge toothy head, two tiny arms, thick hind legs and long heavy tail.',
 'Saber-Toothed Tiger':'A stocky tawny prehistoric cat with two prominent long upper canine teeth, short tail.',
 'Unicorn':'A white horse with exactly one spiral horn on forehead, pale mane, four legs, no wings.',
 'Pegasus':'A white horse with two feathered wings rising from shoulders, no horn.',
 'Owlbear':'A bulky brown bear body with large owl head, round facial disks, hooked beak and heavy clawed paws.',
 'Nightmare':'A black horse with orange flaming mane, flaming hooves and flowing fiery tail.',
 'Scarecrow':'An animated straw scarecrow with patched brown tunic, crooked hat, two stick arms and straw hands.',
 'Gelatinous Cube':'One translucent pale green cube with clearly visible cubic outline and a tiny bone suspended inside.',
 'Gray Ooze':'An irregular low gray slime puddle with a raised rounded lobe; no face, no limbs.',
 'Black Pudding':'A glossy near-black amorphous slime mound with two short reaching pseudopods; no face.',
 'Ochre jelly':'An amber yellow amorphous jelly mound with one broad pseudopod and a scalloped base; no face.',
 'Flameskull':'One ivory human skull surrounded by distinct green flame shapes, two dark eye sockets and visible jaw.',
 'Crawling Claw':'One disembodied pale undead hand crawling on bent fingers, five digits, short ragged wrist, no gore.',
 'Purple Worm':'A thick purple segmented worm rearing upward with a round tooth-ring mouth and tapering body.',
 'Rust Monster':'A squat rust-orange insect beast with four legs, two long feathery antennae and a paddle-shaped tail.',
 'Winter Wolf':'A large white wolf with icy blue shadows, pointed ears, long muzzle and bushy tail.',
 'Yeti':'A hulking white-furred bipedal ape monster with broad shoulders, blue face, long arms and large feet.',
 'Death Dog':'A dark canine monster with exactly two separate dog heads on one four-legged body.',
}
base = 'Use case: stylized-concept. Create ONE original fantasy RPG pixel-art asset. Design for an actual 32 by 32 pixel canvas. If output is larger, show only a nearest-neighbor enlarged 32x32 sprite: one consistent square pixel grid, no drawn grid. Broad flat color clusters, 12-20 colors, dark one-logical-pixel outline, light from upper left, strong readable silhouette. No gradients, blur, dithering, texture noise, text, watermark, UI panel, border, ground shadow or tiny decoration. Keep all identifying features readable after reduction to 32x32. '
chosen=[r for r in requirements if r['category'] in ('equipment','biome_view','place_view') and not r.get('candidates')]
chosen += [next(r for r in requirements if r['category']=='monster' and r['name']==n) for n in creatures]
assert len(chosen)==100
jobs=[]
for i,r in enumerate(chosen,1):
 assert not r.get('candidates'),r['name']
 scene=r['category'] in ('biome_view','place_view')
 subject=details.get(r['name'], 'A complete '+r['name'].lower()+' with recognizable species anatomy and characteristic silhouette, natural colors, side or three-quarter view.')
 if r['name'].startswith('Giant '):subject+=' Oversized powerful proportions; preserve the actual animal species, no extra limbs.'
 layout=('Opaque square landscape, eye-level view toward the horizon or distant opening, foreground at bottom, distant scenery above. No overhead map view. Fill the square.' if scene else 'Genuine transparent background. One complete centered subject with a one-pixel clear margin. Full body for creatures, full object for equipment. No scenery or extra objects beyond the stated subject.')
 key='batch100-'+re.sub('[^a-z0-9]+','-',r['name'].lower()).strip('-')+'-v1'
 jobs.append(dict(index=i,key=key,category=r['category'],name=r['name'],nameRu=r.get('nameRu',r['name']),usage='place_view' if scene else 'map_actor' if r['category']=='monster' else 'item',transparent=not scene,description=subject,prompt=base+'Subject: '+subject+' '+layout))
OUT.mkdir(parents=True,exist_ok=True)
target=OUT/'manifest.json'
if target.exists():raise SystemExit('Existing frozen batch preserved')
target.write_text(json.dumps(dict(requested=100,status='planned',jobs=jobs),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(target)
