"""Curated first D&D equipment bindings from visually reviewed new packs.

Explicit IDs only. Multiple visual variants are allowed, and one illustration
may serve several requirements. These drafts do not grant mechanical properties
or bypass the art approval/profile/identity checks of the future runtime.
"""
import json
from pathlib import Path
from art_requirement_bindings import apply_reviewed_usages, save_bindings
from art_spell_assets import load_spell_assets, merge_spell_bindings

ROOT=Path(__file__).resolve().parents[1]
ASSETS=json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))
apply_reviewed_usages(ROOT, ASSETS)
BY_ID={a['id']:a for a in ASSETS}
BINDINGS=[]


def icons(pack,numbers):
    return [f'{pack}:icon-{n:03d}' for n in numbers]


def bind(name, ids, reason, category='equipment',note='Visual representation of the requested object; does not establish size, material or rules properties.',usage=None):
    usage=usage or ('spell' if category=='spell' else 'map_actor' if category in ('monster','creature_priority') else 'item')
    candidates=[]
    for asset_id in dict.fromkeys(ids):
        asset=BY_ID.get(asset_id)
        if not asset:continue  # Respect user deletion; never restore a candidate.
        if usage not in asset.get('usages',[]):raise ValueError(f'Wrong visual role for {name}: {asset_id}')
        candidates.append(dict(assetId=asset_id,sha256=asset['sha256'],usage=usage,reason=reason,match='visual_representation',sourcePriority=1 if asset['pack']=='dcss' else 0))
    BINDINGS.append(dict(category=category,name=name,selectionPolicy=dict(
        mode='balanced_variants',seedScope='world_and_subject',preserveAssignment=True,
        requiredChecks=['usage','style_profile','art_approval','subject_facts'],
        variantFamilyCountsAsOne=True),note=note,candidates=candidates))


P='prepared-items-native-32-'
B='prepared-items-native-32-batch2-'
bind('Shield',icons('craftpix-883583',range(1,37)), 'A complete isolated shield, not a character holding a shield.')
amulets=icons('craftpix-883583',range(37,49))+icons(P+'31-jewellery',[21,22,23,24,29,30,31,32,33,34,36,37,38,39,40,41,42,43,44,45,46,47,48])
bind('Amulet',amulets,'Isolated pendant or necklace worn on a cord/chain.')
bind('Book',icons(P+'13-books',[1,2,3,4,6,7,8,9,10,11,12,19,20,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,38,39,40,41,42,43,44,45,46,47,48]),'A single complete book with visible cover/pages.')
bind('Spellbook',icons(P+'13-books',[3,4,19,20,21,22,23,24,28,31,32,33,35,36,40,43,46,48]),'Ornamented book suitable as a spellbook representation; contents remain a game fact.')
chests=icons(P+'32-treasure',[1,3,5,7,9,11,13,14,15,16,17,18,19,23,24])
bind('Chest',chests,'Closed chest variants; open/coin-filled versions excluded for this generic closed-container need.')
bind('chest',chests,'Closed chest/container illustration.',category='prop_priority')
backpack=icons(B+'35-kobold-loot',[2]);torch=icons(B+'35-kobold-loot',[45])
bind('Backpack',backpack,'Brown backpack with a bedroll attached.')
bind('backpack',backpack,'Complete backpack illustration.',category='prop_priority')
bind('Torch',torch,'A complete handheld torch with a visible flame.')
bind('torch',torch,'Complete lit torch illustration.',category='prop_priority')
rope=icons(P+'02-crafting',[10])+icons(P+'10-mining',[45,47])+icons(P+'23-farming',[13])+icons(B+'28-dwarf-loot',[39])
bind('Rope, hempen (50 feet)',rope,'Coiled/bound rope; fibre and length are not visually certified.')
bind('Rope, silk (50 feet)',rope,'Coiled/bound rope; game metadata distinguishes silk from hemp.')
bind('rope',rope,'Coiled/bound rope illustration.',category='prop_priority')
lantern=icons(P+'10-mining',[42,44])+icons(B+'22-undead-loot',[10])+icons(B+'28-dwarf-loot',[11])+icons(B+'35-kobold-loot',[22])
bind('lantern',lantern,'Complete lantern body; lighting behaviour is not inferred.',category='prop_priority')
bind('Shovel',icons(P+'10-mining',[32,33,34,35])+icons(P+'23-farming',[29]),'Complete shovel/spade with handle and blade.')
bind("Piek, miner's",icons(P+'10-mining',[25,26,27,28,29,30,36,37,38])+icons(B+'28-dwarf-loot',[10])+icons(B+'35-kobold-loot',[5]),'Complete mining pick; broken pick variants excluded.')
bind('Hammer',icons(P+'02-crafting',[5])+icons(P+'04-crafting-materials',[47])+icons(P+'10-mining',[41])+icons(B+'28-dwarf-loot',[2]),'Complete handheld hammer, rather than a framed striking action.')
bind('Bucket',icons(P+'23-farming',[47]),'Wooden bucket with upright handle.')
bind('Basket',icons(P+'23-farming',[44])+icons(B+'28-dwarf-loot',[26]),'Woven basket with an open interior and visible carrying shape.')
bind('Sickle',icons(P+'23-farming',[27])+icons(P+'33-spears',[8,35])+icons(B+'31-exotic-weapons',[5,13]),'Curved harvesting blade with a handle; scythe polearms excluded.')
bind('Trident',icons(P+'33-spears',[18,42])+icons(B+'13-spears',[36,43]),'Visible three-pronged polearm head.')
bind('Spear',icons(P+'33-spears',[3,15,26,28])+icons(B+'13-spears',[1,2,3,4,5,6,8,9,10,12,20,21,22,25,26,45]),'Single pointed blade on a long shaft; forks and scythes excluded.')
bind('Longsword',icons(P+'36-swords',[1,13,25,26,35]),'Straight sword with a crossguard and no surrounding particle effects; exact scale remains a game fact.')
bind('Rapier',icons(P+'36-swords',[12]),'Thin straight blade with a prominent hand guard; exact weapon statistics are not inferred.')
bind('Scimitar',icons(P+'36-swords',[6,9,18,30,33]),'Curved single blade with handle; colour is a visual variant, not a damage type.')
bind('Battleaxe',icons(B+'02-axes',[1,7,9,25,27,31,34,36,41]),'Complete axe with a broad cutting head; hand count/weight remain game metadata.')
bind('Wand',icons(P+'40-staves',[17,18,24,36,37,38,39,48]),'Slender complete rod/branch-like implement; spell effect not inferred.')
bind('Yew wand',icons(P+'40-staves',[36,38,39]),'Wood-like branch/rod silhouette; botanical species is not visually confirmed.')
bind('Wooden staff',icons(P+'40-staves',[23,38,39]),'Plain wood-like long shaft; no specific wood species is claimed.')
bind('Staff',icons(P+'40-staves',[23,38,39]),'Complete plain staff/branch silhouette.')
bind('Quarterstaff',icons(P+'40-staves',[38,39]),'Long plain wood-like shaft; scale and combat rules belong to item data.')

