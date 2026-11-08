"""Persist observations made by viewing the imported contact sheets.

These are visual descriptions, not D&D mechanics, spell names or automatic art
approval. A green liquid is not automatically healing/poison; green minerals
are not automatically emeralds. Unknown identities stay unknown.
"""
import json
import ast
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
annotations={}

def put(pack, visual_type, text, expected=48):
    labels=[line.strip() for line in text.strip().splitlines() if line.strip()]
    assert len(labels)==expected,(pack,len(labels),expected)
    entries={}
    for i,label in enumerate(labels,1):
        entries[str(i)]=dict(descriptionEn=label.rstrip('.')+'.',
            traits=dict(visualType=visual_type),
            tags=list(dict.fromkeys(re.findall(r'[a-z]+',label.lower()))),
            reviewMethod='visual_contact_sheet_96px',
            mechanicalMeaning=None)
    annotations[pack]=entries

put('craftpix-128598','potion_bottle','''
Slender cylindrical vial with red liquid
Slender cylindrical vial with yellow liquid
Round-bottom flask with green liquid
Dark red long-necked bottle
Slender tapered vial with blue liquid
Slender dark gray pointed bottle
Round-bottom flask with bright green liquid
Pale cyan bottle with a rounded lower bulb
Round flask with blue liquid
Angular flask with deep blue liquid
Pointed magenta bottle
Round purple bottle with a dark central marking
Curved teardrop flask with orange liquid
Tall slender dark red bottle
Tall purple bottle with a rounded shoulder
Angular narrow flask with yellow liquid
Twisted double-bulb flask with amber liquid
Tall orange cylindrical bottle
Square flask with green liquid
Red curved bottle with side fins
Round flask with a red pointed base
Narrow hourglass flask with green liquid
Red liquid in a curved horn-shaped flask
Narrow diamond-shaped blue flask
Round golden-yellow flask with a slim neck
Tall thin dark blue flask with a round base
Dark blue tapered flask
Faceted deep blue bottle
Small luminous pale green bulb-shaped flask
Small faceted amber flask
Round brown-orange bottle
Teal bottle with a flared triangular base
Flat round flask with red liquid
Bright green bottle with a broad flat base
Faceted orange-red bottle
Blue bottle with a broad angular shoulder
Long narrow tapered teal vial
Long narrow tapered orange vial
Diamond-shaped pale purple bottle
Diamond-shaped violet bottle
Pale blue flask with a long pointed bottom
Pale purple flask with a long pointed bottom
Round pink flask with a broad bottom
Faceted pink flask with a central glass ridge
Round dark green bottle
Dark green bottle with a central bright highlight
Round golden-brown bottle
Golden-brown bottle with a broad vertical highlight
''')

put('craftpix-253495','pigment_bottle','''
Round glass bottle of red pigment
Round glass bottle of yellow pigment
Round glass bottle of blue pigment
Round glass bottle of black pigment
Round glass bottle of pale gray pigment
Round glass bottle of dark blue-gray pigment
Green hourglass-shaped pigment bottle
Dark green hourglass-shaped pigment bottle
Teal hourglass-shaped pigment bottle
Angular orange pigment bottle
Angular purple pigment bottle
Angular pink pigment bottle
Tall cylindrical red pigment bottle
Tall cylindrical yellow pigment bottle
Tall cylindrical blue pigment bottle
Tall cylindrical black pigment bottle
Tall cylindrical pale gray pigment bottle
Tall cylindrical dark gray pigment bottle
Tall green pigment bottle with a narrow waist
Tall dark green pigment bottle with a narrow waist
Tall teal pigment bottle with a narrow waist
Broad angular orange pigment bottle
Broad angular purple pigment bottle
Broad angular pink pigment bottle
Round red bottle wrapped in a tan band
Round yellow bottle wrapped in a tan band
Round blue bottle wrapped in a tan band
Round black bottle wrapped in a tan band
Round gray bottle wrapped in a tan band
Round dark gray bottle wrapped in a tan band
Green bottle wrapped in a tan band
Dark green bottle wrapped in a tan band
Teal bottle wrapped in a tan band
Angular orange bottle wrapped in a tan band
Angular purple bottle wrapped in a tan band
Angular pink bottle wrapped in a tan band
Yellow bottle with a patterned glass body
Amber bottle with a side glass handle
Pale blue narrow-waisted bottle
Blue glass bottle with a circular yellow side detail
Orange bottle with a wide fluted base
Green bottle with a broad shoulder
Dark blue bottle with a curved elongated body
Purple bottle with a long fluted base
Short dark red bottle wrapped in a tan band
Bright orange ribbed bottle with a green neck
Green round bottle wrapped in a broad gold band
Dark red ribbed bottle with a broad gold band
''')

