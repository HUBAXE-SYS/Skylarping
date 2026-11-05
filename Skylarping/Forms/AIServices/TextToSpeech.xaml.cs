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
    public partial class TextToSpeech : Page
    {
        private const int MinSpeed = 50;
        private const int MaxSpeed = 200;

        public TextToSpeech()
        {
            InitializeComponent();
            ServerBox.Text = Settings.TtsServerUrl;
            ApiKeyBox.Password = Settings.TtsApiKey;
            ModelBox.Text = Settings.TtsModel;
            SpeedBox.Text = Settings.TtsSpeedPercent.ToString();
            InterruptionBox.IsChecked = Settings.TtsInterruption;
            TestView.Endpoint = () => Draft;
            TestView.SpeedPercent = () => Speed;
        }

        /// <summary>The endpoint as currently typed (not yet saved).</summary>
        private LlmEndpoint Draft
        {
            get { return LlmEndpoint.ForTextToSpeech(ServerBox.Text, ApiKeyBox.Password, ModelBox.Text, LlmEndpoint.FromSettings()); }
        }

        private int Speed
        {
            get
            {
                int speed;
                return int.TryParse(SpeedBox.Text.Trim(), out speed) && speed >= MinSpeed && speed <= MaxSpeed ? speed : 100;
            }
        }

        /// <summary>Null if the fields can be saved, otherwise what's wrong.</summary>
        public string Validate()
        {
            int speed;
            if (!int.TryParse(SpeedBox.Text.Trim(), out speed) || speed < MinSpeed || speed > MaxSpeed)
                return "The speed must be between " + MinSpeed + " and " + MaxSpeed + " percent.";
            if (ServerBox.Text.Trim().Length > 0)
                return Draft.ValidateUrl();
            return null;
        }

        public void Commit()
        {
            Settings.TtsServerUrl = ServerBox.Text.Trim().TrimEnd('/');
            Settings.TtsApiKey = ApiKeyBox.Password.Trim();
            Settings.TtsModel = ModelBox.Text.Trim();
            Settings.TtsSpeedPercent = Speed;
            Settings.TtsInterruption = InterruptionBox.IsChecked == true;
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