# Further equipment: only complete isolated objects, not action panels or
# paper-doll fragments. Quantities and exact scale remain properties of the item.
bind('Dagger',icons(B+'31-exotic-weapons',[16,24])+icons(P+'02-crafting',[36]),'Complete small blade with a handle; exact dimensions are not certified.')
bind('Whip',icons(B+'22-undead-loot',[31])+icons(B+'31-exotic-weapons',[21]),'Coiled leather-like lash with a visible grip; spiked chains excluded.')
bind('Flail',icons(B+'31-exotic-weapons',[8,27]),'Weighted balls connected by a chain to the gripping part.')
bind('Morningstar',icons(B+'02-axes',[16,21,22]),'A spiked striking head on a handle, distinct from a cutting axe head.')
bind('Mace',icons(B+'02-axes',[15,19,21,22,23]),'A complete blunt/spiked headed weapon; game data supplies the exact weapon type and weight.')
bind('Handaxe',icons(B+'02-axes',[1,10,26,28])+icons(B+'28-dwarf-loot',[18]),'A compact axe silhouette with a single cutting head.')
bind('Greataxe',icons(B+'02-axes',[14,25,31,32,38,39,43,45,46,47]),'Large broad or double axe head; two-handed statistics are not inferred from icon scale.')
bind('Glaive',icons(B+'13-spears',[23,24,40]),'A long shaft carrying a curved cutting blade; short swords, bows and forks excluded.')
bind('Dart',icons(B+'31-exotic-weapons',[14]),'A thin pointed projectile with visible shaft.')
bind('Blowgun',icons(B+'31-exotic-weapons',[36]),'A long narrow tube illustrated with a separate dart.')
bind('Club',icons(P+'40-staves',[38])+icons(B+'31-exotic-weapons',[28]),'Plain or studded wooden striking shaft; exact length is game data.')
bind('Light hammer',icons(P+'04-crafting-materials',[47])+icons(P+'02-crafting',[5]),'Small complete hammer, not a picture of a hammering action.')
bind('Warhammer',icons(B+'28-dwarf-loot',[2])+icons(P+'40-staves',[7,33]),'Complete long-handled hammer silhouette; combat statistics come from item data.')
bind('Arrows (20)',icons(B+'11-arrows',[1,2,3,4,5,6,7,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,25,26,27,28,29,30,31,32]),'An isolated conventional arrow, without particle effects; icon does not depict the stack count.')
bind('Fishing tackle',icons(P+'20-fishing',[34,35,36,37,38,39,40,43,44,45,46,48]),'Rod, hook, lure or line-spool representing fishing equipment, not a caught fish.',note='Individual components represent the kit; the full list of kit contents remains game data.')
bind('Net',icons(P+'20-fishing',[41,42]),'A visible mesh net; exact size remains game data.')
bind('Grappling hook',icons(B+'31-exotic-weapons',[48]),'Rope with a visible metal hook.')
bind('Chain (10 feet)',icons(P+'04-crafting-materials',[10]),'A curved linked metal chain; depicted length is not a measurement.')
bind('Spikes, iron (10)',icons(P+'02-crafting',[17])+icons(B+'28-dwarf-loot',[46]),'Bundle of metal nails/spikes; count and metal grade are game facts.')
bind('Pouch',icons(P+'23-farming',[16])+icons(B+'22-undead-loot',[13])+icons(B+'24-fairy-loot',[40])+icons(B+'28-dwarf-loot',[42])+icons(B+'35-kobold-loot',[27]),'A tied pouch or small soft bag; no invisible contents are assumed.')
bind('Sack',icons(P+'23-farming',[1,4,5,6]),'A soft sack with gathered or folded upper opening; contents are not fixed by the icon.')
bind('Component pouch',icons(B+'22-undead-loot',[13])+icons(B+'24-fairy-loot',[40])+icons(B+'35-kobold-loot',[27]),'A small pouch can represent spell components; contents remain item data.')
bind('Jug or pitcher',icons(P+'18-fruit-vegetables',[37,38,39,42]),'A complete handled jug/pitcher silhouette.')
bind('Flask or tankard',icons(P+'28-food',[37,38,39,40,41])+icons(P+'18-fruit-vegetables',[40]),'Complete drinking mugs/tankards; filled and foaming variants should follow the scene.')
bind('Vial',icons(B+'22-undead-loot',[29])+icons(P+'29-potions',[18,29,34,36,43,47]),'A complete narrow-necked vial; liquid colour is cosmetic and does not establish an effect.')
bind('Ink (1 ounce bottle)',icons(B+'22-undead-loot',[4]),'A visible dark ink pot with blue quill; volume is a game fact.')
bind('Crystal',icons(P+'05-gems',[2,7,8,13,14,15,16,18,19,20,21,22,23,24,25,26,27,37,38,39,40,42,43,44,45,46,47,48]),'A complete faceted crystal or mineral; colour does not assign a spell effect.')
bind('Orb',icons(P+'12-artifacts',[2,6,10,12])+icons(B+'24-fairy-loot',[20]),'A complete round orb; internal glow does not certify a specific power.')
bind('Rod',icons(P+'40-staves',[14,17,18,23,24,36,37,38,39,42,47,48]),'A complete rod/staff-like implement with no framed background.')
bind('Signet ring',icons(P+'31-jewellery',[5,6,11,19]),'Ring with a prominent emblem or flat inset; ownership/seal text remains unspecified.')
bind('Robes',icons(B+'05-cuirasses',[9,10,11,12,17,19,20,24,29]),'A complete robe or long cloth garment shown as an isolated object.')
bind('Breastplate',icons(B+'05-cuirasses',[3,4,13,18,22,25,28,34,35,36,38,44]),'A complete rigid chest armour piece; protection rating/material are supplied by game rules.')
bind('Leather',icons(B+'05-cuirasses',[7,16]),'A complete brown leather-like torso garment; exact material/AC is not determined by pixels.')

# Spell bindings select an illustrative motif, not evidence that the artist's
# source ability has the same rules, damage type, level, targeting or duration.
SPELL_NOTE='Illustration of the named spell motif only. Rules, targets, damage and duration come exclusively from Dnd5e game data; final art approval is pending.'
def spell(name,pack,numbers,reason):bind(name,icons(pack,numbers),reason,category='spell',note=SPELL_NOTE)
fire=P+'11-pyromancer';ice=P+'25-cryomancer';wind=P+'21-aeromancer';earth=B+'38-earth';holy=B+'20-priest'
spell('Fire Bolt',fire,[15,16],'A streaking flame or fire projectile, distinct from an isolated weapon.')
spell('Fireball',fire,[16,31],'Round flame projectiles and falling fiery spheres.')
spell('Burning Hands',fire,[1,8,9,26],'Open hand, branching flame or flame fragments radiating outward.')
spell('Produce Flame',fire,[1,27],'A hand/figure visibly holding a flame, rather than a potion or weapon.')
spell('Create Bonfire',fire,[24,30,37],'A cluster of flames or fire burning against the ground.')
spell('Wall of Fire',fire,[13,24],'Tall continuous flames/flame wall motif.')
spell('Scorching Ray',fire,[15,28],'Narrow directed flame/beam rather than a container or creature.')
spell('Flaming Sphere',fire,[16],'A single round orange sphere wreathed in flame.')
spell('Ray of Frost',ice,[15,32],'Directed blue ice shard/beam motif.')
spell('Ice Storm',ice,[13,46],'Falling/bursting ice shards; fixed ice-wall illustrations excluded.')
spell('Wall of Ice',ice,[26],'A tall slanted mass of contiguous ice.')
spell('Cone of Cold',ice,[9,13,17,20],'A broad spreading wave/fan of ice rather than a single weapon.')
spell('Gust',wind,[11,12,14],'A curling stream or spiral of air.')
spell('Gust of Wind',wind,[11,12,30],'Wind stream or twisting funnel motif; direction and targets are game data.')
spell('Whirlwind',wind,[26,30],'A dark/pale funnel-shaped vortex.')
spell('Mage Hand',wind,[1,18,20],'A clearly visible hand in magical-looking rays; no weapon is required.')
spell("Bigby's Hand",wind,[1,18,20],'A large hand motif; size and chosen hand action are not inferred from the source skill.')
spell('Magic Missile',B+'32-lightning',[17],'Three luminous blue-purple projectiles, without physical arrow shafts.')
spell('Shield',holy,[34,42],'An isolated round/kite shield as a protection motif within a framed spell icon.')
spell('Mage Armor',holy,[33],'A complete breastplate as an arcane protection motif, not proof of equipped armour.')
spell('Cure Wounds',P+'30-buffs',[30,31],'Heart and healing-cross motifs; healing amount is game data.')
spell('Healing Word',P+'30-buffs',[30,31],'A heart with radiating recovery marks; spoken component comes from rules.')
spell('Sacred Flame',fire,[9,10],'Bright gold-orange branching flame; radiant spell rules remain separate from source-pack labels.')
spell('Sunbeam',holy,[15,16,17,21],'Directed radiant light rays or a bright sun source.')
spell('Light',holy,[7,17],'Bright gold radiant star/sun motif.')
spell('Entangle',B+'16-druid',[10,18,44],'Grasping roots/branches or tangled plants, without claiming a creature species.')
spell('Earth Tremor',earth,[7,17,28,33],'Cracked ground or stones bursting upward.')
spell('Erupting Earth',earth,[17,28,33,38],'Earth/rock spikes and bursting stones.')
spell('Mold Earth',earth,[2,4,15,38,40],'Manipulated or floating stones and earth.')
spell('Wall of Stone',earth,[5,6,30],'Contiguous rock formation or stone barrier.')
spell('Stone Shape',earth,[13,15,38,40],'Stone objects being held/arranged into a shaped form.')
spell('Detect Magic',holy,[45,46],'Eyes/face with luminous gaze; no particular school of magic is claimed.')
spell('Arcane Eye',P+'16-archer',[47],'An eye close-up as a scrying/vision motif.')
spell('Darkvision',P+'16-archer',[47],'Luminous blue eye motif; range and lighting rules are not inferred.')
spell('Web',B+'09-halloween',[23],'A visible spider on a blue radial web.')
spell('Hold Person',P+'37-debuffs',[3],'A visibly chained hand/arm, representing restraint rather than a bound object.')
spell('Chill Touch',B+'19-necromancer',[17],'A raised skeletal/grey hand motif.')
spell('Animate Dead',B+'19-necromancer',[2,4,6,19],'Complete skeletons or a skeleton rising/crawling from the ground.')
spell('Speak with Dead',B+'19-necromancer',[1,2,18,34],'Skull/skeleton motifs; speech/answers are not asserted by the image.')
spell('Fear',B+'15-curses',[15,16,25,34],'Fanged screaming mouth or hooded skull motifs.')
spell('Crown of Madness',fire,[5],'A flaming gold crown motif, not a claim of a particular damage type.')

