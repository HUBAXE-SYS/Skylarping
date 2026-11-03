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
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace Skylarping.Native.Windows
{
    internal class AutoLaunch
    {
        private const string KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        ///  Same arguments classic Skype put in its Run entry. /minimized starts the app in the
        ///  system tray; /nosplash is accepted for parity (there is no splash screen).
        /// </summary>
        private const string RunArgs = "/nosplash /minimized";

        /// <summary> True when started with /minimized (as Windows does at sign-in): stay in the tray. </summary>
        internal static readonly bool StartInTray = Environment
            .GetCommandLineArgs()
            .Skip(1)
            .Any(a => string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase));

        private static string Command =>
            "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" " + RunArgs;

        private static string ReadEntry()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KEY, false))
                return key?.GetValue(Universal.NAME) as string;
        }

        internal static bool Get()
        {
            try
            {
                return !string.IsNullOrEmpty(ReadEntry());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AutoLaunch] Could not read the Run entry: " + ex);
                return false;
            }
        }

        internal static void Set(bool yes)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KEY))
                {
                    if (yes)
                        key.SetValue(Universal.NAME, Command);
                    else
                        key.DeleteValue(Universal.NAME, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AutoLaunch] Could not write the Run entry: " + ex);
            }
        }

        /// <summary>
        ///  Points an existing Run entry at this exe, for entries from older builds
        ///  (no startup argument) or after the app was moved.
        /// </summary>
        internal static void Refresh()
        {
            try
            {
                string entry = ReadEntry();
                if (!string.IsNullOrEmpty(entry) && !string.Equals(entry, Command, StringComparison.OrdinalIgnoreCase))
                    Set(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AutoLaunch] Could not refresh the Run entry: " + ex);
            }
        }
    }
}
