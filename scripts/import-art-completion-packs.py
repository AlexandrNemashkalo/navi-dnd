"""Import reviewed, original 32px furniture and selected utility icons without backgrounds."""
import hashlib
import io
import json
import shutil
import zipfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT/'src/ArtLibrary'
WORK = ROOT/'artifacts/art-completion'
manifest = LIB/'external-assets.json'
entries = json.loads(manifest.read_text(encoding='utf-8'))
by_id = {a['id']: a for a in entries}


def add(pack, key, raw, name, name_ru, description, usage, url, author, license_name, member):
    with Image.open(io.BytesIO(raw)) as image:
        if image.size != (32, 32):
            raise ValueError('Expected original 32px icon: '+key)
        if image.convert('RGBA').getchannel('A').getbbox() is None:
            raise ValueError('Empty icon: '+key)
    target = LIB/'Sources'/pack/(key+'.png')
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.read_bytes() != raw:
        raise ValueError('Modified source preserved: '+str(target))
    target.write_bytes(raw)
    entry = dict(id=pack+':'+key, pack=pack, file=key+'.png', name=name, nameRu=name_ru,
                 sha256=hashlib.sha256(raw).hexdigest(), usage=usage,
                 style='fantasy.pixel.furniture.32' if usage=='prop' else 'fantasy.pixel.item.32',
                 framing='isolated_object', kind='prop' if usage=='prop' else 'item',
                 descriptionEn=description, sourceUrl=url, author=author, license=license_name,
                 licenseFile='Sources/'+pack+'/LICENSE.txt', sourceMember=member)
    if entry['id'] in by_id and any(by_id[entry['id']].get(k) != v for k,v in entry.items()):
        raise ValueError('Existing external metadata changed: '+entry['id'])
    if entry['id'] not in by_id:
        entries.append(entry); by_id[entry['id']] = entry


furniture_ru = {
    'chair':'Стул', 'stool':'Табурет', 'throne':'Трон', 'cushion':'Подушка', 'bench':'Скамья',
    'bed':'Кровать', 'hammock':'Гамак', 'quilt':'Одеяло', 'chest':'Сундук', 'wardrobe':'Шкаф',
    'drawers':'Комод', 'barrel':'Бочка', 'crate':'Ящик', 'potion_shelf':'Полка с флаконами',
    'trunk_arcane':'Украшенный сундук', 'desk':'Письменный стол', 'bookcase':'Книжный шкаф',
    'lectern':'Кафедра', 'spellbook':'Книга заклинаний', 'scroll_rack':'Стойка со свитками',
    'celestial_globe':'Глобус', 'candle_stand':'Подсвечник', 'candelabra_three':'Канделябр',
    'magic_lamp':'Лампа', 'crystal_orb_lamp':'Хрустальный светильник', 'sun_orb_lamp':'Сферический светильник',
    'chandelier_gold':'Люстра', 'wall_torch':'Настенный факел', 'hanging_lantern':'Подвесной фонарь',
    'rug':'Ковёр', 'magic_mirror':'Зеркало', 'cauldron':'Котёл', 'fireplace':'Камин',
    'potted_plant':'Растение в горшке', 'potted_moon_fern':'Папоротник в горшке', 'desk_skull':'Череп',
    'brass_telescope':'Телескоп', 'hourglass':'Песочные часы', 'banner':'Знамя',
    'round_table':'Круглый стол', 'long_table':'Длинный стол', 'arcane_altar':'Алтарь', 'alchemy_table':'Алхимический стол',
}
with zipfile.ZipFile(WORK/'wizard-furniture/pack-1.zip') as archive:
    pack='wizard-furniture-32'
    folder=LIB/'Sources'/pack;folder.mkdir(parents=True,exist_ok=True)
    (folder/'LICENSE.txt').write_bytes(archive.read('LICENSE.txt'))
    (folder/'README.md').write_bytes(archive.read('README.md'))
    for key, info in json.loads(archive.read('spritesheet.json'))['frames'].items():
        member='PNG/32x32/'+info['category']+'/'+key+'.png'
        base=next((label for prefix,label in sorted(furniture_ru.items(),key=lambda x:-len(x[0])) if key==prefix or key.startswith(prefix+'_')),None)
        if not base:raise ValueError('Missing furniture description: '+key)
        name=key.replace('_',' ')
        add(pack,key,archive.read(member),name,base,'Complete isolated '+name+'; transparent background, no UI frame.',
            'prop','https://reodesuxz.itch.io/free-pixel-wizard-tower-furniture-icons-32x32',
            'reodesuxz','Custom: use in games permitted; no standalone redistribution',member)

