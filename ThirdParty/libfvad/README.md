# libfvad

Voice activity detection (is this audio frame someone speaking?), extracted from WebRTC.
Used from C# through `Yggdrasil.Audio.VoiceDetector`, typically on the output of
`Yggdrasil.Audio.EchoCanceller` to decide when the user is talking or interrupting.

- **Upstream:** https://github.com/dpirch/libfvad
- **Version:** v1.0 (tag `v1.0`)
- **Source archive:** `https://github.com/dpirch/libfvad/archive/refs/tags/v1.0.tar.gz`
  (SHA-256 `8c8a1e4911a454b1a8e3ed062abbce4865eea3fc794417e2c96373309174215c`)
- **License:** BSD-3-Clause, see [LICENSE](LICENSE), plus the WebRTC patent grant in [PATENTS](PATENTS).
  Binary releases must include both (the app build copies them to `THIRD_PARTY_LICENSES\`).

## Contents

| Path | What |
|---|---|
| `src/`, `include/` | Unmodified upstream library sources and headers (tests, examples and autotools files omitted) |
| `libfvad.def` | Export list (the six public `fvad_*` functions) |
| `build.sh` | Builds the DLLs with MSYS2 MinGW-w64 |
| `prebuilt/x86`, `prebuilt/x64` | Built `libfvad.dll`, copied next to the app as `x86\` and `x64\` |

## Building

Needs MSYS2 with the msvcrt-based toolchains (`ucrt64` does not run on Windows XP):

```
pacman -S mingw-w64-i686-gcc mingw-w64-x86_64-gcc
sh ThirdParty/libfvad/build.sh
```

Integer-only C, statically linked libgcc, DLLs marked for Windows XP (5.01) / XP x64 (5.02).
