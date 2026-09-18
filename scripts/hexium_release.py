#!/usr/bin/env python3
"""Validate Hexium packages and fail closed until an upload API is documented."""

from __future__ import annotations

import argparse
import json
import re
import urllib.request
import zipfile
from pathlib import Path
from urllib.parse import urlparse

DEPENDENCY = re.compile(r"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$")
IDENTITY = re.compile(r"^(?P<namespace>[A-Za-z0-9_]+)-(?P<name>[A-Za-z0-9_]+)$")
VERSION = re.compile(r"^\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$")
REQUIRED_ROOT_FILES = {"manifest.json", "README.md", "icon.png"}


def fail(message: str) -> None:
    raise SystemExit(f"Hexium release validation failed: {message}")


def load_manifest(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        fail(f"cannot read {path}: {error}")


def validate_blog(url: str, check_reachable: bool) -> None:
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in {
        "ragnavik.com", "www.ragnavik.com", "ragnavik.vercel.app"
    }:
        fail("blog URL must be an HTTPS Ragnavik website URL")
    if not check_reachable:
        return
    request = urllib.request.Request(url, headers={"User-Agent": "Ragnavik release validation"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            if response.status >= 400:
                fail(f"blog URL returned HTTP {response.status}")
    except Exception as error:
        fail(f"blog URL is not publicly reachable: {error}")


def validate(args: argparse.Namespace) -> dict:
    manifest = load_manifest(args.manifest)
    identity = IDENTITY.fullmatch(args.identity)
    if not identity:
        fail("identity must use Team-Package format")
    if identity["namespace"] != "LostKode" or manifest.get("name") != identity["name"]:
        fail(f"manifest identity does not match {args.identity}")
    if manifest.get("version_number") != args.expected_version:
        fail(f"expected version {args.expected_version}, found {manifest.get('version_number')}")
    if not VERSION.fullmatch(args.expected_version):
        fail("version is not supported by Hexium")
    expected_artifact = f"{args.identity}-{args.expected_version}.zip"
    if args.package.name != expected_artifact:
        fail(f"artifact must be named {expected_artifact}")
    for field in ("name", "description", "version_number", "website_url", "dependencies"):
        if field not in manifest:
            fail(f"manifest is missing required field {field}")
    if len(manifest["description"]) > 256:
        fail("description exceeds Hexium's 256 character limit")
    for dependency in manifest["dependencies"]:
        if not DEPENDENCY.fullmatch(dependency):
            fail(f"invalid dependency string: {dependency}")
    if args.package.stat().st_size > 512 * 1024 * 1024:
        fail("archive exceeds Hexium's 512 MB limit")
    with zipfile.ZipFile(args.package) as archive:
        bad_entry = archive.testzip()
        if bad_entry:
            fail(f"archive CRC failed for {bad_entry}")
        names = set(archive.namelist())
        missing = REQUIRED_ROOT_FILES - names
        if missing:
            fail(f"package is missing required root entries: {sorted(missing)}")
        packaged = json.loads(archive.read("manifest.json"))
    if packaged != manifest:
        fail("packaged manifest does not match the source manifest")
    validate_blog(args.blog_url, not args.skip_blog_check)
    result = {
        "release_id": args.release_id,
        "identity": args.identity,
        "version": args.expected_version,
        "artifact": args.package.name,
        "blog_url": args.blog_url,
        "upload_enabled": False,
    }
    print(json.dumps(result, indent=2))
    return result


def upload(args: argparse.Namespace) -> None:
    validate(args)
    fail(
        "automated upload is disabled because Hexium's official OpenAPI specification does not "
        "document a package publication endpoint; use the submit page only after explicit publication approval"
    )


def add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--identity", required=True)
    parser.add_argument("--expected-version", required=True)
    parser.add_argument("--release-id", required=True)
    parser.add_argument("--blog-url", required=True)
    parser.add_argument("--skip-blog-check", action="store_true")


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    check = commands.add_parser("validate")
    add_common(check)
    check.set_defaults(func=validate)
    publish = commands.add_parser("upload")
    add_common(publish)
    publish.set_defaults(func=upload)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
