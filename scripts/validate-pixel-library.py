"""Read-only pixel audit and contact sheets. Requires Pillow; no AI/API calls."""
import hashlib
import json
import math
from collections import Counter, defaultdict
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/art-catalog-review'
SHEETS = OUT / 'contact-sheets'
SHEETS.mkdir(parents=True, exist_ok=True)
records, errors = [], []
previous_path=OUT/'pixel-audit.json'
previous={r['path']:r for r in json.loads(previous_path.read_text(encoding='utf-8'))} if previous_path.exists() else {}
previous_sheets_path=SHEETS/'index.json'
previous_sheets=json.loads(previous_sheets_path.read_text(encoding='utf-8')) if previous_sheets_path.exists() else []
surveyed={a['path'] for s in previous_sheets if s.get('visualReview') in ('surveyed','surveyed_remaining') for a in s['assets']}
pixel_hashes = defaultdict(list)
groups = defaultdict(list)
for path in sorted((ROOT / 'src/icons/dcss').rglob('*.png')):
    relative = path.relative_to(ROOT / 'src/icons').as_posix()
    try:
        with Image.open(path) as original:
            im = original.convert('RGBA')
            alpha = im.getchannel('A')
            bounds = alpha.getbbox()
            # RGB values beneath alpha=0 are irrelevant to visual identity.
            normalized = bytearray(im.tobytes())
            for i in range(0, len(normalized), 4):
                if normalized[i+3] == 0:
                    normalized[i:i+3] = b'\0\0\0'
            digest = hashlib.sha256(f'{im.width}x{im.height}:'.encode() + normalized).hexdigest()
            pixel_hashes[digest].append(relative)
            record = dict(path=relative, width=im.width, height=im.height,
                          alphaRange=alpha.getextrema(), visibleBounds=bounds,
                          empty=bounds is None, pixelHash=digest,
                          edgeContact=bool(bounds and (bounds[0] == 0 or bounds[1] == 0 or bounds[2] == im.width or bounds[3] == im.height)),
                          colorCount=len(im.getcolors(im.width*im.height) or []),
                          technicalStatus='passed' if bounds and im.size == (32,32) else 'needs_review',
                          semanticStatus='pending', artStatus='pending')
            record['usageSurveyed']=relative in surveyed and previous.get(relative,{}).get('pixelHash')==digest
            records.append(record)
            parts=Path(relative).parts
            group = parts[1]
            if group == 'player': group += '-' + parts[2]
            if group == 'gui': group += '-' + parts[2]
            groups[group].append(path)
    except FileNotFoundError:
        continue  # The user may be deleting resources in the browser right now.
    except Exception as exc:
        errors.append(dict(path=relative, error=str(exc)))

sheet_index=[]
record_by_path={r['path']:r for r in records}
for group, paths in sorted(groups.items()):
    for offset in range(0,len(paths),192):
        batch=paths[offset:offset+192]
        board=Image.new('RGB',(16*96,math.ceil(len(batch)/16)*94),'#323941')
        draw=ImageDraw.Draw(board)
        entries=[]
        for i,path in enumerate(batch):
            try:
                with Image.open(path) as original:
                    im=original.convert('RGBA')
            except FileNotFoundError:
                continue
            im.thumbnail((64,64),Image.Resampling.NEAREST)
            if im.size==(32,32): im=im.resize((64,64),Image.Resampling.NEAREST)
            x=(i%16)*96;y=(i//16)*94
            board.paste(im,(x+16,y),im)
            number=offset+i+1
            draw.text((x+2,y+65),f'{number} {path.stem[:12]}',fill='white')
            entries.append(dict(number=number,path=path.relative_to(ROOT/'src/icons').as_posix()))
        name=f'{group}-{offset//192+1:02d}.png'
        board.save(SHEETS/name)
        state='surveyed_remaining' if entries and all(record_by_path[a['path']]['usageSurveyed'] for a in entries) else 'pending'
        sheet_index.append(dict(sheet=name,assets=entries,visualReview=state))

duplicates=[dict(pixelHash=k,paths=v) for k,v in pixel_hashes.items() if len(v)>1]
summary=dict(files=len(records),decodeErrors=errors,
             sizes=dict(Counter(f"{r['width']}x{r['height']}" for r in records)),
             empty=sum(r['empty'] for r in records),
             opaque=sum(r['alphaRange'][0]==255 for r in records),
             edgeContact=sum(r['edgeContact'] for r in records),
             duplicateGroups=len(duplicates),redundantCopies=sum(len(d['paths'])-1 for d in duplicates),
             contactSheets=len(sheet_index),groups={k:len(v) for k,v in sorted(groups.items())})
for name,value in [('pixel-audit.json',records),('pixel-audit-summary.json',summary),
                   ('pixel-duplicates.json',duplicates),('contact-sheets/index.json',sheet_index)]:
    (OUT/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
