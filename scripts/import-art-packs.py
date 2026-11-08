"""Import user-supplied Craftpix ZIPs for offline V2 review, without AI calls.

Only craftpix*.zip in the repository root are considered. Archives are never
modified. Original PNG variants and license/readme bytes are retained. The
preferred color variant is also prepared as a separate 32x32 PNG. Re-running
refuses to overwrite edited output. Requires Pillow.
"""
import hashlib
import io
import json
import math
import re
import zipfile
from collections import Counter
from pathlib import Path, PurePosixPath
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/art-catalog-review'
IMPORTED = OUT / 'imported'
IMPORTED.mkdir(parents=True, exist_ok=True)
MANIFEST = OUT / 'imported-assets.json'
previous = {r['id']: r for r in json.loads(MANIFEST.read_text(encoding='utf-8'))} if MANIFEST.exists() else {}
packs_path=OUT/'imported-packs.json'
previous_packs=json.loads(packs_path.read_text(encoding='utf-8')) if packs_path.exists() else []
library=ROOT/'src/ArtLibrary'
if not previous_packs and (library/'packs.json').exists():
    previous_packs=json.loads((library/'packs.json').read_text(encoding='utf-8'))
previous_packs=[p for p in previous_packs if p.get('sourceKind')!='preview_extraction']
excluded_path=OUT/'excluded-packs.json'
if not excluded_path.exists() and (library/'excluded-packs.json').exists():
    excluded_path.write_bytes((library/'excluded-packs.json').read_bytes())
excluded=json.loads(excluded_path.read_text(encoding='utf-8')) if excluded_path.exists() else {}
deleted_path=OUT/'deletions.json'
if not deleted_path.exists() and (library/'deletions.snapshot.json').exists():
    deleted_path.write_bytes((library/'deletions.snapshot.json').read_bytes())
def is_deleted(asset_id):
    # The review server can delete an icon while this import is running.
    current=json.loads(deleted_path.read_text(encoding='utf-8')) if deleted_path.exists() else {}
    return asset_id in current
annotations_path = ROOT / 'scripts/art-pack-annotations.json'
annotations = json.loads(annotations_path.read_text(encoding='utf-8')) if annotations_path.exists() else {}
goblin_path = OUT / 'craftpix-portraits/manifest.json'
goblins = {int(r['id'].split(':')[-1]): r for r in json.loads(goblin_path.read_text(encoding='utf-8'))} if goblin_path.exists() else {}
seen_archives, packs, assets, duplicates, problems = {}, [], [], [], []
PACK_ROLES={
    'craftpix-780987':'item','craftpix-128598':'item','craftpix-253495':'item',
    'craftpix-305321':'portrait','craftpix-415209':'portrait',
    'craftpix-459799':'monster_illustration','craftpix-495817':'spell',
    'craftpix-523115':'spell','craftpix-688754':'item',
    'craftpix-737210':'monster_illustration','craftpix-883583':'item',
    'craftpix-909691':'spell','craftpix-945210':'portrait',
    'craftpix-986554':'item','craftpix-996288':'item',
}

def digest(data):
    return hashlib.sha256(data).hexdigest()

def immutable_write(path, data):
    if path.is_symlink() or not any(path.resolve().is_relative_to(root.resolve()) for root in (IMPORTED,library/'Sources')):
        raise ValueError(f'Output escapes the managed import directory: {path}')
    if path.exists():
        if path.read_bytes() != data:
            raise ValueError(f'Refusing to overwrite changed output: {path}')
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)

def safe_member(name):
    p = PurePosixPath(name)
    if not name or p.is_absolute() or any(c in '\\:<>"|?*' or ord(c)<32 for c in name):return False
    for part in name.split('/'):
        if part in ('.','..','') or part.endswith(('.',' ')):return False
        base=part.split('.')[0].upper()
        if base in ('CON','PRN','AUX','NUL') or re.fullmatch(r'(COM|LPT)[1-9]',base):return False
    return True

class RetainedSource:
    """Read a verified source tree after its redundant ZIP was removed."""
    def __init__(self, pack):
        self.members={m['member']:m for m in pack['sourceFiles']}
    def __enter__(self): return self
    def __exit__(self,*args): return False
    def infolist(self):
        result=[]
        for name,m in self.members.items():
            info=zipfile.ZipInfo(name);info.file_size=m['size'];result.append(info)
        return result
    def read(self, member):
        name=member if isinstance(member,str) else member.filename
        m=self.members[name]
        path=(ROOT/m['path']).resolve() if m.get('pathBase')=='workspace' else (OUT/m['path']).resolve()
        if not path.is_relative_to(IMPORTED.resolve()) and not path.is_relative_to((library/'Sources').resolve()):raise ValueError('Unsafe retained source path')
        data=path.read_bytes()
        if digest(data)!=m['sha256']:raise ValueError(f'Retained source changed: {name}')
        return data

