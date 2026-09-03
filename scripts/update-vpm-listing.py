#!/usr/bin/env python3
"""Add one immutable VPM package version to a VCC listing."""

import json
import re
import sys
from pathlib import Path
from urllib.parse import urlsplit


def fail(message):
    raise SystemExit(f"error: {message}")


def load_json(path):
    try:
        with path.open(encoding="utf-8") as source:
            return json.load(source)
    except (OSError, json.JSONDecodeError) as error:
        fail(f"invalid JSON in {path}: {error}")


def direct_https_zip(url):
    parsed = urlsplit(url) if isinstance(url, str) else None
    return bool(
        parsed
        and parsed.scheme == "https"
        and parsed.netloc
        and not parsed.username
        and not parsed.password
        and not parsed.query
        and not parsed.fragment
        and parsed.path.endswith(".zip")
    )


def main():
    if len(sys.argv) != 4:
        fail(f"usage: {Path(sys.argv[0]).name} LISTING PACKAGE_MANIFEST ZIP_SHA256")

    listing_path, manifest_path = map(Path, sys.argv[1:3])
    zip_sha256 = sys.argv[3]
    if not re.fullmatch(r"[0-9a-fA-F]{64}", zip_sha256):
        fail("ZIP_SHA256 must be 64 hexadecimal characters")

    listing = load_json(listing_path)
    manifest = load_json(manifest_path)
    if not isinstance(listing, dict):
        fail("listing must be an object")
    for key in ("name", "id", "url"):
        if not isinstance(listing.get(key), str) or not listing[key]:
            fail(f"listing.{key} must be a non-empty string")
    author = listing.get("author")
    if not isinstance(author, dict) or not all(
        isinstance(author.get(key), str) and author[key] for key in ("name", "email")
    ):
        fail("listing.author must contain non-empty name and email")
    packages = listing.get("packages")
    if not isinstance(packages, dict):
        fail("listing.packages must be an object")

    name, version, url = (manifest.get(key) for key in ("name", "version", "url"))
    if not isinstance(name, str) or not name or not isinstance(version, str) or not version:
        fail("package manifest name and version must be non-empty strings")
    if not direct_https_zip(url):
        fail("package manifest url must be a direct HTTPS ZIP download")

    release = dict(manifest)
    release["zipSHA256"] = zip_sha256.lower()
    package = packages.get(name)
    if package is None:
        package = {"versions": {}}
        packages[name] = package
    if not isinstance(package, dict) or not isinstance(package.get("versions"), dict):
        fail(f"listing.packages[{name!r}].versions must be an object")
    versions = package["versions"]
    existing = versions.get(version)
    if existing is not None:
        if existing == release:
            return
        fail(f"conflicting existing package version: {name} {version}")

    versions[version] = release
    listing_path.write_text(json.dumps(listing, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
