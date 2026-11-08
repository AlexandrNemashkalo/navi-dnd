"""One-time/repeatable cleanup of superseded art staging directories.

Only named art experiment paths are removed. Unique extraction inputs and all
metadata are verified in the permanent library before a final staging tree is
removed. Never touches V1, DCSS, 000000, saves, browser storage or live logs.
"""
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts'
LIBRARY = ROOT / 'src/ArtLibrary'
REVIEW = ARTIFACTS / 'art-catalog-review'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def copy_verified(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists():
        shutil.copy2(source, target)
    if digest(source) != digest(target):
        raise ValueError(f'Changed source/destination preserved: {source}')


def main():
    removed, count, total = [], 0, 0

    def remove_tree(path, reason):
        nonlocal count, total
        path = path.resolve()
        if not path.is_relative_to(ARTIFACTS.resolve()) or path == ARTIFACTS.resolve() or path.is_symlink():
            raise ValueError('Cleanup outside explicit artifacts tree')
        files = [p for p in path.rglob('*') if p.is_file()]
        if any(p.is_symlink() for p in path.rglob('*')):
            raise ValueError('Symlink in cleanup tree')
        size = sum(p.stat().st_size for p in files)
        shutil.rmtree(path)
        removed.append(dict(path=path.relative_to(ROOT).as_posix(), reason=reason, files=len(files), bytes=size))
        count += len(files)
        total += size

    for collection in ('portraits-native-32', 'items-native-32', 'items-native-32-batch2'):
        folder = ARTIFACTS / collection
        if not folder.exists():
            continue
        for file in folder.rglob('*'):
            if not file.is_file():
                continue
            relative = file.relative_to(folder)
            if relative.parts[0] == 'previews' or file.name == 'index.html':
                continue  # Rebuildable alternate galleries, not source inputs.
            if relative.parts[0] == 'png' and file.suffix.lower() == '.png':
                pack = relative.parent.name
                if collection == 'portraits-native-32' and pack == 'halflings':
                    continue  # Explicitly excluded source family.
                permanent = LIBRARY / 'Sources' / f'prepared-{collection}-{pack}' / file.name
                if not permanent.exists() or digest(file) != digest(permanent):
                    raise ValueError(f'PNG not safely imported: {file}')
            else:
                copy_verified(file, LIBRARY / 'ExtractionSources' / collection / relative)
        remove_tree(folder, 'final_png_verified_in_library_inputs_retained_galleries_superseded')

    # The old original-source copies are redundant only after the saved registry
    # references the verified permanent paths instead of these folders.
    packs = json.loads((LIBRARY / 'packs.json').read_text(encoding='utf-8'))
    for pack in packs:
        folder = REVIEW / 'imported' / pack['id'] / 'source'
        if not folder.exists():
            continue
        for file in folder.rglob('*'):
            if file.is_file():
                permanent = LIBRARY / 'Sources' / pack['id'] / file.relative_to(folder)
                if not permanent.exists() or digest(file) != digest(permanent):
                    raise ValueError(f'Original has no identical permanent copy: {file}')
        remove_tree(folder, 'verified_duplicate_of_permanent_source')
    (REVIEW / 'imported-packs.json').write_text(json.dumps(packs, ensure_ascii=False, indent=2), encoding='utf-8')

    for name in ('elf-portraits-32-v2', 'portraits-32-clean', 'portraits-transparent-32'):
        folder = ARTIFACTS / name
        if folder.exists():
            remove_tree(folder, 'superseded_intermediate_extraction_or_unfinished_experiment')
    folder = REVIEW / 'craftpix-portraits'
    if folder.exists():
        active = json.loads((REVIEW / 'assets.json').read_text(encoding='utf-8'))
        if any(a['path'].startswith('craftpix-portraits/') for a in active):
            raise ValueError('Legacy portraits still active')
        remove_tree(folder, 'legacy_gallery_superseded_by_imported_portraits')
    excluded_folder = REVIEW / 'imported/craftpix-945210'
    if excluded_folder.exists():
        remove_tree(excluded_folder, 'unused_preview_of_excluded_64px_pack')
    sheet_folder = REVIEW / 'contact-sheets'
    index = sheet_folder / 'index.json'
    kept = {s['sheet'] for s in json.loads(index.read_text(encoding='utf-8'))} if index.exists() else set()
    stale = [p for p in sheet_folder.glob('*.png') if p.name not in kept]
    stale += [REVIEW / name for name in ('craftpix-contact-sheet.png', 'review-screenshot.png') if (REVIEW / name).exists()]
    for file in stale:
        size = file.stat().st_size
        file.unlink()
        removed.append(dict(path=file.relative_to(ROOT).as_posix(), reason='superseded_generated_preview', files=1, bytes=size))
        count += 1
        total += size
    prior_path = LIBRARY / 'consolidation-report.json'
    prior = json.loads(prior_path.read_text(encoding='utf-8')) if prior_path.exists() else {}
    report = dict(filesRemoved=count+prior.get('filesRemoved',0), bytesRemoved=total+prior.get('bytesRemoved',0), removed=prior.get('removed',[])+removed)
    (LIBRARY / 'consolidation-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Removed {count} redundant/intermediate files ({total / 1048576:.1f} MiB). Permanent sources and review data retained.')


if __name__ == '__main__':
    main()
