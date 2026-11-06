/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// New Group Conversation / Edit Group.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Skylarping.Characters;
using Skylarping.Models;
using Skylarping.Preferences;

namespace Skylarping.Forms.Pages
{
    public partial class EditGroup : Page
    {
        private const int MinMembers = 2;

        private WindowBase window;
        private readonly StoredGroup _group; // the group being edited; null when creating one
        private bool _saving;

        public EditGroup() : this(null)
        {
        }

        public EditGroup(StoredGroup group)
        {
            InitializeComponent();
            _group = group;
            StoredGroup shown = group ?? new StoredGroup();
            NameBox.Text = shown.Name;
            UnpromptedBox.IsChecked = shown.Unprompted;
            QuietMinBox.Text = shown.QuietMinMinutes.ToString();
            QuietMaxBox.Text = shown.QuietMaxMinutes.ToString();
            RepeatsBox.Text = shown.Repeats.ToString();
            DelayMinBox.Text = shown.DelayMinSeconds.ToString();
            DelayMaxBox.Text = shown.DelayMaxSeconds.ToString();
            ChainLimitBox.Text = shown.ChainLimit.ToString();
            CallPauseMinBox.Text = shown.CallPauseMinSeconds.ToString();
            CallPauseMaxBox.Text = shown.CallPauseMaxSeconds.ToString();
            CallQuietMinBox.Text = shown.CallQuietMinSeconds.ToString();
            CallQuietMaxBox.Text = shown.CallQuietMaxSeconds.ToString();
            BuildMemberList(shown.MemberIds);
        }

        private bool Editing => _group != null;

        private void BuildMemberList(IList<string> members)
        {
            var viewModel = Universal.ActiveViewModel;
            foreach (DirectMessage contact in viewModel.ContactList.Where(viewModel.IsCharacter))
            {
                var box = new CheckBox
                {
                    Content = contact.DisplayName,
                    Tag = contact.Identifier,
                    IsChecked = members.Contains(contact.Identifier),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                box.Checked += (s, e) => UpdateButton();
                box.Unchecked += (s, e) => UpdateButton();
                MemberList.Children.Add(box);
            }
        }

        private List<string> SelectedMembers()
        {
            return MemberList.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        }

        public void ShowWindow()
        {
            window = new WindowBase(this)
            {
                MinWidth = 460,
                MinHeight = 420,
                Width = 500,
                Height = 640,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Title = Settings.BrandingName + "™ - " + (Editing ? "Edit Group" : "New Group Conversation"),
                HeaderIcon = WindowBase.IconType.GroupChat,
                HeaderText = Editing ? "Edit " + _group.Name : "New Group Conversation",
                ButtonLeftText = Editing ? Universal.Lang["sF_OPTIONS_BUTTON_SAVE"] : "Create",
                ButtonRightText = Universal.Lang["sZAPBUTTON_CANCEL"],
                ButtonLeftAction = Save,
                ButtonRightAction = () => window.Close()
            };
            window.ButtonLeft.MinWidth = 100;
            window.ButtonRight.MinWidth = 98;
            UpdateButton();
            window.Show();
            NameBox.Focus();
        }

        private void Field_Changed(object sender, TextChangedEventArgs e) => UpdateButton();

        private void UnpromptedBox_Changed(object sender, RoutedEventArgs e)
        {
            UnpromptedPanel.Visibility = UnpromptedBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButton()
        {
            if (window != null)
                window.ButtonLeft.IsEnabled = !_saving && NameBox.Text.Trim().Length > 0 && SelectedMembers().Count >= MinMembers;
        }

        private static int Number(TextBox box, int fallback, int least)
        {
            int value;
            return int.TryParse(box.Text.Trim(), out value) && value >= least ? value : fallback;
        }

        private StoredGroup BuildGroup()
        {
            StoredGroup group = _group?.Copy() ?? new StoredGroup();
            group.Name = NameBox.Text.Trim();
            group.MemberIds = SelectedMembers();
            group.Unprompted = UnpromptedBox.IsChecked == true;

            int quietMin = Number(QuietMinBox, StoredGroup.DefaultQuietMinMinutes, 1);
            int quietMax = Number(QuietMaxBox, StoredGroup.DefaultQuietMaxMinutes, 1);
            group.QuietMinMinutes = Math.Min(quietMin, quietMax);
            group.QuietMaxMinutes = Math.Max(quietMin, quietMax);
            group.Repeats = Number(RepeatsBox, StoredGroup.DefaultRepeats, 0);

            int delayMin = Number(DelayMinBox, StoredGroup.DefaultDelayMinSeconds, 0);
            int delayMax = Number(DelayMaxBox, StoredGroup.DefaultDelayMaxSeconds, 0);
            group.DelayMinSeconds = Math.Min(delayMin, delayMax);
            group.DelayMaxSeconds = Math.Max(delayMin, delayMax);
            group.ChainLimit = Number(ChainLimitBox, 0, 0);

            int pauseMin = Number(CallPauseMinBox, StoredGroup.DefaultCallPauseMinSeconds, 0);
            int pauseMax = Number(CallPauseMaxBox, StoredGroup.DefaultCallPauseMaxSeconds, 0);
            group.CallPauseMinSeconds = Math.Min(pauseMin, pauseMax);
            group.CallPauseMaxSeconds = Math.Max(pauseMin, pauseMax);

            int callQuietMin = Number(CallQuietMinBox, StoredGroup.DefaultCallQuietMinSeconds, 1);
            int callQuietMax = Number(CallQuietMaxBox, StoredGroup.DefaultCallQuietMaxSeconds, 1);
            group.CallQuietMinSeconds = Math.Min(callQuietMin, callQuietMax);
            group.CallQuietMaxSeconds = Math.Max(callQuietMin, callQuietMax);
            return group;
        }

        private async void Save()
        {
            if (_saving)
                return;
            _saving = true;
            UpdateButton();
            try
            {
                StoredGroup group = BuildGroup();
                if (Editing)
                    Universal.ActiveViewModel.GroupUpdated(await Universal.Backend.UpdateGroup(group));
                else
                    Universal.ActiveViewModel.GroupCreated(await Universal.Backend.CreateGroup(group));
                window.Close();
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                _saving = false;
                UpdateButton();
                Universal.ShowMessage("The group couldn't be saved: " + ex.Message, "Group Conversation", WindowBase.IconType.Error);
            }
        }
    }
}
