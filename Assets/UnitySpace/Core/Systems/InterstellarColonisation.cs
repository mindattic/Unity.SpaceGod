using System.Collections.Generic;

namespace UnitySpace.Core
{
    /// <summary>
    /// Colony ships through the gates (U.SPC-T293). A system a new gate opens (<see cref="Galaxy.SystemOpened"/>,
    /// <see cref="GateConstruction"/>) has nobody in it to send settlers, so each system's <see cref="Colonisation"/> is
    /// shown, beside its own free sites, the free sites of every empty system open to settlement that its colonists can
    /// reach by gates that will let them through: a faction with people to spare and the money for a voyage then sends
    /// its colony ship to whichever is best for the time it takes. The voyage is reckoned as its legs are flown: from
    /// the port out to the gate, through the gates (<see cref="GateNetwork.RouteDays"/>), and in from the far gate to the
    /// site, each in-system leg at a freighter's flip-and-burn (<see cref="Contracts.TravelDays"/>). On landing the
    /// colonists found their outpost in that system's markets, where it lives as any other settlement: the system is
    /// settled, its founder holds it, and it runs (at the detail the player's whereabouts give it, U.SPC-T104) with
    /// the gate traffic that now trades with it. One colony ship at a time goes to a system still waiting for its first.
    /// </summary>
    public sealed class InterstellarColonisation
    {
        readonly Galaxy galaxy;

        public InterstellarColonisation(Galaxy galaxy)
        {
            this.galaxy = galaxy;
            foreach (var s in galaxy.Systems) Wire(s);
            galaxy.SystemAdded += (s, jd) => Wire(s);
        }

        void Wire(StarSystem home)
        {
            var c = home.World.Colonisation;
            if (c != null) c.Abroad = (f, from, jd) => SitesFrom(home, f, from, jd);
        }

        /// <summary>
        /// The free sites <paramref name="f"/>'s colonists at <paramref name="from"/> (in <paramref name="home"/>) could
        /// settle in the empty systems open to settlement, by the quickest way through the gates open to them, with the
        /// days each would take. A system another colony ship is already bound for is left to it.
        /// </summary>
        public List<ForeignSite> SitesFrom(StarSystem home, Faction f, Settlement from, double jd)
        {
            var list = new List<ForeignSite>();
            if (galaxy.Network == null || from == null) return list;
            foreach (var t in galaxy.Systems)
            {
                if (t == home || !t.OpenToSettlement) continue;
                var c = t.World.Colonisation;
                if (c == null || c.Awaited) continue;
                var route = galaxy.Network.Route(home, t, f, jd);
                if (route == null || route.Count == 0) continue;
                var depart = route[0];
                var arrive = route[route.Count - 1].Partner;
                double outward = Contracts.TravelDays((home.Ephemeris.HelioAt(from.Body, jd) - depart.PositionKm).Length);
                double through = GateNetwork.RouteDays(route);
                foreach (var site in c.Sites)
                {
                    if (site.Score <= 0 || Colonisation.Occupied(t.World.Economy, site.Body, jd) || c.Bound(site.Body)) continue;
                    double inward = Contracts.TravelDays((t.Ephemeris.HelioAt(site.Body, jd) - arrive.PositionKm).Length);
                    list.Add(new ForeignSite
                    {
                        Site = site, Into = c, Economy = t.World.Economy, Where = t.Name,
                        Days = outward + through + inward,
                    });
                }
            }
            return list;
        }
    }
}
