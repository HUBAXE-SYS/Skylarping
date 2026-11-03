/* Skylarping build configuration for SpeexDSP 1.2.1 (replaces the autotools-generated config.h).
   Target: Windows XP and later, x86 and x64, built with MinGW-w64 (see build.sh). */

/* Floating-point build with the built-in smallft FFT (the upstream default). */
#define FLOATING_POINT
#define USE_SMALLFT

/* No SSE: plain C so the 32-bit DLL also runs on pre-SSE XP-era CPUs.
   At 16 kHz mono the echo canceller is cheap enough without it. */

/* GCC supports C99 variable-length arrays for scratch buffers. */
#define VAR_ARRAYS

/* Exported symbols come from libspeexdsp.def. */
#define EXPORT
