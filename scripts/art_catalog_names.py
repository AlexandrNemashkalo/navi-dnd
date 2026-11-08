"""Display-only Russian names; canonical keys and artwork bindings stay unchanged."""
import json
import re


# Names used by the review catalog that are absent from the game's localization.
CATALOG_NAMES_RU = {
    'Sea': 'Море', 'Beach': 'Пляж', 'DeepForest': 'Густой лес',
    'Peaks': 'Вершины', 'SnowForest': 'Заснеженный лес', 'Forest': 'Лес',
    'castle': 'Замок', 'innkeeper': 'Трактирщик', 'merchant': 'Торговец',
    'healer': 'Целитель', 'guard': 'Стражник', 'farmer': 'Фермер',
    'beggar': 'Нищий', 'scholar': 'Учёный', 'guide': 'Проводник',
    'child': 'Ребёнок', 'elder': 'Старейшина', 'slime': 'Слизь', 'dragon': 'Дракон',
    'chest': 'Сундук', 'door': 'Дверь', 'key': 'Ключ', 'lantern': 'Фонарь',
    'rope': 'Верёвка', 'barrel': 'Бочка', 'trap': 'Ловушка',
    'campfire': 'Костёр', 'tent': 'Палатка', 'cart': 'Телега', 'boat': 'Лодка',
    'water': 'Вода', 'Gandalf the Grey': 'Гэндальф Серый',
    'pixel symbol': 'пиксельный символ', 'horizon view': 'вид на горизонт',
    'bracers': 'Наручи', 'axes': 'Топоры', 'axes effects': 'Топоры с эффектами',
    'boots': 'Сапоги', 'cuirasses': 'Кирасы', 'trousers': 'Штаны',
    'staves': 'Посохи', 'staves effects': 'Посохи с эффектами',
    'halloween': 'Хэллоуин', 'arrows': 'Стрелы', 'arrows effects': 'Стрелы с эффектами',
    'spears': 'Копья', 'spears effects': 'Копья с эффектами', 'curses': 'Проклятия',
    'demon': 'Демон', 'necromancer': 'Некромант', 'pirate': 'Пират',
    'undead loot': 'Трофеи нежити', 'fairy': 'Фея', 'fairy loot': 'Трофеи фей',
    'demon magic': 'Демоническая магия', 'blood mage': 'Маг крови',
    'dwarf': 'Дварф', 'dwarf loot': 'Трофеи дварфов', 'engineering': 'Механизмы',
    'goblins orcs': 'Гоблины и орки', 'exotic weapons': 'Экзотическое оружие',
    'lightning': 'Молнии', 'summoner': 'Призыватель', 'chaos loot': 'Трофеи хаоса',
    'kobold loot': 'Трофеи кобольдов', 'kobolds': 'Кобольды', 'earth': 'Земля',
    'dark elves': 'Тёмные эльфы', 'demons': 'Демоны', 'dwarves': 'Дварфы',
    'elves': 'Эльфы', 'humans': 'Люди', 'pirates': 'Пираты', 'undead': 'Нежить',
}


def entry_names(item):
    """Game databases use either language objects or 'Russian (English)'."""
    english = item.get('en', {}).get('name')
    if english:
        return english, item.get('ru', {}).get('name', '')
    raw = item.get('name', '')
    match = re.search(r'\(([^()]*)\)\s*$', raw)
    return (match.group(1), raw[:match.start()].strip()) if match else (raw, '')


def add_russian_names(root, requirements, assets):
    localized = json.loads((root / 'src/NaviDnD.Data/Localization/ru.json').read_text(encoding='utf-8-sig'))
    names = {key.casefold(): value for key, value in localized.items()}
    names.update({key.casefold(): value for key, value in CATALOG_NAMES_RU.items()})
    # Scoped lookups prevent e.g. a spell name from renaming a monster with the same name.
    by_category = {}
    for row in requirements:
        if not row.get('nameRu'):
            # Some monsters already carry both names, including nested parentheses.
            # Retain their historical binding key; normalize only the display fields.
            match = re.fullmatch(r'(.*?)\s+(?:\[([A-Za-z][^\]]+)\]|\(([A-Za-z][^\u0400-\u04ff]+)\))', row['name'])
            if match:
                row['nameEn'] = match.group(2) or match.group(3)
                row['nameRu'] = match.group(1)
        if row.get('nameRu'):
            by_category.setdefault(row['category'], {}).setdefault(row.get('nameEn', row['name']).casefold(), row['nameRu'])

    def translate(name, category=None):
        scoped = by_category.get(category, {})
        result = scoped.get(name.casefold()) or names.get(name.casefold(), '')
        if not result:
            result = next((lookup[name.casefold()] for lookup in by_category.values() if name.casefold() in lookup), '')
        # UI translations sometimes use all capitals; catalog titles use normal case.
        return result.capitalize() if result.isupper() else result

    for row in requirements:
        if row.get('nameRu'):
            continue
        parts = [translate(part, row['category']) for part in row['name'].split(' / ')]
        if all(parts):
            row['nameRu'] = ' / '.join(parts)

    for asset in assets:
        label = asset.get('label', '')
        category = 'spell' if asset['pack'] == 'bg3-spells' else None
        translation = translate(label, category)
        if not translation and ' / ' in label:
            parts = label.split(' / ')
            # Prepared source-pack labels have a numeric family prefix and slot suffix.
            parts[0] = re.sub(r'^\d+-', '', parts[0]).replace('-', ' ')
            translated = [part if part.isdigit() else translate(part) for part in parts]
            if all(translated):
                translation = ' / '.join(translated)
        if translation and translation != label:
            asset['labelRu'] = translation
        mapping = asset.get('spellMapping')
        if mapping:
            translated = translate(mapping.get('sourceName', ''), 'spell')
            if translated:
                # Do not mutate the source manifest's mapping object.
                asset['spellMapping'] = dict(mapping, sourceNameRu=translated)
