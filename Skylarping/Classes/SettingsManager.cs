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

using Skylarping.Enumerations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Skylarping.Preferences
{
    public static class Settings
    {
        private const int ConfigRevision = 2;

        #region UI/General

        public static bool StartMinimized
        {
            get => SELECT("StartMinimized", false, "UI/General");
            set => WRITE("StartMinimized", value, nameof(StartMinimized), "UI/General");
        }

        public static bool AllowMultipleInstances
        {
            get => SELECT("AllowMultipleInstances", true, "UI/General");
            set => WRITE("AllowMultipleInstances", value, nameof(AllowMultipleInstances), "UI/General");
        }

        public static WindowFrame WindowFrame
        {
            get => SELECT("WindowFrame", WindowFrame.SkypeAero, "UI/General");
            set => WRITE("WindowFrame", value, nameof(WindowFrame), "UI/General");
        }
        public static int EmojiFps
        {
            get => SELECT("EmojiFps", 50, "UI/General");
            set => WRITE("EmojiFps", value, nameof(EmojiFps), "UI/General");
        }
        public static int MsgLoadCount
        {
            get => SELECT("MsgLoadCount", 30, "UI/General");
            set => WRITE("MsgLoadCount", value, nameof(MsgLoadCount), "UI/General");
        }
        /// <summary>
        /// Most messages kept in the open conversation; older ones drop out of view (they stay in the
        /// database). Bounds UI cost (the chat list isn't virtualized) and AI context. No options UI yet.
        /// </summary>
        public static int MaxConversationItems
        {
            get => SELECT("MaxConversationItems", 250, "UI/General");
            set => WRITE("MaxConversationItems", value, nameof(MaxConversationItems), "UI/General");
        }
        public static int CredsSubCount
        {
            get => SELECT("CredsSubCount", 0, "UI/General");
            set => WRITE("CredsSubCount", value, nameof(CredsSubCount), "UI/General");
        }

        public static string BrandingName
        {
            get => SELECT("BrandingName", "Skype", "UI/General");
            set => WRITE("BrandingName", value, nameof(BrandingName), "UI/General");
        }

        public static string Colorway
        {
            get => SELECT("Colorway", "Default", "UI/General");
            set => WRITE("Colorway", value, nameof(Colorway), "UI/General");
        }
        public static string CredsText
        {
            get => SELECT("CredsText", "$ 0.00", "UI/General");
            set => WRITE("CredsText", value, nameof(CredsText), "UI/General");
        }

        public static string PresentationFramework
        {
            get => SELECT("PresentationFramework", "Aero.NormalColor", "UI/General");
            set 
            {
                string framework = value;
                if (value == "Fluent.Dynamic")
                {
                    if (Universal.IsDarkTheme) framework = "Fluent.Dark";
                    else framework = "Fluent.Light";
                }
                WRITE("PresentationFramework", framework, nameof(PresentationFramework), "UI/General");
            }
        }
        public static string Language
        {
            get => SELECT("Language", "English", "UI/General");
            set => WRITE("Language", value, nameof(Language), "UI/General");
        }
        public static bool UseSystemCulture
        {
            get => SELECT("UseSystemCulture", true, "UI/General");
            set => WRITE("UseSystemCulture", value, nameof(UseSystemCulture), "UI/General");
        }
        public static string Theme
        {
            get => SELECT("Theme", "Skype4", "UI/General");
            set => WRITE("Theme", value, nameof(Theme), "UI/General");
        }
        public static bool RoomCallUI
        {
            get => SELECT("RoomCallUI", false, "UI/General");
            set => WRITE("RoomCallUI", value, nameof(RoomCallUI), "UI/General");
        }
        public static bool CallOutToReconnectSound
        {
            get => SELECT("CallOutToReconnectSound", false, "UI/General");
            set => WRITE("CallOutToReconnectSound", value, nameof(CallOutToReconnectSound), "UI/General");
        }
        public static Soundpack SoundPack
        {
            get => SELECT("SoundPack", Soundpack.Enhanced, "UI/General");
            set => WRITE("SoundPack", value, nameof(SoundPack), "UI/General");
        }


        public static bool EnableNotifications
        {
            get => SELECT("EnableNotifications", true, "UI/General");
            set => WRITE("EnableNotifications", value, nameof(EnableNotifications), "UI/General");
        }
        // TODO Above is not needed if we have a NotificationTrigger of 0.
        // TODO This should be migrated to a check list. The code works fine I believe - we'd like to have a checkbox that calculates the value.
        public static NotificationTriggerType NotificationTrigger
        {
            get => SELECT("NotificationTrigger", NotificationTriggerType.PDM, "UI/General");
            set => WRITE("NotificationTrigger", value, nameof(NotificationTrigger), "UI/General");
        }
        // TODO This should be merged with above after the change. Make sure to add a Migrator.cs rule detecting this, and merging two variables.
        public static bool AllowImplicitMentions
        {
            get => SELECT("AllowImplicitMentions", true, "UI/General");
            set => WRITE("AllowImplicitMentions", value, nameof(NotificationTrigger), "UI/General");
        }
        public static bool UseClearType
        {
            get => SELECT("UseClearType", true, "UI/General");
            set => WRITE("UseClearType", value, nameof(UseClearType), "UI/General");
        }
        public static bool DynamicSidebarTabs
        {
            get => SELECT("DynamicSidebarTabs", true, "UI/General");
            set => WRITE("DynamicSidebarTabs", value, nameof(DynamicSidebarTabs), "UI/General");
        }
        public static bool BlueNotifications
        {
            get => SELECT("BlueNotifications", false, "UI/General");
            set => WRITE("BlueNotifications", value, nameof(BlueNotifications), "UI/General");
        }
        /// <summary>
        /// If FALSE dynamic window colors will be used, i.e. inactive window = gray gradient, active window = blue gradient.
        /// If TRUE a single solid window color will be maintained whether the window is active or inactive.
        /// </summary>
        public static bool FallbackFillColors
        {
            get => SELECT("FallbackFillColors", false, "UI/General");
            set => WRITE("FallbackFillColors", value, nameof(FallbackFillColors), "UI/General");
        }
        /// <summary>
        /// If FALSE original versions of edited and deleted messages will not be stored.
        /// If TRUE original versions of edited and deleted messages will be stored. Not recommended for message streaming backends.
        /// </summary>
        public static bool StoreMessageHistory
        {
            get => SELECT("StoreMessageHistory", false, "UI/General");
            set => WRITE("StoreMessageHistory", value, nameof(StoreMessageHistory), "UI/General");
        }
        public static bool NikoIcons
        {
            get => SELECT("NikoIcons", false, "UI/General");
            set => WRITE("NikoIcons", value, nameof(NikoIcons), "UI/General");
        }
        // TODO: Rename to QuitWithoutAsking with migration
        public static bool QuitWithoutAsking
        {
            get => SELECT("QuitWithoutAsking", false, "UI/General");
            set => WRITE("QuitWithoutAsking", value, nameof(QuitWithoutAsking), "UI/General");
        }
        /// <summary> true = do not inform </summary>
        public static bool InformDND
        {
            get => SELECT("InformDND", false, "UI/General");
            set => WRITE("InformDND", value, nameof(InformDND), "UI/General");
        }
        /// <summary> true = already closed the "this is converstaions list" popup on Skype 4 </summary>
        public static bool InboxNoticeShown
        {
            get => SELECT("InboxNoticeShown", false, "UI/General");
            set => WRITE("InboxNoticeShown", value, nameof(InboxNoticeShown), "UI/General");
        }
        /// <summary> true = hide the sidebar on call start </summary>
        public static bool HideLeftHandSide
        {
            get => SELECT("HideLeftHandSide", false, "UI/General");
            set => WRITE("HideLeftHandSide", value, nameof(HideLeftHandSide), "UI/General");
        }

        #endregion

        #region UI/Login

        public static bool AutoLogin
        {
            get => SELECT("AutoLogin", true, "UI/Login");
            set => WRITE("AutoLogin", value, nameof(AutoLogin), "UI/Login");
        }

        /// <summary>
        /// Display name of the local profile, used for automatic sign-in.
        /// </summary>
        public static string ProfileName
        {
            get => SELECT("ProfileName", string.Empty, "UI/Login");
            set => WRITE("ProfileName", value, nameof(ProfileName), "UI/Login");
        }

        #endregion

        #region UI/MainWindow

        public static double ConvListWidth
        {
            get => SELECT("ConvListWidth", -1.0, "UI/MainWindow");
            set => WRITE("ConvListWidth", value, nameof(ConvListWidth), "UI/MainWindow");
        }

        public static bool SaveWindowPosition
        {
            get => SELECT("SaveWindowPosition", true, "UI/MainWindow");
            set => WRITE("SaveWindowPosition", value, nameof(SaveWindowPosition), "UI/MainWindow");
        }

        #endregion

        #region UI/MainWindow40

        public static double Height
        {
            get => SELECT("Height", (double)-1, "UI/MainWindow40");
            set => WRITE("Height", value, nameof(Height), "UI/MainWindow40");
        }

        public static bool Maximized
        {
            get => SELECT("Maximized", false, "UI/MainWindow40");
            set => WRITE("Maximized", value, nameof(Maximized), "UI/MainWindow40");
        }

        public static double Width
        {
            get => SELECT("Width", (double)-1, "UI/MainWindow40");
            set => WRITE("Width", value, nameof(Width), "UI/MainWindow40");
        }

        public static double X
        {
            get => SELECT("X", (double)-1, "UI/MainWindow40");
            set => WRITE("X", value, nameof(X), "UI/MainWindow40");
        }

        public static double Y
        {
            get => SELECT("Y", (double)-1, "UI/MainWindow40");
            set => WRITE("Y", value, nameof(Y), "UI/MainWindow40");
        }

        #endregion

        #region Skylarping/System

        /// <summary>
        /// If FALSE a warning will be shown if you are running a version of Windows or Wine with incompatibilities.
        /// If TRUE no warning will be shown. Automatically set to TRUE after warning is shown once.
        /// </summary>
        public static bool OldPlatformWarningShown
        {
            get => SELECT("OldPlatformWarningShown", false, "Skylarping/System");
            set => WRITE("OldPlatformWarningShown", value, nameof(OldPlatformWarningShown), "Skylarping/System");
        }

        #endregion

        #region AI/LanguageModel

        // The OpenAI-compatible chat model every character talks through (Tools > AI Services).
        // Speech-to-text, text-to-speech and image generation will get sibling AI/* sections.

        /// <summary>Base URL of the OpenAI-compatible API, e.g. http://localhost:1234/v1. Empty = not set up yet.</summary>
        public static string LlmServerUrl
        {
            get => SELECT("ServerUrl", "", "AI/LanguageModel");
            set => WRITE("ServerUrl", value ?? "", nameof(LlmServerUrl), "AI/LanguageModel");
        }

        /// <summary>API key. Stored encrypted for the current Windows user (DPAPI); see <see cref="AI.ApiKeyProtector"/>.</summary>
        public static string LlmApiKey
        {
            get => AI.ApiKeyProtector.Unprotect(SELECT("ApiKey", "", "AI/LanguageModel"));
            set => WRITE("ApiKey", AI.ApiKeyProtector.Protect(value), nameof(LlmApiKey), "AI/LanguageModel");
        }

        public static string LlmModel
        {
            get => SELECT("Model", "", "AI/LanguageModel");
            set => WRITE("Model", value ?? "", nameof(LlmModel), "AI/LanguageModel");
        }

        /// <summary>Context window of the model in tokens. 0 = unknown (no trimming by token count).</summary>
        public static int LlmContextWindow
        {
            get => SELECT("ContextWindow", 0, "AI/LanguageModel");
            set => WRITE("ContextWindow", value, nameof(LlmContextWindow), "AI/LanguageModel");
        }

        /// <summary>True once a language model server has been entered.</summary>
        public static bool LlmConfigured => !string.IsNullOrEmpty(LlmServerUrl.Trim());

        #endregion

        #region AI/Display

        /// <summary>How the model's thinking (reasoning) is shown in chat.</summary>
        public static ChatBlockDisplayMode ThinkingDisplay
        {
            get => SELECT("ThinkingDisplay", ChatBlockDisplayMode.Collapsed, "AI/Display");
            set => WRITE("ThinkingDisplay", value, nameof(ThinkingDisplay), "AI/Display");
        }

        /// <summary>If FALSE, messages are shown as plain text (emoticons and links still work).</summary>
        public static bool MarkdownParsing
        {
            get => SELECT("MarkdownParsing", true, "AI/Display");
            set => WRITE("MarkdownParsing", value, nameof(MarkdownParsing), "AI/Display");
        }

        #endregion

        #region Registry

        /// <summary>
        /// If FALSE Skylarping will not start when the computer starts.
        /// If TRUE Skylarping will start when the computer starts.
        /// </summary>
        public static bool AutoLaunch
        {
            get => Native.Windows.AutoLaunch.Get();
            set => Native.Windows.AutoLaunch.Set(value);
        }

        #endregion

        public static void Save() { }

        public static void Reset()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Universal.NAME,
            "shared.xml"
        );

        private static XDocument LoadOrCreate()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var loaded = XDocument.Load(FilePath);
                    if (loaded.Root != null)
                    {
                        var revision = (int?)loaded.Root.Attribute("revision");
                        if (revision == ConfigRevision)
                            return loaded;
                    }
                }
                catch { }

                System.Windows.MessageBox.Show(
                    $"{Universal.NAME} has discovered that your configuration file is corrupt or from an older version of the application. " +
                    "It has been reset and your settings have been wiped.",
                    "Configuration file reset",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
                File.Delete(FilePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var doc = new XDocument(new XElement("config", new XAttribute("revision", ConfigRevision)));
            doc.Save(FilePath);
            return doc;
        }
        private static XElement GetOrCreateNode(XDocument doc, string path)
        {
            var parts = path.Split('/');
            XElement current = doc.Root;
            foreach (var part in parts)
            {
                var next = current.Element(part);
                if (next == null)
                {
                    next = new XElement(part);
                    current.Add(next);
                }
                current = next;
            }
            return current;
        }

        private static string Get(string key, string default_value, string path_xml)
        {
            try
            {
                var doc = LoadOrCreate();
                return GetOrCreateNode(doc, path_xml).Element(key)?.Value ?? default_value;
            }
            catch
            {
                return default_value;
            }
        }

        private static void Set(string key, string value, string path_xml)
        {
            var doc = LoadOrCreate();
            var node = GetOrCreateNode(doc, path_xml);
            var el = node.Element(key);
            if (el == null)
                node.Add(new XElement(key, value));
            else
                el.Value = value;
            doc.Save(FilePath);
        }

        private static string SELECT(string key, string default_value, string path_xml)
        {
            string rawValue = Get(key, null, path_xml);

            if (rawValue == null)
            {
                Set(key, default_value, path_xml);
                return default_value;
            }

            return rawValue;
        }

        private static bool SELECT(string key, bool default_value, string path_xml)
        {
            string value = Get(key, default_value.ToString(), path_xml);

            // (omega, nilFinx) Skype's settings parser is a little weird. It counted <= 0 as false and >= 1 as true.
            // Skylarping's settings parser will reflect that even though there doesn't seem to be a use case for this.

            int vi;
            if (int.TryParse(value, out vi))
                return vi > 0;

            // boolean parsing retained for backwards compatibility with old Skylarping configuration files

            bool vb;
            if (bool.TryParse(value, out vb))
                return vb;

            Set(key, default_value.ToString(), path_xml);
            return default_value;
        }

        private static int SELECT(string key, int default_value, string path_xml)
        {
            if (int.TryParse(Get(key, default_value.ToString(), path_xml), out var v))
            {
                return v;
            }
            Set(key, default_value.ToString(), path_xml);
            return default_value;
        }

        private static double SELECT(string key, double default_value, string path_xml)
        {
            if (double.TryParse(Get(key, default_value.ToString(), path_xml), out var v))
            {
                return v;
            }
            Set(key, default_value.ToString(), path_xml);
            return default_value;
        }

        private static TEnum SELECT<TEnum>(string key, TEnum default_value, string path_xml) where TEnum : struct, Enum
        {
            string rawValue = Get(key, default_value.ToString(), path_xml);
            if (Enum.TryParse<TEnum>(rawValue, true, out var result))
            {
                return result;
            }
            Set(key, default_value.ToString(), path_xml);
            return default_value;
        }

        private static void WRITE<T>(string key, T value, string propName, string path_xml)
        {
            string serialized = value is bool b ? (b ? "1" : "0") : value.ToString();
            Set(key, serialized, path_xml);
            Default.Notify(propName);
        }

        public static readonly SettingsProxy Default = new SettingsProxy();

        public class SettingsProxy : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            internal void Notify(string n) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            private static readonly Dictionary<string, PropertyInfo> _props =
                new Dictionary<string, PropertyInfo>();

            static SettingsProxy()
            {
                foreach (
                    var p in typeof(Settings).GetProperties(
                        BindingFlags.Public | BindingFlags.Static
                    )
                )
                    _props[p.Name] = p;
            }

            public object this[string key]
            {
                get => _props.TryGetValue(key, out var p) ? p.GetValue(null, null) : null;
                set
                {
                    if (!_props.TryGetValue(key, out var p))
                        return;
                    try
                    {
                        var converted = Convert.ChangeType(value, p.PropertyType);
                        p.SetValue(null, converted, null);
                        Notify(key);
                    }
                    catch { }
                }
            }
        }

    }
}
