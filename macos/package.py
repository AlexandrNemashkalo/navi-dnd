"""Сборка архива для macOS на Windows (вызывает build-macos.ps1).

silero <python.tar.gz> <папка колёс> <v5_5_ru.pt> <THIRD_PARTY_NOTICES.md> <Speech/silero>
    Комплект озвучки: переносимый Python 3.12 (python-build-standalone) + torch/numpy из колёс + русская модель.
zip <NaviDnD.app> <архив.zip>
    ZIP с Unix-правами: Finder при распаковке сохраняет «исполняемый» у программ (755), остальным — 644.
zipdir <папка> <архив.zip>
    Содержимое папки (без неё самой) — ZIP, одинаковый при каждой сборке: фиксированные даты и порядок, Unix-права
    (программы Python — 755). Так комплект озвучки лежит в приложении и попадает в патч, только если изменился.
"""
import os
import shutil
import sys
import tarfile
import zipfile

# Из Python — только то, что нужно silero_worker.py: без IDLE, Tk, тестов, pip, заголовков и справки.
SKIP_PYTHON = ('python/share/', 'python/include/', 'python/lib/pkgconfig/', 'python/lib/tcl', 'python/lib/tk',
               'python/lib/itcl', 'python/lib/thread', 'python/lib/libtcl', 'python/lib/libtk',
               'python/lib/python3.12/test/', 'python/lib/python3.12/idlelib/', 'python/lib/python3.12/tkinter/',
               'python/lib/python3.12/turtledemo/', 'python/lib/python3.12/ensurepip/',
               'python/lib/python3.12/site-packages/')
# Как в copy_bundle_dependencies.py: без кешей, заголовков сборки и тестов пакетов.
SKIP_PACKAGE_PARTS = {'__pycache__', 'include', 'tests', 'test', 'benchmarks'}
EXECUTABLE = {'NaviDnD', 'NaviDnD.McpServer', 'alacritty', 'createdump', 'setup-silero.sh'}


def silero(tar_path, wheels, model, notices, dest):
    if os.path.exists(dest):
        raise SystemExit(f'Destination exists: {dest}')
    with tarfile.open(tar_path) as archive:
        for member in archive.getmembers():
            name = member.name
            if not member.isfile() or name.startswith(SKIP_PYTHON) or '/__pycache__/' in name:
                continue
            if name.startswith('python/bin/') and name != 'python/bin/python3.12':
                continue
            if '..' in name.split('/') or name.startswith('/'):
                raise SystemExit(f'Unsafe path in Python archive: {name}')
            write(archive.extractfile(member), os.path.join(dest, *name.split('/')))
    # bin/python3 в архиве — ссылка на python3.12; игра запускает python3, копия работает так же.
    shutil.copy2(os.path.join(dest, 'python', 'bin', 'python3.12'), os.path.join(dest, 'python', 'bin', 'python3'))

    site = os.path.join(dest, 'python', 'lib', 'python3.12', 'site-packages')
    for wheel in sorted(os.listdir(wheels)):
        with zipfile.ZipFile(os.path.join(wheels, wheel)) as archive:
            for name in archive.namelist():
                parts = name.split('/')
                if name.endswith('/') or parts[0].endswith('.data') or SKIP_PACKAGE_PARTS & set(parts[:-1]):
                    continue
                if parts[0].endswith('.dist-info') and parts[-1] in ('RECORD', 'direct_url.json'):
                    continue
                if '..' in parts or name.startswith('/'):
                    raise SystemExit(f'Unsafe path in {wheel}: {name}')
                write(archive.open(name), os.path.join(site, *parts))

    shutil.copy2(model, os.path.join(dest, 'v5_5_ru.pt'))
    shutil.copy2(notices, os.path.join(dest, 'THIRD_PARTY_NOTICES.md'))
    with open(os.path.join(dest, '.ready'), 'w', newline='\n') as ready:
        ready.write('python3.12 / torch2.6.0 / silero-v5_5_ru\n')


def write(source, target):
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with source, open(target, 'wb') as output:
        shutil.copyfileobj(source, output)


def make_zip(source, output):
    top = os.path.basename(os.path.normpath(source))
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for root, dirs, files in os.walk(source):
            dirs.sort()
            for name in sorted(files):
                path = os.path.join(root, name)
                entry = top + '/' + os.path.relpath(path, source).replace('\\', '/')
                info = zipfile.ZipInfo.from_file(path, entry)
                info.create_system = 3  # Unix: права из external_attr
                mode = 0o755 if name in EXECUTABLE or '/python/bin/' in entry else 0o644
                info.external_attr = (0o100000 | mode) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(path, 'rb') as file:
                    data = file.read()
                if name.endswith('.sh'):
                    data = data.replace(b'\r\n', b'\n')
                archive.writestr(info, data)


def make_dir_zip(source, output):
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for root, dirs, files in os.walk(source):
            dirs.sort()
            for name in sorted(files):
                path = os.path.join(root, name)
                entry = os.path.relpath(path, source).replace('\\', '/')
                info = zipfile.ZipInfo(entry, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                mode = 0o755 if entry.startswith('python/bin/') else 0o644
                info.external_attr = (0o100000 | mode) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(path, 'rb') as file:
                    archive.writestr(info, file.read())


if __name__ == '__main__':
    command, *arguments = sys.argv[1:]
    {'silero': silero, 'zip': make_zip, 'zipdir': make_dir_zip}[command](*arguments)
