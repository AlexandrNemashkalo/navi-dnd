"""Verify recovery from permanent sources after ZIP/review files are gone."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


class ArtImportRecoveryTests(unittest.TestCase):
    def test_permanent_sources_rebuild_native_icons_without_resurrecting_deleted_candidates(self):
        library=ROOT/'src/ArtLibrary'
        packs=json.loads((library/'packs.json').read_text(encoding='utf-8'))
        chosen=next(p for p in packs if p['id']=='craftpix-128598')
        excluded=next(p for p in packs if p['id']=='craftpix-945210')
        with tempfile.TemporaryDirectory(prefix='navidnd-art-import-') as temp:
            root=Path(temp);scripts=root/'scripts';scripts.mkdir()
            shutil.copyfile(ROOT/'scripts/import-art-packs.py',scripts/'import-art-packs.py')
            shutil.copyfile(ROOT/'scripts/art-pack-annotations.json',scripts/'art-pack-annotations.json')
            destination=root/'src/ArtLibrary';destination.mkdir(parents=True)
            shutil.copytree(library/'Sources'/chosen['id'],destination/'Sources'/chosen['id'])
            (destination/'packs.json').write_text(json.dumps([chosen,excluded]),encoding='utf-8')
            (destination/'excluded-packs.json').write_text(json.dumps({excluded['id']:{'reason':'user_excluded_64px'}}),encoding='utf-8')
            deleted_id=chosen['id']+':icon-001'
            (destination/'deletions.snapshot.json').write_text(json.dumps({deleted_id:{'id':deleted_id}}),encoding='utf-8')
            result=subprocess.run([sys.executable,str(scripts/'import-art-packs.py')],cwd=root,capture_output=True,text=True,timeout=40)
            self.assertEqual(0,result.returncode,result.stderr)
            output=root/'artifacts/art-catalog-review'
            assets=json.loads((output/'imported-assets.json').read_text(encoding='utf-8'))
            self.assertEqual(48,len(assets))
            self.assertTrue(all(a['pack']==chosen['id'] and (a['sourceWidth'],a['sourceHeight'])==(32,32) for a in assets))
            self.assertTrue(all(a['descriptionEn'] for a in assets))
            self.assertFalse((output/'imported'/chosen['id']/'icons/001.png').exists())
            self.assertTrue((output/'imported'/chosen['id']/'icons/002.png').exists())
            self.assertEqual(47,len(list((output/'imported'/chosen['id']/'icons').glob('*.png'))))


if __name__=='__main__':unittest.main()
