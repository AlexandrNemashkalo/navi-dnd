#!/bin/bash
# Build natively on macOS, or in the pinned cargo-zigbuild Linux CI image.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source_commit=94e7c8874e526b1e67b349d9ba30ddf81669119e
toolchain=1.85.0
target=aarch64-apple-darwin
patch="$root/macos/alacritty-osc22.patch"
output="${1:-$root/artifacts/macos-cache/alacritty-custom}"
if [[ "$(uname -s)" == Darwin ]]; then
    xcode-select -p >/dev/null
    build_command=build
else
    command -v cargo-zigbuild >/dev/null
    [[ -d "${SDKROOT:-}/System/Library/Frameworks" ]] || { echo 'macOS SDK is required.' >&2; exit 1; }
    # Do not use the Linux host's freetype.pc for an Apple target. freetype-sys
    # falls back to compiling its bundled sources with Zig for arm64 macOS.
    export FREETYPE2_NO_PKG_CONFIG=1
    build_command=zigbuild
fi
command -v rustup >/dev/null
command -v python3 >/dev/null
mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/navidnd-alacritty.XXXXXX")"
trap 'rm -rf -- "$work"' EXIT
git clone --quiet --depth 1 --branch v0.17.0 --no-checkout https://github.com/alacritty/alacritty.git "$work/source"
git -C "$work/source" checkout --quiet --detach "$source_commit"
git -C "$work/source" apply --check "$patch"
git -C "$work/source" apply "$patch"
rustup toolchain install "$toolchain" --profile minimal
rustup target add --toolchain "$toolchain" "$target"
cd "$work/source"
rustc "+$toolchain" --edition 2021 --test alacritty/src/renderer/text/braille.rs -o "$work/braille-tests"
"$work/braille-tests"
cargo "+$toolchain" test --locked -p alacritty_terminal
cargo "+$toolchain" "$build_command" --locked --release --target "$target" -p alacritty --no-default-features
cp "${CARGO_TARGET_DIR:-target}/$target/release/alacritty" "$output/alacritty"
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