# Column/row positions in the transparent original. Framed and shadowed versions are excluded.
utility = [
    ('campfire',2,4,'Костёр','Small burning campfire with wood beneath the flame.','prop'),
    ('tent',3,4,'Палатка','Complete tan triangular camping tent, seen from the side.','prop'),
    ('spyglass',7,10,'Подзорная труба','Complete brass handheld telescope with a blue end lens.','item'),
    ('magnifying-glass',8,10,'Увеличительное стекло','Single circular blue magnifying lens with a short handle.','item'),
    ('hunting-trap',14,10,'Охотничий капкан','Open circular metal trap with two toothed jaws.','item'),
    ('hourglass',15,10,'Песочные часы','Complete wooden hourglass with visible sand and blue glass.','item'),
    ('mirror',1,11,'Зеркало','Complete handheld mirror in a gold frame.','item'),
    ('manacles',2,11,'Кандалы','Two linked metal wrist cuffs, viewed as a complete object.','item'),
    ('lyre',3,11,'Лира','Complete golden lyre with parallel strings.','item'),
    ('viol',4,11,'Виола','Complete bowed brown string instrument.','item'),
    ('ocarina',5,11,'Окарина','Small blue wind instrument with visible finger holes.','item'),
    ('flute',6,11,'Флейта','Long light wooden flute with finger holes.','item'),
    ('pan-flute',7,11,'Свирель','Row of descending golden pipes bound together.','item'),
    ('horn',8,11,'Рог','Curved golden hunting horn with a dark strap.','item'),
    ('brass-key',9,11,'Латунный ключ','Complete brown-brass key with a circular bow and a long toothed stem.','item'),
    ('mortar',12,11,'Ступка','Pale mortar bowl with a separate pestle.','item'),
    ('herbs',13,11,'Травы','Short stem with narrow green leaves.','item'),
    ('dice',13,13,'Игральные кости','A pair of white dice with black pips.','item'),
    ('sack-powder',1,20,'Порошок','Small brown pile of loose powder.','item'),
    ('cauldron',9,19,'Котёл','Complete dark metal cauldron with a wide open mouth.','item'),
    ('cotton',5,17,'Хлопок','Loose white cotton cluster.','item'),
]
with zipfile.ZipFile(WORK/'shikashi/pack-1.zip') as archive:
    pack='shikashi-utility-32';folder=LIB/'Sources'/pack;folder.mkdir(parents=True,exist_ok=True)
    member=next(n for n in archive.namelist() if n.endswith('#1 - Transparent Icons.png'))
    sheet=Image.open(io.BytesIO(archive.read(member))).convert('RGBA')
    text=archive.read(next(n for n in archive.namelist() if n.endswith('.txt')))
    (folder/'LICENSE.txt').write_bytes(text)
    (folder/'ATTRIBUTION.txt').write_text("Shikashi / shikashipx: https://shikashipx.itch.io/shikashis-fantasy-icons-pack\nOriginal pack statement: based in part on game-icons.net, CC BY 3.0.\nCurrent author page metadata: CC BY 4.0.\nSelected unchanged 32x32 cells cropped from the original transparent sheet.\n",encoding='utf-8')
    for key,col,row,ru,description,usage in utility:
        im=sheet.crop((col*32,row*32,col*32+32,row*32+32));buffer=io.BytesIO();im.save(buffer,format='PNG')
        add(pack,key,buffer.getvalue(),key.replace('-',' '),ru,description,usage,
            'https://shikashipx.itch.io/shikashis-fantasy-icons-pack','Shikashi; game-icons.net contributors',
            'CC-BY-3.0 / author page CC-BY-4.0',member+f'#{col},{row},32,32')

with zipfile.ZipFile(WORK/'idylwild.zip') as archive:
    pack='idylwild-inventory-32';folder=LIB/'Sources'/pack;folder.mkdir(parents=True,exist_ok=True)
    (folder/'LICENSE.txt').write_text("Idylwild's Inventory by Idylwild\nhttps://opengameart.org/content/idylwilds-inventory\nCC0 1.0. Attribution appreciated but not required.\nOriginal native 32x32 files, unchanged.\n",encoding='utf-8')
    for key,ru,description in [('parchment','Пергамент','Single light parchment sheet with curled edges.'),('cloth','Ткань','Folded cloth fabric.'),('urn','Урна','Complete rounded urn with a narrow upper opening.'),('herb','Трава','Small leafy herb stem.')]:
        add(pack,key,archive.read(key+'.png'),key,ru,description,'item',
            'https://opengameart.org/content/idylwilds-inventory','Idylwild','CC0-1.0',key+'.png')

