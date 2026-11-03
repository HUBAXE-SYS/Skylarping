#!/bin/sh
# Builds libfvad.dll for Windows XP+ (x86 and x64) with MSYS2's MinGW-w64 toolchains.
# Requires the msvcrt-based "mingw32" and "mingw64" environments (not ucrt64, which doesn't run on XP):
#   pacman -S mingw-w64-i686-gcc mingw-w64-x86_64-gcc
# Usage (from any shell):  sh ThirdParty/libfvad/build.sh [path-to-msys64]
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
MSYS="${1:-/c/msys64}"
SRC="fvad.c
signal_processing/division_operations.c signal_processing/energy.c signal_processing/get_scaling_square.c
signal_processing/resample_48khz.c signal_processing/resample_by_2_internal.c
signal_processing/resample_fractional.c signal_processing/spl_inl.c
vad/vad_core.c vad/vad_filterbank.c vad/vad_gmm.c vad/vad_sp.c"

build() {
    arch="$1"; toolchain="$2"; osver="$3"
    out="$HERE/prebuilt/$arch"
    mkdir -p "$out" "$HERE/obj/$arch"
    PATH="$MSYS/$toolchain/bin:$PATH"
    objs=""
    for f in $SRC; do
        o="$HERE/obj/$arch/$(echo "$f" | tr '/' '_' | sed 's/\.c$/.o/')"
        gcc -c -O2 -std=gnu11 -D_WIN32_WINNT=0x0501 -DWINVER=0x0501 \
            -I"$HERE/include" -I"$HERE/src" -o "$o" "$HERE/src/$f"
        objs="$objs $o"
    done
    # Static libgcc so the DLL only depends on system DLLs; XP OS/subsystem version in the PE header.
    gcc -shared -static-libgcc -o "$out/libfvad.dll" $objs "$HERE/libfvad.def" \
        -Wl,--major-os-version,5,--minor-os-version,"$osver" \
        -Wl,--major-subsystem-version,5,--minor-subsystem-version,"$osver" \
        -Wl,--nxcompat,--dynamicbase -s
    echo "built $out/libfvad.dll"
}

build x86 mingw32 1   # Windows XP (5.1)
build x64 mingw64 2   # Windows XP x64 / Server 2003 (5.2)
rm -rf "$HERE/obj"
