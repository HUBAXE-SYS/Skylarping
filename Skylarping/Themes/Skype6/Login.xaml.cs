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

using Skylarping.ViewModels;
using System.Windows;

namespace Skylarping.Skype6
{
    public partial class Login : LoginWindowBase
    {
        public Login(bool switchuser = false) : base(switchuser)
        {
            InitializeComponent();
            InitializeLoginWindow();
            LoginButton.IsEnabledChanged += (s, e) => UpdateLoginButtonOpacity();
            UpdateLoginButtonOpacity();
        }

        private void UpdateLoginButtonOpacity() => LoginButton.Opacity = LoginButton.IsEnabled ? 1 : 0.4;

        protected override IMainWindowHolder CreateMainWindow() => new Main();
    }
}