put('craftpix-688754','mineral','''
Long jagged pale blue crystal cluster
Slender pale green mineral shard
Large orange-red stone with dark green edges
Dark gray-green angular mineral block
Small cluster of round purple stones
Curved gold-colored mineral fragment
Orange-yellow rounded stone fragment
Long tapered teal crystal cluster
Small pile of orange-red dark-edged stones
Cluster of tall gold-colored spikes
Flat elongated golden mineral with purple bands
Rough cluster of orange-gold crystals
Tall purple crystal cluster
Tall dark teal crystal cluster
Pile of orange-gold angular stones
Bright purple crystal cluster
Dark angular mineral with orange speckles
Curved red and gray mineral spikes
Gray-blue jagged crystal fragments
Dark rock with bright cyan patches
Long red-pink crystal shards
Rounded green stone with bright angular facets
Several long green-gold crystals
Dark spiraled mineral with pale bands
Long orange-red pointed crystal
Slender golden pointed crystal
Golden angular stone with a bright central face
Faceted red-pink pointed gemstone
Rounded lime-green stone with dark edges
Purple star-shaped crystal cluster
Dark green crystal cluster with bright ridges
Flat gold-green mineral shards
Three deep blue faceted stones
Orange-red layered angular mineral
Tall orange and green striped mineral
Rough green block with lime highlights
Tall cyan crystal rods with gold-colored edges
Dark green crystal rods with gold-colored speckles
Purple and gold-colored crystal cluster
Blue-green cluster with brown-orange central bands
Rounded teal-green mineral with a tiled surface
Deep blue star-shaped crystal cluster
Tall pale blue-white jagged crystals
Long flat gray-purple crystal shards
Pile of pink faceted stones
Pile of cyan-blue faceted stones
Dark gray flat stones with green edges
Brown-orange curled mineral fragments
''')

put('craftpix-883583','shield_or_amulet','''
Round brown wooden shield with a gray rim
Round tan shield with a dark metal boss
Round gold-brown shield with diagonal bands
Round lime-green shield with a dark central boss
Round purple shield with a bright central disc
Brown round shield with red vertical markings
Brown kite shield with a central silver spike
Dark green angular shield with four gold-colored studs
Narrow brown kite shield with a central ridge
Gold-colored round shield with diagonal ridges
Round blue shield with a silver boss
Winged silver shield with an orange central gem
Tall green rectangular shield with a central stripe
Tall red rectangular shield with gold-colored edges
Brown angular shield with a bright vertical stripe
Tall blue faceted shield
Blue-red quartered shield with a gold-colored boss
Red shield with a golden cross-shaped ornament
Dark blue shield with a golden branching ornament
Dark brown shield with an orange branching ornament
Silver-edged shield with a green tree motif
Black pointed shield with vertical dark ridges
Blue shield with a pale geometric marking
Dark shield with a gray skull-like face
Silver kite shield with a black central slit
Green kite shield with a small cyan-gold ornament
Bright green narrow shield with a central ridge
Red kite shield with a golden circular boss
Dark purple pointed shield with a cyan central gem
Pale cyan angular shield with a central diamond
Tall red shield with a dark upper ornament
Long black-red shield with angular silver tips
Long purple shield with pale chevron bands
Blue and orange cross-shaped shield ornament
Black and gold-colored pointed shield with a pale central face
Long black-red shield with a pale red central motif
Dark diamond pendant with a green central gem on a chain
Golden pendant with an orange-red circular center
Gold-colored branching pendant with a small red gem
Purple three-pronged pendant with a long pink gem
Red heart-shaped pendant with a gold-colored top
Green and gold-colored star-shaped pendant
Blue winged pendant with a pale central gem
Gold-colored cross-shaped pendant with a blue central gem
Teal winged pendant with an orange tapered bottom
Gold-colored pendant with green hanging points
Red faceted pointed pendant on a dark chain
Gold-colored angular pendant with an orange central gem
''')

