using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum StructureKind
    {
        /// <summary>An orbital shipyard: an open frame with the hull being built in its cradle.</summary>
        Shipyard,

        /// <summary>A factory station (foundry, fab, works, arsenal, labs) with its radiators.</summary>
        Factory,

        /// <summary>A spinning habitat ring for a sizeable settlement's orbital population.</summary>
        Habitat,

        /// <summary>A crewed station that really flies (the ISS, Tiangong).</summary>
        CrewedStation,

        /// <summary>A space telescope (Hubble, JWST).</summary>
        Telescope,

        /// <summary>A robotic orbiter (Mars Reconnaissance Orbiter, LRO...).</summary>
        Probe,
    }

    /// <summary>
    /// A permanent thing in orbit: game-layer industry made physical (a settlement's shipyard and
    /// factories, its habitat), or a real spacecraft. It holds a circular orbit about its body.
    /// </summary>
    public sealed class OrbitalStructure
    {
        public string Name;
        public StructureKind Kind;
        public int Body;
        public ParkingOrbit Orbit;
        public Faction Owner;
        public Settlement Settlement;
        public IndustryKind Industry;

        /// <summary>Real size (km), for drawing at the ship scale.</summary>
        public double LengthKm;

        /// <summary>Flies at the Sun-planet L2 point (JWST) instead of an orbit about the body.</summary>
        public bool AtL2;

        /// <summary>A real spacecraft (geometry from published orbits; phases illustrative).</summary>
        public bool Real;
        public string Note;

        public DVec3 HelioAt(Ephemeris eph, double jd)
        {
            if (AtL2)
            {
                // Sun-Earth L2 sits 1.5 million km beyond Earth on the Sun-Earth line (it moves with Earth).
                var planet = eph.HelioAt(Body, jd);
                return planet + planet.Normalized * 1.5e6;
            }
            return eph.HelioAt(Body, jd) + Orbit.OffsetAt(jd);
        }

        /// <summary>The same, from the ephemeris' current frame.</summary>
        public DVec3 Helio(Ephemeris eph)
        {
            if (AtL2)
            {
                var planet = eph.Helio(Body);
                return planet + planet.Normalized * 1.5e6;
            }
            return eph.Helio(Body) + Orbit.OffsetAt(eph.Jd);
        }

        /// <summary>A yard's progress on the hull in its cradle (0..1), or 0.</summary>
        public double SlipProgress =>
            Kind == StructureKind.Shipyard && Settlement?.OnSlip is ShipClass c ? Math.Min(1, Settlement.BuildPoints / Economy.HullCost(c)) : 0;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A satellite constellation drawn as a cloud of points: shells of circular orbits in evenly
    /// spaced planes (a Walker pattern). Plane counts, altitudes and inclinations are the real ones;
    /// where each satellite is along its orbit is illustrative (there are no live orbital elements).
    /// </summary>
    public sealed class Constellation
    {
        public string Name;
        public int Body;
        public Rgb Color;
        public readonly List<Shell> Shells = new List<Shell>();

        public readonly struct Shell
        {
            public readonly double RadiusKm, IncDeg;
            public readonly int Planes, PerPlane;

            public Shell(double radiusKm, double incDeg, int planes, int perPlane)
            {
                RadiusKm = radiusKm;
                IncDeg = incDeg;
                Planes = planes;
                PerPlane = perPlane;
            }
        }

        public int Count
        {
            get
            {
                int n = 0;
                foreach (var s in Shells) n += s.Planes * s.PerPlane;
                return n;
            }
        }

        /// <summary>Every satellite's offset from its body (km, ecliptic) at a date.</summary>
        public void Offsets(BodyDefinition body, double jd, DVec3[] output)
        {
            var axis = body.SpinAxis;
            var node0 = BodyOrientation.EquatorNode(axis);
            var east0 = DVec3.Cross(axis, node0);
            int i = 0;
            int shellIndex = 0;
            foreach (var s in Shells)
            {
                double rate = Kepler.CircularRate(body.Gm, s.RadiusKm);
                double inc = s.IncDeg * Astro.Deg2Rad;
                for (int p = 0; p < s.Planes; p++)
                {
                    double node = Astro.TwoPi * p / s.Planes + shellIndex * 0.37;
                    var n = node0 * Math.Cos(node) + east0 * Math.Sin(node);
                    var inPlane = DVec3.Cross(axis, n);
                    var v = inPlane * Math.Cos(inc) + axis * Math.Sin(inc);
                    for (int k = 0; k < s.PerPlane && i < output.Length; k++, i++)
                    {
                        double phase = Astro.TwoPi * (k + 0.5 * (p % 2)) / s.PerPlane + rate * (jd - Astro.J2000);
                        output[i] = (n * Math.Cos(phase) + v * Math.Sin(phase)) * s.RadiusKm;
                    }
                }
                shellIndex++;
            }
        }
    }

    /// <summary>
    /// A docking ring: the one way down to a populous world through its atmosphere. A powered
    /// station holds position directly over a city at the planet's stationary-orbit altitude (or three
    /// radii out where it has none), turning with the ground (fiction: it keeps station by thrust,
    /// since only the equator is truly geostationary). Ships queue at it, are cleared down one at a
    /// time, descend to the city, land, and climb back to it to leave.
    /// </summary>
    public sealed class DockingRing
    {
        public int Id;
        public string Name;
        public string City;
        public int Body;
        public double LatDeg, LonDeg;

        /// <summary>From the body's centre, km.</summary>
        public double RadiusKm;

        /// <summary>The body's radius and where its atmosphere starts (km above the surface).</summary>
        public double SurfaceKm, EntryInterfaceKm;

        /// <summary>One ship is cleared to descend per interval (days).</summary>
        public double EntryIntervalDays = 30.0 / 1440;

        /// <summary>When the next ship may begin its descent.</summary>
        public double NextEntryJd = double.MinValue;

        /// <summary>Hours from the ring to the ground (and back).</summary>
        public double DescentDays;

        public Settlement Settlement;
        public Faction Owner;

        /// <summary>How far out along the holding line arriving ships wait (km past the ring).</summary>
        public double HoldKm => Math.Max(RadiusKm * 0.05, SurfaceKm * 0.3);

        readonly BodyDefinition body;

        /// <summary>For a world that keeps one face to its parent: the ephemeris, to find that face.</summary>
        public Ephemeris Ephemeris;

        double lockedEpoch = double.NaN, lockedW0, lockedRate;

        public DockingRing(BodyDefinition body)
        {
            this.body = body;
            Body = body.Index;
            SurfaceKm = body.RadiusKm;
        }

        /// <summary>
        /// A body-fixed point over the ring's city at a distance from the body's centre, as a circle
        /// about the spin axis that turns with the planet (so flight planning can intercept it).
        /// <paramref name="lonOffsetDeg"/> moves it east along the holding line's parallel.
        /// </summary>
        public ParkingOrbit Point(double radiusKm, double lonOffsetDeg = 0)
        {
            var north = BodyOrientation.MapNorth(body);
            var node = BodyOrientation.EquatorNode(north);
            var east = DVec3.Cross(north, node);
            double lat = LatDeg * Astro.Deg2Rad;
            double w0 = double.IsNaN(body.PrimeMeridianDeg) ? 0 : body.PrimeMeridianDeg;
            double epoch = Astro.J2000;
            double rate = BodyOrientation.RateDegPerDay(body) * Astro.Deg2Rad;
            if (body.FacesParent && Ephemeris != null && body.ParentIndex >= 0)
            {
                // Longitude 0 faces the parent (as the surface is drawn). Its direction is taken once
                // and carried on at the mean orbital rate, so the point is the same whenever it is asked
                // for (a ship planned days ahead meets it exactly); the orbit's slight eccentricity swings
                // the true sub-parent point a degree or two either side.
                if (double.IsNaN(lockedEpoch))
                {
                    lockedEpoch = Ephemeris.Jd;
                    var toParent = -Ephemeris.Relative(body.Index);
                    var p = toParent - north * DVec3.Dot(toParent, north);
                    lockedW0 = Math.Atan2(DVec3.Dot(p, east), DVec3.Dot(p, node)) * Astro.Rad2Deg;
                    double sense = DVec3.Dot(DVec3.Cross(Ephemeris.Relative(body.Index), Ephemeris.RelativeVelocityKmS(body.Index)), north) >= 0 ? 1 : -1;
                    lockedRate = sense * Math.Abs(body.Elements.At(lockedEpoch).MeanMotion);
                }
                epoch = lockedEpoch;
                w0 = lockedW0;
                rate = lockedRate;
            }
            return new ParkingOrbit
            {
                Body = Body,
                Center = north * (radiusKm * Math.Sin(lat)),
                RadiusKm = radiusKm * Math.Cos(lat),
                U = node,
                V = east,
                EpochJd = epoch,
                PhaseAtEpoch = (w0 + LonDeg + lonOffsetDeg) * Astro.Deg2Rad,
                RateRadPerDay = rate,
                Fixed = true,
            };
        }

        /// <summary>The ring itself.</summary>
        public ParkingOrbit RingPoint => Point(RadiusKm);

        /// <summary>
        /// Where a ship arrives to wait its turn: off the top of the line, spread east or west and up or
        /// down by ship (golden-ratio steps, so no two ships share a spot).
        /// </summary>
        public ParkingOrbit HoldPoint(Ship ship)
        {
            double f = ship.Id * 0.6180339887498949 % 1;
            double g = ship.Id * 0.7548776662466927 % 1;
            return Point(RadiusKm + HoldKm * (0.85 + 0.3 * g), (f - 0.5) * 1.2);
        }

        /// <summary>Unit vector from the body's centre up through the ring at a date.</summary>
        public DVec3 Up(double jd) => Point(1).OffsetAt(jd);

        public override string ToString() => Name;
    }

    /// <summary>Everything a system has in orbit, and the docking rings over its populous worlds.</summary>
    public sealed class Infrastructure
    {
        public readonly List<OrbitalStructure> Structures = new List<OrbitalStructure>();
        public readonly List<Constellation> Constellations = new List<Constellation>();
        public readonly List<DockingRing> Rings = new List<DockingRing>();

        readonly BodyCatalog catalog;
        readonly Ephemeris ephemeris;

        public Infrastructure(BodyCatalog catalog, Ephemeris ephemeris = null)
        {
            this.catalog = catalog;
            this.ephemeris = ephemeris;
        }

        public OrbitalStructure YardOf(Settlement s)
        {
            foreach (var st in Structures)
                if (st.Kind == StructureKind.Shipyard && st.Settlement == s) return st;
            return null;
        }

        public IEnumerable<DockingRing> RingsAt(int body)
        {
            foreach (var r in Rings)
                if (r.Body == body) yield return r;
        }

        // ---------------------------------------------------------------- building

        static string FacilityName(IndustryKind k)
        {
            switch (k)
            {
                case IndustryKind.Smelter: return "Foundry";
                case IndustryKind.ElectronicsFab: return "Fab";
                case IndustryKind.MachineWorks: return "Works";
                case IndustryKind.MunitionsPlant: return "Arsenal";
                case IndustryKind.PharmaLab: return "Labs";
                case IndustryKind.Shipyard: return "Yards";
                default: return k.ToString();
            }
        }

        /// <summary>
        /// Each settlement's industry, put in orbit of its world: the shipyard as a yard, each factory
        /// as a station, and a habitat for the big ones. Mines and farms stay on the ground.
        /// </summary>
        public void AddSettlementStructures(Economy economy, int seed)
        {
            var rng = new Random(seed);
            var perBody = new Dictionary<int, int>();
            foreach (var s in economy.Settlements)
            {
                if (s.Kind == SettlementKind.Haven) continue;
                var b = catalog[s.Body];
                var list = new List<(StructureKind kind, IndustryKind industry, string name, double km)>();
                foreach (var f in s.Facilities)
                {
                    var k = f.Recipe.Kind;
                    if (k == IndustryKind.Mine || k == IndustryKind.Farm) continue;
                    if (k == IndustryKind.Shipyard) list.Add((StructureKind.Shipyard, k, s.Name + " " + FacilityName(k), 3.5));
                    else list.Add((StructureKind.Factory, k, s.Name + " " + FacilityName(k), 1.6));
                }
                if (s.Population >= 100e3 && b.Kind != BodyKind.Station) list.Add((StructureKind.Habitat, IndustryKind.Farm, s.Name + " Habitat", 2.2));

                foreach (var (kind, industry, name, km) in list)
                {
                    perBody.TryGetValue(s.Body, out int n);
                    perBody[s.Body] = n + 1;
                    // Low, near-equatorial orbits, a layer apart; free stations get a loose cluster around them.
                    double radii = b.Kind == BodyKind.Station ? 3.0 + n * 0.9 : 1.32 + n * 0.11;
                    var orbit = EquatorialOrbit(b, b.RadiusKm * radii, rng.NextDouble() * 12 - 6, rng.NextDouble() * 360, rng.NextDouble() * 360);
                    Structures.Add(new OrbitalStructure
                    {
                        Name = name, Kind = kind, Body = s.Body, Orbit = orbit, Owner = s.Owner, Settlement = s, Industry = industry, LengthKm = km,
                        Note = kind == StructureKind.Shipyard ? "Orbital shipyard (game layer)" : kind == StructureKind.Habitat ? "Orbital habitat (game layer)" : "Factory station (game layer)",
                    });
                }
            }
        }

        /// <summary>
        /// A circular orbit about a body's equator: <paramref name="incDeg"/> from it, its node
        /// <paramref name="nodeDeg"/> round from the equator's node, prograde with the spin.
        /// </summary>
        public static ParkingOrbit EquatorialOrbit(BodyDefinition b, double radiusKm, double incDeg, double nodeDeg, double phaseDeg, double epochJd = Astro.J2000)
        {
            var axis = b.SpinAxis;
            var node0 = BodyOrientation.EquatorNode(axis);
            var east0 = DVec3.Cross(axis, node0);
            double node = nodeDeg * Astro.Deg2Rad, inc = incDeg * Astro.Deg2Rad;
            var n = node0 * Math.Cos(node) + east0 * Math.Sin(node);
            var v = DVec3.Cross(axis, n) * Math.Cos(inc) + axis * Math.Sin(inc);
            return new ParkingOrbit
            {
                Body = b.Index, RadiusKm = radiusKm, U = n, V = v, EpochJd = epochJd, PhaseAtEpoch = phaseDeg * Astro.Deg2Rad,
                RateRadPerDay = Kepler.CircularRate(Math.Max(b.Gm, 1e-12), radiusKm),
            };
        }

        void Real(string name, StructureKind kind, string body, double altitudeKm, double incDeg, double nodeDeg, double phaseDeg, double km, string note)
        {
            var b = catalog[body];
            Structures.Add(new OrbitalStructure
            {
                Name = name, Kind = kind, Body = b.Index, Orbit = EquatorialOrbit(b, b.RadiusKm + altitudeKm, incDeg, nodeDeg, phaseDeg),
                LengthKm = km, Real = true, Note = note,
            });
        }

        /// <summary>
        /// Sol's real spacecraft and constellations (orbits from published altitudes and
        /// inclinations, circular; positions along them illustrative). See Core/Systems/SOURCES.md.
        /// </summary>
        public void AddSolSpacecraft()
        {
            Real("ISS", StructureKind.CrewedStation, "Earth", 420, 51.64, 40, 0, 0.109, "International Space Station: ~420 km, 51.64°, crewed since 2000.");
            Real("Tiangong", StructureKind.CrewedStation, "Earth", 385, 41.47, 170, 120, 0.055, "China's space station: ~385 km, 41.5°.");
            Real("Hubble", StructureKind.Telescope, "Earth", 530, 28.47, 290, 230, 0.0132, "Hubble Space Telescope: ~530 km, 28.5°, since 1990.");
            Structures.Add(new OrbitalStructure
            {
                Name = "JWST", Kind = StructureKind.Telescope, Body = catalog["Earth"].Index, AtL2 = true, LengthKm = 0.021, Real = true,
                Note = "James Webb Space Telescope, at the Sun-Earth L2 point 1.5 million km beyond Earth (its halo orbit is not drawn).",
            });
            Real("Lunar Reconnaissance Orbiter", StructureKind.Probe, "Moon", 100, 90, 30, 0, 0.004, "LRO: polar lunar orbit (drawn circular at 100 km).");
            Real("Mars Reconnaissance Orbiter", StructureKind.Probe, "Mars", 300, 93, 60, 0, 0.0137, "MRO: ~300 km sun-synchronous polar orbit.");
            Real("Mars Odyssey", StructureKind.Probe, "Mars", 400, 93.2, 150, 90, 0.0057, "2001 Mars Odyssey: ~400 km polar orbit.");
            Real("MAVEN", StructureKind.Probe, "Mars", 3000, 75, 250, 200, 0.0114, "MAVEN: 150 × 6,200 km ellipse at 75° (drawn circular at its mean).");
            Real("Mars Express", StructureKind.Probe, "Mars", 5000, 86.3, 330, 300, 0.012, "Mars Express: 300 × 10,000 km polar ellipse (drawn circular).");
            Real("ExoMars TGO", StructureKind.Probe, "Mars", 400, 74, 20, 45, 0.0175, "Trace Gas Orbiter: ~400 km, 74°.");

            int earth = catalog["Earth"].Index;
            double re = catalog[earth].RadiusKm;
            var gps = new Constellation { Name = "GPS", Body = earth, Color = new Rgb(0.55f, 0.95f, 0.6f) };
            gps.Shells.Add(new Constellation.Shell(26560, 55, 6, 5));
            var galileo = new Constellation { Name = "Galileo", Body = earth, Color = new Rgb(0.45f, 0.7f, 1f) };
            galileo.Shells.Add(new Constellation.Shell(29600, 56, 3, 9));
            var glonass = new Constellation { Name = "GLONASS", Body = earth, Color = new Rgb(1f, 0.55f, 0.5f) };
            glonass.Shells.Add(new Constellation.Shell(25510, 64.8, 3, 8));
            var beidou = new Constellation { Name = "BeiDou", Body = earth, Color = new Rgb(1f, 0.85f, 0.4f) };
            beidou.Shells.Add(new Constellation.Shell(27900, 55, 3, 8));
            var starlink = new Constellation { Name = "Starlink", Body = earth, Color = new Rgb(0.3f, 0.33f, 0.38f) };
            starlink.Shells.Add(new Constellation.Shell(re + 550, 53.0, 72, 22));
            starlink.Shells.Add(new Constellation.Shell(re + 540, 53.2, 72, 22));
            starlink.Shells.Add(new Constellation.Shell(re + 570, 70.0, 36, 20));
            starlink.Shells.Add(new Constellation.Shell(re + 560, 97.6, 6, 58));
            starlink.Shells.Add(new Constellation.Shell(re + 530, 43.0, 28, 40));
            var geo = new Constellation { Name = "Geostationary belt", Body = earth, Color = new Rgb(0.95f, 0.75f, 0.95f) };
            geo.Shells.Add(new Constellation.Shell(42164, 0.05, 1, 540));
            Constellations.AddRange(new[] { starlink, gps, galileo, glonass, beidou, geo });
        }

        // ---------------------------------------------------------------- docking rings

        /// <summary>
        /// Radius of the body's stationary orbit (km from its centre), where a satellite keeps pace with
        /// the ground; or three radii out where that lies beyond the body's neighbourhood (slow
        /// rotators such as Venus, tidally locked worlds).
        /// </summary>
        public static double RingRadiusKm(BodyDefinition b)
        {
            double periodS = Math.Abs(b.RotationHours) * 3600;
            double stationary = periodS > 0 ? Math.Pow(b.Gm * periodS * periodS / (4 * Math.PI * Math.PI), 1.0 / 3) : double.MaxValue;
            double limit = ScaleModel.NeighbourhoodRadii * 0.85 * b.RadiusKm;
            return stationary < limit ? stationary : b.RadiusKm * 3;
        }

        static double EntryInterface(string body)
        {
            switch (body)
            {
                case "Venus": return 250;
                case "Mars": return 125;
                case "Titan": return 600;
                default: return 120;
            }
        }

        /// <summary>Adds a ring over a site; it belongs to the world's largest settlement.</summary>
        public DockingRing AddRing(Economy economy, string body, string name, string city, double latDeg, double lonDeg)
        {
            var b = catalog[body];
            Settlement owner = null;
            foreach (var s in economy.At(b.Index))
                if (owner == null || s.Population > owner.Population) owner = s;
            var ring = new DockingRing(b)
            {
                Ephemeris = ephemeris,
                Id = Rings.Count, Name = name, City = city, LatDeg = latDeg, LonDeg = lonDeg,
                RadiusKm = RingRadiusKm(b), EntryInterfaceKm = EntryInterface(body), Settlement = owner, Owner = owner?.Owner,
            };
            // About 3.5 hours down from geostationary altitude; proportionally less from closer in.
            ring.DescentDays = 3.5 / 24 * Math.Max(0.25, (ring.RadiusKm - b.RadiusKm) / 35793.0);
            Rings.Add(ring);
            return ring;
        }

        /// <summary>
        /// How many rings a world's population warrants: one per order of magnitude past 100,000
        /// (⌈log₁₀(population / 10⁵)⌉, one to seven).
        /// </summary>
        public static int RingsFor(double population) =>
            Math.Max(1, Math.Min(7, (int)Math.Ceiling(Math.Log10(Math.Max(population, 1) / 1e5))));

        public void AddSolRings(Economy economy)
        {
            // Earth: one over each great city (seven, as many as its population warrants).
            AddRing(economy, "Earth", "New York Ring", "New York", 40.7128, -74.0060);
            AddRing(economy, "Earth", "Paris Ring", "Paris", 48.8566, 2.3522);
            AddRing(economy, "Earth", "Beijing Ring", "Beijing", 39.9042, 116.4074);
            AddRing(economy, "Earth", "Tokyo Ring", "Tokyo", 35.6762, 139.6503);
            AddRing(economy, "Earth", "Sydney Ring", "Sydney", -33.8688, 151.2093);
            AddRing(economy, "Earth", "Johannesburg Ring", "Johannesburg", -26.2041, 28.0473);
            AddRing(economy, "Earth", "Moscow Ring", "Moscow", 55.7558, 37.6173);
            // Mars (1.5 million: two), over Jezero crater and Pavonis Mons on the Tharsis bulge.
            AddRing(economy, "Mars", "Jezero Gate", "Jezero", 18.38, 77.58);
            AddRing(economy, "Mars", "Tharsis Gate", "Pavonis", 1.48, -112.96);
            // Venus's aerostat and Titan's port: one each.
            AddRing(economy, "Venus", "Aphrodite Gate", "Aphrodite Aerostat", -5, 105);
            AddRing(economy, "Titan", "Kraken Gate", "Kraken Port", 68, 50);
        }

        public void AddCentauriRings(Economy economy)
        {
            // Proxima b is tidally locked: the colony lies along the terminator, at dawn and dusk.
            AddRing(economy, "Proxima b", "Dawn Gate", "New Kentaurus (dawn)", 10, 90);
            AddRing(economy, "Proxima b", "Dusk Gate", "New Kentaurus (dusk)", -10, -90);
        }
    }
}
