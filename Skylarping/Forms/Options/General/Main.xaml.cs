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
using System.Windows.Input;
using Skylarping.Backend;
using Skylarping.Helpers;
using Skylarping.Preferences;

namespace Skylarping.Forms.OptionPages.General
{
    public partial class Main : Page
    {
        public Main()
        {
            InitializeComponent();
            AboutMeBox.Text = Settings.ProfileAboutMe;
            AutoAwayMinutesBox.Text = Settings.AutoAwayMinutes.ToString();
            Unloaded += (s, e) => { SaveAboutMe(); SaveAutoAwayMinutes(); }; // also when switching pages or closing Options
            UpdatePicture();
        }

        private void UpdatePicture()
        {
            byte[] avatar = Universal.CurrentUser?.Avatar;
            PicturePreview.Source = avatar != null ? ImageHelper.GenerateFromArray(avatar) : Universal.AnonymousAvatar;
            RemovePictureButton.IsEnabled = avatar != null;
        }

        private async void ChangePicture_Click(object sender, RoutedEventArgs e)
        {
            if (await ProfilePicture.Change(Window.GetWindow(this)))
                UpdatePicture();
        }

        private async void RemovePicture_Click(object sender, RoutedEventArgs e)
        {
            if (await ProfilePicture.Remove())
                UpdatePicture();
        }

        private void AboutMeBox_LostFocus(object sender, RoutedEventArgs e) => SaveAboutMe();

        private void AutoAwayMinutesBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void AutoAwayMinutesBox_LostFocus(object sender, RoutedEventArgs e) => SaveAutoAwayMinutes();

        // 1-999 minutes; anything else (empty, 0, pasted junk) puts the saved value back.
        private void SaveAutoAwayMinutes()
        {
            int minutes;
            if (int.TryParse(AutoAwayMinutesBox.Text.Trim(), out minutes) && minutes >= 1 && minutes <= 999)
            {
                if (minutes != Settings.AutoAwayMinutes)
                    Settings.AutoAwayMinutes = minutes;
            }
            AutoAwayMinutesBox.Text = Settings.AutoAwayMinutes.ToString();
        }

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