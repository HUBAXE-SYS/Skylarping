/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Change Your Picture... / Remove Your Picture for the signed-in user. Used by the Skype
// menu, a click on your own picture in the main window, and Options > General. The
// picture is squared the same way as a character's (CharacterAvatar.Normalize) and
// saved by the backend; Universal.CurrentUser.Avatar updates, so every view follows.
/*==========================================================*/

using Microsoft.Win32;
using Skylarping.Characters;
using Skylarping.Forms;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Skylarping.Helpers
{
    public static class ProfilePicture
    {
        private const string Title = "Change Your Picture";

        public static bool HasPicture => Universal.CurrentUser?.Avatar != null;

        /// <summary>Asks for a picture file and makes it the user's picture. Returns true if it changed.</summary>
        public static async Task<bool> Change(Window owner)
        {
            if (Universal.CurrentUser == null || Universal.Backend == null)
                return false;

            var dlg = new OpenFileDialog
            {
                Title = "Choose your picture",
                Filter = CharacterAvatar.OpenFilter,
                CheckFileExists = true
            };
            if (dlg.ShowDialog(owner) != true)
                return false;

            byte[] picture;
            try
            {
                picture = CharacterAvatar.Normalize(File.ReadAllBytes(dlg.FileName));
            }
            catch (Exception ex)
            {
                string message = ex is CharacterCardException ? ex.Message : "The picture couldn't be opened: " + ex.Message;
                Universal.ShowMessage(message, Title, WindowBase.IconType.Error);
                return false;
            }
            return await Save(picture);
        }

        /// <summary>Goes back to the default picture. Returns true if it changed.</summary>
        public static Task<bool> Remove()
        {
            if (!HasPicture || Universal.Backend == null)
                return TaskEx.FromResult(false);
            return Save(null);
        }

        private static async Task<bool> Save(byte[] picture)
        {
            bool saved = false;
            try { saved = await Universal.Backend.SetAvatar(picture); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("SetAvatar failed: " + ex); }
            if (!saved)
                Universal.ShowMessage("Your picture couldn't be saved.", Title, WindowBase.IconType.Error);
            return saved;
        }
    }
}
