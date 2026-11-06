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

using Skylarping.Helpers;
using Skylarping.Sounds;
using Skylarping.Preferences;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Skylarping.Backend;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Skylarping.Models;

namespace Skylarping.Forms
{
    public partial class CallScreen : Page
    {
        private BitmapImage pill,
            rectangle,
            logo_small,
            logo_big,
            unmuted,
            muted,
            chat_active,
            chat_inactive,
            sidebar_expand,
            sidebar_collapse,
            screen_contract,
            screen_expand;
        private bool isPillMode;
        private bool isLogoBig;
        private bool isMuted;
        private bool isFullscreen = false;
        private bool _is_answer;
        private bool _hangUpRequested = false;
        private ActiveCall _call;
        private readonly IChatBackend _backend = Universal.Backend;
        private LocationChangeEventArgs location;
        private const int MemberPictureSize = 96;

        private DispatcherTimer _callTimer;
        private TimeSpan _callElapsed;
        private Frame _originalFrame;
        private Conversation _conversation;

        public CallScreen(
            Conversation conversation,
            CallScreen.LocationChangeEventArgs initial_location,
            bool is_answering_call = false
        )
        {
            InitializeComponent();
            _conversation = conversation;
            byte[] partner_avatar;

            if (_conversation is DirectMessage dm)
            {
                partner_avatar = dm.Partner.Avatar;
                PartnerDisplayName.Text = dm.Partner.DisplayName;
            }
            else if (_conversation is Group group)
            {
                partner_avatar = group.Avatar;
                PartnerDisplayName.Text = string.Format("{0} ({1} members)", group.DisplayName, group.Members.Length);
                ShowMembers(group);
            }
            else
            {
                return;
            }

            if (Universal.CurrentUser?.Avatar != null)
                MyAvatar.Source = ImageHelper.GenerateFromArray(
                    Universal.CurrentUser.Avatar
                );
            else
                MyAvatar.Source = Universal.AnonymousAvatar;
            if (partner_avatar != null)
                PartnerAvatar.Source = ImageHelper.GenerateFromArray(partner_avatar);
            else
                PartnerAvatar.Source = Universal.AnonymousAvatar;

            _is_answer = is_answering_call;
            if (_is_answer)
                CallStatus.Text = Universal.Lang["sF_OPTIONS_SOUNDS_CONNECTING"];

            rectangle = Load("Call Screen/rectangle.png");
            pill = Load("Call Screen/pill.png");
            logo_small = Load("Branding/logo-call-small.png");
            logo_big = Load("Branding/logo-call-big.png");
            unmuted = Load("Call Screen/btn_mic.png");
            muted = Load("Call Screen/btn_mic_off.png");
            chat_active = Load("Call Screen/btn_chat_active.png");
            chat_inactive = Load("Call Screen/btn_chat_inactive.png");
            sidebar_expand = Load("Call Screen/btn_sidebar_expand.png");
            sidebar_collapse = Load("Call Screen/btn_sidebar_collapse.png");
            screen_contract = Load("Call Screen/btn_screen_contract.png");
            screen_expand = Load("Call Screen/btn_screen_expand.png");


            isPillMode = !(this.ActualWidth >= 1025.0);
            isLogoBig = !(this.ActualWidth >= 700 && this.ActualHeight >= 700);
            Resized(null, null);
            location = initial_location;

            if (Settings.RoomCallUI)
            {
                bottom_gradient.Opacity = 0.6;
                floor.Height = new GridLength(110);
            }

            SetButtonSource(SidebarButton, location.SidebarToggle);
            SetButtonSource(ChatButton, location.ChatToggle);
            SetButtonSource(FullscreenButton, isFullscreen);
        }

        private CancellationTokenSource _ringCts;

        public async Task StartCall()
        {
            _backend.CallStateChanged += OnCallStateChanged;
            _call = new ActiveCall(
                "INIT",
                _conversation.Identifier,
                new User[] { Universal.CurrentUser }
            );

            _ringCts = new CancellationTokenSource();
            var token = _ringCts.Token;

            _ = TaskEx.Run(async () =>
            {
                await SoundManager.PlayAsync("CALL_INIT", token);
                if (_is_answer)
                    return;
                while (!token.IsCancellationRequested)
                {
                    await SoundManager.PlayAsync(Settings.CallOutToReconnectSound ? "CALL_RECONNECT_FRONT" : "CALL_OUT", token);
                }
            });

            ActiveCall call = await _backend.StartCall(
                _conversation.Identifier,
                isMuted
            );

            if (_hangUpRequested)
                return; // in case user has already hung up before the call is established

            _ringCts.Cancel();
            SoundManager.StopPlayback("CALL_OUT");
            SoundManager.StopPlayback("CALL_INIT");

            if (call == null)
            {
                SoundManager.Play("CALL_ERROR1");
                HangUpRequested(this, EventArgs.Empty);
            }
            else
            {
                SwitchToOngoingCallUI();
                _call = call;
            }
        }

