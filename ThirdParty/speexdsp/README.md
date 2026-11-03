# SpeexDSP

Acoustic echo cancellation, noise suppression and gain control for voice calls.
Used from C# through `Yggdrasil.Audio.EchoCanceller`.

- **Upstream:** https://github.com/xiph/speexdsp
- **Version:** SpeexDSP 1.2.1 (tag `SpeexDSP-1.2.1`)
- **Source archive:** `https://github.com/xiph/speexdsp/archive/refs/tags/SpeexDSP-1.2.1.tar.gz`
  (SHA-256 `d17ca363654556a4ff1d02cc13d9eb1fc5a8642c90b40bd54ce266c3807b91a7`)
- **License:** BSD-3-Clause, see [COPYING](COPYING). Binary releases must include that file
  (the app build copies it to `THIRD_PARTY_LICENSES\speexdsp.txt`).

## Contents

| Path | What |
|---|---|
| `src/`, `include/speex/` | Unmodified upstream library sources and headers (test programs omitted) |
| `libspeexdsp.def` | Upstream Windows export list (`win32/libspeexdsp.def`) |
| `config.h`, `include/speex/speexdsp_config_types.h` | Hand-written replacements for the autotools-generated files |
| `build.sh` | Builds the DLLs with MSYS2 MinGW-w64 |
| `prebuilt/x86`, `prebuilt/x64` | Built `libspeexdsp.dll`, copied next to the app as `x86\` and `x64\` |

## Building

Needs MSYS2 with the msvcrt-based toolchains (`ucrt64` does not run on Windows XP):

```
pacman -S mingw-w64-i686-gcc mingw-w64-x86_64-gcc
sh ThirdParty/speexdsp/build.sh
```

The build is floating point with the built-in smallft FFT and no SSE, links libgcc
statically, and marks the DLLs for Windows XP (5.01) / XP x64 (5.02). The resulting DLLs
import only `KERNEL32.dll` and `msvcrt.dll`.
