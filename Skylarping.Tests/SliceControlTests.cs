// SliceControl sizing. The call button has no fixed width, so it has to follow its text.

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;

namespace Skylarping.Tests
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SliceControlTests
    {
        // Longer than the 150 ms hover fade.
        private const int FadeMilliseconds = 400;

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Mouse(SliceControl control, RoutedEvent routedEvent) =>
            control.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });

        [Test]
        public void AutoWidthButtonShrinksWithItsTextAfterBeingHovered()
        {
            var sprite = new WriteableBitmap(60, 250, 96, 96, PixelFormats.Pbgra32, null);
            var button = new SliceControl
            {
                Width = double.NaN,
                Source = sprite,
                ElementCount = 5,
                DefaultIndex = 0,
                HoverIndex = 1,
                PressedIndex = 2,
                LeftWidth = 23,
                RightWidth = 15,
                TextLeftMargin = 26,
                TextSize = 13,
                Text = "Call group",
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(button);
            var window = new Window
            {
                Content = row,
                Width = 400,
                Height = 100,
                Left = -2000,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                double groupWidth = button.ActualWidth;

                Mouse(button, UIElement.MouseEnterEvent);
                Pump(FadeMilliseconds);
                Mouse(button, UIElement.MouseLeaveEvent);
                Pump(FadeMilliseconds);

                button.Text = "Call";
                window.UpdateLayout();

                Assert.Less(button.ActualWidth, groupWidth);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
