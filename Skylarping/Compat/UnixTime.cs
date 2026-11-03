/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// .NET 4.0 compatibility: DateTimeOffset's Unix-time helpers arrived in .NET 4.6.
// Same results as the framework versions (UTC, truncating toward the epoch's past).
/*==========================================================*/

using System;

namespace Skylarping
{
    public static class UnixTime
    {
        private const long UnixEpochTicks = 621355968000000000L; // 1970-01-01T00:00:00Z
        private const long UnixEpochSeconds = UnixEpochTicks / TimeSpan.TicksPerSecond;
        private const long UnixEpochMilliseconds = UnixEpochTicks / TimeSpan.TicksPerMillisecond;

        public static DateTimeOffset FromUnixTimeSeconds(long seconds)
        {
            return new DateTimeOffset(UnixEpochTicks + seconds * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        }

        public static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
        {
            return new DateTimeOffset(UnixEpochTicks + milliseconds * TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        public static long ToUnixTimeSeconds(this DateTimeOffset value)
        {
            return value.UtcDateTime.Ticks / TimeSpan.TicksPerSecond - UnixEpochSeconds;
        }

        public static long ToUnixTimeMilliseconds(this DateTimeOffset value)
        {
            return value.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond - UnixEpochMilliseconds;
        }
    }
}