        private static readonly Brush SpeakingBrush = FrozenBrush(Color.FromRgb(0x8C, 0xC6, 0x3F));
        private readonly Dictionary<string, Border> _memberFrames = new Dictionary<string, Border>();

        private static Brush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void ShowMembers(Group group)
        {
            PartnerAvatar.Visibility = Visibility.Collapsed;
            MemberPanel.Visibility = Visibility.Visible;
            foreach (User member in group.Members)
            {
                var frame = new Border
                {
                    Width = MemberPictureSize,
                    Height = MemberPictureSize,
                    BorderThickness = new Thickness(3),
                    BorderBrush = Brushes.Transparent,
                    Child = new Image
                    {
                        Stretch = Stretch.UniformToFill,
                        Source = member.Avatar != null
                            ? (ImageSource)ImageHelper.GenerateFromArray(member.Avatar)
                            : Universal.AnonymousAvatar
                    }
                };
                RenderOptions.SetBitmapScalingMode(frame.Child, BitmapScalingMode.HighQuality);
                var card = new StackPanel { Margin = new Thickness(6, 0, 6, 0), Width = MemberPictureSize + 12 };
                card.Children.Add(frame);
                card.Children.Add(new TextBlock
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    Text = member.DisplayName,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                MemberPanel.Children.Add(card);
                _memberFrames[member.Identifier] = frame;
            }
        }

        /// <summary>Outlines whoever can be heard on a group call; null for nobody.</summary>
        public void ShowSpeaking(string memberId)
        {
            foreach (KeyValuePair<string, Border> member in _memberFrames)
                member.Value.BorderBrush = member.Key == memberId ? SpeakingBrush : Brushes.Transparent;
        }

        private static BitmapImage Load(string name)
        {
            return ImageHelper.FreezeLoad("Universal/" + name);
        }

        /// <summary>How long a call has lasted: 04:09, or 1:04:09 from an hour on.</summary>
        public static string FormatCallTime(TimeSpan elapsed)
        {
            string minutes = elapsed.ToString(@"mm\:ss");
            return elapsed.TotalHours >= 1 ? (int)elapsed.TotalHours + ":" + minutes : minutes;
        }

        public void SwitchToOngoingCallUI()
        {
            MyAvatar.Visibility = Visibility.Collapsed;
            MyAvatar = null;
            ConnectionAnimation.Visibility = Visibility.Collapsed;
            ConnectionAnimation = null;

            _callElapsed = TimeSpan.Zero;
            CallStatus.Text = FormatCallTime(_callElapsed);

            _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _callTimer.Tick += (s, e) =>
            {
                _callElapsed = _callElapsed.Add(TimeSpan.FromSeconds(1));
                CallStatus.Text = FormatCallTime(_callElapsed);
            };
            _callTimer.Start();
        }

        #region Events / event handlers

        public event EventHandler HangUpRequested;
        public event EventHandler<LocationChangeEventArgs> LocationChangeRequested;

        public class LocationChangeEventArgs : EventArgs
        {
            public bool SidebarToggle;
            public bool ChatToggle;

            public LocationChangeEventArgs(bool sidebar, bool chat)
            {
                SidebarToggle = sidebar;
                ChatToggle = chat;
            }
        }

        private void OnCallStateChanged(object sender, CallEventArgs e)
        {
            if (e.State == CallState.Ended)
            {
                Dispatcher.Invoke(() =>
                {
                    OnHangUp(null, null);
                });
            }
        }

        private async void OnMuteToggled(object sender, MouseButtonEventArgs e)
        {
            isMuted = !isMuted;
            if (isMuted)
                MuteButton.Source = muted;
            else
                MuteButton.Source = unmuted;
            await _backend.SetMuted(_call, isMuted);
        }

        private void OnSidebarToggled(object sender, MouseButtonEventArgs e)
        {
            location.SidebarToggle = !location.SidebarToggle;
            SetButtonSource(SidebarButton, location.SidebarToggle);
            if (isFullscreen)
                ExitFullscreen();
            if (LocationChangeRequested != null)
                LocationChangeRequested(this, location);
        }

        private void OnChatToggled(object sender, MouseButtonEventArgs e)
        {
            location.ChatToggle = !location.ChatToggle;
            SetButtonSource(ChatButton, location.ChatToggle);
            if (isFullscreen)
                ExitFullscreen();
            if (LocationChangeRequested != null)
                LocationChangeRequested(this, location);
        }

        private Window _fullscreenWindow;

        private void OnFullscreenToggled(object sender, MouseButtonEventArgs e)
        {
            isFullscreen = !isFullscreen;
            SetButtonSource(FullscreenButton, isFullscreen);

            if (isFullscreen)
                EnterFullscreen();
            else
                ExitFullscreen();
        }

        private void EnterFullscreen()
        {
            _originalFrame = GetFrameParent();

            if (_originalFrame != null)
            {
                while (_originalFrame.NavigationService.CanGoBack)
                    _originalFrame.NavigationService.RemoveBackEntry();
                _originalFrame.Content = null;
            }

            _fullscreenWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                ResizeMode = ResizeMode.NoResize,
                Content = this
            };

            _fullscreenWindow.Closed += (s, e) =>
            {
                if (isFullscreen)
                {
                    ExitFullscreen();
                }
            };

            if (_originalFrame != null)
                _originalFrame.Content = null;

            SetButtonSource(FullscreenButton, isFullscreen);
            _fullscreenWindow.Show();
        }

