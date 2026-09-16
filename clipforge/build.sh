#!/usr/bin/env bash
#
# Builds the ClipForge Windows binaries and installer.
#
# Runs on Windows or Linux: the .NET SDK cross-compiles to win-x64, and NSIS (makensis) produces
# the installer. Requires the Microsoft .NET 8 SDK - the Ubuntu dotnet-sdk-8.0 package ships
# without the WindowsDesktop targets and cannot build WinForms.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/ClipForge/ClipForge.csproj"
ICON="$ROOT/src/ClipForge/Assets/app.ico"
DIST="$ROOT/dist"
STAGE="${CLIPFORGE_STAGE:-$ROOT/.build}"
VERSION="1.0.0"

DOTNET="${DOTNET:-dotnet}"
command -v "$DOTNET" >/dev/null || { echo "error: dotnet SDK not found"; exit 1; }

echo ">> cleaning"
rm -rf "$STAGE" "$DIST"
mkdir -p "$STAGE" "$DIST"

COMMON=(-c Release -r win-x64 --self-contained true -p:DebugType=none -p:Version="$VERSION" --nologo)

echo ">> publishing folder build"
"$DOTNET" publish "$PROJECT" "${COMMON[@]}" -p:PublishSingleFile=false -o "$STAGE/app"

echo ">> publishing portable single-file build"
"$DOTNET" publish "$PROJECT" "${COMMON[@]}" \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$STAGE/portable"

cp "$STAGE/portable/ClipForge.exe" "$DIST/ClipForge-${VERSION}-portable.exe"

if command -v makensis >/dev/null; then
    echo ">> building installer"
    makensis -V2 \
        -DPAYLOAD="$STAGE/app" \
        -DICONFILE="$ICON" \
        -DOUTFILE="$DIST/ClipForge-${VERSION}-Setup.exe" \
        "$ROOT/installer/ClipForge.nsi"
else
    echo ">> makensis not found - skipping the installer (portable build still produced)"
fi

echo
echo "Artifacts in $DIST:"
ls -lh "$DIST"
