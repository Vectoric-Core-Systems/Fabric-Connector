#!/bin/sh
# Convert a Godot project with the Fabric Connector and check the result against Godot itself (tools/godot-dump/README.md).
#   run_compare.sh <godot exe> <godot project dir> <AverAssetC> <FabricGodotCompareTest> [work dir]
# Godot only ever runs --headless, on a copy of the project in the work dir (default: a folder under $TMPDIR).
set -e
if [ "$#" -lt 4 ]; then
  echo "usage: $0 <godot exe> <godot project dir> <AverAssetC> <FabricGodotCompareTest> [work dir]" >&2
  exit 2
fi
GODOT=$1
PROJECT=$2
ASSETC=$3
COMPARE=$4
WORK=${5:-${TMPDIR:-/tmp}/fabric-godot-compare}
rm -rf "$WORK"
mkdir -p "$WORK"
"$ASSETC" fabric "$PROJECT" "$WORK/out" --overwrite
CONVERTED=$(find "$WORK/out" -mindepth 1 -maxdepth 1 -type d | head -n 1)
"$COMPARE" compare --converted "$CONVERTED" --godot "$GODOT" --godot-project "$PROJECT" --work "$WORK/godot-project"
