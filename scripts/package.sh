#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$repo_root/scripts/build.sh"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version_number"])' "$repo_root/package/manifest.json")"
dll="$repo_root/src/bin/Release/netstandard2.1/RagnavikUI.dll"
output="$repo_root/artifacts/LostKode-Ragnavik_UI-$version.zip"
python3 "$repo_root/scripts/package.py" "$repo_root/package" "$dll" "$output"
sha256sum "$output"
