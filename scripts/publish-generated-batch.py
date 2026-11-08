"""Publish only batch icons explicitly approved after inspecting final review sheets."""
import hashlib,json
from pathlib import Path
from PIL import Image
from art_requirement_bindings import load_bindings,save_bindings

ROOT=Path(__file__).resolve().parents[1]
LIB=ROOT/'src/ArtLibrary'
BATCH=LIB/'Sources/own-art/generation-batches/hundred-20261107'
manifest=json.loads((BATCH/'manifest.json').read_text(encoding='utf-8'))
approvals=json.loads((BATCH/'review.json').read_text(encoding='utf-8'))
approved={x['key']:x for x in approvals['approved']}
bindings=load_bindings(ROOT)
known={(b['category'],b['name']):b for b in bindings['bindings']}
review_path=LIB/'runtime-review.json'
review=json.loads(review_path.read_text(encoding='utf-8'))
reviewed={x['assetId'] for x in review['assets']}
count=0
for job in manifest['jobs']:
 if job['key'] not in approved:continue
 meta=LIB/'Sources/own-art'/(job['key']+'.provenance.json')
 data=json.loads(meta.read_text(encoding='utf-8'))
 image=meta.with_name(job['key']+'.png')
 assert Image.open(image).size==(32,32)
 assert hashlib.sha256(image.read_bytes()).hexdigest()==data['sha256']
 assert approved[job['key']]['sha256']==data['sha256'],'Review belongs to different image bytes'
 reason=approved[job['key']]['reason']
 assert reason.strip()
 data.update(published=True,reviewStatus='approved',origin='Built-in image_gen; final 32x32 silhouette and identifying features visually reviewed.')
 meta.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 if data['id'] not in reviewed:
  review['assets'].append(dict(assetId=data['id'],sha256=data['sha256'],method=reason,sheet='hundred/review-%03d-%03d.png'%(((job['index']-1)//20)*20+1,((job['index']-1)//20+1)*20)))
 key=(job['category'],job['name'])
 if key in known and known[key].get('candidates'):continue
 entry=dict(category=job['category'],name=job['name'],candidates=[dict(assetId=data['id'],sha256=data['sha256'],usage=job['usage'],match='visual_representation',sourcePriority=0,reason=reason)],
  selectionPolicy=dict(mode='balanced_variants',preserveAssignment=True,requiredChecks=['usage','source_hash','subject_facts']),
  note='Original generated representation. Inspected at final 32x32; game statistics and scale remain data properties.')
 if key in known:bindings['bindings'].remove(known[key])
 bindings['bindings'].append(entry);count+=1
save_bindings(ROOT,bindings)
review_path.write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Published',len(approved),'reviewed batch images; new bindings:',count)
