using System;
using System.Globalization;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>
    /// How the LOG prints numbers. Values are not localized (a speed is a
    /// speed in every language), so the formats live beside the data rather
    /// than in the menu text library; every one uses the invariant culture,
    /// the way the mission brief prints its reward.
    /// </summary>
    public static class StatFormat
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Total play time as DD:HH:MM:SS.</summary>
        public static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return string.Format(Inv, "{0:00}:{1:00}:{2:00}:{3:00}", (int)t.TotalDays, t.Hours, t.Minutes, t.Seconds);
        }

        /// <summary>A lap-style time as mm:ss.ff, or a dash when there is no record yet.</summary>
        public static string Lap(float seconds)
        {
            if (seconds <= 0f) return "—";
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format(Inv, "{0:00}:{1:00}.{2:00}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds / 10);
        }

        public static string Kmh(float kmh) => string.Format(Inv, "{0:0} KM/H", kmh);

        public static string Meters(float meters) => string.Format(Inv, "{0:0.0} M", meters);

        public static string Money(long amount) => "$" + amount.ToString("N0", Inv);

        public static string Count(int count) => count.ToString(Inv);

        /// <summary>"done / total".</summary>
        public static string Ratio(int done, int total) => string.Format(Inv, "{0} / {1}", done, total);
    }
}
