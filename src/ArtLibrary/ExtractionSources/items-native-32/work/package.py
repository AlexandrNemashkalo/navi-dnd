from pathlib import Path
import json,hashlib,zipfile
from PIL import Image
root=Path('artifacts/items-native-32'); packs=json.loads((root/'work/packs.json').read_text()); sources=json.loads((root/'work/sources-found.json').read_text())
urls={i['path']:i['url'] for s in sources for i in s['images']}
labels=['Трофеи','Материалы','Минералы','Ремесло','Самоцветы','Цветы и корни','Варвар','Алхимия','Вор','Добыча','Пиромант','Артефакты','Книги','Руны','Чернокнижник','Лучник','Ягоды и орехи','Фрукты и овощи','Копейщик','Рыбалка','Аэромант','Арбалетчик','Фермерство','Свитки','Криомант','Грибы','Мясо и шкуры','Еда','Зелья','Усиления','Украшения','Сокровища','Клинки','Символы','Мечи с эффектами','Мечи','Ослабления','Шлемы','Дробящее оружие с эффектами','Дробящее оружие']
records=[]; hashes={}; duplicates=[]
for p,label in zip(packs,labels):
 p['label']=label;p['sourceUrl']=urls.get(p['source'],p['sourceUrl']);p['count']=48
 for n in range(1,49):
  f=root/'png'/p['id']/f"{p['id']}-{n:02}.png";im=Image.open(f);assert im.size==(32,32) and im.mode=='RGBA',str(f)
  px=list(im.getdata()); alphas={c[3] for c in px};assert alphas<={0,255};assert all(c==(0,0,0,0) for c in px if c[3]==0)
  assert (0 in alphas) if p['kind']=='item' else (alphas=={255}),str(f)
  digest=hashlib.sha256(im.tobytes()).hexdigest()
  if digest in hashes:duplicates.append([hashes[digest],str(f)])
  hashes[digest]=str(f)
  records.append({'file':f.relative_to(root).as_posix(),'pack':p['id'],'number':n,'transparentPixels':sum(c[3]==0 for c in px),'sha256':digest})
assert len(records)==1920
(root/'manifest.json').write_text(json.dumps({'packs':packs,'icons':records},ensure_ascii=False,indent=2),encoding='utf-8')
(root/'verification.json').write_text(json.dumps({'count':len(records),'packs':40,'size':'32x32','rgba':True,'binaryAlpha':True,'transparentRGBZero':True,'itemCount':sum(p['kind']=='item' for p in packs)*48,'skillCount':sum(p['kind']=='skill' for p in packs)*48,'duplicatePixels':duplicates},indent=2),encoding='utf-8')
html='''<!doctype html><html lang="ru"><meta charset="utf-8"><title>Иконки 32×32</title><style>body{margin:24px;font:16px system-ui;background:#20242b;color:#eee}header{position:sticky;top:0;background:#20242bf5;padding:12px;z-index:2}select,button{padding:8px;margin:4px}section{display:grid;grid-template-columns:repeat(auto-fill,minmax(145px,1fr));gap:10px}a{color:inherit;text-decoration:none;text-align:center;background:#333842;padding:8px}img{width:128px;height:128px;image-rendering:pixelated;background:var(--bg);display:block;margin:auto}small{display:block;margin-top:5px}body{--bg:repeating-conic-gradient(#ddd 0% 25%,#aaa 0% 50%) 0/16px 16px}</style><header><b>1920 иконок · 40 наборов · PNG 32×32</b><p>Предметы: прозрачный фон, сохранены обводка и эффекты. Навыки: цветной рисунок сохранён целиком, внешняя рамка удалена.</p><select id="pack"><option value="">Все наборы</option></select><button onclick="bg('white')">Белый</button><button onclick="bg('#101112')">Тёмный</button><button onclick="bg('repeating-conic-gradient(#ddd 0% 25%,#aaa 0% 50%) 0/16px 16px')">Шахматный</button><select onchange="document.querySelectorAll('img').forEach(i=>{i.style.width=i.style.height=this.value+'px'})"><option value="128">4×</option><option value="32">1×</option><option value="64">2×</option><option value="256">8×</option></select><p id="count"></p></header><section id="grid"></section><script>const packs=PACKS;function bg(v){document.body.style.setProperty('--bg',v)}const sel=document.getElementById('pack');packs.forEach(p=>sel.add(new Option(p.id+' — '+p.label,p.id)));function render(){let html='',count=0;packs.filter(p=>!sel.value||p.id===sel.value).forEach(p=>{for(let n=1;n<=48;n++){const name=p.id+'-'+String(n).padStart(2,'0')+'.png',url='png/'+p.id+'/'+name;html+='<a href="'+url+'" download><img loading="lazy" src="'+url+'"><small>'+p.label+' · '+n+'</small></a>';count++}});document.getElementById('grid').innerHTML=html;document.getElementById('count').textContent=count+' файлов · нажмите для скачивания'}sel.onchange=render;render()</script></html>'''.replace('PACKS',json.dumps(packs,ensure_ascii=False))
(root/'index.html').write_text(html,encoding='utf-8')
(root/'README.txt').write_text('1920 PNG 32x32. Порядок наборов соответствует приложениям, внутри — слева направо, сверху вниз.\nПредметы имеют прозрачный фон. Цветной рисунок навыков сохранён.\nИзвлечение по исходной пиксельной сетке, без изменения размера силуэта и без удаления чёрной обводки.\nИсточники — публичные увеличенные превью тех же изображений; JPEG-компрессия не позволяет восстановить точную исходную палитру PNG. Происхождение указано в manifest.json.\nГалерея index.html работает локально в Windows и macOS.\n',encoding='utf-8')
with zipfile.ZipFile(root.with_suffix('.zip'),'w',zipfile.ZIP_DEFLATED) as z:
 for f in root.rglob('*'):
  if f.is_file() and 'work' not in f.relative_to(root).parts:z.write(f,f.relative_to(root).as_posix())
print(json.loads((root/'verification.json').read_text()));print('ZIP bytes',root.with_suffix('.zip').stat().st_size)
