"""Prepare completed batch sources as unpublished 32px drafts and review sheets."""
import hashlib,json,subprocess,sys,textwrap
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
BATCH=LIB/'Sources/own-art/generation-batches/hundred-20261107'
OUT=ROOT/'artifacts/art-completion/hundred'
OUT.mkdir(parents=True,exist_ok=True)
manifest=json.loads((BATCH/'manifest.json').read_text(encoding='utf-8'))
ready=[]
for job in manifest['jobs']:
 result=BATCH/'results'/(job['key']+'.json')
 if not result.exists():continue
 record=json.loads(result.read_text(encoding='utf-8'))
 assert record['key']==job['key']
 job['prompt']=record['prompt']
 target=LIB/'Sources/own-art'/(job['key']+'.png')
 meta=target.with_suffix('.provenance.json')
 if meta.exists() and not target.exists():
  # A prepared image removed by the gallery/user must not reappear on rerun.
  continue
 if not target.exists():
  subprocess.run([sys.executable,str(ROOT/'scripts/prepare-generated-icon.py'),record['source'],job['key'],
   '--name',job['name'],'--name-ru',job['nameRu'],'--description',job['description'],'--usage',job['usage'],'--colors','20'],check=True,capture_output=True)
  data=json.loads(meta.read_text(encoding='utf-8'))
  data.update(published=False,reviewStatus='pending',origin='Built-in image_gen; generated source prepared as an unreviewed exact 32x32 draft.',
   promptFile=result.relative_to(ROOT).as_posix(),batch='hundred-20261107')
  meta.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 im=Image.open(target).convert('RGBA')
 assert im.size==(32,32) and set(im.getchannel('A').tobytes()) <= {0,255},target
 assert im.getchannel('A').getextrema()==((0,255) if job['transparent'] else (255,255)),target
 assert hashlib.sha256(target.read_bytes()).hexdigest()==json.loads(meta.read_text(encoding='utf-8'))['sha256']
 ready.append((job,im))
try:font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',14)
except OSError:font=ImageFont.load_default()
for start in range(0,100,20):
 sheet=Image.new('RGB',(1000,800),(35,39,44));draw=ImageDraw.Draw(sheet)
 for job,im in ready:
  if not start<job['index']<=start+20:continue
  n=job['index']-1-start;x=(n%5)*200;y=(n//5)*200
  enlarged=im.resize((128,128),Image.Resampling.NEAREST)
  sheet.paste(enlarged,(x+8,y+8),enlarged)
  sheet.paste(im,(x+152,y+104),im)
  draw.text((x+8,y+142),str(job['index'])+'. '+ '\n'.join(textwrap.wrap(job['name'],24)),font=font,fill='white')
 sheet.save(OUT/('review-%03d-%03d.png'%(start+1,start+20)))
status=dict(requested=100,generated=sum((BATCH/'results'/(j['key']+'.json')).exists() for j in manifest['jobs']),prepared=len(ready),
 errors=sum((BATCH/'errors'/(j['key']+'.json')).exists() and not (BATCH/'results'/(j['key']+'.json')).exists() for j in manifest['jobs']),published=sum(json.loads((LIB/'Sources/own-art'/(j['key']+'.provenance.json')).read_text(encoding='utf-8')).get('published',True) for j,_ in ready))
(OUT/'progress.json').write_text(json.dumps(status,indent=2)+'\n',encoding='utf-8')
manifest['status']='complete' if status['published']==100 else 'awaiting_review' if status['prepared']==100 else 'generating'
manifest['progress']=status
(BATCH/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(status))
