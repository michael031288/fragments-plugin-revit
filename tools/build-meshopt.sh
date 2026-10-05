#!/usr/bin/env bash
# Builds the meshoptimizer 0.25 vertex and index codecs vendored under
# native/meshoptimizer (the same sources Tessera's meshopt 0.6.2 crate links).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/native/meshoptimizer/src"
out="$root/native/meshoptimizer/build"
mkdir -p "$out"
so="$out/libmeshoptimizer.so"
vertex="$src/vertexcodec.cpp"
index="$src/indexcodec.cpp"
if [[ -f "$so" && "$so" -nt "$vertex" && "$so" -nt "$index" ]]; then
  exit 0
fi
g++ -shared -fPIC -O2 -DNDEBUG -o "$so" "$vertex" "$index"
