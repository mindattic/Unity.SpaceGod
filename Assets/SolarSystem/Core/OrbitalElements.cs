using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// Keplerian elements with linear secular rates, in the layout of JPL's "Keplerian Elements
    /// for Approximate Positions of the Major Planets" (Standish): a, e, i, mean longitude L,
    /// longitude of perihelion ϖ, longitude of ascending node Ω, each with a per-century rate.
    /// Every body uses this one shape; moons and minor planets are converted into it.
    /// </summary>
    public sealed class OrbitalElements
    {
        public double EpochJd { get; private set; }
        public double AKm { get; private set; }
        public double ADotKmPerCentury { get; private set; }
        public double E { get; private set; }
        public double EDot { get; private set; }
        public double IDeg { get; private set; }
        public double IDot { get; private set; }
        public double LDeg { get; private set; }
        public double LDotDegPerCentury { get; private set; }
        public double VarpiDeg { get; private set; }
        public double VarpiDot { get; private set; }
        public double NodeDeg { get; private set; }
        public double NodeDot { get; private set; }

        OrbitalElements() { }

        /// <summary>JPL planet table entry (J2000 epoch, a in AU, rates per Julian century).</summary>
        public static OrbitalElements FromJpl(
            double aAu, double e, double iDeg, double lDeg, double varpiDeg, double nodeDeg,
            double aDotAu, double eDot, double iDot, double lDot, double varpiDot, double nodeDot)
        {
            return new OrbitalElements
            {
                EpochJd = Astro.J2000,
                AKm = aAu * Astro.AuKm,
                ADotKmPerCentury = aDotAu * Astro.AuKm,
                E = e,
                EDot = eDot,
                IDeg = iDeg,
                IDot = iDot,
                LDeg = lDeg,
                LDotDegPerCentury = lDot,
                VarpiDeg = varpiDeg,
                VarpiDot = varpiDot,
                NodeDeg = nodeDeg,
                NodeDot = nodeDot,
            };
        }

        /// <summary>
        /// Classical elements at an epoch (SBDB / satellite tables), with optional nodal and
        /// apsidal precession in degrees per day.
        /// </summary>
        public static OrbitalElements FromClassical(
            double epochJd, double aKm, double e, double iDeg, double nodeDeg, double argPeriDeg,
            double meanAnomalyDeg, double periodDays,
            double nodeRateDegPerDay = 0, double varpiRateDegPerDay = 0)
        {
            double varpi = nodeDeg + argPeriDeg;
            return new OrbitalElements
            {
                EpochJd = epochJd,
                AKm = aKm,
                E = e,
                IDeg = iDeg,
                LDeg = meanAnomalyDeg + varpi,
                LDotDegPerCentury = 360.0 / periodDays * Astro.DaysPerCentury,
                VarpiDeg = varpi,
                VarpiDot = varpiRateDegPerDay * Astro.DaysPerCentury,
                NodeDeg = nodeDeg,
                NodeDot = nodeRateDegPerDay * Astro.DaysPerCentury,
            };
        }

        /// <summary>Kepler's third law: period in days for semi-major axis a (km) around gm (km³/s²).</summary>
        public static double PeriodFromGm(double aKm, double gm) =>
            Astro.TwoPi * Math.Sqrt(aKm * aKm * aKm / gm) / Astro.SecondsPerDay;

        public KeplerState At(double jd)
        {
            double t = (jd - EpochJd) / Astro.DaysPerCentury;
            double a = AKm + ADotKmPerCentury * t;
            double e = Math.Max(0, Math.Min(0.99, E + EDot * t));
            double i = (IDeg + IDot * t) * Astro.Deg2Rad;
            double l = LDeg + LDotDegPerCentury * t;
            double varpi = VarpiDeg + VarpiDot * t;
            double node = NodeDeg + NodeDot * t;
            double m = Astro.WrapRadians((l - varpi) * Astro.Deg2Rad);
            double n = LDotDegPerCentury / Astro.DaysPerCentury * Astro.Deg2Rad;
            return new KeplerState(a, e, i, node * Astro.Deg2Rad, (varpi - node) * Astro.Deg2Rad, m, n);
        }
    }
}
