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

// Elements named in a theme's XAML; see MainWindowBase.Parts.cs.
namespace Skylarping
{
    public abstract partial class LoginWindowBase
    {
        private object Part(string name) => FindName(name);

        private global::System.Windows.Controls.TextBlock header => (global::System.Windows.Controls.TextBlock)Part(nameof(header));
        private global::Skylarping.SliceControl LoginButton => (global::Skylarping.SliceControl)Part(nameof(LoginButton));
        private global::System.Windows.Controls.Grid LoginControls => (global::System.Windows.Controls.Grid)Part(nameof(LoginControls));
        private global::System.Windows.Controls.RowDefinition MenuBarRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(MenuBarRow));
        private global::System.Windows.Controls.Image Spinner => (global::System.Windows.Controls.Image)Part(nameof(Spinner));
        private global::System.Windows.Controls.TextBox UsernameBox => (global::System.Windows.Controls.TextBox)Part(nameof(UsernameBox));
    }
}
