"""Authored anatomy families for visual review, not automatic runtime approval."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/art-completion'
assets={a['id']:a for a in json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))}
requirements=json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))
by_english={r.get('nameEn',r['name']):r for r in requirements if r['category']=='monster' and not r.get('candidates')}
rules={
 'animals/grizzly_bear':'Brown Bear|Cave Bear',
 'animals/bat':'Giant Bat|Swarm of Bats',
 'animals/boar':'Boar|Giant Boar',
 'animals/hog_new':'Boar|Giant Boar',
 'animals/hound':'Blink Dog',
 'animals/war_dog':'Wolf',
 'animals/bear':'Giant Bear',
 'animals/giant_beetle':'Giant Fire Beetle',
 'animals/giant_blowfly':'Giant Fly',
 'animals/giant_cockroach_new':'Swarm of Beetles',
 'animals/jackal':'Giant Hyena|Hyena',
 'animals/river_rat':'Giant Rat Diseased',
 'animals/rat':'Rat|Swarm of Rats',
 'animals/raven':'Swarm of Ravens',
 'animals/redback':'Swarm of Spiders',
 'animals/spider':'Swarm of Spiders',
 'animals/scorpion':'Scorpion',
 'animals/wasp':'Swarm of Insects|Wasp',
 'animals/yellow_wasp':'Swarm of Wasps',
 'animals/sheep':'Goat|Giant Goat',
 'animals/porcupine':'Giant Badger|Badger',
 'animals/snake':'Swarm of Poisonous Snakes',
 'animals/adder':'Swarm of Poisonous Snakes',
 'animals/turtle':'Giant Sea Horse',
 'animals/alligator_snapping_turtle_new':'Dragon Turtle',
 'animals/elephant_new':'Mammoth',
 'animals/giant_lizard':'Giant Crocodile',
 'animals/crocodile':'Giant Crocodile',
 'animals/komodo_dragon':'Giant Lizard',
 'undead/zombies/zombie_small':'Zombie|Revenant|Ghast',
 'undead/skeletons/skeleton_humanoid_large_new':'Minotaur Skeleton',
 'undead/skeletons/skeleton_dragon':'Adult Blue Dracolich',
 'undead/skeletons/skeleton_snake':'Bone Naga Guardian|Bone Naga Spirit',
 'undead/spectrals/spectral_warrior':'Specter|Poltergeist|Banshee',
 'undead/wraith':'Specter|Poltergeist',
 'undead/vampire':'Vampire Spellcaster|Vampire Warrior|Vampire Spawn|Vampire Warrior',
 'undead/phantom':'Banshee|Will-o’-Wisp',
 'undead/freezing_wraith':'Wraith',
 'undead/flayed_ghost':'Banshee',
 'undead/wight':'Sword Wraith Warrior|Sword Wraith Commander',
 'ogre_new':'Half-Ogre (Ogrillon)|Ogre Battering Ram|Ogre Bolt Launcher|Ogre Chain Brute|Ogre Howdah|Mouth of Grolantor',
 'two_headed_ogre_new':'Ettin',
 'ogre_mage_new':'Oni',
 'troll':'Venom Troll|Rot Troll|Spirit Troll|Dire Troll',
 'goblin_new':'Goblin Boss',
 'hobgoblin_new':'Hobgoblin Captain|Hobgoblin Warlord|Hobgoblin Iron Shadow',
 'hobgoblin_old':'Hobgoblin Devastator',
 'orc_warrior_new':'Orc War Chief|Orc Blade of Ilneval|Orc Red Fang of Shargaas',
 'orc_priest_new':'Orc Eye of Gruumsh|Orc Hand of Yurtrus',
 'orc_high_priest_new':'Orc Nurtured One of Yurtrus',
 'orc_sorcerer_new':'Orc Claw of Luthic',
 'orc_new':'Orog',
 'big_kobold_new':'Kobold Dragonshield|Kobold Inventor',
 'kobold_demonologist':'Kobold Scale Sorcerer',
 'gnoll_sergeant':'Gnoll Pack Lord|Gnoll Hunter|Gnoll Flesh Gnawer|Flind',
 'gnoll_shaman':'Gnoll Fang of Yeenoghu',
 'deep_dwarf':'Duergar',
 'deep_elf_fighter_new':'Drow|Drow Elite Warrior|Drow House Captain',
 'deep_elf_mage':'Drow Mage|Drow Arachnomancer',
 'deep_elf_high_priest':'Drow Priestess of Lolth|Drow Matron Mother',
 'deep_elf_blademaster':'Drow Shadowblade',
 'deep_elf_priest':'Drow Favored Consort',
 'wizard':'Mage|Archmage|Apprentice Wizard|Abjurer|Conjurer|Diviner|Enchanter|Evoker|Illusionist|Transmuter|Warlock of the Archfey|Warlock of the Fiend|Warlock of the Great Old One|Warlock of the Great Old One',
 'necromancer_new':'Necromancer|Deathlock|Deathlock Mastermind|Deathlock Wight',
 'human_new':'Commoner|Acolyte|Cultist|Bandit|Berserker|Gladiator|Scout|Veteran|Guard|Thug|Tribal Warrior|Archer|Champion|Master Thief|Swashbuckler|Assassin|Bandit Captain|Cult Fanatic|Priest|Druid|Archdruid|Bard|Blackguard|Martial Arts Adept|War Priest',
 'tengu_new':'Aarakocra|Kenku',
 'tengu':'Aarakocra|Kenku',
 'merfolk':'Merfolk',
 'merfolk_aquamancer':'Merrow',
 'dragons/shadow_dragon':'Shadow Dragon|Adult Black Dragon|Ancient Black Dragon|Young Black Dragon|Black Dragon Wyrmling',
 'dragons/storm_dragon_new':'Adult Blue Dragon|Ancient Blue Dragon|Young Blue Dragon|Blue Dragon Wyrmling',
 'dragons/ice_dragon_new':'Adult White Dragon|Ancient White Dragon|Young White Dragon|White Dragon Wyrmling',
 'dragons/golden_dragon':'Adult Gold Dragon|Ancient Gold Dragon|Young Gold Dragon|Gold Dragon Wyrmling',
 'dragons/swamp_dragon_new':'Adult Green Dragon|Ancient Green Dragon|Young Green Dragon|Green Dragon Wyrmling',
 'dragons/iron_dragon':'Adult Silver Dragon|Ancient Silver Dragon|Young Silver Dragon|Silver Dragon Wyrmling',
 'dragons/mottled_dragon':'Adult Red Dragon|Ancient Red Dragon|Young Red Dragon|Red Dragon Wyrmling',
 'demons/balrug_new':'Balor',
 'demons/efreet':'Efreeti',
 'demons/hairy_devil':'Barlgura',
 'demons/red_devil_new':'Bearded Devil|Barbed Devil',
 'demons/ice_devil':'Ice Devil Spear',
 'demons/rotting_devil':'Ghast',
 'demons/reaper_new':'Avatar of Death',
 'demons/green_death':'Green Slaad',
 'demons/blue_death':'Blue Slaad',
 'demons/neqoxec_new':'Mind Flayer',
 'nonliving/guardian_golem':'Animated Armor',
 'nonliving/spectral_sbl':'Flying Sword',
 'nonliving/insubstantial_wisp':'Will-o’-Wisp',
 'nonliving/fire_vortex':'Fire Snake',
 'slime_creatures/slime_creature_new':'Gelatinous Cube|Black Pudding|Ochre Jelly',
 'jelly':'Ochre Jelly|Gray Ooze',
 'jelly_amazon':'Oblex Adult',
 'jelly_death':'Black Pudding',
}
proposals=[];assigned=set()
for relative,names in rules.items():
    asset=assets.get('dcss/monster/'+relative)
    if not asset or asset.get('width')!=32 or asset.get('height')!=32:continue
    needs=[by_english[n] for n in dict.fromkeys(names.split('|')) if n in by_english and n not in assigned]
    if not needs:continue
    proposals.append(dict(assetId=asset['id'],sha256=asset['sha256'],sourceDescription=asset.get('descriptionEn'),
                          requirements=[dict(name=r['name'],nameEn=r.get('nameEn',r['name'])) for r in needs]))
    assigned.update(r.get('nameEn',r['name']) for r in needs)
(OUT/'monster-family-proposals.json').write_text(json.dumps(proposals,ensure_ascii=False,indent=2),encoding='utf-8')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',12)
for start in range(0,len(proposals),48):
    group=proposals[start:start+48];sheet=Image.new('RGB',(960,((len(group)+7)//8)*145),'#20262c');draw=ImageDraw.Draw(sheet)
    for n,row in enumerate(group):
        im=Image.open(ROOT/'artifacts/art-catalog-review'/assets[row['assetId']]['preview']).convert('RGBA').resize((96,96),Image.Resampling.NEAREST)
        x=n%8*120;y=n//8*145;sheet.paste(im,(x+12,y),im);draw.text((x+2,y+98),f'{start+n+1}: '+row['requirements'][0]['nameEn'][:16],font=font,fill='white');draw.text((x+2,y+116),str(len(row['requirements']))+' references',font=font,fill='#bbd2df')
    sheet.save(OUT/f'monster-families-{start//48+1}.png')
print('Unapproved anatomy proposals:',len(proposals),'sprites for',len(assigned),'reference entries.')
