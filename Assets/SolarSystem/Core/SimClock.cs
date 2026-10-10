using System;

namespace SolarSystem.Core
{
    /// <summary>Simulation time as a Julian date, advanced by real time multiplied by a warp level.</summary>
    public sealed class SimClock
    {
        public readonly struct WarpLevel
        {
            public readonly double SimSecondsPerSecond;
            public readonly string Label;

            public WarpLevel(double rate, string label)
            {
                SimSecondsPerSecond = rate;
                Label = label;
            }
        }

        public static readonly WarpLevel[] Levels =
        {
            new WarpLevel(1, "real time"),
            new WarpLevel(10, "10 s/s"),
            new WarpLevel(60, "1 min/s"),
            new WarpLevel(600, "10 min/s"),
            new WarpLevel(3600, "1 hour/s"),
            new WarpLevel(6 * 3600, "6 hours/s"),
            new WarpLevel(86400, "1 day/s"),
            new WarpLevel(7 * 86400, "1 week/s"),
            new WarpLevel(30 * 86400, "1 month/s"),
            new WarpLevel(365.25 * 86400, "1 year/s"),
        };

        public const int DefaultWarpIndex = 4;

        public double Jd { get; private set; }
        public int WarpIndex { get; private set; } = DefaultWarpIndex;
        public bool Paused { get; set; }

        public WarpLevel Warp => Levels[WarpIndex];
        public DateTime Utc => Astro.ToDateTime(Jd);

        public SimClock(double startJd) => Jd = startJd;

        public static SimClock StartingNow() => new SimClock(Astro.JulianDate(DateTime.UtcNow));

        public void SetWarp(int index) => WarpIndex = Math.Max(0, Math.Min(Levels.Length - 1, index));
        public void Faster() => SetWarp(WarpIndex + 1);
        public void Slower() => SetWarp(WarpIndex - 1);
        public void SetJd(double jd) => Jd = jd;

        /// <summary>Advances by real seconds; returns the simulated days elapsed.</summary>
        public double Advance(double realSeconds)
        {
            if (Paused || realSeconds <= 0) return 0;
            double days = realSeconds * Warp.SimSecondsPerSecond / Astro.SecondsPerDay;
            Jd += days;
            return days;
        }
    }
}
