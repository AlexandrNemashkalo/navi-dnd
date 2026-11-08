"""Extract and annotate the user's existing transparent portraits, without generation.

Requires Pillow. Original archive and files are preserved. The supplied pack's
License.txt is retained verbatim; no license grant is inferred from pixels.
"""
import hashlib
import io
import json
import zipfile
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/art-catalog-review/craftpix-portraits'
OUT.mkdir(parents=True, exist_ok=True)
archive = ROOT / 'craftpix-net-415209-free-goblins-and-orcs-pixel-art-icon-pack.zip'
# Observed from contact sheet, not inferred gender/species/class/personality.
observations = [
 ('dark_green', 'Tall dark crest, pointed ears and narrow reddish eyes.'),
 ('olive', 'Broad bald face, pronounced brow and exposed square teeth.'),
 ('pale_cyan', 'Pale blue face, small dark head covering, pale braided beard and light clothing.'),
 ('green', 'Long orange hair, pointed ears and exposed lower teeth.'),
 ('light_green', 'Blue-gray cap with a pale feather, pointed ears and a dark eye area.'),
 ('green', 'Brown head wrap, dark patch over one eye and exposed lower teeth.'),
 ('green', 'High rounded forehead, long pointed ears and narrow red eyes.'),
 ('green', 'Dark blue head covering, pale angular shape behind the head and an orange collar.'),
 ('olive', 'Dark swept-back hair, prominent pointed ears and pale tusks.'),
 ('green', 'Reddish head covering, pointed ears and a pale upright blade beside the head.'),
 ('dark_green', 'Side-facing profile, swept-back dark hair and a reddish eye.'),
 ('olive', 'Short brown crest, wide pointed ears and purple clothing.'),
 ('dark_green', 'Brown swept-up hair, large pale eyes and prominent lower teeth.'),
 ('olive', 'Small gold head ornament, dark eye areas and a long narrow face.'),
 ('gray_green', 'Pale ridged head covering, dark face and pale shoulder ornaments.'),
 ('pale_green', 'Dark swept-back hair, pale face and red-and-gold clothing.'),
 ('pale_green', 'Short swept-back gold hair and orange clothing.'),
 ('green', 'Brown hair, red forehead markings and a pale curved blade beside the head.'),
 ('dark_olive', 'Dark hood, narrow visible eyes and pale exposed teeth.'),
 ('teal', 'Bald teal face, dark eye sockets, pointed ears and a gold-colored collar.'),
 ('olive', 'Narrow dark crest, wide eyes, open mouth and a skull-topped staff beside the head.'),
 ('olive', 'Dark red wrapped head cloth with a pale knot, narrow eye and an angular profile.'),
 ('pale_cyan', 'Narrow dark central crest, long pale face and pointed ears.'),
 ('olive', 'Bald side-facing head, long pointed ear and dark red clothing.'),
 ('pale_cyan', 'Dark red swept-back hair, grinning teeth and dark clothing.'),
 ('teal', 'Red headscarf, pointed ears and a pale curved blade beside the shoulder.'),
 ('olive', 'Brown cap with a pink band, pale cheek areas and a dark high collar.'),
 ('olive', 'Dark brown hair, prominent lower tusks and orange shoulder armor.'),
 ('pale_teal', 'Teal cap with a pale spiral, angular pale face and gray shoulder pieces.'),
 ('pale_green', 'Pale mask-like face, red markings and a feathered head ornament.'),
 ('green', 'Tan hood surrounding a dark green face with small pale eyes.'),
 ('light_green', 'Red upright crest, light green profile and angular dark shoulder pieces.'),
 ('teal', 'Dark blue hood surrounding a teal face with pale tusks and a brown beard.'),
 ('olive', 'Dark helmet with a purple side ornament and pointed ears.'),
 ('light_green', 'Orange-brown hair drawn back, wide pointed ears and pale lower teeth.'),
 ('olive', 'Shaggy dark brown hair, pale lower teeth and brown shoulder clothing.'),
 ('dark_green', 'Side-facing dark green head with a reddish eye and gold-colored earrings.'),
 ('olive', 'Long narrow profile, dark scalp marking, pointed ear and purple shoulder clothing.'),
 ('pale', 'Pale face, two curved white horns, red cheek markings and brown hair.'),
 ('green', 'Brown tied-up hair, narrow yellowish eyes and dark red clothing.'),
 ('green', 'Dark round eye coverings, brown hair and yellow-orange clothing.'),
 ('light_green', 'Red headband with a trailing pale feather, pointed ears and dark clothing.'),
 ('dark_green', 'Orange-brown tied-up hair, reddish eye details and red clothing.'),
 ('gray_green', 'Spiky orange hair, purple eyes, pale teal face and visible lower tusks.'),
 ('olive', 'Long reddish-brown hair covering part of the face and a dark collar.'),
 ('teal', 'Dark blue swept-back hair, braided lock, pointed ear and a pale blade behind the head.'),
 ('green', 'Long orange-brown hair, gold-colored earrings and visible pale teeth.'),
 ('teal', 'Rounded curled teal head ornament or hair, pointed ears and a gold-colored earring.'),
 ('olive', 'Bald head with a red circular forehead marking, pointed ears and exposed teeth.'),
 ('dark_green', 'Bald rounded head, small pale eyes and a pale diagonal blade beside the shoulder.'),
]
assert len(observations) == 50
with zipfile.ZipFile(archive) as z:
    members = [n for n in z.namelist() if '/Transperent/' in n and n.lower().endswith('.png')]
    members.sort(key=lambda n: int(Path(n).stem.removeprefix('Icon')))
    assert len(members) == 50, 'Unexpected archive revision: review the source before import.'
    (OUT/'License.txt').write_bytes(z.read('License.txt'))
    records=[]
    board=Image.new('RGB',(800,520),'#454b53')
    draw=ImageDraw.Draw(board)
    for i,(member,(skin,observation)) in enumerate(zip(members,observations,strict=True),1):
        raw=z.read(member)
        with Image.open(io.BytesIO(raw)) as original:
            im=original.convert('RGBA')
        assert im.size==(32,32), member
        assert im.getchannel('A').getextrema()==(0,255), member
        filename=f'portrait-{i:02d}.png'
        im.save(OUT/filename)
        enlarged=im.resize((64,64),Image.Resampling.NEAREST)
        board.paste(enlarged,(((i-1)%10)*80+8,((i-1)//10)*104+4),enlarged)
        draw.text((((i-1)%10)*80+5,((i-1)//10)*104+74),str(i),fill='white')
        records.append(dict(id=f'craftpix:goblin-orc:{i:02d}',pack='craftpix-goblin-orc',
            path=filename,sourceMember=member,width=32,height=32,alphaRange=[0,255],
            sha256=hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),
            sourceSha256=hashlib.sha256(raw).hexdigest(),
            descriptionEn=observation,traits=dict(skinColor=skin),
            gender='unknown',species='unknown',ageBand='unknown',
            usages=['portrait'],framing='head_shoulders',
            identityFamily=f'craftpix:goblin-orc:{i:02d}',
            technicalStatus='passed',semanticStatus='visually_described',
            artStatus='needs_in_game_review',reviewMethod='detail_sheet_128px',
            provenance=dict(archive=archive.name,member=member,licenseRef='License.txt')))
    (OUT/'manifest.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
    board.save(OUT.parent/'craftpix-contact-sheet.png')
print(f'Validated and described {len(records)} transparent 32x32 portraits: {OUT}')