        private void ExitFullscreen()
        {
            isFullscreen = false;
            SetButtonSource(FullscreenButton, isFullscreen);

            _fullscreenWindow.Content = null;
            if (_originalFrame != null)
            {
                _originalFrame.NavigationUIVisibility = NavigationUIVisibility.Hidden;
                _originalFrame.Content = this;
                while (_originalFrame.NavigationService.CanGoBack)
                    _originalFrame.NavigationService.RemoveBackEntry();
            }
            _fullscreenWindow?.Close();
            _fullscreenWindow = null;
        }

        private Frame GetFrameParent()
        {
            DependencyObject parent = VisualTreeHelper.GetParent(this);
            while (parent != null)
            {
                if (parent is Frame frame)
                    return frame;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void SetButtonSource(SliceControl button, bool active)
        {
            if (button == SidebarButton)
            {
                button.Source = active ? sidebar_collapse : sidebar_expand;
            }
            else if (button == ChatButton)
            {
                button.Source = active ? chat_active : chat_inactive;
            }
            else if (button == FullscreenButton)
            {
                button.Source = active ? screen_contract : screen_expand;
            }
        }

        private void OnHangUp(object sender, MouseButtonEventArgs e)
        {
            _hangUpRequested = true;
            _ringCts?.Cancel();
            SoundManager.StopPlayback("CALL_OUT");
            SoundManager.StopPlayback("CALL_INIT");
            _backend.CallStateChanged -= OnCallStateChanged;
            _ = _backend.EndCall(_call);
            _callTimer?.Stop();
            _callTimer = null;
            SoundManager.Play("HANGUP");

            if (isFullscreen)
            {
                ExitFullscreen();
            }

            HangUpRequested(this, EventArgs.Empty);
        }

        #endregion

        private void Resized(object sender, RoutedEventArgs e)
        {
            bool newPillMode = this.ActualWidth >= 1025.0;
            if (newPillMode != isPillMode)
            {
                if (newPillMode) // pill
                {
                    ActionBar.Margin = new Thickness(0, 0, 0, 16);
                    ActionBar.HorizontalAlignment = HorizontalAlignment.Center;
                    ActionBarContainer.Source = pill;
                    ActionBarContainer.SliceMode = 1;
                    ActionBarContainer.ClearValue(HeightProperty);
                }
                else // rectangle
                {
                    ActionBar.Margin = new Thickness(0);
                    ActionBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                    ActionBarContainer.Source = rectangle;
                    ActionBarContainer.SliceMode = 2;
                    ActionBarContainer.Height = 88;
                }

                isPillMode = newPillMode;
            }

            bool newLogoBig = this.ActualWidth >= 700 && this.ActualHeight >= 700;
            if (newLogoBig != isLogoBig)
            {
                if (newLogoBig) // big logo
                {
                    Logo.Width = 169;
                    Logo.Source = logo_big;
                }
                else // small logo
                {
                    Logo.Width = 52;
                    Logo.Source = logo_small;
                }

                isLogoBig = newLogoBig;
            }
        }
    }
}
