"""Load project-created images into the review gallery and draft catalog."""
import hashlib
import json
from PIL import Image


def load_own_assets(root):
    folder = root / 'src/ArtLibrary/Sources/own-art'
    review = root / 'artifacts/art-catalog-review'
    deleted_path = review / 'deletions.json'
    snapshot = root / 'src/ArtLibrary/deletions.snapshot.json'
    deleted = json.loads((deleted_path if deleted_path.exists() else snapshot).read_text(encoding='utf-8')) if deleted_path.exists() or snapshot.exists() else {}
    assets = []
    for metadata in sorted(folder.glob('*.provenance.json')):
        info = json.loads(metadata.read_text(encoding='utf-8'))
        if info.get('published') is False:
            continue
        # Earlier mixed weapon/status samples stay archived; only named spells are active.
        if info.get('sourceKind') == 'bg3_line_mask':
            continue
        if info['id'] in deleted:
            continue
        source = folder / info['file']
        if source.parent != folder or source.suffix.lower() != '.png':
            raise ValueError(f'Invalid own-art file: {source}')
        raw = source.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != info['sha256']:
            raise ValueError(f'Own-art checksum mismatch: {source}')
        with Image.open(source) as image:
            if image.size != (info['width'], info['height']):
                raise ValueError(f'Own-art dimensions differ from metadata: {source}')
            width, height = image.size
            alpha = image.convert('RGBA').getchannel('A')
            bounds = alpha.getbbox()
            transparent = alpha.getextrema()[0] < 255
        cache_name = source.stem + '-' + digest[:12] + '.png' if info.get('colorFile') else source.name
        target = review / 'imported/own-art/icons' / cache_name
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists() and target.read_bytes() != raw:
            raise ValueError(f'Edited own-art cache preserved: {target}')
        if not target.exists():
            target.write_bytes(raw)
        assets.append(dict(
            id=info['id'], pack='own-art', label=info['subject'], labelRu=info.get('nameRu',''),
            path=target.relative_to(review).as_posix(), preview=target.relative_to(review).as_posix(),
            sha256=digest, sourceSha256=digest, width=width, height=height,
            sourceWidth=info.get('sourceDimensions',[width,height])[0], sourceHeight=info.get('sourceDimensions',[width,height])[1], hasTransparency=transparent, visibleBounds=bounds,
            usages=[info['usage']], proposedUsages=[info['usage']], inferredKind=info['usage'],
            styleId=info.get('styleId', 'fantasy.pixel.portrait.32'), identityFamily=info['id'], variantFamily=info['id'],
            framing=info.get('framing', 'head_shoulders'), gender='unknown', species='unknown',
            descriptionEn=info.get('descriptionEn', 'Elderly wizard with a pointed grey hat, bushy eyebrows and a long grey beard.'),
            traits={}, technicalStatus='passed' if bounds else 'needs_review',
            semanticStatus='visually_described', artStatus='needs_in_game_review',
            metadataSource='own_art_visual_review', transform=info.get('transform','none_on_import'),
            provenance=dict(member=source.name, sourceKind=info.get('sourceKind', 'ai_generated'),
                            origin=info['origin'], **{key: info[key] for key in
                            ('sourcePath', 'license', 'licenseRef', 'author') if key in info})))
        if info.get('sourceKind') == 'bg3_line_mask':
            for field in ('color', 'background'):
                if not info.get(field + 'File'):
                    continue
                layer = folder / info[field + 'File']
                if layer.parent != folder or hashlib.sha256(layer.read_bytes()).hexdigest() != info[field + 'Sha256']:
                    raise ValueError(f'Invalid {field} layer: {layer}')
                with Image.open(layer) as pixels:
                    if pixels.size != (width, height):
                        raise ValueError(f'Layer dimensions differ: {layer}')
    return assets
