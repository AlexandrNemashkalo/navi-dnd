"""Render reviewed overlay examples onto exactly one 32x32 transparent canvas."""
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]


def render_recipe(root, recipe):
    if recipe.get('canvas') != [32,32] or recipe.get('mode') != 'alpha_overlay':
        raise ValueError('Only one 32x32 overlay canvas is supported')
    canvas=Image.new('RGBA',(32,32))
    for layer in recipe['layers']:
        path=(root/layer['path']).resolve()
        allowed=((root/'src/icons/dcss').resolve(),(root/'src/ArtLibrary/Sources').resolve())
        if path.suffix.lower()!='.png' or not any(path.is_relative_to(p) for p in allowed):
            raise ValueError('Composition layer outside managed PNG roots')
        raw=path.read_bytes()
        if hashlib.sha256(raw).hexdigest()!=layer['sha256']:
            raise ValueError('Composition layer changed; review recipe again')
        with Image.open(path) as original:
            if original.size!=(32,32):
                raise ValueError('Composition layer must already be 32x32; resizing is forbidden')
            canvas.alpha_composite(original.convert('RGBA'))
    return canvas


def main():
    recipes=json.loads((ROOT/'src/ArtLibrary/composition-examples.json').read_text(encoding='utf-8'))
    for recipe in recipes:
        target=(ROOT/recipe['preview']).resolve()
        if not target.is_relative_to((ROOT/'artifacts/art-catalog-review/dcss-details').resolve()):
            raise ValueError('Preview outside review cache')
        image=render_recipe(ROOT,recipe)
        target.parent.mkdir(parents=True,exist_ok=True);image.save(target)
    print(f'{len(recipes)} overlay examples rendered at 32x32; source PNGs untouched.')


if __name__=='__main__':main()
