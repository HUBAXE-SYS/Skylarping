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

using Skymu.Preferences;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Windows.Controls;

namespace Skymu.Forms
{
    public partial class HomeUnavailable : Page
    {
        public HomeUnavailable()
        {
            InitializeComponent();
            Universal.Lang.PropertyChanged += RefreshText;
            RefreshText(null, null);
        }

        private void RefreshText(object sender, PropertyChangedEventArgs e)
        {
            JObject lang = GetLanguage();
            if (lang == null) return;
            NoHomeHead.Text = ((string)lang["header"] ?? "").Replace("Skype", Settings.BrandingName);
            NoHomeBody.Text = ((string)lang["p1"] ?? "").Replace("Skype", Settings.BrandingName);
            NoHomeListHead.Text = ((string)lang["p2"] ?? "").Replace("Skype", Settings.BrandingName);
            NoHomeList1.Text = (string)lang["list1li1"];
            NoHomeList2.Text = (string)lang["list1li2"];
        }

        private static JObject GetLanguage()
        {
            string json = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", "home-unavailable.json"));

            string cultureName = "en-US";
            try
            {
                cultureName = CultureInfo.GetCultures(CultureTypes.AllCultures)
                    .FirstOrDefault(c =>
                        c.NativeName.StartsWith(Settings.Language) ||
                        c.DisplayName.StartsWith(Settings.Language) ||
                        c.EnglishName.StartsWith(Settings.Language)
                    )?.Name ?? "en-US";
            }
            catch { }

            string langChunk = cultureName.Split('-')[0];

            JObject root = JObject.Parse(json);

            return (root[langChunk] as JObject) ?? (root["en"] as JObject);
        }
    }
}