put('craftpix-780987','organic_material','''
Orange spiked scale-like fragment
Dark curled hide or shell fragment
Rough golden scale-like fragment
Brown branching star-shaped fragment
Flat oval blue scale-like fragment
Flat oval pale green scale-like fragment
Single long curved pale horn
Cluster of several pale curved horns
Long pale yellow fang
Dark jaw-like piece with pale yellow fangs
Long spiral brown horn
Cluster of brown conical horns
Two flat green scale-like fragments
Cluster of small bright cyan rounded stones
Round yellow-green patterned scale-like fragment
Long blue feather
Purple jagged crystal-like fragments
Thin golden branching spines
Curled silver band-like shell fragment
Cluster of rough silver-gray fragments
Long pale curved tusk with a brown base
Two pale curved tusks
Pale yellow angular bone fragment
Branched pale yellow antler fragment
Dark brown oval scale-like fragment
Brown curled piece with a pale textured surface
Cluster of short gold-colored pointed fragments
Orange spiked star-shaped fragment
Flat green piece with a dark rounded center
Brown base with three red spikes
Thin pale curved U-shaped fragment
White layered spiral shell-like fragment
Long gold-colored curved fang
Dark jaw-like piece with pale gray fangs
Tall pale yellow pointed fragment with a green center
Three pale yellow pointed fragments
Rounded cream-and-brown scale-like piece
Cluster of long lime-green spikes
Pale gold patterned scale-like fragments
Long red feather
Long orange-red tapered crystal-like fragment
Cluster of short red-brown spines
Curved brown segmented horn
Spiral brown horn with a curved tip
Pale branching antler
Cluster of pale branching antlers
Long pale yellow hollow bone
Two broad pale yellow bone-like pieces
''')

put('craftpix-986554','loot_item','''
Pale humanoid skull
Single long pale bone
Pale lower jawbone
Branched pale bone
Skeletal hand with long finger bones
Broken pale bone in two pieces
Pale rib cage
Large pale tooth-like stone and a smaller fragment
Long brown fang with a pale top
Short hollow bone with a brown side
Pale animal-like skull with an open toothed jaw
Red anatomical heart
Two eyeballs with blue and green irises
Pink brain
Dark purple forearm ending in a skeletal hand
Dark purple foot or boot-shaped severed limb
Dark purple hand wearing a green-stoned ring
Blue gemstone on a thin dark necklace
Silver ring with a large purple stone
Golden crown with orange-red points
Round glass flask with green liquid
Brown coin pouch with a gold-colored coin
Brown star-shaped fragment
Long yellowish bone-like fragment
Purple book with a pale skull on its cover
Necklace of pale teeth on a brown cord
Pair of long black gloves
Long cyan crystal
Large faceted red gemstone
Pale pendant on a long black cord
Silver knife with a brown handle
Small gray hammer with a brown handle
Pale skull with a short red candle
Group of dark and red candles
Unrolled pale scroll with dark writing
Small red-handled knife with a pale blade
Orange segmented insect-like body
Small brown humanoid doll
Green diamond-shaped case with a blue oval base
Dark bowl holding an orange-red pile
Long dark gray key
Closed pale envelope with a red seal
Partly unrolled golden scroll with dark writing
Dark horned helmet
Brown glove with long fingers
Brown boot with a dark upper opening
Thin arrow with a pale head and purple fletching
Pale antlered animal skull
Brown branching root with green shoots
Long pale fang with a red tip on a thin cord
''',50)

