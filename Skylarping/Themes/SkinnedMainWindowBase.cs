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

using Skylarping.Formatting;
using Skylarping.Forms;
using Skylarping.Forms.Pages;
using Skylarping.Helpers;
using Skylarping.Infrastructure.Main;
using Skylarping.Native.Windows;
using Skylarping.ViewModels;
using Skylarping.Sounds;
using Skylarping.Preferences;
using Skylarping.Enumerations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Skylarping.Backend;
using Skylarping.Models;
using System.Linq;

namespace Skylarping
{
    /// <summary>
    ///  A main window that draws its own title bar and XAML menu bar (Skype 4 and 5). Themes on the
    ///  system frame and a native menu derive from MainWindowBase directly.
    /// </summary>
    public abstract partial class SkinnedMainWindowBase : MainWindowBase
    {
        private readonly WindowFrame _currentFrame = ResolveFrame((WindowFrame)Settings.WindowFrame);

        // The Aero frames need DWM glass (Vista/7 with composition on); without it (e.g. Windows XP)
        // their transparent background would render black, so fall back to the Basic skinned frame.
        private static WindowFrame ResolveFrame(WindowFrame frame)
        {
            if ((frame == WindowFrame.SkypeAero || frame == WindowFrame.SkypeAeroCustom) && !WindowChrome.IsGlassAvailable)
                return WindowFrame.SkypeBasic;
            return frame;
        }
        private Thickness OriginalWindowAreaMargin;
        private BitmapImage img_maximize,
            img_restore,
            img_split,
            img_join;
        private enum WindowFrame
        {
            SkypeAero,
            SkypeBasic,
            Native,
            SkypeAeroCustom,
        };
        /// <summary>Font of the window title on the Aero frames.</summary>
        protected abstract string AeroTitleFont { get; }

        protected override void InitializeFrame() => InitializeWindowFrame();

        protected override void ShowCaption(string name) => TitleMain.Text = name;

        private BitmapImage GenerateTitlebarButtonImage(string name)
        {
            string framedir = "Aero";
            if (_currentFrame == WindowFrame.SkypeBasic) framedir = "Basic";

            return ImageHelper.FreezeLoad(
                $"Universal/Window Frame/{framedir}/{name}/combined.png"
            );
        }
        #region Custom window logic

