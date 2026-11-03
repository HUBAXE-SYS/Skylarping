/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Skymu.Audio
{
    /// <summary>
    /// Loads a native DLL from the x86\ or x64\ folder next to the application (matching the
    /// process architecture) before its DllImports are first used. Once a DLL is loaded by full
    /// path, Windows resolves later DllImports of the same file name to that copy.
    /// </summary>
    internal static class NativeLoader
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        internal static void Ensure(string dllName)
        {
            lock (Sync)
            {
                if (Loaded.Contains(dllName))
                    return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string arch = IntPtr.Size == 8 ? "x64" : "x86";
                string[] candidates =
                {
                    Path.Combine(Path.Combine(baseDir, arch), dllName),
                    Path.Combine(baseDir, dllName),
                };

                foreach (string path in candidates)
                {
                    if (File.Exists(path) && LoadLibraryW(path) != IntPtr.Zero)
                    {
                        Loaded.Add(dllName);
                        return;
                    }
                }

                throw new DllNotFoundException(
                    "Could not load " + dllName + " (" + arch + "). Expected it at " + candidates[0] + ".");
            }
        }
    }
}
