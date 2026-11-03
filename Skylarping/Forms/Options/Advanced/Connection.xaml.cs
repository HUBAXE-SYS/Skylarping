/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Skylarping.AI;
using Skylarping.Preferences;

namespace Skylarping.Forms.OptionPages.Advanced
{
    public partial class Connection : Page
    {
        public Connection()
        {
            InitializeComponent();
            Refresh();
            // Stay current when AI Services or the wizard saves while this page is open.
            Settings.Default.PropertyChanged += OnSettingChanged;
            Unloaded += (s, e) => Settings.Default.PropertyChanged -= OnSettingChanged;
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.LlmServerUrl) || e.PropertyName == nameof(Settings.LlmModel))
                Dispatcher.BeginInvoke(new System.Action(Refresh));
        }

        private void Refresh()
        {
            ServerText.Text = Settings.LlmConfigured ? Settings.LlmServerUrl : "Not set up yet";
            ModelText.Text = Settings.LlmModel.Length > 0 ? Settings.LlmModel : "(server default)";
            TestButton.IsEnabled = Settings.LlmConfigured;
        }

        private void AIServicesButton_Click(object sender, RoutedEventArgs e)
        {
            AIServices.ShowWindow();
        }

        private void WizardButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionWizard.ShowWizard(Window.GetWindow(this));
            Refresh();
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            TestButton.IsEnabled = false;
            try
            {
                await TestView.Run(LlmEndpoint.FromSettings());
            }
            finally
            {
                TestButton.IsEnabled = Settings.LlmConfigured;
            }
        }
    }
}
