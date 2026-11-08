"""Verify delivery of the reviewed 100-image batch, including actual runtime bindings."""
import hashlib,json
from collections import Counter
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
BATCH=LIB/'Sources/own-art/generation-batches/hundred-20261107'
manifest=json.loads((BATCH/'manifest.json').read_text(encoding='utf-8'))
review=json.loads((BATCH/'review.json').read_text(encoding='utf-8'))
approved={r['key']:r for r in review['approved']}
runtime=json.loads((LIB/'RuleSets/Dnd5e/runtime-pictures.json').read_text(encoding='utf-8'))
images={i['id']:i for i in runtime['images']}
assert len(manifest['jobs'])==100
assert len({j['key'] for j in manifest['jobs']})==100
records=[]
for job in manifest['jobs']:
 meta=LIB/'Sources/own-art'/(job['key']+'.provenance.json')
 data=json.loads(meta.read_text(encoding='utf-8'))
 path=meta.with_name(job['key']+'.png')
 digest=hashlib.sha256(path.read_bytes()).hexdigest()
 assert data['published'] and data['reviewStatus']=='approved',job['key']
 assert digest==data['sha256']==approved[job['key']]['sha256']
 with Image.open(path) as im:
  assert im.mode=='RGBA' and im.size==(32,32),path
  assert set(im.getchannel('A').tobytes())<={0,255}
  assert im.getchannel('A').getextrema()==((0,255) if job['transparent'] else (255,255))
  raw=im.tobytes()
  visible={tuple(raw[i:i+3]) for i in range(0,len(raw),4) if raw[i+3]}
  assert len(visible)<=20,(path,len(visible))
 with Image.open(ROOT/data['sourcePath']) as original:
  assert list(original.size)==data['sourceDimensions']
 result=json.loads((BATCH/'results'/(job['key']+'.json')).read_text(encoding='utf-8'))
 assert len(result['prompt'])>100
 assert digest in images,job['key']
 assert (LIB/images[digest]['path']).read_bytes()==path.read_bytes()
 assert any(job['name'] in b['names'] and job['category'] in b['categories'] and b['imageId']==digest for b in runtime['bindings']),job['name']
 records.append(dict(key=job['key'],name=job['name'],nameRu=job['nameRu'],category=job['category'],
  file=path.relative_to(ROOT).as_posix(),sha256=digest,width=32,height=32,visibleColors=len(visible)))
assert len({r['sha256'] for r in records})==100,'Duplicate final images'
assert not any(i.get('sourceAssetId')=='own-art:batch100-sea-horse-v1' for i in runtime['images'])
report=dict(generated=100,reviewed=100,published=100,runtimeBound=100,uniqueImages=100,
 categories=dict(Counter(j['category'] for j in manifest['jobs'])),records=records)
out=ROOT/'artifacts/art-completion/hundred/delivery.json'
out.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='records'}))