        public void InitializeWindowFrame()
        {
            if (_currentFrame != WindowFrame.Native) // using Skype's custom border
            {
                OriginalWindowAreaMargin = WindowArea.Margin; // for maximization stuff
                WindowChrome chrome = new WindowChrome();
                //chrome.UseAeroCaptionButtons = false;
                WindowChrome.SetWindowChrome(this, chrome); // WindowChrome configuration ensures that system frame is not drawn
                SetClickable(TitleBarIcon, close, minimize, maximize, split);
                TitleMain.Visibility = Visibility.Visible;
                if (
                    _currentFrame == WindowFrame.SkypeAero
                    || _currentFrame == WindowFrame.SkypeAeroCustom
                ) // switch configuration from Skype Basic to Aero
                {
                    Thickness AeroThickness = new Thickness(8, 30, 8, 8);
                    OriginalWindowAreaMargin = AeroThickness;
                    chrome.GlassFrameThickness = AeroThickness;
                    // Set up the window background and margin
                    WindowArea.Margin = AeroThickness;
                    TitleBar.Background = Brushes.Transparent;
                    if (_currentFrame == WindowFrame.SkypeAero)
                    {
                        this.Background = Brushes.Transparent;
                    }
                    else if (_currentFrame == WindowFrame.SkypeAeroCustom) // TODO: finish this
                    {
                        var img = ImageHelper.FreezeLoad(
                            "Universal/Window Frame/Aero/aero-background.png"
                        );
                        this.Background = new ImageBrush
                        {
                            ImageSource = img,
                            Stretch = Stretch.None,
                            TileMode = TileMode.None,
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, img.Width, img.Height),
                        };
                    }

                    // Titlebar font styling
                    TitleMain.FontFamily = new FontFamily(AeroTitleFont);
                    TitleMain.FontWeight = FontWeights.Normal;
                    TitleMain.FontSize = 12;
                    TitleMain.Foreground = Brushes.Black;

                    // Titlebar drop shadow (Imitates the Aero glow effect)
                    TitleMain.Effect = new DropShadowEffect
                    {
                        ShadowDepth = 0,
                        Direction = 330,
                        Color = Colors.White,
                        Opacity = 1,
                        BlurRadius = 20,
                    };

                    Style aeroStyle = (Style)FindResource("TitlebarTextStyleAero");
                    TitleMain.Style = aeroStyle;
                    TitleShadow.Style = aeroStyle;
                    TitleShadow2.Style = aeroStyle;
                    TitleShadow3.Style = aeroStyle;
                    TitleBarIcon.Margin = new Thickness(8, 5, 0, 0);
                }

                img_maximize = GenerateTitlebarButtonImage("maximize");
                img_restore = GenerateTitlebarButtonImage("restore");
                img_split = GenerateTitlebarButtonImage("split");
                img_join = GenerateTitlebarButtonImage("join");

                close.Source = GenerateTitlebarButtonImage("close");
                maximize.Source = img_maximize;
                minimize.Source = GenerateTitlebarButtonImage("minimize");
                split.Source = img_split;
            }
            else // using system native border
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                TitleBar.Visibility = Visibility.Collapsed;
                WindowArea.Margin = new Thickness(0);
            }
        }

        private DropShadowEffect CreateDropShadow(string color)
        {
            return new DropShadowEffect()
            {
                Color = (Color)Application.Current.Resources[color],
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 1,
            };
        }

        private void SetClickable(params IInputElement[] buttons)
        {
            foreach (var b in buttons)
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        }

        private void HandleWindowStateChanged()
        {
            if (OriginalWindowAreaMargin.Top != 0)
            {
                if (WindowState == WindowState.Maximized)
                {
                    maximize.Source = img_restore;
                    // Maximize is clamped to the work area, so the frame no longer hangs off-screen;
                    // pull it out by the same amount to keep the maximized layout as before.
                    Thickness o = WindowChrome.GetMaximizedFrameOverhang(this);
                    FrameArea.Margin = new Thickness(-o.Left, 5 - o.Top, -o.Right, -o.Bottom);
                    Thickness ReducedWinAreaMargin = OriginalWindowAreaMargin;
                    ReducedWinAreaMargin.Top -= 4;
                    WindowArea.Margin = ReducedWinAreaMargin;
                }
                else
                {
                    maximize.Source = img_maximize;
                    FrameArea.Margin = new Thickness(0);
                    WindowArea.Margin = OriginalWindowAreaMargin;
                }
            }
        }

        private void HandleWindowButtonEnter(SliceControl button)
        {
            if (button != null && _currentFrame != WindowFrame.SkypeBasic)
            {
                if (button.Name == "close")
                {
                    button.Effect = CreateDropShadow("WindowFrame.Button.Close.Glow");
                }
                else
                {
                    button.Effect = CreateDropShadow("WindowFrame.Button.Generic.Glow");
                }
            }
        }

        private void HandleWindowButtonLeave(SliceControl button)
        {
            if (vmodel.IsWindowActive)
            {
                if (button != null)
                {
                    button.Effect = null;
                }
            }
            else if (!vmodel.IsWindowActive)
            {
                button.Effect = null;
            }
        }

        private void FillSolid()
        {
            ContentBgTop.Fill = (Brush)Application.Current.Resources["Background"];
            ContentBgBottom.Fill = (Brush)Application.Current.Resources["Background"];
            MainMenuBar.Background = (Brush)Application.Current.Resources["Background"];
            MainMenuBarDivider.Fill = (Brush)Application.Current.Resources["Card.Border"];
            MenuBarArea.Background = (Brush)Application.Current.Resources["Background"];
            if (_currentFrame == WindowFrame.SkypeBasic)
            {
                TitleBar.Background = (Brush)Application.Current.Resources["Background"];
                this.Background = (Brush)Application.Current.Resources["Background"];
            }
        }

        protected override void OnActivationChanged(bool active)
        {
            if (active)
                ApplyActiveLook();
            else
                ApplyInactiveLook();
        }

        private void ApplyActiveLook()
        {
            foreach (var button in new[] { close, minimize, maximize, split })
            {
                button.DefaultIndex = 0;
            }

            if (Settings.FallbackFillColors)
            {
                FillSolid();
                return;
            }

            ContentBgTop.Fill = (Brush)Application.Current.Resources["Active.Window"];
            ContentBgBottom.Fill = (Brush)Application.Current.Resources["Active.Background"];
            MainMenuBar.Background = (Brush)Application.Current.Resources["Active.Menubar"];
            MainMenuBarDivider.Fill = (Brush)Application.Current.Resources["Active.Background"];

            if (_currentFrame == WindowFrame.SkypeBasic)
            {
                TitleBar.Background = (Brush)Application.Current.Resources["Active.Titlebar"];
                this.Background = (Brush)Application.Current.Resources["Active.Background"];
            }
        }

        private void ApplyInactiveLook()
        {
            foreach (var button in new[] { close, minimize, maximize, split })
            {
                button.DefaultIndex = 3;
            }

            if (Settings.FallbackFillColors)
            {
                FillSolid();
                return;
            }

            ContentBgTop.Fill = (Brush)Application.Current.Resources["Inactive.Window"];
            ContentBgBottom.Fill = (Brush)Application.Current.Resources["Inactive.Background"];
            MainMenuBar.Background = (Brush)Application.Current.Resources["Inactive.Menubar"];
            MainMenuBarDivider.Fill = (Brush)Application.Current.Resources["Inactive.Background"];


            if (_currentFrame == WindowFrame.SkypeBasic)
            {
                TitleBar.Background = (Brush)Application.Current.Resources["Inactive.Titlebar"];
                this.Background = (Brush)Application.Current.Resources["Inactive.Background"];
            }
        }

        private void HandleWindowButtonClick(SliceControl button)
        {
            if (button != null)
            {
                switch (button.Name)
                {
                    case "close":
                        Close();
                        break;
                    case "split":
                        Universal.NotImplemented("Split Window");
                        break;
                    case "minimize":
                        WindowState = WindowState.Minimized;
                        break;
                    case "maximize":
                        if (WindowState == WindowState.Normal)
                            WindowState = WindowState.Maximized;
                        else
                            WindowState = WindowState.Normal;
                        break;
                }
            }
        }

        #endregion

        protected void TitleButton_Click(object sender, MouseButtonEventArgs e)
        {
            HandleWindowButtonClick(sender as SliceControl);
        }

        protected void TitleButton_MouseLeave(object sender, MouseEventArgs e)
        {
            HandleWindowButtonLeave(sender as SliceControl);
        }

        protected void TitleButton_MouseEnter(object sender, MouseEventArgs e)
        {
            HandleWindowButtonEnter(sender as SliceControl);
        }

        protected void Window_StateChanged(object sender, EventArgs e)
        {
            HandleWindowStateChanged();
        }
        protected void ProfileMenu_SubmenuOpened(object sender, RoutedEventArgs e) =>
            RemovePictureMenuItem.IsEnabled = ProfilePicture.HasPicture;
        protected async void OnStatus(object sender, RoutedEventArgs e)
        {
            await SharedServices.SetStatusFromMenuItems(sender, MenubarStatusHolder.Items);
        }
    }
}
