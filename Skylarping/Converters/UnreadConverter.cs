/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Conversation.UnreadCount -> the contact list's new-message badge.
//  ConverterParameter "index":      slice of Notifications/unread.png (0 = "1" ... 8 = "9", also used for more)
//  ConverterParameter "visibility": Visible while there are unread messages
//  ConverterParameter "weight":     bold name while there are unread messages
/*==========================================================*/

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Skylarping.Converters
{
    public class UnreadConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = value is int n ? n : 0;
            switch (parameter as string)
            {
                case "index":
                    return Math.Max(0, Math.Min(count, 9) - 1);
                case "weight":
                    return count > 0 ? FontWeights.Bold : FontWeights.Normal;
                default:
                    return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
