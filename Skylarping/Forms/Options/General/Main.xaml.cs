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

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Skylarping.Backend;
using Skylarping.Preferences;

namespace Skylarping.Forms.OptionPages.General
{
    public partial class Main : Page
    {
        public Main()
        {
            InitializeComponent();
            AboutMeBox.Text = Settings.ProfileAboutMe;
            Unloaded += (s, e) => SaveAboutMe(); // also when switching pages or closing Options
        }

        private void AboutMeBox_LostFocus(object sender, RoutedEventArgs e) => SaveAboutMe();

        private void SaveAboutMe()
        {
            string text = AboutMeBox.Text.Replace("\r\n", "\n").Trim();
            if (text != Settings.ProfileAboutMe)
                Settings.ProfileAboutMe = text;
        }

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = (ComboBox)sender;

            // Prevent accidental triggers on window loading
            if (comboBox == null || !comboBox.IsLoaded)
                return;

            Dialog dialog = new Dialog(
                                    type: WindowBase.IconType.Question,
                                    content: "Changing this option requires restarting Skylarping to apply properly.",
                                    header: "Would you like to restart Skylarping?",
                                    brText: "No",
                                    blEnabled: true,
                                    blText: "Yes"
                                );
            dialog.BRAction = () =>
            {
                dialog.Close();
            };
            dialog.BLAction = () =>
            {
                dialog.Close();
                Process.Start(Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            };
            dialog.ShowDialog();
        }
    }
}