put('craftpix-996288','ranged_weapon','''
Teal angular bow surrounded by green sparkles
Golden bow surrounded by yellow sparkles
Dark purple bow with a violet radiating aura
Red curved bow with red wisps
Dark curved bow with small gold-colored fittings
Brown bow with a bright golden burst
Brown bow with red accents
Wooden bow wrapped in green vines
Dark green curved bow
Gray-blue bow with a tan grip
Red-brown curved bow
Purple curled bow with a violet burst
Dark blue bow with black swirling wisps
Orange-gold bow with a fiery-looking aura
Gold-colored bow with descending yellow sparkles
Teal bow with descending green sparkles
Dark teal curved bow with a brown grip
Dark blue bow with an angular upper tip
Golden bow with blue floating particles
Pale blue bow with white crystal-like fragments
Brown bow with curling green vines
Red-brown bow with a bright yellow burst
Pale gray-blue bow with black swirling wisps
Brown bow with a red outer edge
Small teal crossbow with a green aura
Small wooden crossbow with yellow sparkles
Wooden crossbow wrapped in green vines
Small curved green crossbow
Gray-blue crossbow with white crystal-like fragments
Red crossbow with red wisps
Small brown crossbow with a tan grip
Teal crossbow with blue floating particles
Gold-brown crossbow with a pale central ornament
Small dark green crossbow
Golden crossbow with a curved bow assembly
Dark purple crossbow with a curled violet upper ornament
Dark crossbow with black swirling wisps
Orange-gold crossbow with a fiery-looking aura
Dark green crossbow with a bright teal central stripe
Brown-red crossbow with a bright yellow burst
Brown-red small crossbow
Pale brown crossbow with a long grip
Red crossbow surrounded by dark red wisps
Gold-orange crossbow with a fiery-looking aura
Purple crossbow with a violet radiating aura
Gray-blue crossbow with white crystal-like fragments
Black-teal crossbow with blue floating particles
Red crossbow with a long pale bolt
''')

put('craftpix-459799','creature','''
Walking dark mushroom with a red spotted cap and pale legs
Brown branching plant creature with orange-red buds
Cream-faced mushroom creature with a red spotted cap and pale beard
Tall dark mushroom creature with a pink cap
Brown-shelled snail with pale body and blue eye stalks
Purple round one-eyed mushroom creature with a red cap
Green long-necked mushroom creature with a brown cap and open mouth
Cluster of orange-capped mushrooms with thin blue legs
Round red spotted mushroom creature with dark legs
Dark snail-like creature under a large orange spotted cap
Coiled green plant-like creature with a dark open mouth
Thin twisting plant creature with a reddish cap and small green leaves
Purple-and-blue star-shaped flower on a green stem
Dark four-legged creature with a red-orange pointed head
Yellow bulb-headed plant creature with broad green leaves
Round orange bulb-headed creature with thin legs and spiked leaves
Small yellow plant creature with upright green shoots
Orange-and-blue flower face on a green leafy stem
Blue flower-headed creature on thin dark roots
Small pink-red flower face with dark green leaves
Round bright green jelly-like creature with a dark tool beside it
Tall dark green jelly-like creature with reddish eye details
Cluster of pale cyan rounded jelly-like bodies
Green jelly-like creature with raised thin arms
Pale blue jelly-like figure with long dripping limbs
Yellow spiral jelly-like creature with a red berry-like object on top
Pale green rectangular jelly-like creature with red eye details
Dark green jelly-like creature with a large pink mouth
Cluster of small green slime-like bodies
Yellow-green pear-shaped slime-like creature with a pink mouth
Small dark brown rat in side view
Dark winged creature with purple spikes and pale front feet
Brown bat with large upright wings
Red-brown bat with spread dark wings
Dark beetle-like insect with red antennae
Round red-and-black beetle with pale front mouthparts
Red scorpion-like creature with a raised curved tail
Dark brown bat with angular spread wings
Green mantis-like insect with orange-red joints
Cluster of orange mushroom caps on a dark green stalk
Round cream-and-red spotted insect with long thin legs
Yellow-and-black flying insect with pale wings and a long pointed tail
Dark red-brown rat with large pointed ears
Small white-bearded figure in a red pointed cap carrying a fork
Orange jack-o-lantern creature with green leafy feet
Brown squirrel-like animal with a pale belly
Cream flying insect with large pale wings and red eye details
Golden moth-like insect with broad patterned wings
Large red-brown ant with long antennae
Purple spider-like creature with a pale upper body
''',50)

