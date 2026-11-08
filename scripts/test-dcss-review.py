"""DCSS review safety: complete captions, stable bytes, no implicit deletion."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('describe_dcss', ROOT/'scripts/describe-dcss-art.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
render_spec=importlib.util.spec_from_file_location('render_compositions',ROOT/'scripts/render-art-compositions.py')
renderer=importlib.util.module_from_spec(render_spec);render_spec.loader.exec_module(renderer)


class DcssReviewTests(unittest.TestCase):
    def test_every_surviving_reviewed_png_has_observed_caption_and_unchanged_hash(self):
        index = json.loads((ROOT/'src/ArtLibrary/dcss-review-index.json').read_text(encoding='utf-8'))
        for batch in index:
            for position, record in enumerate(batch['assets'],1):
                source = ROOT/record['path']
                if not source.exists():
                    continue
                self.assertIn((int(batch['sheet']),position),module.OBSERVATIONS)
                self.assertEqual(record['sha256'],hashlib.sha256(source.read_bytes()).hexdigest())
        annotations = json.loads((ROOT/'src/ArtLibrary/dcss-annotations.json').read_text(encoding='utf-8'))
        self.assertEqual('overlay_32',annotations['dcss/player/body/robe_blue']['composition']['mode'])
        self.assertNotIn('retentionReview',annotations['dcss/player/body/robe_blue'])
        self.assertEqual('overlay_32',annotations['dcss/monster/panlord/demon_head_brain_top']['composition']['mode'])
        self.assertTrue(annotations['dcss/monster/panlord/demon_head_brain_top']['retentionReview']['userApproved'])

    def run_fixture(self, changed=False):
        with tempfile.TemporaryDirectory(prefix='navidnd-dcss-review-') as temp:
            root=Path(temp);library=root/'src/ArtLibrary';library.mkdir(parents=True)
            png=root/'src/icons/dcss/example.png';png.parent.mkdir(parents=True);png.write_bytes(b'original fixture bytes')
            record=dict(id='dcss/example',path='src/icons/dcss/example.png',sha256=hashlib.sha256(png.read_bytes()).hexdigest())
            absent=dict(id='dcss/removed',path='src/icons/dcss/removed.png',sha256='0'*64)
            (library/'dcss-review-index.json').write_text(json.dumps([dict(sheet='001',assets=[record,absent])]),encoding='utf-8')
            new=png.with_name('unreviewed.png');new.write_bytes(b'new fixture bytes')
            if changed:png.write_bytes(b'edited fixture bytes')
            saved_root,saved_library=module.ROOT,module.LIBRARY
            module.ROOT,module.LIBRARY=root,library
            try:
                if changed:
                    with self.assertRaisesRegex(ValueError,'changed'):
                        module.main()
                else:
                    with contextlib.redirect_stdout(io.StringIO()):module.main()
                    report=json.loads((library/'dcss-description-coverage.json').read_text(encoding='utf-8'))
                    self.assertEqual(1,report['described'])
                    self.assertEqual(['dcss/unreviewed'],report['missing'])
                    self.assertFalse((png.parent/'removed.png').exists())
                self.assertEqual(b'edited fixture bytes' if changed else b'original fixture bytes',png.read_bytes())
                self.assertEqual(b'new fixture bytes',new.read_bytes())
            finally:
                module.ROOT,module.LIBRARY=saved_root,saved_library

    def test_deleted_png_stays_deleted_and_new_png_remains_pending(self):
        self.run_fixture()

    def test_changed_png_cannot_receive_stale_caption(self):
        self.run_fixture(changed=True)

    def test_retained_upper_layers_render_inside_one_32_square_without_source_changes(self):
        recipes=json.loads((ROOT/'src/ArtLibrary/composition-examples.json').read_text(encoding='utf-8'))
        for recipe in recipes:
            before={layer['path']:(ROOT/layer['path']).read_bytes() for layer in recipe['layers']}
            image=renderer.render_recipe(ROOT,recipe)
            self.assertEqual((32,32),image.size)
            self.assertIsNotNone(image.getbbox())
            self.assertTrue(all('_bottom' not in layer['path'] for layer in recipe['layers']))
            for path,raw in before.items():self.assertEqual(raw,(ROOT/path).read_bytes())
        with self.assertRaisesRegex(ValueError,'32x32'):
            renderer.render_recipe(ROOT,dict(recipes[0],canvas=[64,32]))


if __name__ == '__main__':
    unittest.main()
