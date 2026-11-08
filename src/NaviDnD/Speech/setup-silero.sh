#!/usr/bin/env bash
# macOS/Linux counterpart of setup-silero.ps1: a venv with CPU PyTorch, numpy and the Russian Silero model.
# Usage: setup-silero.sh [--python python3.12] [--destination <folder>]
set -euo pipefail

python=python3
destination=""
while [ $# -gt 0 ]; do
    case "$1" in
        --python) python="$2"; shift 2 ;;
        --destination) destination="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

fail() { echo "$1" >&2; exit 1; }

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[ -n "$destination" ] || destination="$script_dir/../Storage/Speech/silero"
mkdir -p "$destination"
destination="$(cd "$destination" && pwd)"

venv="$destination/venv"
local_python="$venv/bin/python3"
if [ ! -x "$local_python" ]; then
    "$python" -m venv "$venv" || fail 'Python 3.10-3.12 is required. Check the configured Python path.'
fi
python_tag="$("$local_python" -c "import sys; print('cp%d%d' % sys.version_info[:2])")"
case "$python_tag" in
    cp310|cp311|cp312) ;;
    # Removed so that the next attempt can build the venv from another Python.
    *) rm -rf "$venv"; fail "Python 3.10-3.12 is required (found $python_tag)." ;;
esac

# PyTorch stopped publishing Intel macOS wheels after 2.2.
if [ "$(uname -s)" = Darwin ] && [ "$(uname -m)" = x86_64 ]; then torch_version=2.2.2; else torch_version=2.6.0; fi
installed_torch="$("$local_python" -c "import importlib.metadata as m; print(m.version('torch'))" 2>/dev/null || true)"
if [ "${installed_torch%%+*}" != "$torch_version" ]; then
    # macOS wheels on PyPI are CPU builds; on Linux the CPU index avoids the multi-gigabyte CUDA build.
    if [ "$(uname -s)" = Darwin ]; then
        "$local_python" -m pip install --disable-pip-version-check --timeout 30 --retries 2 "torch==$torch_version"
    else
        "$local_python" -m pip install --disable-pip-version-check --timeout 30 --retries 2 \
            --index-url https://download.pytorch.org/whl/cpu "torch==$torch_version"
    fi || fail 'Failed to install CPU PyTorch.'
fi
"$local_python" -m pip install --disable-pip-version-check 'numpy==1.26.4' || fail 'Failed to install numpy.'

model="$destination/v5_5_ru.pt"
if [ ! -f "$model" ]; then
    curl --fail --location --silent --show-error --connect-timeout 20 --max-time 600 --retry 2 \
        --output "$model.part" 'https://models.silero.ai/models/tts/ru/v5_5_ru.pt' \
        || { rm -f "$model.part"; fail 'Silero model download failed.'; }
    mv -f "$model.part" "$model"
fi

"$local_python" -c 'import torch, numpy' || fail 'Python dependency validation failed.'
printf 'torch%s / silero-v5_5_ru\n' "$torch_version" > "$destination/.ready"