put('craftpix-737210','creature','''
Brown armored insect-like creature with an orange light on top
Pale four-limbed bony creature with long brown extensions
Twisted pale gray worm-like creature
Dark multi-legged creature with a golden face
Round dark blue horned beetle-like creature
Pale blue-white crawling creature with large folded front limbs
Brown long-bodied creature with a red crest and green facial details
Blue-purple crawling creature with a pointed head
Green slime-like creature with a dark orange-lit head
Red winged four-legged beast
Red segmented winged insect-like creature
Thin brown tree-like humanoid with a yellow face
Coiled golden snake-like creature with a purple side marking
Purple hanging winged creature with a pale upper ornament
Brown rocky orb covered in bright golden eye-like circles
Orange-red flaming orb above a small pale skull-like base
Brown upright creature with a large yellow-green eye
Hunched brown creature with a pale rounded head
Pink-orange winged insect-like creature with a wide toothed face
Green-brown slime-like creature with broad horns and golden eye details
Red rounded blob covered in multiple yellow eyes
Small red winged imp-like creature
Dark teal round creature with yellow eyes and pale feet
Dark blue upright creature with orange horns and a pale central stripe
Brown multi-legged crawling creature with a red-green head
Pale horned humanoid creature with raised arms
Dark long-tailed feline-like beast with an orange facial marking
Orange-red long-tailed creature with a pale face
Pale branching coral-like creature with reddish tips
Dark brown animal with a pale antlered skull-like head
Dark purple round creature with a spiked dome and pale legs
Black-and-white upper body above a long red lower body
Gray-blue spider-like creature with a large rounded abdomen
Pale green long-legged insect-like creature
Upright brown reptile-like creature with a dark spiked crest
Orange tapered slug-like creature with a pale belly marking
Red muscular beast with a dark brown back
Golden armored insect-like creature with a pale central face
Orange-brown imp-like creature with horns and a pale belly
Dark round many-legged creature with a green-orange patterned shell
Green segmented creature with thin dangling limbs
Small flying creature with dark wings and a glowing green eye
Dark hunched winged creature with a pointed head
Pale rooted creature with orange-tipped spines and a dark teal central face
Small gold-colored many-limbed creature with a blue spiked upper body
Dark spider-like creature with a pale round patterned upper body
Bright orange flaming creature walking on two dark legs
Green-and-black scorpion with a long raised tail
Blue-and-gold armored creature with pointed shoulders
Gray upright horned creature with red head and hand details
''',50)

put('craftpix-305321','humanoid_face','''
Pointed-eared face with tied-up magenta hair and pink shoulder pieces
Pale pointed-eared face with a purple cap and long white moustache and beard
Tan pointed-eared face with swept teal-blue hair and a dark high collar
Tan pointed-eared face with short swept-back teal hair and a dark collar
Pale lined face with swept pale cyan hair and a teal high collar
Pale pointed-eared face with long messy blond hair and a blue collar
Brown pointed-eared face with long red hair and small blue ornaments
Pale pointed-eared face with dark red hair, thick pale moustache and red-gold collar
Dark pointed-eared face with short teal hair and angular blue shoulder pieces
Pale pointed-eared face with a pink bob haircut and orange collar
Brown pointed-eared face with dark blue hair and a green head garment
Long dark pointed-eared face with a pale pink striped crest and purple collar
Light pointed-eared face with swept-up green hair and an angular green collar
Olive pointed-eared face with a dark green bob haircut
Pale pointed-eared face with long dark red hair and a red high collar
Dark pointed-eared face with long blue hair and blue shoulder pieces
Pale pointed-eared profile with slick dark blue hair and purple facial markings
Light pointed-eared profile with orange-red flowers around greenish hair
Pale pointed-eared face with a green cap, purple cheek markings and purple collar
Light pointed-eared face with a tousled blond bob and small pale moustache
Pale pointed-eared face with a red beret, blue spectacles and a white moustache
Dark pointed-eared face with a pale cyan bob and orange-red neck ornament
Light pointed-eared face with gray-blue hair tied in a high bun
Pale pointed-eared face with orange braided hair, a broad grin and green clothing
Pale pointed-eared face with orange swept hair and dark eye covering
Pale pointed-eared profile with swept dark red hair and a pointed nose
Light pointed-eared face with blond hair tied up and a purple collar
Dark pointed-eared face with long purple hair and a blue collar
Pale pointed-eared face with pale blue hair, a spiked green crown and spectacles
Light pointed-eared face with orange twin hair buns, wide eyes and a red mouth
Pale pointed-eared face with a green beret, blond fringe and purple collar
Tan pointed-eared profile with long reddish-brown hair and an orange collar
Tan pointed-eared face with pale cyan hair and a small orange forehead ornament
Brown pointed-eared face with short swept red hair and pale earrings
Pale pointed-eared profile with long pale blue hair covering one side of the face
Dark pointed-eared face with green-blond hair and a purple collar
Light pointed-eared profile with blond hair, beard and a purple collar
Brown pointed-eared face with dark blue swept-back hair and a blue collar
Pale pointed-eared profile with orange tied-up hair and an angular blue collar
Pale pointed-eared face with a purple cap, long nose and green collar
Brown pointed-eared face with long pink hair and a broad smile
Pale pointed-eared face with short shaggy green hair
Pale pointed-eared face with dark red hair, moustache and beard
Tan pointed-eared face with spiked blue hair and a dark cheek marking
Pale pointed-eared face with a pink wrapped cap and purple collar
Dark pointed-eared profile framed by a long red hood
Light pointed-eared face with pale blue hair and a red headband
Dark pointed-eared face framed by a purple hood
Orange-brown pointed-eared face with teal hair and dark markings across the forehead
Light pointed-eared face with a green hood and a broad smile
''',50)

