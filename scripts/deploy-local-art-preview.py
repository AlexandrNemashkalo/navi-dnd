from pathlib import Path
from datetime import datetime
import argparse,hashlib,json,os,shutil

parser=argparse.ArgumentParser(description='Deploy local preview resources without modifying personal saves.')
parser.add_argument('--art-only',action='store_true',help='Copy only ArtLibrary resources; preserve all assemblies, prompts and spell resources.')
args=parser.parse_args()

repo=Path(__file__).resolve().parents[1]
target=(Path(os.environ['LOCALAPPDATA'])/'NaviDnD-Run').resolve()
game=repo/'src/NaviDnD/bin/Debug/net9.0'
server=repo/'src/NaviDnD.McpServer/bin/Debug/net9.0'
backup=repo/'artifacts/bg3-runtime-backups'/datetime.now().strftime('%Y%m%d-%H%M%S')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
assert (target/'NaviDnD.exe').is_file()
assert (game/'ArtLibrary/RuleSets/Dnd5e/runtime-pictures.json').read_bytes()==(repo/'src/ArtLibrary/RuleSets/Dnd5e/runtime-pictures.json').read_bytes()
if not args.art_only:
    assert (game/'SpellArt/RuleSets/Dnd5e/spell-icons.json').read_bytes()==(repo/'src/ArtLibrary/RuleSets/Dnd5e/spell-icons.json').read_bytes()
# Match all package versions before overlaying compiled project assemblies onto the existing self-contained host.
assemblies=[] if args.art_only else [('NaviDnD',game),('NaviDnD.McpServer',server),('NaviDnD.Launcher',game)]
for name,source in assemblies:
    def packages(p):
        d=json.loads(p.read_text(encoding='utf-8-sig'))
        return {k for k,v in d['libraries'].items() if v['type']=='package'}
    assert packages(target/(name+'.deps.json'))==packages(source/(name+'.deps.json')), name+' dependencies differ'
storage={p.relative_to(target).as_posix():sha(p) for p in (target/'Storage').rglob('*') if p.is_file()}
copies=[]
if not args.art_only:
    copies.extend((game/(name+'.dll'),target/(name+'.dll')) for name in ['NaviDnD','NaviDnD.Data','NaviDnD.MapGen','NaviDnD.Launcher'])
    copies.append((server/'NaviDnD.McpServer.dll',target/'NaviDnD.McpServer.dll'))
    copies.append((repo/'src/NaviDnD/Prompts/Dnd5e/heroOptions.json',target/'Prompts/Dnd5e/heroOptions.json'))
folders=['ArtLibrary'] if args.art_only else ['SpellArt','ArtLibrary']
copies.extend((p,target/p.relative_to(game)) for folder in folders for p in (game/folder).rglob('*') if p.is_file())
# Publish images first, manifests next, and code last. Unchanged resources need no overlay.
copies.sort(key=lambda pair: 2 if pair[1].suffix == '.dll' else 1 if pair[1].suffix == '.json' else 0)
copies=[(source,dest) for source,dest in copies if not dest.exists() or sha(source)!=sha(dest)]
audit=[]
for source,dest in copies:
    assert dest.resolve().is_relative_to(target) and not dest.resolve().is_relative_to(target/'Storage')
    relative=dest.relative_to(target)
    if dest.exists():
        saved=backup/relative;saved.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dest,saved)
    dest.parent.mkdir(parents=True,exist_ok=True)
    temporary=dest.with_name(dest.name+'.art-update.tmp')
    shutil.copy2(source,temporary)
    assert sha(temporary)==sha(source)
    os.replace(temporary,dest)
    assert sha(dest)==sha(source)
    audit.append(dict(file=relative.as_posix(),sha256=sha(dest)))
after={p.relative_to(target).as_posix():sha(p) for p in (target/'Storage').rglob('*') if p.is_file()}
assert storage==after,'Storage changed during deployment'
backup.mkdir(parents=True,exist_ok=True)
(backup/'deployment.json').write_text(json.dumps(dict(target=str(target),artOnly=args.art_only,files=audit,storageFilesUnchanged=len(storage)),indent=2),encoding='utf-8')
print('Updated',len(copies),'program/art files; all',len(storage),'save/settings files unchanged.')
print('Backup:',backup)
