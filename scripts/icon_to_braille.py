"""PNG -> binary lines, original line colors, soft background, standalone preview.

Requires Pillow. Works on Windows/macOS; no AI or terminal-specific image protocol.
"""
import argparse
import base64
import colorsys
from collections import deque
from io import BytesIO
import math
from pathlib import Path

from PIL import Image, ImageFilter, ImageOps

BITS = ((0, 1, 2, 6), (3, 4, 5, 7))


def pixel_data(image):
    # Pillow 14 replaces getdata; retain compatibility with earlier project installs.
    return list(image.get_flattened_data() if hasattr(image, 'get_flattened_data') else image.getdata())


def remove_specks(mask, size, minimum):
    remaining = set(i for i, on in enumerate(mask) if on)
    while remaining:
        seed = remaining.pop()
        component, pending = [seed], [seed]
        while pending:
            i = pending.pop()
            x, y = i % size, i // size
            for yy in range(max(0, y - 1), min(size, y + 2)):
                for xx in range(max(0, x - 1), min(size, x + 2)):
                    n = yy * size + xx
                    if n in remaining:
                        remaining.remove(n)
                        component.append(n)
                        pending.append(n)
        if len(component) < minimum:
            for i in component:
                mask[i] = False


def clean_line_palette(mask, colors):
    """Reject dim off-palette texture flecks without recoloring genuine strokes.

    The bright stroke cores supply the palette, including multiple real colors.
    Neutral highlights and bright accent colors remain; dim pixels must belong to
    a supported hue. Unlike a fixed brown blacklist, this preserves gold spells.
    """
    pixels = pixel_data(colors)
    visible = [(i, colorsys.rgb_to_hsv(*(c/255 for c in p[:3]))) for i, p in enumerate(pixels) if p[3] >= 128]
    if not visible:
        return mask.copy(), colors.copy()
    peak = max(hsv[2] for _, hsv in visible)
    cores = [hsv for _, hsv in visible if hsv[2] >= peak * 0.86]
    histogram = [0] * 36
    for h, s, _ in cores:
        if s >= 0.20:
            histogram[int(h*36) % 36] += 1
    minimum = max(8, len(cores) * 0.04)
    palette = [i/36 for i in range(36)
               if sum(histogram[(i+d) % 36] for d in (-1, 0, 1)) >= minimum]
    # A predominantly neutral core can also reject isolated colored backdrop flecks.
    neutral_core = sum(s < 0.20 for _, s, _ in cores) >= len(cores) * 0.75
    if not palette and not neutral_core:
        return mask.copy(), colors.copy()
    dots = pixel_data(mask.convert('L'))
    for i, (h, saturation, value) in visible:
        if saturation < 0.20 or value >= 210/255:
            continue
        distance = min((min(abs(h-c), 1-abs(h-c)) for c in palette), default=1)
        if distance > 1/12:
            dots[i] = 255
            pixels[i] = (0, 0, 0, 0)
    cleaned = Image.new('L', mask.size)
    cleaned.putdata(dots)
    rgb = Image.new('RGBA', colors.size)
    rgb.putdata(pixels)
    return cleaned, rgb


def edges(rgb, size, threshold):
    """Sobel, non-maximum suppression and connected weak edges; no filled areas."""
    blurred = pixel_data(rgb.filter(ImageFilter.GaussianBlur(0.65)))
    strength, direction = [0.0] * (size * size), [0] * (size * size)
    offsets = ((1, 0), (1, 1), (0, 1), (-1, 1))
    for y in range(1, size - 1):
        for x in range(1, size - 1):
            p = [blurred[(y + dy) * size + x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)]
            gradients = [(p[2][c] + 2*p[5][c] + p[8][c] - p[0][c] - 2*p[3][c] - p[6][c],
                          p[6][c] + 2*p[7][c] + p[8][c] - p[0][c] - 2*p[1][c] - p[2][c]) for c in range(3)]
            gx, gy = max(gradients, key=lambda g: g[0]*g[0] + g[1]*g[1])
            i = y * size + x
            strength[i] = math.hypot(gx, gy) / 4
            direction[i] = int(((math.degrees(math.atan2(gy, gx)) % 180) + 22.5) / 45) % 4
    thin = [0.0] * len(strength)
    for y in range(1, size - 1):
        for x in range(1, size - 1):
            i = y * size + x
            dx, dy = offsets[direction[i]]
            if strength[i] >= strength[(y-dy)*size+x-dx] and strength[i] >= strength[(y+dy)*size+x+dx]:
                thin[i] = strength[i]
    mask = [v >= threshold for v in thin]
    pending = deque(i for i, on in enumerate(mask) if on)
    while pending:
        i = pending.popleft()
        x, y = i % size, i // size
        for yy in range(max(1, y-1), min(size-1, y+2)):
            for xx in range(max(1, x-1), min(size-1, x+2)):
                n = yy * size + xx
                if not mask[n] and thin[n] >= threshold * 0.45:
                    mask[n] = True
                    pending.append(n)
    return mask


