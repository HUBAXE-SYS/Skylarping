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

// Elements named in a theme's XAML. The compiler generates fields for them on the theme's own
// class, out of reach from here, so they are looked up by name. Private on purpose: these don't
// clash with the generated fields. ThemeWindowTests checks every theme has all of them.
namespace Skylarping
{
    public abstract partial class MainWindowBase
    {
        // Null while the XAML is still loading, like a generated field: controls can raise events
        // (MessageTextBox's TextChanged) before the elements after them exist.
        private object Part(string name) => FindName(name);

        private global::Skylarping.SliceControl btnContacts => (global::Skylarping.SliceControl)Part(nameof(btnContacts));
        private global::Skylarping.SliceControl btnRecents => (global::Skylarping.SliceControl)Part(nameof(btnRecents));
        private global::Skylarping.SliceControl CallButton => (global::Skylarping.SliceControl)Part(nameof(CallButton));
        private global::System.Windows.Controls.Grid ChatArea => (global::System.Windows.Controls.Grid)Part(nameof(ChatArea));
        private global::System.Windows.Controls.RowDefinition ChatButtonRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(ChatButtonRow));
        private global::System.Windows.Controls.ContentControl ChatProfileArea => (global::System.Windows.Controls.ContentControl)Part(nameof(ChatProfileArea));
        private global::System.Windows.Controls.RowDefinition ChatTopBarRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(ChatTopBarRow));
        private global::System.Windows.Shapes.Rectangle ChatTopBarSplitter => (global::System.Windows.Shapes.Rectangle)Part(nameof(ChatTopBarSplitter));
        private global::System.Windows.Controls.RowDefinition ChatTopbarSplitterRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(ChatTopbarSplitterRow));
        private global::Skylarping.SliceControl close => (global::Skylarping.SliceControl)Part(nameof(close));
        private global::System.Windows.Controls.ColumnDefinition ContactsColumn => (global::System.Windows.Controls.ColumnDefinition)Part(nameof(ContactsColumn));
        private global::System.Windows.Controls.Grid ContentArea => (global::System.Windows.Controls.Grid)Part(nameof(ContentArea));
        private global::System.Windows.Shapes.Rectangle ContentBgBottom => (global::System.Windows.Shapes.Rectangle)Part(nameof(ContentBgBottom));
        private global::System.Windows.Shapes.Rectangle ContentBgTop => (global::System.Windows.Shapes.Rectangle)Part(nameof(ContentBgTop));
        private global::System.Windows.Controls.ListBox ConversationItemsList => (global::System.Windows.Controls.ListBox)Part(nameof(ConversationItemsList));
        private global::System.Windows.Controls.ListBox ConversationList => (global::System.Windows.Controls.ListBox)Part(nameof(ConversationList));
        private global::System.Windows.Controls.Primitives.Popup EmojiFlyout => (global::System.Windows.Controls.Primitives.Popup)Part(nameof(EmojiFlyout));
        private global::System.Windows.Controls.WrapPanel EmojiWrapPanel => (global::System.Windows.Controls.WrapPanel)Part(nameof(EmojiWrapPanel));
        private global::System.Windows.Controls.ContentControl FillMessagePanelHost => (global::System.Windows.Controls.ContentControl)Part(nameof(FillMessagePanelHost));
        private global::System.Windows.Controls.ContentControl FillWindowHost => (global::System.Windows.Controls.ContentControl)Part(nameof(FillWindowHost));
        private global::System.Windows.Controls.Grid FrameArea => (global::System.Windows.Controls.Grid)Part(nameof(FrameArea));
        private global::System.Windows.Controls.Grid Home => (global::System.Windows.Controls.Grid)Part(nameof(Home));
        private global::System.Windows.Controls.Menu MainMenuBar => (global::System.Windows.Controls.Menu)Part(nameof(MainMenuBar));
        private global::System.Windows.Shapes.Rectangle MainMenuBarDivider => (global::System.Windows.Shapes.Rectangle)Part(nameof(MainMenuBarDivider));
        private global::Skylarping.SliceControl maximize => (global::Skylarping.SliceControl)Part(nameof(maximize));
        private global::System.Windows.Controls.ListBox MentionList => (global::System.Windows.Controls.ListBox)Part(nameof(MentionList));
        private global::System.Windows.Controls.Primitives.Popup MentionPopup => (global::System.Windows.Controls.Primitives.Popup)Part(nameof(MentionPopup));
        private global::System.Windows.Controls.StackPanel MenuBarArea => (global::System.Windows.Controls.StackPanel)Part(nameof(MenuBarArea));
        private global::System.Windows.Controls.MenuItem MenubarStatusHolder => (global::System.Windows.Controls.MenuItem)Part(nameof(MenubarStatusHolder));
        private global::System.Windows.Controls.RichTextBox MessageTextBox => (global::System.Windows.Controls.RichTextBox)Part(nameof(MessageTextBox));
        private global::System.Windows.Controls.Border MessageWindow => (global::System.Windows.Controls.Border)Part(nameof(MessageWindow));
        private global::System.Windows.Controls.RowDefinition MessageWindowRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(MessageWindowRow));
        private global::Skylarping.SliceControl minimize => (global::Skylarping.SliceControl)Part(nameof(minimize));
        private global::System.Windows.Controls.TextBox MoodBox => (global::System.Windows.Controls.TextBox)Part(nameof(MoodBox));
        private global::System.Windows.FrameworkElement ProfileGrid => (global::System.Windows.FrameworkElement)Part(nameof(ProfileGrid));
        private global::Skylarping.SliceControl PseudoSearchBox => (global::Skylarping.SliceControl)Part(nameof(PseudoSearchBox));
        private global::System.Windows.Controls.ColumnDefinition RecentsColumn => (global::System.Windows.Controls.ColumnDefinition)Part(nameof(RecentsColumn));
        private global::System.Windows.Controls.MenuItem RemovePictureMenuItem => (global::System.Windows.Controls.MenuItem)Part(nameof(RemovePictureMenuItem));
        private global::System.Windows.Controls.TextBox SearchBox => (global::System.Windows.Controls.TextBox)Part(nameof(SearchBox));
        private global::Skylarping.SliceControl SendMsgButton => (global::Skylarping.SliceControl)Part(nameof(SendMsgButton));
        private global::System.Windows.Controls.Grid Sidebar => (global::System.Windows.Controls.Grid)Part(nameof(Sidebar));
        private global::System.Windows.Controls.Image SidebarAvatar => (global::System.Windows.Controls.Image)Part(nameof(SidebarAvatar));
        private global::System.Windows.Controls.ColumnDefinition SidebarColumn => (global::System.Windows.Controls.ColumnDefinition)Part(nameof(SidebarColumn));
        private global::System.Windows.Controls.ColumnDefinition SkypeSplitterColumn => (global::System.Windows.Controls.ColumnDefinition)Part(nameof(SkypeSplitterColumn));
        private global::System.Windows.Controls.Image Spinner => (global::System.Windows.Controls.Image)Part(nameof(Spinner));
        private global::Skylarping.SliceControl split => (global::Skylarping.SliceControl)Part(nameof(split));
        private global::System.Windows.Controls.StackPanel StatusArea => (global::System.Windows.Controls.StackPanel)Part(nameof(StatusArea));
        private global::Skylarping.SliceControl StatusIcon => (global::Skylarping.SliceControl)Part(nameof(StatusIcon));
        private global::System.Windows.Controls.Grid TitleBar => (global::System.Windows.Controls.Grid)Part(nameof(TitleBar));
        private global::System.Windows.Controls.Image TitleBarIcon => (global::System.Windows.Controls.Image)Part(nameof(TitleBarIcon));
        private global::System.Windows.Controls.TextBlock TitleMain => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleMain));
        private global::System.Windows.Controls.TextBlock TitleShadow => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow));
        private global::System.Windows.Controls.TextBlock TitleShadow2 => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow2));
        private global::System.Windows.Controls.TextBlock TitleShadow3 => (global::System.Windows.Controls.TextBlock)Part(nameof(TitleShadow3));
        private global::System.Windows.Controls.RowDefinition TopbarWindowRow => (global::System.Windows.Controls.RowDefinition)Part(nameof(TopbarWindowRow));
        private global::System.Windows.Controls.StackPanel TypingIndicator => (global::System.Windows.Controls.StackPanel)Part(nameof(TypingIndicator));
        private global::System.Windows.Controls.TextBlock TypingIndicatorText => (global::System.Windows.Controls.TextBlock)Part(nameof(TypingIndicatorText));
        private global::System.Windows.Controls.Grid WindowArea => (global::System.Windows.Controls.Grid)Part(nameof(WindowArea));
    }
}
