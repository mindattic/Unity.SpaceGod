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

        /// <summary>This system's jump gates, one for each link of the <see cref="GateNetwork"/> it is on; empty until linked.</summary>
        public IReadOnlyList<JumpGate> Gates => gates;
        readonly List<JumpGate> gates = new List<JumpGate>();

        internal void AddGate(JumpGate gate) => gates.Add(gate);

        /// <summary>This system's gate straight to <paramref name="other"/>, or null if they are not directly linked.</summary>
        public JumpGate GateTo(StarSystem other)
        {
            foreach (var g in gates)
                if (g.Destination == other) return g;
            return null;
        }

        /// <summary>The gate a ship at <paramref name="helioKm"/> (from this system's root) is close enough to activate, or null.</summary>
        public JumpGate GateInRange(DVec3 helioKm)
        {
            JumpGate best = null;
            double bestKm = double.MaxValue;
            foreach (var g in gates)
            {
                double km = (helioKm - g.PositionKm).Length;
                if (g.CanActivate(helioKm) && km < bestKm)
                {
                    bestKm = km;
                    best = g;
                }
            }
            return best;
        }

        /// <summary>
        /// Nobody has settled it yet, but a working gate leads in (U.SPC-T97): the first gate into an empty system opens it
        /// to settlement, and stays its way in while it holds (see <see cref="GateConstruction"/>).
        /// </summary>
        public bool OpenToSettlement
        {
            get
            {
                if (Settled) return false;
                foreach (var g in gates)
                    if (g.Link != null && g.Link.Working) return true;
                return false;
            }
        }

        /// <summary>When its first gate was finished and it was opened to settlement (see <see cref="OpenToSettlement"/>); NaN for one
        /// that was on the network from the start, or is not yet.</summary>
        public double OpenedJd = double.NaN;

        /// <summary>Has settlements to trade with (Sol, Alpha Centauri), rather than only stars and worlds.</summary>
        public bool Settled
        {
            get
            {
                foreach (var s in World.Economy.Settlements)
                    if (s.Kind != SettlementKind.Haven) return true;
                return false;
            }
        }

        /// <summary>The catalogue entry it was built from; null for Sol, whose bodies are listed in <see cref="BodyCatalog"/>.</summary>
        public SystemSpec Spec { get; }

        /// <summary>Debris belts and discs (Epsilon Eridani's belts, Tau Ceti's disc); empty where none is known.</summary>
        public IReadOnlyList<DebrisDisc> Discs { get; }

        /// <summary>How much of the system the overview frames (km from the root): the planets, or a binary's two stars.</summary>
        public double OverviewSpanKm { get; }

        /// <summary>The farthest any member or disc reaches from the root (km), when the system was built.</summary>
        public double ExtentKm { get; }

        StarSystem(string name, DVec3 positionLy, Ephemeris ephemeris, World world, SystemSpec spec, List<DebrisDisc> discs, double overviewSpanKm)
        {
            Name = name;
            PositionLy = positionLy;
            Catalog = ephemeris.Catalog;
            Ephemeris = ephemeris;
            World = world;
            Spec = spec;
            Discs = discs;
            OverviewSpanKm = overviewSpanKm;
            double extent = 0;
            for (int i = 0; i < Catalog.Count; i++) extent = Math.Max(extent, ephemeris.Helio(i).Length);
            foreach (var d in discs) extent = Math.Max(extent, ephemeris.Helio(d.HostIndex).Length + d.OuterKm);
            ExtentKm = extent;
        }

        public void Update(double jd)
        {
            Ephemeris.Update(jd);
            World.Update(jd);
        }

        /// <summary>Who holds the system: the owner of its most populous settlement (see <see cref="Polities.HolderOf"/>); null if unsettled.</summary>
        public Faction Holder => World.Holder;

        public static StarSystem CreateSol(double jd, Polities polities = null)
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(jd);
            return new StarSystem("Sol System", DVec3.Zero, eph, World.CreateDefault(eph, jd, polities: polities), null, new List<DebrisDisc>(), 2.2 * Astro.AuKm);
        }

        public static StarSystem CreateAlphaCentauri(double jd, Polities polities = null) =>
            Create(AlphaCentauri.Spec(), jd, (eph, at, seed) => World.CreateAlphaCentauri(eph, at, seed, polities), polities);

        /// <summary>
        /// A nearby system with the society planned for it (<see cref="NearbySocieties"/>, U.SPC-T90): its stations
        /// added to the catalogue entry, its settlements, lanes and traffic built by <see cref="World.CreateSettled"/>.
        /// A system with no society planned is left unsettled.
        /// </summary>
        public static StarSystem CreateSettled(SystemSpec spec, double jd, Polities polities = null)
        {
            var society = NearbySocieties.For(spec);
            if (society == null) return Create(spec, jd, polities: polities);
            society.AddStations(spec);
            return Create(spec, jd, (eph, at, seed) => World.CreateSettled(eph, at, seed, society, polities), polities);
        }

        /// <summary>
        /// Builds a system from its catalogue entry. <paramref name="world"/> makes its game layer
        /// (settlements, fleets); left out, the system is unsettled (see <see cref="World.CreateUnsettled"/>), its
        /// survey service one of <paramref name="polities"/>.
        /// </summary>
        public static StarSystem Create(SystemSpec spec, double jd, Func<Ephemeris, double, int, World> world = null, Polities polities = null)
        {
            var built = StarSystemBuilder.Build(spec);
            var catalog = new BodyCatalog(built.Bodies);
            foreach (var d in built.Discs) d.HostIndex = catalog[d.HostName].Index;
            var eph = new Ephemeris(catalog);
            eph.Update(jd);
            var w = world != null ? world(eph, jd, SeedOf(spec.Name)) : World.CreateUnsettled(eph, jd, SeedOf(spec.Name), polities);
            double span = double.IsNaN(spec.OverviewSpanAu) ? OverviewSpan(catalog) : spec.OverviewSpanAu * Astro.AuKm;
            return new StarSystem(spec.Name, spec.PositionLy, eph, w, spec, built.Discs, span);
        }

        /// <summary>Alpha Centauri keeps the seed its world was tuned with; the others get one from their names.</summary>
        static int SeedOf(string name)
        {
            if (name == "Alpha Centauri") return 4243;
            int h = 17;
            foreach (char c in name) h = h * 31 + c;
            return h & 0x7fffffff;
        }

        /// <summary>
        /// The overview's reach: a little past the farthest planet (or binary member) about a star or
        /// barycentre, leaving out wide companions hundreds of AU out; at least 0.12 AU.
        /// </summary>
        static double OverviewSpan(BodyCatalog catalog)
        {
            double span = 0;
            foreach (var b in catalog.Bodies)
            {
                if (b.ParentIndex < 0 || b.Kind == BodyKind.Station || !catalog[b.ParentIndex].UsesStellarMapping) continue;
                double apo = b.Elements.AKm * Math.Abs(b.OrbitScale) * (1 + b.Elements.E);
                if (apo < 200 * Astro.AuKm) span = Math.Max(span, apo);
            }
            return Math.Max(span * 1.15, 0.12 * Astro.AuKm);
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

        /// <summary>The jump-gate lattice between the systems (see <see cref="GateNetwork"/>).</summary>
        public GateNetwork Network { get; private set; }

        /// <summary>The ships crossing between systems and the voyages that send them (see <see cref="InterstellarTraffic"/>).</summary>
        public InterstellarTraffic Traffic { get; private set; }

        /// <summary>The gates being surveyed, built and rebuilt (U.SPC-T97): how the network grows outward and heals after a war.</summary>
        public GateConstruction Construction { get; private set; }

        /// <summary>Raised when a system joins the simulation (a frontier star reached by its first gate): (the system, when).</summary>
        public event Action<StarSystem, double> SystemAdded;

        /// <summary>
        /// Raised when the first gate into an empty system is finished and it is open to settlement (see
        /// <see cref="StarSystem.OpenToSettlement"/>): (the system, the faction that built the gate, when). Colonisation
        /// takes it from there.
        /// </summary>
        public event Action<StarSystem, Faction, double> SystemOpened;

        /// <summary>The wars between the systems' powers: their goals, campaigns, occupations and treaties (U.SPC-T92, see <see cref="InterstellarWars"/>).</summary>
        public InterstellarWars Wars { get; private set; }

        /// <summary>
        /// The interstellar polities (U.SPC-T91): every system's factions, one object each however many systems they
        /// hold, with one diplomacy table between them all.
        /// </summary>
        public Polities Polities { get; } = new Polities();

        /// <summary>
        /// The system simulated at full detail (U.SPC-T104): the one the player is in. Every other system keeps
        /// running at once, but at coarse detail (<see cref="World.Coarse"/>), stepped on a fixed grid of
        /// <see cref="CoarseStepDays"/> rather than every frame. Null runs every system at full detail.
        /// </summary>
        public StarSystem Focus { get; set; }

        /// <summary>How often (days) a system at coarse detail is stepped: every six hours, on the hour.</summary>
        public double CoarseStepDays = 0.25;

        /// <summary>Whether <paramref name="s"/> runs at full detail: it is the <see cref="Focus"/>, or there is none.</summary>
        public bool AtFullDetail(StarSystem s) => Focus == null || s == Focus;

        /// <summary>
        /// Every system to <paramref name="jd"/>, then the traffic between them. The <see cref="Focus"/> is run to
        /// the moment; the others to the last <see cref="CoarseStepDays"/> mark at or before it, so where they
        /// stand never depends on the frame rate. A system that has just become the focus is first brought up
        /// to the moment at coarse detail, from its last mark, and only then promoted: it carries on from exactly
        /// the state it had reached, with every ship, cargo and battle where it was. One the player has left is
        /// demoted as it stands and steps on from the next mark.
        /// </summary>
        public void Update(double jd)
        {
            foreach (var s in Systems)
            {
                if (AtFullDetail(s))
                {
                    s.Update(jd);
                    s.World.Coarse = false;
                }
                else
                {
                    s.World.Coarse = true;
                    double mark = CoarseMark(jd);
                    if (!(mark <= s.World.Jd)) s.Update(mark);
                }
            }
            Traffic?.Update(jd);
            Construction?.Update(jd);
            Wars?.Update(jd);
        }

        /// <summary>
        /// Brings <paramref name="system"/> into the simulation from <paramref name="jd"/> (a frontier star reached by its
        /// first gate, U.SPC-T97): it runs with the rest from now on (at coarse detail unless it is the focus), its gate
        /// traffic is taken on, and the plain star it was shown as among the <see cref="NearbyStars"/> gives way to it.
        /// </summary>
        public void AddSystem(StarSystem system, double jd)
        {
            if (system == null || Systems.Contains(system)) return;
            Systems.Add(system);
            string shown = system.Spec?.NeighbourName ?? system.Name;
            NearbyStars.RemoveAll(n => string.Equals(n.Name, shown, StringComparison.OrdinalIgnoreCase));
            system.World.Coarse = !AtFullDetail(system);
            Traffic?.Register(system);
            SystemAdded?.Invoke(system, jd);
        }

        internal void RaiseOpened(StarSystem system, Faction by, double jd) => SystemOpened?.Invoke(system, by, jd);

        /// <summary>
        /// Makes <paramref name="system"/> the <see cref="Focus"/> now (the player has arrived there): it is brought
        /// up to <paramref name="jd"/> from its last coarse step, at the detail it was running at, and goes on at
        /// full detail from there, with nothing it was doing interrupted. The old focus drops to coarse detail
        /// at the next <see cref="Update"/>.
        /// </summary>
        public void Promote(StarSystem system, double jd)
        {
            Focus = system;
            if (system == null) return;
            if (!(jd <= system.World.Jd)) system.Update(jd);
            system.World.Coarse = false;
        }

        /// <summary>The last coarse step at or before <paramref name="jd"/>.</summary>
        public double CoarseMark(double jd) => Math.Floor(jd / CoarseStepDays + 1e-9) * CoarseStepDays;

        public StarSystem Find(string name)
        {
            foreach (var s in Systems)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>How far a system's gate sits from its root: <see cref="JumpGate.RadiusKm"/> clear of
        /// the root's surface, so a star's gate is outside its photosphere (a barycentre has no surface).</summary>
        public static double GateDistanceKm(StarSystem s) => s.Catalog.Root.RadiusKm + JumpGate.RadiusKm;

        /// <summary>Unit direction (J2000 ecliptic) from one system to another.</summary>
        public static DVec3 Direction(StarSystem from, StarSystem to) => (to.PositionLy - from.PositionLy).Normalized;

        public static double DistanceLy(StarSystem from, StarSystem to) => (to.PositionLy - from.PositionLy).Length;

        /// <summary>
        /// Sol, Alpha Centauri, and the nearest real systems beyond (<see cref="NearbySystems"/>), each
        /// settled more thinly the farther out it is (<see cref="NearbySocieties"/>), all joined by the jump-gate
        /// lattice (<see cref="GateNetwork"/>) that the interstellar traffic flies; the rest of the
        /// nearest stars are shown, not simulated, until a faction's gate reaches one (<see cref="GateConstruction"/>,
        /// <see cref="FrontierSystems"/>).
        /// </summary>
        public static Galaxy CreateDefault(double jd)
        {
            var g = new Galaxy();
            // Sol first: the Federation is founded there, with its capital on Earth, before its frontier joins it.
            var sol = StarSystem.CreateSol(jd, g.Polities);
            var alphaCentauri = StarSystem.CreateAlphaCentauri(jd, g.Polities);
            g.Systems.Add(sol);
            g.Systems.Add(alphaCentauri);
            foreach (var spec in NearbySystems.All()) g.Systems.Add(StarSystem.CreateSettled(spec, jd, g.Polities));
            var simulated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in g.Systems)
                if (s.Spec?.NeighbourName != null) simulated.Add(s.Spec.NeighbourName);
            foreach (var n in Neighbours())
                if (!simulated.Contains(n.Name)) g.NearbyStars.Add(n);
            g.Connect(jd);
            return g;
        }

        /// <summary>
        /// Joins <see cref="Systems"/> (built into this galaxy's <see cref="Polities"/>) by the jump gates: the lattice
        /// <see cref="GateNetwork.Build"/> makes, or <paramref name="network"/> if given (a test's own map); arms the gates
        /// its states hold (<see cref="GateNetwork.Fortify"/>); and starts the traffic, the gate building and the wars between them.
        /// </summary>
        public void Connect(double jd, GateNetwork network = null)
        {
            Network = network ?? GateNetwork.Build(Systems);
            Network.Fortify();
            Traffic = new InterstellarTraffic(this, jd);
            Polities.InTransit = Traffic.InTransit;
            Construction = new GateConstruction(this, jd);
            Wars = new InterstellarWars(this, jd);
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
