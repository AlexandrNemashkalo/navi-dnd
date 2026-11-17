"""Check the authored inventory bindings and their packaged image bytes."""
import hashlib
import json
from pathlib import Path
import unittest
from art_requirement_bindings import add_runtime_requirements

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT/'src/ArtLibrary'


class InventoryArtBindingsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.runtime = json.loads((LIB/'RuleSets/Dnd5e/runtime-pictures.json').read_text(encoding='utf-8'))
        cls.bindings = {row['key']: row for row in cls.runtime['bindings']}
        cls.images = {row['id']: row for row in cls.runtime['images']}

    def test_denominations_preserve_named_palette_through_source_pipeline(self):
        requirements = []
        add_runtime_requirements(ROOT, requirements)
        source = {row['id']: row for row in requirements}
        for metal, russian in [('copper', 'Медная монета'), ('silver', 'Серебряная монета'),
                               ('electrum', 'Электрумовая монета'), ('gold', 'Золотая монета'),
                               ('platinum', 'Платиновая монета')]:
            with self.subTest(metal=metal):
                key = 'curated:item:' + metal + '-coins'
                binding = self.bindings[key]
                expected = None if metal == 'gold' else metal
                self.assertEqual(expected, binding.get('palette'))
                self.assertEqual(expected, source[key].get('palette'))
                self.assertIn(metal.title()+' coin', binding['names'])
                self.assertIn(metal.title()+' pieces', binding['names'])
                self.assertIn(russian, binding['names'])
                self.assertEqual('prepared-items-native-32-32-treasure:icon-037', self.images[binding['imageId']]['sourceAssetId'])

    def test_vestments_and_incense_are_specific_objects_and_bytes_match(self):
        for key, name, asset in [('vestments', 'Облачение', 'prepared-items-native-32-batch2-05-cuirasses:icon-020'),
                                 ('incense', 'Палочка благовоний', 'own-art:incense-sticks-v4')]:
            row = self.bindings['curated:item:'+key]
            image = self.images[row['imageId']]
            self.assertIn(name, row['names'])
            self.assertEqual(asset, image['sourceAssetId'])
            self.assertEqual(row['imageId'], hashlib.sha256((LIB/image['path']).read_bytes()).hexdigest())
        for alias in ['delapouite/two-coins', 'lorc/crown-coin', 'delapouite/coins', 'delapouite/coins-pile']:
            self.assertEqual(self.bindings['curated:item:coins']['imageId'], self.runtime['engineAliases'][alias])
        self.assertEqual(self.bindings['curated:item:incense']['imageId'], self.runtime['engineAliases']['lorc/incense'])

    def test_previous_incense_source_remains_unchanged(self):
        source = LIB/'Sources/own-art/incense-sticks-v3.png'
        old = 'f1f3739686afca2849e74664747c0f3d498a35f96b07cd52581ecf902cb333b7'
        self.assertEqual(old, hashlib.sha256(source.read_bytes()).hexdigest())
        self.assertIn(old, self.images)
        self.assertEqual(['incense-sticks-v3'], self.bindings['legacy:item:incense-sticks-v3']['names'])


if __name__ == '__main__':
    unittest.main()