put('craftpix-945210','humanoid_face','''
Pale pointed-eared face with short pale blue-gray hair, long nose and burgundy collar
Pale freckled face with orange-red braided hair and blue clothing
Pale pointed-eared face with short curly blond hair and green clothing
Dark face with reddish curls, small blue hair bows and blue clothing
Brown pointed-eared face with dark gray hair, beard and a cream collar
Pale face with swept pink-orange hair and a blue high collar
Pale pointed-eared face with dark gray hair covering one eye and orange clothing
Tan pointed-eared face with a blue head wrap, pale moustache and yellow clothing
Pale pointed-eared face with short blond curls and purple clothing
Brown face with swept reddish-brown hair and green clothing
Dark face with short pale pink hair and a light collar
Pale pointed-eared face with short red hair and a dark high collar
Pale pointed-eared face with pale blue hair, a gold-colored headband and red clothing
Light face with orange hair, a pink flower ornament and pink clothing
Pale pointed-eared face with gray hair, a gold-colored forehead band and dark clothing
Dark face with long blond braids, green hair ornaments and green clothing
Brown pointed-eared face under a green head covering with a pale central stripe
Pale pointed-eared face with short gray hair, gray beard and blue clothing
Light pointed-eared profile with red tied-back hair, pale moustache and brown clothing
Tan face with long blond hair, a pink head wrap and pink clothing
Pale pointed-eared face with short dark red hair and a pale blue collar
Brown pointed-eared face with dark gray hair, beard and a purple-cream collar
Light pointed-eared face with red hair, a blue headband and dark blue clothing
Pale face with dark red hair covering one eye and purple clothing
Pale face with curly blond hair and dark red clothing
Tan pointed-eared face with pale blue hair, gold-colored headband and blue collar
Pale pointed-eared face with red hair and green-striped clothing
Brown face with dark hair, orange-gold head ornament and golden collar
Pale face with a pale blue bob haircut, purple headband and purple collar
Tan face with red hair, blue feather-like hair ornament and pink clothing
Dark face with loose pink-orange hair and a purple collar
Olive pointed-eared face with short dark hair and a cream-green neckline
Pale freckled face with short pink-orange hair and a dark blue collar
Pale pointed-eared face with short gray hair, gray beard and blue-orange clothing
Dark face with short reddish curls and a yellow-purple collar
Pale face with pale blue hair and beard, framed by a green hood
Dark pointed-eared face with a blue wrapped head covering and pale neckline
Tan pointed-eared face with gray tied-back hair and a pale yellow neck ornament
Pale face with short blond hair and pink clothing
Tan face with dark reddish facial hair framed by a teal hood
Pink-toned face with pale blue hair and blue-yellow shoulder clothing
Dark face with reddish curls, a gold-colored headband and purple-red clothing
Light face with long dark gray hair and blue clothing with a wide neckline
Tan pointed-eared profile with long pale blue hair tied back and green clothing
Pale face with pink-orange hair and beard, framed by pale blue clothing
Brown lined face with reddish-brown curls, beard and a pale yellow collar
Pale pointed-eared face with short gray hair and a pink collar
Pale freckled face with orange hair tied up and a dark blue collar
''')

