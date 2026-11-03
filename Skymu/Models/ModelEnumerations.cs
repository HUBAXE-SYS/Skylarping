/*==========================================================*/
// Yggdrasil API copyright © OmegaAOL 2025-2026.
// For any inquiries, email hackersword666@gmail.com.
/*==========================================================*/
// License: https://www.apache.org/licenses/LICENSE-2.0
// SPDX-License-Identifier: Apache-2.0
// Modified by the Skylarping project: moved into the Skymu
// codebase, renamed and adapted (see git history).
/*==========================================================*/

namespace Skymu.Models
{
    public enum DialogType
    {
        Error,
        Warning,
        Information,
        Choice
    }

    public enum ListType
    {
        Contacts,
        Conversations,
    }

    public enum PresenceStatus
    {
        Online,
        DoNotDisturb,
        Away,
        OnlineMobile,
        DoNotDisturbMobile,
        AwayMobile,
        Invisible,
        Blocked,
        Offline,
        Unknown
    }

    public enum Fetch
    {
        Newest,
        Oldest,
        BeforeIdentifier,
        AfterIdentifier,
        NewestAfterIdentifier,
    }

    public enum CallState
    {
        Ringing,
        Active,
        Ended,
        Failed,
    }

    public enum AttachmentType
    {
        Image,
        ThumbnailImage,
        Video,
        Audio,
        File,
    }

    public enum ClickableItemType
    {
        User,
        GroupChat,
    }

    public enum ConversationType
    {
        DirectMessage,
        Group,
    }

    /// <summary>
    /// If both is present, use Explicit over Implicit.
    /// </summary>
    public enum MentionType
    {
        None,
        Implicit,
        Explicit
    }
}
