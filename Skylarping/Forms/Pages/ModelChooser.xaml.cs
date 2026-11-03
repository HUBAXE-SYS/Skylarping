/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Skylarping.AI;
using Skylarping.Preferences;

namespace Skylarping.Forms.Pages
{
    public partial class ModelChooser : Page
    {
        private readonly LlmEndpoint _endpoint;
        private WindowBase _window;
        private string _selected;

        private ModelChooser(LlmEndpoint endpoint)
        {
            _endpoint = endpoint;
            InitializeComponent();
        }

        /// <summary>Shows the model list for <paramref name="endpoint"/>. Returns the chosen id, or null if cancelled.</summary>
        public static string Pick(Window owner, LlmEndpoint endpoint)
        {
            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
            {
                Universal.ShowMessage(invalid, "Model list", WindowBase.IconType.Information);
                return null;
            }

            var page = new ModelChooser(endpoint);
            page._window = new WindowBase(page)
            {
                Width = 380,
                Height = 400,
                MinWidth = 300,
                MinHeight = 300,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Title = Settings.BrandingName + "™ - Choose a model",
                HeaderIcon = WindowBase.IconType.SkypeWifi,
                HeaderText = "Models on " + endpoint.BaseUrl,
                ButtonLeftText = "OK",
                ButtonRightText = Universal.Lang["sF_OPTIONS_BUTTON_CANCEL"],
                ButtonLeftAction = page.Accept,
            };
            page._window.ButtonRightAction = () => page._window.Close();
            page._window.ButtonLeft.MinWidth = 73;
            page._window.ButtonRight.MinWidth = 73;
            page._window.ButtonLeft.IsEnabled = false;
            if (owner != null && owner.IsVisible)
                page._window.Owner = owner;
            page.Loaded += (s, e) => page.LoadModels();
            page._window.ShowDialog();
            return page._selected;
        }

        private async void LoadModels()
        {
            try
            {
                IList<string> models = await TaskEx.Run(() => OpenAICompatClient.ListModels(_endpoint));
                ModelsList.Items.Clear();
                foreach (string id in models)
                    ModelsList.Items.Add(id);

                if (models.Count == 0)
                {
                    StatusText.Text = "The server didn't list any models. You can still type a model name by hand.";
                    return;
                }

                int index = 0;
                for (int i = 0; i < models.Count; i++)
                    if (string.Equals(models[i], _endpoint.Model, StringComparison.OrdinalIgnoreCase))
                        index = i;
                ModelsList.SelectedIndex = index;
                ModelsList.ScrollIntoView(ModelsList.SelectedItem);
                StatusText.Text = models.Count + " model" + (models.Count == 1 ? "" : "s") + " found. Double-click one to use it.";
                ModelsList.Focus();
            }
            catch (LlmException ex)
            {
                StatusText.Text = "Couldn't get the model list. " + ConnectionTest.Explain(ex);
            }
        }

        private void Accept()
        {
            if (ModelsList.SelectedItem == null)
                return;
            _selected = ModelsList.SelectedItem.ToString();
            _window.Close();
        }

        private void ModelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_window != null)
                _window.ButtonLeft.IsEnabled = ModelsList.SelectedItem != null;
        }

        private void ModelsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Accept();
        }
    }
}