# New-pack creature images, independently of spell panels. Shared ordinary/giant
# depictions use game metadata for scale; no body stat block is read from pixels.
CREATURE_NOTE='Whole-creature visual representation, not a face portrait or proof of alignment, age, size category or combat statistics.'
def creature(name,pack,numbers,reason):bind(name,icons(pack,numbers),reason,category='monster',note=CREATURE_NOTE)
small='craftpix-459799';chaos='craftpix-737210'
creature('Rat',small,[31],'A complete side-view dark brown rat.')
creature('Giant Rat',small,[31],'A complete rat silhouette; giant scale must be supplied by the game.')
creature('Bat',small,[33,34,38],'Complete bats with visible wings and head.')
creature('Giant Bat',small,[33,34,38],'Complete bat shapes; size is a game fact rather than an icon measurement.')
creature('Spider',chaos,[33,46],'Whole many-legged spider with a rounded abdomen, not a status badge.')
creature('Giant Spider',chaos,[33,46],'Whole spider shapes; the scene establishes their scale.')
creature('Scorpion',chaos,[48],'Whole scorpion with a long raised curved tail.')
creature('Giant Scorpion',chaos,[48],'Complete scorpion silhouette; giant scale is supplied by game state.')
creature('Giant Wasp',small,[42],'Whole yellow-black winged insect with a long pointed abdomen.')
creature('Imp',chaos,[22],'Small red winged horned creature with a tail.')
creature('Fire Elemental',chaos,[47],'A flame-bodied walking creature, rather than a framed fire spell.')
creature('Awakened Shrub',small,[17],'Small rooted leafy creature with an expressive face.')
creature('Awakened Tree',chaos,[12],'Brown branch-limbed tree-like humanoid.')
creature('Twig Blight',chaos,[12],'A thin woody branching creature; exact D&D identity remains the mapping choice.')
creature('Myconid Sprout',small,[1,9],'Walking small mushroom silhouettes; age/size are game data.')
creature('Myconid Adult',small,[3,4],'Anthropomorphic mushroom creatures with caps and visible faces/limbs.')
for name,pack,numbers in [('rat',small,[31]),('spider',chaos,[33,46]),('slime',small,[22,23,24,25,28,29,30])]:
    bind(name,icons(pack,numbers),'Whole reviewed creature representation.',category='creature_priority',note=CREATURE_NOTE)

# Invisible magical properties are authored rule-set correspondences, never
# observations of the source artwork. The same base form may serve several items.
MAGIC_NOTE='Rule-set illustration choice. Only the visible object form is matched; its magic, rarity, bonuses and contents are supplied by the item record, not inferred from the picture.'
def magic(name,ids,reason):bind(name,ids,reason,category='magic_item',note=MAGIC_NOTE)
def variants_of(name):
    return [c['assetId'] for b in BINDINGS if b['category']=='equipment' and b['name']==name for c in b['candidates']]
magic('ENDURING SPELLBOOK',variants_of('Spellbook'),'A complete ornamented book; endurance is not a visual claim.')
magic('TANKARD OF SOBRIETY',icons(P+'28-food',[37,39,41])+icons(P+'18-fruit-vegetables',[40]),'Complete drinking tankards; sobriety is an item effect.')
magic('ALCHEMY JUG',icons(P+'18-fruit-vegetables',[37,38,39,42]),'Complete handled pitchers/jugs; contents are defined when the item is used.')
magic('DECANTER OF ENDLESS WATER',icons(P+'18-fruit-vegetables',[37,38,39,42]),'A complete pouring vessel; endless water is a rule-set property.')
bag=icons(B+'35-kobold-loot',[1])+icons(B+'24-fairy-loot',[40])+icons(B+'28-dwarf-loot',[42])
magic('BAG OF HOLDING',bag,'Complete soft bags; extradimensional capacity is not inferred from size or colour.')
magic('BAG OF TRICKS',bag,'Complete tied bags; no unseen animal contents are assumed.')
magic('ROPE OF CLIMBING',rope,'A complete coil/bundle of rope; autonomous movement is supplied by item rules.')
magic('ROPE OF ENTANGLEMENT',rope,'A complete coil/bundle of rope; restraint properties are not visual facts.')
magic('GOGGLES OF NIGHT',icons(B+'28-dwarf-loot',[40]),'An isolated pair of dark goggles with round blue lenses.')
magic('BOOTS OF THE WINTERLANDS',icons(B+'28-dwarf-loot',[13]),'A complete pair of visibly fur-trimmed boots; cold protection remains a rule-set property.')
magic('BOOTS OF ELVENKIND',icons(B+'04-boots',[2,7,8,16,17]),'Complete leather-like boots; elven origin and quiet movement are not inferred.')
magic('BRACERS OF ARCHERY',icons(B+'01-bracers',[7,8,17,24,40,42]),'Paired bracers/gloves; archery bonuses come from item rules.')
magic('GLOVES OF THIEVERY',icons(B+'01-bracers',[8,16,31,40]),'Complete paired dark/leather-like gloves, not an image of a theft action.')
magic('GLOVES OF MISSILE SNARING',icons(B+'01-bracers',[3,18,25,34,40]),'Complete paired gloves/gauntlets; catching projectiles is a gameplay property.')
magic('GLOVES OF SWIMMING AND CLIMBING',icons(B+'01-bracers',[13,19,23,35,40]),'Complete paired gloves; movement abilities are not inferred from decoration.')
magic('SENTINEL SHIELD',variants_of('Shield'),'A complete shield; vigilance and initiative effects belong to the item record.')
magic('TRIDENT OF FISH COMMAND',variants_of('Trident'),'A complete three-pronged polearm; fish control is not an image observation.')
for name in ['WAND OF CONDUCTING','WAND OF MAGIC DETECTION','WAND OF WEB','WAND OF SECRETS','WAND OF MAGIC MISSILES',
             'WAND OF LIGHTNING BOLTS','WAND OF FIREBALLS','WAND OF PARALYSIS','WAND OF BINDING','WAND OF FEAR','WAND OF WONDER']:
    magic(name,icons(P+'40-staves',[2,4,5,6,13,15,16,18,21,24,37,42,43,45,47,48]),
          'A complete decorated wand/rod; colour and head ornament do not prove which spell it casts.')
for name in ['AMULET OF PROOF AGAINST DETECTION AND LOCATION','AMULET OF HEALTH','PERIAPT OF HEALTH','PERIAPT OF WOUND CLOSURE','MEDALLION OF THOUGHTS','NECKLACE OF ADAPTATION']:
    magic(name,amulets,'A complete neck-worn pendant/amulet; its invisible magical effect is assigned by the item record.')
for name in ['RING OF MIND SHIELDING','RING OF SWIMMING','RING OF JUMPING','RING OF WARMTH','RING OF WATER WALKING']:
    magic(name,icons(P+'31-jewellery',[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,25,26,27,28]),
          'A complete finger ring; gemstone colour does not establish the magic effect.')
# Further reviewed object families. Written scrolls are not blank paper, and a
# bottle with an hourglass silhouette is not a working hourglass.
candle=icons(P+'12-artifacts',[35])
bind('Candle',candle,'A complete candle on a small stand; burn time is supplied by the item record.')
scrolls=icons(P+'24-scrolls',[1,2,3,5,6,7,8,10,11,13,15,16,17,18,19,20,21,23,24,25,26,27,28,29,30,31,34,35,36,37,38,39,43,44,45,46,47,48])+icons(B+'22-undead-loot',[18])+icons(B+'24-fairy-loot',[43])+icons(B+'28-dwarf-loot',[16])
for name in ['SPELL SCROLL Cantrip ','SPELL SCROLL 1 lvl','SPELL SCROLL 2 lvl','SPELL SCROLL 3 lvl',
             'SPELL SCROLL 4 lvl ','SPELL SCROLL 5 lvl','SPELL SCROLL 6 lvl','SPELL SCROLL 7 lvl',
             'SPELL SCROLL 8 lvl','SPELL SCROLL 9 lvl','SCROLL OF PROTECTION']:
    magic(name,scrolls,'A complete written or rolled scroll; seals and ink colour do not disclose its spell or level.')
for name in ['CANDLE OF THE DEEP','CANDLE OF INVOCATION']:
    magic(name,candle,'A complete candle on a stand; magical behaviour and alignment are not visible facts.')
for name in ['MANUAL OF QUICKNESS OF ACTION','MANUAL OF GOLEMS','MANUAL OF GAINFUL EXERCISE',
             'MANUAL OF BODILY HEALTH','TOME OF LEADERSHIP AND INFLUENCE','TOME OF UNDERSTANDING',
             'TOME OF CLEAR THOUGHT']:
    magic(name,variants_of('Book'),'A complete bound book; subject, language and magical benefits belong to its game record.')
for name in ['ORB OF TIME','ORB OF DIRECTION','CRYSTAL BALL','CRYSTAL BALL of True Seeing',
             'CRYSTAL BALL of Telepathy','CRYSTAL BALL of Mind Reading']:
    magic(name,variants_of('Orb'),'A complete spherical orb; its unseen divination properties are not inferred from colour.')