sources=list(sorted(ROOT.glob('craftpix*.zip')))
current_ids={m[1] for p in sources if (m:=re.match(r'craftpix(?:-net)?-(\d+)-',p.name))}
retained={p['archive']:p for p in previous_packs if p['id'].removeprefix('craftpix-') not in current_ids}
sources.extend(ROOT/name for name in sorted(retained))
for archive in sources:
    source_match=re.match(r'craftpix(?:-net)?-(\d+)-',archive.name)
    if source_match and 'craftpix-'+source_match[1] in excluded:
        continue
    archive_hash = digest(archive.read_bytes()) if archive.exists() else retained[archive.name]['sha256']
    if archive_hash in seen_archives:
        duplicates.append(dict(archive=archive.name, duplicateOf=seen_archives[archive_hash],sha256=archive_hash))
        continue
    seen_archives[archive_hash] = archive.name
    match = re.match(r'craftpix(?:-net)?-(\d+)-(.+?)(?: \(\d+\))?\.zip$', archive.name)
    if not match:
        problems.append(dict(archive=archive.name, issue='unrecognized_pack_name'))
        continue
    pack_id = 'craftpix-' + match[1]
    label = match[2]
    pack_root = IMPORTED / pack_id
    # Confirmed by visual review, not a keyword guess (the orc pack has no
    # "avatar" in its archive name despite consisting entirely of faces).
    usage=PACK_ROLES.get(pack_id,'unclassified')
    is_portrait=usage=='portrait'
    pack_assets=[]
    source_files=[]
    source=zipfile.ZipFile(archive) if archive.exists() else RetainedSource(retained[archive.name])
    with source as z:
        if len(z.infolist()) > 10000 or sum(i.file_size for i in z.infolist()) > 256*1024*1024:
            raise ValueError(f'Archive exceeds import limits: {archive.name}')
        source_names=set()
        for info in z.infolist():
            if info.is_dir():
                continue
            name=info.filename
            if not safe_member(name):
                raise ValueError(f'Unsafe archive path: {archive.name}/{name}')
            folded=name.casefold()
            if folded in source_names:
                raise ValueError(f'Conflicting archive members: {archive.name}/{name}')
            source_names.add(folded)
            # Retain every source file, including PSDs and preview images, before
            # the caller considers removing an archive. No extractall/path trust.
            raw_source=z.read(info)
            permanent_source=library/'Sources'/pack_id/name
            immutable_write(permanent_source,raw_source)
            source_files.append(dict(member=name,path=permanent_source.relative_to(ROOT).as_posix(),pathBase='workspace',
                                     size=len(raw_source),sha256=digest(raw_source)))
            if name.lower().endswith(('.txt',)) and ('license' in name.lower() or 'readme' in name.lower()):
                immutable_write(pack_root/'source'/name,z.read(info))
            if name.startswith('__MACOSX/') or not name.lower().endswith('.png') or '/png/' not in '/'+name.lower():
                continue  # Preview sheets and PSDs stay in the original archive.
            if info.file_size > 16*1024*1024:
                raise ValueError(f'Image exceeds import limits: {name}')
            raw=z.read(info)
            immutable_write(pack_root/'source'/name,raw)
            try:
                with Image.open(io.BytesIO(raw)) as original:
                    if original.width>4096 or original.height>4096:
                        raise ValueError('image dimensions exceed import limits')
                    im=original.convert('RGBA')
            except Exception as exc:
                problems.append(dict(archive=archive.name,member=name,issue=str(exc)))
                continue
            if im.size not in ((32,32),(64,64)):
                problems.append(dict(archive=archive.name,member=name,issue='nonstandard_source_size',size=list(im.size)))
            parts={s.lower() for s in PurePosixPath(name).parts}
            variant='transparent' if 'transperent' in parts or 'transparent' in parts else 'background' if 'background' in parts else 'monochrome' if 'monochrome' in parts else 'simple_color' if 'simple_color' in parts else 'framed'
            if variant not in ('transparent','framed'):
                continue
            if im.size!=(32,32):
                problems.append(dict(archive=archive.name,member=name,issue='excluded_non_native_32px',size=list(im.size)))
                continue
            stem=PurePosixPath(name).stem
            num_match=re.match(r'Icon(\d+)(.*)$',stem,re.I)
            if not num_match:
                problems.append(dict(archive=archive.name,member=name,issue='unknown_icon_identity'))
                continue
            number=int(num_match[1]);suffix=num_match[2].lower()
            identity=f'{pack_id}:icon-{number:03d}'
            asset_id=identity+suffix
            target=pack_root/'icons'/f'{number:03d}{suffix}.png'
            output=im
            buffer=io.BytesIO();output.save(buffer,format='PNG')
            if not is_deleted(asset_id):
                immutable_write(target,buffer.getvalue())
                if is_deleted(asset_id):target.unlink(missing_ok=True)
            observed=annotations.get(pack_id,{}).get(str(number),{})
            if pack_id=='craftpix-415209' and number in goblins:
                observed=dict(descriptionEn=goblins[number]['descriptionEn'],traits=goblins[number]['traits'],reviewMethod='detail_sheet_128px')
            caption=observed.get('descriptionNoEffectEn') if suffix=='_no_effect' else observed.get('descriptionEn')
            if not caption:caption=observed.get('descriptionEn')
            traits=dict(observed.get('traits',{}))
            if pack_id=='craftpix-883583':traits['visualType']='shield' if number<=36 else 'amulet'
            usages=[usage,'map_actor'] if usage=='monster_illustration' else [usage]
            style='fantasy.pixel.portrait.32' if is_portrait else 'fantasy.pixel.skill-framed.32' if usage=='spell' else 'fantasy.pixel.creature.32' if usage=='monster_illustration' else 'fantasy.pixel.item.32' if usage=='item' else 'unclassified'
            alpha=output.getchannel('A');bounds=alpha.getbbox()
            source_digest=digest(raw)
            record=dict(id=asset_id,pack=pack_id,label=f'{label} / {number:02d}{suffix}',
                path=target.relative_to(OUT).as_posix(),preview=target.relative_to(OUT).as_posix(),
                sha256=digest(buffer.getvalue()),sourceSha256=source_digest,
                width=32,height=32,sourceWidth=im.width,sourceHeight=im.height,
                hasTransparency=alpha.getextrema()[0]<255,visibleBounds=bounds,
                usages=usages,proposedUsages=usages,inferredKind=usage,styleId=style,
                identityFamily=identity,variantFamily=identity,variant=variant,
                effectVariant='no_effect' if suffix=='_no_effect' else 'source_default',
                framing='head_shoulders' if is_portrait else 'unknown',
                gender='unknown',species='unknown',ageBand='unknown',
                descriptionEn=caption,traits=traits,tags=list(dict.fromkeys(re.findall(r'[a-z]+',(caption or '').lower()))),
                reviewMethod=observed.get('reviewMethod'),
                metadataSource='visual_detail_review' if observed else 'pack_and_variant_only',
                technicalStatus='passed' if bounds and (not is_portrait or alpha.getextrema()[0]==0) else 'needs_review',
                semanticStatus='visually_described' if observed else 'pending',
                artStatus='needs_32px_review' if im.size!=(32,32) else 'needs_in_game_review',
                transform='none',
                provenance=dict(archive=archive.name,archiveSha256=archive_hash,member=name,
                    licenseRef=(pack_root/'source/License.txt').relative_to(OUT).as_posix()))
            if any(a['id']==asset_id for a in pack_assets):
                raise ValueError(f'Duplicate logical asset ID: {asset_id}')
            pack_assets.append(record)
        pack_assets.sort(key=lambda a:a['id'])
        # One sheet includes every selected variant, not just one representative per family.
        ordered=sorted(pack_assets,key=lambda a:(int(re.search(r'icon-(\d+)',a['id'])[1]),a['id']))
        columns=8;cell=112
        board=Image.new('RGB',(columns*cell,math.ceil(len(ordered)/columns)*128),'#454b53')
        draw=ImageDraw.Draw(board)
        for i,a in enumerate(ordered):
            if is_deleted(a['id']) or not (OUT/a['path']).exists():
                continue
            try:
                im=Image.open(OUT/a['path']).convert('RGBA').resize((96,96),Image.Resampling.NEAREST)
            except FileNotFoundError:
                continue
            x=(i%columns)*cell+8;y=(i//columns)*128+4
            board.paste(im,(x,y),im)
            draw.text((x,y+99),a['id'].split(':')[-1].replace('icon-',''),fill='white')
        board.save(pack_root/'contact-sheet.png')
        packs.append(dict(id=pack_id,label=label,archive=archive.name,sha256=archive_hash,
            selectedAssets=len(pack_assets),identityFamilies=len({a['identityFamily'] for a in pack_assets}),
            preview=(pack_root/'contact-sheet.png').relative_to(OUT).as_posix(),
            sourceVariantsRetained=True,usage=usage,sourceFiles=source_files))
        assets.extend(pack_assets)

processed={p['id'] for p in packs}
packs.extend(p for p in previous_packs if p['id'] not in processed)
assets.extend(a for a in previous.values() if a['pack'] not in processed and a['pack'] not in excluded)

for filename,value in [('imported-assets.json',assets),('imported-packs.json',packs),
                       ('archive-import-report.json',dict(sourcePacks=len(packs),activePacks=sum(p['id'] not in excluded for p in packs),assets=len(assets),excludedPacks=excluded,
                         identityFamilies=len({a['identityFamily'] for a in assets}),
                         duplicateArchives=duplicates,issues=problems,
                         sourceSizes=dict(Counter(f"{a['sourceWidth']}x{a['sourceHeight']}" for a in assets)),
                         usages=dict(Counter(a['usages'][0] for a in assets))))]:
    (OUT/filename).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Read {len(packs)} distinct source packs ({sum(p["id"] not in excluded for p in packs)} active); {len(assets)} native 32px candidates; duplicate archives: {len(duplicates)}; issues: {len(problems)}')
