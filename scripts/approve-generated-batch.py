"""Record an explicit visual review of selected batch indices, bound to current hashes."""
import argparse,json
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('indices',nargs='+',type=int)
parser.add_argument('--reason',required=True)
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
batch=root/'src/ArtLibrary/Sources/own-art/generation-batches/hundred-20261107'
manifest=json.loads((batch/'manifest.json').read_text(encoding='utf-8'))
review=json.loads((batch/'review.json').read_text(encoding='utf-8'))
assert set(args.indices)<={j['index'] for j in manifest['jobs']}
for job in manifest['jobs']:
 if job['index'] not in args.indices:continue
 data=json.loads((batch.parents[1]/(job['key']+'.provenance.json')).read_text(encoding='utf-8'))
 review['approved']=[x for x in review['approved'] if x['key']!=job['key']]
 review['approved'].append(dict(key=job['key'],sha256=data['sha256'],reason=job['name']+': '+args.reason))
(batch/'review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Approved total:',len(review['approved']))