for name in ['SHIELD OF EXPRESSION','ARROW-CATCHING SHIELD','SHIELD OF MISSILE ATTRACTION',
             'ANIMATED SHIELD','SPELLGUARD SHIELD']:
    magic(name,variants_of('Shield'),'A complete shield; movement, expression and protective effects are game properties.')
for name in ['ROPE OF MENDING']:
    magic(name,rope,'A complete coil of rope; self-repair is not a visual claim.')
for name in ['LANTERN OF REVEALING','GHOST LANTERN']:
    magic(name,lantern,'A complete enclosed lantern; revealed entities or bound spirits are not implied by the image.')
for name in ['MACE OF SMITING','MACE OF DISRUPTION','MACE OF TERROR']:
    magic(name,variants_of('Mace'),'A complete mace; fear and damage properties are defined by the item record.')
magic('DAGGER OF VENOM',variants_of('Dagger'),'A complete dagger; poison remains hidden until established by the game.')
magic('SCIMITAR OF SPEED',variants_of('Scimitar'),'A complete curved sword; speed is an item property.')
magic('HEWARD\'S HANDY HAVERSACK',variants_of('Backpack'),'A complete backpack with attached roll; magical capacity is not visually disclosed.')
magic('BAG OF DEVOURING',bag,'A complete tied bag; dangerous contents are not disclosed by its appearance.')
for name in ['STAFF OF BIRDCALLS','STAFF OF ADORNMENT','STAFF OF FLOWERS','STAFF OF THE WOODLANDS']:
    magic(name,icons(P+'40-staves',[23,38,39]),'A complete branch-shaped staff; flowers, sounds and abilities are not claimed to be depicted.')
for name in ['STAFF OF HEALING','STAFF OF CHARMING','STAFF OF POWER','STAFF OF STRIKING','STAFF OF THE MAGI']:
    magic(name,icons(P+'40-staves',[2,4,10,13,21,22,27,34,41,43]),'A complete ornamented staff; head ornament does not establish spells or charges.')


# Explicit DCSS fallbacks, selected by reviewed content rather than path search.
def dcss(paths):return ['dcss/'+p for p in paths]
for name,paths,reason in [
    ('Wolf',['monster/animals/wolf','monster/animals/war_dog'],'Whole grey/brown canine with wolf-like silhouette.'),
    ('Dire Wolf',['monster/animals/warg'],'Whole pale grey wolf; large scale is supplied by the game.'),
    ('Skeleton',['monster/undead/skeletons/skeleton_humanoid_small_new','monster/undead/skeletons/skeleton_humanoid_small_old'],'Complete upright humanoid skeleton, not an animal skeleton.'),
    ('Ogre',['monster/ogre_new','monster/ogre_old'],'Complete broad humanoid, with a club in one variant.'),
    ('Troll',['monster/troll'],'Complete tall green shaggy humanoid.'),
    ('Ghost',['monster/undead/phantom_new','monster/undead/phantom_old'],'Pale cyan spectral humanoid; incorporeal rules remain game data.')]:
    bind(name,dcss(paths),reason,category='monster',note=CREATURE_NOTE)
    if name != 'Dire Wolf':
        bind(name.lower(),dcss(paths),reason,category='creature_priority',note=CREATURE_NOTE)
altars=dcss(['dungeon/altars/altar_old','dungeon/altars/misc_altar','dungeon/altars/cheibriados'])
for category in ['furniture','prop_priority']:
    bind('altar',altars,'Complete stone/draped altar; no DCSS deity or religious rules are imported.',category=category,usage='prop')
bind('door',dcss(['dungeon/doors/closed_door','dungeon/doors/open_door']),
     'Complete doorway in closed/open states; choose the variant matching game state.',category='prop_priority',usage='prop',
     note='Open and closed are state variants, not interchangeable random decoration. Final state-aware integration is pending.')
bind('key',icons(P+'32-treasure',range(25,37))+icons(B+'22-undead-loot',[23]),'Complete key with shaft and head; lock compatibility is game data.',category='prop_priority')
barrels=icons(P+'23-farming',[18])
bind('barrel',barrels,'A complete brown barrel with side handles.',category='prop_priority')
bind('barrels',barrels,'A barrel illustration; object count is supplied by the scene.',category='furniture',usage='item')
bind('Barrei',barrels,'A complete barrel; the source equipment reference retains its original spelling.')
bind('Shortbow',icons(B+'31-exotic-weapons',[11])+icons(B+'35-kobold-loot',[47]),'Complete compact curved bow; broken bows excluded.')
bind('Longbow',icons(P+'33-spears',[23,45,46,47])+icons(B+'28-dwarf-loot',[41]),'Complete curved bow; precise length and weapon statistics remain game data.')
bind('Bottle, glass',icons(P+'18-fruit-vegetables',[41])+icons(P+'28-food',[28,29,30]),'Complete bottle silhouette; contents and capacity are not inferred.')
bind('Rations (1 day)',icons(P+'28-food',[11,20]),'Bread as an illustration of carried provisions; quantity and full ration contents come from game data.')
spell('Lightning Bolt',B+'32-lightning',[1,27],'Branching lightning streaks and a humanoid beside a bolt.')
spell('Chain Lightning',B+'32-lightning',[1],'Branching lightning motif; target count and chaining are game rules.')
spell('Shocking Grasp',B+'32-lightning',[36],'Gripping hand surrounded by blue electric arcs.')
spell('Witch Bolt',B+'32-lightning',[1,46],'Electric arcs or a caster reaching through blue lightning; sustained connection is a game rule.')


# Further gaps across animals, mundane equipment and dungeon props.
for name,files,reason in [
    ('Crocodile',['alligator','alligator_baby'],'Complete green crocodilian with long jaws and visible teeth.'),
    ('Giant Crocodile',['alligator'],'Complete crocodilian; giant scale is established by the scene.'),
    ('Black Bear',['black_bear_new','black_bear_old'],'Complete dark bear-like quadruped.'),
    ('Polar Bear',['polar_bear'],'Complete pale white bear.'),
    ('Elephant',['elephant_new','elephant_old','elephant_dire_new','elephant_dire_old'],'Complete grey elephant, with visible tusks in some variants.'),
    ('Mastiff',['hound'],'Complete brown hound; exact breed and statistics remain game data.'),
    ('Jackal',['jackal_new','jackal_old'],'Complete lean canine; no magical traits are inferred.'),
    ('Frog',['giant_frog'],'Complete green frog; physical scale is supplied by the scene.'),
    ('Giant Frog',['giant_frog'],'Complete green frog; giant size is a game property.'),
    ('Giant Toad',['giant_toad'],'Complete gold-brown toad-like amphibian.'),
    ('Lizard',['iguana','giant_lizard'],'Complete long-tailed lizard.'),
    ('Giant Lizard',['iguana','giant_lizard'],'Complete lizard silhouette; giant scale is supplied by the game.'),
    ('Poisonous Snake',['adder','small_snake','viper'],'Complete snake; venom is not inferred from its colour.'),
    ('Giant Poisonous Snake',['viper','black_mamba_new','black_mamba_old'],'Complete snake; venom and giant size come from game data.'),
    ('Constrictor Snake',['ball_python','anaconda_new','anaconda_old'],'Complete coiled snake; constriction rules are not inferred from the icon.'),
    ('Giant Constrictor Snake',['anaconda_new','anaconda_old'],'Complete broad coiled snake; giant scale belongs to game data.'),
    ('Giant Centipede',['giant_centipede'],'Complete elongated many-legged arthropod.'),
    ('Hell Hound',['hell_hound_new','hell_hound_old'],'Complete red hound, with orange markings in one variant; fire abilities remain game rules.')]:
    bind(name,dcss(['monster/animals/'+f for f in files]),reason,category='monster',note=CREATURE_NOTE)
for name,files,reason in [
    ('Crossbow, hand',['ranged/hand_crossbow','ranged/hand_crossbow_2','ranged/hand_crossbow_3'],'Complete compact crossbow; held-object overlays excluded.'),
    ('Crossbow, light',['ranged/crossbow_1','ranged/crossbow_2'],'Complete crossbow with stock and transverse bow.'),
    ('Crossbow, heavy',['ranged/arbalest_1','ranged/arbalest_2','ranged/arbalest_3'],'Complete arbalest silhouette; weight and damage are game properties.'),
    ('Sling',['ranged/sling_1','ranged/sling_2'],'Complete coiled sling, not a bow or firearm.')]:
    bind(name,dcss(['item/weapon/'+f for f in files]),reason)
bind('Plate',dcss(['item/armor/torso/plate_1','item/armor/torso/plate_mail_2']),
     'Rigid plate torso as an armour-set illustration; full set contents and AC come from game data.')
bind('trap',dcss(['dungeon/traps/trap_blade','dungeon/traps/trap_axe','dungeon/traps/trap_dart']),
     'Visible mechanical floor trap with a blade or dart mechanism; show only when discovered.',
     category='prop_priority',usage='prop',note='Visible/discovered traps only. Mechanism must match the scene; hidden traps must not be revealed through art.')


