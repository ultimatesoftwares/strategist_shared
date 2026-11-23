#!/bin/sh
# Wine feasibility gate for the NativeAOT FleetBridge DLL.
#
# Usage: ./run.sh <FleetBridge-win64.dll> [FleetBridge-win32.dll]
#
# Cross-compiles the loader with MinGW, then runs it under Wine inside the
# terminals Docker image (the exact environment MT4/MT5 run in). Download the
# DLLs from the GitHub Actions "FleetBridge-win-x64"/"FleetBridge-win-x86"
# artifacts first.
#
# Requirements on the host: docker, g{cc,++}-mingw-w64 (posix threads), and a
# locally built terminals:latest image.
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
dll64="${1:?usage: run.sh <FleetBridge-win64.dll> [FleetBridge-win32.dll]}"
dll32="${2:-}"

image="${TERMINALS_IMAGE:-terminals:latest}"

echo "== compiling winecheck loaders =="
x86_64-w64-mingw32-gcc-posix -O2 -municode -o "$here/winecheck64.exe" "$here/loader.c"
if [ -n "$dll32" ]; then
    i686-w64-mingw32-gcc-posix -O2 -municode -o "$here/winecheck32.exe" "$here/loader.c"
fi

run_in_wine() {
    exe="$1"
    dll="$2"
    echo "== running $exe against $dll under $image =="
    docker run --rm \
        -v "$here:/winecheck:ro" \
        -v "$(cd "$(dirname "$dll")" && pwd)/$(basename "$dll"):/winecheck/FleetBridge.dll:ro" \
        --entrypoint /bin/bash \
        "$image" \
        -c 'export WINEPREFIX=/root/.wine WINEDEBUG=-all DISPLAY=:99 && \
            (Xvfb :99 -screen 0 1024x768x16 >/dev/null 2>&1 &) && sleep 2 && \
            wine "/winecheck/'"$exe"'" "Z:\\winecheck\\FleetBridge.dll"'
}

run_in_wine winecheck64.exe "$dll64"
if [ -n "$dll32" ]; then
    run_in_wine winecheck32.exe "$dll32"
fi

echo "ALL WINE CHECKS PASSED"
