/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// A log of one call's audio, for finding echo trouble (Options > Audio settings). Off, it
// costs nothing. On, it writes a line a second from the microphone thread, so writes must
// stay quick; a write that fails turns the log off for the rest of the call.
/*==========================================================*/

using System;
using System.Globalization;
using System.IO;
using Skylarping.Preferences;

namespace Skylarping.Audio
{
    public static class CallLog
    {
        private static readonly object Lock = new object();
        private static StreamWriter _writer;
        private static int _started;

        /// <summary>Where the logs go: the logs folder next to the profiles.</summary>
        public static string Folder
        {
            get
            {
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Universal.NAME), "logs");
            }
        }

        public static bool Enabled
        {
            get { lock (Lock) return _writer != null; }
        }

        /// <summary>Opens a new log for a call, if the setting is on. Never throws.</summary>
        public static void Start()
        {
            if (!Settings.CallLog)
                return;
            lock (Lock)
            {
                Stop();
                try
                {
                    Directory.CreateDirectory(Folder);
                    string path = Path.Combine(Folder, "call-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                    _writer = new StreamWriter(path, false) { AutoFlush = true };
                    _started = Environment.TickCount;
                    _writer.WriteLine("Skylarping call log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + " on Windows " + Environment.OSVersion.Version + " (.NET " + Environment.Version + ")");
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }

        /// <summary>Writes one line, stamped with the milliseconds since the call started.</summary>
        public static void Write(string line)
        {
            lock (Lock)
            {
                if (_writer == null)
                    return;
                try
                {
                    _writer.WriteLine(((Environment.TickCount - _started) / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "  " + line);
                }
                catch (Exception)
                {
                    try { _writer.Dispose(); } catch { }
                    _writer = null;
                }
            }
        }

        public static void Stop()
        {
            lock (Lock)
            {
                if (_writer == null)
                    return;
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
        }
    }
}