# New-pack motifs shared only where the visible subject remains appropriate.
for names,pack,numbers,reason in [
    (['Control Flames','Continual Flame'],fire,[9,10,39],'Flame or hands controlling a flame.'),
    (['Fire Storm','Incendiary Cloud'],fire,[13,24],'Broad mass of flames; area and persistence are game rules.'),
    (['Meteor Swarm',"Melf's Minute Meteors"],fire,[31],'Several visibly falling fiery meteors; count and damage remain game data.'),
    (['Delayed Blast Fireball'],fire,[16],'A round fireball; delay is not inferred from the picture.'),
    (['Immolation','Investiture of Flame','Fire Shield'],fire,[17,20],'Humanoid silhouette surrounded by flame or fiery aura.'),
    (["Ashardalon's Stride"],fire,[22,38],'Boot moving through or standing in fire.'),
    (["Aganazzar's Scorcher","AGANAEZAR'S SCORCHER"],fire,[15,28],'Directed flame beam/projectile motif.'),
    (["DRAGON'S BREATH"],fire,[34],'Open mouth emitting fire; other damage types require a different variant.'),
    (["Otiluke's Freezing Sphere"],ice,[16],'Round cracked ice sphere.'),
    (['Frostbite'],ice,[22,24,36],'Frozen or ice-covered face.'),
    (['Armor of Agathys','Investiture of Ice'],ice,[37],'Ice-covered torso; temporary protection is a game rule.'),
    (['Ice Knife'],ice,[15,18,32],'Pointed ice shard held or flying as a blade/projectile.'),
    (["Rime's Binding Ice"],ice,[13,20],'Burst or spreading spray of ice.'),
    (['Druidcraft','Plant Growth','Mold Plant'],B+'16-druid',[2,8,17],'Leaves, a tree or a growing flower as a plant manipulation motif.'),
    (['Barkskin'],B+'16-druid',[28,43],'Wood-textured face, rather than a stone face.'),
    (['PRIMAL SAVAGERY'],B+'16-druid',[9,10,11,45],'Claw, taloned hand or snarling animal muzzle.'),
    (['Beast Sense','Beast Bond'],B+'16-druid',[7,35,37],'Animal head as a beast connection motif; no mind-reading rules are inferred.'),
    (['Earthquake','Bones of the Earth'],earth,[17,28,33],'Split earth or rising rock spikes.'),
    (['Move Earth','Transmute Rock','Magic Stone'],earth,[15,40,41],'Moving or arranged stones; material conversion and size are game properties.'),
    (['Dust Devil','Control Winds','Warding Wind'],wind,[26,30],'A curling wind vortex.'),
    (['Mass Cure Wounds','Mass Healing Word','Heal','Mass Heal','Power Word Heal'],P+'30-buffs',[30,31],'Heart and healing marks; target count and healing amount are game data.')]:
    for name in names:spell(name,pack,numbers,reason)

# Explicit reviewed roles are recorded separately from automatic path inference.
ADDITIONAL_DCSS_ROLES={}
def reviewed_dcss(name,paths,reason,category='equipment',usage='item',note=None):
    ids=dcss(paths)
    for asset_id in ids:
        asset=BY_ID.get(asset_id)
        if not asset:continue
        if asset.get('semanticStatus')!='visually_described':raise ValueError(asset_id)
        ADDITIONAL_DCSS_ROLES[asset_id]=dict(assetId=asset_id,sha256=asset['sha256'],usages=[usage],reason=reason)
        asset['usages']=sorted(set(asset.get('usages',[]))|{usage})
    bind(name,ids,reason,category=category,usage=usage,note=note or 'Reviewed visible form only; game properties remain separate.')

for name,files,reason in [
    ('Greatsword',['greatsword_1_new','greatsword_1_old','greatsword_2','greatsword_3_new','greatsword_3_old','greatsword_4'],'Complete long sword with substantial blade and guard.'),
    ('Shortsword',['short_sword_1_new','short_sword_2_old'],'Complete short sword; knife-like variants excluded.'),
    ('Halberd',['halberd_1','halberd_2','halberd_4','halberd_5'],'Long shaft with a combined pointed/hooked axe head.'),
    ('Greatclub',['giant_club_new','giant_club_old','giant_club_2','giant_club_3'],'Large complete wooden club; exact dimensions are game data.'),
    ('War pick',['hammer_1_old','hammer_2_new','hammer_2_old','hammer_3'],'Complete pointed pick head on a handle; file names are not treated as descriptions.'),
    ('Maul',['war_hammer','lucern_hammer'],'Complete heavy double hammer; weapon statistics supplied by the game.'),
    ('Javelin',['spear','spear_1'],'Slender complete pointed shaft; throwing use and length are game data.'),
    ('Pike',['spear_4','spear_6'],'Long-shafted pointed polearm; exact length is supplied by the equipment record.')]:
    reviewed_dcss(name,['item/weapon/'+f for f in files],reason)
for name,files,reason in [
    ('Hide',['animal_skin_1_new','animal_skin_1_old','animal_skin_2_new','animal_skin_2_old'],'Complete hide garment with straps or a torso outline.'),
    ('Chain mail',['chain_mail_1','chain_mail_2','chain_mail_3','orcish_chain_mail'],'Complete chain-textured torso armour.'),
    ('Chain shirt',['dwarven_ringmail','scale_mail_1_new','scale_mail_1_old'],'Short chain-textured torso garment, selected by appearance rather than filename.'),
    ('Studded leather',['ring_mail_1_new','ring_mail_2_new'],'Dark or brown torso with visible studs; historical filename ignored.'),
    ("Clothes, common",['robe_1_new','robe_1_old','robe_2_new'],'Plain complete belted robe as everyday clothing.'),
    ("Clothes, fine",['robe_art_1','robe_art_2','robe_ego_1','robe_ego_2'],'Decorated complete robe with gold trim or belt.')]:
    reviewed_dcss(name,['item/armor/torso/'+f for f in files],reason)
for names,paths,reason in [
    (['Haste','Expeditious Retreat','Longstrider'],['enchantment/haste_old','air/swiftness_new'],'Running figure or boot with motion streaks.'),
    (['Slow','Time Stop'],['enchantment/slow_new','enchantment/extension'],'Hourglass or hand beside an hourglass; timing rules remain separate.'),
    (['Invisibility','Greater Invisibility'],['enchantment/invisibility_new','enchantment/invisibility_old'],'Faint outlined humanoid.'),
    (['See Invisibility','True Seeing'],['enchantment/see_invisible'],'Eye above a faint figure as a perception motif.'),
    (['Find Traps'],['divination/detect_traps'],'Eye above a floor trap; does not reveal traps without the game check.'),
    (['Locate Creature'],['divination/detect_creatures'],'Eye over a humanoid target.'),
    (['Confusion'],['enchantment/mass_confusion_new'],'Question marks as a confusion motif.'),
    (['Charm Person','CHARM MONSTER'],['enchantment/enslavement_new'],'Heart motif; charm rules are not imported from DCSS.'),
    (['Mirror Image','Blur'],['translocation/phase_shift_old'],'Humanoid beside a pale duplicate.'),
    (['Telekinesis'],['translocation/apportation_new'],'Hand under an object with a dotted movement trail.'),
    (['Spiritual Weapon',"Mordenkainen's Sword",'Magic Weapon'],['enchantment/spectral_weapon','enchantment/sure_blade_old'],'Glowing or spectral sword motif.'),
    (['CAUSE FEAR'],['enchantment/cause_fear_new','enchantment/cause_fear_old'],'Hooded skull or screaming face.')]:
    for name in names:reviewed_dcss(name,['gui/spells/'+p for p in paths],reason,category='spell',usage='spell',note=SPELL_NOTE)
reviewed_dcss('Trees',['dungeon/trees/mangrove_1','dungeon/trees/mangrove_2','dungeon/trees/mangrove_3'],
    'Whole leafy tree with exposed roots; a map prop, not a completed forest landscape.',category='terrain',usage='prop')
reviewed_dcss('Gargoyle',['monster/nonliving/gargoyle'],'Whole winged stone statue creature.',category='monster',usage='map_actor',note=CREATURE_NOTE)
reviewed_dcss('Centaur',['monster/yaktaur_new','monster/yaktaur_old','monster/yaktaur-melee_new','monster/yaktaur-melee_old'],
    'Complete humanoid torso joined to a horse body; weapon variants remain scene choices.',category='monster',usage='map_actor',note=CREATURE_NOTE)
reviewed_dcss('dragon',['monster/dragons/dragon','monster/dragons/iron_dragon','monster/dragons/steam_dragon','monster/dragons/storm_dragon_new'],
    'Whole winged or long-tailed dragon silhouette; no species colour or age inferred.',category='creature_priority',usage='map_actor',note=CREATURE_NOTE)


