"""Prepare an explicitly chosen generated source as an exact 32x32 own-art asset."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('key')
parser.add_argument('--name', required=True)
parser.add_argument('--name-ru', required=True)
parser.add_argument('--description', required=True)
parser.add_argument('--usage', choices=['portrait','item','prop','place_view','map_actor'], required=True)
parser.add_argument('--colors', type=int, choices=range(2,257), help='Optional reviewed palette reduction; avoid for tiny facial highlights')
args = parser.parse_args()
if not args.key or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789-' for c in args.key):
    parser.error('Use a lowercase filename key')
folder = ROOT/'src/ArtLibrary/Sources/own-art'
archive = folder/'generation-sources'/args.key
archive.mkdir(parents=True, exist_ok=True)
source = args.source.resolve()
original = archive/'original.png'
if original.exists() and original.read_bytes() != source.read_bytes():
    raise ValueError('Existing source preserved; choose a new versioned key')
if original.resolve() != source: shutil.copy2(source, original)
with Image.open(original) as image:
    source_size = image.size
    image = image.convert('RGBA')
    if image.width != image.height:
        side = max(image.size)
        square = Image.new('RGBA', (side,side))
        square.paste(image, ((side-image.width)//2,(side-image.height)//2))
        image = square
    final = image.resize((32,32), Image.Resampling.NEAREST)
    final.putalpha(final.getchannel('A').point(lambda value: 255 if value >= 128 else 0))
    if args.colors:
        alpha=final.getchannel('A')
        final=final.convert('RGB').quantize(colors=args.colors,method=Image.Quantize.MAXCOVERAGE,dither=Image.Dither.NONE).convert('RGBA')
        final.putalpha(alpha)
target = folder/(args.key+'.png')
if target.exists(): raise ValueError('Existing final preserved; choose a new versioned key')
final.save(target)
with Image.open(target) as check:
    assert check.size == (32,32) and check.mode == 'RGBA'
    assert check.getchannel('A').getbbox() is not None
metadata = dict(id='own-art:'+args.key,file=target.name,subject=args.name,nameRu=args.name_ru,
    width=32,height=32,sha256=hashlib.sha256(target.read_bytes()).hexdigest(),usage=args.usage,
    framing='head_shoulders' if args.usage=='portrait' else 'landscape' if args.usage=='place_view' else 'isolated_object',
    styleId='fantasy.pixel.'+args.usage+'.32',descriptionEn=args.description,
    sourceKind='ai_generated',origin='Built-in image_gen; explicitly prepared and visually reviewed for NaviDnD.',
    sourcePath=original.relative_to(ROOT).as_posix(),sourceDimensions=list(source_size),
    transform='nearest_neighbor_to_32; alpha_threshold_128; '+('maxcoverage_palette_'+str(args.colors)+'_no_dither' if args.colors else 'no_palette_quantization'),
    promptTemplate='src/ArtLibrary/Sources/own-art/pixel-generation-prompt.txt')
target.with_suffix('.provenance.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(str(target), '32x32 RGBA')
