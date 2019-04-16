using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Diagnostics;

namespace Microsoft.Automata.Utilities
{
    /// <summary>
    /// High precision timer -- Modified to use Stopwatch (lowering the precision?) so it will run on Wine
    /// </summary>
    internal static class HighTimer
    {
        private readonly static long frequency;
        static HighTimer()
        {
            frequency = Stopwatch.Frequency;
        }

        /// <summary>
        /// Gets the frequency.
        /// </summary>
        /// <value>The frequency.</value>
        public static long Frequency
        {
            get { return frequency; }
        }

        /// <summary>
        /// Gets the current ticks value.
        /// </summary>
        /// <value>The now.</value>
        public static long Now
        {
            get
            {
                return Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Returns the duration of the timer (in seconds)
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public static double ToSeconds(long start, long end)
        {
            return (end - start) / (double)frequency;
        }

        /// <summary>
        ///Returns the duration in seconds
        /// </summary>
        /// <param name="ticks">The ticks.</param>
        /// <returns></returns>
        public static double ToSeconds(long ticks)
        {
            return ticks / (double)frequency;
        }

        /// <summary>
        ///Returns the duration in seconds from <paramref name="start"/>
        /// </summary>
        /// <param name="start">The start.</param>
        /// <returns></returns>
        public static double ToSecondsFromNow(long start)
        {
            return ToSeconds(start, HighTimer.Now);
        }
    }
}
