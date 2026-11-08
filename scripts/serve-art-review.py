"""Loopback-only art review with explicit, immediate deletion of known PNGs.

python scripts/serve-art-review.py --port 8765
Deletion is authorized by the user; no per-file confirmation dialog is shown.
V1's 000000 pack, archives, PSDs, saves and arbitrary paths are never API targets.
"""
import argparse
import hashlib
import json
import mimetypes
import os
import secrets
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit


class ReviewStore:
    def __init__(self, workspace):
        self.workspace = Path(workspace).resolve()
        self.review = self.workspace / 'artifacts/art-catalog-review'
        self.deletions_file = self.review / 'deletions.json'
        self.reviews_file = self.review / 'reviews.json'
        self.lock = threading.RLock()
        self._asset_stamp=None
        self._asset_cache={}

    def deleted(self):
        if not self.deletions_file.exists():
            return {}
        return json.loads(self.deletions_file.read_text(encoding='utf-8'))

    def assets(self):
        path=self.review/'assets.json'
        stat=path.stat();stamp=(stat.st_mtime_ns,stat.st_size)
        if stamp!=self._asset_stamp:
            self._asset_cache={a['id']:a for a in json.loads(path.read_text(encoding='utf-8'))}
            self._asset_stamp=stamp
        return self._asset_cache

    def saved_reviews(self):
        return json.loads(self.reviews_file.read_text(encoding='utf-8')) if self.reviews_file.exists() else {}

    def save_review(self, asset_id, record):
        with self.lock:
            target=self.assets().get(asset_id) or self.deleted().get(asset_id)
            if target is None:
                path=self.review/'requirements.json'
                if path.exists():
                    target=next((r for r in json.loads(path.read_text(encoding='utf-8')) if r['id']==asset_id),None)
            if target is None or not isinstance(record,dict):raise ValueError('Unknown review target')
            expected=target.get('sha256') or json.dumps([target['id'],target.get('name'),target.get('source')],ensure_ascii=False,separators=(',',':'))
            if record.get('fingerprint')!=expected:raise ValueError('Review fingerprint changed')
            status=record.get('status')
            if status not in ('unreviewed','approved_portrait','approved_other','rejected','needs_generation'):
                raise ValueError('Invalid review status')
            notes=record.get('notes','');species=record.get('species','');gender=record.get('gender','unknown')
            if not isinstance(notes,str) or len(notes)>12000 or not isinstance(species,str) or len(species)>160 or gender not in ('male','female','unknown'):
                raise ValueError('Invalid review fields')
            edited_at=record.get('updatedAt',round(time.time()*1000))
            if not isinstance(edited_at,int) or edited_at<0:raise ValueError('Invalid review timestamp')
            reviews=self.saved_reviews()
            if reviews.get(asset_id,{}).get('updatedAt',0)>edited_at:
                return dict(id=asset_id,review=reviews[asset_id],olderEditIgnored=True)
            saved=dict(status=status,notes=notes,species=species,gender=gender,fingerprint=expected,updatedAt=edited_at)
            reviews[asset_id]=saved
            temporary=self.reviews_file.with_suffix('.json.new')
            temporary.write_text(json.dumps(reviews,ensure_ascii=False,indent=2),encoding='utf-8');temporary.replace(self.reviews_file)
            return dict(id=asset_id,review=saved)

    def delete(self, asset_id, expected_hash):
        with self.lock:
            assets=self.assets()
            if asset_id not in assets:
                if asset_id in self.deleted():
                    return dict(deletedId=asset_id, alreadyDeleted=True)
                raise ValueError('Unknown asset ID')
            asset=assets[asset_id]
            relative=asset['path']
            if not isinstance(relative,str) or '\\' in relative or ':' in relative or '..' in relative.split('/'):
                raise ValueError('Unsafe asset path')
            if relative.startswith('dcss/'):
                target=self.workspace/'src/icons'/relative
                allowed=self.workspace/'src/icons/dcss'
            elif relative.startswith('imported/') and '/icons/' in relative:
                target=self.review/relative
                allowed=self.review/'imported'
            elif relative.startswith('craftpix-portraits/'):
                target=self.review/relative
                allowed=self.review/'craftpix-portraits'
            else:
                raise ValueError('Asset is outside deletable pixel roots')
            target=target.absolute()
            if target.suffix.lower()!='.png' or target.is_symlink() or not target.resolve().is_relative_to(allowed.resolve()):
                raise ValueError('Asset is not a permitted PNG')
            if not isinstance(expected_hash,str) or expected_hash!=asset.get('sha256'):
                raise ValueError('Catalog fingerprint changed; reload the page')
            existed=target.exists()
            if existed:
                actual=hashlib.sha256(target.read_bytes()).hexdigest()
                if actual!=expected_hash:
                    raise ValueError('File changed after catalog review; reload/re-audit before deletion')
            tombstones=self.deleted()
            tombstones[asset_id]=dict(id=asset_id,path=relative,sha256=expected_hash,
                                     kind='file_deleted',sourceRetained=True)
            # Persist the intent first, then unlink. If deletion fails, roll back
            # the intent. A crash cannot cause the importer to resurrect a file.
            old=self.deletions_file.read_bytes() if self.deletions_file.exists() else None
            temporary=self.deletions_file.with_suffix('.json.new')
            temporary.write_text(json.dumps(tombstones,ensure_ascii=False,indent=2),encoding='utf-8')
            temporary.replace(self.deletions_file)
            try:
                if existed:
                    target.unlink()
            except Exception:
                if old is None:
                    self.deletions_file.unlink(missing_ok=True)
                else:
                    temporary.write_bytes(old)
                    temporary.replace(self.deletions_file)
                raise
            return dict(deletedId=asset_id, fileRemoved=existed)

    def static_path(self, url_path):
        if url_path in ('/','/index.html'):
            return self.review/'index.html'
        decoded=unquote(url_path)
        if '\\' in decoded or ':' in decoded or '..' in decoded.split('/'):
            return None
        target=(self.workspace/decoded.lstrip('/')).resolve()
        if not target.is_file() or target.is_symlink():
            return None
        if target.is_relative_to((self.workspace/'src/icons/dcss').resolve()) and target.suffix.lower()=='.png':
            return target
        if target.is_relative_to(self.review.resolve()):
            if target.name=='index.html' or target.suffix.lower()=='.png':
                return target
        return None


