"""Shared preparation for the two spell renderers and their title color."""
import colorsys
from PIL import Image
from icon_to_braille import pixel_data, remove_specks


def adjust_lines(mask, colors, profile):
    """Optional per-source recovery of faint colored strokes; never tint the backdrop."""
    dots, pixels = pixel_data(mask), pixel_data(colors)
    hue_range = profile.get('hueRange')
    gain = profile.get('brightnessGain', 1.0)
    for i, p in enumerate(pixels):
        if not p[3]:
            continue
        h, s, v = colorsys.rgb_to_hsv(*(c/255 for c in p[:3]))
        if hue_range and not (hue_range[0] <= h*360 <= hue_range[1] and s >= 0.30):
            dots[i] = 255
        elif gain != 1:
            pixels[i] = tuple(round(c*255) for c in colorsys.hsv_to_rgb(h, s, min(1, v*gain))) + (255,)
    active = [p == 0 for p in dots]
    remove_specks(active, mask.width, profile.get('minComponent', 3))
    result = Image.new('L', mask.size)
    result.putdata([0 if p else 255 for p in active])
    rgb = Image.new('RGBA', colors.size)
    rgb.putdata([p if on else (0, 0, 0, 0) for p, on in zip(pixels, active)])
    return result, rgb


def dominant_line_color(colors):
    pixels = [p[:3] for p in pixel_data(colors) if p[3] >= 128]
    if not pixels:
        raise ValueError('A spell icon must have visible strokes.')
    chromatic = [(p, colorsys.rgb_to_hsv(*(c/255 for c in p))) for p in pixels]
    chromatic = [(p, hsv) for p, hsv in chromatic if hsv[1] >= 0.25]
    if len(chromatic) >= len(pixels)*0.20:
        counts = [0]*36
        for _, (h, _, _) in chromatic:
            counts[int(h*36) % 36] += 1
        center = max(range(36), key=lambda i: sum(counts[(i+d) % 36] for d in (-1, 0, 1))) / 36
        pixels = [p for p, (h, _, _) in chromatic if min(abs(h-center), 1-abs(h-center)) <= 1/12]
    # Exclude white glints and the background; choose a typical real stroke hue.
    return [sorted(p[c] for p in pixels)[len(pixels)//2] for c in range(3)]


def pixel_icon(colors, background):
    """Exact 32x32 grid: average only stroke RGB inside each 2x2 source block.

    This keeps thin lines legible instead of mixing their color with dark background.
    """
    if colors.size != (64,64) or background.size != colors.size:
        raise ValueError('Spell layers must be 64x64.')
    output = background.resize((32,32), Image.Resampling.BOX)
    for y in range(32):
        for x in range(32):
            strokes = [colors.getpixel((x*2+dx,y*2+dy)) for dy in range(2) for dx in range(2)]
            strokes = [p for p in strokes if p[3] >= 128]
            if strokes:
                output.putpixel((x,y),tuple(round(sum(p[c] for p in strokes)/len(strokes)) for c in range(3))+(255,))
    return output