PORTRAIT_NOTE='Face portrait suggested for an authored character role, not evidence of profession, gender, species, morality or class. Check known character facts and face-family occupancy before assignment.'
human='prepared-portraits-native-32-humans'
for category,name,pack,numbers,reason in [
    ('npc_role','mage',human,[8,23,24],'Visible face beneath a pointed fantasy hat.'),
    ('npc_role','noble',human,[33,34,35],'Visible face wearing a crown or tiara.'),
    ('npc_role','farmer',human,[11],'Visible face beneath a broad straw hat.'),
    ('npc_role','scholar',human,[41],'Visible face with spectacles; scholarship is an authored role.'),
    ('npc_role','sailor','prepared-portraits-native-32-pirates',[2,5,27,41],'Visible face with bandanna or broad maritime-style hat.'),
    ('npc_role','guard','prepared-portraits-native-32-dwarves',[9,33,40,42,43],'Visible face with protective helmet; closed face-covering helmets excluded.'),
    ('npc_role','hunter',human,[25,38],'Visible face with broad hat or green hood.'),
    ('npc_role','guide',human,[25,38,44],'Visible face with practical hat or hood.'),
    ('npc_role','bard',human,[27,29,42],'Visible face under a feathered cap/hat as a performer styling option.'),
    ('class_look','Wizard',human,[8,23,24],'Visible face with pointed hat, as an optional wizard visual style.'),
    ('class_look','Ranger',human,[25,38],'Visible face under broad hat or green hood.'),
    ('class_look','Bard',human,[27,29,42],'Visible face and expressive feathered headwear.'),
    ('class_look','Fighter','prepared-portraits-native-32-dwarves',[9,33,40,42,43],'Visible face in protective headgear.'),
    ('class_look','Rogue',human,[6,18,44],'Visible hood-framed face; criminality and class are not visual facts.')]:
    bind(name,icons(pack,numbers),reason,category=category,usage='portrait',note=PORTRAIT_NOTE)
bind('Blanket',icons(B+'28-dwarf-loot',[37])+icons(B+'35-kobold-loot',[29]),'Folded cloth with a visible border; exact dimensions and warmth are game data.')
bind('Mess kit',icons(B+'35-kobold-loot',[37]),'Bowl and spoon as an illustration of eating utensils; full kit contents remain game data.')
reviewed_dcss('Lamp',['item/misc/magic_lamp'],'Complete shallow oil-lamp silhouette; no magical ability is inferred.')
vessels=icons(P+'29-potions',[3,5,9,10,15,19,21,28,33,34,36,40,46])
for name in ['Alchemist\'s fire (flask)','Perfume (vial)','Potion of healing','Acid (vial)','Oil (flask)','Antitoxin (vial)','Holy water (flask)','Poison, basic (vial)']:
    bind(name,vessels,'Complete closed flask/vial; the contained substance is an authored fact, never inferred from colour.',
         note='Container illustration only. Keep an assigned appearance stable and do not disclose unidentified contents.')
for name in ['POTION OF CLIMBING','POTION OF HEALING','POTION OF Greater HEALING','POTION OF ANIMAL FRIENDSHIP',
             'POTION OF FIRE BREATH','POTION OF WATER BREATHING','POTION OF Hill GIANT STRENGTH','POTION OF RESISTANCE',
             'POTION OF POISON','POTION OF GASEOUS FORM','POTION OF HEROISM','POTION OF INVULNERABILITY','POTION OF Superior HEALING',
             'POTION OF stone GIANT STRENGTH','POTION OF FROST GIANT STRENGTH','POTION OF FIRE GIANT STRENGTH',
             'POTION OF GROWTH','POTION OF DIMINUTION','POTION OF MIND READING','POTION OF CLAIRVOYANCE',
             'POTION OF LONGEVITY','POTION OF VITALITY','POTION OF INVISIBILITY','POTION OF FLYING','POTION OF Supreme HEALING',
             'POTION OF cloud GIANT STRENGTH','POTION OF SPEED','POTION OF storm GIANT STRENGTH','ELIXIR OF HEALTH','PHILTER OF LOVE',
             'OIL OF SLIPPERINESS','OIL OF ETHEREALNESS','OIL OF SHARPNESS','PERFUME OF BEWITCHING','UNIVERSAL SOLVENT','SOVEREIGN GLUE']:
    magic(name,vessels,'Complete stoppered flask; no exact unique appearance established. Substance, dose and magical effect remain hidden game facts.')
magic('BROOCH OF SHIELDING',icons(B+'28-dwarf-loot',[15]),'Complete decorative flower brooch; shielding is not inferred from its appearance.')
magic('NECKLACE OF PRAYER BEADS',icons(B+'28-dwarf-loot',[36]),'Necklace with distinct round beads; powers belong to the item record.')
magic('NECKLACE OF FIREBALLS',icons(B+'28-dwarf-loot',[36]),'Necklace with round orange beads; these do not establish fire damage visually.')
magic('GAUNTLETS OF OGRE POWER',icons(B+'01-bracers',[3,18,25,34]),'Paired metal hand armour; strength is a game property.')
magic('BRACERS OF DEFENSE',icons(B+'01-bracers',[7,17,24,42]),'Paired forearm armour; defence bonuses are not read from pixels.')
for name in ['BOOTS OF FALSE TRACKS','BOOTS OF STRIDING AND SPRINGING','BOOTS OF LEVITATION','BOOTS OF SPEED']:
    magic(name,icons(B+'04-boots',[2,7,8,16,17]),'Complete boots; tracks, movement and magical effects are not inferred.')
for name in ['BELT OF DWARVENKIND','BELT OF hill GIANT STRENGTH','BELT OF stone GIANT STRENGTH',
             'BELT OF frost GIANT STRENGTH','BELT OF fire GIANT STRENGTH','BELT OF storm GIANT STRENGTH','BELT OF cloud GIANT STRENGTH']:
    magic(name,icons(B+'35-kobold-loot',[20]),'Complete brown belt with silver buckle; magical strength is not visually established.')
for name in ['HAT OF WIZARDRY','HAT OF VERMIN','HAT OF DISGUISE']:
    reviewed_dcss(name,['item/armor/headgear/hat_1','item/armor/headgear/hat_2','item/armor/headgear/hat_3'],
        'Complete pointed hat; its powers are supplied by game data.',category='magic_item',note=MAGIC_NOTE)
for name in ['HELM OF COMPREHENDING LANGUAGES','HELM OF TELEPATHY','HELM OF TELEPORTATION','HELM OF BRILLIANCE']:
    magic(name,icons(B+'28-dwarf-loot',[23])+icons(B+'35-kobold-loot',[10]),'Complete metal helmet; powers and language knowledge remain game data.')
for name in ['DECK OF ILLUSIONS','DECK OF MANY THINGS']:
    reviewed_dcss(name,['item/misc/misc_deck_new','item/misc/misc_deck_old','item/misc/misc_deck_rare_new','item/misc/misc_deck_rare_old'],
        'Complete patterned card deck; effects are not imported from the source game.',category='magic_item',note=MAGIC_NOTE)
reviewed_dcss('WIND FAN',['item/misc/misc_fan_new','item/misc/misc_fan_old'],
    'Complete handheld folding fan; wind power remains an item property.',category='magic_item',note=MAGIC_NOTE)


for name in ['RING OF ANIMAL INFLUENCE','RING OF PROTECTION','RING OF FEATHER FALLING','RING OF X-RAY VISION',
             'RING OF FREE ACTION','RING OF RESISTANCE','RING OF THE RAM','RING OF EVASION','RING OF SPELL STORING',
             'RING OF SHOOTING STARS','RING OF REGENERATION','RING OF TELEKINESIS','RING OF water ELEMENTAL COMMAND',
             'RING OF Air ELEMENTAL COMMAND','RING OF earth ELEMENTAL COMMAND','RING OF fire ELEMENTAL COMMAND',
             'RING OF INVISIBILITY','RING OF SPELL TURNING','RING OF DJINNI SUMMONING','RING OF THREE WISHES','RING OF WINTER']:
    magic(name,icons(P+'31-jewellery',[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,25,26,27,28]),
          'Complete finger ring; this is a generic base-form proposal, not an exact unique magical-item appearance.')
for name in ['CLOAK OF MANY FASHIONS','CLOAK OF BILLOWING','CLOAK OF PROTECTION','CLOAK OF THE MANTA RAY',
             'CLOAK OF ELVENKIND','MANTLE OF SPELL RESISTANCE','CLOAK OF THE BAT','CLOAK OF DISPLACEMENT',
             'CAPE OF THE MOUNTEBANK','CLOAK OF ARACHNIDA','CLOAK OF INVISIBILITY']:
    reviewed_dcss(name,['item/armor/back/cloak_1_leather','item/armor/back/cloak_2','item/armor/back/cloak_3','item/armor/back/cloak_4'],
        'Complete cloak as a base-form proposal; transformed wings/fins and hidden powers are not depicted.',category='magic_item',note=MAGIC_NOTE)
for name in ['ROBE OF THE ARCHMAGI']:
    magic(name,variants_of('Robes'),'Complete ornamented robe; magical properties are not visually confirmed.')
