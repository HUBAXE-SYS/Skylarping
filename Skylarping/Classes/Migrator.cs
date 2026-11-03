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
using System.IO;
using Skylarping.Preferences;

namespace Skylarping.Migration
{
    class Migrator
    {
        public static void Run()
        {

        }

        /// <summary>
        /// Moves the data folder from before the rebrand (%AppData%\Skymu) to %AppData%\Skylarping,
        /// once. Must run before anything reads Settings or opens a database.
        /// </summary>
        public static bool MoveLegacyDataFolder()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string oldPath = Path.Combine(appData, "Skymu");
                string newPath = Path.Combine(appData, Universal.NAME);
                if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
                {
                    Directory.Move(oldPath, newPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Migrator] Could not move legacy data folder: " + ex.Message);
            }
            return false;
        }
    }
}
