#!/usr/bin/env python3
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent
manifest_path = root / "package/manifest.json"
source_path = root / "src/RagnavikUI.cs"
errors: list[str] = []

manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
required_keys = {"name", "version_number", "website_url", "description", "dependencies"}
if set(manifest) != required_keys:
    errors.append(f"manifest keys must be exactly {sorted(required_keys)}")
if manifest.get("name") != "Ragnavik_UI":
    errors.append("manifest name must be Ragnavik_UI")
if not re.fullmatch(r"\d+\.\d+\.\d+", manifest.get("version_number", "")):
    errors.append("manifest version_number must be semantic version x.y.z")
dependencies = manifest.get("dependencies", [])
if not isinstance(dependencies, list) or len(dependencies) != len(set(dependencies)):
    errors.append("manifest dependencies must be a unique list")

source = source_path.read_text(encoding="utf-8")
match = re.search(r'BepInPlugin\("lostkode\.ragnavik\.ui",\s*"Ragnavik UI",\s*"([^"]+)"\)', source)
if not match:
    errors.append("source must declare the expected plugin ID and name")
elif match.group(1) != manifest.get("version_number"):
    errors.append("source plugin version does not match manifest version")

required_files = [
    root / "package/README.md",
    root / "package/CHANGELOG.md",
    root / "package/icon.png",
    root / "package/config/Azumatt.AzuClock.cfg",
]
for path in required_files:
    if not path.is_file() or path.stat().st_size == 0:
        errors.append(f"required package asset missing or empty: {path.relative_to(root)}")

if errors:
    print("validation failed:", file=sys.stderr)
    for error in errors:
        print(f"  - {error}", file=sys.stderr)
    raise SystemExit(1)

print(f"validated Ragnavik UI {manifest['version_number']} source and package metadata")
