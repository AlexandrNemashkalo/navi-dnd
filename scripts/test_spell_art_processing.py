import unittest
from PIL import Image, ImageDraw
from icon_to_braille import convert_icon, pixel_data
from spell_art_processing import adjust_lines, dominant_line_color, pixel_icon


class SpellArtProcessingTests(unittest.TestCase):
    def test_title_uses_blue_stroke_color_instead_of_white_highlights(self):
        image = Image.new('RGBA', (64,64))
        draw = ImageDraw.Draw(image)
        draw.line((10,8,10,55), fill=(80,150,230,255), width=2)
        draw.line((20,8,20,55), fill=(255,255,255,255), width=3)
        self.assertEqual(dominant_line_color(image),[80,150,230])

    def test_pixel_grid_preserves_thin_stroke_rgb_without_dark_background_mixing(self):
        colors=Image.new('RGBA',(64,64))
        colors.putpixel((20,20),(40,120,240,255))
        background=Image.new('RGBA',(64,64),(30,32,36,255))
        result=pixel_icon(colors,background)
        self.assertEqual(result.size,(32,32))
        self.assertEqual(result.getpixel((10,10)),(40,120,240,255))
        self.assertEqual(result.getpixel((11,10)),(30,32,36,255))

    def test_faint_cyan_recovery_removes_brown_and_keeps_hue(self):
        image=Image.new('RGBA',(64,64),(15,15,20,255))
        draw=ImageDraw.Draw(image)
        draw.line((10,10,10,53),fill=(10,80,110,255),width=2)
        draw.line((30,10,30,53),fill=(180,140,90,255),width=2)
        mask,colors,_=convert_icon(image,mode='lines',threshold=40)
        mask,colors=adjust_lines(mask,colors,dict(hueRange=[170,225],brightnessGain=1.7,minComponent=8))
        self.assertEqual(colors.getpixel((10,30)),(17,136,187,255))
        self.assertEqual(mask.getpixel((10,30)),0)
        self.assertEqual(colors.getpixel((30,30))[3],0)
        self.assertEqual(mask.getpixel((30,30)),255)


if __name__ == '__main__':
    unittest.main()
