"""Report literal DCSS references and paper-doll recipes without restoring files.

This is a dependency inventory, not a claim that every dynamic runtime reference
has been discovered. V1's 000000 image files are neither read nor modified.
"""
import json
import re
from collections import defaultdict
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/art-catalog-review'
references=defaultdict(list)
for folder in ('NaviDnD','NaviDnD.Data','NaviDnD.MapGen','NaviDnD.McpServer'):
    for path in sorted((ROOT/'src'/folder).rglob('*.cs')):
        if any(part in ('bin','obj','Storage','logs') for part in path.parts):continue
        for number,line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(),1):
            if line.lstrip().startswith('//'):continue
            for slug in re.findall(r'"(dcss/[^"\n]+)"',line):
                if not re.fullmatch(r'dcss/[a-zA-Z0-9_./-]+',slug) or slug.endswith('/'):continue
                target=slug if slug.endswith('.png') else slug+'.png'
                references[target].append(dict(source=path.relative_to(ROOT).as_posix(),line=number,kind='literal'))

options_path=ROOT/'src/NaviDnD/Prompts/Dnd5e/heroOptions.json'
options=json.loads(options_path.read_text(encoding='utf-8-sig'))
for race in options['races']:
    if race.get('doll'):
        for gender in ('male','female'):
            references[f"dcss/player/base/{race['doll']}_{gender}.png"].append(dict(source=options_path.relative_to(ROOT).as_posix(),key=race['name'],kind='doll_base_recipe'))
for cls in options['classes']:
    for gear in cls.get('dolls',[]):
        for layer in gear.get('gear','').split('+'):
            if layer:references[f'dcss/player/{layer}.png'].append(dict(source=options_path.relative_to(ROOT).as_posix(),key=cls['name'],kind='doll_gear_recipe'))
        for field,folder in [('hair','hair'),('hairFemale','hair'),('beard','beard')]:
            if gear.get(field):references[f"dcss/player/{folder}/{gear[field]}.png"].append(dict(source=options_path.relative_to(ROOT).as_posix(),key=cls['name'],kind='doll_detail_recipe'))

entries=[]
for path,uses in sorted(references.items()):
    entries.append(dict(path=path,exists=(ROOT/'src/icons'/path).is_file(),usedBy=uses))
report=dict(scope='literal_references_and_dnd5e_hero_doll_recipes',
            dynamicReferenceCoverage='incomplete',references=entries,
            referencedFiles=len(entries),missingFiles=sum(not e['exists'] for e in entries))
for name,value in [('dependencies.json',report),('broken-image-references.json',[e for e in entries if not e['exists']])]:
    target=OUT/name;temporary=target.with_suffix('.json.new')
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8');temporary.replace(target)
print(f'Direct/recipe image dependencies: {len(entries)}; missing now: {report["missingFiles"]}. No files restored.')
