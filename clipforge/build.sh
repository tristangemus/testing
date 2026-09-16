#!/usr/bin/env bash
#
# Builds the ClipForge Windows binaries and installers.
#
# Runs on Windows or Linux: the .NET SDK cross-compiles to win-x64, and NSIS (makensis) produces
# the installers. Requires the Microsoft .NET 8 SDK - the Ubuntu dotnet-sdk-8.0 package ships
# without the WindowsDesktop targets and cannot build WinForms.
#
# Two editions of each artifact are produced:
#   standalone - bundles the .NET runtime. Large, but runs on a clean Windows install.
#   compact    - needs the .NET 8 Desktop Runtime; the installer offers to fetch it.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/ClipForge/ClipForge.csproj"
ICON="$ROOT/src/ClipForge/Assets/app.ico"
NSI="$ROOT/installer/ClipForge.nsi"
DIST="$ROOT/dist"
STAGE="${CLIPFORGE_STAGE:-$ROOT/.build}"
VERSION="1.0.0"

DOTNET="${DOTNET:-dotnet}"
command -v "$DOTNET" >/dev/null || { echo "error: dotnet SDK not found"; exit 1; }

echo ">> cleaning"
rm -rf "$STAGE" "$DIST"
mkdir -p "$STAGE" "$DIST"

publish() { # <self-contained> <single-file> <output>
    "$DOTNET" publish "$PROJECT" -c Release -r win-x64 --nologo \
        -p:DebugType=none -p:Version="$VERSION" \
        --self-contained "$1" \
        -p:PublishSingleFile="$2" \
        ${3:+} -o "$3" \
        $( [ "$2" = true ] && [ "$1" = true ] && echo "-p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true" )
}

echo ">> publishing standalone (bundled runtime)"
publish true false "$STAGE/standalone-app"
publish true true  "$STAGE/standalone-portable"

echo ">> publishing compact (framework-dependent)"
publish false false "$STAGE/compact-app"
publish false true  "$STAGE/compact-portable"

cp "$STAGE/standalone-portable/ClipForge.exe" "$DIST/ClipForge-${VERSION}-portable.exe"
cp "$STAGE/compact-portable/ClipForge.exe"    "$DIST/ClipForge-${VERSION}-portable-compact.exe"

if command -v makensis >/dev/null; then
    echo ">> building standalone installer"
    makensis -V2 -DPAYLOAD="$STAGE/standalone-app" -DICONFILE="$ICON" \
        -DOUTFILE="$DIST/ClipForge-${VERSION}-Setup.exe" "$NSI"

    echo ">> building compact installer"
    makensis -V2 -DPAYLOAD="$STAGE/compact-app" -DICONFILE="$ICON" -DREQUIRE_RUNTIME=1 \
        -DOUTFILE="$DIST/ClipForge-${VERSION}-Setup-compact.exe" "$NSI"
else
    echo ">> makensis not found - skipping installers (portable builds still produced)"
fi

echo
echo "Artifacts in $DIST:"
ls -lh "$DIST"
