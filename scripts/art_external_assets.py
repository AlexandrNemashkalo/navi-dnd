"""Load explicitly reviewed native tiles with pinned provenance and hashes."""
import hashlib
import json
from PIL import Image


def load_external_assets(root):
    library=root/'src/ArtLibrary'
    manifest=library/'external-assets.json'
    if not manifest.exists():return []
    review=root/'artifacts/art-catalog-review'
    deleted=set()
    for path in [library/'deletions.snapshot.json',review/'deletions.json']:
        if path.exists():deleted.update(json.loads(path.read_text(encoding='utf-8')))
    assets=[]
    for info in json.loads(manifest.read_text(encoding='utf-8')):
        if info['id'] in deleted:continue
        folder=library/'Sources'/info['pack']
        source=folder/info['file']
        if source.resolve().parent!=folder.resolve() or source.suffix!='.png':raise ValueError('Invalid external asset path')
        raw=source.read_bytes();digest=hashlib.sha256(raw).hexdigest()
        if digest!=info['sha256']:raise ValueError('Changed external asset: '+info['id'])
        with Image.open(source) as im:
            if im.size!=(32,32):raise ValueError('External tiles must be native 32x32')
            alpha=im.convert('RGBA').getchannel('A');bounds=alpha.getbbox();transparent=alpha.getextrema()[0]<255
        target=review/'imported'/info['pack']/'icons'/source.name
        target.parent.mkdir(parents=True,exist_ok=True)
        if target.exists() and target.read_bytes()!=raw:raise ValueError('Edited external cache preserved')
        if not target.exists():target.write_bytes(raw)
        relative=target.relative_to(review).as_posix()
        assets.append(dict(id=info['id'],pack=info['pack'],label=info['name'],labelRu=info.get('nameRu',''),path=relative,preview=relative,
            sha256=digest,sourceSha256=digest,width=32,height=32,sourceWidth=32,sourceHeight=32,
            visibleBounds=bounds,hasTransparency=transparent,usages=info.get('usages',[info['usage']]),proposedUsages=info.get('usages',[info['usage']]),
            styleId=info['style'],identityFamily=info['id'],variantFamily=info['id'],
            framing=info.get('framing','tile'),inferredKind=info.get('kind','environment'),descriptionEn=info['descriptionEn'],traits={},technicalStatus='passed',
            semanticStatus='visually_described',artStatus='needs_in_game_review',metadataSource='individual_visual_review',
            provenance=dict(member=info.get('sourceMember',source.name),sourceKind=info.get('sourceKind','original_png'),sourceUrl=info['sourceUrl'],
                license=info.get('license','CC-BY-4.0'),author=info.get('author','David Gwizdala'),
                commit=info.get('commit'),licenseFile=info.get('licenseFile'))))
    return assets
