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

namespace Skymu.Backend
{
    /// <summary>
    ///  Owns the lifetime of the app's single chat backend (Universal.Backend).
    /// </summary>
    internal static class BackendManager
    {
        /// <summary>
        ///  Disposes any current backend and creates a fresh one, wired to the app's dialog and message handlers.
        /// </summary>
        public static IChatBackend CreateBackend()
        {
            DisposeBackend();
            IChatBackend backend = new StubBackend();
            backend.DialogRequested += Universal.BackendDialogHandler;
            backend.MessagesChanged += Universal.BackendMessageHandler;
            Universal.Backend = backend;
            return backend;
        }

        public static void DisposeBackend()
        {
            IChatBackend backend = Universal.Backend;
            if (backend == null)
                return;
            Universal.Backend = null;
            backend.DialogRequested -= Universal.BackendDialogHandler;
            backend.MessagesChanged -= Universal.BackendMessageHandler;
            try
            {
                backend.Dispose();
            }
            catch { }
        }
    }
}
