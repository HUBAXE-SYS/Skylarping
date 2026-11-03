/*==========================================================*/
// Copyright � The Skymu Team and other contributors.
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

using Skymu.Mvvm;
using Skymu.Forms;
using Skymu.Helpers;
using Skymu.Native.Windows;
using Skymu.Preferences;
using Skymu.Sounds;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Skymu.Backend;
using Skymu.Models;

namespace Skymu.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly Func<IMainWindowHolder> _createMainWindow;

        public event Action<bool> AnimationToggleRequested;
        public event Action<string> HeaderTextRequested;
        public event Action<IMainWindowHolder> MainWindowReady;

        public LoginViewModel(Func<IMainWindowHolder> createMainWindow)
        {
            _createMainWindow = createMainWindow;
        }

        /// <summary>
        ///  The display name to sign in with automatically, or null to show the login form.
        /// </summary>
        public string GetAutoLoginName(bool switchUser)
        {
#if DEBUG
            if (DebugConfig.TestMode)
                return "Saul Goodman";
            if (DebugConfig.DisableAutoLogin)
                return null;
#endif
            if (switchUser || !Settings.AutoLogin || string.IsNullOrWhiteSpace(Settings.ProfileName))
                return null;
            return Settings.ProfileName;
        }

        /// <summary>
        ///  Signs in with the given display name and opens the main window.
        ///  When remember is true the name is stored for the next start.
        /// </summary>
        public async Task Login(string displayName, bool remember = true)
        {
            displayName = displayName?.Trim();
            if (string.IsNullOrEmpty(displayName))
                return;

            AnimationToggleRequested?.Invoke(true);
            Tray.SetConnecting();

            IChatBackend backend = BackendManager.CreateBackend();
            bool signedIn;
            try
            {
                signedIn = await backend.SignIn(displayName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                signedIn = false;
            }

            if (!signedIn)
            {
                BackendManager.DisposeBackend();
                Tray.SetStatus(PresenceStatus.Offline);
                AnimationToggleRequested?.Invoke(false);
                HeaderTextRequested?.Invoke(Universal.Lang["sF_USERENTRY_ERROR_1101"]);
                return;
            }

            Debug.WriteLine("[SKYMU] Login success. Initiating main window...");
            if (remember)
            {
                Settings.ProfileName = displayName;
                Settings.Save();
            }

            HeaderTextRequested?.Invoke("Loading user data");
            IMainWindowHolder mainWindow = _createMainWindow();
            mainWindow.Ready += (s, e) => MainWindowReady?.Invoke(mainWindow);

            try
            {
                await mainWindow.BeginLoading();
            }
            catch (Exception ex)
            {
                BackendManager.DisposeBackend();
                AnimationToggleRequested?.Invoke(false);
                HeaderTextRequested?.Invoke(Universal.Lang["sF_USERENTRY_ERROR_1101"]);
                Universal.ExceptionHandler(ex);
            }
        }

        public void RunPostLogin(IMainWindowHolder mainWindow)
        {
            Tray.SetStatus(Universal.CurrentUser.ConnectionStatus);
            Universal.SignedIn = true;
            mainWindow.Show();
            SoundManager.Play("LOGIN");
            string brand = Settings.BrandingName;

            // something must have went wrong with Runtime.DetectOS() at app startup
            // the code block below depends on knowing the OS, so don't run it
            if (!Enum.TryParse(Universal.Platform, true, out PlatformType platform)) return;

            // warn the user once if they're running an old OS like XP / old Wine version
            if (!Settings.OldPlatformWarningShown)
            {
                string message = null;

                if (platform == PlatformType.Unknown)
                    message = brand + " could not determine your operating system. If you are using an unsupported platform, you may encounter bugs.";

                else if (platform < PlatformType.Windows2000)
                {
                    if (platform == PlatformType.WineLegacy)
                        message = brand + " does not support Wine versions older than 10. You may encounter significant bugs.";
                    else if (platform == PlatformType.Wine10)
                        message = brand + " has limited support for Wine 10. Some features may not work as expected.";
                    else if (platform == PlatformType.Wine11)
                        message = brand + " does not have complete support for Wine 11. Some features may not work as expected.";
                }

                // Windows XP is the target platform, so it gets no warning.
                else if (platform == PlatformType.Windows2000)
                    message = brand + " does not officially support Windows 2000 or any extended kernels, and you may encounter significant bugs.";

                // Windows is well known for its backwards compatibility, this should never be an issue.
                // else if (platform > PlatformType.Windows11) message = brand + " has not yet been tested on your version of Windows. You may encounter bugs.";

                if (message != null)
                    Universal.ShowMessage(message, "Compatibility warning");
            }
        }
    }
}
