using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// One star system as a self-contained world: its own bodies (body 0 is its root, the Sun or a
    /// barycentre), ephemeris, hyperlanes, fleet, markets and wars. Every system runs on the shared
    /// clock, whether or not it is the one being looked at.
    /// </summary>
    public sealed class StarSystem
    {
        public string Name { get; }

        /// <summary>Position of the system's root relative to the Sun, light-years (J2000 ecliptic axes, as inside every system).</summary>
        public DVec3 PositionLy { get; }

        public BodyCatalog Catalog { get; }
        public Ephemeris Ephemeris { get; }
        public World World { get; }
        public Hyperlanes Lanes => World.Fleet.Lanes;

        /// <summary>This system's own jump gate, paired with another system's (see <see cref="Galaxy.CreateDefault"/>); null until paired.</summary>
        public JumpGate Gate { get; internal set; }

        StarSystem(string name, DVec3 positionLy, Ephemeris ephemeris, World world)
        {
            Name = name;
            PositionLy = positionLy;
            Catalog = ephemeris.Catalog;
            Ephemeris = ephemeris;
            World = world;
        }

        public void Update(double jd)
        {
            Ephemeris.Update(jd);
            World.Update(jd);
        }

        public static StarSystem CreateSol(double jd)
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(jd);
            return new StarSystem("Sol System", DVec3.Zero, eph, World.CreateDefault(eph, jd));
        }

        public static StarSystem CreateAlphaCentauri(double jd)
        {
            var eph = new Ephemeris(BodyCatalog.CreateAlphaCentauri());
            eph.Update(jd);
            return new StarSystem("Alpha Centauri", AlphaCentauri.PositionLy, eph, World.CreateAlphaCentauri(eph, jd));
        }

        public override string ToString() => Name;
    }

    /// <summary>A real star near the Sun, for the interstellar view (not simulated).</summary>
    public readonly struct NearbyStar
    {
        public readonly string Name;
        public readonly DVec3 PositionLy;
        public readonly string SpectralType;
        public readonly double ApparentMagnitude;
        public readonly Rgb Color;

        public NearbyStar(string name, double raDeg, double decDeg, double distanceLy, string spectralType, double magnitude, double teffK)
        {
            Name = name;
            PositionLy = Astro.DirectionFromRaDec(raDeg, decDeg) * distanceLy;
            SpectralType = spectralType;
            ApparentMagnitude = magnitude;
            Color = AlphaCentauri.StarColor(teffK);
        }

        /// <summary>Absolute magnitude (as seen from 10 parsecs), from the apparent one and the distance; NaN when unknown.</summary>
        public double AbsoluteMagnitude => double.IsNaN(ApparentMagnitude) ? double.NaN
            : ApparentMagnitude - 5 * (Math.Log10(PositionLy.Length / 3.261563777) - 1);
    }

    /// <summary>
    /// The neighbourhood: the simulated star systems at their real positions, plus the nearest real
    /// stars around them for the view between systems. The seed of a larger map.
    /// </summary>
    public sealed class Galaxy
    {
        public readonly List<StarSystem> Systems = new List<StarSystem>();
        public readonly List<NearbyStar> NearbyStars = new List<NearbyStar>();

        public void Update(double jd)
        {
            foreach (var s in Systems) s.Update(jd);
        }

        public StarSystem Find(string name)
        {
            foreach (var s in Systems)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>Unit direction (J2000 ecliptic) from one system to another.</summary>
        public static DVec3 Direction(StarSystem from, StarSystem to) => (to.PositionLy - from.PositionLy).Normalized;

        public static double DistanceLy(StarSystem from, StarSystem to) => (to.PositionLy - from.PositionLy).Length;

        public static Galaxy CreateDefault(double jd)
        {
            var g = new Galaxy();
            var sol = StarSystem.CreateSol(jd);
            var alphaCentauri = StarSystem.CreateAlphaCentauri(jd);
            g.Systems.Add(sol);
            g.Systems.Add(alphaCentauri);
            g.NearbyStars.AddRange(Neighbours());
            LinkGates(sol, alphaCentauri);
            return g;
        }

        /// <summary>Gives each of the two simulated systems a jump gate facing the other, at its own
        /// outer edge (see <see cref="JumpGate.RadiusKm"/>), and pairs them.</summary>
        static void LinkGates(StarSystem sol, StarSystem alphaCentauri)
        {
            sol.Gate = new JumpGate("Centauri Gate", Direction(sol, alphaCentauri) * JumpGate.RadiusKm);
            alphaCentauri.Gate = new JumpGate("Sol Gate", Direction(alphaCentauri, sol) * JumpGate.RadiusKm);
            sol.Gate.Destination = alphaCentauri;
            alphaCentauri.Gate.Destination = sol;
        }

        /// <summary>
        /// The nearest stars beyond Alpha Centauri (Wikipedia "List of nearest stars", from RECONS and
        /// Gaia DR3): name, right ascension, declination, distance (ly), spectral type, visual magnitude
        /// (NaN for brown dwarfs too faint to list), and a photosphere temperature for the colour.
        /// </summary>
        public static IEnumerable<NearbyStar> Neighbours()
        {
            NearbyStar S(string name, double rh, double rm, double rs, double dd, double dm, double ds, bool south, double ly, string type, double mag, double teff) =>
                new NearbyStar(name, Astro.Hms(rh, rm, rs), Astro.Dms(dd, dm, ds, south), ly, type, mag, teff);

            yield return S("Barnard's Star", 17, 57, 48.5, 4, 41, 36, false, 5.9629, "M4.0Ve", 9.53, 3195);
            yield return S("Luhman 16", 10, 49, 18.9, 53, 19, 10, true, 6.5102, "L8 + T1", double.NaN, 1300);
            yield return S("WISE 0855−0714", 8, 55, 10.8, 7, 14, 43, true, 7.430, "Y4", double.NaN, 285);
            yield return S("Wolf 359", 10, 56, 29.2, 7, 0, 53, false, 7.856, "M6.0V", 13.44, 2800);
            yield return S("Lalande 21185", 11, 3, 20.2, 35, 58, 12, false, 8.3044, "M2.0V", 7.47, 3550);
            yield return S("Sirius", 6, 45, 8.9, 16, 42, 58, true, 8.7094, "A1V + DA2", -1.46, 9940);
            yield return S("Gliese 65", 1, 39, 1.3, 17, 57, 1, true, 8.770, "M5.5Ve", 12.54, 2840);
            yield return S("Ross 154", 18, 49, 49.4, 23, 50, 10, true, 9.7063, "M3.5Ve", 10.43, 3250);
            yield return S("Ross 248", 23, 41, 54.7, 44, 10, 30, false, 10.3057, "M5.5Ve", 12.29, 2800);
            yield return S("Epsilon Eridani", 3, 32, 55.8, 9, 27, 30, true, 10.4749, "K2V", 3.73, 5084);
            yield return S("Gliese 887", 23, 5, 52.0, 35, 51, 11, true, 10.7241, "M0.5V", 7.34, 3690);
            yield return S("Ross 128", 11, 47, 44.4, 0, 48, 16, false, 11.0074, "M4.0Vn", 11.13, 3190);
            yield return S("EZ Aquarii", 22, 38, 33.4, 15, 17, 57, true, 11.109, "M5.0Ve", 13.33, 2900);
            yield return S("61 Cygni", 21, 6, 53.9, 38, 44, 58, false, 11.4039, "K5.0V + K7.0V", 5.21, 4526);
            yield return S("Procyon", 7, 39, 18.1, 5, 13, 30, false, 11.463, "F5IV–V + DQZ", 0.38, 6530);
            yield return S("Struve 2398", 18, 42, 46.7, 59, 37, 49, false, 11.4908, "M3.0V + M3.5V", 8.90, 3400);
            yield return S("Groombridge 34", 0, 18, 22.9, 44, 1, 23, false, 11.6191, "M1.5V + M3.5V", 8.08, 3700);
            yield return S("DX Cancri", 8, 29, 49.5, 26, 46, 37, false, 11.6797, "M6.5Ve", 14.78, 2700);
            yield return S("Epsilon Indi", 22, 3, 21.7, 56, 47, 10, true, 11.8670, "K5Ve + T1 + T6", 4.69, 4630);
            yield return S("Tau Ceti", 1, 44, 4.1, 15, 56, 15, true, 11.9118, "G8.5Vp", 3.49, 5344);
            yield return S("GJ 1061", 3, 35, 59.7, 44, 30, 45, true, 11.9839, "M5.5V", 13.09, 2950);
        }
    }
}
