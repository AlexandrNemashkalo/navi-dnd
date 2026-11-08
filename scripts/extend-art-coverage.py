"""Add authored base-form correspondences without changing existing selections.

An ordinary item illustration represents the visible object, never its secret powers.
Run audit-art-catalog.py before/after; all source hashes and roles are revalidated.
"""
import copy
import json
from pathlib import Path
from art_requirement_bindings import load_bindings, save_bindings

ROOT = Path(__file__).resolve().parents[1]
requirements = json.loads((ROOT/'artifacts/art-catalog-review/requirements.json').read_text(encoding='utf-8'))
assets = {a['id']: a for a in json.loads((ROOT/'artifacts/art-catalog-review/assets.json').read_text(encoding='utf-8'))}
document = load_bindings(ROOT)
existing = {(b['category'], b['name']): b for b in document['bindings']}
known = {(r['category'], r['name']) for r in requirements}
added = []


def reuse(category, names, source_category, source_name, reason):
    source = existing.get((source_category, source_name))
    if not source or not source['candidates']:
        raise ValueError('Missing authored base form: '+source_name)
    for name in names.split('|'):
        key = category, name
        if key not in known:
            raise ValueError('Unknown reference name: '+repr(key))
        if key in existing:
            continue
        entry = copy.deepcopy(source)
        entry.update(category=category, name=name, note='Base-form illustration, not an exact unique artifact likeness. Hidden effects, material, scale and quantities remain game data.')
        entry['selectionPolicy']['mode'] = 'balanced_variants'
        for candidate in entry['candidates']:
            candidate.update(reason=reason, match='visual_representation')
        document['bindings'].append(entry)
        existing[key] = entry
        added.append(key)


def magic(names, base, category='equipment', reason=None):
    reuse('magic_item', names, category, base, reason or 'The same complete visible object form as '+base+'; magical identity is supplied by the reference entry, not the pixels.')


magic('DARK SHARD AMULET|CLOCKWORK AMULET|AMULET OF THE BLACK SKULL', 'Amulet')
magic('BEAD OF NOURISHMENT|BEAD OF REERESHMENT|BEAD OF FORCE', 'PEARL OF POWER', 'magic_item', 'One round bead/pearl represents the small spherical object; nourishment, force and liquid properties remain game facts.')
magic('CAST—OFF ARMOR|SMOLDERING ARMOR|ARMOR 0E GLEAMING|ADAMANTINE ARMOR|MARINER\'S ARMOR|MITHRAL ARMOR|ARMOR +1|ARMOR OF RESISTANCE|ARMOR OF VULNERABILITY|SCORPION ARMOR|DEMON ARMOR|ARMOR +2|ARMOR +3', 'Plate')
magic('GLAMOURED STUDDED LEATHER', 'Studded leather')
magic('DRAGON SCALE MAIL', 'Chain mail')
magic('CLOTHES OF MENDING', 'Clothes, common')
magic('ROBE OF USEFUL ITEMS|ROBE OF EYES|ROBE OF STARS|ROBE OF SCINTILLATING COLORS', 'Robes')
magic('DREAD HELM', 'HELM OF TELEPATHY', 'magic_item')
magic('Winged boots|SLIPPERS OF SPIDER CLIMBING', 'BOOTS OF SPEED', 'magic_item')
magic('CAP OF WATER BREATHING', 'HAT OF DISGUISE', 'magic_item')
magic('HEWARD’S HANDY SPICE POUCH|BAG OF BEANS', 'Pouch')
magic('MOON—TOUCHED SWORD|WEAPON +1|WEAPON OF WARNING|VICIOUS WEAPON|WEAPON +2|SUN BLADE|GIANT SLAYER|DRAGON SLAYER|FLAME TONGUE|FROST BRAND|WEAPON +3|VORPAL SWORD|SWORD OF KAS', 'Longsword', reason='A complete sword is a base-form representative; light, flame, frost, sentience and magical bonuses do not change this illustration or reveal hidden effects.')
magic('AXE OF THE DWARVISH LORDS', 'Battleaxe')
magic('WALLOPING AMMUNITION|AMMUNITION +1|AMMUNITION +2|AMMUNITION +3', 'Arrows (20)')
magic('POLE OF COLLAPSING|VETERAN’S CANE|STAFF OF THE ADDER|STAFF OF THE PYTHON|STAFF OF THE FORGOTTEN ONE', 'Wooden staff')
magic('POLE OF ANGLING', 'Fishing tackle')
magic('TENTACLE ROD|ROD OF LORDLY MIGHT', 'Rod')
magic('WAND OF ORCUS', 'Wand')
magic('EAR HORN OF HEARING', 'HORN OF SILENT ALARM', 'magic_item')
magic('EYES OF MINUTE SEEING|EYES OF THE EAGLE|EYES OF CHARMING', 'GOGGLES OF NIGHT', 'magic_item')
magic('EVERSMOKING BOTTLE|EFREETI BOTTLE|IRON FLASK', 'Bottle, glass')
magic('KEOGHTOM\'S OINTMENT', 'Vial')
magic('TOME OF THE STILLED TONGUE|BOOK OF EXALTED DEEDS|BOOK OF VILE DARKNESS', 'Book')
magic('ORB OF DRAGONKIND|DRIFTGLOBE', 'Orb')
magic('RUBY OF THE WAR MAGE|GEM OF BRIGHTNESS|ELEMENTAL GEM|SENDING STONES|GEM OF SEEING|IOUN STONE (Awareness)|IOUN STONE (Protection)|IOUN STONE (Sustenance)|IOUN STONE (Reserve)|IOUN STONE (Leadership)|IOUN STONE (Absorption)|IOUN STONE (Agility)|IOUN STONE (Insight)|IOUN STONE (Intellect)|IOUN STONE (Strength)|IOUN STONE (Fortitude)|IOUN STONE (Greater Absorption)|IOUN STONE (Mastery)|IOUN STONE (Regeneration)', 'Crystal')
magic('NOLZUR\'S MARVELOUS PIGMENTS', 'POT OF AWAKENING', 'magic_item', 'A complete pot represents the pigment container; paint colour and magical capabilities are not inferred.')

for names, base in [
    ('Crossbow bolts (20)', 'Arrows (20)'),
    ('Hammer, sledge', 'Maul'),
    ('Ring mail|Scale mail', 'Chain mail'),
    ('Splint|Half plate', 'Breastplate'),
    ('Clothes, traveler\'s|Clothes, costume|Padded', 'Clothes, common'),
    ('Pole (10-foot)', 'Quarterstaff'),
    ('Piton', 'Spikes, iron (10)'),
    ('Lantern, hooded|Lantern, bullseye', 'LANTERN OF REVEALING'),
]:
    reuse('equipment', names, 'magic_item' if base == 'LANTERN OF REVEALING' else 'equipment', base,
          'Complete '+base+' silhouette represents the same basic object form; subtype, material, length and quantity remain reference data.')

save_bindings(ROOT, document)
print('Added base-form correspondences:', len(added))
for category in sorted({c for c, _ in added}):
    print(category, sum(c == category for c, _ in added))
