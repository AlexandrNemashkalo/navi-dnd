"""Verify inspected scenery subjects, source provenance and saved-image compatibility."""
import hashlib
import json
from pathlib import Path
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT/'src/ArtLibrary'


class SceneryArtBindingsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.runtime = json.loads((LIB/'RuleSets/Dnd5e/runtime-pictures.json').read_text(encoding='utf-8'))
        cls.bindings = {r['key']: r for r in cls.runtime['bindings']}
        cls.images = {r['id']: r for r in cls.runtime['images']}

    def test_named_scenery_uses_specific_reviewed_subjects(self):
        for key, asset in [('curated:prop:window', 'window-shaded-v1'),
                           ('engine:terrain:Plank floor', 'plank-floor-shaded-v1'),
                           ('curated:item:cart-axle', 'cart-axle-shaded-v1'),
                           ('engine:furniture:hearth', 'hearth-shaded-v1'),
                           ('engine:furniture:cabinet', 'cabinet-shaded-v1'),
                           ('engine:furniture:table', 'table-shaded-v1'),
                           ('engine:furniture:barrels', 'barrel-shaded-v1'),
                           ('engine:furniture:counter', 'bar-counter-shaded-v1'),
                           ('engine:furniture:bench', 'bench-shaded-v1'),
                           ('engine:terrain:Thicket', 'forest-thicket-v1')]:
            with self.subTest(key=key):
                image = self.images[self.bindings[key]['imageId']]
                self.assertEqual('own-art:'+asset, image['sourceAssetId'])
                self.assertEqual(image['id'], hashlib.sha256((LIB/image['path']).read_bytes()).hexdigest())
        axle = self.bindings['curated:item:cart-axle']
        self.assertTrue({'item', 'feature'} <= set(axle['categories']))
        self.assertTrue({'Cart axle', 'Spare axle', 'Axle', 'Запасная ось', 'Ось повозки'} <= set(axle['names']))
        floor = self.bindings['engine:terrain:Plank floor']
        self.assertIn('terrain', floor['categories'])
        self.assertIn('Дощатый пол', floor['names'])
        self.assertNotIn('delapouite/planks', self.runtime['engineAliases'])

    def test_engine_aliases_select_places_and_objects_instead_of_furniture_or_grates(self):
        for alias, key in [('delapouite/village', 'engine:place:village'),
                           ('delapouite/castle', 'engine:place:capital'),
                           ('delapouite/window-bars', 'curated:prop:window'),
                           ('delapouite/barrel', 'engine:furniture:barrels'),
                           ('delapouite/forest', 'engine:terrain:Thicket')]:
            self.assertEqual(self.bindings[key]['imageId'], self.runtime['engineAliases'][alias])
        for key in ['engine:place:village', 'engine:place:capital']:
            self.assertEqual('place_view', self.bindings[key]['usage'])

    def test_tall_grass_has_its_own_muted_pixel_picture(self):
        grass = self.bindings['engine:terrain:Tall grass']
        self.assertEqual('own-art:tall-grass-shaded-v1', self.images[grass['imageId']]['sourceAssetId'])
        self.assertTrue({'terrain', 'feature'} <= set(grass['categories']))
        self.assertEqual(['Tall grass', 'Высокая трава'], grass['names'])
        self.assertEqual(grass['imageId'], self.runtime['engineAliases']['delapouite/high-grass'])
        self.assertNotEqual(grass['imageId'], self.runtime['engineAliases']['delapouite/grass'])

    def test_bar_counter_names_and_terrain_select_a_closed_counter(self):
        counter = self.bindings['engine:furniture:counter']
        terrain = self.bindings['engine:terrain:Counter']
        self.assertEqual(counter['imageId'], terrain['imageId'])
        self.assertTrue({'Counter', 'Bar counter', 'Барная стойка', 'Стойка'} <= set(counter['names']))
        self.assertIn('terrain', terrain['categories'])
        self.assertEqual(counter['imageId'], self.runtime['engineAliases']['delapouite/bar-stool'])
        self.assertIn('02e4c32077cf6606447e72550b904a681160354247e3379f72bdb5172c1e127a', self.images)

    def test_bench_names_and_terrain_select_reviewed_wooden_bench(self):
        bench = self.bindings['engine:furniture:bench']
        terrain = self.bindings['engine:terrain:Bench']
        self.assertEqual(bench['imageId'], terrain['imageId'])
        self.assertTrue({'Bench', 'Wooden bench', 'Скамья', 'Скамейка', 'Деревянная скамья'} <= set(bench['names']))
        self.assertTrue({'terrain', 'feature'} <= set(terrain['categories']))
        self.assertEqual(bench['imageId'], self.runtime['engineAliases']['delapouite/park-bench'])
        self.assertIn('6e8cecbc0ccffc8f09d545321517245ab4395ea74beefaf235bbec1f4ac26713', self.images)

    def test_generated_assets_have_archived_sources_exact_prompts_and_transparency(self):
        for key in ['window', 'plank-floor', 'cart-axle', 'hearth', 'cabinet', 'table', 'barrel', 'tall-grass', 'bar-counter', 'bench']:
            with self.subTest(key=key):
                source = LIB/'Sources/own-art'/(key+'-shaded-v1.png')
                meta = json.loads(source.with_suffix('.provenance.json').read_text(encoding='utf-8'))
                self.assertEqual(meta['sha256'], hashlib.sha256(source.read_bytes()).hexdigest())
                self.assertTrue((ROOT/meta['sourcePath']).is_file())
                self.assertIn('Asset type:', (ROOT/meta['promptTemplate']).read_text(encoding='utf-8'))
                with Image.open(source) as image:
                    self.assertEqual((32,32), image.size)
                    self.assertEqual((0,255), image.getchannel('A').getextrema())

    def test_old_published_pictures_remain_readable(self):
        for old in ['4482c67ad532344411c676721ea69004a2968ea4f768b72d2c245d13d3eca46b',
                    '4c204edda44a2c71e3aff9561bc5cad92b4d7fe383e465c128eb50c5cad35d22',
                    'b6849cb5f0b0d46da79edc93f21c77e93671bddab40ea16337012a34e9787b38',
                    '66cfd1d77438cc031d4aaa65492b041497179042844c6abf57ef800b4fdea2d1',
                    'a71f40526c2ed948bcbc873ccdb2987730e117cbd3398315cb5b584ce4fbcc3d']:
            self.assertIn(old, self.images)


if __name__ == '__main__':
    unittest.main()
