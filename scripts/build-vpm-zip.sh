#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(CDPATH= cd -- "$script_dir/.." && pwd)
package_dir=${PACKAGE_DIR:-"$repo_dir/Packages/net.orpine.gryphprime.vrchat-outfit-tools"}
output_dir=${1:-${OUTPUT_DIR:-"$repo_dir/dist"}}

fail() {
  printf '%s\n' "error: $*" >&2
  exit 1
}

command -v python3 >/dev/null 2>&1 || fail "python3 is required to read package.json"
command -v zip >/dev/null 2>&1 || fail "zip is required to assemble the VPM archive"
version=$("$script_dir/validate-package.sh")
package_name=$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["name"])' "$package_dir/package.json")

mkdir -p "$output_dir"
output_dir=$(CDPATH= cd -- "$output_dir" && pwd)
archive="$output_dir/$package_name-$version.zip"
temporary_dir=$(mktemp -d "${TMPDIR:-/tmp}/$package_name-$version.XXXXXX")
temporary_archive="$temporary_dir/package.zip"
trap 'rm -rf "$temporary_dir"' 0 1 2 15

rm -f "$archive"
(
  cd "$package_dir"
  zip -q -r "$temporary_archive" . \
    -x '.DS_Store' '*/.DS_Store' '._*' '*/._*' '__MACOSX/*'
)
mv "$temporary_archive" "$archive"
trap - 0 1 2 15
rmdir "$temporary_dir"
printf '%s\n' "$archive"
