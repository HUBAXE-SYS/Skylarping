/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// "Show me as Away when I've been inactive for N minutes" (Options > General).
// Polls the system-wide idle time (GetLastInputInfo, works on XP). Only an Online user is
// switched to Away, and only that automatic Away is undone on the next input; a status the
// user picks by hand always wins. Automatic changes are not remembered for the next sign-in.
// Never kicks in during a call (you may be talking without touching the keyboard).
/*==========================================================*/

using Skylarping.Models;
using Skylarping.Preferences;
using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Skylarping.Helpers
{
    public static class AutoAway
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private static DispatcherTimer _timer;
        private static Func<bool> _isInCall;
        private static bool _wentAway; // we set Away ourselves and will set Online back
        private static bool _busy;

        public static void Start(Func<bool> isInCall)
        {
            Stop();
            _isInCall = isInCall;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += Tick;
            _timer.Start();
        }

        public static void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Tick;
                _timer = null;
            }
            _wentAway = false;
        }

        /// <summary>Milliseconds since the last keyboard or mouse input anywhere in the session.</summary>
        public static uint IdleMilliseconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info))
                return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime); // wraps correctly after 49.7 days
        }

        private static async void Tick(object sender, EventArgs e)
        {
            User me = Universal.CurrentUser;
            var backend = Universal.Backend;
            if (_busy || me == null || backend == null)
                return;

            _busy = true;
            try
            {
                uint idle = IdleMilliseconds();
                int minutes = Math.Max(1, Settings.AutoAwayMinutes);
                bool idleLongEnough = idle >= (uint)minutes * 60000u;

                if (_wentAway)
                {
                    if (me.ConnectionStatus != PresenceStatus.Away)
                        _wentAway = false; // the user picked another status: leave it alone
                    else if (!idleLongEnough || !Settings.AutoAway)
                    {
                        _wentAway = false;
                        await backend.SetConnectionStatus(PresenceStatus.Online, remember: false);
                    }
                }
                else if (Settings.AutoAway
                    && idleLongEnough
                    && me.ConnectionStatus == PresenceStatus.Online
                    && !(_isInCall?.Invoke() ?? false))
                {
                    if (await backend.SetConnectionStatus(PresenceStatus.Away, remember: false))
                        _wentAway = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AutoAway: " + ex);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
