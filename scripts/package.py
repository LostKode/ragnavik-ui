#!/usr/bin/env python3
import pathlib
import sys
import zipfile

FIXED_TIME = (1980, 1, 1, 0, 0, 0)


def add_file(archive: zipfile.ZipFile, source: pathlib.Path, destination: str) -> None:
    info = zipfile.ZipInfo(destination, FIXED_TIME)
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    archive.writestr(info, source.read_bytes(), compresslevel=9)


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