def fitted_source(source, size, inset):
    original = ImageOps.exif_transpose(source).convert('RGBA')
    if inset < 0 or inset * 2 >= min(original.size):
        raise ValueError('Inset must leave a nonempty image.')
    # Clear the optional UI rim without cropping/stretching the subject.
    clean = Image.new('RGBA', original.size)
    box = (inset, inset, original.width-inset, original.height-inset)
    clean.paste(original.crop(box), box[:2])
    fitted = ImageOps.contain(clean, (size, size), Image.Resampling.LANCZOS)
    rgba = Image.new('RGBA', (size, size))
    rgba.paste(fitted, ((size-fitted.width)//2, (size-fitted.height)//2))
    return rgba


def convert_icon(source, size=64, mode='auto', inset=0, threshold=140, edge_threshold=35, min_component=3):
    if size < 8 or size > 512 or size % 4:
        raise ValueError('Size must be a multiple of 4 between 8 and 512.')
    if not 1 <= threshold <= 255 or not 1 <= edge_threshold <= 255 or min_component < 1:
        raise ValueError('Thresholds must be 1..255 and minimum component must be positive.')
    if mode not in ('auto', 'lines', 'edges'):
        raise ValueError('Mode must be auto, lines or edges.')
    original = ImageOps.exif_transpose(source)
    rgba = fitted_source(original, size, inset)
    pixels = pixel_data(rgba)
    # Median interior perimeter estimates a solid light/dark background. Transparent
    # backgrounds are black for edge detection; their hidden RGB never participates.
    margin = max(1, round(inset * min(size/original.width, size/original.height)))
    perimeter = [rgba.getpixel((x, y)) for y in range(margin, size-margin)
                 for x in range(margin, size-margin)
                 if x in (margin, size-margin-1) or y in (margin, size-margin-1)]
    opaque = [p for p in perimeter if p[3] >= 128]
    if opaque:
        bg = tuple(sorted(p[c] for p in opaque)[len(opaque)//2] for c in range(3))
    else:
        visible = [p for p in pixels if p[3] >= 128]
        light = sum(max(p[:3]) for p in visible) / max(1, len(visible)) >= 128
        bg = (0, 0, 0) if light else (255, 255, 255)
    contrast = [max(abs(p[c]-bg[c]) for c in range(3)) if p[3] >= 128 else 0 for p in pixels]
    mask = [v >= threshold for v in contrast]
    chosen = mode
    if mode == 'auto':
        chosen = 'edges' if sum(mask) / max(1, sum(p[3] >= 128 for p in pixels)) > 0.24 else 'lines'
    if chosen == 'edges':
        composite = Image.new('RGBA', rgba.size, (*bg, 255))
        composite.alpha_composite(rgba)
        mask = edges(composite.convert('RGB'), size, edge_threshold)
    # Edge pixels use the nearby foreground color, never the background side of a contour.
    colors = Image.new('RGBA', rgba.size)
    for i, on in enumerate(mask):
        if not on:
            continue
        sample = i
        if chosen == 'edges':
            x, y = i % size, i // size
            neighbors = [yy*size+xx for yy in range(max(0,y-1),min(size,y+2))
                         for xx in range(max(0,x-1),min(size,x+2))]
            sample = max(neighbors, key=lambda n: contrast[n])
        if pixels[sample][3] < 128:
            mask[i] = False
        else:
            colors.putpixel((i % size, i // size), (*pixels[sample][:3], 255))
    remove_specks(mask, size, min_component)
    bw = Image.new('L', rgba.size, 255)
    bw.putdata([0 if on else 255 for on in mask])
    colors.putalpha(bw.point(lambda v: 255-v))
    return bw, colors, chosen


def build_background(source, mask, inset=0, feather=6.0):
    """A clean neutral field with a restrained hint of the line hue.

    No source texture or local glow is copied: both made low-resolution terminal
    cells look muddy. This field fills the display independently of the line mask.
    """
    if mask.width != mask.height or not math.isfinite(feather) or feather < 0:
        raise ValueError('A square mask and finite nonnegative feather are required.')
    rgba = fitted_source(source, mask.width, inset)
    pixels, dots = pixel_data(rgba), pixel_data(mask.convert('L'))
    backdrop = [p for p, dot in zip(pixels, dots) if p[3] >= 128 and dot >= 128]
    if not backdrop:
        return Image.new('RGBA', mask.size)
    strokes = [p for p, dot in zip(pixels, dots) if p[3] >= 128 and dot < 128]
    hue = [sorted(p[c] for p in strokes)[len(strokes)//2] for c in range(3)] if strokes else [128]*3
    mean = sum(hue)/3
    tint = [max(-3, min(3, (c-mean)*0.05)) for c in hue]
    background = Image.new('RGBA', mask.size)
    for y in range(mask.height):
        for x in range(mask.width):
            nx, ny = 2*x/(mask.width-1)-1, 2*y/(mask.height-1)-1
            lift = max(0, 1-(nx*nx+ny*ny)/2)
            lift = lift*lift*(3-2*lift)
            rgb = tuple(round(base+(6+t)*lift) for base,t in zip((28,30,34),tint))
            # Fade at the full canvas edges, not at the cropped source bounds.
            distance = min(x, mask.width-1-x, y, mask.height-1-y)
            t = min(1.0, distance/feather) if feather else 1.0
            background.putpixel((x,y), (*rgb, round(255*t*t*(3-2*t))))
    return background


def background_cells(background):
    """One alpha-weighted background per 2x4 terminal cell, matching the game."""
    rows = []
    for y in range(0, background.height, 4):
        row = []
        for x in range(0, background.width, 2):
            samples = [background.getpixel((x+px,y+py)) for px in range(2) for py in range(4)]
            alpha = sum(p[3] for p in samples)
            row.append((*[sum(p[c]*p[3] for p in samples)//alpha for c in range(3)], alpha//8) if alpha else (0,0,0,0))
        rows.append(row)
    return rows


def braille_cells(mask, colors):
    mask = mask.convert('L')
    colors = colors.convert('RGBA')
    rows, color_rows = [], []
    for y in range(0, mask.height, 4):
        chars, cells = [], []
        for x in range(0, mask.width, 2):
            code, samples = 0, []
            for px in range(2):
                for py in range(4):
                    if mask.getpixel((x+px, y+py)) < 128:
                        code |= 1 << BITS[px][py]
                        samples.append(colors.getpixel((x+px,y+py)))
            chars.append(chr(0x2800 + code))
            cells.append('#%02x%02x%02x' % tuple(sum(p[c] for p in samples)//len(samples) for c in range(3)) if samples else None)
        rows.append(''.join(chars))
        color_rows.append(cells)
    return rows, color_rows


def preview_layers(mask, colors, background=None, width_chars=36):
    """Match the game's separate symbol/backdrop layouts for the technical preview."""
    if width_chars < 2:
        raise ValueError('Preview width must be at least two characters.')
    width, height = width_chars*2, width_chars//2*4
    scale = min(width/mask.width, height/mask.height)
    if scale >= 1:
        scale = math.floor(scale)
    fitted = (max(1,int(mask.width*scale)), max(1,int(mask.height*scale)))
    position = ((width-fitted[0])//2, (height-fitted[1])//2)
    geometry = Image.new('L',(width,height),255)
    geometry.paste(mask.resize(fitted,Image.Resampling.NEAREST),position)
    rgb = Image.new('RGBA',(width,height))
    rgb.paste(colors.resize(fitted,Image.Resampling.NEAREST),position)
    backdrop = background.resize((width,height),Image.Resampling.NEAREST) if background else None
    return geometry, rgb, backdrop


def preview_html(original, mask, colors, background=None):
    def uri(img):
        stream = BytesIO()
        img.save(stream, format='PNG')
        return 'data:image/png;base64,' + base64.b64encode(stream.getvalue()).decode('ascii')
    displayed_mask, displayed_colors, displayed_background = preview_layers(mask, colors, background)
    rows, palette = braille_cells(displayed_mask, displayed_colors)
    def art(backgrounds=None):
        result = []
        for y, (row, cells) in enumerate(zip(rows,palette)):
            spans = []
            for x, (char, color) in enumerate(zip(row,cells)):
                style = f'color:{color};' if color else ''
                if backgrounds:
                    r,g,b,a = backgrounds[y][x]
                    style += f'background:rgba({r},{g},{b},{a/255:.5f})'
                spans.append(f'<span style="{style}">{char}</span>')
            result.append(''.join(spans))
        return '\n'.join(result)
    return ('<!doctype html><meta charset="utf-8"><title>Цветной Брайль</title>'
            '<style>body{background:#171b20;color:#ddd;font:16px system-ui;margin:32px}'
            'main{display:flex;gap:32px;flex-wrap:wrap}img{width:256px;height:256px;object-fit:contain;image-rendering:pixelated}'
            'pre{font:16px/2 Consolas,monospace;letter-spacing:0;margin:0}pre span{display:inline-block;width:1ch;height:2em;vertical-align:top}h2{font-size:16px}</style>'
            '<h1>Иконка → линии → цветной Брайль</h1><main>' +
            ''.join(f'<section><h2>{label}</h2><img src="{uri(img)}"></section>' for label,img in
                    [('Оригинал',original),('Ч/б маска',mask),('Цвет линий',colors)]) +
            f'<section><h2>Только линии · {displayed_mask.width//2}×{displayed_mask.height//4}</h2><pre>{art()}</pre></section>' +
            (f'<section><h2>Линии и нейтральный фон</h2><pre>{art(background_cells(displayed_background))}</pre></section>' if displayed_background else '') + '</main>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path, help='Output stem: .mask.png, .color.png, .background.png, .html')
    parser.add_argument('--size', type=int, default=64)
    parser.add_argument('--mode', choices=['auto', 'lines', 'edges'], default='auto')
    parser.add_argument('--inset', type=int, default=0, help='Ignore this many source pixels at each border')
    parser.add_argument('--threshold', type=int, default=140, help='Line/background RGB contrast, 1..255')
    parser.add_argument('--edge-threshold', type=int, default=35)
    parser.add_argument('--min-component', type=int, default=3)
    parser.add_argument('--clean-palette', action='store_true', help='Remove dim texture flecks outside the bright stroke palette')
    parser.add_argument('--background-feather', type=float, default=6.0)
    parser.add_argument('--force', action='store_true')
    args = parser.parse_args()
    targets = [Path(str(args.output)+suffix) for suffix in ('.mask.png','.color.png','.background.png','.html')]
    if any(p.resolve() == args.input.resolve() for p in targets):
        parser.error('Input cannot be an output file.')
    if not args.force and any(p.exists() for p in targets):
        parser.error('Output already exists; choose a new stem or pass --force.')
    with Image.open(args.input) as original:
        mask, colors, chosen = convert_icon(original, args.size, args.mode, args.inset, args.threshold, args.edge_threshold, args.min_component)
        if args.clean_palette:
            mask, colors = clean_line_palette(mask, colors)
        background = build_background(original, mask, args.inset, args.background_feather)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        mask.save(targets[0])
        colors.save(targets[1])
        background.save(targets[2])
        targets[3].write_text(preview_html(original, mask, colors, background), encoding='utf-8')
    print(f'{chosen}: {mask.width}x{mask.height}; preview: {targets[3]}')


if __name__ == '__main__':
    main()
