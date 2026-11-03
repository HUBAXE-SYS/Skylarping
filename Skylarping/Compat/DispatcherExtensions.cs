/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// .NET 4.0 compatibility: Dispatcher.Invoke(Action) / Invoke(Func<T>) / BeginInvoke(Action)
// overloads were added in .NET 4.5; 4.0 only takes a Delegate, which a lambda can't convert to.
// These extensions are only picked when no 4.0 instance overload fits, so callers stay unchanged.
/*==========================================================*/

using System;
using System.Windows.Threading;

namespace Skylarping
{
    public static class DispatcherExtensions
    {
        private static readonly object[] NoArgs = new object[0];

        public static void Invoke(this Dispatcher dispatcher, Action callback)
        {
            dispatcher.Invoke(callback, NoArgs);
        }

        public static void Invoke(this Dispatcher dispatcher, Action callback, DispatcherPriority priority)
        {
            dispatcher.Invoke(callback, priority, NoArgs);
        }

        public static TResult Invoke<TResult>(this Dispatcher dispatcher, Func<TResult> callback)
        {
            return (TResult)dispatcher.Invoke(callback, NoArgs);
        }

        public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Action callback)
        {
            return dispatcher.BeginInvoke(callback, NoArgs);
        }

        public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Action callback, DispatcherPriority priority)
        {
            return dispatcher.BeginInvoke(callback, priority, NoArgs);
        }
    }
}
