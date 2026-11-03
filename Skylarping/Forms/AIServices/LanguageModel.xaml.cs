/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System.Windows;
using System.Windows.Controls;
using Skylarping.AI;
using Skylarping.Forms.Pages;
using Skylarping.Preferences;

namespace Skylarping.Forms.AIServicePages
{
    public partial class LanguageModel : Page
    {
        public LanguageModel()
        {
            InitializeComponent();
            ServerBox.Text = Settings.LlmServerUrl;
            ApiKeyBox.Password = Settings.LlmApiKey;
            ModelBox.Text = Settings.LlmModel;
            ContextBox.Text = Settings.LlmContextWindow.ToString();
        }

        /// <summary>The endpoint as currently typed (not yet saved).</summary>
        public LlmEndpoint Draft
        {
            get { return new LlmEndpoint(ServerBox.Text, ApiKeyBox.Password, ModelBox.Text); }
        }

        /// <summary>Null if the fields can be saved, otherwise what's wrong.</summary>
        public string Validate()
        {
            int context;
            if (!int.TryParse(ContextBox.Text.Trim(), out context) || context < 0)
                return "The context window must be a whole number of tokens (0 if you don't know it).";
            if (ServerBox.Text.Trim().Length > 0)
                return Draft.ValidateUrl();
            return null;
        }

        public void Commit()
        {
            LlmEndpoint draft = Draft;
            Settings.LlmServerUrl = draft.BaseUrl;
            Settings.LlmApiKey = draft.ApiKey;
            Settings.LlmModel = draft.Model;
            int context;
            Settings.LlmContextWindow = int.TryParse(ContextBox.Text.Trim(), out context) && context > 0 ? context : 0;
        }

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            // Old results describe settings that are no longer in the boxes.
            if (TestView != null && !TestView.IsRunning)
                TestView.Clear();
        }

        private void ModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string picked = ModelChooser.Pick(Window.GetWindow(this), Draft);
            if (picked != null)
                ModelBox.Text = picked;
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            TestButton.IsEnabled = false;
            try
            {
                await TestView.Run(Draft);
            }
            finally
            {
                TestButton.IsEnabled = true;
            }
        }
    }
}
