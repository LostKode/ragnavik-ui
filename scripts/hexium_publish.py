#!/usr/bin/env python3
"""Prepare TCLI metadata and verify a published Hexium package."""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from urllib.parse import urlparse

DEPENDENCY = re.compile(r"^(?P<key>[A-Za-z0-9_]+-[A-Za-z0-9_]+)-(?P<version>\d+\.\d+\.\d+)$")


def fail(message: str) -> None:
    raise SystemExit(message)


def quote(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def load_manifest(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def prepare(args: argparse.Namespace) -> None:
    manifest = load_manifest(args.manifest)
    if manifest["version_number"] != args.expected_version:
        fail(f"expected version {args.expected_version}, found {manifest['version_number']}")
    if args.blog_url:
        parsed_url = urlparse(args.blog_url)
        if parsed_url.scheme != "https" or parsed_url.hostname not in {
            "ragnavik.com", "www.ragnavik.com", "ragnavik.vercel.app"
        }:
            fail("blog URL must be an HTTPS Ragnavik website URL")
        request = urllib.request.Request(
            args.blog_url,
            headers={"User-Agent": "Ragnavik release validation"},
        )
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                if response.status >= 400:
                    fail(f"blog URL returned HTTP {response.status}")
        except Exception as error:
            fail(f"blog URL is not publicly reachable: {error}")
    with zipfile.ZipFile(args.package) as archive:
        names = set(archive.namelist())
        missing = {"manifest.json", "README.md", "icon.png"} - names
        if missing:
            fail(f"package is missing required entries: {sorted(missing)}")
        packaged = json.loads(archive.read("manifest.json"))
    if packaged != manifest:
        fail("packaged manifest does not match the source manifest")

    dependencies: dict[str, str] = {}
    for dependency in manifest.get("dependencies", []):
        match = DEPENDENCY.fullmatch(dependency)
        if not match:
            fail(f"invalid dependency string: {dependency}")
        dependencies[match["key"]] = match["version"]

    lines = [
        '[config]',
        'schemaVersion = "0.0.1"',
        '',
        '[package]',
        'namespace = "LostKode"',
        f"name = {quote(manifest['name'])}",
        f"versionNumber = {quote(manifest['version_number'])}",
        f"description = {quote(manifest['description'])}",
        f"websiteUrl = {quote(manifest['website_url'])}",
        'containsNsfwContent = false',
        '',
        '[package.dependencies]',
    ]
    lines.extend(f"{quote(key)} = {quote(version)}" for key, version in sorted(dependencies.items()))
    lines.extend([
        '',
        '[build]',
        'outdir = ".tcli-build"',
        '',
        '[publish]',
        f"repository = {quote(args.repository.rstrip('/'))}",
        'communities = ["valheim"]',
        '',
        '[publish.categories]',
        'valheim = [' + ', '.join(quote(item) for item in args.category) + ']',
        '',
    ])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({
        "release_id": args.release_id,
        "package": f"LostKode-{manifest['name']}-{manifest['version_number']}",
        "blog_url": args.blog_url,
        "archive": str(args.package),
    }, indent=2))


def current_version(name: str) -> str | None:
    url = f"https://valheim.hexium.gg/mods/LostKode/{name}"
    request = urllib.request.Request(url, headers={"User-Agent": "Ragnavik release validation"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            page = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise
    for payload in re.findall(r'<script type="application/ld\+json">(.*?)</script>', page, re.DOTALL):
        metadata = json.loads(payload)
        if metadata.get("@type") == "SoftwareApplication":
            version = metadata.get("softwareVersion")
            if isinstance(version, str):
                return version
    fail(f"Hexium page did not expose structured version metadata: {url}")


def current(args: argparse.Namespace) -> None:
    print(current_version(args.name) or "")


def verify(args: argparse.Namespace) -> None:
    url = f"https://valheim.hexium.gg/mods/LostKode/{args.name}"
    for attempt in range(1, 13):
        try:
            published = current_version(args.name)
            if published == args.version:
                print(f"verified LostKode-{args.name}-{args.version} at {url}")
                return
            detail = "not published" if published is None else f"currently exposes {published}"
            print(f"verification attempt {attempt}/12: {detail}", file=sys.stderr)
        except Exception as error:
            print(f"verification attempt {attempt}/12: {error}", file=sys.stderr)
        time.sleep(10)
    fail(f"Hexium did not expose LostKode-{args.name}-{args.version} at {url}")


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("--manifest", type=Path, required=True)
    prep.add_argument("--package", type=Path, required=True)
    prep.add_argument("--expected-version", required=True)
    prep.add_argument("--release-id", required=True)
    prep.add_argument("--blog-url")
    prep.add_argument("--category", action="append", required=True)
    prep.add_argument("--repository", default="https://hexium.gg")
    prep.add_argument("--output", type=Path, required=True)
    prep.set_defaults(func=prepare)
    live = commands.add_parser("current")
    live.add_argument("--name", required=True)
    live.set_defaults(func=current)
    check = commands.add_parser("verify")
    check.add_argument("--name", required=True)
    check.add_argument("--version", required=True)
    check.add_argument("--repository", default="https://hexium.gg")
    check.set_defaults(func=verify)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
