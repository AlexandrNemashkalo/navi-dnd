import concurrent.futures
import html
import json
import re
from pathlib import Path
from urllib.request import Request, urlopen

root=Path(__file__).resolve().parent
packs=json.loads((root/'packs.json').read_text())
def get(url):
    with urlopen(Request(url,headers={'User-Agent':'Mozilla/5.0'}),timeout=45) as response:
        return response.read()
collection=get('https://craftpix.net/sets/fantasy-pixel-art-icons-collection/').decode()
links=set(re.findall(r'https://craftpix.net/(?:product|freebies)/[^"<>\s]+/',collection))
def fetch(slug):
    urls=[x for x in links if x.rstrip('/').split('/')[-1]==slug]
    url=urls[0] if urls else 'https://craftpix.net/product/'+slug+'/'
    page=get(url).decode()
    (root/(slug+'.html')).write_text(page,encoding='utf-8')
    # Gallery images appear in data-fancybox/lightbox links; retain candidates for visual matching.
    images=list(dict.fromkeys(re.findall(r'https://img\.craftpix\.net/[^"<>\s]+\.(?:jpg|png|webp)',page)))
    words=[x for x in slug.split('-') if x not in ('free','and','for','the','icons','icon','pixel','art','pack','rpg','game')]
    images=[u for u in images if not re.search(r'-\d+x\d+\.',u) and all(w.lower() in u.lower() for w in words)]
    stems=list(dict.fromkeys(re.sub(r'\.(?:jpg|png|webp)$','',u) for u in images))
    found=[]
    for u in stems:
        local=root/(u.rsplit('/',1)[-1]+'.jpg')
        if not local.exists():
            try: local.write_bytes(get(u+'.jpg'))
            except Exception: continue
        found.append({'url':u+'.jpg','path':local.name})
    return {'slug':slug,'page':url,'images':found}
results=[]
with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
    fs={pool.submit(fetch,slug):slug for slug in dict.fromkeys(p['slug'] for p in packs)}
    for f in concurrent.futures.as_completed(fs):
        try:
            result=f.result();results.append(result);print(result['slug'],len(result['images']),flush=True)
        except Exception as e: print('ERROR',fs[f],str(e),flush=True)
(root/'sources-found.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
