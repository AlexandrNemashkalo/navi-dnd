"""Observed captions from numbered 96px contact sheets, bound to PNG hashes.

No game mechanics, gender or ancestry is inferred by this description pass.
User reviews remain a separate overlay. Captions are English canonical data.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LIBRARY = ROOT / 'src/ArtLibrary'
CAPTIONS = {}


def add(pack, text, prefix=''):
    rows = [r.strip() for r in text.split('|')]
    if len(rows) != 48 or any(not r for r in rows):
        raise ValueError(f'{pack}: expected 48 captions, got {len(rows)}')
    for number, caption in enumerate(rows, 1):
        CAPTIONS[f'{pack}:icon-{number:03d}'] = prefix + caption + '.'


add('prepared-portraits-native-32-elves', '''long gold hair and green headband|blond hair, pink flowers and green eyes|swept blond hair and dark collar|swept silver hair, turquoise forehead jewel|short grey hair and gold circlet|blue winged crown and gold hair|silver helmet with green crest|closed angular olive helmet
|swept grey hair and gold trim|gold hair with blue flowers|loose orange hair and green collar|pink hair and gold flower circlet|long gold hair and blue forehead jewel|orange hair and green leaf headpiece|silver swept hair and green face markings|gold hair and blue floral circlet
|silver winged full helmet|high gold horned helmet|swept gold hair and blue eye mask|auburn hair and gold flower|green hood and dark forehead band|short blond hair and blue feathers|short swept gold hair, bare neck|side-swept silver hair and pink band
|long blond hair, brown pointed cap|silver hair and pale high collar|gold and green crown, long pointed ears|auburn hair and purple headband|braided blond hair and green eye patch|braided auburn hair and green eye patch|green leaf-shaped face-covering helmet|short gold side-swept hair
|silver full-face mask with green eye lenses|short swept orange hair|tied grey hair and pointed ear|gold hair and blue-red headband|short blond hair and silver headband|auburn hair with blue-green flower circlet|brown hair and small green pipe|round blond hairstyle and orange collar
|gold hair and blue forehead band|short grey hair with small side ornaments|blond bob and green eye mask|silver hair with blue wrap|long blond braids and green leaves|orange swept hair and green eyes|blue head wrap and small orange cap|short wavy gold hair and red collar''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-humans', '''short dark hair, narrow face|black hair beneath tan bonnet|bald head, brown moustache|blond braid and olive bonnet|bald crown and large grey moustache|brown hood framing smiling face|dark skin and white wrapped cap|blond bob and orange pointed hat
|red bob and brown moustache|dark skin, short brown bob|wide straw hat|long curly orange hair, green collar|black cap and pale collar|dark beard and short brown hair|dark red cap and blue eye marking|auburn hair and blue forehead band
|grey hair, long grey moustache|pale blue hood and red lips|dark skin, grey hair and moustache|short brown hair and small goatee|red swept hair and black collar|white beard and dark cloak|black hair and wide red pointed hat|blond hair and purple pointed hat
|broad dark hat and blond hair|closed blue steel helmet|dark feathered hat and brown moustache|short brown hair and green scarf|purple feathered cap and narrow beard|bald head and blue scarf|gold swept bob and orange collar|brown bob and green forehead ornament
|red hair, silver crown and green collar|brown bob and turquoise tiara|gold crown and long black beard|red hair and gold circlet|gold hair, short beard and grey collar|brown bob and green hood|black hair, moustache and pointed beard|red swept hair and dark green collar
|bald crown, spectacles and white beard|wide blue feathered hat|brown cap and light moustache|grey hood and stern face|orange beret with green feather|dark skin and broad brown hat|orange brimmed hat and grey hood|blond bob, dark lips and green collar''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-dark-elves', '''swept silver hair, grey face and red eyes|long silver hair and yellow eyes|pale hair and skull forehead ornament|silver hair with red-black headpiece|grey hair, pointed ear and gold earring|pale hood-like hair and purple eyes|long pale hair and high blue headpiece|black hood with gold forehead mask
|braided pale hair and gold trim|pale bob with red headband|silver hair and blue forehead jewel|silver hair and red-purple crown|long pale hair, pink eyes and turquoise collar|red hood and gold hair|silver hair and purple swept collar|pale hair and gold-red angular crown
|purple hood and dark forehead jewel|green hood and gold trim|swept silver hair, grey skin and gold collar|silver bob, crescent forehead ornament, pink lips|dark blue hood and orange eye|tousled pale hair, gold earring|olive pointed hood and dark face|white head wrap and green forehead gem
|silver hair, blue circlet and red eyes|gold horned face helmet|short pale hair, orange eyes and high collar|long white hair with small red horns|red-gold eye mask and pale hair|red-gold mask and swept pale hair|dark hood and skull forehead ornament|pale high swept hair and blue ear ornaments
|high black spiked crown and blue-red brow|pale swept hair and blue eye patch|grey hair, dark face and red eyes|brown cap and red forehead band|white hair and red-gold headband|pale hair and turquoise curled forehead ornament|short swept pale hair, dark face and orange eyes|black horned crown with skull ornament
|gold-orange high headpiece and long pale hair|pale hair, dark face and red eyes|pale bob and red-gold forehead beads|dark horned helmet and pale side hair|short white bob and red eyes|pale hair, dark face and purple eyes|skull-shaped shoulder/head armour with red trim|black-gold spiked crown and grey face''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-undead', '''grinning skull beneath dark purple hood|pale long-haired face with leafy crown|green bald stitched head|blue-grey face beneath brown cap|skull with gold-red crown and red hood|purple skull-like face beneath wrapped cap|wooden face with branching twigs|grinning skull with broad grey hat
|pale pointed-ear face and swept pale hair|black hair, exposed teeth and green-grey skin|purple bald pointed-ear face with sharp teeth|high black hair and cyan eyes|red hood, dark face and pale glowing eyes|green stitched head with wrapped scalp|pale face, dark eye sockets and gold crown|grinning dark face and blue side spikes
|skull in blue cap and scarf|cloth-wrapped face with single bright lens|grinning skull in broad brown hat|long horned animal skull|yellow-eyed skull in blue hood|red-eyed skull in green hood|pale swept hair and angular grey face|branching wooden head with orange eyes
|dark long hair, purple skeletal face|wrapped scalp and grey face|golden wrapped face with circular forehead object|green skull-like face in dark red hood|skull beneath horned cap|orange carved pumpkin head and green pointed hat|pale green face, orange hair and leafy cap|purple skeletal face with second skull behind shoulder
|horned steel helmet around dark skull face|green stitched bald head|purple face and wide brown hat|swept white hair, red eyes and white beard|pale green face and red hood|dark hood, cloth face wrap and one cyan eye|grinning pale skull and raised curled ornament|cloth-wrapped purple face and white turban
|long dark hair, green face and blue scarf|grey skull, orange eyes and dark red collar|orange-red fiery skull|cyan-eyed skull with red cloth over mouth|tilted grey wrapped head and floating purple-gold ring|deep purple hood surrounding small skull|pale skull, purple eyes and green scarf|pale angular face with black hair and deep eye sockets''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-demons', '''red face, curled dark horns and blue hair|pink face, small horns and white side hair|orange face and swept flame-like hair|white face, small brown horns and silver crest|grey face, red hair and dark horns|red face and long narrow gold horns|blue-grey grinning face and curled horns|purple face and large brown curled horns
|red face, pale crest and branching horns|blue-grey face and red hair|red face, swept gold hair and dark horns|blue face and long dark blue hair|purple face and large dark winged headpiece|dark blue face and white swept hair|grey face and tall red spiky hair|orange hair around dark red-blue face
|gold hair and black-red horned forehead mask|grey face, purple eye and large curled horn|purple face, red crest and pointed horns|orange-red face framed by dark angular collar|dark grey face, red eyes and small horns|orange face and green horned hood|red bat-like face with long narrow horns|red-white face and curved pale horns
|purple face beneath ornate gold beaded headpiece|pale face framed by orange flame-like hair|dark red grinning face beneath blue horned hood|orange face, brown ridged crown and blue side gems|red tentacled face and conical head|red-blue angular full-face helmet with gold spikes|blue-grey face, short pale horn and high collar|smiling purple face with blue horned crown
|red face framed by pale turquoise hood|split red-grey face beneath small blue horns|purple face crossed by pale diagonal horn|grey smiling face, gold band and heavy curled horns|purple face and two pale curved horns|orange face beneath blue horned helmet|purple face, swept gold hair and brown horns|grey-red masked face and purple swept crest
|purple face and swept brown-gold hair|red face and broad brown horned crown|pale skull-like face, red hair and gold circlet|blue full-face mask with side spikes and gold gem|red tentacled head with pale curled side horns|pink-green snarling face with four horn tips|orange face and tall brown skull headpiece|dark blue angular horned helmet with red mouth''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-pirates', '''grinning skull beneath brown tricorn|red bandanna and short brown beard|dark skin, brown wrap and small pipe|blue-grey face and red cap|blond hair, black eye patch and brown hat|dark skin and white-black tricorn|green face, red markings and gold earrings|blue skin, swept black hair and eye patch
|dark skin and black tricorn with skull|blue pointed-ear face and tan wrap|orange bob and tired round face|pale blue face, black swept hair and pointed ears|red beard, grey hat and bared teeth|pale face, orange hair and brown-red hood|dark skin, shaved sides and gold ear rings|blue-green face, pointed ears and dark bob
|brown hat and curved blade held across mouth|bald crown, black eye patch and long orange beard|grey pointed-ear face with red eye|green grinning tusked face and short dark hair|green face, long blond hair and dark eyes|grey bald face with stitched scalp|pale blue face and braided blue hair|blue face, grey swept hair and red hood
|green pointed-ear face, eye patch and dark tricorn|pale blue face and white hair with brown band|smiling face beneath broad red-brown hat|pale bald skull-like face|blue skeletal face and dark swept hair|green face, black hair and red bandanna|dark skin, brown hair and purple collar|blue bald face with red diagonal markings
|orange hair, dark eye sockets and gold teeth|pale skull with red eyes and purple side ornament|grey grinning face and red cap|bald crown and white side beard|pale blue face beneath blue hood|grey face and swept brown hair|dark skin, orange bob and beard|blond bob and black face markings
|grey beard and broad red-brown hat|blue-green long-haired face and purple cap|red hair, black eye patch and broad grin|swept brown hair and broad painted grin|brown moustache, beard and red-gold collar|brown hair and red forehead band|pale green face, pointed ears and dark hood|green bald face and purple eyes''', 'Head-and-shoulders portrait: ')

add('prepared-portraits-native-32-dwarves', '''orange hair, pointed ear and wide smile|dark skin and long black hair|gold beard and blue axe behind shoulder|red cap and broad dark red beard|red hair and beard, raised fist|dark skin, black beard and dark swept hair|orange hair and beard, upright spear|dark skin and purple cap
|steel helmet and long brown beard|brown bob and pale green collar|orange moustache, beard and raised foaming mug|brown beard and high tied hair|dark skin, dark beard and blue axe|dark skin, red hood and blond braids|gold hair and curled gold moustache|dark skin and red hood
|gold hair, beard and hood-like frame|short dark hair and pointed sideburns|dark skin and red braids|bald crown, round spectacles and pale beard|dark skin, orange braids and blue forehead gem|blue-gold face-covering helmet|red beard and axe behind shoulder|brown hair in high bun and orange collar
|blond hair, dark skin and pale collar|bald crown, blue eye patches and white beard|black hair, moustache and thin beard|brown bob, dark skin and red side ornaments|red hair and high red beard|bald crown, swept pale brows and long white beard|red hood and green-white collar|large gold beard and hair with raised hand
|steel helmet and orange moustache|dark skin, red hair and gold earring|orange spiked hair and long forked beard|dark skin, brown braids and purple scarf|gold beard and raised mug|dark skin and large brown beard|black beard, dark hood and upright blade|horned steel helmet and broad blond beard
|brown hair, beard and dark green collar|steel-gold helmet, blond beard and upright blade|gold-edged helmet and long brown beard|dark skin, black hair and pale blue collar|orange braids and pale green eyes|dark skin, gold braids and broad smile|red hair, moustache and broad red beard|dark beard and two raised brown side bunches''', 'Head-and-shoulders portrait: ')


add('prepared-items-native-32-01-loot', '''grey webbed fin|gold spiral disc|green curled tongue|orange curled scale|blue horn|pink webbed wing|green striped curl|long beaked skull
|blue pointed pendant|green oval scale|purple curled tail|pale layered shell|green eyeball|yellow eye in dark socket|pair of yellow eyeballs|green clawed foot
|pale hooked fang|green spotted egg|gold broken shell|round red liquid flask|narrow purple vial|brown segmented shell|blue cut horn|brown layered scales
|pale flat shell|large pale pink eye|pink open claw|cyan curled horn|orange segmented tail|orange-black membrane wing|blue three-pronged fin|pale skull with purple eye
|turquoise trailing tentacles|brown tapered shell|lavender forked fin|pale triangular layered plate|green coiled tail|yellow eye in brown socket|pair of blue eyeballs|orange clawed foot
|metallic pointed sting|purple ridged cocoon|yellow eggs in nest|blue ladle-shaped vessel|bowl of green contents|pale brown oval shell|red hooked fragment|orange layered shell''', 'Isolated material icon: ')
add('prepared-items-native-32-02-crafting', '''gold ingot|gold chain strip|pale green folded plates|heap of gold granules|dark hammer|gold handled tool|brown stretched hide|green fabric roll
|pale parchment sheet|bound pale rope|brown folded hide|gold patterned plate|stack of silver plates|bundle of silver rods|orange ingot|gold ring with side stem
|three silver nails|pile of dark rivets|orange curled strip|gold rectangular plate|gold mesh roll|brown looped cord|pale flat plate|orange feather-like shard
|silver pickaxe|pale blue ingot|red rod|blue-grey ore chunk|yellow handled pincers|pale triangular plate|pale stretched hide|brown stretched leather
|orange curved trough|brown coiled strap|gold oval plate|small knife|orange folded leather|dark segmented bar|silver feather-like plate|olive looped strap
|silver bolts and nuts|dark metal pieces|brown curled shavings|stack of pale planks|orange braided material|two gold caps|green rough crystal|curved cutting tool''', 'Isolated crafting icon: ')
add('prepared-items-native-32-03-minerals', '''orange dark ore|red streaked grey ore|gold ore pieces|red-blue ore|purple crystal slab|orange faceted stone|green crystal cluster|gold-brown ore
|green veins in brown rock|blue round ore|tall cyan cluster|blue faceted cluster|red crystal on sand, flame mark|gold shard on sand, lightning mark|purple crystal on sand, wand mark|blue shard on sand, droplet mark
|dark purple ore on sand, grey marker|green cluster on sand, leaf mark|orange crystal, flame mark|gold crystal, lightning mark|purple crystal, wand mark|cyan crystal, droplet mark|brown crystal cluster|green crystal, leaf mark
|orange veins in grey rock|purple veins in brown rock|blue-gold layered ore|green veins in grey rock|brown-orange rough rock|purple-grey round ore|purple striped ore|purple diamond in brown rock
|green angular ore|red crystals in grey rock|green-brown ore chunks|red faceted round stone|orange crystal, flame mark|gold needles, lightning mark|purple angular cluster|blue elongated crystal
|dark purple ore, grey marker|green ore, leaf mark|orange ore, flame mark|gold needles, lightning mark|purple stacked crystal, wand mark|blue crystal chunks, droplet mark|brown ore, grey marker|green crystal slab, leaf mark''', 'Isolated mineral icon: ')
add('prepared-items-native-32-04-crafting-materials', '''orange folded leather|turquoise folded cloth|spool of brown thread and needle|red liquid in corked flask|three dark nails|two gold coin-like discs|red-striped rough ore|purple dust heap
|green-white ore|curved metal chain|red powder heap|beige round pearl-like ball|spool of gold thread|blue stitched cloth patch|purple chain loops|grey powder heap
|three pale sewing needles|pale blue round stone|purple rectangular gem|green faceted gem|orange triangular gem|gold teardrop nugget|small blade with gold handle|small mallet
|brown folded leather|blue anvil|gold-red pointed metal pieces|purple liquid in flask|two dark rods|green feather|brown-gold ore|blue-brown ore
|pink branching tube|pink looped ring|green liquid in flask|yellow powder heap|orange curved fragment|purple thread spool|blue yarn ball with needle|green-yellow powder heap
|green thread on spool|four pale hooked pieces|orange star gem|blue round faceted gem|dark green sphere|pink faceted shard|small hammer|dark hanging wire frame''', 'Isolated crafting icon: ')
add('prepared-items-native-32-05-gems', '''pale rectangular gem in dark frame|pink diamond in dark frame|green crystal in gold frame|blue round gem in dark frame|orange crystal in brown frame|cyan round gem with curved frame|pale rectangular framed gem|pink diamond framed gem
|green narrow framed gem|blue round framed gem|orange narrow framed gem|cyan round framed gem|red angular crystal and flame mark|gold shard and lightning mark|purple star gem and wand mark|blue branching gem and droplet mark
|dark rock cluster and grey marker|green layered gem and leaf mark|red angular gem and flame mark|gold spiked gem and lightning mark|purple four-point gem|blue angular gem|dark grey crystal cluster|green faceted gem and leaf mark
|pale octagonal gem|turquoise diamond|orange elongated diamond|purple gems in dark round setting|pale square gem with red corners|pink-blue round gem|blue rectangular gem in gold frame|turquoise square gem in angular frame
|orange diamond in grid frame|purple round gem in dark frame|pale red-spotted square gem|pink-blue swirling round gem|orange crystal and flame mark|yellow zigzag crystal|purple star-like crystal|blue curved crystal
|grey octagonal stone|green layered crystal|orange jagged gem|gold angular gem|purple angular gem|cyan segmented crystal|grey pointed mineral|green layered mineral''', 'Isolated gemstone icon: ')
add('prepared-items-native-32-06-herbs', '''pink flowers and leaves|blue flower spike|pale yellow flower bud|red flower stalk|pink star flower|pink hanging flowers|pale branching root|orange branching root
|pale cut root fan|brown tangled roots|pale narrow root|round yellow root|orange flowers and leaves|green-yellow bud|blue drooping flower|red blossoms
|orange fan leaves|purple-blue flower cluster|orange tapered root|bare branching twig|brown toothed plant mouth|pale hollow root mass|orange root mass|pale radial roots
|purple cup flowers|gold grain-like stalk|red layered flower|blue bell stalk|orange cup on leaves|pink flower stalk|brown twig with roots|pale layered bulb
|pale orange forked root|orange root with side shoots|gold bulb with roots|brown hairy root|pink round flowers|orange spiked flower|blue hanging flower cluster|orange berries on twig
|orange star flower|blue round flower|purple bulb in brown husk|pale bulb with long green stem|gold root with leafy top|brown seedpod in pointed husk|yellow segmented pod|orange toothed plant mouth''', 'Isolated plant icon: ')
add('prepared-items-native-32-07-barbarian', '''raised fist, orange aura|dark horned helmet, yellow rays|double axe, orange aura|studded club, red streaks|crossed axe and blade, pink aura|upright double axe, red rays|steel boot, orange motion|pair of steel boots, green streaks
|helmeted figure in cyan motion|hand and gauntlet, green rays|round wooden shield and steel fist|fist striking with small debris|raised brown boot, pink aura|brown boot beside shield|green clenched fist and debris|round wooden shield, blue circle
|small axe on wooden handle, green motion|bare torso with raised hand|fist beside horned helmet|axe breaking through pink shape|open brown-red collar|steel pick head, blue streaks|brown boot thrust forward|bare torso with raised arms, yellow rays
|double axe in pink rays|horned helmet above crossed blades|hammer and striking fist|hand swinging steel weapon, blue motion|two axe heads, cyan arcs|axe behind pink diagonal slash|dark fragments and metal piece, green aura|upright axe above peaked base
|studded weapon and red debris|axe cutting through blue arc|hand gripping spiked club|bare torso in horned helmet holding axes|helmeted figure with round shield|double axe in orange rays|paired hammers in red motion|brown crossed straps
|raised boot, cyan motion|large steel axe swung forward|bare torso before wooden barrier|armoured fist in red slash|double axe in purple rays|steel boot beside brown boot|armoured boot and yellow streaks|hammer swung by gripping hand''', 'Framed action illustration: ')
add('prepared-items-native-32-08-alchemy', '''orange curled leaf|green grain stalk|orange serrated leaf|purple-gold curved pod|red-orange seedpods|green needle sprig|gold oval seed|pair of brown seedpods
|orange seed with green leaf|split brown seed shells|red powder on paper|curled blue segmented root|gold-green narrow leaves|three green leaves|yellow-green oval leaf|green clover-like cluster
|orange curled tendrils|two spotted green leaves|three red hanging buds|pale bulb with cloves|long brown feather-like leaf|three pale blue berry heads|red layered fungus|blue-green flower bud
|purple leaf with gold rim|gold curled root|brown-red twig berries|orange-green serrated leaf|pale green pointed leaves|narrow green stem|green-orange angular pod|purple berry cluster
|green curved pod with dark seeds|split pale seed shells|green powder on paper|green teardrop leaf|green-gold three-leaf sprig|green narrow bud|green spiked fruit|brown leafless branch
|two red leaves|pink bud on green stem|orange round seed|pair of brown shelf mushrooms|curled pale green shoots|orange tapered root with green leaves|pale blue buds|yellow hollow root mass''', 'Isolated botanical ingredient icon: ')


add('prepared-items-native-32-09-thief', '''raised wrapped fist|black hooded face|small straight knife|hand holding curved knife|curved blade in blue spiral|upright knife in red flames|large curved dagger|green straight blade
|side-facing hooded face|empty hand over broken blade|curved knife and metal fragments|knife strike with red droplets|raised brown boot|hooded figure extending blade|three hanging coin pouches|two gold eyes in darkness
|curved knife beneath moon|hooded figure with raised arm|shield and flying knives|three thrown metal discs|gold face mask in blue shield|knife with blue motion trail|boot thrust forward|brown boot with dark emblem
|sweeping curved blade|hooded face and purple aura|hand gripping small knife|hand throwing knives|fist hitting brown surface|curved red-edged knife|hand tossing coin|gold key with dotted trail
|curved knife and red drops|extended hand and three blades|hand gripping crossed knives|gold flask and pale vial|hooded figure leaning forward|curved dagger and dark orb|short hooked dagger|crossed brown boots
|boot with purple glow|large curved dagger in blue rays|hooded figure in moonlight|bloodied blade in green motion|pointed metal dart|raised boot in green motion|three-point silver blade|gripping hand and curved blade''', 'Framed action illustration: ')
add('prepared-items-native-32-10-mining', '''pale spiral fossil|small skeleton fragments|orange spiral fossil|curved pale ribs|long pale animal skull|green bone fragment|pale horned skull|brown necklace with teeth
|pale three-root tooth|orange fossil with green inset|green jawbone|pale spotted jaw fragment|pale rooted stump|round orange fossil|pale hollow skull fragment|orange ridged fossil
|pale claw bones|orange fossil with green spot|long pale bone|orange fossil with branching green inset|horned grey skull|curled small skeleton|fan of thin pale bones|orange spiral fossil
|gold pointed pickaxe|steel pickaxe, gold handle|blue pickaxe, brown handle|gold pickaxe, green handle|flat steel pick, gold handle|blue pickaxe, brown handle|dark metal hooked frame|gold shovel
|brown shovel|grey shovel, gold handle|blue shovel, brown handle|wide gold pick|wide grey pick|wide blue pick|yellow wrapped explosive with fuse|round orange bomb with fuse
|dark hammer|blue metal lantern|narrow dark chisel|square yellow lantern|coil of brown rope|orange cloth pouch|brown looped rope|gold linked claw-shaped object''', 'Isolated tool or excavated object: ')
add('prepared-items-native-32-11-pyromancer', '''open hand and orange sparks|pointing hand with yellow rays|three floating gold rings|hand reaching toward yellow flare|flaming gold crown|two hands and red forehead symbol|green droplets amid pink curls|raised hand over cracked ground
|orange branching flame|yellow-orange sweeping flame|three curved fire streaks|red spiral wave|large yellow flame wall|orange crescent flame|red streaking fire projectile|orange fireball
|red humanoid silhouette in flames|flame-shaped gold face|purple hooded figure|red silhouette in yellow aura|gold winged mask|boot trailing fire|hand gripping upright blade|cluster of orange flames
|orange potion and stopper|orange fragments bursting outward|orange silhouette holding flame|staff firing diagonal beam|gloved hand and long staff|orange circular ground ring|three falling orange meteors|gloved fist trailing flames
|small figure atop orange fire|open fiery mouth|upright staff, purple rays|head silhouette wreathed in flame|fire beneath dark rock overhang|boot standing in fire|paired hands around gold flame|hand gripping green-orange glowing object
|red crystal shard|small red curled creature silhouette|orange eye-shaped slash|curved orange trail and dark shard|red flaming faceted crystal|green vines with orange sparks|green orb wreathed in flame|gold bird-like silhouette''', 'Framed effect illustration: ')
add('prepared-items-native-32-12-artifacts', '''red-orange fiery stone|purple sphere with metal points|pale skull fragment|dark green crystal fan|green oval gem in gold rim|purple patterned sphere|green gem wrapped in gold bands|purple faceted pendant
|blue-green spiked crown|pink glowing orb|red heart-shaped stone|blue star-speckled sphere|gold curved charm with pink gems|brown horn with gold rim|dark pointed pyramid|gold face mask
|silver-blue winged mask|tooth necklace with blue beads|orange teardrop pendant|red bead necklace with purple charm|pale horned skull pendant|necklace of pale claws|purple crystal pendant|orange crescent pendant with blue gem
|green gem necklace|orange gem pendant|blue-orange oval pendant|purple diamond pendant|orange-blue diamond pendant|purple orb on gold stand|gold handled green eye-shaped object|green-blue pointed wand
|purple gem in dark upright frame|small skull with flame|gold candle on grey stand|dark cube with coloured gems|green upright curved charm|pale round perforated disc with purple studs|dark green oval-headed wand|pale skull with horned gem crown
|gold goblet with red interior|red spiked wand|gold narrow wand|purple star-tipped wand|brown zigzag wand|blue crystal atop green base|orange-green circular emblem|open gold oval locket''', 'Isolated ornamental object: ')
add('prepared-items-native-32-13-books', '''gold patterned cover|green cover and pale border|orange geometric cover|blue cover and oval emblem|stack of orange volumes|green strapped cover|plain dark upright cover|plain brown upright cover
|green narrow volume|black cover with blue top|red curved cover|blue cover with red binding|open pale pages and red cover|open white pages with coloured markings|open pale pages with coloured markings|open yellow pages
|open brown volume|open cream pages, red symbol|red cover, dark emblem|green cover, gold oval|brown cover, pink gem|orange cover and dark clasp|pale blue cover and dark eye emblem|green cover, orange oval
|blue curved cover|pink striped cover|red upright volume|purple cover with round emblem|green curved volume|orange folded volume|blue-gold cover and diagonal bar|silver-orange cover and crossed bars
|purple cover in metal frame|orange cover with metal clasps|green cover in gold frame|blue cover in gold frame|purple cover and gold clasp|brown cover and red teardrop|pink cover with gold star|dark cover with turquoise gem
|dark blue cover, gold corners|pink cover and dark round centre|orange patterned cover and metal clasps|black cover with red vertical emblem|brown cover with small pale symbol|green cover, gold border and orange inset|grey-green cover with red-orange inset|purple ornate cover and dark oval''', 'Isolated book icon: ')
add('prepared-items-native-32-14-runes', '''yellow hooked mark on grey stone|yellow curved mark on brown stone|yellow angular mark on brown tablet|gold ring mark on orange tablet|gold angular mark on orange tablet|gold spiral mark on red tablet|pale curl on grey diamond stone|pale angular mark on olive stone
|gold horizontal bars on brown stone|gold square spiral on gold stone|gold right-angle mark on gold stone|gold crescent mark on glowing orange stone|pink hook on grey tablet|pink angular mark on brown tablet|pink triangular mark on purple tablet|pink crossed mark on glowing purple tablet
|pink hooked mark on purple tablet|pink zigzag on glowing purple tablet|green curve on grey round stone|green zigzag on green round stone|green horizontal triangle mark|green square hook on green stone|green parallel strokes on dark stone|green triangular strokes on glowing stone
|pale branching mark on grey stone|pale hook on green diamond|pale parallel strokes on cyan diamond|pale upright mark on cyan diamond|pale fork mark on glowing cyan diamond|pale crossed mark on glowing cyan diamond|pink curl on grey round stone|pink hook on brown round stone
|pink parallel strokes on brown stone|pink branching bar on red stone|pink narrow triangle on red stone|pink Y mark on red stone|pale arch on grey stone|pale loops on grey stone|pale fork on grey stone|pale U mark on grey stone
|pale branching mark on icy stone|pale vertical bars on icy stone|turquoise loop on grey stone|turquoise hook on grey stone|cyan angled bar on dark blue stone|cyan branching mark on blue stone|cyan hooked lines on blue stone|cyan arch on glowing blue stone''', 'Inscribed stone icon: ')
add('prepared-items-native-32-15-warlock', '''pale clawed hand and purple sparks|hand and red diagonal slash|hands and floating red heart|gloved hand with flame|hooded figure and green-red tendrils|two hands surrounding pink triangle|pale red-strapped mask|gold loop and pale skull disc
|green hooked tendril|hand holding pale screaming face|upright gold staff and purple aura|pale clawed hand beneath dark blade|red clawed hand|dark blue clawed hand|purple circular wave|green curled cloth-like wave
|purple spiked blast|green hooked head-shaped tendril|red falling projectile|blue falling projectile|scattered green droplets|orange hands rising upward|green orb in purple flame|red horned humanoid silhouette
|upraised hands in blue aura|red skull silhouette|dark hooded face|black-green purple cloak|brown wooden face|raised dark arm and purple claws|red animal-like silhouette|blue gaping mouth
|dark upright monument with green glow|dark silhouette with purple tendrils|hooded figure extending red hand|green eye close-up|green potion vial|black branching lines over pale field|dark star with pale centre|red spiked object and pale marked disc
|dark boot and pale staff|green circular grid|purple spiral orb|green-red hooked tendril|dark hood with pale skull emblem|green splashes and small pale square|green folded shape with orange centre|dark humanoid with purple branching arms''', 'Framed effect illustration: ')
add('prepared-items-native-32-16-archer', '''silver arrowhead in orange streaks|single arrow, green rays|curved flying arrow, blue aura|three silver arrowheads|silver arrowhead in red rays|gold arrowhead in blue rays|orange arrowhead, purple rays|three arrows in green motion
|cluster of silver arrows|spread of flying arrows|three upright arrowheads|pair of diagonal arrows|arrow piercing red round fruit|arrow in target bullseye|arrows beside quiver|hand grasping arrowhead, pink aura
|purple humanoid with outstretched arms|boot beside three arrows|crossbow and yellow rays|crossbow with blue-tipped bolt|crossbow with pale-tipped bolt|wrapped forearm, blue motion|purple humanoid in star aura|hooded side-facing figure and bow
|metal ring trap in grass|scattered pale shaft fragments|small fleeing pale animal silhouette|crossbow and white cross rays|floating green metal fragments|raised hand holding projectiles|green dark bottle|silver arrowhead, green motion
|bloodied silver arrowhead|hand holding flames|boot standing on grassy mound|red round fruit and green splash|hooded figure drawing bow|hooded face with gold aura|wrapped forearm and diagonal pale streak|hand holding arrow and red drops
|raised gloved hand beside arrow|hooded figure holding large arrowhead|fan of arrows|wooden shaft splintering|arrows bursting outward|arrows streaking through purple rays|blue eye close-up|red heart inside circular reticle''', 'Framed projectile/action illustration: ')


add('prepared-items-native-32-17-berries', '''blue berries and leaf|gold berry cluster|red hanging berries|orange berry sprig|pale yellow berries|red berries beneath broad leaf|pink teardrop fruit|orange round fruit
|yellow oval fruit|red strawberry|purple pointed fruit|blue-green round fruit|pale paired seeds|brown star-shaped pod|blue hanging berries|red narrow berries
|purple berry sprig|red seeds on green husk|pale textured oval seed|layered blue-green bulb|green striped seed|red strawberry and leaf|brown spiral nut|purple spiked fruit
|orange striped fruit|blue-pink berry cluster|three striped round nuts|purple grape-like cluster|green curved pods|black striped seeds|three orange striped fruits|blue elongated berries
|purple textured berry|green spiny pod|three red round berries|pale blue teardrop berries|brown peanuts with kernels|blue seeds with pale kernels|brown chestnut-like fruit with pale kernel|pair of ridged brown nuts
|cut pale nut with brown centre|black seeds and pale kernel|brown powder on paper|black powder on paper|gold powder on paper|grey powder on paper|orange powder on paper|pale powder on paper''', 'Isolated fruit/seed ingredient: ')
add('prepared-items-native-32-18-fruit-vegetables', '''red round fruit and leaf|yellow pear|pink round fruit with seam|yellow fruit behind green leaf|pale yellow segmented round fruit|orange split fruit|brown oval root|orange round fruit and leaf
|green oval fruit|purple segmented fruit|three red cherries|three dark berries|long green cucumber|purple aubergine|pale yellow squash|green broccoli crown
|red tied bundle|yellow turnip|brown potato|red tomato|small pale turnip|red radishes|green pea pod|white garlic bulb
|orange carrot|red chilli|long green onion|purple flower-like pieces|orange bell pepper|curved green-yellow pepper|green striped round melon|brown striped oval melon
|green cabbage|white cauliflower|orange pumpkin|brown round coconut-like fruit|purple pitcher|pale blue pitcher|yellow pitcher with red trim|orange tankard
|blue bottle|green jug|brown shell with green interior|gold rectangular layered food|red-orange food on paper|mixed orange-green food on paper|blue pieces on paper|purple pieces on paper''', 'Isolated food/container icon: ')
add('prepared-items-native-32-19-spearman', '''raised fist|steel helmet and face|broad spearhead|narrow spearhead in red motion|crossed pole blades|upright spearhead|three flying darts|crossed polearms
|side-facing helmeted head|hand reaching over shaft|blue shield and spear|spearhead and red droplets|raised brown boot|boot beside blue shield|blue shield and debris|blue shield with red centre
|hand lifting narrow spear|bare torso and raised fist|fist beside steel helmet|three spearheads in pink motion|gold face in blue shield|spearhead with blue trail|brown boot thrust forward|bare torso flexing raised arms
|silver spear in pink rays|helmet above crossed polearms|hand gripping spear horizontally|flying narrow spear|crossed shafts and white arc|bloodied spear tip|broken blade fragments|hand gripping narrow blade
|spearhead and red droplets|spear thrust beside wall|metal fragments in pink burst|spear with green motion trail|helmeted figure and blue shield|upright spear in orange rays|narrow spear in red motion|crossed brown straps
|boot in cyan motion|blade above wooden barrier|helmeted bare torso by wooden barrier|shaft breaking beside spearhead|large spearhead in purple rays|boot beside fallen spear|spearhead in yellow motion|hand thrusting short blade''', 'Framed polearm/action illustration: ')
add('prepared-items-native-32-20-fishing', '''curved grey-orange fish|gold-red fish|blue-white fish|curled blue-green fish|green-orange fish|orange-white fish|narrow green-orange fish|purple-gold fish
|blue-pink curled fish|green-blue curved fish|round orange fish|grey-green angular fish|green-gold fish|small brown fish|grey-red fish|purple spiked shell
|blue curved fish|green curved fish|grey-red fish|gold striped curved fish|red fish|grey-red prawn|green-orange fish|blue-gold fish
|purple octopus|gold fish lure with hooks|red squid|orange crab|orange prawn|gold spiral shell|pink long worm|brown segmented worm
|three small blue fish lures|silver spoon lure|three green hooked lures|pink-blue lure|gold fishing rod|grey fishing rod|orange fishing rod|dark blue fishing rod
|dark mesh net|brown framed net|large pale fish hook|small hook and leader|green treble hook|silver treble hook|green wrapped float|blue line spool''', 'Isolated fishing object or aquatic creature: ')
add('prepared-items-native-32-21-aeromancer', '''open hand in pink rays|hand cupping dark swirl|hands around grey sphere|palm against curling air|hand and gold orb|two hands and red orb|blue figure with gold zigzag|upraised hand and blue wand
|grey cloud bank|grey angular blast|three pale swirling curls|black spiral wave|yellow ground burst|grey-green spiral wave|gold spiral projectile|grey sphere and gold ring
|broken grey links|hand gripping grey swirl|dark blue staff with red-orange flare|outstretched hand with blue cuff|raised dark clenched fist|screaming face amid dark streaks|dark pointed hood|pale blue face-covering mask
|three pale curved projectiles|black funnel vortex|purple orb inside grey swirl|dark humanoid with raised arms|round grey mesh disc|large pale tornado|blue staff and gold curl|blue staff above pale blast
|pale winged eye mask|dark boot on pale gust|dark rising wave|gold open mouth|helmeted face within blue circle|dark hooded silhouette|hooded face and gold curls|boot on dark gust
|gloved hand raising blue staff|blue circular ground rune|gold orb in black curved streaks|hand holding grey curved blade|blue swirling drop projectile|purple-gold spiral|pale circular crosshair disc|grey blades against green foliage''', 'Framed wind/action illustration: ')
add('prepared-items-native-32-22-crossbowman', '''short bolt and cyan trail|single pale bolt|bolt falling in green spiral|three blue bolts|silver bolt in gold rays|gold bolt in orange rays|orange outlined bolt|three bolts in red motion
|green orb and small bolt|bolt in target bullseye|fan of bolts from quiver|bolt piercing round red fruit|red humanoid silhouette|boot beside green bolts|crossbow with small pale marks|crossbow in blue rays
|crossbow surrounded by metal fragments|gloved fist thrust forward|three upright arrowheads|two bolts in orange motion|metal jaw trap in grass|white mesh shape|gloved hand with metal discs|crossbow firing in gold rays
|three bolts in yellow rays|spread of silver bolts|dark humanoid leaning backward|wide hat and blue face covering|brown grenade-shaped object|three broad wooden shafts|bloodied silver bolt|bolt with flame trail
|five dark star-like projectiles|gloved hand and grid of gold lights|hat-wearing silhouette lifting small crossbow|face beneath broad brown hat|gloved forearm and pale flash|boot beside red-green splash|raised dark gauntlet and bolt|crossbow firing in red rays
|boot on grass|bolt and scattered blue droplets|fan of gold bolts|crossed glowing purple blades|two bolts in purple rays|curved bolt in pale gust|green eye close-up|orange circular reticle''', 'Framed ranged-weapon/action illustration: ')
add('prepared-items-native-32-23-farming', '''brown sack with green contents|paper with red contents|dark folded cloth|grey folded cloth sack|orange tied sack|pale folded sack|brown three-tine fork|dark green three-tine fork
|silver three-tine fork|brown spade|green spade|silver spade|bound brown rope|folded orange leather|green satchel|purple pouch
|brown rectangular case|small brown barrel with side handles|green hoe|silver hoe|green rake|silver rake|brown broom|grey broom
|brown angled scythe|silver angled scythe|curved green sickle|curved silver blade|silver shovel|small green fork|gold rectangular bundle|small wooden crate
|open slatted basket|brown wooden crate|seedlings in rectangular planter|orange flower in planter|bare branch with blue buds|red-leaved sapling|small green sapling|bare root sapling
|small green-leaved sapling|purple flowering sapling|pale woven bowl|basket with tall handle|blue vase|orange pot|wooden bucket|grey watering can''', 'Isolated farming/tool icon: ')
add('prepared-items-native-32-24-scrolls', '''yellow curled sheet with red markings|orange curled sheet|pale curled sheet with blue markings|ragged gold sheet|plain brown rolled sheet|tied pale rolled scroll|orange sheet with red circular seal|pale sheet with purple loop
|green-edged sheet with red markings|brown flat written sheet|orange sheet with pink seal|yellow-green sheet with red border|closed white roll with red ends|closed blue-gold tube|closed gold roll|yellow roll with purple seal
|orange roll with green seal|pale roll with red seal|pale ragged sheet with gold markings|yellow ragged sheet and orange seal|orange sheet with green line|red sheet with green seal and pale tab|yellow roll with purple tabs|gold roll with green tabs
|pale scroll with dark ends|white-blue scroll with green mark|orange scroll with green mark|white scroll on red-ended rods|yellow curled sheet|orange scroll with green markings|long curled gold scroll|green-brown folded scroll
|pale forked long scroll|yellow curled sheet with red marks|pale scroll with red seal|grey curled sheet|orange curled scroll with red emblem|cyan curled scroll with blue emblem|yellow curled scroll with orange emblem|purple curled scroll
|blue curled scroll|green curled scroll|yellow sheet with pink top border|grey sheet and brown seal|white sheet with dark round seal|orange curved sheet|yellow curved sheet with red border|pale sheet with purple round seal''', 'Isolated written scroll icon: ')


add('prepared-items-native-32-25-cryomancer', '''open hand and ice chips|pointing hand and small snowflake|glove bearing blue crystal|hand holding curved ice wave|fist and upright ice spikes|two raised hands and icy fragments|crossed blue-white objects|hand gripping blue crystal
|rolling pale ice wave|curved blue wave|three pale curled gusts|tall curling wave|burst of ice shards|blue circular wave|flying blue shard|round cracked ice sphere
|hand striking with ice fan|glove holding pointed ice|hands surrounding blue shard|hand sweeping through icy spray|upright blue obelisk|pale blue frozen face|dark hood with gold inner rim|ice-covered face mask
|scattered angular ice fragments|tall slanted ice wall|round eye-like gem in ice|dark blue humanoid silhouette|blue liquid test tube|cyan handprint|large blue arrowhead|thin spear of ice
|pale mask with dark eye openings|boot on icy gust|thin figure above ice burst|screaming icy face|ice-covered upright torso|dark hood in blue aura|hooded figure above ice spikes|boot on icy ground
|staff with blue crystal|blue circular ground mark|upright ice shard and red orb|gloved pale fist|blue crystal on gold shaft|curved trail of ice shards|ice-covered triangular emblem|blue fragments among green foliage''', 'Framed ice/action illustration: ')
add('prepared-items-native-32-26-mushrooms', '''pair of cream rounded caps|gold clustered caps|orange caps, pale stems|brown caps, yellow stems|thin gold caps|red-white wavy caps|brown thick-stem caps|orange shelf caps
|red-gold caps|red white-spotted caps|brown-pink cap with gills|pale flat caps|dark pointed caps|blue layered caps|tiny gold caps, red stems|purple curled cap
|wide gold flat cap|red-blue shelf fungus|orange branching fungus|brown spotted caps|purple ribbed cups|pink-orange shelf caps|cyan round tips on dark stalks|orange ridged cups
|pale blue funnel caps|orange wrinkled caps|brown tall pitted caps|grey-blue shelf caps|pale grey flat caps|pink bowl caps|brown small caps|green flat cap
|gold curled caps|green spotted cap|orange striped caps|pale grey tall caps|green-gold pointed cups|brown red-spotted caps|dark purple broad caps|small green pointed buds
|tall blue pointed fungus|orange concentric caps|red pointed caps|red spherical caps|red branching fungus|cyan folded cap|red-gold curved fungus|brown cylindrical caps''', 'Isolated fungus illustration: ')
add('prepared-items-native-32-27-meat-skin', '''red paired organs|pale pink fillet|red steak|brown bone-in meat|red oblong meat|pink rib slab|grey scaled skin|brown scaled skin
|pink paired meat pieces|red curled meat|pale jointed bone|pink bone-in meat|two pale brown bulbs|dark red segmented meat|brown striped skin|orange-pale meat pieces
|rolled brown leather|pale grey hide|gold pointed hide|orange striped hide|red meat chunk|long dark red meat|red rib meat with bone|pair of red bone-in pieces
|orange scaled skin|brown-pale cut layered strip|rolled tan hide|rolled dark scaled hide|red steak|brown bone-in shank|pale pink tapered meat|brown handled mallet-like piece
|green-red fish fillet|red-pale layered strip|pale small skull|long orange curved horn|three pale ribs|pale horned skull|curled dark horn|bundle of pale bones
|red-pale ribcage|round red steak|small pale skull and antlers|long branched dark antler|pair of cloven hoof pieces|pale skull and orange antlers|brown branching antler|long pale bone''', 'Isolated animal material or meat: ')
add('prepared-items-native-32-28-food', '''grilled strips on platter|skewer with brown pieces|brown drumstick|curled grilled food|blue-gold dish|brown croissant|gold textured loaf|round browned pie
|scored loaf|small decorated round cake|pale bread pieces|striped golden loaf|dark red fish steak|small green fish|curved brown sausage|red segmented sausage
|sliced meat roll|bone-in roast|hanging ring of sausages|brown ring bread|purple layered pastry|round topped pizza|long purple-topped pastry|cut pie with purple filling
|split red-brown sausage|gold banded container|purple strapped bottle|grey bottle|green bottle|dark blue bottle|plate of red-orange food|plate with orange fruit and green leaves
|plate of diced salad|woven bowl of pale food|woven bowl of red-yellow food|woven bowl of green food|wooden tankard with steel bands|foaming banded tankard|brown wooden mug|foaming wooden mug
|blue metal mug|tall wooden cup|red bowl of yellow food|red bowl of vegetables|red bowl of pale blue food|green bowl of brown pieces|green bowl of pink pieces|green bowl of vegetables''', 'Isolated food/drink icon: ')
add('prepared-items-native-32-29-potions', '''red-green striped round flask|gold angular flask|green-brown banded flask|green hourglass flask|blue angular flask|pale gold pointed flask|dark skull-shaped bottle|purple twisted bottle
|pink narrow hourglass bottle|cyan-gold round bottle with loop|purple triangular flask|gold-blue hourglass bottle|orange curved flask|dark green flask with red markings|turquoise round bottle|orange twisting bottle
|pink flask in ornate frame|pink narrow tube with blue wings|green drop bottle and red cord|gold tube in green holder|red broad bottle|pale-gold curved bottle|yellow-green pointed bottle|gold-black curved bottle
|purple spiral bottle|pale blue crescent bottle|orange flask with green oval|green round flask with gold bands|blue tapered vial|pink pointed flask|orange narrow flask with blue oval|yellow-orange double bulb bottle
|green-blue rectangular bottle|dark blue long-neck flask|red round gem-shaped flask|dark purple narrow flask|green long-neck flask with orange inset|gold striped spiral bottle|red-blue wide-bottom flask|purple rectangular bottle
|pink ornate tube|pink heart flask|blue-white spiralled vial|red pointed cross-like flask|green twisted bottle with red centre|round amber bottle with paper tag|purple tube with oval gem|orange crescent bottle with blue top''', 'Isolated decorative potion container; contents/effect unverified: ')
add('prepared-items-native-32-30-buffs', '''flame and green up arrow|flame on red shield|orange scratches and green up arrow|gold bolt on shield|purple starburst and green up arrow|purple star on shield|skull and green up arrow|skull on dark shield
|sword and green up arrow|sword over pale shield|axe and green up arrow|axe over green shield|spear and green up arrow|spear over gold shield|curved blade and green up arrow|curved blade over purple shield
|red dripping slash and green up arrow|red drop on shield|green leaf and green up arrow|green leaf on shield|gold wavy lines and green up arrow|gold waves on shield|blue swirling emblem and green up arrow|blue swirl on shield
|bow and green up arrow|bow over pale shield|pickaxe and green up arrow|pickaxe over purple shield|purple orb in orange grid|large red heart|heart surrounded by red plus signs|raised fist and green up arrow
|gold ridged round object and green up arrow|gold ridged object on shield|purple diamond mark and green up arrow|purple diamond mark on shield|steel breastplate and green up arrow|blue hooded head and green up arrow|green flexing silhouette|boot with pale wings
|red cloak|green hood|boot with red plus signs|hand with blue motion|boot in gold motion|green upward arrows around bars|orange humanoid in circular frame|pale winged upright emblem''', 'Framed status-symbol illustration: ')
add('prepared-items-native-32-31-jewellery', '''gold ring with blue angular gem|silver ring with green oval gem|gold ring with purple round gem|green ring with gold gem|gold eye ring with red slit|gold ring with red-black square|purple ring with green bow|silver ring with pale round gems
|turquoise gold-rimmed band|gold-purple patterned band|silver ring with green square|purple band with red cross|silver-blue narrow band|red-purple ornate band|gold-blue band with red gem|dark ring with gold-green gem
|gold band with red gems|silver ring with orange shard|gold ring with purple flower|black ring with red accents|blue round pendant|red-orange diamond pendant|green teardrop pendant|green angular pendant
|gold band with green-red accents|silver-blue ring with gold edge|dark red eye ring|gold band with pale purple inset|green-gold ornate pendant|purple triangular pendant|green-gold winged pendant|red angular pendant
|blue hourglass-shaped pendant|pink hourglass-shaped pendant|pale necklace with pink and red gems|blue-purple feather-like necklace|orange horizontal charm and silver loop|gold star with green gem|gold wings and green gem|orange face-shaped pendant
|blue-green ornate mask pendant|dark pendant with red eye spots|turquoise gem in gold frame|gold bead necklace|pale tooth charms and red bead|skull pendant with red gem|gold-green eye pendant|red spiked pendant in gold ring''', 'Isolated jewellery icon: ')
add('prepared-items-native-32-32-treasure', '''closed brown chest|open brown coin chest|closed red chest|open red coin chest|closed purple chest|open purple coin chest|closed pink-gold chest|open pink-gold coin chest
|closed green chest|open green coin chest|closed green-blue chest|open green-blue coin chest|closed pale wooden chest|closed brown chest with pale bands|closed pale gold chest|closed pale gold chest with red base
|closed red-gold ornate chest|closed orange-gold ornate chest|closed icy blue chest|open icy blue chest|green coiled container|open green coiled container|closed turquoise-gold chest|closed green-gold chest
|silver square-loop key|gold key with green square gem|silver key with blue gem|dark key with red loop|gold key with orange sphere|silver key with purple gem|gold key with blue-green gem|gold key with pink claw-like end
|gold ornate key|dark key with green loop|gold skull-shaped key|green crystal key|stack of gold coins|single patterned gold coin|brown spiral coin-like disc|purple ring with silver interior
|silver ring with blue studs|gold bracelet with coloured gems|gold cup with green-red gems|gold-red pedestal cup|dark blue-gold cup|gold ring with green gem|gold-red crown with green gem|dark ring with orange gem''', 'Isolated treasure/container icon: ')


add('prepared-items-native-32-33-spears', '''long straight silver sword|gold long blade|narrow dark spear|orange long blade|silver two-prong fork|silver three-prong polearm|curved gold-edged blade|grey curved sickle
|pale curved blade on long handle|blue-grey glaive|thin dark hooked scythe|silver hooked blade, purple crossbar|double-ended narrow blade|dark double-ended polearm|silver spear, gold-red shaft|green-gold double-ended spear
|broad silver cleaver blade|green three-prong fork|orange pointed staff with orb|gold two-prong fork|green curved blade|pink hooked blade|brown curved bow|curved blue blade with loop guard
|silver two-prong fork|slim silver spear|long silver-gold glaive|gold straight spear|green broad spear|gold pointed spear with forked side|grey scythe blade|gold hooked pole blade
|broad gold cleaver blade|gold narrow hooked blade|silver crescent sickle|dark curved glaive|green double-ended spear|dark gold-edged double blade|silver double-ended spear|gold double-ended spear
|silver fork with orange shaft|silver-gold trident|gold straight spear with red shaft|blue straight spear|green curved bow|brown curved bow|dark pale-tipped bow|three purple pointed fragments''', 'Isolated weapon-shaped object: ')
add('prepared-items-native-32-34-sigils', '''green-blue round gem with pointed tabs|orange round square-centred disc|silver layered round disc|green rectangular gem in round frame|purple inscribed round gem|gold-red radial disc|blue-silver round star|orange gem on pale square
|gold disc in blue star frame|blue circular geometric grid|pink-black four-point star|pink-blue square in round frame|orange-gold curled frame|red triangular eye-like emblem|green triangular leaf emblem|orange-green radial disc
|purple circle and gold cross|gold-edged disc with coloured sectors|pale turquoise geometric square|red-green square with angular lines|multicolour geometric square|dark square with red-orange sectors|blue diamond with pale centre|green crossed diamond emblem
|blue inverted triangle with gold centre|silver hourglass frame and blue orb|red inverted triangle and gold bars|blue-green six-point star|green-gold hanging circle below triangle|green inverted triangle with dots|gold radial flower disc|red square spiral in grey frame
|orange concentric circles|gold-orange angular gem|green-blue hourglass grid|purple round eye emblem|orange triangle with hanging dots|pink diamond with blue centre|blue pointed crystal over crossed triangle|purple inverted triangle with pale orb
|pink hourglass triangle|green-blue striped inverted triangle|grey radial wheel|gold-orange radial wheel|orange oval on green geometric square|gold mosaic square|green star and upright bar|pink triangle and gold cross disc''', 'Isolated geometric emblem; meaning unverified: ')
add('prepared-items-native-32-36-swords', '''silver straight sword, gold guard|orange straight sword|gold broad straight blade|red straight blade|narrow gold blade|silver curved blade|cyan straight blade|thin grey blade with loop guard
|pink curved blade|gold narrow ornate blade|green straight blade|thin dark blade with angular loop guard|pale silver straight blade|orange sword and curved guard|pale green broad blade|blue blade with purple guard gem
|long orange blade|cyan curved blade|purple narrow blade with dark prongs|pale curved blade with basket guard|gold narrow blade with red tassel|wide blue-grey straight blade|broad gold-orange blade|brown-gold broad blade
|gold-edged narrow blade, red guard|blue thin straight blade|broad gold blade, plain handle|orange patterned broad blade|red straight blade and angular guard|pink curved blade and brown handle|grey straight blade and basket guard|blue straight blade, green guard gem
|thin grey curved blade|pink broad blade|thin blue straight blade|thin green blade|dark broad straight blade|orange-gold broad blade|gold curved broad blade|green sword with broad guard
|pale green broad blade|cyan straight blade|orange straight blade and jagged guard|dark blade with silver basket guard|blue wavy pointed blade|red broad blade with dark guard|blue blade with loop guard|broad orange-gold blade''', 'Isolated sword: ')
add('prepared-items-native-32-37-debuffs', '''cracked silver sword|loose open hand and blue drops|hand restrained by chain|boot and red down arrow|crossed broken pale bands|blue falling humanoid|purple jagged hand shape|dark spider with red abdomen
|raised open hand|purple floating dots and scratches|green open mouth with fangs|four red scratches|boot with grey curling lines|brown breastplate and red down arrow|orange flaming horse-like head|boot surrounded by fire
|two red bleeding cuts|pale branching cracks|dark hooded ghost silhouette|pair of orange curved horns|grey helmet trailing dark purple cloth|wide startled eye|broken bow and fragments|open palm beside red exclamation
|orange flaming hand|dark curved blade beside purple figure|winged boot and red down arrow|boot with red bent arrow|boot stuck in grey rubble|red heart and red down arrow|heart pierced by silver bolt|raised fist and red down arrows
|green tongue and red drop|pale blade with red drop|blue ice shard|boot in tangled vines|pale hunched figure with red cloak|blue side-facing head silhouette|grey skeleton leaning forward|tilted hourglass with green sand
|broken brown shield-like pieces|red head silhouette and green-red dots|two raised bound hands|boot wrapped in dark tendrils|upraised hand wrapped in purple cloth|red down arrow around bars|pale crescent moon|blue round orb with dark handle''', 'Framed condition-symbol illustration: ')
add('prepared-items-native-32-38-helmets', '''brown open hood|brown cap with steel goggles|plain silver open-face helmet|steel helmet with olive face cover|purple-gold domed hat|brown wide hat with silver band|orange pointed hood|brown ragged hood
|white-green cloth hood|red beret with feather|purple head wrap with pink jewel|blue pointed hat|silver hooded helmet|olive hood with dark face opening|steel helmet with red crest|brown horned helmet
|brown face helmet|steel-gold angular face helmet|green-gold hooded helmet|red cloth hood|chainmail coif with gold brow|silver pointed hooded helmet|green cloth hood with silver brow|gold face helmet with red crest
|blue-silver barred face helmet|red-silver barred face helmet|green tentacle-front helmet|blue barred helmet|red-gold closed helmet|green-blue ring-front hood|grey flat-topped closed helmet|silver-blue barred helmet
|purple spiked helmet|black-gold face helmet|green-silver closed helmet|gold-orange horned helmet|blue draped face veil|gold broad round hat|purple-silver pointed hood|brown-gold broad hat
|red spiked hood|gold-silver brow helmet|pink spiked hood|gold angular full-face helmet|silver bird-like face helmet|gold-blue wrapped headpiece|black-gold horned face helmet|brown horned skull-like helmet''', 'Isolated headwear: ')
add('prepared-items-native-32-40-staves', '''silver spiked orb on dark shaft|blue loop-headed staff|red-orange lattice head|gold-red curled head|pink square-loop head|orange round head|silver hammer-like head|red cross head and hooked base
|green angular head|gold star head|silver fan head|green angular fork head|gold round headed staff|gold pointed rod|cyan twisted rod|gold ringed rod
|red narrow faceted rod|pink banded rod|silver striped orb and gold handle|brown horned skull head|blue curled orb head|gold winged head|plain twisted brown branch|dark green segmented rod
|silver spiked orb head|blue oval mesh head|green-gold narrow head|silver-green pointed head|orange segmented head|cyan pointed head|dark grey disc head|orange forked head
|gold double mallet head|green star-like head|blue spiked disc head|pale skull head with red horns|pale cyan faceted rod|plain gold twisted branch|pale twisted branch|grey hooked blade head
|gold loop head|brown gold-ended rod|purple orb in green claws|orange flame-shaped head|three pink hooked tips|dark red branching head|brown-gold bead-like head|pale small bone head''', 'Isolated staff/wand-shaped object: ')

add('prepared-items-native-32-batch2-01-bracers', '''gold forearm plates|steel long cuffs|silver fingered gauntlets|dark cuffs, olive fingers|purple long cuffs|orange cuffs, blue bands|brown gold-rimmed cuffs|olive gold-rimmed cuffs
|grey cuffs with green decoration|red-gold pointed cuffs|purple-brown gauntlets|blue-gold short cuffs|silver-green fingered gauntlets|olive-dark segmented gauntlets|steel gauntlets with red fingers|brown cuffs with dark fingers
|orange patterned gauntlets|silver gauntlets with gold bands|green gauntlets with gold bands|red-orange gauntlets|steel cuffs and gold fingers|blue ridged gauntlets|green-banded olive gauntlets|gold-brown gauntlets
|blue-silver gauntlets|red cuffs, steel bands|olive-green long cuffs|blue cuffs, black fingers|red-gold long cuffs|silver-green gauntlets|black pointed gauntlets|dark gauntlets, blue knuckles
|purple spiked gauntlets|dark cuffs, gold fingers|green-grey segmented cuffs|cream cuffs, red trim|blue-orange gauntlets|orange-gold gauntlets|black cuffs, purple gem|short brown leather gloves
|red spiked gauntlets|plain gold cuffs|pink-black gauntlets|gold-silver gauntlets|pale blue spiked gauntlets|gold-blue slender forearm pieces|black-gold gauntlets|dark gauntlets with pale horn-like trim''', 'Pair of isolated arm/hand armour pieces: ')
add('prepared-items-native-32-batch2-02-axes', '''blue axe, wooden haft|green angular axe|dark gold-edged axe|gold hooked axe|silver forked head|silver-purple narrow head|large grey open-headed axe|orange hooked axe
|green crescent axe|red broad axe|red-orange hooked axe|gold patterned axe|green broad open-headed axe|gold crescent axe|grey round perforated head|pink spiked mace head
|green spiked head|purple curved axe|gold spiked bar head|grey fork head|pale star-shaped head|gold spiked block head|green round spiked head|gold-orange angular axe
|grey double axe|small gold-red axe|grey straight-edged axe|pink curved axe|red hooked axe|cyan triangular axe|broad grey rectangular axe|broad gold-red rectangular axe
|gold-red axe|grey slanted cleaver|gold fork-like axe|cyan broad axe|gold hooked head|grey-blue curved axe|purple double axe|red branching axe
|dark grey broad axe|purple broad axe|cyan double axe|gold triple-point head|blue ornate double axe|red angular double axe|gold-brown double axe|blue round split head''', 'Isolated hafted weapon: ')
add('prepared-items-native-32-batch2-04-boots', '''orange wrapped high boots|short brown boots|plain steel greaves|olive boots with steel cuffs|purple-gold high boots|brown high boots with pale cuffs|short brown boots with pale cuffs|dark olive boots
|pale green ankle boots|red slippers with gold studs|purple slippers with gold trim|blue slippers with gold cuffs|pale blue-green greaves|olive laced boots|steel-red greaves|brown ankle boots
|brown high boots|short steel-gold boots|green boots with gold toes|tall red pointed boots|steel-gold greaves|dark blue greaves|green-brown high boots|orange laced high boots
|blue-silver greaves|red-silver greaves|olive boots with green panels|blue ridged greaves|red-gold pointed ankle boots|steel-green greaves|dark patterned high boots|dark ankle boots with blue caps
|purple spiked boots|grey-gold greaves|grey-green greaves|white-gold-red tall boots|orange-grey greaves|grey greaves with gold pointed tops|purple-grey ankle boots|gold-brown ankle boots
|red crisscross high boots|black-gold boots|pink-black high boots|gold-silver greaves|pale blue plate greaves|short gold shoes with side points|black-gold pointed boots|dark greaves with pale crossed straps''', 'Pair of isolated footwear pieces: ')
add('prepared-items-native-32-batch2-05-cuirasses', '''red-orange wrapped tunic|brown tunic, steel chest plate|silver plate torso|olive-steel torso armour|purple-red tunic with gold trim|orange tunic with silver shoulders|crossed brown leather tunic|dark cloth torso with tan sash
|green-white robe|red-gold robe|purple-gold robe|blue robe with gold sash|pale silver-gold plate torso|olive-black torso armour|silver torso with red sleeves|brown short tunic
|brown-orange patterned robe|dark torso with gold plates|green robe with gold shoulders|plain red-gold robe|pale blue robe with gold straps|dark blue ridged torso armour|green-grey torso and cloak|gold-orange long garment
|blue-silver plate torso|red-silver plate torso|olive torso and green hanging panels|blue chest plate and dark skirt|red robe with gold trim|green-blue segmented torso|dark grey angular plate torso|blue-black layered torso
|purple-black spiked robe|steel torso with gold plates|green-grey rounded plate torso|gold-orange pointed breastplate|orange-grey torso armour|gold-red torso with steel shoulders|purple-white fitted torso|orange segmented long armour
|red-orange patterned torso|pale gold-black fitted torso|pink-red spiked torso|gold-silver breastplate|pale blue angular chest armour|blue-gold slender torso|black torso with gold-orange frame|brown horned skull-shaped chest armour''', 'Isolated torso garment/armour: ')
add('prepared-items-native-32-batch2-06-trousers', '''orange cloth trousers|brown plain trousers|steel dark leg plates|olive-steel leggings|red-gold trousers with purple greaves|brown-orange leggings with steel knees|brown-red trousers|olive cloth trousers
|dark trousers with green greaves|red draped trousers|purple draped trousers|blue-gold stockings|pale silver greaves|olive dark leg plates|red-dark trousers with steel shins|brown-dark trousers
|brown patterned leggings|dark leggings with gold knees|green-gold leggings|red-orange leg armour|blue leggings with gold sides|dark blue leg plates|olive leggings with green side cloth|gold-orange leggings
|blue-silver leg plates|red-silver leg plates|olive-green leggings|blue-black leg plates|red-gold cloth leggings|silver-green leg plates|dark grey angular leg plates|blue cloth with grey shin guards
|purple spiked leggings|black-gold leg plates|green-black leg plates|orange-gold layered leggings|orange-grey pointed leg plates|gold-red segmented leg plates|pale blue trousers with purple boots|orange-black leggings
|red spiked leggings|pale gold-black leggings|pink-black leg armour|gold-silver leg armour|pale blue angular leg plates|gold long side panels with blue gem|black leggings with gold-red trim|brown leggings with pale crossed straps''', 'Isolated pair of legwear pieces: ')
add('prepared-items-native-32-batch2-07-staves', '''red three-prong head|glowing green orb and leafy shaft|plain narrow red branching head|gold curled round head|small gold round head|multicolour gem head with sparks|pink pointed head|gold-red forked head
|brown-red three-prong head|cyan small cross head|purple round gem in brown hook|pale bone fork head|green-gold head with hanging cyan fringe|red fork head|red pointed crystal head|dark blue cross head
|grey cross head with purple bead|red three-prong head|gold branch head|cyan three-prong head|dark blue pointed head|green orb with leafy side points|gold star head|red trident head
|cyan oval loop head|dark red curled head|orange oval gem head|pale branching head|pale skull head and red side blade|green pointed head|red orb and green curved shaft|gold-orange rectangular head
|dark fork head with gold band|red trident head|dark blue three-prong head|gold curled flame-shaped head|pink trident head|green fork head|gold-orange hook head with hanging beads|red oval loop head
|gold star head|pink round loop head|small blue oval head|gold-red flared head|dark green leaflike head|purple-green gem head with floating squares|pale bone branching head|blue oval gem in gold frame''', 'Isolated long staff: ')

add('prepared-items-native-32-batch2-09-halloween', '''orange pumpkin|large orange pumpkin|yellow-green pumpkin|orange pumpkin bucket|brown sack with red face|brown tied pouch with stitched pattern|three coloured round sweets|three wrapped orange sweets
|carved orange smiling pumpkin|yellow carved pumpkin|red carved pumpkin|pink carved pumpkin|brown pumpkin with glowing mouth|gold grinning pumpkin|green-speckled cupcake|purple frosted cupcake
|blue-purple striped lollipop|green wrapped sweet|blue-gold curled ribbon|green-yellow crescent sweet|red spider with pale legs|three coloured bugs on web|dark spider on blue web|yellow bat
|three orange round sweets|orange round fruit|green pear-shaped fruit|blue-purple lollipop|brown stitched ghost-like patch|brown horned skull patch|brown wooden face patch|pale open fanged jaws
|gold snake head|red-green curled snake|black wolf silhouette and moon|leaping black cat and moon|black hood silhouette and moon|black owl and moon|dark curled figure and moon|black bat and moon
|purple pointed hat|pale hand with pink cuff|three wrapped brown sweets|plate with cut brain-like food|pale ladle with red-green contents|dark disc with pale skull|purple round skull emblem|pale skull trailing coloured beads''', 'Framed decorative illustration: ')
add('prepared-items-native-32-batch2-10-blacksmith', '''square hammer face, sparks|hammer striking metal|upright dark mallet|wide mallet in gold motion|dark double mallet|mallet over pink arc|brown mallet with gold glow|glowing orange metal on dark surface
|long dark hammer, sparks|dark mallet and orange sparks|square dark hammer and purple burst|hammer striking dark metal|crossed dark hammers|upright mallet with ground sparks|hanging metal chains|bright curved orange metal streak
|four glowing diamond shapes|bucket of glowing orange contents|large glowing orange diamond|small crucible held in metal tongs|metal bar held by hand|silver tongs and pale steam|glowing metal bar held in tongs|hand gripping silver rod
|bed of glowing coals|gold rimmed dark circular vessel|large crucible with glowing rim|pouring molten orange stream|stacked metal ingots|metal balls and rod|hand hammering hot metal|gold breastplate
|three upright rods|hands using dark tool|gold curved hood-like plate|dark steel ingot|blade pressed to brown surface|curved metal horseshoe|chainmail mesh|silver blade above brown block
|glowing narrow blade|hand gripping metal tong|riveted dark metal plate|silver blade with star glint|steel breastplate|four gold coins|stack of pale steel plates|crossed orange tongs''', 'Framed metalworking illustration: ')
add('prepared-items-native-32-batch2-11-arrows', '''dark narrow tip, pale fletching|orange tip, pink fletching|silver tip, green fletching|green tip, red fletching|blue tip, grey fletching|gold-red tip, orange fletching|grey tip, pink fletching|pink tip, pink lower shaft
|gold tip, gold fletching|green tip, green fletching|pale tip, blue fletching|dark green tip, purple fletching|gold tip, red fletching|cyan tip, blue fletching|gold tip, orange fletching|gold tip, purple fletching
|orange broad tip, brown fletching|dark narrow tip, red fletching|gold tip, brown forked fletching|gold crescent tip, gold fletching|green tip, green fletching|gold tip, red fletching|red square tip, red fletching|cyan dotted segmented shaft, purple end
|pale blue tip, cyan fletching|silver tip, green lower shaft|green tip, green fletching|silver tip, pink fletching|silver-blue tip, blue fletching|green tip, red lower shaft|silver tip, blue fletching|silver tip, gold fletching
|blue forked tip, purple shaft|red crescent tip, red shaft|dark tip, green shaft and red fletching|pink tip, pink shaft|pale gold tip, orange shaft|gold-red tip, red shaft|gold tip, purple shaft|red tip, dark shaft and brown fletching
|orange forked tip, brown shaft|pale tip, gold shaft and blue fletching|gold tip, gold-dark shaft|gold crescent tip, gold shaft|green tip, dark shaft|gold tip, gold shaft and red fletching|red square tip, red shaft|cyan arrow with dotted cyan-purple parallel trails''', 'Single isolated arrow-like projectile: ')
add('prepared-items-native-32-batch2-13-spears', '''small gold tip, blue shaft|silver tip, red tassel|gold long blade and shaft|blue long blade and dark shaft|gold pointed tip, dark shaft|red narrow tip and shaft|green crescent tip, teal fittings|gold-red broad spearhead
|green-gold pointed head|gold-red pointed head|cyan spiked head|red-gold head, green shaft|grey fork, purple shaft|orange blade, gold guard|small pale tip, gold shaft|orange flame-shaped head
|dark crescent fork, turquoise fittings|green looped spearhead|gold-orange broad head|pink tapered head, dark shaft|broad silver blade, blue shaft|long pale silver blade, gold shaft|grey hooked blade, gold shaft|pink curved blade, dark shaft
|pink narrow tip, gold shaft|red oval tip and shaft|gold double-ended side points|gold circular fork head|gold star head|orange forked head|silver axe-like fork, blue shaft|cyan broad head, dark shaft
|green-gold head|red cross head|cyan double-blade head|green trident head|silver hooked head with red fitting|silver oval fork head|orange claw-like head|pink hooked head
|green spiked head|gold spearhead with red fittings|silver trident head|pale blue blade|red-orange broad head|small gold forked head|broad gold-red blade|grey hooked blade, gold-blue shaft''', 'Isolated long polearm: ')
add('prepared-items-native-32-batch2-15-curses', '''green-blue hand with red fingertips|red claws on green background|three bloodied scratches|green clawed hand|red-green humanoid symbol|green curled hand|dark blue angular red-eyed mask|green claw-like web
|two reaching pale hands|upright pointed gold object|long pointed brown object|pale cracked surface|brown claws over blue water|orange branch with dark holes|open fanged mouth|close-up pale fangs
|dark gaping mouth with spots|pale hooded figure with dark wings|small pale ghost|pale ghost with blue trailing fringe|brown raised hand, purple tendrils|orange reaching hand|brown spoon-like object|hooded dark figure in grey circle
|open mouth framed by pale fingers|purple-green spiral|pale icy bent shape|red dripping strips|bright gold fan of rays|red humanoid trailing strands|brown hanging hand and green trails|dark silhouette with blue lightning-like rays
|dark figure behind pink hanging grid|grey skull with gold eyes|red hood with dark face|green hood and white curling wisps|pale skull with orange frame|large bloodshot eyeball|pale skull in blue web|purple skull with gold rays
|green reaching hand with gold streaks|brown claw-like tips and pink rays|pale bone with star glint|pale skull and detached jaw|skull atop many dark legs|grey reptile-like face and gold eyes|green skull-like mask|cyan ghost-head profile''', 'Framed ominous/condition illustration: ')
add('prepared-items-native-32-batch2-16-druid', '''outstretched dark gloved hand|hand holding floating leaves|upraised brown gauntlet|hands around green and pink gems|raised open palm|hanging brown hand|pale wolf-like head in blue circle|green tree
|large brown claw|brown hand with turquoise talons|snarling pale animal muzzle|three red scratches|gold beaked bird head|red paw print and blue claw marks|axe cutting brown wood|pale textured hide
|pink flower and green stem|tangled pale branches|dark cauldron beneath horizontal branch|two hands clasped|round wood cross-section and small blue vessel|pale skull on brown stalk|brown hollow log|red and pale animal skulls
|green-blue overlapping scales|gold striped animal face|two glowing eyes in leafy darkness|brown wooden face|gold medallion on green chain|purple triangular woven ornament|pale seed hanging within pink ribs|brown paw on blue forearm
|red-blue mask and gold trim|grey bird head and orange beak|grey wolf head|red humanoid silhouette and gold orb|curled grey wolf head|single orange eye in darkness|roaring gold-brown animal head|grey striped animal head
|green ornate upright garment|pale blue humanoid spreading arms|brown wooden face and gold eyes|brown branching claw and blue swirls|wooden clawed hand|green leaf in gold triangle|orange leaf in purple circle|dark oval stone with blue paw print''', 'Framed nature/action illustration: ')

add('prepared-items-native-32-batch2-17-bones', '''pale skull with red teeth|gold skull|side-facing pale skull|blue metal face helmet|broken brown jaw|small skull under black swept shape|pale rounded animal skull|pale long-toothed skull
|hollow grey skull fragment|brown beaked skull|pale long skull with red teeth|gold elongated skull|three pale teeth with red roots|pile of orange-brown material|three small pale bones|three curved gold ribs
|broken brown long bone|three small hollow skull pieces|three blood-tipped pale fangs|three pale claw pieces|three bent pale bones|three brown joint fragments|three grey jaw-like pieces|pale spiked vertebrae
|pale long horned skull|brown pointed animal skull|pale flared nasal skull|pale grinning skull with red seams|brown long-snouted skull|brown hollow curled bone|pale skull with brown horns|gold pointed horned skull
|gold grinning horned skull|pale long skull and dark horns|brown long bone with red tip|grey open jaws with red teeth|three curved brown fangs|three brown teeth|three hollow white bone tubes|three brown curved ribs
|pale ribcage|three grey bone segments|three brown claws|three gold curled bones|orange jawbone|brown branching bone|three pale red-tipped fangs|three dark blue curved claws''', 'Isolated bone/skull material: ')
add('prepared-items-native-32-batch2-18-demon', '''green claw in dark swirl|pale curved tentacle|pale horned ornate mask|orange claw-like slash|pale blue screaming ghost face|green ghost face|blue horned humanoid|dark horned crown with orange flame
|red humanoid with raised arms|pale ghost enclosed in grey ring|red horned mask|dark humanoid with green eye-shaped head|purple horned silhouette|red-blue horned face|gaping green toothed mouth|blue curled round creature
|snarling brown animal head|purple broad-horned torso|grinning skull beneath dark crown|small red horned humanoid|dark pointed claw|hooded figure behind ledge|open orange fanged mouth|clenched brown fist
|long red tooth-lined mouth|orange mouth inside blue wave|dark horned face on raised hand|orange horned face with green eyes|brown muscled torso and gold flame|gold skull in blue swirl|black bird-like head beneath gold crest|green fanged mouth
|green broad humanoid silhouette|red horned head and torso|three blue ghost faces|purple curled claw shapes|dark mask with red-orange eyes|two pale curved claws around dark face|pink shapes with pale eyes|dark many-legged silhouette with red claws
|purple horned face|pale clawed hand in orange rays|two pale eyes beneath red symbol|open pale hand with red palm mark|red-gold circular eye symbol|green rune-like symbol in dark circle|purple orb above stepped altar|red face above gold grid''', 'Framed creature/effect illustration: ')
add('prepared-items-native-32-batch2-19-necromancer', '''large pale skull|white skeleton profile|dark skull and orange sparks|pale skeleton in green light|hooded skull holding pale blade|skeleton crawling from dark ground|grey foot and green wavy trails|grey humanoid with blue hanging trails
|pale curved hand and green drips|pale hand and pink rays|two hands pouring blue light|hands holding pale upright shield-like shape|open hand and orange arc|bent pale limb|dark eye in gold oval|pale ribcage on gold background
|grey raised hand|green skull, red eye and pale hammer|purple hooded green face|three walking skeletal silhouettes|green-eyed snarling face|grey screaming skull and orange eyes|grey gravestone and pale streaks|pale blue curving beam
|small dark figure on rocky ground|purple hooded skull lifting hand|dark red hooded face|dark crawling silhouette over green ground|purple hooded skull reaching forward|purple robed silhouette with outstretched arms|grey crossing bone pattern|open pale book and red symbol
|two grey-green fists|pale curved bone spiral|tall pale ribbed figure|blue-purple spiral|cyan skeletal humanoid|cyan ghost rising from grave|orange eyeball under dark hood|red anatomical heart
|white bone charm on wooden planks|grey skull and orange flame|dark ribcage and green-pink butterfly-like mark|green bone lattice charm|green-eyed skull in purple hood|dark skull in red oval|purple book with green symbol|pale skull in turquoise circle''', 'Framed skeletal/effect illustration: ')
add('prepared-items-native-32-batch2-20-priest', '''raised black-gloved hand|gloved hand above gold sun|gloved hand holding green flame|dark face with blue forehead light|outstretched dark glove|two fists below blue cross|small gold cross and pink rays|large gold curved beam
|upraised dark glove and blue glow|two reaching gloved hands|open dark hand in blue rays|open dark hand and red arc|upright dark palm|pointing dark hand|pale green diagonal beam|curved gold trail
|gold sun and radial rays|green vertical light curtain|pale wing-like light rays|gold winged circle|converging blue rays|gold-silver hammer|silver sword with gold guard|silver narrow spearhead
|gold grid-like cross|blue humanoid spreading arms|gold head profile|dark green seated silhouette|sword hilt and purple rays|gauntlet holding blue orb|narrow gold-silver staff|gold cross with blue gem
|brown breastplate|round wooden shield|dark purple reaching claw|pale grinning skull|black gauntlet and orange sparks|two pale bandages with gold emblems|face with glowing pale eyes|gold head profile
|gold-silver horned helmet|red shield in gold frame|open pale book with green glow|closed green book and gold mark|hooded face and blue forehead jewel|round brown mask with glowing eyes|purple-gold winged emblem|green-red radial flower emblem''', 'Framed radiant/action illustration: ')
add('prepared-items-native-32-batch2-21-pirate', '''curved hook with gold motion|upright fish hook|curved pale hook|pointed silver blade|three silver blades|large curved silver blade|brown eye with black patch|dark face and red-green head wrap
|hand gripping curved blade|fist holding dagger|bloodied curved blade|curved hook beside hand|hook with purple web|brown boot with green horizontal trails|hand holding dark dagger|curved axe-like blade
|figure with red bandanna and raised arm|dark figure standing on ship bow|crossed hook and blade|two curved white arcs|round black bomb with fuse|four black cannonballs|lit fuse near dark bomb|brown clenched fist
|weapon and small cannonballs|dark cannon barrel|two narrow dark blades|brown arm and pointing blade|dark wooden doorway|brown hat beside wooden doorway|stacked wooden barrels|ship on blue sea
|black bomb and pink sparks|black ball and pale chain|hand scattering coloured beads|red-black cloaked figure with curved blade|dagger pinned into wooden post|open hand wearing green-red wristband|ship silhouette and gold lights|hand gripping gold coin
|crossed pale blades|two linked black balls in curved motion|tan map with coloured islands|hands gripping rope|ship on blue waves|dark broad hat with gold trim|brown hooded face|green bottle and pale smoke curl''', 'Framed nautical/action illustration: ')
add('prepared-items-native-32-batch2-22-undead-loot', '''pale narrow skull and brown twig|bound bundle of pale bones|red book with gold circle|blue quill and dark ink pot|black pouch spilling pale pieces|orange round pendant on cord|gold ring with blue oval gem|red heart-shaped flesh
|black-red eye mask|small orange lantern|dark hooked staff with red cord|broken dark staff pieces|brown pouch with dark patch|silver dagger|dark spider|pale skull pierced by blade
|gold band with green-red gem|pale written scroll|two grey stones and cord|red-handled silver dagger|brown glove|orange book pierced by blade|gold key|three pale eyeballs with red pupils
|two broken pale bones|small silver-red bottle and cap|red spiral meat-like oval|round purple potion flask|orange narrow vial|dark green square flask|red whip and dark grip|open dark round container
|brown plated shoulder piece|brown leather trousers|orange belt pouch|dark boots with gold toes|bundle of arrows|long orange feather|torn parchment with red hand mark|pair of brown curved horns
|silver bowl|green leafy twig|pale skull with brown band|red clawed glove|brown feather|two dark round red-eyed objects|three pale fangs|pink webbed wing''', 'Isolated object/material icon: ')
add('prepared-items-native-32-batch2-23-fairy', '''brown spoon-like wand and green sparks|green sprout above dark leaves|two hands lifting leafy sprig|hand scattering green leaves|tree roots gripping ground|bare foot above blue water|gold frog in pale ring|red-orange face mask and green aura
|hand lifting pink-gold trinket|small flying winged figure|hand holding thin wand|upright gold-violet ornament over green circle|humanoid outlined against red lattice|two hands and blue butterfly|hand holding green speckled sphere|purple smiling face beneath green hood
|small robed figure raising staff|orange curled trail|small green winged humanoid|hands holding blue gem and orange cloth|brown-green face with gold eyes|green orb in pale-gold frame|green-gold angular butterfly|hand tracing red diamond
|two hands holding blue container|two hands around blue floating pieces|green-hooded figure holding purple object|pale hand reaching toward green sprig|green hooded figure in pale ring|two hands around purple folded object|hands and pale gold ribbons|green hooded figure in pink circle
|side-facing crowned figure|pale scroll with purple gem|red-green arrow in blue clouds|crossed staffs and pale-blue lightning-like branch|colourful berry/leaf mask|small green creature beneath rain streaks|orange butterfly perched on green plant|hand holding small gold-blue crown
|green-haired figure holding blue-pink crystal|finger held to lips|small winged figure with green curved tail|pointed-ear face with dark blue hair|brown reaching hand|green four-leaf clover|blue butterfly in green ring|red round fruit with dark stem''', 'Framed woodland/action illustration: ')
add('prepared-items-native-32-batch2-24-fairy-loot', '''gold-brown winged insect|two red white-spotted mushrooms|purple flower|red berry cluster|gold ring and orange flower|silver ring with green round gem|dark twig with red berries|brown pine cone
|multicolour berries|blue shoes|red-gold fingerless gloves|pale gloves with brown cuffs|small wooden holder with blue-gold gems|brown patterned forearm cuff|brown curved boot|pink flower with green leaves
|green watering can|red rose|silver ring with pink gems|purple orb in green leaves|small looped gold-white chain|orange-blue round pendant|pink curled pendant|cyan oval pendant with dark centre
|pale rectangular vial with orange-purple contents|pale flask with pink-blue butterfly|gold book with leaf mark|purple curved bow|broken brown bow|dark blue staff with pink gem|dark curved staff with red dots|orange ribbon
|dark twig with red-green berries|blue-white feather|blue flower on curved stem|purple petals|orange root with green shoot|tied green stalks|bare brown root|brown pouch with green stripe
|brown sheet with green leaves|blue-purple dust cloud|tied pale rolled scroll|gold star-tipped wand|green leaf-shaped vial with blue contents|purple-blue round flask|pale paired curved flasks|multicolour flower sphere''', 'Isolated woodland object/material: ')

add('prepared-items-native-32-batch2-25-demon-magic', '''red orb in dark spiral|orange sphere over reflection|dark face and green dotted curls|dark silhouette and pale fan of rays|black pointing hand|brown webbed wing|large orange flame|orange falling fireball
|black clawed hand|dark torso with two gold cross marks|black humanoid raising arms|black fist surrounded by flame|pink vertical light over red circle|black winged shape with orange heart|dark figure in blue rays|long pale blade
|three pale blades|two hands around orange orb|pale skull and jaw|dark skull beneath red cage|outstretched hand and orange glow|pale hand with red palm mark|brown boot|metal boot beside red down arrows
|dark hooded figure with purple wisps|upright black trident|pale skull above orange flames|gold zigzag inside dark oval|dark claw and pale crescent|dark staff crossing pale blue orb|pale hand and orange lightning-like line|red-eyed side-facing head
|orange spiral eye close-up|blue shield|small dark fragments and pink columns|hourglass with purple sand|green concentric eye mark|orange open mouth and purple droplets|winged gold sword emblem|orange mushroom-shaped blast
|silver knife and pink rays|brown shield and small dark gem|dark mouth surrounded by pale spikes|dark blue hooded figure|pale hands and dark bat|green bubbling puddle|robed figure holding dark bird|pale ghost silhouette with trailing roots''', 'Framed dark effect/action illustration: ')
add('prepared-items-native-32-batch2-26-blood-mage', '''red liquid drops and ripple|dark robed silhouette|pale hand with red fingertips|red heart and radial rays|raised dark fist|small green-red paired symbols|face with red glowing eyes|hand and thin red lines
|two hands cupping red mass|three gold-red standing marks|dark hooded head|three red scratches|hooded figure and red circular trail|dark humanoid struck by pale red beam|dark silhouette with red branching lines|red humanoid silhouette
|hooded figure and red circular arc|red spiral wave|pale forearm with red line|three steel cups with red curling trails|red coiled orb and green rings|two hands below red crystal|dark hood over pale red-marked terrain|hooded face behind red-gold streaks
|small figure in pink lattice|dark humanoid in pale red circle|hand cupping dark hood|red footprints between trees|dark silhouette reaching upward|pale fist inside red curved line|dark torso with red heart|steel chalice inside red-blue rings
|gold-brown skull and purple ripples|red bat-like silhouette|upraised red hand|hooded figure and pale red branching tendrils|hooded figure gripping dark staff|hooded figure among green strands|hand emitting thin red beam|hands supporting orange circular disc
|dark hunched figure and orange rays|pale scroll surrounded by red coils|red open hand|dark torso and red star mark|dark hooded figure and red coils|upright sword and red-blue trails|pale skull with orange rays|red reaching hand and pale trailing form''', 'Framed red effect/action illustration: ')
add('prepared-items-native-32-batch2-27-dwarf', '''bearded figure with raised arms|broad silver axe|red cylinder with green fuse|bearded helmeted figure|crossed sword and axe|pile of brown skull-like rocks|orange figure and blue axe|pale scroll with red markings
|pale blue humanoid silhouette|round gold shield with red centre|bearded face beside brown pipe|blade striking brown wooden planks|pale blue spiral blast|falling dark stones|orange-bearded helmeted figure|blue clenched gauntlet
|brown arm bracer and pale axe|hand gripping dark tool|small running orange figure|hands resting on sword pommel|row of raised weapons|sword pointing downward|broad blue axe and red arcs|pale bag-like object and orange rays
|small dark bottle and gold splashes|hands holding foaming cups|curved pale hook|brown hammer beside orange beard|flaming torch|round brown shield and silver curved blade|helmeted bearded face in gold circle|pink axe-shaped energy outline
|bald bearded figure holding blue crystals|brown hammer striking orange surface|hooded orange-bearded face|silver curved blade in blue motion|bald bearded face|bearded figure holding brown cup|red-orange anatomical heart|dark tornado funnel
|bearded figure holding weapon|boot trampling chain|hand holding pale bag and green leaf|two raised fists and gold rays|dark mallet hitting wooden planks|blue round toothed ring|orange roaring face|small dark object above turquoise ground ring''', 'Framed bearded-character/action illustration: ')
add('prepared-items-native-32-batch2-28-dwarf-loot', '''curved orange pipe with green band|brown banded hammer|brown shield with side blades|green bottle and pale label|brown pointed beard-like tuft|cut brown log|green-blue feather|pair of pale teeth
|silver double axe|silver pickaxe|orange lantern|brown bowl of pale tools|brown fur-topped boots|two pale wooden planks|purple-blue flower brooch|tied pale scroll
|silver broad axe|small brown axe|pile of gold ingots|gold ring with purple gem|brown fur-trimmed glove|brown smoking pipe|steel helmet|dark red-blue book
|silver sword with gold guard|woven basket|green flask with pale cloth top|brown belt pouch|blue metal pouch/container|open pale book|dark fur-trimmed headwear|brown-green leather torso
|dark double axe|multicolour faceted stones|sliced pale loaf|orange bead necklace with green drop|brown folded cloth with pale edge|three pale bones|coil of pale rope|dark goggles with blue lenses
|brown curved bow|dark brown pouch|brown mug with pale foam|brown fur-lined cap|blue round pendant on cord|bundle of silver nails|pink coiled cord|woven basket with yellow-green fruit''', 'Isolated object/material icon: ')
add('prepared-items-native-32-batch2-29-engineering', '''metal wrench and dark bolt|metal rings and wire loop|brown tongs over pale plate|silver saw cutting wood|metal tongs near orange sparks|long pointed steel tool|round pale magnifier|wooden mallet on plank
|brown banded barrel|pale plan sheet and gear|steel glove|pale bulb in dark housing|green pouch holding tools|dark curved metal shell|gold looped mechanism|hand holding dark gear
|bent wooden handle and steel shaft|round dial and gold circular arrows|round dial and blue circular arrows|metal keys on ring|small red-roofed house|pale glowing bottle|purple conical object in gold frame|hand gripping dark tool
|round dark bomb|wooden crate|gold cylinder with dark top|green angular breastplate|brown control box with dark dial|grey bucket|wooden box wrapped in metal band|molten orange material in dark vessel
|brown stump with metal tool|gold ring with blue pane|pale blue mechanical torso|orange-lit furnace|upright metal chain|purple ladder-like strip|gold barred curved object|hand holding red cylindrical vial
|two palms pressed together|hand holding dark tool|small mechanical device on stand|tan plan with red diagrams|brown curved board|multicolour circuit-like square|coloured pipes with gauges|dark purple oval portal''', 'Framed tool/mechanism illustration: ')
add('prepared-items-native-32-batch2-30-goblins-orcs', '''green arrow in cracked grey surface|large brown mallet|green hooded figure beside brown shield|boot stepping on dark rock|green hand and face close-up|crossed silver blades|green face with hands at forehead|small red projectile
|gaping green fanged mouth|spiked brown club|green face and raised axe|two brown fists side by side|dark bomb with orange flame|dark silhouette and red spiral|wooden defensive stakes|green projectile in gold motion
|blue metal spikes on wooden wall|green pointed-ear head with open mouth|dark running silhouette|green face beneath dark hood|three gold chain loops|green face under pale cloth|green shouting face with raised finger|small green creature and orange flame
|green raised fist|green face wearing dark goggles|black bomb and orange sparks|small dark figure behind icy lattice|brown hooded face in green aura|dark running animal silhouette|small green figure in doorway|dark silhouette behind red bars
|dark bomb with bright orange fuse|two hands and purple arc|pair of raised green boots|green hand holding flame|small humanoid silhouettes in purple blast|dark figure throwing curved blue object|green side-facing head beside plank|green crouched foot-like shape
|silver hooked blade|red spiked shield|crossed gold blades|green head and crossed silver blades|green figure holding long stick|green wrapped cylindrical object|dark eye-like face with yellow eyes|green head surrounded by pink bone ring''', 'Framed green-creature/action illustration: ')
add('prepared-items-native-32-batch2-31-exotic-weapons', '''three narrow metal blades with handles|brown spiked wrist cuff|round gold ring with four blades|dark sheathed straight sword|silver curved sickle|brown forked hand weapon|small dark hooked blade|two dark spiked balls on chain
|four-point throwing star|three dark spiked throwing balls|small brown curved bow|green axe with red hanging strap|silver crescent sickle|narrow silver-tipped dart|small dark scythe|curved silver dagger
|three thin dark pointed stars|pair of green crescent blades|fan of silver blades|round textured ball and cord|brown curved whip|brown narrow toothed blade|dark lock-like weight with loop|brown curved dagger
|silver three-prong hand blade|three dark rings with spikes|two spiked balls on chain|spiked brown club|looped studded whip|pair of brown leather gloves|gold wavy blade|dark curved cord and pointed handle
|pair of brown spiked cuffs|metal claw blades on leather cuff|thin curved sword with red tassel|brown blowpipe and small dart|spiked chain loops|green bottle with red cloth wick|small silver forked spear|dark broad trident
|green-brown pointed glove|short brown rope with wooden handles|brown hooked pick|large silver scissors|pair of brown wooden clogs|long dark striped blade|brown double mallet|dark rope loop and metal hook''', 'Isolated unusual weapon/tool: ')
add('prepared-items-native-32-batch2-32-lightning', '''purple branching lightning-like streaks|brown face and pale glowing eye|green handprint|gold figure surrounded by blue arcs|hooded figure holding golden glow|grey tornado above blue ground|dark-haired figure in yellow robe|pale small robed figure
|cyan radial burst|pale hooded face and orange rays|gold reaching hand|hand over pale blue gust|dark raised fist and green sparks|hooded face and gold aura|dark-haired face and gold rays|hand amid red branching lines
|three purple-blue falling drops|metal horned helmet and pink arcs|orange ribbed armour silhouette|pale scroll and blue arcs|gloved fist and white streaks|gold crossed weapons and red sparks|hanging hand and green trails|dark humanoid with pale head glow
|brown log in pale vortex|dark figure outlined in orange|green humanoid and pale lightning-like streak|closed book with pale blue gem|dark curved bow and purple orb|hooded figure holding blue rays|dark boot with gold glow|small gold helmet and purple arcs
|raised dark arm and gold rays|blue robed figure with purple arcs|raised fist and small stone debris|gripping hand and blue arcs|thin silver projectile and orange motion|flexed brown arm with pink arcs|silver-blue hand armour|crossed dark armoured limbs
|blue hanging metal cage|pink vertical rays above orange ground|gold ribbed torso amid purple arcs|small stone pedestal|upright gold-silver sword|hooded figure reaching through blue arcs|crossed green beams|two hands holding pale blue crystal''', 'Framed electric/action illustration: ')

add('prepared-items-native-32-batch2-33-summoner', '''pale torn scroll with orange circle|brown horned figure with pale outlined arms|small gold fox-like creature|dark fist with three red blades|open red mouth breathing flame|small creature wrapped in green coils|dark horned masked figure|upright red bone-like rods
|orange book and pale glow|steel helmet and red circular arcs|close-up cat-like face with green eyes|gold-red pendant with blue orb|short-haired face and green scarf|pale mask with dark blue eye openings|blue shield with red paw print|red-haired face and green collar
|dark humanoid silhouette with red squares|red horned creature within green ring|dark boot inside ground circle|four pale ghost heads|horned hooded figure with raised arms|brown bat with spread wings|small dark figure and green branching arcs|open pale hand
|three dark heads beneath orange arcs|green clawed creature beneath purple arc|green spiral shell|snarling brown canine head|red-blue angular winged emblem|figure cupping hand beside ear|brown boot beside dark ball|pale central orb in red geometric web
|shouting figure and raised fist|gold-armoured figure with orange trails|green insect-like creature|brown armoured creature in blue motion|yellow snake|dark wolf-like head in orange motion|five blue floating stones|hands around blue/pink floating stones
|pale horned skull-like shape and pink arcs|dark figure behind large horned head|green reptile head|dark hooded face inside pink ring|head in gold hood, pale green bottle|small robed figure in grassy circle|hunched figure in purple circular frame|dark many-legged insect with pale abdomen''', 'Framed creature/action illustration: ')
add('prepared-items-native-32-batch2-34-chaos-loot', '''pale skull with brown side horns|grey-red rough claw piece|pale teeth and fragments|gold segmented plate|grey hooked claw|green-red patterned cube|dark oval stone with red mark|green patterned curved fragment
|long pale bone|dark round stone with pale-gold pattern|red webbed wing|brown perforated mask|small dark spider with pale-marked abdomen|green-orange round patterned object|dark stone with red hand mark|pale long finger bones
|pale humanoid skull|red heart-shaped flesh|brown mound with blue eye|silver blade with gold guard|brown fan with red pointed tips|purple crystal cluster|brown sheet with cyan open-mouth emblem|brown bottle with pale stopper
|small horned skull|coiled brown plate|green pouch and dark ties|small dark spiked wand|orange glowing bent fragment|pale torn sheet with red emblem|silver narrow knife|purple round pendant on cord
|coiled red whip|brown curved horn|grey feather-like fan|orange fiery curl|green orb with pale teeth|red-brown and pale-blue mask|cyan-pink angular gem with red strands|brown organic piece with gold glow
|pink ridged ear-like fragment|two pale curved horns|small skull beneath black antlers|red fanged mask|silver double-ended dart with green centre|brown crown with blue orb and pale fangs|green-gold curved segmented piece|curved silver blade and red grip''', 'Isolated fantasy object/material: ')
add('prepared-items-native-32-batch2-35-kobold-loot', '''tan shoulder bag|brown backpack with green bedroll|gold tooth necklace|brown leather glove|silver pickaxe|broken silver pickaxe|silver spade|broken silver spade
|two pale bones|steel helmet with blue crest|goggles and orange cloth|green short tunic|black angular torso armour|brown hooked staff with hanging charms|broken dark hooked staff|brown bowl of blue stones
|pink crystals in grey rock|orange berry-like cluster|green crystal cluster|brown belt with silver buckle|brown cap with pale feather|orange lantern|curved gold blade|straight silver sword
|gold ingots and dark stones|brown rock pieces|tan pouch|necklace with pale fangs and purple charm|pale green folded cloth|dark pot of gold coins|brown floppy hat|cut brown log
|gold ring with green accent|pale band with pink and green accents|dark rock and blue drops|pale eye-shaped pendant|brown bowl and spoon|pale skull|purple ore chunks|small dark wooden chest
|pale folded map|bundle of pale bones|dark stone with blue veins|brown book with pale bone clasps|flaming torch|brown leather bracer|small dark bow|red pointed ear-like fragment''', 'Isolated object/material icon: ')
add('prepared-items-native-32-batch2-36-kobolds', '''red reptile-like figure with small blade|brown armoured reptile figure|brown figure with broad hat and blade|red figure in blue trousers|steel breastplate and red loincloth|dark hunched creature holding blade|green-orange crouching creature with blade|brown-red upright figure with spear
|brown figure with pale teeth and red vest|red reptile-like figure and purple object|dark figure in metal spider-like frame|red figure in pale skirt|pale figure with green tunic and knife|orange hooded crouching creature|bulky grey figure and purple cloth|brown figure with green tunic and red cap
|dark blue robed figure and thin wand|green-cloaked figure with staff|pale figure in red cape|orange figure with purple tunic|brown hatted figure in pink robe|dark masked figure with crossed blades|brown figure in orange tunic with sword|pale figure with green trousers and round bow
|green figure lifting double axe|orange figure with blue trousers and axe|dark horned creature holding blade|small creature above wooden base|red creature in silver plate armour|pale armoured creature|red-orange figure with round shield|brown figure in blue trousers
|red-brown figure and short blade|open toothed wooden chest|pale figure in orange scarf|small brown figure in red trousers|dark figure in blue-purple robe with hooked staff|low brown crouching creature with blade|grey creature with axe and red shield|shaggy gold round creature
|dark red robed figure with raised arms|blue-cloaked figure holding knife|orange-hooded figure with dark clothing|red-gold armoured creature with axe|brown barrel with two eyes|small brown figure behind silver shield|grey-green creature in brown robe|small pale face in blue hood and green-red lower garments''', 'Full-body creature/object sprite: ')
add('prepared-items-native-32-batch2-37-monk', '''raised fist and orange motion|robed figure in purple aura|bare torso in wide stance|pale figure with folded arms|pale figure arching backward|pale figure thrusting fist|open palm with spread fingers|hand gripping narrow blade
|bare torso leaning sideways|pale figure with raised hand|green-grey coiled snake|orange-grey tiger-like head|white bear-like face|green frog face|green outlined standing figure|two hands pressed together
|seated figure in gold aura|figure holding pale square tiles|dark bird head and pale beak|dark boot|round tan flask with green-red top|figure flying forward|dark leaping figure above tan sphere|green folded leaf-like form
|smoking round incense vessel|hands pressed in prayer|seated figure beneath orange ring|two side-facing human profiles|pale bald head profile|row of pale robed figures|standing robed figure and pale blue circle|grid of small bald faces
|three green geometric light symbols|pale round orb with gold aura|green face in orange flame|pale skull in blue-orange flames|bald face with green glowing eyes|bright small humanoid with gold rays|dark boot over green loops|seated figure in pink ground rings
|pale figure leaning into purple motion|two open hands and gold rays|closed purple book and blue flame|pale lotus-like flower|thin pale humanoid in blue rays|green leaf in purple circle|handful of upright pale sticks|hourglass beside bald face''', 'Framed martial/meditative illustration: ')
add('prepared-items-native-32-batch2-38-earth', '''hands holding gold orb|wooden log and round brown stone|dark egg-shaped stone and pink cracks|three falling stones beside green figure|tall brown rock spire|dark jagged rock and small figure|heavy dark stone above cracked ground|figure carrying green bundle
|green-robed figure and floating stones|hand above gold dust mound|brown outlined upright figure|green-robed figure before orange gate|round shield between two hands|hand reaching through pale-green aura|floating brown stones|grey whirling stone funnel
|ground split by glowing orange crack|dark mountains and small red figure|small figure on grey stone circle|stone shelf above green figure|brown root-like humanoid|small figure inside rock shelter|green figure surrounded by floating stones|dark round stone with green motion
|brown tree with pale outlined branches|green-clad figure and floating stones|figure walking up slanted stone|dark spikes bursting from ground|grey shield with pale cross|dark stone door and red-orange cracks|brown hooded face in blue aura|smoking volcano-like mound
|stones exploding outward|small figure holding two large stones|brown wooden humanoid|small green tree in purple rays|large gold curling wave|green-gold burst between stone walls|figure holding dark sphere|three stacked flat stones
|two round stones and gold rays|hands lifting large rocks above face|sharp dark rock spire|green hooded figure beside pale round stone|tree stump and green branching light|pale cube with dark round holes|upright sword and round shield|dark orb in orange-red swirling field''', 'Framed stone/nature/action illustration: ')

# Effect versions share the observed physical object, with separately observed
# visible particles. A glow does not establish a D&D damage type or enchantment.
for base, effect, groups in [
    ('prepared-items-native-32-36-swords', 'prepared-items-native-32-35-swords-effects', {
        'gold rays': [2,14,43], 'green droplets': [3,28,38], 'dark red curls': [4,29,46],
        'dark wisps': [6,22,31], 'blue droplets': [7,18,42], 'pale ice-like chunks': [8,35,47],
        'purple rays': [9,30,34], 'falling gold sparks': [10,27,39], 'turquoise sparks': [11,36,40],
        'orange flames': [17,23,48]}),
    ('prepared-items-native-32-batch2-11-arrows', 'prepared-items-native-32-batch2-12-arrows-effects', {
        'dark wisps': [1,29], 'dark red curls': [2,34], 'pale ice-like chunks': [5,31],
        'orange flames': [6,38], 'purple rays': [8,36], 'turquoise sparks': [10,27],
        'blue droplets': [14,25], 'falling gold sparks': [15,43], 'gold zigzags': [17,40],
        'green droplets': [19,41], 'small green dots': [21,45], 'orange starbursts': [22,46],
        'small red hearts': [23,47]}),
    ('prepared-items-native-32-batch2-13-spears', 'prepared-items-native-32-batch2-14-spears-effects', {
        'falling gold sparks': [1,5,27], 'gold zigzags': [6,16,39], 'turquoise sparks': [7,17,36],
        'orange flames': [8,10,47], 'blue droplets': [11,32,35], 'green droplets': [12,19,30],
        'pale ice-like chunks': [15,22,44], 'dark red curls': [20,34,45],
        'dark wisps': [23,38,48], 'purple rays': [24,25,40]}),
    ('prepared-items-native-32-batch2-02-axes', 'prepared-items-native-32-batch2-03-axes-effects', {
        'pale ice-like chunks': [1,45,48], 'turquoise sparks': [2,9,23], 'gold zigzags': [3,11,40],
        'orange flames': [4,24,33], 'dark wisps': [5,15,27], 'green droplets': [8,12,47],
        'dark red curls': [10,29,46], 'purple rays': [16,28,39], 'falling gold sparks': [26,35,44],
        'blue droplets': [30,36,43]}),
    ('prepared-items-native-32-batch2-07-staves', 'prepared-items-native-32-batch2-08-staves-effects', {
        'dark red curls': [1,24,34], 'gold zigzags': [3,14,27], 'falling gold sparks': [5,23,41],
        'purple rays': [7,37,42], 'orange flames': [8,32,44], 'green droplets': [9,18,40],
        'blue droplets': [10,20,25], 'dark wisps': [12,28,47], 'pale ice-like chunks': [16,21,35],
        'turquoise sparks': [30,38,45]}),
    ('prepared-items-native-32-40-staves', 'prepared-items-native-32-39-staves-effects', {
        'dark wisps': [1,7,11], 'pale ice-like chunks': [2,26,35], 'gold zigzags': [3,29,46],
        'orange flames': [4,16,41], 'purple rays': [5,18,45], 'green droplets': [6,14,32],
        'turquoise sparks': [9,24,34], 'falling gold sparks': [10,13,47],
        'blue droplets': [15,30,37], 'dark red curls': [17,36]})]:
    effects = {n: label for label, numbers in groups.items() for n in numbers}
    for n in range(1,49):
        caption = CAPTIONS[f'{base}:icon-{n:03d}'].rstrip('.')
        CAPTIONS[f'{effect}:icon-{n:03d}'] = caption + ('; surrounded by ' + effects[n] if n in effects else '; no surrounding particle effect visible') + '.'


def main():
    assets = json.loads((LIBRARY / 'prepared-assets.json').read_text(encoding='utf-8'))
    annotations = {}
    for asset in assets:
        caption = CAPTIONS.get(asset['id'])
        if caption:
            source = LIBRARY / 'Sources' / asset['pack'] / asset['provenance']['member']
            source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
            if source_hash != asset['sha256']:
                raise ValueError(f'Reviewed PNG changed; inspect it again before updating its fingerprint: {source}')
            annotations[asset['id']] = dict(descriptionEn=caption, sha256=source_hash,
                reviewMethod='individual_numbered_contact_sheet_96px', semanticStatus='visually_described')
    target = LIBRARY / 'prepared-annotations.json'
    previous = json.loads(target.read_text(encoding='utf-8')) if target.exists() else {}
    previous.update(annotations)
    temporary = target.with_suffix('.json.new')
    temporary.write_text(json.dumps(previous, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(target)
    print(f'Observed captions: {len(previous)} / {len(assets)}')


if __name__ == '__main__':
    main()
