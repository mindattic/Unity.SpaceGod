using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T103: the galaxy map. Every system with the gate network between them, coloured by who holds it, the traffic
    /// through each gate, war fronts, blockades, raiders and gates being built -- but only as the player knows them: the
    /// system they are in as it is, every other by the newest news of it that has come through the gates.
    /// </summary>
    [TestFixture]
    public class GalaxyMapTests
    {
        const double StartJd = 2461324.0;
        const double Minute = 1.0 / 1440;

        static (Galaxy g, StarSystem sol, StarSystem ac) Create()
        {
            var g = Galaxy.CreateDefault(StartJd);
            // No relay goes out on its own while a test steps the news: each one sends what it means to.
            g.Traffic.News.NextRelayJd = double.PositiveInfinity;
            return (g, g.Find("Sol System"), g.Find("Alpha Centauri"));
        }

        /// <summary>Sends <paramref name="from"/>'s relay now and lets it land in <paramref name="to"/>.</summary>
        static double Relay(Galaxy g, StarSystem from, StarSystem to, double jd)
        {
            var news = g.Traffic.News;
            double lag = news.RelayDays(from, to, jd);
            news.Relay(jd);
            news.Update(jd + lag + Minute);
            return jd + lag + Minute;
        }

        [Test]
        public void HereIsSeenAsItIsAndEveryOtherSystemOnlyByItsNews()
        {
            var (g, sol, ac) = Create();
            var map = GalaxyMap.Build(g, sol, StartJd);
            Assert.That(map.Systems.Count, Is.EqualTo(g.Systems.Count), "every simulated system is on the map");
            Assert.That(map.Links.Count, Is.EqualTo(g.Network.Links.Count), "with every gate link");

            var here = map.Of(sol);
            Assert.That(here.Here, Is.True);
            Assert.That(here.AgeDays, Is.EqualTo(0));
            Assert.That(here.Holder, Is.SameAs(sol.Holder));
            Assert.That(here.Holder.Name, Is.EqualTo(World.SolFederation));
            Assert.That(here.Holdings.Count, Is.GreaterThan(1), "Sol is shared between several polities");
            Assert.That(here.Holdings[0].Faction, Is.SameAs(sol.Holder), "the biggest holding first: the holder's");

            var there = map.Of(ac);
            Assert.That(there.Known, Is.True, "the charts the game starts with");
            Assert.That(there.AgeDays, Is.EqualTo(g.Traffic.News.Known(sol, ac).AgeDays(StartJd)).Within(1e-9), "as old as the news Sol holds of it");
            Assert.That(there.Holder, Is.SameAs(ac.Holder));

            // A system that has never sent news (nobody settled it) is a star on the charts and no more.
            foreach (var s in g.Systems.Where(s => !s.Settled))
            {
                var m = map.Of(s);
                Assert.That(m.Known, Is.False, s.Name);
                Assert.That(m.Holder, Is.Null);
                Assert.That(double.IsNaN(m.AgeDays), Is.True);
            }

            // Seen from Centauri, it is Centauri that is live and Sol that is news.
            var fromAc = GalaxyMap.Build(g, ac, StartJd);
            Assert.That(fromAc.Of(ac).AgeDays, Is.EqualTo(0));
            Assert.That(fromAc.Of(sol).AgeDays, Is.GreaterThan(0));
        }

        [Test]
        public void ASiegeFarOffShowsOnlyOnceWordOfItHasComeThroughTheGates()
        {
            var (g, sol, ac) = Create();
            var port = ac.World.Economy.Settlements.First(s => s.Kind != SettlementKind.Haven && !s.Hidden);
            ac.World.Diplomacy.DeclareBlockade(port, ac.World.Pirates ?? ac.World.Belt);
            double jd = StartJd + 0.5;

            Assert.That(GalaxyMap.Build(g, sol, jd).Of(ac).Threats, Is.Empty, "Sol has not heard yet");
            Assert.That(GalaxyMap.Build(g, ac, jd).Of(ac).Threats.Any(t => t.Port == port.Name), Is.True, "Centauri sees it at once");

            double heard = Relay(g, ac, sol, jd);
            var map = GalaxyMap.Build(g, sol, heard);
            var threat = map.Of(ac).Threats.Single(t => t.Port == port.Name);
            Assert.That(threat.BlockadedBy, Is.Not.Null);
            Assert.That(map.Of(ac).AgeDays, Is.GreaterThan(0));
        }

        [Test]
        public void AGateClosedFarOffIsShutOnTheMapOnlyOnceTheNewsArrivesAndAtHomeAtOnce()
        {
            var (g, sol, ac) = Create();
            var link = g.Network.Between(sol, ac);
            Assert.That(link, Is.Not.Null, "Sol and Centauri are joined directly");
            double jd = StartJd + 0.5;

            // Centauri shuts its end.
            g.Traffic.Blockade(link.GateIn(ac), jd, InterstellarTraffic.BlockadeDays, "test");
            var before = GalaxyMap.Build(g, sol, jd).Between(sol, ac);
            Assert.That(before.Shut, Is.False, "word has not reached Sol");
            double heard = Relay(g, ac, sol, jd);
            var after = GalaxyMap.Build(g, sol, heard).Between(sol, ac);
            Assert.That(link.A == ac ? after.ShutAtA : after.ShutAtB, Is.True, "the news has come round by the other gates");
            Assert.That(link.A == ac ? after.OwnerA : after.OwnerB, Is.SameAs(link.GateIn(ac).Owner));

            // Sol's own end, shut, is known in Sol at once; and once the blockade runs out it reads open again.
            var (g2, sol2, ac2) = Create();
            var link2 = g2.Network.Between(sol2, ac2);
            g2.Traffic.Blockade(link2.GateIn(sol2), jd, 10, "test");
            var home = GalaxyMap.Build(g2, sol2, jd).Between(sol2, ac2);
            Assert.That(home.Shut, Is.True);
            Assert.That(link2.A == sol2 ? home.ShutAtA : home.ShutAtB, Is.True, "the Sol end");
            Assert.That(GalaxyMap.Build(g2, sol2, jd + 11).Between(sol2, ac2).Shut, Is.False);
        }

        [Test]
        public void TheGateBetweenTwoPolitiesAtWarIsAFrontAndAPortHeldByBothSidesIsContested()
        {
            var (g, sol, ac) = Create();
            var federation = sol.Holder;
            var accord = ac.Holder;
            Assert.That(federation, Is.Not.SameAs(accord));
            g.Polities.Diplomacy.Set(federation, accord, Relation.Peace);
            var link = GalaxyMap.Build(g, sol, StartJd).Between(sol, ac);
            Assert.That(link.WarFront, Is.False);

            g.Polities.Diplomacy.Set(federation, accord, Relation.War);
            var map = GalaxyMap.Build(g, sol, StartJd);
            Assert.That(map.AtWar(federation, accord), Is.True, "the war is known: the table is the one every system shares");
            var front = map.Between(sol, ac);
            Assert.That(front.WarFront, Is.True);
            Assert.That(front.Front.Involves(federation) && front.Front.Involves(accord), Is.True);
            Assert.That(map.Fronts(), Does.Contain(front));

            // Inside Sol: the Federation at war with another polity holding markets there puts the front in the system.
            var rival = map.Of(sol).Holdings.Select(h => h.Faction).First(f => f != federation);
            g.Polities.Diplomacy.Set(federation, rival, Relation.War);
            var contested = GalaxyMap.Build(g, sol, StartJd).Of(sol);
            Assert.That(contested.Contested, Is.True);
            Assert.That(contested.ContestedBy.Involves(federation), Is.True);
        }

        [Test]
        public void TrafficThroughAGateIsCountedOnlyUpToTheNewsOfItsEnds()
        {
            var (g, sol, ac) = Create();
            var link = g.Network.Between(sol, ac);
            // A far link: neither end is Sol, so its traffic is known only up to the news of its ends.
            var far = g.Network.Links.First(l => l.A != sol && l.B != sol && l.A.Settled && l.B.Settled);
            var log = g.Traffic;
            log.LogCrossing(new GateCrossing { Link = link, From = sol, Jd = StartJd - 10, Purpose = VoyagePurpose.Trade, Faction = sol.Holder, Ships = 1, Tonnes = 4000 });
            log.LogCrossing(new GateCrossing { Link = link, From = ac, Jd = StartJd - 5, Purpose = VoyagePurpose.Trade, Faction = ac.Holder, Ships = 1, Tonnes = 2500 });
            log.LogCrossing(new GateCrossing { Link = link, From = ac, Jd = StartJd - 2, Purpose = VoyagePurpose.Raid, Faction = ac.World.Pirates, Ships = 3 });
            log.LogCrossing(new GateCrossing { Link = link, From = sol, Jd = StartJd - GalaxyMap.FlowWindowDays - 5, Purpose = VoyagePurpose.Trade, Faction = sol.Holder, Ships = 1, Tonnes = 9000 });
            log.LogCrossing(new GateCrossing { Link = far, From = far.A, Jd = StartJd + 0.2, Purpose = VoyagePurpose.Courier, Faction = far.A.Holder, Ships = 1 });

            var map = GalaxyMap.Build(g, sol, StartJd + 0.3);
            var flow = map.Between(sol, ac);
            Assert.That(flow.Crossings, Is.EqualTo(3), "Sol is one end: everything through it up to now, inside the window");
            Assert.That(flow.Trade, Is.EqualTo(2));
            Assert.That(flow.Raids, Is.EqualTo(1));
            Assert.That(flow.Tonnes, Is.EqualTo(6500));
            Assert.That(link.A == sol ? flow.TowardB : flow.TowardA, Is.EqualTo(1), "one run out from Sol");
            Assert.That(flow.Flow, Is.EqualTo(1), "the busiest link on the map");
            Assert.That(map.Of(sol).Raids, Is.EqualTo(1), "the raid came in to Sol");

            var farFlow = map.Between(far.A, far.B);
            Assert.That(farFlow.Crossings, Is.EqualTo(0), "that crossing is newer than any news of its ends");
            double heard = Relay(g, far.A, sol, StartJd + 0.5);
            Assert.That(GalaxyMap.Build(g, sol, heard).Between(far.A, far.B).Crossings, Is.EqualTo(1), "and shows once news newer than it has come");
        }

        [Test]
        public void RaidersAboutASystemShowAsPirateActivityOnceHeardOf()
        {
            var (g, sol, ac) = Create();
            var pirates = ac.World.Pirates;
            Assume.That(pirates, Is.Not.Null);
            int before = SystemReport.Take(ac, StartJd).PirateShips;
            for (int i = 0; i < 10; i++)
            {
                var s = ac.World.Fleet.AddShip("Shade " + i, ShipClass.Destroyer, pirates, ShipRole.Patrol);
                s.Combat = new ShipCombat(CombatSpec.Of(ShipClass.Destroyer), ShipClass.Destroyer);
            }
            var live = GalaxyMap.Build(g, ac, StartJd).Of(ac);
            Assert.That(live.PirateShips, Is.EqualTo(before + 10));
            Assert.That(live.Pirates, Is.EqualTo(PirateActivity.High));
            Assert.That(GalaxyMap.Build(g, sol, StartJd).Of(ac).PirateShips, Is.EqualTo(before), "not yet heard of in Sol");
            double heard = Relay(g, ac, sol, StartJd + 0.5);
            Assert.That(GalaxyMap.Build(g, sol, heard).Of(ac).Pirates, Is.EqualTo(PirateActivity.High));
        }

        [Test]
        public void AGateBeingBuiltShowsOnceEitherEndIsKnown()
        {
            var (g, sol, ac) = Create();
            // Two systems the network does not join directly.
            StarSystem a = null, b = null;
            foreach (var x in g.Systems)
            foreach (var y in g.Systems)
                if (a == null && x != y && x.Settled && g.Network.Between(x, y) == null)
                {
                    a = x;
                    b = y;
                }
            Assume.That(a, Is.Not.Null);
            var work = new GateWork { A = a, B = b, Progress = 0.4, Builder = a.Holder };
            var map = GalaxyMap.Build(g, sol, StartJd, new[] { work });
            var building = map.Between(a, b);
            Assert.That(building, Is.Not.Null);
            Assert.That(building.UnderConstruction, Is.True);
            Assert.That(building.Link, Is.Null, "no gate to fly through yet");
            Assert.That(building.Progress, Is.EqualTo(0.4));
            Assert.That(map.Links.Count, Is.EqualTo(g.Network.Links.Count + 1));

            // Between two systems never heard from it is not shown.
            var unknown = g.Systems.Where(s => !s.Settled).ToList();
            if (unknown.Count >= 2)
                Assert.That(GalaxyMap.Build(g, sol, StartJd, new[] { new GateWork { A = unknown[0], B = unknown[1] } }).Between(unknown[0], unknown[1]), Is.Null);
        }
    }
}