supplements = [
    ('harbor','harbor-props-32','https://reodesuxz.itch.io/free-pixel-lighthouse-harbor-icons-32x32', {
        'rowboat':('Гребная лодка','Complete wooden rowing boat in side view with an oar.','prop'),
        'canoe':('Каноэ','Complete narrow wooden canoe in side view.','prop'),
        'sailboat_red':('Парусная лодка','Complete boat with a mast and red and white sail.','prop'),
        'ship_anchor':('Якорь','Complete iron anchor with a ring and two curved flukes.','item'),
        'oar':('Весло','Complete wooden oar with a broad blade.','item'),
    }),
    ('market','village-market-32','https://reodesuxz.itch.io/free-pixel-medieval-village-market-icons-32x32', {
        'red_awning_stall':('Лавка с красным навесом','Complete wooden market stall with a red striped awning.','prop'),
        'blue_awning_stall':('Лавка с синим навесом','Complete wooden market stall with a blue striped awning.','prop'),
        'green_awning_stall':('Лавка с зелёным навесом','Complete wooden market stall with a green striped awning.','prop'),
        'handcart':('Ручная телега','Complete wooden two-wheeled handcart with an upright handle.','prop'),
        'scales':('Торговые весы','A complete balance with two hanging pans and a central upright.','item'),
        'well':('Колодец','Complete water well with a roof and a stone basin.','prop'),
        'padlock':('Замок','Complete grey padlock with a dark keyhole.','item'),
        'horse_saddle':('Седло','Complete brown leather riding saddle.','item'),
        'cloth_roll_red':('Красный рулон ткани','Complete roll of red fabric with a visible circular end.','item'),
        'cloth_roll_blue':('Синий рулон ткани','Complete roll of blue fabric with a visible circular end.','item'),
        'wax_seal_letter':('Письмо с печатью','Folded light envelope with a red wax seal.','item'),
        'gold_coins':('Золотые монеты','Small pile of complete gold coins.','item'),
    }),
    ('tools-hardware','workshop-tools-32','https://reodesuxz.itch.io/free-pixel-tools-hardware-icons-32x32', {
        'horseshoe':('Подкова','Complete U-shaped metal horseshoe with visible nail holes.','item'),
        'padlock':('Навесной замок','Complete gold padlock with an arched shackle.','item'),
        'chain':('Цепь','Three connected oval metal chain links.','item'),
        'sledgehammer':('Кувалда','A broad dark metal hammer head on a long wooden handle.','item'),
        'anvil':('Наковальня','Complete iron anvil with a horn and a supporting base.','prop'),
        'file_rasp':('Напильник','Long flat metal file with a brown handle.','item'),
        'chisel':('Зубило','A straight chisel with a broad light metal edge and wooden handle.','item'),
        'mallet':('Киянка','Complete wooden mallet with a broad striking head.','item'),
    }),
    ('fishing','fishing-village-32','https://reodesuxz.itch.io/free-pixel-fishing-village-props-32x32', {}),
]
for source_folder,pack,url,wanted in supplements:
    archive_path=WORK/source_folder/'pack-1.zip'
    if not archive_path.exists():continue
    with zipfile.ZipFile(archive_path) as archive:
        folder=LIB/'Sources'/pack;folder.mkdir(parents=True,exist_ok=True)
        (folder/'LICENSE.txt').write_bytes(archive.read('LICENSE.txt'))
        for key,(ru,description,usage) in wanted.items():
            matches=[name for name in archive.namelist() if '/32x32/' in name and name.endswith('/'+key+'.png')]
            if len(matches)!=1:raise ValueError('Missing original 32px source: '+key)
            add(pack,key,archive.read(matches[0]),key.replace('_',' '),ru,description,usage,url,'reodesuxz',
                'Custom: use in games permitted; no standalone redistribution',matches[0])

manifest.write_text(json.dumps(entries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('External art total:',len(entries),'; original furniture and selected camping, trade and utility icons.')
