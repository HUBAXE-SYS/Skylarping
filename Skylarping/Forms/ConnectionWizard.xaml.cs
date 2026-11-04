/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Windows;
using System.Windows.Controls;
using Skylarping.AI;
using Skylarping.Forms.Pages;
using Skylarping.Preferences;

namespace Skylarping.Forms
{
    public partial class ConnectionWizard : Page
    {
        private const int StepCount = 4;

        private static bool _isShowing;
        private static bool _suppressForSession;

        private WindowBase _window;
        private int _step;
        private bool _saved;

        private ConnectionWizard()
        {
            InitializeComponent();

            ServerBox.Text = Settings.LlmServerUrl;
            ApiKeyBox.Password = Settings.LlmApiKey;
            ModelBox.Text = Settings.LlmModel;
            ContextBox.Text = Settings.LlmContextWindow.ToString();
            SpeechServerBox.Text = Settings.SttServerUrl;
            SpeechApiKeyBox.Password = Settings.SttApiKey;
            SpeechModelBox.Text = Settings.SttModel;
            SpeechTest.Endpoint = () => SpeechDraft;
            SpeechTest.Language = () => Settings.SttLanguage;
        }

        /// <summary>Shows the wizard after sign-in if no language model is set up. Cancel suppresses it until the next run.</summary>
        public static void ShowIfNeeded(Window owner)
        {
            if (Settings.LlmConfigured || _isShowing || _suppressForSession)
                return;
            if (!ShowWizard(owner))
                _suppressForSession = true;
        }

        /// <summary>Shows the wizard (modal). Returns true if the user finished it and settings were saved.</summary>
        public static bool ShowWizard(Window owner)
        {
            if (_isShowing)
                return false;
            _isShowing = true;
            try
            {
                var page = new ConnectionWizard();
                page._window = new WindowBase(page)
                {
                    Width = 520,
                    Height = 460,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.Manual,
                    Title = Settings.BrandingName + "™ - Connection Wizard",
                    HeaderIcon = WindowBase.IconType.SkypeWifi,
                    HeaderText = "Connect to a language model",
                    ButtonEdgeLeftText = Universal.Lang["sZAPBUTTON_BACK"],
                    ButtonEdgeLeftEnabled = true,
                    ButtonLeftText = "Next",
                    ButtonRightText = Universal.Lang["sF_OPTIONS_BUTTON_CANCEL"],
                    ButtonEdgeLeftAction = page.Back,
                    ButtonLeftAction = page.Next,
                };
                page._window.ButtonRightAction = () => page._window.Close();
                page._window.ButtonEdgeLeft.MinWidth = 73;
                page._window.ButtonLeft.MinWidth = 73;
                page._window.ButtonRight.MinWidth = 73;
                if (owner != null && owner.IsVisible)
                    page._window.Owner = owner;
                page.ShowStep(0);
                page._window.ShowDialog();
                return page._saved;
            }
            finally
            {
                _isShowing = false;
            }
        }

        private LlmEndpoint Draft
        {
            get { return new LlmEndpoint(ServerBox.Text, ApiKeyBox.Password, ModelBox.Text, Settings.LlmReasoningEffort); }
        }

        // Falls back to the language model as typed here: on first run nothing is saved yet.
        private LlmEndpoint SpeechDraft
        {
            get { return LlmEndpoint.ForSpeechToText(SpeechServerBox.Text, SpeechApiKeyBox.Password, SpeechModelBox.Text, Draft); }
        }

        private void ShowStep(int step)
        {
            _step = step;
            StepWelcome.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            StepServer.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            StepTest.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            StepSpeech.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            string[] captions = { "Welcome", "Server and model", "Connection test", "Voice calls (optional)" };
            StepCaption.Text = "Step " + (step + 1) + " of " + StepCount + ": " + captions[step];

            _window.ButtonEdgeLeft.IsEnabled = step > 0;
            _window.ButtonLeftText = step == StepCount - 1 ? "Finish" : "Next";
            _window.ButtonLeft.IsEnabled = true;

            if (step == 1)
                ServerBox.Focus();
            else if (step == 2)
                RunTest();
            else if (step == 3)
                SpeechServerBox.Focus();
        }

        private void Back()
        {
            if (_step > 0 && !TestView.IsRunning && !SpeechTest.IsRunning)
                ShowStep(_step - 1);
        }

        private void Next()
        {
            if (SpeechTest.IsRunning)
                return;
            if (_step == 1)
            {
                string invalid = Draft.ValidateUrl();
                if (invalid != null)
                {
                    Universal.ShowMessage(invalid, "Connection Wizard", WindowBase.IconType.Information);
                    return;
                }
                int context;
                if (!int.TryParse(ContextBox.Text.Trim(), out context) || context < 0)
                {
                    Universal.ShowMessage("The context window must be a whole number of tokens (0 if you don't know it).",
                        "Connection Wizard", WindowBase.IconType.Information);
                    return;
                }
            }
            else if (_step == StepCount - 1)
            {
                Finish();
                return;
            }
            ShowStep(_step + 1);
        }

        private async void RunTest()
        {
            AfterTest.Visibility = Visibility.Collapsed;
            _window.ButtonLeft.IsEnabled = false;
            _window.ButtonEdgeLeft.IsEnabled = false;
            ConnectionTestResult result;
            try
            {
                result = await TestView.Run(Draft);
            }
            finally
            {
                _window.ButtonLeft.IsEnabled = true;
                _window.ButtonEdgeLeft.IsEnabled = true;
            }

            _window.ButtonLeftText = result.Succeeded ? "Next" : "Continue Anyway";
            AfterTestText.Text = result.Succeeded
                ? "All set. To try it out after finishing, send a message to Echo / Sound Test Service in your contacts."
                : "Click Back to change the settings and Test Again, or continue anyway (for example if the server isn't running right now).";
            AfterTest.Visibility = Visibility.Visible;
        }

        private void RetestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TestView.IsRunning)
                RunTest();
        }

        private void Finish()
        {
            if (SpeechServerBox.Text.Trim().Length > 0)
            {
                string invalid = SpeechDraft.ValidateUrl();
                if (invalid != null)
                {
                    Universal.ShowMessage(invalid, "Connection Wizard", WindowBase.IconType.Information);
                    return;
                }
            }

            LlmEndpoint draft = Draft;
            Settings.LlmServerUrl = draft.BaseUrl;
            Settings.LlmApiKey = draft.ApiKey;
            Settings.LlmModel = draft.Model;
            int context;
            Settings.LlmContextWindow = int.TryParse(ContextBox.Text.Trim(), out context) && context > 0 ? context : 0;
            Settings.SttServerUrl = SpeechServerBox.Text.Trim().TrimEnd('/');
            Settings.SttApiKey = SpeechApiKeyBox.Password.Trim();
            Settings.SttModel = SpeechModelBox.Text.Trim();
            Settings.Save();
            _saved = true;
            _window.Close();
        }

        private void ModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string picked = ModelChooser.Pick(_window, Draft);
            if (picked != null)
                ModelBox.Text = picked;
        }

        private void SpeechModelListButton_Click(object sender, RoutedEventArgs e)
        {
            string picked = ModelChooser.Pick(_window, SpeechDraft);
            if (picked != null)
                SpeechModelBox.Text = picked;
        }

        private void Speech_Changed(object sender, RoutedEventArgs e)
        {
            if (SpeechTest != null)
                SpeechTest.Clear();
        }
    }
}
