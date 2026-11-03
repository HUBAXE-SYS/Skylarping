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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Skymu.Models;

namespace Skymu
{
    public class DateHeaderItem
    {
        public string DateText { get; set; }
    }

    public class CompactRecentsHelper
    {
        public static ObservableCollection<object> GroupByDate(
            ObservableCollection<Conversation> conversations
        )
        {
            var result = new ObservableCollection<object>();
            if (conversations == null || conversations.Count == 0)
                return result;

            var sorted = conversations.OrderByDescending(c => c.LastMessageTime).ToList();
            var groups = sorted
                .GroupBy(c => GetDateKey(c.LastMessageTime))
                .ToDictionary(g => g.Key, g => g.ToList());

            var sortedKeys = groups
                .Keys.OrderByDescending(k => ParseDateKey(k, sorted[0].LastMessageTime))
                .ToList();

            foreach (var key in sortedKeys)
            {
                result.Add(new DateHeaderItem { DateText = key });
                foreach (var convo in groups[key])
                    result.Add(convo);
            }

            return result;
        }

        private static string GetDateKey(DateTime dt)
        {
            var today = DateTime.Today;
            var dateOnly = dt.Date;

            if (dateOnly == today)
                return Universal.Lang["sTODAY"];
            if (dateOnly == today.AddDays(-1))
                return Universal.Lang["sYESTERDAY"];
            return dt.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        }

        private static DateTime ParseDateKey(string key, DateTime reference)
        {
            var today = DateTime.Today;

            if (key == Universal.Lang["sTODAY"])
                return today;
            if (key == Universal.Lang["sYESTERDAY"])
                return today.AddDays(-1);
            if (
                DateTime.TryParseExact(
                    key,
                    "dddd, MMMM d, yyyy",
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out DateTime parsed
                )
            )
                return parsed;

            return reference;
        }
    }
}
