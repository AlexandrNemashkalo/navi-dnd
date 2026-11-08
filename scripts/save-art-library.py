"""Keep imported originals outside ignored build artifacts and save a draft.

No runtime integration or art approval is implied. Original PSD/PNG variants,
licenses and source hashes are retained. User-deleted candidates stay excluded.
"""
import hashlib
import json
from pathlib import Path
from art_review_metadata import load_reviews, apply_review
from art_visual_metadata import load_annotations, apply_annotation
from art_own_assets import load_own_assets
from art_external_assets import load_external_assets

ROOT=Path(__file__).resolve().parents[1]
REVIEW=ROOT/'artifacts/art-catalog-review'
LIBRARY=ROOT/'src/ArtLibrary'
LIBRARY.mkdir(parents=True,exist_ok=True)
packs=json.loads((REVIEW/'imported-packs.json').read_text(encoding='utf-8'))
prepared_packs=REVIEW/'prepared-packs.json'
if prepared_packs.exists():packs+=json.loads(prepared_packs.read_text(encoding='utf-8'))
excluded_path=REVIEW/'excluded-packs.json'
excluded=json.loads(excluded_path.read_text(encoding='utf-8')) if excluded_path.exists() else {}
deleted_path=REVIEW/'deletions.json'
deleted=json.loads(deleted_path.read_text(encoding='utf-8')) if deleted_path.exists() else {}
copied=0
for pack in packs:
    for member in pack['sourceFiles']:
        source=(ROOT/member['path']).resolve() if member.get('pathBase')=='workspace' else (REVIEW/member['path']).resolve()
        target=(LIBRARY/'Sources'/pack['id']/member['member']).resolve()
        if not target.is_relative_to((LIBRARY/'Sources'/pack['id']).resolve()):raise ValueError('Unsafe source member')
        if not source.exists() and pack['id'] in excluded:
            member['available']=False;member['discardReason']='user_excluded_64px_source_pack'
            continue
        raw=source.read_bytes()
        if hashlib.sha256(raw).hexdigest()!=member['sha256']:raise ValueError(f'Source changed: {source}')
        if target.exists():
            if target.read_bytes()!=raw:raise ValueError(f'Edited permanent source preserved: {target}')
        else:
            target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw);copied+=1
        member.update(path=target.relative_to(ROOT).as_posix(),pathBase='workspace',available=True)

assets=json.loads((REVIEW/'imported-assets.json').read_text(encoding='utf-8'))
prepared_assets=REVIEW/'prepared-assets.json'
if prepared_assets.exists():assets+=json.loads(prepared_assets.read_text(encoding='utf-8'))
reviews=load_reviews(ROOT)
assets+=load_own_assets(ROOT)
assets+=load_external_assets(ROOT)
annotations=load_annotations(ROOT)
draft=[]
for asset in assets:
    asset=apply_review(apply_annotation(asset,annotations),reviews)
    if asset['id'] in deleted or asset['pack'] in excluded:continue
    source=LIBRARY/'Sources'/asset['pack']/asset['provenance']['member']
    if not source.exists():raise ValueError(f'Missing permanent source: {source}')
    if hashlib.sha256(source.read_bytes()).hexdigest()!=asset['sourceSha256']:raise ValueError('Source checksum mismatch')
    draft.append(dict(id=asset['id'],relativePath=source.relative_to(LIBRARY).as_posix(),
        contentHash=asset['sourceSha256'],style=asset['styleId'],identityFamily=asset['identityFamily'],
        description=asset['descriptionEn'] or ('Pending individual description: '+asset['label']),usages=asset['usages'],traits=asset['traits'],
        technicalApproved=asset['technicalStatus']=='passed',semanticApproved=asset['semanticStatus']=='visually_described',
        artApproved=False))
revision=hashlib.sha256(json.dumps(draft,sort_keys=True).encode()).hexdigest()[:16]
def atomic(name,data):
    target=LIBRARY/name;tmp=target.with_suffix(target.suffix+'.new')
    tmp.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8');tmp.replace(target)
atomic('packs.json',[p for p in packs if p.get('sourceKind')!='preview_extraction'])
atomic('excluded-packs.json',excluded)
atomic('deletions.snapshot.json',deleted)
atomic('reviews.snapshot.json',reviews)
atomic('catalog.draft.json',dict(schemaVersion=1,revision=revision,assets=draft))
print(f'Permanent art sources: {LIBRARY}; copied {copied} files; draft has {len(draft)} output 32px candidates. Art approval remains pending.')
