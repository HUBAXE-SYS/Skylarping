#!/bin/sh
# Builds libspeexdsp.dll for Windows XP+ (x86 and x64) with MSYS2's MinGW-w64 toolchains.
# Requires the msvcrt-based "mingw32" and "mingw64" environments (not ucrt64, which doesn't run on XP):
#   pacman -S mingw-w64-i686-gcc mingw-w64-x86_64-gcc
# Usage (from any shell):  sh ThirdParty/speexdsp/build.sh [path-to-msys64]
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
MSYS="${1:-/c/msys64}"
SRC="buffer.c fftwrap.c filterbank.c jitter.c mdf.c preprocess.c resample.c scal.c smallft.c"

build() {
    arch="$1"; toolchain="$2"; osver="$3"
    out="$HERE/prebuilt/$arch"
    mkdir -p "$out" "$HERE/obj/$arch"
    PATH="$MSYS/$toolchain/bin:$PATH"
    objs=""
    for f in $SRC; do
        o="$HERE/obj/$arch/${f%.c}.o"
        gcc -c -O2 -std=gnu99 -DHAVE_CONFIG_H -D_WIN32_WINNT=0x0501 -DWINVER=0x0501 \
            -I"$HERE" -I"$HERE/include" -o "$o" "$HERE/src/$f"
        objs="$objs $o"
    done
    # Static libgcc so the DLL only depends on system DLLs; XP OS/subsystem version in the PE header.
    gcc -shared -static-libgcc -o "$out/libspeexdsp.dll" $objs "$HERE/libspeexdsp.def" \
        -Wl,--major-os-version,5,--minor-os-version,"$osver" \
        -Wl,--major-subsystem-version,5,--minor-subsystem-version,"$osver" \
        -Wl,--nxcompat,--dynamicbase -s
    echo "built $out/libspeexdsp.dll"
}

build x86 mingw32 1   # Windows XP (5.1)
build x64 mingw64 2   # Windows XP x64 / Server 2003 (5.2)
rm -rf "$HERE/obj"
