using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// U.SPC-T91: the interstellar polities. One faction model across every star system: a polity is one object
    /// wherever it holds settlements, with one treasury, one navy, one diplomacy table and one capital.
    /// </summary>
    [TestFixture]
    public class PolitiesTests
    {
        const double StartJd = 2461324.0;

        static Galaxy Create() => Galaxy.CreateDefault(StartJd);

        static double RunUntil(Galaxy g, double fromJd, double days, Func<bool> done)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done())
            {
                jd += 0.5;
                g.Update(jd);
            }
            return jd;
        }

        [Test]
        public void EverySystemSharesOneRegistryOfPolitiesAndOneDiplomacyTable()
        {
            var g = Create();
            var p = g.Polities;
            Assert.That(p.Worlds, Is.EquivalentTo(g.Systems.Select(s => s.World)));
            // One object per name: no system made its own copy of anybody.
            Assert.That(p.Factions.Select(f => f.Name), Is.Unique);
            foreach (var s in g.Systems)
            {
                Assert.That(s.World.Polities, Is.SameAs(p), s.Name);
                Assert.That(s.World.Diplomacy, Is.SameAs(p.Diplomacy), s.Name);
                foreach (var f in s.World.Fleet.Factions)
                    Assert.That(p.Factions, Does.Contain(f), $"{f.Name} in {s.Name} is not a polity");
            }

            // The Federation holds the core: Sol and the Barnard's Star frontier, the same polity in both.
            var sol = g.Find("Sol System");
            var barnard = g.Find("Barnard's Star");
            var federation = p.Find(World.SolFederation);
            Assert.That(sol.World.Sol, Is.SameAs(federation));
            Assert.That(barnard.World.Sol, Is.SameAs(federation));
            Assert.That(p.SystemsHeldBy(federation), Is.EquivalentTo(new[] { sol.World, barnard.World }));
            Assert.That(sol.Holder, Is.SameAs(federation));
            Assert.That(barnard.Holder, Is.SameAs(federation));
            Assert.That(barnard.World.Economy.Settlements.Count(s => s.Owner == federation), Is.GreaterThan(0));

            // Its one capital is on Earth, whichever system asks.
            Assert.That(p.CapitalOf(federation).Name, Is.EqualTo("Earth Orbital"));
            Assert.That(p.CapitalSystemOf(federation), Is.SameAs(sol.World));

            // Alpha Centauri has its own government; every other frontier system its own state; the raiders are clans.
            var ac = g.Find("Alpha Centauri");
            Assert.That(ac.Holder.Name, Is.EqualTo("Centauri Accord"));
            Assert.That(ac.Holder.Kind, Is.EqualTo(PolityKind.State));
            var frontier = g.Systems.Where(s => s != sol && s != barnard && s != ac).ToList();
            Assert.That(frontier, Is.Not.Empty);
            foreach (var s in frontier)
            {
                var holder = s.Holder;
                Assert.That(holder, Is.Not.Null, s.Name + " is held by nobody");
                Assert.That(holder.Kind, Is.EqualTo(PolityKind.State), s.Name);
                Assert.That(holder, Is.Not.SameAs(federation), s.Name);
                Assert.That(p.SystemsHeldBy(holder), Is.EquivalentTo(new[] { s.World }), s.Name + " shares its state");
                Assert.That(p.CapitalSystemOf(holder), Is.SameAs(s.World), s.Name);
            }
            foreach (var f in p.Factions.Where(f => f.IsPirate)) Assert.That(f.Kind, Is.EqualTo(PolityKind.PirateClan), f.Name);
            Assert.That(p.Factions.Count(f => f.Kind == PolityKind.PirateClan && p.SettlementsOf(f).Any(x => x.Kind == SettlementKind.Haven)),
                Is.GreaterThanOrEqualTo(3), "pirate clans with havens of their own");
        }

        [Test]
        public void ATreasuryIsOneAccountWhicheverSystemSpendsOrEarnsIt()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var barnard = g.Find("Barnard's Star");
            var federation = sol.World.Sol;
            // The Federation keeps warships at Barnard's Star, whose upkeep comes out of the one treasury.
            Assert.That(barnard.World.Fleet.Ships.Any(s => !s.Destroyed && s.Faction == federation && s.Spec.IsMilitary), Is.True);

            double before = federation.Treasury;
            // Only Barnard's Star runs: its days' upkeep and trade still move the Federation's money as Sol sees it.
            for (double jd = StartJd + 0.5; jd <= StartJd + 5; jd += 0.5) barnard.Update(jd);
            Assert.That(sol.World.Sol.Treasury, Is.Not.EqualTo(before));
            Assert.That(sol.World.Sol.Treasury, Is.EqualTo(barnard.World.Sol.Treasury));

            // Run the whole galaxy: one figure, never two.
            RunUntil(g, StartJd + 5, 30, () => false);
            foreach (var f in g.Polities.Factions)
            {
                var copies = g.Systems.SelectMany(s => s.World.Fleet.Factions).Where(x => x.Name == f.Name).Distinct().ToList();
                Assert.That(copies, Has.Count.EqualTo(1), f.Name);
            }

            // Its reserve of spacers is mustered from its people in every system it holds.
            var everywhere = Crews.ReserveFor(federation, g.Polities.Settlements);
            var solOnly = Crews.ReserveFor(federation, sol.World.Economy.Settlements);
            Assert.That(everywhere, Is.GreaterThan(solOnly));
            Assert.That(federation.Manpower, Is.LessThanOrEqualTo(everywhere + 1e-6));
            // And it recruits once a day, by the system that keeps its books.
            Assert.That(g.Polities.HomeOf(federation), Is.SameAs(sol.World));
            Assert.That(g.Polities.KeptBy(barnard.World, barnard.World.Fleet.Factions), Does.Not.Contain(federation));
        }

        [Test]
        public void ANavyIsOneFleetWhoseShipsAreCountedOnceAcrossSystemsAndGates()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var barnard = g.Find("Barnard's Star");
            var federation = g.Polities.Find(World.SolFederation);
            var p = g.Polities;

            List<string> Names() => p.Navy(federation).Select(s => s.Name).ToList();
            var navy = Names();
            Assert.That(navy, Is.Unique);
            int inSol = sol.World.Fleet.Ships.Count(s => !s.Destroyed && s.Faction == federation && s.Spec.IsMilitary);
            int inBarnard = barnard.World.Fleet.Ships.Count(s => !s.Destroyed && s.Faction == federation && s.Spec.IsMilitary);
            Assert.That(inSol, Is.GreaterThan(0));
            Assert.That(inBarnard, Is.GreaterThan(0));
            Assert.That(navy, Has.Count.EqualTo(inSol + inBarnard), "one navy: the warships of both systems");

            // The frontier patrol sails for Sol: it is one navy's group the whole way, never lost and never doubled.
            var flagship = barnard.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && s.Faction == federation && s.Spec.IsMilitary);
            string name = flagship.Name;
            double jd = RunUntil(g, StartJd, 30, () => flagship.Phase == ShipPhase.Parked && flagship.Battle == null);
            var v = g.Traffic.Send(flagship, barnard, sol, VoyagePurpose.Expedition, jd);
            Assert.That(v, Is.Not.Null, "no way from Barnard's Star to Sol");
            bool sawInGate = false;
            jd = RunUntil(g, jd, 120, () =>
            {
                var names = Names();
                Assert.That(names.Count(n => n == name), Is.EqualTo(1), name + " counted other than once");
                sawInGate |= g.Traffic.InTransit().Any(s => s.Name == name);
                return sol.World.Fleet.Ships.Any(s => s.Name == name && !s.Destroyed);
            });
            Assert.That(sawInGate, Is.True, "it never crossed");
            var there = sol.World.Fleet.Ships.First(s => s.Name == name && !s.Destroyed);
            Assert.That(there.Faction, Is.SameAs(federation));
            Assert.That(p.Navy(federation), Does.Contain(there));
            // In Sol it flies the same colours as the home fleet: no visitor from another Federation.
            Assert.That(sol.World.Fleet.Factions, Does.Contain(there.Faction));
        }

        [Test]
        public void RelationsAreOneTableAWarDeclaredAnywhereHoldsEverywhere()
        {
            var g = Create();
            var sol = g.Find("Sol System");
            var barnard = g.Find("Barnard's Star");
            var eridani = g.Find("Epsilon Eridani");
            var federation = sol.World.Sol;
            var union = eridani.World.Sol;
            Assert.That(union, Is.Not.SameAs(federation));
            Assert.That(sol.World.Diplomacy.Get(federation, union), Is.EqualTo(Relation.Peace));

            // The Federation's war with the Reach, declared in Sol, stands at Barnard's Star too.
            var reach = sol.World.Reach;
            Assert.That(barnard.World.Diplomacy.Get(federation, reach), Is.EqualTo(Relation.War));
            Assert.That(g.Polities.AtWar(federation), Is.True);

            // War declared from the frontier is war in Sol and at Eridani, both ways round.
            barnard.World.Diplomacy.Set(union, federation, Relation.War);
            foreach (var s in new[] { sol, barnard, eridani })
            {
                Assert.That(s.World.Diplomacy.Get(federation, union), Is.EqualTo(Relation.War), s.Name);
                Assert.That(s.World.Diplomacy.Hostile(union, federation), Is.True, s.Name);
            }
            // Eridani's ports shut to the Federation, the Federation's (at home and on the frontier) to Eridani.
            var eridaniPort = eridani.World.Economy.Settlements.First(s => s.Owner == union);
            Assert.That(eridani.World.Diplomacy.CanDockAt(federation, eridaniPort), Is.False);
            foreach (var w in new[] { sol.World, barnard.World })
            {
                var port = w.Economy.Settlements.First(s => s.Owner == federation);
                Assert.That(w.Diplomacy.CanDockAt(union, port), Is.False, port.Name);
            }
            // And the Federation's gates, wherever they stand, are shut to an enemy.
            foreach (var gate in sol.Gates.Concat(barnard.Gates))
            {
                Assume.That(gate.Owner, Is.SameAs(federation));
                Assert.That(gate.Admits(union, StartJd), Is.False, gate.Name);
            }

            // Peace made anywhere is peace everywhere.
            eridani.World.Diplomacy.Set(federation, union, Relation.Peace);
            Assert.That(sol.World.Diplomacy.Get(federation, union), Is.EqualTo(Relation.Peace));
            Assert.That(barnard.World.Diplomacy.CanDockAt(union, barnard.World.Economy.Settlements.First(s => s.Owner == federation)), Is.True);
        }

        [Test]
        public void ACapitalLostMovesTheSeatToTheBiggestHoldingLeftInAnySystem()
        {
            var g = Create();
            var p = g.Polities;
            var sol = g.Find("Sol System");
            var federation = sol.World.Sol;
            var earth = p.CapitalOf(federation);
            Assert.That(earth.Name, Is.EqualTo("Earth Orbital"));

            earth.Owner = sol.World.Reach;
            var seat = p.CapitalOf(federation);
            Assert.That(seat, Is.Not.SameAs(earth));
            Assert.That(seat.Owner, Is.SameAs(federation));
            var biggest = p.SettlementsOf(federation).OrderByDescending(s => s.Population).First();
            Assert.That(seat, Is.SameAs(biggest));
            // Retaken, it is the next seat's: the capital moved and stays moved until that one too is lost.
            earth.Owner = federation;
            Assert.That(p.CapitalOf(federation), Is.SameAs(seat));
        }

        [Test]
        public void AWorldBuiltOnItsOwnKeepsItsOwnPolities()
        {
            var a = StarSystem.CreateSol(StartJd);
            var b = StarSystem.CreateSol(StartJd);
            Assert.That(a.World.Polities, Is.Not.SameAs(b.World.Polities));
            Assert.That(a.World.Sol, Is.Not.SameAs(b.World.Sol));
            Assert.That(a.World.Diplomacy, Is.Not.SameAs(b.World.Diplomacy));

            // Two systems built into one registry share whoever they have in common, and only them.
            var shared = new Polities();
            var sol = StarSystem.CreateSol(StartJd, shared);
            double afterSol = sol.World.Sol.Treasury;
            var spec = NearbySystems.All().First(s => s.Name == "Barnard's Star");
            var barnard = StarSystem.CreateSettled(spec, StartJd, shared);
            Assert.That(barnard.World.Sol, Is.SameAs(sol.World.Sol));
            Assert.That(barnard.World.Belt, Is.Not.SameAs(sol.World.Belt));
            Assert.That(shared.Factions.Count(f => f.Name == World.SolFederation), Is.EqualTo(1));
            // The frontier's revenues joined the one treasury rather than starting a second.
            Assert.That(sol.World.Sol.Treasury, Is.Not.EqualTo(afterSol));
        }
    }
}