def make_handler(store, token):
    class Handler(BaseHTTPRequestHandler):
        def valid_host(self):
            return self.headers.get('Host')==f'127.0.0.1:{self.server.server_port}'

        def respond(self, status, payload, content_type='application/json; charset=utf-8'):
            raw=payload if isinstance(payload,bytes) else json.dumps(payload,ensure_ascii=False).encode()
            self.send_response(status)
            self.send_header('Content-Type',content_type)
            self.send_header('Content-Length',str(len(raw)))
            self.send_header('Cache-Control','no-store')
            self.send_header('X-Content-Type-Options','nosniff')
            self.send_header('Content-Security-Policy',"default-src 'self'; img-src 'self' data:; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'")
            self.end_headers()
            self.wfile.write(raw)

        def do_GET(self):
            if not self.valid_host():
                self.respond(403,dict(error='Invalid host'))
                return
            if urlsplit(self.path).path=='/api/reviews':
                if self.headers.get('X-Art-Token')!=token:
                    self.respond(403,dict(error='Invalid local review token'));return
                self.respond(200,dict(reviews=store.saved_reviews()));return
            target=store.static_path(urlsplit(self.path).path)
            if target is None:
                self.respond(404,dict(error='Not found'))
                return
            raw=target.read_bytes()
            if target.name=='index.html':
                config=json.dumps(dict(deleteEnabled=True,token=token,deletedIds=list(store.deleted()),reviews=store.saved_reviews(),reviewApiBase=f'http://127.0.0.1:{self.server.server_port}'),ensure_ascii=True).replace('<','\\u003c')
                raw=raw.replace(b'<script>',('<script>window.ART_REVIEW_SERVER='+config+';</script><script>').encode(),1)
            self.respond(200,raw,mimetypes.guess_type(str(target))[0] or 'application/octet-stream')

        def do_POST(self):
            origin=f'http://127.0.0.1:{self.server.server_port}'
            if not self.valid_host() or self.headers.get('Origin') not in (None,origin) or self.headers.get('X-Art-Token')!=token:
                self.respond(403,dict(error='Invalid local review token or origin'))
                return
            route=urlsplit(self.path).path
            if route not in ('/api/delete','/api/review') or self.headers.get('Content-Type','').split(';')[0]!='application/json':
                self.respond(400,dict(error='Invalid request'))
                return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<=65536:
                    raise ValueError('Invalid body length')
                body=json.loads(self.rfile.read(length))
                if not isinstance(body,dict):raise ValueError('Expected JSON object')
                result=store.delete(body.get('id'),body.get('sha256')) if route=='/api/delete' else store.save_review(body.get('id'),body.get('review'))
                self.respond(200,result)
            except (ValueError,TypeError,KeyError) as exc:
                self.respond(409,dict(error=str(exc)))
            except OSError as exc:
                self.respond(500,dict(error=str(exc)))

        def log_message(self, fmt, *args):
            # No token, request bodies or personal paths in logs.
            print(fmt%args,flush=True)
    return Handler


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port',type=int,default=8765)
    args=parser.parse_args()
    workspace=Path(__file__).resolve().parents[1]
    store=ReviewStore(workspace)
    session_path=store.review/'server-session.json'
    session=json.loads(session_path.read_text(encoding='utf-8')) if session_path.exists() else {}
    token=session.get('token') or secrets.token_urlsafe(32)
    session_path.write_text(json.dumps(dict(token=token)),encoding='utf-8')
    server=ThreadingHTTPServer(('127.0.0.1',args.port),make_handler(store,token))
    url=f'http://127.0.0.1:{server.server_port}/artifacts/art-catalog-review/index.html'
    (store.review/'server-info.json').write_text(json.dumps(dict(url=url,pid=os.getpid())),encoding='utf-8')
    print(url,flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__=='__main__':
    main()
