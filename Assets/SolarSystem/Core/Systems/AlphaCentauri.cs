using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// The Alpha Centauri system, the Sun's nearest neighbour: the binary Alpha Centauri A and B
    /// circling their barycentre every 80 years, the red dwarf Proxima Centauri 13,000 AU away on a
    /// half-million-year orbit around the pair, and the planets found so far. All positions are in
    /// the same J2000 ecliptic axes as the Sol system, with the A-B barycentre at the origin.
    ///
    /// Sources (see Core/Systems/SOURCES.md):
    ///  - A-B orbit, masses, distance: Akeson et al. 2021, AJ 162, 14 (arXiv 2104.10086).
    ///  - Radii, luminosities, temperatures, rotation: Wikipedia "Alpha Centauri", "Proxima Centauri"
    ///    and the papers they cite.
    ///  - Proxima's orbit around A-B: Kervella, Thévenin and Lovis 2017, A&amp;A 598, L7 (arXiv 1611.03495).
    ///  - Proxima b (Anglada-Escudé et al. 2016), d (Faria et al. 2022, confirmed 2025), c (Damasso et
    ///    al. 2020, disputed).
    ///  - Alpha Cen A "S1": Beichman et al. 2025, ApJL (JWST/MIRI, August 2024), a candidate not yet re-detected.
    ///
    /// What is not known is assumed and said so in each summary: the planets' orbital planes and
    /// phases (only periods, separations and minimum masses are measured), the stars' spin poles,
    /// and the orientation of Proxima's orbital plane. The stations are game-layer fiction.
    /// </summary>
    public static class AlphaCentauri
    {
        public const double SunGm = 1.32712440018e11;
        const double SunRadiusKm = 695700;
        const double EarthGm = 398600.435;
        const double EarthRadiusKm = 6371.0;
        const double JupiterRadiusKm = 71492;

        public const double MassA = 1.0788, MassB = 0.9092, MassProxima = 0.1221;

        /// <summary>Distance of the A-B pair (light-years, Akeson et al. 2021) and of Proxima (light-years).</summary>
        public const double DistanceLy = 4.344, ProximaDistanceLy = 4.2465;

        /// <summary>Alpha Centauri A's position on the sky (ICRS J2000).</summary>
        public static readonly double RaDeg = Astro.Hms(14, 39, 36.494), DecDeg = Astro.Dms(60, 50, 2.37, south: true);

        /// <summary>Proxima Centauri's position on the sky (ICRS J2000).</summary>
        public static readonly double ProximaRaDeg = Astro.Hms(14, 29, 42.946), ProximaDecDeg = Astro.Dms(62, 40, 46.16, south: true);

        // A-B relative orbit (Akeson et al. 2021): Campbell elements of B about A.
        const double OrbitPeriodYears = 79.762;
        const double OrbitAAu = 23.299; // 17.493" at 1.3319 pc
        const double OrbitE = 0.51947;
        const double OrbitIDeg = 79.243;
        const double OrbitNodeDeg = 205.073;
        const double OrbitArgPeriDeg = 231.519;
        const double PeriastronYear = 1875.66;

        /// <summary>Unit vector (ecliptic J2000) from the Sun toward the A-B pair.</summary>
        public static DVec3 Direction => Astro.DirectionFromRaDec(RaDeg, DecDeg);

        /// <summary>Position of the A-B pair relative to the Sun, light-years (ecliptic J2000).</summary>
        public static DVec3 PositionLy => Direction * DistanceLy;

        /// <summary>Proxima's position relative to the A-B pair today, km (ecliptic J2000), from the two stars' sky positions and distances.</summary>
        public static DVec3 ProximaOffsetKm =>
            (Astro.DirectionFromRaDec(ProximaRaDeg, ProximaDecDeg) * ProximaDistanceLy - PositionLy) * Astro.LightYearKm;

        /// <summary>The normal of the A-B orbit (ecliptic J2000), also used as the assumed pole of the stars and of Proxima's planets.</summary>
        public static DVec3 BinaryNormal
        {
            get
            {
                OrbitalElements.VisualBinaryGeometry(RaDeg, DecDeg, OrbitIDeg, OrbitNodeDeg, OrbitArgPeriDeg, out _, out var normal);
                return normal;
            }
        }

        public static IEnumerable<BodyDefinition> Definitions()
        {
            double epoch = Astro.J2000;
            OrbitalElements.VisualBinaryGeometry(RaDeg, DecDeg, OrbitIDeg, OrbitNodeDeg, OrbitArgPeriDeg, out var abPeri, out var abNormal);
            double abPeriod = OrbitPeriodYears * 365.25;
            double tPeri = Astro.J2000 + (PeriastronYear - 2000) * 365.25;
            double abMean = Astro.WrapDegrees360((epoch - tPeri) / abPeriod * 360);
            var abOrbit = OrbitalElements.FromVectors(epoch, OrbitAAu * Astro.AuKm, OrbitE, abPeri, abNormal, abMean, abPeriod);
            double massAB = MassA + MassB;
            Pole(abNormal, out double poleRa, out double poleDec);

            yield return new BodyDefinition
            {
                Name = "Alpha Centauri", Kind = BodyKind.Barycentre, RadiusKm = 0, Gm = SunGm * massAB,
                Summary = "The centre of mass that Alpha Centauri A and B circle every 79.8 years.",
            };
            yield return new BodyDefinition
            {
                Name = "Alpha Centauri A", Kind = BodyKind.Star, ParentName = "Alpha Centauri",
                RadiusKm = 1.223 * SunRadiusKm, Gm = SunGm * MassA, Elements = abOrbit, OrbitScale = -MassB / massAB,
                PoleRaDeg = poleRa, PoleDecDeg = poleDec, RotationHours = 28.3 * 24,
                TeffK = 5748, LuminositySolar = 1.5059, Color = StarColor(5748),
                Summary = "Rigil Kentaurus. G2V, 1.08 solar masses, half again as luminous as the Sun. "
                          + "Pole assumed along the A-B orbit's.",
            };
            yield return new BodyDefinition
            {
                Name = "Alpha Centauri B", Kind = BodyKind.Star, ParentName = "Alpha Centauri",
                RadiusKm = 0.864 * SunRadiusKm, Gm = SunGm * MassB, Elements = abOrbit, OrbitScale = MassA / massAB,
                PoleRaDeg = poleRa, PoleDecDeg = poleDec, RotationHours = 36.7 * 24,
                TeffK = 5154, LuminositySolar = 0.4981, Color = StarColor(5154),
                Summary = "Toliman. K1V, an orange dwarf. Swings between 11.2 and 35.4 AU from A on an 80-year orbit. "
                          + "No planet confirmed (the 2012 \"Bb\" was refuted).",
            };

            // Proxima: on a 0.5-eccentricity orbit about the pair, now near apastron (Kervella et al. 2017:
            // P ≈ 550 kyr, periastron in ~280 kyr). Its present offset from A-B comes from the two sky
            // positions and distances; the orbit is placed so it passes there at mean anomaly 174°.
            // The orbital plane's orientation is assumed (the A-B plane, tipped to contain the offset).
            var offset = ProximaOffsetKm;
            var proxNormal = (abNormal - offset.Normalized * DVec3.Dot(abNormal, offset.Normalized)).Normalized;
            const double proxE = 0.50, proxMean = 173.7;
            double ecc = Kepler.SolveEccentricAnomaly(proxMean * Astro.Deg2Rad, proxE);
            double proxA = offset.Length / (1 - proxE * Math.Cos(ecc));
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + proxE) * Math.Sin(ecc / 2), Math.Sqrt(1 - proxE) * Math.Cos(ecc / 2));
            var u = offset.Normalized;
            var proxPeri = u * Math.Cos(nu) - DVec3.Cross(proxNormal, u) * Math.Sin(nu);
            double proxPeriod = OrbitalElements.PeriodFromGm(proxA, SunGm * (massAB + MassProxima));
            yield return new BodyDefinition
            {
                Name = "Proxima Centauri", Kind = BodyKind.Star, ParentName = "Alpha Centauri",
                RadiusKm = 0.1542 * SunRadiusKm, Gm = SunGm * MassProxima,
                Elements = OrbitalElements.FromVectors(epoch, proxA, proxE, proxPeri, proxNormal, proxMean, proxPeriod),
                PoleRaDeg = poleRa, PoleDecDeg = poleDec, RotationHours = 84.9 * 24,
                TeffK = 2992, LuminositySolar = 0.001567, Color = StarColor(2992),
                Summary = "The nearest star to the Sun: an M5.5 red dwarf flare star, 4.25 light-years away. "
                          + "Orbits A and B every ~500,000 years; its orbital plane is assumed.",
            };

            // Proxima's planets: periods and separations are measured; the plane (taken as the A-B
            // plane), eccentricities near zero and phases are assumptions.
            var plane = abNormal;
            Pole(plane, out double planeRa, out double planeDec);
            yield return Planet("Proxima d", "Proxima Centauri", plane, 0.02881, 0.0, 5.12338, 40, 0.81 * EarthRadiusKm, 0.26 * EarthGm,
                new Rgb(0.55f, 0.42f, 0.36f), planeRa, planeDec, tidallyLocked: true, BodyStatus.Confirmed,
                "Smallest known planet of Proxima, a quarter of Earth's mass at least, scorched on a 5.1-day orbit. "
                + "Confirmed in 2025. Radius estimated; orbital plane and phase assumed.");
            var b = Planet("Proxima b", "Proxima Centauri", plane, 0.04848, 0.02, 11.18465, 210, 1.07 * EarthRadiusKm, 1.07 * EarthGm,
                new Rgb(0.5f, 0.45f, 0.42f), planeRa, planeDec, tidallyLocked: true, BodyStatus.Confirmed,
                "In Proxima's habitable zone, 1.07 Earth masses at least, probably tidally locked: one face in endless day. "
                + "Discovered 2016. Radius estimated, atmosphere unknown (drawn thin); orbital plane and phase assumed.");
            b.AtmosphereColor = new Rgb(0.75f, 0.55f, 0.5f);
            b.AtmosphereStrength = 0.3f;
            yield return b;
            var c = Planet("Proxima c", "Proxima Centauri", plane, 1.489, 0.04, 1928, 95, 1.6 * EarthRadiusKm, 7 * EarthGm,
                new Rgb(0.72f, 0.76f, 0.8f), planeRa, planeDec, tidallyLocked: false, BodyStatus.Disputed,
                "DISPUTED. A cold super-Earth claimed in 2020 on a 5.3-year orbit; later data have not confirmed it. "
                + "Radius guessed from its 7 Earth masses.");
            c.RotationHours = 30;
            yield return c;

            // The JWST candidate around A, inclined to the A-B plane (Beichman et al. 2025: a ≈ 1-2 AU,
            // e ≈ 0.4, inclined orbit; one of the allowed solutions is used).
            var tilt = DVec3.Cross(abNormal, abPeri).Normalized;
            var s1Normal = (abNormal * Math.Cos(50 * Astro.Deg2Rad) + tilt * Math.Sin(50 * Astro.Deg2Rad)).Normalized;
            Pole(s1Normal, out double s1Ra, out double s1Dec);
            var s1 = Planet("Alpha Centauri Ab", "Alpha Centauri A", s1Normal, 1.9, 0.4, OrbitalElements.PeriodFromGm(1.9 * Astro.AuKm, SunGm * MassA),
                300, 1.05 * JupiterRadiusKm, 120 * EarthGm, new Rgb(0.78f, 0.72f, 0.62f), s1Ra, s1Dec, tidallyLocked: false, BodyStatus.Candidate,
                "CANDIDATE \"S1\": a Saturn-mass gas giant glimpsed by JWST in August 2024 in A's habitable zone, "
                + "not yet seen again. Orbit is one of the solutions allowed (a 1.9 AU, e 0.4, tilted 50°).");
            s1.GasGiant = true;
            s1.RotationHours = 10.5;
            s1.AtmosphereColor = new Rgb(0.85f, 0.78f, 0.65f);
            s1.AtmosphereStrength = 0.3f;
            yield return s1;

            // Game layer: free-flying stations on their own orbits about each star.
            yield return Station("Rigil Gate", "Alpha Centauri A", abNormal, abPeri, 1.2, 20,
                "Fiction. The Toliman Combine's trade hub in A's habitable zone, where the long lanes meet.");
            yield return Station("Toliman Forge", "Alpha Centauri B", abNormal, abPeri, 0.7, 140,
                "Fiction. Orbital foundry and shipyard of the Toliman Combine, in B's habitable zone.");
            yield return Station("Blackhollow", "Proxima Centauri", abNormal, abPeri, 0.5, 260,
                "Fiction. A derelict survey station the Long Dark took over, dark against a dim star.");
        }

        static BodyDefinition Planet(string name, string parent, DVec3 normal, double aAu, double e, double periodDays, double meanDeg,
            double radiusKm, double gm, Rgb color, double poleRa, double poleDec, bool tidallyLocked, BodyStatus status, string summary)
        {
            // Periapsis direction: any direction in the plane (assumed), turned by the phase choice.
            var reference = Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var peri = DVec3.Cross(reference, normal).Normalized;
            return new BodyDefinition
            {
                Name = name, Kind = BodyKind.Planet, ParentName = parent, RadiusKm = radiusKm, Gm = gm,
                Elements = OrbitalElements.FromVectors(Astro.J2000, aAu * Astro.AuKm, e, peri, normal, meanDeg, periodDays),
                PoleRaDeg = poleRa, PoleDecDeg = poleDec,
                RotationHours = tidallyLocked ? periodDays * 24 : 24,
                FacesParent = tidallyLocked,
                Color = color,
                Status = status,
                Summary = summary,
            };
        }

        static BodyDefinition Station(string name, string parent, DVec3 normal, DVec3 reference, double aAu, double meanDeg, string summary)
        {
            double gm = name == "Rigil Gate" ? SunGm * MassA : name == "Toliman Forge" ? SunGm * MassB : SunGm * MassProxima;
            Pole(normal, out double ra, out double dec);
            return new BodyDefinition
            {
                Name = name, Kind = BodyKind.Station, ParentName = parent, RadiusKm = 15, Gm = 1e-7,
                Elements = OrbitalElements.FromVectors(Astro.J2000, aAu * Astro.AuKm, 0, reference, normal, meanDeg,
                    OrbitalElements.PeriodFromGm(aAu * Astro.AuKm, gm)),
                PoleRaDeg = ra, PoleDecDeg = dec, RotationHours = 2,
                Color = new Rgb(0.75f, 0.76f, 0.8f),
                Status = BodyStatus.Fictional,
                Summary = summary,
            };
        }

        /// <summary>The equatorial RA/Dec of an ecliptic direction, as a pole for the catalogue.</summary>
        static void Pole(DVec3 ecliptic, out double raDeg, out double decDeg) => Astro.RaDecOf(ecliptic, out raDeg, out decDeg);

        /// <summary>
        /// Approximate visible colour of a blackbody photosphere (normalised so the brightest channel
        /// is 1), from a fit to the CIE colour of Planck spectra (Tanner Helland's approximation).
        /// </summary>
        public static Rgb StarColor(double teffK)
        {
            double t = teffK / 100;
            double r, g, b;
            if (t <= 66)
            {
                r = 255;
                g = 99.4708025861 * Math.Log(t) - 161.1195681661;
                b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            }
            else
            {
                r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
                g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
                b = 255;
            }
            float C(double v) => (float)(Math.Max(0, Math.Min(255, v)) / 255);
            return new Rgb(C(r), C(g), C(b));
        }
    }
}
