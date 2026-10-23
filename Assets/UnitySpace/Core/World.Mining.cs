using System;

namespace Unity.SpaceGod.Core
{
    public sealed partial class World
    {
        /// <summary>
        /// The asteroid-belt mining pipeline that feeds this system's megastructure builds (U.SPC-T330, see
        /// <see cref="BeltMining"/>): capture, grinding, separation, convoys to warehouses, and the builds they supply.
        /// </summary>
        public BeltMining Mining { get; private set; }

        /// <summary>Raiders about at which the pipeline's convoys and warehouses run their full risk.</summary>
        public const int MiningPressureRaiders = 6;

        /// <summary>Connects the pipeline to this world: real distances, the raiders actually about, its guns, its news.</summary>
        void WireMining(int seed)
        {
            var m = Mining = new BeltMining(seed);
            m.Position = (body, jd) => Ephemeris.HelioAt(body, jd);
            m.PiratePressure = MiningPiratePressure;
            m.Raiders = () => Pirates;
            m.Guarded = w =>
            {
                foreach (var s in Economy.Settlements)
                    if (s.Body == w.Body && s.Owner == w.Owner && !s.Fallen && s.Platforms.Count > 0) return true;
                return false;
            };
            m.Log = (jd, text, body) => Log(jd, text, WorldEventKind.Economy, body);
            m.HeavyLane = MiningHeavyLane;
            m.YardBuilt = (p, jd) =>
            {
                foreach (var s in Economy.Settlements)
                {
                    if (s.Body != p.SiteBody || s.Owner != p.Builder || s.Fallen) continue;
                    foreach (var f in s.Facilities)
                        if (f.Recipe.Kind == IndustryKind.Shipyard)
                        {
                            f.Level += 1;
                            f.BaseLevel += 1;
                            return;
                        }
                    s.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
                    return;
                }
            };
        }

        /// <summary>
        /// How many raiders are about, 0..1: the raider hulls at sea (escorts aside) and the havens they work from,
        /// against <see cref="MiningPressureRaiders"/>.
        /// </summary>
        double MiningPiratePressure()
        {
            double raiders = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction != null && s.Faction.IsPirate) raiders++;
            foreach (var h in Economy.Settlements)
                if (h.Kind == SettlementKind.Haven && !h.Fallen) raiders += 0.5;
            return Math.Min(1, raiders / MiningPressureRaiders);
        }

        /// <summary>
        /// The heavy lanes an ore tug can push a load along from <paramref name="from"/> to <paramref name="to"/> (U.SPC-T483):
        /// the quickest route between their planetary systems over heavy lanes alone, its length, and the pushes from the
        /// source to the first ramp and from the last ramp to the destination. Null where no heavy lanes join them.
        /// </summary>
        HeavyLanePath? MiningHeavyLane(int from, int to, double jd)
        {
            var lanes = Fleet?.Lanes;
            if (lanes == null) return null;
            var route = lanes.HeavyRoute(lanes.SystemOf(from), lanes.SystemOf(to), jd);
            if (route == null || route.Count == 0) return null;
            double laneKm = 0;
            var names = new System.Text.StringBuilder();
            foreach (var hop in route)
            {
                laneKm += lanes.LaneLengthKm(hop.Lane, jd);
                if (names.Length > 0) names.Append(", ");
                names.Append(hop.Lane.Name);
            }
            var first = route[0];
            var last = route[route.Count - 1];
            double legKm = (Ephemeris.HelioAt(from, jd) - lanes.RampHelio(first.Lane, first.From, jd)).Length
                + (Ephemeris.HelioAt(to, jd) - lanes.RampHelio(last.Lane, last.To, jd)).Length;
            return new HeavyLanePath(laneKm, legKm, names.ToString());
        }

        /// <summary>
        /// Sol's belt works: the Belt Consortium's refinery at Ceres ships what it separates to a depot on Callisto,
        /// out at Jupiter, which feeds the tether going up at Ganymede and the first ring of a swarm around the Sun.
        /// Its two ore tugs (U.SPC-T483) push blocks of ice cut in the belt out to the depot along the heavy Ceres-Jupiter
        /// lane, water and volatiles for the builds (the tether's carbon ribbon among them).
        /// </summary>
        void SeedSolMining(double jd)
        {
            var depot = Mining.AddWarehouse("Valhalla Depot", Belt, Catalog["Callisto"].Index);
            var works = Mining.AddOperation("Ceres Belt Works", Belt, Catalog["Ceres"].Index, depot);
            works.OreTugs = 2;
            works.TugIceShare = 1;
            works.IceDepot = depot;
            Mining.Begin(MegastructureSpec.SpaceElevator, Belt, Catalog["Ganymede"].Index, "Ganymede Elevator", jd);
            Mining.Begin(MegastructureSpec.DysonRing, Belt, 0, "Helios Swarm", jd);
        }
    }
}
