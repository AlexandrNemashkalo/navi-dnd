"""Deletion tests use disposable fixtures, never the user's image library."""
import hashlib
import importlib.util
import json
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

spec=importlib.util.spec_from_file_location('art_review_server',Path(__file__).with_name('serve-art-review.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class ReviewDeletionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)
        self.review=self.root/'artifacts/art-catalog-review';self.review.mkdir(parents=True)
        self.target=self.review/'imported/pack/icons/001.png';self.target.parent.mkdir(parents=True)
        self.target.write_bytes(b'fixture PNG data')
        self.sha=hashlib.sha256(self.target.read_bytes()).hexdigest()
        self.records=[dict(id='test:001',path='imported/pack/icons/001.png',sha256=self.sha)]
        self.write_assets()
        self.store=module.ReviewStore(self.root)
        (self.review/'index.html').write_text('<html><script>window.ready=true;</script></html>',encoding='utf-8')

    def tearDown(self):self.temp.cleanup()
    def write_assets(self):(self.review/'assets.json').write_text(json.dumps(self.records),encoding='utf-8')

    def test_actual_file_deleted_and_tombstone_persisted(self):
        result=self.store.delete('test:001',self.sha)
        self.assertTrue(result['fileRemoved']);self.assertFalse(self.target.exists())
        self.assertIn('test:001',self.store.deleted())

    def test_changed_file_is_preserved(self):
        self.target.write_bytes(b'user edit')
        with self.assertRaises(ValueError):self.store.delete('test:001',self.sha)
        self.assertEqual(b'user edit',self.target.read_bytes());self.assertFalse(self.store.deleted())

    def test_unknown_id_and_hash_mismatch_cannot_delete(self):
        for asset_id,sha in [('unknown',self.sha),('test:001','bad')]:
            with self.assertRaises(ValueError):self.store.delete(asset_id,sha)
        self.assertTrue(self.target.exists())

    def test_v1_pack_is_never_deletable(self):
        original=self.root/'src/icons/000000/a.png';original.parent.mkdir(parents=True);original.write_bytes(b'V1')
        self.records.append(dict(id='v1',path='000000/a.png',sha256=hashlib.sha256(b'V1').hexdigest()));self.write_assets()
        with self.assertRaises(ValueError):self.store.delete('v1',self.records[-1]['sha256'])
        self.assertTrue(original.exists())
        self.assertIsNone(self.store.static_path('/src/icons/000000/a.png'))

    def test_traversal_and_source_files_cannot_be_deleted(self):
        for path in ['../secret.png','imported/pack/source/001.png','D:/secret.png']:
            self.records[0]['path']=path;self.write_assets()
            with self.assertRaises(ValueError):self.store.delete('test:001',self.sha)
        self.assertTrue(self.target.exists())

    def test_repeat_delete_is_idempotent(self):
        self.store.delete('test:001',self.sha)
        self.assertFalse(self.store.delete('test:001',self.sha)['fileRemoved'])

    def test_only_permitted_static_images_are_exposed(self):
        private=self.root/'src/NaviDnD/Storage/private.json';private.parent.mkdir(parents=True);private.write_text('{}')
        self.assertIsNone(self.store.static_path('/src/NaviDnD/Storage/private.json'))
        self.assertIsNone(self.store.static_path('/../src/NaviDnD/Storage/private.json'))
        self.assertEqual(self.target.resolve(),self.store.static_path('/artifacts/art-catalog-review/imported/pack/icons/001.png'))

    def test_reviews_are_saved_on_disk_and_survive_a_new_store(self):
        record=dict(status='approved_portrait',notes='Visible face',gender='unknown',species='',fingerprint=self.sha,updatedAt=200)
        self.store.save_review('test:001',record)
        restored=module.ReviewStore(self.root)
        self.assertEqual('Visible face',restored.saved_reviews()['test:001']['notes'])
        self.assertIsNone(restored.static_path('/artifacts/art-catalog-review/reviews.json'))

    def test_old_async_review_cannot_overwrite_newer_edit(self):
        newer=dict(status='approved_portrait',notes='New',fingerprint=self.sha,updatedAt=200)
        older=dict(status='rejected',notes='Old',fingerprint=self.sha,updatedAt=100)
        self.store.save_review('test:001',newer)
        self.assertTrue(self.store.save_review('test:001',older)['olderEditIgnored'])
        self.assertEqual('New',self.store.saved_reviews()['test:001']['notes'])

    def test_review_of_changed_fingerprint_is_rejected(self):
        with self.assertRaises(ValueError):self.store.save_review('test:001',dict(status='rejected',notes='No',fingerprint='wrong'))
        self.assertFalse(self.store.saved_reviews())

    def test_http_requires_token_and_rejects_foreign_origin(self):
        token='test-only-token'
        server=ThreadingHTTPServer(('127.0.0.1',0),module.make_handler(self.store,token))
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        url=f'http://127.0.0.1:{server.server_port}'
        try:
            body=json.dumps(dict(id='test:001',sha256=self.sha)).encode()
            for headers in [dict(),{'X-Art-Token':token,'Origin':'https://example.invalid'}]:
                req=urllib.request.Request(url+'/api/delete',data=body,headers={'Content-Type':'application/json',**headers})
                with self.assertRaises(urllib.error.HTTPError) as caught:urllib.request.urlopen(req)
                self.assertEqual(403,caught.exception.code)
                self.assertTrue(self.target.exists())
            req=urllib.request.Request(url+'/api/delete',data=body,headers={'Content-Type':'application/json','X-Art-Token':token,'Origin':url})
            with urllib.request.urlopen(req) as response:self.assertEqual(200,response.status)
            self.assertFalse(self.target.exists())
        finally:
            server.shutdown();server.server_close();thread.join(timeout=2)


if __name__=='__main__':unittest.main()
