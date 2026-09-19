#!/usr/bin/env python3
import os
import pathlib
import sys
import zipfile

FIXED_TIME = (1980, 1, 1, 0, 0, 0)
TOKENS = {
    b"__RAGNAVIK_ENTRY_ENVIRONMENT__": "RAGNAVIK_ENTRY_ENVIRONMENT",
    b"__RAGNAVIK_SERVER_ADDRESS__": "RAGNAVIK_SERVER_ADDRESS",
    b"__RAGNAVIK_SERVER_PORT__": "RAGNAVIK_SERVER_PORT",
}

def packaged_bytes(source: pathlib.Path) -> bytes:
    data = source.read_bytes()
    for token, variable in TOKENS.items():
        if token not in data:
            continue
        value = os.environ.get(variable, "").strip()
        if not value:
            raise SystemExit(f"release artifact requires {variable}")
        if any(character in value for character in "\r\n\t"):
            raise SystemExit(f"{variable} contains invalid whitespace")
        data = data.replace(token, value.encode("utf-8"))
    return data


def add_file(archive: zipfile.ZipFile, source: pathlib.Path, destination: str) -> None:
    info = zipfile.ZipInfo(destination, FIXED_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, packaged_bytes(source), compresslevel=9)


def main() -> int:
    if len(sys.argv) != 4:
        raise SystemExit("usage: package.py PACKAGE_DIR DLL_PATH OUTPUT_ZIP")

    package_dir = pathlib.Path(sys.argv[1])
    dll_path = pathlib.Path(sys.argv[2])
    output_zip = pathlib.Path(sys.argv[3])
    files = [
        (package_dir / "manifest.json", "manifest.json"),
        (package_dir / "README.md", "README.md"),
        (package_dir / "CHANGELOG.md", "CHANGELOG.md"),
        (package_dir / "icon.png", "icon.png"),
        (package_dir / "assets/ragnavik-fjord-gate.png", "plugins/RagnavikUI/ragnavik-fjord-gate.png"),
        (package_dir / "assets/discord.png", "plugins/RagnavikUI/discord.png"),
        (package_dir / "assets/buymeacoffee.png", "plugins/RagnavikUI/buymeacoffee.png"),
        (package_dir / "direct-entry.env", "plugins/RagnavikUI/direct-entry.env"),
        (package_dir / "character-environment.env", "plugins/RagnavikUI/character-environment.env"),
        (package_dir / "config/Azumatt.AzuClock.cfg", "config/Azumatt.AzuClock.cfg"),
        (dll_path, "plugins/RagnavikUI/RagnavikUI.dll"),
    ]
    missing = [str(path) for path, _ in files if not path.is_file()]
    if missing:
        raise SystemExit("missing package inputs: " + ", ".join(missing))

    output_zip.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output_zip, "w") as archive:
        for source, destination in sorted(files, key=lambda item: item[1]):
            add_file(archive, source, destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
