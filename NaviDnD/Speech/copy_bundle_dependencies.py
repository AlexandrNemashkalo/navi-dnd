"""Copy public runtime dependencies without developer caches or build headers."""
import shutil
import sys
from pathlib import Path

source, destination = map(Path, sys.argv[1:3])
destination.mkdir(parents=True, exist_ok=True)
names = ["torch", "functorch", "torchgen", "numpy", "numpy.libs", "filelock",
         "fsspec", "jinja2", "markupsafe", "mpmath", "networkx", "sympy", "typing_extensions.py"]
ignore = shutil.ignore_patterns("__pycache__", "*.pyc", "include", "tests", "test", "benchmarks")

def long_path(path):
    return "\\\\?\\" + str(path.resolve())

for name in names:
    src, dst = source / name, destination / name
    if src.is_dir():
        shutil.copytree(long_path(src), long_path(dst), ignore=ignore, dirs_exist_ok=True)
    else:
        shutil.copy2(long_path(src), long_path(dst))

for name in ["torch", "numpy", "filelock", "fsspec", "jinja2", "markupsafe",
             "mpmath", "networkx", "sympy", "typing_extensions"]:
    for metadata in source.glob(name + "-*.dist-info"):
        shutil.copytree(long_path(metadata), long_path(destination / metadata.name),
                        ignore=shutil.ignore_patterns("direct_url.json"), dirs_exist_ok=True)
