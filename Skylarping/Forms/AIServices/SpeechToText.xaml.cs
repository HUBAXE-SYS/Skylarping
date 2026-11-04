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
    public partial class SpeechToText : Page
    {
        private const int MinEndSilence = 200;
        private const int MaxEndSilence = 3000;

        public SpeechToText()
        {
            InitializeComponent();
            ServerBox.Text = Settings.SttServerUrl;
            ApiKeyBox.Password = Settings.SttApiKey;
            ModelBox.Text = Settings.SttModel;
            LanguageBox.Text = Settings.SttLanguage;
            EndSilenceBox.Text = Settings.SttEndSilenceMilliseconds.ToString();
            DetectionBox.SelectedIndex = Settings.SttSpeechDetection;
            TestView.Endpoint = () => Draft;
            TestView.Language = () => LanguageBox.Text;
        }

        /// <summary>The endpoint as currently typed (not yet saved).</summary>
        private LlmEndpoint Draft
        {
            get { return LlmEndpoint.ForSpeechToText(ServerBox.Text, ApiKeyBox.Password, ModelBox.Text, LlmEndpoint.FromSettings()); }
        }

        /// <summary>Null if the fields can be saved, otherwise what's wrong.</summary>
        public string Validate()
        {
            int silence;
            if (!int.TryParse(EndSilenceBox.Text.Trim(), out silence) || silence < MinEndSilence || silence > MaxEndSilence)
                return "The pause that ends your turn must be between " + MinEndSilence + " and " + MaxEndSilence + " milliseconds.";
            if (ServerBox.Text.Trim().Length > 0)
                return Draft.ValidateUrl();
            return null;
        }

        public void Commit()
        {
            Settings.SttServerUrl = ServerBox.Text.Trim().TrimEnd('/');
            Settings.SttApiKey = ApiKeyBox.Password.Trim();
            Settings.SttModel = ModelBox.Text.Trim();
            Settings.SttLanguage = LanguageBox.Text.Trim();
            Settings.SttEndSilenceMilliseconds = int.Parse(EndSilenceBox.Text.Trim());
            Settings.SttSpeechDetection = DetectionBox.SelectedIndex;
        }

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            if (TestView != null)
                TestView.Clear();
        }

        private void ModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string picked = ModelChooser.Pick(Window.GetWindow(this), Draft);
            if (picked != null)
                ModelBox.Text = picked;
        }
    }
}
