import unittest
from PIL import Image, ImageDraw
from icon_to_braille import convert_icon, braille_cells, build_background, background_cells, pixel_data, preview_layers, clean_line_palette


class LineConversionTests(unittest.TestCase):
    def test_blue_strokes_lose_brown_texture_but_retain_their_dim_blue_edges(self):
        image = Image.new('RGB', (64,64), (20,20,25))
        draw = ImageDraw.Draw(image)
        draw.line((20,10,20,53), fill=(90,190,250), width=3)
        draw.line((22,10,22,53), fill=(60,125,190), width=1)
        draw.line((36,38,41,42), fill=(181,146,107), width=2)
        mask, colors, _ = convert_icon(image, mode='lines', threshold=115)
        self.assertEqual(mask.getpixel((39,40)),0)
        cleaned, rgb = clean_line_palette(mask, colors)
        self.assertEqual(cleaned.getpixel((39,40)),255)
        self.assertEqual(rgb.getpixel((39,40))[3],0)
        self.assertEqual(rgb.getpixel((22,25)),(60,125,190,255))
        self.assertEqual(rgb.getpixel((20,25)),(90,190,250,255))

    def test_palette_cleanup_preserves_genuine_multiple_colors_and_gold_shading(self):
        image = Image.new('RGB', (64,64), (20,20,25))
        draw = ImageDraw.Draw(image)
        draw.line((18,8,18,55), fill=(80,190,250), width=3)
        draw.line((40,8,40,55), fill=(250,200,80), width=3)
        draw.line((42,8,42,55), fill=(185,145,65), width=1)
        mask, colors, _ = convert_icon(image, mode='lines', threshold=115)
        cleaned, rgb = clean_line_palette(mask, colors)
        self.assertEqual(cleaned.tobytes(),mask.tobytes())
        self.assertEqual(rgb.tobytes(),colors.tobytes())

    def test_colored_strokes_keep_original_rgb_and_binary_geometry(self):
        image = Image.new('RGB', (64,64), (10,15,20))
        draw = ImageDraw.Draw(image)
        draw.line((12,8,12,55), fill=(45,180,245), width=2)
        draw.line((40,8,40,55), fill=(240,190,35), width=2)
        mask, colors, mode = convert_icon(image)
        self.assertEqual(mode, 'lines')
        self.assertEqual(set(pixel_data(mask)), {0,255})
        self.assertEqual(colors.getpixel((12,20)), (45,180,245,255))
        self.assertEqual(colors.getpixel((40,20)), (240,190,35,255))
        self.assertEqual(colors.getpixel((20,20))[3], 0)
        rows, palette = braille_cells(mask,colors)
        self.assertEqual((len(rows),len(rows[0])), (16,32))
        self.assertEqual(palette[5][6], '#2db4f5')

    def test_filled_icon_is_an_outline_with_empty_center(self):
        image = Image.new('RGBA',(64,64))
        ImageDraw.Draw(image).rectangle((12,12,51,51),fill=(45,180,245,255))
        mask, colors, mode = convert_icon(image)
        self.assertEqual(mode, 'edges')
        self.assertEqual(mask.getpixel((32,32)),255)
        self.assertTrue(80 < sum(p==0 for p in pixel_data(mask)) < 400)
        self.assertTrue(all(c[:3] == (45,180,245) for c in pixel_data(colors) if c[3]))

    def test_hidden_rgb_is_ignored_and_dark_transparent_strokes_survive(self):
        image = Image.new('RGBA',(64,64),(255,0,255,0))
        ImageDraw.Draw(image).line((8,32,55,32),fill=(0,0,0,255),width=2)
        mask, colors, _ = convert_icon(image, mode='lines')
        self.assertEqual(mask.getpixel((0,0)),255)
        self.assertEqual(mask.getpixel((32,32)),0)
        self.assertEqual(colors.getpixel((32,32)),(0,0,0,255))

    def test_ui_border_is_removed_without_stretching_the_subject(self):
        image = Image.new('RGB',(64,64),'black')
        draw = ImageDraw.Draw(image)
        draw.rectangle((0,0,63,63),outline='yellow',width=4)
        draw.line((20,20,40,40),fill='white',width=2)
        mask, _, _ = convert_icon(image, mode='lines', inset=5)
        self.assertEqual(mask.getpixel((1,20)),255)
        self.assertEqual(mask.getpixel((30,30)),0)

    def test_background_is_neutral_and_does_not_copy_source_texture(self):
        image = Image.new('RGB',(64,64))
        for y in range(64):
            for x in range(64):
                image.putpixel((x,y),(20+x,35,70))
        ImageDraw.Draw(image).line((30,10,30,53),fill='white',width=2)
        mask, _, _ = convert_icon(image, mode='lines')
        bg = build_background(image,mask)
        self.assertEqual(bg.getpixel((0,32))[3],0)
        self.assertTrue(0 < bg.getpixel((2,32))[3] < 255)
        self.assertEqual(bg.getpixel((32,32))[3],255)
        # White strokes get a neutral backdrop; source shading/texture is discarded.
        r,g,b,_ = bg.getpixel((30,32))
        self.assertTrue(28 <= min(r,g,b) <= max(r,g,b) <= 44)
        self.assertLessEqual(max(r,g,b)-min(r,g,b),8)
        self.assertEqual(bg.getpixel((50,32)),bg.getpixel((13,32)))
        clean = Image.new('RGB',(64,64),(15,20,25))
        ImageDraw.Draw(clean).line((30,10,30,53),fill='white',width=2)
        self.assertEqual(bg.tobytes(),build_background(clean,mask).tobytes())

    def test_preview_expands_background_in_both_axes_without_scaling_symbol(self):
        image = Image.new('RGB',(64,64),(15,20,25))
        ImageDraw.Draw(image).line((30,10,30,53),fill='white',width=2)
        mask,colors,_ = convert_icon(image,mode='lines',inset=5)
        background = build_background(image,mask,inset=5)
        geometry,rgb,backdrop = preview_layers(mask,colors,background)
        self.assertEqual(geometry.size,(72,72))
        self.assertEqual(geometry.crop((4,4,68,68)).tobytes(),mask.tobytes())
        self.assertEqual(rgb.crop((4,4,68,68)).tobytes(),colors.tobytes())
        self.assertGreater(backdrop.getpixel((2,36))[3],0)
        self.assertGreater(backdrop.getpixel((36,2))[3],0)
        self.assertGreater(backdrop.getpixel((69,36))[3],0)
        self.assertGreater(backdrop.getpixel((36,69))[3],0)

    def test_no_background_is_invented_for_isolated_transparent_lines(self):
        image = Image.new('RGBA',(64,64),(255,0,255,0))
        ImageDraw.Draw(image).line((10,10,50,50),fill=(200,230,255,255),width=2)
        mask, _, _ = convert_icon(image, mode='lines')
        self.assertIsNone(build_background(image,mask).getbbox())

    def test_background_cell_excludes_hidden_rgb_and_keeps_coverage(self):
        bg = Image.new('RGBA',(2,4),(255,0,255,0))
        bg.putpixel((0,0),(40,100,180,255))
        self.assertEqual(background_cells(bg),[[(40,100,180,31)]])


if __name__ == '__main__':
    unittest.main()