put('craftpix-495817','combat_action_icon','''
Armored fist against an orange-gold burst
Dark helmet against golden rays
Small silver shield against an orange-gold burst
Silver sword against a red glow
Silver sword against a pink circular field
Upright silver blade with a dark crossguard on red
Dark armored boot against orange flames
Crossed armored weapons on a green field
Broken round shield with red fragments on cyan
Tall red-orange shield on a green field
Raised curved silver sword against golden rays
Armored figure with a raised fist and red cloak
Silver shield with pink radiating lines
Two crossed silver swords on purple
Close-up of a silver helmet against pale golden light
Bare fist meeting a pale blue impact shape
Sword crossing a red-orange shield on green
Skull and silver blade with red fragments
Armored fist striking beside a pale helmet on blue
Three parallel silver blades on pink
Blue shield with a dark face-like center on green
Silver sword above a green lower edge on blue
Dark boot kicking with pale green motion trails
Dark silhouette raising a fist against golden rays
Long silver sword with pale pink motion streaks
Dark helmet above crossed blades on gold
Armored fist gripping a horizontal silver blade
Bare fist meeting an armored hand on blue
Silver blade beside a brown gauntlet on green
Silver blade with a red center on purple
Armored figure with a large raised gauntlet
Upright silver sword against a purple glow
Broad silver blade with a dark edge on red
Two brown armored hands around a pale blue blade
Dark boot with pink motion streaks
Silver blade striking a dark shape on green
Silver blade beside a round metal piece on blue
Armored fist holding an upright sword on gold
Silver blade with small red droplets on dark red
Silver blade sweeping through pale debris on red
Broken silver blade and fragments on green
Several broken blade pieces on pink
Armored figure before a pale spectral-looking silhouette on purple
Curved red motion trail beside a silver blade
Silver shield surrounded by a purple circular trail
Brown gauntlet gripping a silver blade on cyan
Silver blade against a bright golden burst
Close-up of a sword crossguard and brown grip on purple
''')

put('craftpix-523115','dark_magic_icon','''
Dark hooded figure covering its face against golden light
Purple luminous spiral on a dark green field
Two dark gloved hands against blue vertical lines
Orange rectangular object with pale loops on blue
Green round flask with a pale skull-like marking on orange
Pale skeletal hand suspended from a dark red strap
Several small yellow-green glowing orbs on red
Dark blue coffin-like emblem on a pink field
Several red-fletched arrows on blue
Pale skull with an orange flaming side
Dark blue cloaked figure covering a pale face
Silver dagger with a brown grip on magenta
Bright red branching veins or roots on black
Skeletal hand with red dripping markings
Two crossed pale shafts with bright blue rays
Dark silhouette rising from a pale circular surface
Dark raven head against golden light
Black-headed tool with a brown handle on purple
Dark clawed hand wrapped in green glowing tendrils
Golden branching hand-like flame on blue
Clenched fist surrounded by a golden-purple glow
Dark green hooded shape on pale green
Dark blue armored head or mask with pale eye details
Pale web-like grid on a gray field
Silver sword beside a dark horned shape
Bright orange orb with blue branching rays
Close-up of a pale eye with an orange-red iris
Red-and-blue swirling form
Dark green hooded figure with a gold-colored curved edge
Black-headed tool striking a tan circular surface
Pale blue shield with a vertical green branching crack
Dark round open-ended object with purple motion trails
Close-up of narrowed reddish eyes
Dark green ribbed shape within a pale skeletal outline
Dark upright silhouette before purple rays and pale vertical shapes
Pale upright humanoid shape with spread arms and blue-white glow
Black bird-like silhouette with a pale skull and orange fragments
Dark smoky shape descending onto a pale yellow ground
Bright green drop-shaped orb on magenta
Round gray target-like form pierced by a pale arrow
Red-pink glowing handprint or claw-like emblem
Orange pointed object sweeping along a golden curved trail
Pale skull resting on a brown hand-like shape
Dark horned hooded silhouette against pink rays
Pale golden small humanoid figure on dark blue
Pale skull crowned by purple-and-gold ornaments
Blue-green skeletal humanoid shape with radiating blue lines
Dark hand reaching through a golden circular glow
Green vertical symbols above a golden ring
Dark central shape inside a purple web
''',50)

