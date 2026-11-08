import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from art_external_assets import load_external_assets


class ExternalAssetsTests(unittest.TestCase):
    def test_native_source_hash_and_user_deletion(self):
        raw=(Path(__file__).resolve().parents[1]/'src/ArtLibrary/Sources/gwiztiles/spr_grass_center.png').read_bytes()
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);library=root/'src/ArtLibrary';folder=library/'Sources/test';folder.mkdir(parents=True)
            source=folder/'grass.png';source.write_bytes(raw)
            record=dict(id='test:grass',pack='test',file='grass.png',name='grass',usage='terrain_tile',style='test',
                descriptionEn='Green grass.',sha256=hashlib.sha256(raw).hexdigest(),sourceUrl='https://example.org/grass.png',commit='fixed')
            (library/'external-assets.json').write_text(json.dumps([record]),encoding='utf-8')
            assets=load_external_assets(root);self.assertEqual((32,32),(assets[0]['width'],assets[0]['height']))
            source.write_bytes(raw+b'changed')
            with self.assertRaisesRegex(ValueError,'Changed external'):load_external_assets(root)
            source.write_bytes(raw)
            cache=root/'artifacts/art-catalog-review'/assets[0]['path'];cache.unlink()
            (library/'deletions.snapshot.json').write_text(json.dumps({'test:grass':{}}),encoding='utf-8')
            self.assertEqual([],load_external_assets(root));self.assertFalse(cache.exists())


if __name__=='__main__':unittest.main()
