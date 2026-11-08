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

// Elements only a theme with its own frame has; see MainWindowBase.Parts.cs.
namespace Skylarping
{
    public abstract partial class SkinnedMainWindowBase
    {
        private object Part(string name) => FindName(name);

        private global::Skylarping.SliceControl close => (global::Skylarping.SliceControl)Part(nameof(close));
        private global::System.Windows.Shapes.Rectangle ContentBgBottom => (global::System.Windows.Shapes.Rectangle)Part(nameof(ContentBgBottom));
        private global::System.Windows.Shapes.Rectangle ContentBgTop => (global::System.Windows.Shapes.Rectangle)Part(nameof(ContentBgTop));
        private global::System.Windows.Controls.Grid FrameArea => (global::System.Windows.Controls.Grid)Part(nameof(FrameArea));
        private global::System.Windows.Controls.Menu MainMenuBar => (global::System.Windows.Controls.Menu)Part(nameof(MainMenuBar));
        private global::System.Windows.Shapes.Rectangle MainMenuBarDivider => (global::System.Windows.Shapes.Rectangle)Part(nameof(MainMenuBarDivider));
        private global::Skylarping.SliceControl maximize => (global::Skylarping.SliceControl)Part(nameof(maximize));
        private global::System.Windows.Controls.StackPanel MenuBarArea => (global::System.Windows.Controls.StackPanel)Part(nameof(MenuBarArea));
        private global::System.Windows.Controls.MenuItem MenubarStatusHolder => (global::System.Windows.Controls.MenuItem)Part(nameof(MenubarStatusHolder));
        private global::Skylarping.SliceControl minimize => (global::Skylarping.SliceControl)Part(nameof(minimize));
        private global::System.Windows.Controls.MenuItem RemovePictureMenuItem => (global::System.Windows.Controls.MenuItem)Part(nameof(RemovePictureMenuItem));
        private global::Skylarping.SliceControl split => (global::Skylarping.SliceControl)Part(nameof(split));
        private global::System.Windows.Controls.Grid TitleBar => (global::System.Windows.Controls.Grid)Part(nameof(TitleBar));
        private global::System.Windows.Controls.Image TitleBarIcon => (global::System.Windows.Controls.Image)Part(nameof(TitleBarIcon));
        private global::System.Windows.Controls.TextBlock TitleMain => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleMain));
        private global::System.Windows.Controls.TextBlock TitleShadow => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow));
        private global::System.Windows.Controls.TextBlock TitleShadow2 => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow2));
        private global::System.Windows.Controls.TextBlock TitleShadow3 => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow3));
        private global::System.Windows.Controls.Grid WindowArea => (global::System.Windows.Controls.Grid)Part(nameof(WindowArea));
    }
}
