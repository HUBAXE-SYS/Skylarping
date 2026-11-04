/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tools > AI Services. Unlike Options, server settings are edited as a draft and only
// written on Save, so a half-typed URL never breaks the running app; Cancel also puts
// back the chat display settings (that page writes through as you click, as in Options).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Skylarping.Enumerations;
using Skylarping.Forms.AIServicePages;
using Skylarping.Forms.OptionPages.Chats;
using Skylarping.Preferences;

namespace Skylarping.Forms
{
    public partial class AIServices : Window
    {
        private static AIServices _open;

        private readonly Dictionary<SliceControl, Grid> catToGrid;
        private readonly Dictionary<SliceControl, Func<Page>> tabDispenser;
        private readonly Dictionary<SliceControl, string[]> tabToText;
        private readonly Dictionary<SliceControl, Page> pages = new Dictionary<SliceControl, Page>();

        private readonly bool _markdownAtOpen = Settings.MarkdownParsing;
        private bool _saved;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        public static void ShowWindow()
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }
            _open = new AIServices();
            _open.Show();
        }

        private AIServices()
        {
            InitializeComponent();

            Background = (SolidColorBrush)Application.Current.Resources["Background"];
            Title = Settings.BrandingName + "™ - AI Services";

            catToGrid = new Dictionary<SliceControl, Grid>
            {
                { HLanguageModel, GLanguageModel },
                { HTools, GTools },
            };
            tabDispenser = new Dictionary<SliceControl, Func<Page>>
            {
                { LM_Server, () => new LanguageModel() },
                { LM_Display, () => new ChatDisplay() },
                { T_Settings, () => new ToolSettings(false) },
                { T_Timeouts, () => new ToolSettings(true) },
            };
            tabToText = new Dictionary<SliceControl, string[]>
            {
                { LM_Server, new[] { "Language model:", "The OpenAI-compatible server your characters chat through" } },
                { LM_Display, new[] { "Chat display:", "How the model's replies look in chat" } },
                { T_Settings, new[] { "Tool settings:", "Options for the SimpleLLMChat-compatible tools" } },
                { T_Timeouts, new[] { "Timeouts:", "How long each tool may run" } },
            };

            SourceInitialized += (s, e) =>
            {
                foreach (var cat in catToGrid)
                {
                    cat.Key.ApplyTemplate();
                    foreach (SliceControl tab in (cat.Value.Children[1] as StackPanel).Children)
                        tab.ApplyTemplate();
                }
                TabSelect(LM_Server, null);
            };

            Closed += (s, e) =>
            {
                if (!_saved)
                    Settings.MarkdownParsing = _markdownAtOpen;
                _open = null;
            };
        }

        private Page Get(SliceControl tab)
        {
            Page page;
            if (!pages.TryGetValue(tab, out page))
            {
                page = tabDispenser[tab]();
                pages[tab] = page;
            }
            return page;
        }

        private void CatSelect(object sender, MouseButtonEventArgs e)
        {
            var sc = (SliceControl)sender;
            foreach (var cat in catToGrid)
            {
                if (ReferenceEquals(cat.Key, sc))
                    continue;
                cat.Key.SetState(ButtonVisualState.Default);
                cat.Key.DefaultIndex = 0;
                cat.Key.HoverIndex = 1;
                cat.Key.PressedIndex = 2;
                cat.Value.Visibility = Visibility.Collapsed;
            }
            sc.SetState(ButtonVisualState.Pressed);
            catToGrid[sc].Visibility = Visibility.Visible;
            sc.DefaultIndex = 3;
            sc.HoverIndex = -1;
            sc.PressedIndex = -1;

            TabSelect((catToGrid[sc].Children[1] as StackPanel).Children[0], null);
        }

        private void TabSelect(object sender, MouseButtonEventArgs e)
        {
            var sc = (SliceControl)sender;
            // The inner slice may be the one clicked; the tab is its templated parent.
            if (((SliceControl)sc.Template.FindName("InnerSlice", sc)) == null)
                sc = sc.Tag as SliceControl;
            foreach (SliceControl tab in (sc.Parent as StackPanel).Children)
            {
                if (ReferenceEquals(tab, sc))
                    continue;
                tab.SetState(ButtonVisualState.Default);
                ((SliceControl)tab.Template.FindName("InnerSlice", tab)).SetState(ButtonVisualState.Default);
            }
            sc.SetState(ButtonVisualState.Pressed);
            ((SliceControl)sc.Template.FindName("InnerSlice", sc)).SetState(ButtonVisualState.Pressed);

            string[] text;
            if (tabToText.TryGetValue(sc, out text))
            {
                CTabTitle.Text = text[0];
                CTabDescription.Text = text[1];
            }
            // Pages are kept (not recreated) so a draft survives switching tabs.
            PageHost.Navigate(Get(sc));
        }

        private void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            Page page;
            if (pages.TryGetValue(LM_Server, out page))
            {
                var lm = (LanguageModel)page;
                string problem = lm.Validate();
                if (problem != null)
                {
                    TabSelect(LM_Server, null);
                    Universal.ShowMessage(problem, "Can't save AI Services", WindowBase.IconType.Information);
                    return;
                }
                lm.Commit();
            }
            foreach (SliceControl tab in new[] { T_Settings, T_Timeouts })
            {
                if (!pages.TryGetValue(tab, out page))
                    continue;
                string problem = ((ToolSettings)page).Validate();
                if (problem != null)
                {
                    TabSelect(tab, null);
                    Universal.ShowMessage(problem, "Can't save AI Services", WindowBase.IconType.Information);
                    return;
                }
            }
            foreach (SliceControl tab in new[] { T_Settings, T_Timeouts })
                if (pages.TryGetValue(tab, out page))
                    ((ToolSettings)page).Commit();
            Settings.Save();
            _saved = true;
            Close();
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void WizardButtonClick(object sender, RoutedEventArgs e)
        {
            if (!ConnectionWizard.ShowWizard(this))
                return;
            // The wizard saved new settings: show them instead of the stale draft.
            pages.Remove(LM_Server);
            TabSelect(LM_Server, null);
        }
    }
}