put('craftpix-909691','radiant_magic_icon','''
Bright golden starburst on a red field
Open raised hand amid golden particles
Clenched armored hand with a bright pale center on blue
Open hand beneath descending pale cyan lines
Armored hand sweeping through a golden curved trail
Clenched fist beneath descending green light
Orange pendant-like drop within a pink-gold oval glow
Close-up of pale golden overlapping scales
Blue-gray hammer head with pale blue central ornament
Armored hammer on a red diagonal glow
Dark armored arm sweeping through a green-gold arc
Tall pale-gold column of light
Red-orange spiral around a golden star
Pale golden multi-pointed radiant star on dark gray
Golden helmet with descending pale light
Large blue shield with pale cyan studs and gold-colored edges
Raised hand inside a pale golden circular field
Golden horned helmet or mask surrounded by rays
Golden central orb with blue-white branching rays
Dark upright hand-like silhouette surrounded by green light
Silver gauntlet-like shape with purple-white branching rays
Three upright golden columns on red
Long silver blade against pale cyan rays
Golden shield tilted against a blue motion trail
Round purple orb on a pale brown radial field
Dark flexing humanoid silhouette within green rays
Dark spiked crown-like silhouette under pale blue light
Golden diagonal light crossing a dark red-green field
Black-rimmed shield with two large purple central lobes
Orange gem-like oval with blue-white branching rays
Gold-colored winged pendant with a central orange gem
Dark green upright faceted object against golden rays
Golden pointed object on a blue field
Golden gauntlet or fist sweeping through pale blue motion trails
Golden semicircle beneath descending pink light
Bright golden lower orb with pale branching rays
Golden vertical branching light on red
Dark armored figures under green vertical light
Small golden upright shape under a green column of light
Dark gauntlet or hand-like shape within a green ring
Large pale glowing shield on gold
Red shield-like seal with a golden vertical center on blue
Red-and-gold rectangular plaque on purple
Blue-and-gold rectangular plaque with a pale central figure
Open hand with pale blue sparks on blue
Pale pink circular seal with geometric markings
Golden-orange circular seal with geometric markings
Pink-gold circular seal with dark geometric markings
''')

plain_bows={
 1:'Dark green angular bow',2:'Brown wooden angular bow',3:'Dark purple bow with a pointed upper end',
 4:'Red curved bow',6:'Brown curved bow',8:'Brown wooden bow',12:'Purple curled bow',
 13:'Dark gray-blue curved bow',14:'Orange-brown curved bow',15:'Brown wooden angular bow',
 16:'Dark green angular bow',19:'Gold-colored curved bow',20:'Gray-blue curved bow',
 21:'Brown wooden bow',22:'Red-brown curved bow',23:'Pale gray-blue curved bow',
 25:'Small teal crossbow',26:'Small wooden crossbow',27:'Brown wooden crossbow',
 29:'Gray-blue crossbow with a long grip',30:'Red crossbow',32:'Pale blue crossbow',
 37:'Dark gray crossbow with a long grip',38:'Orange crossbow',40:'Brown-red crossbow',
 43:'Red crossbow with a long grip',44:'Gold-orange crossbow',45:'Purple curved crossbow',
 46:'Gray-blue crossbow',47:'Black-teal crossbow'}
for number,entry in annotations['craftpix-996288'].items():
    entry['traits']['weaponType']='bow' if int(number)<=24 else 'crossbow'
    if int(number) in plain_bows:entry['descriptionNoEffectEn']=plain_bows[int(number)]+', without a surrounding aura.'

# Portrait traits describe visible framing, not a mechanically certain ancestry.
for pack in ('craftpix-305321','craftpix-945210'):
    for entry in annotations[pack].values():
        entry['traits']['framing']='head_shoulders'
        caption=entry['descriptionEn'].lower()
        if 'pointed-eared' in caption:entry['traits']['earShape']='pointed'
        if 'beard' in caption:entry['traits']['facialHair']='beard'
        elif 'moustache' in caption:entry['traits']['facialHair']='moustache'

# The 64px pack remains excluded even though its old observations are retained.
goblin_source=ast.parse((ROOT/'scripts/prepare-craftpix-portraits.py').read_text(encoding='utf-8'))
goblin_observations=next(ast.literal_eval(node.value) for node in goblin_source.body
    if isinstance(node,ast.Assign) and any(isinstance(target,ast.Name) and target.id=='observations' for target in node.targets))
assert len(goblin_observations)==50
annotations['craftpix-415209']={str(i):dict(descriptionEn=description,
    traits=dict(skinColor=skin,visualType='humanoid_face',framing='head_shoulders'),
    reviewMethod='detail_sheet_128px') for i,(skin,description) in enumerate(goblin_observations,1)}
OUTPUT=ROOT/'scripts/art-pack-annotations.json'
if __name__=='__main__':
    OUTPUT.write_text(json.dumps(annotations,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Described',sum(len(p) for p in annotations.values()),'visual families in',len(annotations),'packs')
