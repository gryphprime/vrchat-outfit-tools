#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(CDPATH= cd -- "$script_dir/.." && pwd)
package_dir=${PACKAGE_DIR:-"$repo_dir/Packages/net.orpine.gryphprime.vrchat-outfit-tools"}
manifest="$package_dir/package.json"

fail() {
  printf '%s\n' "error: $*" >&2
  exit 1
}

command -v python3 >/dev/null 2>&1 || fail "python3 is required to validate package.json"
[ -d "$package_dir" ] || fail "package directory is missing: $package_dir"

for path in \
  package.json \
  README.md \
  Editor/OutfitToggleGenerator.cs \
  Editor/net.orpine.gryphprime.vrchat-outfit-tools.Editor.asmdef \
  Runtime/OutfitToggleGeneratedMenu.cs \
  Runtime/net.orpine.gryphprime.vrchat-outfit-tools.Runtime.asmdef \
  AppleIntelligence/OutfitToggleAppleIntelligence.swift \
  AppleIntelligence/build-macos.sh \
  Plugins/macOS/OutfitToggleAppleIntelligence.bundle/Contents/Info.plist \
  Plugins/macOS/OutfitToggleAppleIntelligence.bundle/Contents/MacOS/OutfitToggleAppleIntelligence
do
  [ -f "$package_dir/$path" ] || fail "expected package file is missing: $path"
done

python3 -c '
import json
import re
import sys
from urllib.parse import urlsplit

manifest = sys.argv[1]
try:
    with open(manifest, encoding="utf-8") as source:
        package = json.load(source)
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"error: invalid package manifest {manifest}: {error}")

errors = []
if package.get("name") != "net.orpine.gryphprime.vrchat-outfit-tools":
    errors.append("name must be net.orpine.gryphprime.vrchat-outfit-tools")
version = package.get("version")
if not isinstance(version, str) or not re.fullmatch(
    r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?",
    version or "",
):
    errors.append("version must be a semantic version")
if package.get("unity") != "2022.3":
    errors.append("unity must be 2022.3")
if not isinstance(package.get("vpmDependencies"), dict):
    errors.append("vpmDependencies must be an object")
author = package.get("author")
if not isinstance(author, dict):
    errors.append("author must be an object")
else:
    if not isinstance(author.get("name"), str) or not author["name"].strip():
        errors.append("author.name must be a non-empty string")
    if not isinstance(author.get("email"), str) or not author["email"].strip():
        errors.append("author.email must be a non-empty string")
url = package.get("url")
parsed_url = urlsplit(url) if isinstance(url, str) else None
if not parsed_url or (
    parsed_url.scheme != "https"
    or not parsed_url.netloc
    or parsed_url.username
    or parsed_url.password
    or parsed_url.query
    or parsed_url.fragment
    or not parsed_url.path.endswith(".zip")
):
    errors.append("url must be a direct HTTPS ZIP download")
if errors:
    raise SystemExit("error: invalid package manifest: " + "; ".join(errors))
print(version)
' "$manifest"
