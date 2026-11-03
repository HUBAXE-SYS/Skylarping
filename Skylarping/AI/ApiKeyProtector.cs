/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Keeps API keys out of shared.xml in plain text: they are encrypted with DPAPI for the
// current Windows user (works on XP / .NET 4.0). A key copied to another user or PC
// can't be decrypted and simply reads back as empty.
/*==========================================================*/

using System;
using System.Security.Cryptography;
using System.Text;

namespace Skylarping.AI
{
    public static class ApiKeyProtector
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Skylarping.AI.ApiKey");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return "";
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(data);
            }
            catch
            {
                // DPAPI unavailable (shouldn't happen on Windows): don't lose the key.
                return plain;
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
                return stored; // hand-edited plain key
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return "";
            }
        }
    }
}
