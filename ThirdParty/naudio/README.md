# NAudio

Audio playback, recording and format handling (WAV/MP3 reading, `WaveOutEvent`, `WaveInEvent`, ...).
Used by the app's sound effects (`SoundManager`), the Stub plugin, and voice calls.

- **Upstream:** https://github.com/naudio/NAudio
- **Version:** NAudio 2.2.1 (tag `v2.2.1`), projects `NAudio.Core` and `NAudio.WinMM`, plus
  `NAudio/Mp3FileReader.cs` from the umbrella project (MP3 decoding via Windows' ACM codec)
- **Source archive:** `https://github.com/naudio/NAudio/archive/refs/tags/v2.2.1.tar.gz`
  (SHA-256 `4550d53d533e9a8252b9a87be032586a18da52e4c10845981fab743f6604907c`)
- **License:** MIT, see [license.txt](license.txt). Binary releases must include it
  (the app build copies it to `THIRD_PARTY_LICENSES\naudio.txt`).

## Why vendored

NAudio 1.10.0, the last release with a .NET Framework 3.5/4.0 build, is licensed under the
Ms-PL, which is not compatible with the AGPL. NAudio 2.x is MIT but only ships .NET Standard
2.0 builds, which .NET 4.0 can't load. Its source compiles for .NET 4.0 almost unchanged, so
it's built here as part of the solution (`NAudio.csproj`).

## Changes from upstream

Only the .NET 4.5.1 generic `Marshal` overloads were replaced with their .NET 4.0 forms
(16 call sites in `Wave/WaveFormats/WaveFormat.cs`, `Mixer/*.cs`):

- `Marshal.SizeOf<T>()` → `Marshal.SizeOf(typeof(T))`
- `Marshal.PtrToStructure<T>(ptr)` → `(T)Marshal.PtrToStructure(ptr, typeof(T))`

Everything else (both source trees, file layout, namespaces) is unmodified. The upstream
`.csproj` files are replaced by the single `NAudio.csproj`.
