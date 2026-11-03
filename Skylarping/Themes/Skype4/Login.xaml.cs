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

using Skylarping.Forms;
using Skylarping.Native.Windows;
using Skylarping.Preferences;
using Skylarping.Sounds;
using Skylarping.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using Skylarping.Backend;
using Skylarping.Models;

namespace Skylarping.Skype4
{
    public partial class Login : Window
    {
        private LoginViewModel _viewModel;
        internal bool noCloseEvent;
        private readonly bool switchuser;
        private string autoLoginName;
        private bool autoLoginHandled; // auto login already ran (or is running) without this window
        private bool signedIn;

        public Login(bool switchuser = false)
        {
            this.switchuser = switchuser;
            InitializeComponent();
            UsernameBox.KeyUp += BoxKeyUp;
            LoginButton.MouseLeftButtonUp += buttonLaunch;
            this.ContentRendered += Login_ContentRendered;

            _viewModel = new LoginViewModel(() => new Main());
            _viewModel.AnimationToggleRequested += LoginToggleAnimation;
            _viewModel.HeaderTextRequested += text => header.Text = text;
            _viewModel.MainWindowReady += OnMainWindowReady;

            SoundManager.Init();
            Tray.SetStatus(PresenceStatus.Offline);
        }

        private async void buttonLaunch(object state, RoutedEventArgs e)
        {
            await SignIn();
        }

        private async System.Threading.Tasks.Task SignIn()
        {
            if (!LoginButton.IsEnabled) return;
            await _viewModel.Login(UsernameBox.Text);
        }

        /// <summary>
        ///  Signs in with the saved name without showing this window. Returns false when
        ///  there is no saved name; if signing in fails, the window is shown with the error.
        /// </summary>
        internal bool TryAutoLogin()
        {
            autoLoginName = _viewModel.GetAutoLoginName(switchuser);
            if (autoLoginName == null)
                return false;
            autoLoginHandled = true;
            _ = AutoLoginHidden(autoLoginName);
            return true;
        }

        private async System.Threading.Tasks.Task AutoLoginHidden(string name)
        {
            await _viewModel.Login(name, RememberAutoLoginName);
            if (!signedIn)
                Show();
        }

#if DEBUG
        private static bool RememberAutoLoginName => !DebugConfig.TestMode;
#else
        private static bool RememberAutoLoginName => true;
#endif

        private void OnMainWindowReady(IMainWindowHolder mainWindow)
        {
            signedIn = true;
            _viewModel.RunPostLogin(mainWindow);
            noCloseEvent = true;
            Close();
        }

        private async void BoxKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            CheckEnableLoginButton();
            if (e.Key == System.Windows.Input.Key.Enter)
                await SignIn();
        }

        private void CheckEnableLoginButton()
        {
            LoginButton.IsEnabled = !string.IsNullOrWhiteSpace(UsernameBox.Text);
        }

        private void OnChangeLanguage(object sender, EventArgs e) { Universal.NotImplemented(Universal.Lang["sLOGIN_CHANGE_LANGUAGE"]); }
        private void OnConnectionOptions(object sender, EventArgs e) { new Options("Advanced_Connection").Show(); }
        private void OnAIServices(object sender, EventArgs e) { AIServices.ShowWindow(); }
        private void OnConnectionWizard(object sender, EventArgs e) { ConnectionWizard.ShowWizard(this); }
        private void OnDisableAutoLogin(object sender, EventArgs e)
        {
            Settings.AutoLogin = false;
            Settings.Save();
            Universal.ShowMessage("You will be asked for your name the next time you start.", "Auto login has been disabled");
        }
        private void OnAccessibility(object sender, EventArgs e) { Universal.NotImplemented(Universal.Lang["sMAINMENU_TOOLS_ACCESSIBILITY"]); }
        private void OnHelp(object sender, EventArgs e) { Universal.OpenUrl(Universal.SKYLARPING_WEBSITE_HELP); }
        private void OnPrivacy(object sender, EventArgs e) { Universal.OpenUrl(Universal.SKYLARPING_WEBSITE_PRIVACY); }
        private void OnAbout(object sender, EventArgs e) { new About().Show(); }
        private void OnClose(object sender, EventArgs e) { Universal.Close(false); }

        private static (string, EventHandler) MI(string label, EventHandler handler) { return (label, handler); }
        private static (string, EventHandler) SEP() { return ("$", null); }

        private void Login_Loaded(object sender, EventArgs e)
        {
            // Back at the login form after signing out or switching user: drop the old session.
            BackendManager.DisposeBackend();

            if (Settings.StartMinimized)
                WindowState = WindowState.Minimized;

            string L(string key) => Universal.Lang[key];

            MenuBarRow.Height = new GridLength(0);
            var menuBar = new NativeMenuBar(this);
            menuBar.Create(
                "&" + L("sMAINMENU_SKYPE"),
                MI(L("sMAINMENU_SKYPE_CLOSE"), OnClose)
            );
            menuBar.Create(
                "&" + L("sMAINMENU_TOOLS"),
                MI(L("sLOGIN_CHANGE_LANGUAGE"), OnChangeLanguage),
                SEP(),
                MI(L("sLOGIN_CONNECTION_OPTIONS"), OnConnectionOptions),
                MI("AI Services...", OnAIServices),
                MI("Connection Wizard...", OnConnectionWizard),
                MI(L("Disable auto login"), OnDisableAutoLogin),
                SEP(),
                MI(L("sMAINMENU_TOOLS_ACCESSIBILITY"), OnAccessibility)
            );
            menuBar.Create(
                "&" + L("sMAINMENU_HELP"),
                MI(L("sMAINMENU_HELP_HELP"), OnHelp),
                SEP(),
                MI(L("sMAINMENU_HELP_PRIVACY"), OnPrivacy),
                MI(L("sMAINMENU_HELP_ABOUT"), OnAbout)
            );

            UsernameBox.Text = Settings.ProfileName;
            CheckEnableLoginButton();

            if (!autoLoginHandled)
                autoLoginName = _viewModel.GetAutoLoginName(switchuser);
            if (!autoLoginHandled && autoLoginName != null)
                LoginToggleAnimation(true);
            else
                UsernameBox.Focus();
        }

        private async void Login_ContentRendered(object sender, EventArgs e)
        {
            if (autoLoginHandled || autoLoginName == null)
                return;
            autoLoginHandled = true;
            await _viewModel.Login(autoLoginName, RememberAutoLoginName);
        }

        private void LoginToggleAnimation(bool anim)
        {
            if (anim)
            {
                LoginControls.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Visible;
                header.Text = Universal.Lang["sSTATUSTEXT_PROFILE_LOGGING_IN"];
            }
            else
            {
                LoginControls.Visibility = Visibility.Visible;
                Spinner.Visibility = Visibility.Collapsed;
                header.Text = Universal.Lang["sF_LOGIN_WELCOME"];
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        private void Login_Closing(object sender, CancelEventArgs ev)
        {
            if (!noCloseEvent)
                Universal.Hide(ev);
        }
    }
}
