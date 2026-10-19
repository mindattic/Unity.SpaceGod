using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// Colony ships through the gates (U.SPC-T293): a frontier star a new gate opens is settled by a faction with people
    /// to spare, its colony ship going the gate route to the best site there, and the new settlement then runs as any other.
    /// </summary>
    [TestFixture]
    public class InterstellarColonisationTests
    {
        const double StartJd = 2461324.0;

        /// <summary>Finishes a gate from <paramref name="from"/> to the frontier star <paramref name="star"/> at once, opening it.</summary>
        static StarSystem Open(Galaxy g, StarSystem from, string star, double jd)
        {
            var p = g.Construction.Start(from.World.Sol, from, null, FrontierSystems.Find(star), jd);
            p.SurveyDays = 0;
            g.Construction.Step(p, jd, 1);
            Assert.That(p.Stage, Is.EqualTo(GateStage.Building));
            p.WorkDays = 1;
            for (int i = 0; i < p.Need.Length; i++) p.Need[i] = 1;
            g.Construction.Step(p, jd + 1, 1);
            Assert.That(p.Stage, Is.EqualTo(GateStage.Done));
            return g.Find(star);
        }

        static void NoPressure(World w)
        {
            foreach (var s in w.Economy.Settlements) s.Overflow = 0;
        }

        /// <summary>
        /// U.SPC-T298: a frontier system whose only outpost failed and fell to ruin is empty again: open to settlement, so
        /// colonists can come back through the gate once the ruin's site is free, instead of a ruin keeping it "settled" for good.
        /// </summary>
        [Test]
        public void AFrontierSystemWhoseOnlyOutpostFailedIsOpenToSettlementAgain()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var frontier = Open(g, sol, "Ross 154", StartJd + 1);
            var site = frontier.World.Colonisation.Sites.Single(c => c.Habitat);
            var outpost = Colonisation.Found(frontier.World.Economy, sol.World.Sol, site, 1000, StartJd + 2);
            Assert.That(frontier.Settled, Is.True);
            Assert.That(frontier.OpenToSettlement, Is.False);
            frontier.World.Colonisation.Abandon(frontier.World.Economy, outpost, StartJd + 3);
            Assert.That(frontier.Settled, Is.False, "a ruin keeps the system settled");
            Assert.That(frontier.OpenToSettlement, Is.True);
        }

        [Test]
        public void EveryFrontierStarHasAHabitatSiteToSettle()
        {
            foreach (var spec in FrontierSystems.All())
            {
                var s = StarSystem.Create(spec, StartJd);
                var habitats = s.World.Colonisation.Sites.Where(c => c.Habitat).ToList();
                Assert.That(habitats.Count, Is.EqualTo(1), spec.Name);
                var h = habitats[0];
                Assert.That(h.Viable, Is.True, spec.Name);
                Assert.That(h.Score, Is.GreaterThan(0), spec.Name);
                Assert.That(h.Has(Good.Water) && h.Has(Good.Nitrogen), Is.True, "water and air from captured comets");
                var body = s.Catalog[h.Body];
                Assert.That(body.Kind, Is.EqualTo(BodyKind.Station));
                Assert.That(body.HabitatSite, Is.True);
                Assert.That(body.Name, Is.EqualTo(spec.Name + " Habitat"));
                // Well clear of its star.
                Assert.That(s.Ephemeris.Helio(h.Body).Length, Is.GreaterThan(FrontierSystems.HabitatMinRadii * s.Catalog.Root.RadiusKm * 0.99), spec.Name);
            }
            // A red dwarf's habitat is in its habitable zone; a brown dwarf as cold as WISE 0855 has none clear of it.
            var ross = StarSystem.Create(FrontierSystems.Find("Ross 154"), StartJd);
            Assert.That(ross.World.Colonisation.Sites.Single(c => c.Habitat).Zone, Is.EqualTo(HabitableZone.Conservative));
            var wise = StarSystem.Create(FrontierSystems.Find("WISE 0855−0714"), StartJd);
            Assert.That(wise.World.Colonisation.Sites.Single(c => c.Habitat).Zone, Is.EqualTo(HabitableZone.None));
            // The settled systems' stations are not sites: only an empty frontier orbit is marked out.
            var g = Galaxy.CreateDefault(StartJd);
            foreach (var sys in g.Systems)
                Assert.That(sys.World.Colonisation.Sites.Any(c => c.Habitat), Is.False, sys.Name);
        }

        /// <summary>
        /// The ticket's case: a frontier star is opened by a new gate, and a faction with people to spare sends a colony
        /// ship the gate route to its habitat site, which founds an outpost there; the system is then settled, held by its
        /// founder, and runs on (at coarse detail away from the player, at full detail when the player goes there).
        /// </summary>
        [Test]
        public void AFrontierSystemAGateOpensIsSettledThroughTheGatesByAFactionWithPressure()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            g.Focus = sol;
            var fed = sol.World.Sol;
            var frontier = Open(g, sol, "Ross 154", StartJd + 1);
            double jd = StartJd + 2;
            Assert.That(frontier, Is.Not.Null);
            Assert.That(frontier.OpenToSettlement, Is.True);
            Assert.That(frontier.Settled, Is.False);

            // Nobody with people to spare: nobody goes.
            NoPressure(sol.World);
            Assert.That(sol.World.Colonisation.TryLaunch(sol.World.Economy, fed, jd), Is.Null);

            // Earth runs out of room: the Federation's colony ship goes through the gates to the new system's habitat site.
            var earth = sol.World.Economy.Settlements.First(s => s.Name == "Earth Orbital");
            earth.Overflow = Colonisation.PartySize * 2;
            double treasury = fed.Treasury;
            var ship = sol.World.Colonisation.TryLaunch(sol.World.Economy, fed, jd);
            Assert.That(ship, Is.Not.Null);
            Assert.That(ship.Abroad, Is.True);
            Assert.That(ship.Where, Is.EqualTo("Ross 154"));
            Assert.That(ship.Into, Is.SameAs(frontier.World.Colonisation));
            Assert.That(ship.Site.Habitat, Is.True);
            Assert.That(ship.Colonists, Is.EqualTo(Colonisation.PartySize).Within(1e-6));
            Assert.That(fed.Treasury, Is.LessThan(treasury - Colonisation.FoundingCost));
            // Out to Sol's gate, through it, and in to the site: longer than the gates alone.
            var route = g.Network.Route(sol, frontier, fed, jd);
            Assert.That(route, Is.Not.Null);
            Assert.That(ship.ArriveJd - jd, Is.GreaterThan(GateNetwork.RouteDays(route) + 2));
            Assert.That(ship.ArriveJd - jd, Is.LessThan(GateNetwork.RouteDays(route) + 30));
            // The system is spoken for: no other colony ship is sent there meanwhile.
            Assert.That(frontier.World.Colonisation.Awaited, Is.True);
            var ac = g.Find("Alpha Centauri");
            var kentaurus = ac.World.Economy.Settlements.First(s => s.Name == "New Kentaurus");
            Assert.That(g.Colonisation.SitesFrom(ac, ac.World.Sol, kentaurus, jd), Is.Empty);

            // Run the galaxy until it lands (at the first market review of its home system after it gets there).
            double landBy = ship.ArriveJd + 11;
            MultiSystemLodTests.Run(g, jd, landBy);
            Assert.That(sol.World.Colonisation.Voyages, Does.Not.Contain(ship));
            Assert.That(frontier.World.Colonisation.Incoming, Is.Empty);
            var outpost = frontier.World.Economy.Settlements.Single();
            Assert.That(outpost.Name, Is.EqualTo("Ross 154 Habitat"));
            Assert.That(outpost.Owner, Is.SameAs(fed));
            Assert.That(outpost.Stage, Is.EqualTo(ColonyStage.Outpost));
            Assert.That(outpost.Population, Is.GreaterThan(Colonisation.PartySize * 0.95));
            Assert.That(outpost.Stock[(int)Good.Food], Is.GreaterThan(0), "its starter goods unloaded");
            Assert.That(frontier.Settled, Is.True);
            Assert.That(frontier.OpenToSettlement, Is.False);
            Assert.That(frontier.Holder, Is.SameAs(fed));
            Assert.That(frontier.World.Fleet.Factions, Does.Contain(fed));
            Assert.That(frontier.World.Events.Any(e => e.Text.Contains("found Ross 154 Habitat")), Is.True);
            Assert.That(sol.World.Events.Any(e => e.Text.Contains("in Ross 154, through the gates")), Is.True);

            // It lives on in that system's markets, at coarse detail while the player is away ...
            double landed = frontier.World.Jd;
            double end = landed + 60;
            MultiSystemLodTests.Run(g, landBy, end);
            Assert.That(frontier.World.Coarse, Is.True);
            Assert.That(end - frontier.World.Jd, Is.InRange(0, g.CoarseStepDays));
            Assert.That(outpost.Fallen, Is.False);
            Assert.That(outpost.Population, Is.GreaterThan(Colonisation.PartySize * 0.9));
            Assert.That(outpost.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Farm), Is.True);
            Assert.That(outpost.Facilities.Any(f => f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Water), Is.True);
            Assert.That(outpost.Price[(int)Good.Food], Is.GreaterThan(0));
            // ... and at full detail when the player arrives.
            g.Promote(frontier, end);
            MultiSystemLodTests.Run(g, end, end + 1);
            Assert.That(frontier.World.Coarse, Is.False);
            Assert.That(frontier.World.Jd, Is.EqualTo(end + 1));
            // The founder can build on outward from it.
            Assert.That(g.Construction.BasesOf(fed), Does.Contain(frontier));
        }

        [Test]
        public void NoColonyShipGoesWhereTheGatesWillNotTakeIt()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var fed = sol.World.Sol;
            var frontier = Open(g, sol, "Ross 154", StartJd + 1);
            double jd = StartJd + 2;
            var earth = sol.World.Economy.Settlements.First(s => s.Name == "Earth Orbital");
            Assert.That(g.Colonisation.SitesFrom(sol, fed, earth, jd).Select(x => x.Where), Does.Contain("Ross 154"));

            // The only gate in is wrecked: the way is shut, and the colonists settle at home instead.
            var gate = sol.GateTo(frontier);
            g.Construction.Damage(gate, 1, jd);
            Assert.That(frontier.OpenToSettlement, Is.False, "no working gate leads in");
            Assert.That(g.Colonisation.SitesFrom(sol, fed, earth, jd), Is.Empty);
            earth.Overflow = Colonisation.PartySize * 2;
            var ship = sol.World.Colonisation.TryLaunch(sol.World.Economy, fed, jd);
            if (ship != null) Assert.That(ship.Abroad, Is.False);
        }
    }
}