for name in ['WAND OF PYROTECHNICS','WAND OF SMILES','WAND OF SCOWLS','WAND OF THE WAR MAGE +1','WAND OF THE WAR MAGE +2',
             'WAND OF THE WAR MAGE +3','WAND OF ENEMY DETECTION','WAND OF POLYMORPH']:
    magic(name,variants_of('Wand'),'Complete wand; magical power is not visually inferred.')
for name in ['ROD OF THE PACT KEEPER +1','ROD OF THE PACT KEEPER +2','ROD OF THE PACT KEEPER +3','ROD OF RULERSHIP',
             'ROD OF ALERTNESS','ROD OF SECURITY','ROD OF RESURRECTION','IMMOVABLE ROD']:
    magic(name,variants_of('Rod'),'Complete rod; switches and hidden mechanisms are not claimed visible.')
for name in ['PERIAPT OF PROOF AGAINST POISON','AMULET OF THE PLANES','TALISMAN OF THE SPHERE','TALISMAN OF PURE GOOD','TALISMAN OF ULTIMATE EVIL']:
    magic(name,amulets,'Complete neck-worn talisman; alignment and powers are not image observations.')
for name in ['SWORD OF VENGEANCE','SWORD OF LIFE STEALING','SWORD OF WOUNDING','SWORD OF SHARPNESS','DANCING SWORD',
             'NINE LIVES STEALER','DEFENDER','LUCK BLADE','SWORD OF ANSWERING','HOLY AVENGER']:
    magic(name,variants_of('Longsword'),'Complete sword as a base-form illustration; no unique magical identity has been visually established.')
magic('BERSERKER AXE',variants_of('Battleaxe'),'Complete axe; rage and curses are not visually revealed.')
magic('DWARVEN THROWER',variants_of('Warhammer'),'Complete warhammer; throwing and returning are game properties.')
magic('HAMMER OF THUNDERBOLTS',variants_of('Maul'),'Complete heavy hammer; thunder effects are not claimed depicted.')
for name in ['STONE OF GOOD LUCK (LUCKSTONE)','STONE OF CONTROLLING EARTH ELEMENTALS']:
    reviewed_dcss(name,['item/misc/misc_stone_new','item/misc/misc_stone_old'],
        'Complete stone; luck and elemental control are hidden item properties.',category='magic_item',note=MAGIC_NOTE)
for name,files,reason in [
    ('Air Elemental',['nonliving/air_elemental_old'],'Whole translucent tornado-like air creature.'),
    ('Clay Golem',['nonliving/clay_golem'],'Whole tan blocky humanoid statue.'),
    ('Iron Golem',['nonliving/iron_golem'],'Whole grey segmented metal statue.'),
    ('Stone Golem',['nonliving/flesh_golem'],'Whole broad brown stone humanoid; chosen by visible stone form, not the source filename.'),
    ('Wraith',['undead/wraith'],'Whole grey spectral skeletal figure.'),
    ('Specter',['undead/phantom_new','undead/phantom_old'],'Whole translucent cyan humanoid shape.')]:
    reviewed_dcss(name,['monster/'+f for f in files],reason,category='monster',usage='map_actor',note=CREATURE_NOTE)


for names,pack,numbers,reason in [
    (['Fog Cloud'],wind,[9],'Grey cloud bank; visibility rules remain game data.'),
    (['Magic Mouth'],wind,[36],'An isolated open mouth as a speech motif.'),
    (['Daylight','Sunburst','DAWN','WORD of RADIANCE'],holy,[17],'Radiant sun with light rays; damage and radius are game data.'),
    (['Shield of Faith','Sanctuary'],holy,[34,42],'Shield motif for magical protection.'),
    (['Blade Ward'],P+'30-buffs',[10,12,14,16],'Weapon over a shield as resistance-to-weapon-damage motif.'),
    (['Protection from Energy','Absorb Elements'],P+'30-buffs',[2,4,24],'Elemental mark on a shield; chosen element must match the game state.'),
    (['Death Ward'],P+'30-buffs',[8],'Skull on a shield as protection from death motif.'),
    (['Hold Monster','Imprisonment','MENTAL PRISON'],P+'37-debuffs',[3],'Restrained hand as a binding motif; target type is not inferred.'),
    (['Forcecage'],B+'32-lightning',[41],'Complete hanging cage as a confinement motif, not a physical inventory item.'),
    (['SOUL CAGE'],B+'25-demon-magic',[20],'Skull beneath a cage as a soul confinement motif.'),
    (['Freedom of Movement'],B+'27-dwarf',[42],'Boot trampling a chain as escape from restraint.'),
    (['Gate','Arcane Gate','Dimension Door','Demiplane'],B+'29-engineering',[48],'Oval portal; destination and travel rules are not inferred.'),
    (["Hunter's Mark"],P+'16-archer',[48],'Heart inside a circular target reticle.'),
    (['Circle of Death','Power Word Kill','TOLL THE DEAD'],B+'19-necromancer',[1,22,46],'Skull/death motif; mechanics remain in the ruleset.'),
    (['Create Undead','DANSE MACABRE','SUMMON UNDEAD'],B+'19-necromancer',[2,4,6,19],'Skeletons or rising undead as a necromancy motif.')]:
    for name in names:spell(name,pack,numbers,reason)


# The toothed chest is a complete mimic, not an inventory container. Its
# prepared pack originally carried an overly broad item role.
mimic_id=icons(B+'36-kobolds',[34])[0]
if mimic_id in BY_ID:
    asset=BY_ID[mimic_id]
    asset['usages']=['map_actor']
    ADDITIONAL_DCSS_ROLES[mimic_id]=dict(assetId=mimic_id,sha256=asset['sha256'],usages=['map_actor'],replace=True,
        reason='Visually rechecked complete toothed open chest creature; not an ordinary container.')
for category,name in [('monster','Mimic'),('creature_priority','mimic')]:
    bind(name,[mimic_id],'Open wooden chest with clearly visible teeth; use only for the revealed creature form.',
         category=category,usage='map_actor',note='Disguised mimic must use an ordinary object appearance until revealed; no automatic secret disclosure.')


magic('PEARL OF POWER',icons(P+'04-crafting-materials',[12]),'A single complete pale round pearl-like bead; magic is a game property.')
magic('BROOM OF FLYING',icons(P+'23-farming',[23,24]),'Complete household broom; flight is not visually inferred.')
magic('PIPE OF SMOKE MONSTERS',icons(B+'28-dwarf-loot',[1,22]),'Complete smoking pipe; creature-shaped smoke is only needed when the item is activated.')
for name in ['HORN OF SILENT ALARM','HORN OF BLASTING','HORN OF VALHALLA']:
    magic(name,icons(P+'12-artifacts',[14]),'Complete gold-rimmed horn; sound and summons are supplied by the item record.')
magic("QUAAL'S FEATHER TOKEN",icons(B+'22-undead-loot',[38,45])+icons(B+'24-fairy-loot',[34])+icons(B+'28-dwarf-loot',[7]),
      'A complete feather; transformed object is not shown before activation.')
magic('UNBREAKABLE ARROW',variants_of('Arrows (20)'),'Complete arrow; durability is not read from the artwork.')
magic('ARROW OF SLAYING',variants_of('Arrows (20)'),'Complete arrow; the intended creature type remains a game fact.')
magic('JAVELIN OF LIGHTNING',variants_of('Javelin'),'Complete javelin before activation; lightning transformation is not shown as a permanent state.')
for name in ['DWARVEN PLATE','PLATE ARMOR OF ETHEREALNESS','ARMOR OF INVULNERABILITY']:
    magic(name,variants_of('Plate'),'Plate torso as an armour-set illustration; extra protection and ethereal travel are game properties.')
magic('ELVEN CHAIN',variants_of('Chain shirt'),'Complete chain shirt; origin and magical properties are not visually inferred.')
magic('EFREETI CHAIN',variants_of('Chain mail'),'Complete chain armour; elemental protection is a game property.')
for name in ['STAFF OF THUNDER AND LIGHTNING','STAFF OF FROST','STAFF OF FIRE','STAFF OF WITHERING','STAFF OF SWARMING INSECTS']:
    magic(name,icons(P+'40-staves',[2,4,10,13,21,22,27,34,41,43]),'Complete ornamented staff before activation; element and charges are not inferred from colour.')


reviewed_dcss('Sling bullets (20)',['item/weapon/ranged/sling_bullet_1_old','item/weapon/ranged/sling_bullet_2_new'],
    'Complete rounded sling projectile; quantity and material are game data.')
reviewed_dcss('Blowgun needles (50)',['item/weapon/ranged/needle-c'],
    'Small complete narrow dart; quantity and poison coating are not visually inferred.')
bind('Ink pen',icons(B+'22-undead-loot',[4]),'Clearly visible writing quill beside an ink pot; the accompanying pot is incidental context.')
magic('POT OF AWAKENING',icons(P+'23-farming',[46]),'Complete orange clay-like planting pot; awakening requires game actions, not a living creature icon.')
for category,name,ids,reason in [
    ('terrain','Altar',altars,'Complete altar as a map prop.'),
    ('terrain','Barrels',barrels,'Complete barrel as a map prop illustration.')]:
    bind(name,ids,reason,category=category,usage='prop' if name=='Altar' else 'item')

