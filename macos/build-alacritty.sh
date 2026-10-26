#!/bin/bash
# Build on macOS with Xcode Command Line Tools, rustup and Python 3 installed.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source_commit=94e7c8874e526b1e67b349d9ba30ddf81669119e
toolchain=1.85.0
target=aarch64-apple-darwin
patch="$root/macos/alacritty-osc22.patch"
output="${1:-$root/artifacts/macos-cache/alacritty-custom}"
[[ "$(uname -s)" == Darwin ]] || { echo 'This build requires macOS and the Apple SDK.' >&2; exit 1; }
xcode-select -p >/dev/null
command -v rustup >/dev/null
command -v python3 >/dev/null
mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/navidnd-alacritty.XXXXXX")"
trap 'rm -rf -- "$work"' EXIT
git clone --quiet --no-checkout https://github.com/alacritty/alacritty.git "$work/source"
git -C "$work/source" checkout --quiet --detach "$source_commit"
git -C "$work/source" apply --check "$patch"
git -C "$work/source" apply "$patch"
rustup toolchain install "$toolchain" --profile minimal
rustup target add --toolchain "$toolchain" "$target"
cd "$work/source"
cargo "+$toolchain" test --locked -p alacritty_terminal
cargo "+$toolchain" build --locked --release --target "$target" -p alacritty --no-default-features
cp "target/$target/release/alacritty" "$output/alacritty"
cp LICENSE-APACHE "$output/alacritty-LICENSE"
python3 - "$output" "$patch" "$source_commit" "$toolchain" "$target" <<'PY'
import hashlib, json, pathlib, sys
output, patch, commit, rust, target = sys.argv[1:]
output = pathlib.Path(output)
digest = lambda p: hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
metadata = dict(sourceCommit=commit, rustToolchain=rust, target=target,
                patchSha256=digest(patch), binarySha256=digest(output / 'alacritty'))
(output / 'build.json').write_text(json.dumps(metadata, indent=2) + '\n')
PY
echo "Custom Alacritty ready: $output"
