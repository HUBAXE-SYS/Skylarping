/*==========================================================*/
// Copyright © The Skymu Team and other contributors.
// For any inquiries or concerns, email contact@skymu.app.
/*==========================================================*/
// Modification or redistribution of this code is governed
// by the terms set out in the project license agreement.
// If you do not comply with those terms, you may not
// modify or distribute any original code from the project.
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Skymu.Native.Windows
{
    public enum PlatformType
    {
        [Description("Unknown")]
        Unknown,

        [Description("Wine (Legacy)")]
        WineLegacy,

        [Description("Wine 10")]
        Wine10,

        [Description("Wine 11")]
        Wine11,

        [Description("Windows 2000")]
        Windows2000,

        [Description("Windows XP")]
        WindowsXP,

        [Description("Windows Vista")]
        WindowsVista,

        [Description("Windows 7")]
        Windows7,

        [Description("Windows 8")]
        Windows8,

        [Description("Windows 8.1")]
        Windows81,

        [Description("Windows 10")]
        Windows10,

        [Description("Windows 11")]
        Windows11,

        [Description("Future Windows")]
        WindowsFuture,
    }

    class Runtime
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref OSVERSIONINFOEX versionInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public short wServicePackMajor;
            public short wServicePackMinor;
            public short wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        public static PlatformType DetectOS()
        {
            string wineVersion = GetWineVersion();
            if (wineVersion != null)
            {
                if (int.TryParse(wineVersion.Split('.')[0], out int wineMajor))
                {
                    if (wineMajor >= 11)
                        return PlatformType.Wine11;
                    if (wineMajor >= 10)
                        return PlatformType.Wine10;
                }
                return PlatformType.WineLegacy;
            }

            var info = new OSVERSIONINFOEX();
            info.dwOSVersionInfoSize = Marshal.SizeOf(info);
            RtlGetVersion(ref info);

            int major = info.dwMajorVersion;
            int minor = info.dwMinorVersion;
            int build = info.dwBuildNumber;

            if (major > 10)
                return PlatformType.WindowsFuture;
            else if (major == 10)
            {
                if (build >= 22000)
                    return PlatformType.Windows11;
                return PlatformType.Windows10;
            }
            else if (major == 6)
            {
                if (minor == 3)
                    return PlatformType.Windows81;
                if (minor == 2)
                    return PlatformType.Windows8;
                if (minor == 1)
                    return PlatformType.Windows7;
                if (minor == 0)
                    return PlatformType.WindowsVista;
            }
            else if (major == 5)
            {
                if (minor >= 1)
                    return PlatformType.WindowsXP;
                if (minor == 0)
                    return PlatformType.Windows2000;
            }
            return PlatformType.Unknown;
        }

        /// <summary>Major .NET version the app runs on (always 4 for .NET Framework 4.x).</summary>
        public static int DetectNetVersion()
        {
            return Environment.Version.Major;
        }

        /// <summary>
        /// e.g. ".NET Framework 4.8". Replaces RuntimeInformation.FrameworkDescription (.NET 4.7.1+).
        /// Every 4.x release reports CLR 4.0.30319, so the installed version comes from the
        /// registry "Release" value Microsoft documents for detecting 4.5 and later.
        /// </summary>
        public static string FrameworkDescription
        {
            get
            {
                string version = "4.0";
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    {
                        object release = key != null ? key.GetValue("Release") : null;
                        if (release is int)
                            version = FrameworkVersionFromRelease((int)release);
                    }
                }
                catch { }
                return ".NET Framework " + version;
            }
        }

        private static string FrameworkVersionFromRelease(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= 528040) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 461308) return "4.7.1";
            if (release >= 460798) return "4.7";
            if (release >= 394802) return "4.6.2";
            if (release >= 394254) return "4.6.1";
            if (release >= 393295) return "4.6";
            if (release >= 379893) return "4.5.2";
            if (release >= 378675) return "4.5.1";
            if (release >= 378389) return "4.5";
            return "4.0";
        }

        private static string GetWineVersion()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                if (ntdll == IntPtr.Zero)
                    return null;

                IntPtr proc = GetProcAddress(ntdll, "wine_get_version");
                if (proc == IntPtr.Zero)
                    return null;

                var del = (WineGetVersionDelegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(WineGetVersionDelegate));
                return del();
            }
            catch
            {
                return null;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate string WineGetVersionDelegate();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
}