def add_variants(name,ids,reason):
    existing=next(b for b in BINDINGS if b['category']=='equipment' and b['name']==name)
    bind(name,ids,reason)
    extra=BINDINGS.pop()['candidates']
    old={c['assetId'] for c in existing['candidates']}
    existing['candidates']=[c for c in extra if c['assetId'] not in old]+existing['candidates']
add_variants('Crossbow, hand',[f'craftpix-996288:icon-{n:03d}_no_effect' for n in [25,26]]+icons('craftpix-996288',[28,31,34,41]),
             'Complete compact crossbow from a new pack, without surrounding magical effects.')
add_variants('Crossbow, light',[f'craftpix-996288:icon-{n:03d}_no_effect' for n in [27,29,30,32,37,43,46]]+icons('craftpix-996288',[33,35,39,42,48]),
             'Complete crossbow from a new pack; exact weight is a game property.')


for name,files,reason in [
    ('Crab',['animals/fire_crab','animals/moth_of_wrath_new','animals/moth_of_wrath_old'],'Complete broad crab with claws; source names do not assign abilities.'),
    ('Giant Crab',['animals/fire_crab','animals/moth_of_wrath_new','animals/moth_of_wrath_old'],'Complete crab; giant size is supplied by the scene.'),
    ('Giant Wolf Spider',['animals/wolf_spider_old'],'Complete brown many-legged spider; giant scale is a game property.'),
    ('Mummy',['undead/mummy','undead/guardian_mummy'],'Complete wrapped humanoid, optionally with a headdress.'),
    ('Mummy Lord',['undead/greater_mummy','undead/mummy_priest'],'Complete wrapped humanoid with ornate headdress or staff.'),
    ('Hippogriff',['hippogriff_new','hippogriff_old'],'Complete eagle-headed winged quadruped as the mythic creature illustration.')]:
    reviewed_dcss(name,['monster/'+f for f in files],reason,category='monster',usage='map_actor',note=CREATURE_NOTE)
kobold_id=icons(B+'36-kobolds',[1])[0]
if kobold_id in BY_ID:
    asset=BY_ID[kobold_id];asset['usages']=['map_actor']
    ADDITIONAL_DCSS_ROLES[kobold_id]=dict(assetId=kobold_id,sha256=asset['sha256'],usages=['map_actor'],replace=True,
        reason='Visually rechecked small whole red reptilian humanoid with tail and blade; not an inventory item.')
for category,name in [('monster','Kobold'),('creature_priority','kobold')]:
    bind(name,[kobold_id],'Whole small red reptilian humanoid with tail and blade; not a portrait.',category=category,usage='map_actor',note=CREATURE_NOTE)


for names,files,reason in [
    (['Passwall'],['earth/passwall_new'],'Narrow stone doorway as passage through a wall motif.'),
    (['Shatter'],['earth/shatter_old'],'Burst over a stone base.'),
    (['Stoneskin','Flesh to Stone','Investiture of Stone'],['earth/statue_form_old'],'Grey statue-like humanoid with segmented limbs.'),
    (['Sleet Storm',"Snilloc's Snowball Swarm"],['ice/freezing_cloud_new','ice/ice_storm_old'],'Icy cloud or vortex with snow-like sparkles.'),
    (['Ray of Sickness',"Melf's Acid Arrow"],['poison/venom_bolt_new','poison/venom_bolt_old'],'Green beam or projectile with a droplet; poison/acid distinction comes from the named spell.'),
    (['Find Familiar'],['summoning/call_canine_familiar_old','summoning/summon_small_mammals'],'Small animal above a summoning ring; selected familiar species must match.'),
    (['Conjure Animals','SUMMON BEAST'],['summoning/monstrous_menagerie','summoning/summon_small_mammal'],'Animal above a summoning ring; exact species and count remain game data.'),
    (['Conjure Elemental','Conjure Minor Elementals','SUMMON ELEMENTAL'],['summoning/summon_elemental'],'Four coloured elemental drops above a summoning ring.'),
    (['SUMMON LESSER DEMONS','SUMMON GREATER DEMON','SUMMON FIEND','INFERNAL CALLING'],['summoning/summon_demon_new','summoning/summon_demon_old'],'Horned or winged creature above a summoning ring.'),
    (['SUMMON ABERRATION'],['summoning/summon_horrible_things'],'Tentacles rising from a summoning ring.'),
    (['SUMMON SHADOWSPAWN'],['summoning/summon_shadow_creatures_new'],'Dark humanoid within a magical frame.'),
    (['Summon Draconic Spirit','ILLUSORY DRAGON'],['summoning/summon_dragon'],'Horned winged draconic head as a conjuration motif.'),
    (['Conjure Plant Creature','Conjure Minor Plant Creatures','DRUID GROVE'],['summoning/summon_forest'],'Tree standing over a magical ground ring.')]:
    for name in names:reviewed_dcss(name,['gui/spells/'+f for f in files],reason,category='spell',usage='spell',note=SPELL_NOTE)


for name,asset_id,reason in [
    ('Grass','grass','Native 32x32 green grassy surface, visually inspected.'),
    ('Sand','sand','Native 32x32 ochre sand with wind-shaped ridges.'),
    ('Dunes','sand','Wind-ridged sand surface; elevation comes from terrain data.'),
    ('Snow','snow','Native 32x32 white snow with pale blue ridges.'),
    ('Deep water','water','Dark blue water with turquoise ripples; depth and traversal remain game facts.'),
    ('Path','road','Horizontal cobbled path segment with transparent edges; orient/connect using map topology.')]:
    bind(name,['gwiztiles:'+asset_id],reason,category='terrain',usage='terrain_tile',
         note='GwizTiles native 32x32, CC BY 4.0, David Gwizdala. Surface tile, not a world-map location illustration. Style and tile connections require in-game review.')
for name,paths,usage,reason in [
    ('Rough stone',['gui/spells/components/stone'],'terrain_tile','Visually inspected unframed grey stone surface filling a 32x32 square.'),
    ('Cracked slabs',['gui/spells/components/stone_cracked'],'terrain_tile','Visually inspected cracked grey stone slab filling a 32x32 square.'),
    ('Rocks',['gui/spells/components/stones'],'prop','Scattered grey stone fragments; transparent overlay requires an underlying ground tile.'),
    ('Scree',['gui/spells/components/stones'],'prop','Loose scattered grey stone fragments as a ground overlay.'),
    ('Rubble',['gui/spells/components/stones'],'prop','Scattered broken stones as a rubble overlay; no intact column or creature.'),
    ('Boulders',['item/misc/misc_stone_old'],'prop','Visually inspected angular grey boulder with a light upper face and dark lower facets; transparent native 32px sprite.'),
    ('Moss',['dungeon/mold_large_1','dungeon/mold_large_2','dungeon/mold_large_3','dungeon/mold_large_4'],'prop','Scattered dark-green organic patches as moss-like ground cover; overlay, not a full ground texture.'),
    ('Column',['dungeon/statues/crumbled_column_2','dungeon/statues/crumbled_column_3'],'prop','Visually inspected grey column on a base, including damaged variants.'),
    ('Stairs up',['dungeon/gateways/stone_stairs_up'],'prop','Visually inspected stone steps rising diagonally.'),
    ('Stairs down',['dungeon/gateways/stone_stairs_down'],'prop','Visually inspected stairwell descending into a dark rectangular pit.')]:
    reviewed_dcss(name,paths,reason,category='terrain',usage=usage,
        note='Explicit terrain review. Preserve ground/overlay distinction, map state and style compatibility; not a place-view replacement.')


def main():
    for binding in BINDINGS:
        if (binding['category'],binding['name']) in [('prop_priority','door'),('prop_priority','trap')]:
            binding['selectionPolicy']['mode']='state_variants'
            binding['selectionPolicy']['requiredChecks'].append('object_state_and_visibility')
    roles_path=ROOT/'src/ArtLibrary/reviewed-usages.json'
    roles=json.loads(roles_path.read_text(encoding='utf-8'))
    by_id={r['assetId']:r for r in roles['assets']}
    for asset_id,entry in ADDITIONAL_DCSS_ROLES.items():
        if asset_id in by_id and by_id[asset_id]['sha256']!=entry['sha256']:raise ValueError('Changed reviewed image: '+asset_id)
        by_id[asset_id]=entry
    roles['assets']=list(by_id.values())
    roles_path.write_text(json.dumps(roles,indent=2),encoding='utf-8')
    document = merge_spell_bindings(ROOT,
        dict(schemaVersion=1, ruleSet='Dnd5e', status='review_draft', bindings=BINDINGS), load_spell_assets(ROOT))
    save_bindings(ROOT, document)
    print(f'{len(document["bindings"])} needs; {sum(len(b["candidates"]) for b in document["bindings"])} candidate links; fixed named spell icons synchronized.')


if __name__=='__main__':main()
