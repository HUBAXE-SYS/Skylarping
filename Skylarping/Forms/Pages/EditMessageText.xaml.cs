/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System.Windows;
using System.Windows.Controls;
using Skylarping.Preferences;

namespace Skylarping.Forms.Pages
{
    public partial class EditMessageText : Page
    {
        private WindowBase _window;
        private string _result;

        private EditMessageText(string text)
        {
            InitializeComponent();
            EditBox.Text = text ?? "";
        }

        /// <summary>Shows the editor; returns the new text, or null if cancelled.</summary>
        public static string Show(string text)
        {
            var page = new EditMessageText(text);
            page._window = new WindowBase(page)
            {
                Width = 520,
                Height = 340,
                MinWidth = 400,
                MinHeight = 260,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Title = Settings.BrandingName + "™ - Edit Message",
                HeaderIcon = WindowBase.IconType.Chat,
                HeaderText = "Edit Message",
                ButtonLeftText = Universal.Lang["sF_OPTIONS_BUTTON_SAVE"],
                ButtonRightText = Universal.Lang["sZAPBUTTON_CANCEL"],
            };
            page._window.ButtonLeftAction = () =>
            {
                page._result = page.EditBox.Text.Replace("\r\n", "\n").Trim();
                page._window.Close();
            };
            page._window.ButtonRightAction = () => page._window.Close();
            page._window.ButtonLeft.IsDefault = false; // Enter makes a new line
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                page._window.Owner = Application.Current.MainWindow;
            page.Loaded += (s, e) =>
            {
                page.EditBox.Focus();
                page.EditBox.CaretIndex = page.EditBox.Text.Length;
            };
            page._window.ShowDialog();
            return string.IsNullOrEmpty(page._result) ? null : page._result;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_window != null)
                _window.ButtonLeft.IsEnabled = EditBox.Text.Trim().Length > 0;
        }
    }
}
