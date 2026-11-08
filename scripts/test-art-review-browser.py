"""Exercise the real delete button in isolated headless Chrome fixtures.

Requires websocket-client (a local --target directory under artifacts/tooling/python
is supported). Never clicks a delete button against the user's real catalog.
"""
import hashlib
import argparse
import importlib.util
import json
import subprocess
import sys
import tempfile
import os
import shutil
import threading
import time
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--chrome',help='Chrome/Chromium executable (Windows or macOS)')
args=parser.parse_args()
browser_candidates=[args.chrome,os.environ.get('CHROME_PATH'),shutil.which('google-chrome'),shutil.which('chromium'),
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge']
chrome=next((Path(p) for p in browser_candidates if p and Path(p).is_file()),None)
if chrome is None:parser.error('Specify the Chrome/Chromium executable with --chrome')
sys.path.insert(0,str(ROOT/'artifacts/tooling/python'))
import websocket

spec=importlib.util.spec_from_file_location('art_review_server',Path(__file__).with_name('serve-art-review.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
review=ROOT/'artifacts/art-catalog-review'
live_assets=json.loads((review/'assets.json').read_text(encoding='utf-8'))
source=next(review/a['path'] for a in live_assets if a['path'].startswith('imported/') and (review/a['path']).exists())
png=source.read_bytes()
fixture_asset=dict(id='fixture:001',pack='fixture',path='imported/fixture/icons/001.png',
    preview='imported/fixture/icons/001.png',sha256=hashlib.sha256(png).hexdigest(),
    width=32,height=32,usages=['portrait'],label='Browser test fixture',labelRu='Проверочная картинка',gender='unknown')
fixture_asset['retentionReview']=dict(recommendation='review_removal',reason='Fixture suggestion only',userApproved=False)
fixture_asset['composition']=dict(mode='overlay_32',canvas=[32,32])
fixture_need=dict(id='fixture:need',name='Fixture equipment need',nameRu='Проверочное снаряжение',category='equipment',source='fixture',
    candidates=[dict(assetId=fixture_asset['id'],sha256=fixture_asset['sha256'],reason='Fixture visual candidate')],candidateIssues=[])
fixture_missing=dict(id='fixture:unmapped',name='Unmapped need',category='equipment',source='fixture',candidates=[],candidateIssues=[])
fixture_spell=dict(id='fixture:spell',name='Заклинание без картинки [Missing spell]',nameEn='Missing spell',nameRu='Заклинание без картинки',category='spell',source='fixture',candidates=[],candidateIssues=[])
data=dict(assets=[fixture_asset],requirements=[fixture_need,fixture_missing,fixture_spell],sheets=[],summary=dict(assets=1,describedPortraits=0))
data['changes']=[dict(id='1',at='2026-11-06T12:00:00+00:00',events=[dict(category=fixture_need['category'],name=fixture_need['name'],kind='added',added=[fixture_asset['id']],removed=[])])]
html=(review/'index.html').read_text(encoding='utf-8')
start=html.index('const data=');end=html.index(';\nconst server=',start)
html=html[:start]+'const data='+json.dumps(data)+html[end:]

with tempfile.TemporaryDirectory(prefix='navidnd-art-browser-',ignore_cleanup_errors=True) as temp:
    workspace=Path(temp);out=workspace/'artifacts/art-catalog-review';out.mkdir(parents=True)
    target=out/fixture_asset['path'];target.parent.mkdir(parents=True);target.write_bytes(png)
    (out/'assets.json').write_text(json.dumps([fixture_asset]),encoding='utf-8')
    (out/'index.html').write_text(html,encoding='utf-8')
    store=module.ReviewStore(workspace)
    server=ThreadingHTTPServer(('127.0.0.1',0),module.make_handler(store,'fixture-token'))
    worker=threading.Thread(target=server.serve_forever,daemon=True);worker.start()
    url=f'http://127.0.0.1:{server.server_port}/artifacts/art-catalog-review/index.html'
    profile=workspace/'browser'
    process=subprocess.Popen([str(chrome),'--headless=new','--disable-gpu','--no-first-run',
        '--remote-debugging-port=0','--user-data-dir='+str(profile),url],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    ws=None;command_id=0
    try:
        deadline=time.monotonic()+20
        while True:
            try:
                lines=(profile/'DevToolsActivePort').read_text().splitlines()
                if lines:port=lines[0];break
            except OSError:
                pass  # Chrome briefly holds an exclusive handle while writing.
            if time.monotonic()>deadline:raise TimeoutError('Chrome debugging port not ready')
            time.sleep(.05)
        while True:
            tabs=json.load(urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list',timeout=2))
            tab=next((t for t in tabs if t.get('url')==url),None)
            if tab:break
            if time.monotonic()>deadline:raise TimeoutError('Fixture tab not ready')
            time.sleep(.05)
        ws=websocket.create_connection(tab['webSocketDebuggerUrl'],timeout=5,suppress_origin=True)
        def command(method,params=None):
            global command_id
            command_id+=1;current=command_id
            ws.send(json.dumps(dict(id=current,method=method,params=params or {})))
            while True:
                reply=json.loads(ws.recv())
                if reply.get('id')==current:
                    if 'error' in reply:raise RuntimeError(reply['error'])
                    return reply.get('result',{})
        def evaluate(expression):
            result=command('Runtime.evaluate',dict(expression=expression,returnByValue=True))
            if result.get('exceptionDetails'):raise RuntimeError(result['exceptionDetails'])
            return result['result'].get('value')
        def until(expression):
            end=time.monotonic()+10
            while not evaluate(expression):
                if time.monotonic()>end:
                    raise TimeoutError(expression + ' ' + str(evaluate("JSON.stringify({mode:$('mode').value,category:$('category').value,pack:$('pack').value,status:$('status').value,records:records().length,grid:$('grid').innerHTML.slice(0,1200),reviews})")))
                time.sleep(.05)
        until("document.querySelectorAll('.delete-file').length===1")
        evaluate("document.getElementById('mode').value='changes'; document.getElementById('mode').dispatchEvent(new Event('change')); true")
        assert evaluate("document.querySelectorAll('.card').length===1 && !document.getElementById('change-batch').hidden && document.querySelector('.candidate-art')!==null")
        assert evaluate("document.querySelector('#mapping-stat b').textContent==='1' && document.getElementById('mapping-stat').textContent.includes('33%')")
        assert evaluate("document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='1 / 2'")
        assert evaluate("document.querySelector('#category-coverage [data-category=spell] strong').textContent==='0 / 1'")
        evaluate("document.querySelector('#category-coverage [data-category=spell]').click(); true")
        assert evaluate("document.querySelectorAll('.card').length===1 && document.querySelector('.card h2').textContent==='Missing spell — Заклинание без картинки'")
        assert evaluate("$('category').value==='spell' && document.querySelector('#category-coverage [data-category=spell]').getAttribute('aria-pressed')==='true'")
        evaluate("document.querySelector('#category-coverage [data-category=equipment]').click(); true")
        assert evaluate("document.querySelectorAll('.card').length===2 && $('category').value==='equipment'")
        evaluate("$('query').value='проверочное снаряжение'; $('query').dispatchEvent(new Event('input')); true")
        assert evaluate("document.querySelectorAll('.card').length===1 && document.querySelector('.card h2').textContent==='Fixture equipment need — Проверочное снаряжение'")
        assert evaluate("document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='1 / 2'")
        evaluate("$('query').value=''; $('coverage').value='with'; $('coverage').dispatchEvent(new Event('change')); true")
        until("document.querySelectorAll('.candidate-art').length===1")
        assert evaluate("document.querySelectorAll('.card').length===1 && document.getElementById('coverage').hidden===false")
        evaluate("document.querySelector('.candidate-art').click(); true")
        until("document.getElementById('dialog').open===true")
        assert evaluate("document.getElementById('dialog-caption').textContent.includes('Fixture visual candidate')")
        assert evaluate("$('dialog-caption').textContent.includes('Fixture equipment need — Проверочное снаряжение') && $('dialog-caption').textContent.includes('Browser test fixture — Проверочная картинка')")
        assert target.exists(), 'Viewing a requirement candidate must preserve the PNG'
        evaluate("document.getElementById('close').click(); document.getElementById('coverage').value='without'; document.getElementById('coverage').dispatchEvent(new Event('change')); true")
        assert evaluate("document.querySelectorAll('.candidate-art').length===0 && document.querySelector('.card').dataset.id==='fixture:unmapped'")
        evaluate("document.getElementById('mode').value='portraits'; document.getElementById('mode').dispatchEvent(new Event('change')); true")
        evaluate("document.getElementById('mode').value='removal'; document.getElementById('mode').dispatchEvent(new Event('change')); true")
        until("!!document.querySelector('.keep-file')")
        assert evaluate("document.getElementById('grid').textContent.includes('Fixture suggestion only')")
        assert target.exists(), 'Recommendation must never delete a file'
        evaluate("document.querySelector('.keep-file').click(); true")
        until("document.querySelectorAll('.card').length===0")
        assert target.exists(), 'Keep action must preserve the PNG'
        evaluate("document.getElementById('mode').value='assets'; document.getElementById('mode').dispatchEvent(new Event('change')); true")
        until("document.querySelectorAll('.delete-file').length===1")
        evaluate("$('query').value='проверочная картинка'; $('query').dispatchEvent(new Event('input')); true")
        assert evaluate("document.querySelectorAll('.card').length===1 && document.querySelector('.card h2').textContent==='Browser test fixture — Проверочная картинка'")
        evaluate("$('query').value=''; $('query').dispatchEvent(new Event('input')); true")
        evaluate("document.querySelector('[data-field=status]').value='rejected'; document.querySelector('[data-field=status]').dispatchEvent(new Event('change')); true")
        until("document.getElementById('notice').textContent.includes('файл')")
        assert evaluate("document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='0 / 2' && document.querySelector('#mapping-stat b').textContent==='0'")
        evaluate("document.querySelector('[data-field=status]').value='approved_other'; document.querySelector('[data-field=status]').dispatchEvent(new Event('change')); true")
        until("document.getElementById('notice').textContent.includes('файл')")
        assert evaluate("document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='1 / 2'")
        assert evaluate("document.querySelector('.delete-file').disabled===false")
        assert evaluate("document.querySelector('.delete-file').textContent==='Удалить PNG'")
        assert evaluate("document.querySelector('.delete-file').getBoundingClientRect().top < document.querySelector('.art').getBoundingClientRect().top")
        evaluate("document.querySelector('textarea').value='Fixture review saved to disk'; document.querySelector('textarea').dispatchEvent(new Event('change')); true")
        until("document.getElementById('notice').textContent.includes('файл')")
        assert store.saved_reviews()[fixture_asset['id']]['notes']=='Fixture review saved to disk'
        evaluate("localStorage.clear(); true")
        command('Page.reload')
        until("document.querySelector('textarea') && document.querySelector('textarea').value==='Fixture review saved to disk'")
        assert target.exists()
        evaluate("document.querySelector('.delete-file').click(); true")
        until("document.querySelectorAll('.card').length===0")
        assert not target.exists(),'Button did not delete fixture PNG on disk'
        assert fixture_asset['id'] in store.deleted()
        assert evaluate("$('asset-count').textContent==='0' && document.querySelector('#mapping-stat b').textContent==='0' && document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='0 / 2'")
        command('Page.reload')
        until("document.readyState==='complete' && document.getElementById('grid') && document.querySelectorAll('.card').length===0")
        assert not target.exists()
        assert evaluate("document.querySelector('#category-coverage [data-category=equipment] strong').textContent==='0 / 2'")
        print('PASS: bilingual names/search/dialogs; category coverage and navigation; live rejection/deletion counts; reviews and deletions survive reload.')
    finally:
        if ws:
            try:ws.send(json.dumps(dict(id=999999,method='Browser.close')))
            except Exception:pass
            ws.close()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.terminate();process.wait(timeout=5)
        server.shutdown();server.server_close();worker.join(timeout=2)
