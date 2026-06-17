#!/bin/bash
# Build the plugin in Release and assemble the auto-load bundle in dist/.
# Result: dist/HydroComplete.bundle/  — copy that folder into
#   %APPDATA%\Autodesk\ApplicationPlugins\  to auto-load in Civil 3D.
set -e
cd "$(dirname "$0")"

CONFIG="${1:-Release}"
echo "Building HydroComplete.Civil3D ($CONFIG)..."
dotnet build src/HydroComplete.Civil3D/HydroComplete.Civil3D.csproj -c "$CONFIG"

OUT="src/HydroComplete.Civil3D/bin/$CONFIG/net8.0-windows"
DEST="dist/HydroComplete.bundle/Contents"
mkdir -p "$DEST"

# Ship the plugin and its portable engine. Host (Ac*/Aecc*/Aec*) assemblies are
# NOT copied — they load from the running AutoCAD process.
cp "$OUT/HydroComplete.Civil3D.dll" "$DEST/"
cp "$OUT/HydroComplete.Engine.dll" "$DEST/"

echo "Bundle assembled at dist/HydroComplete.bundle/"
ls -la "$DEST"
