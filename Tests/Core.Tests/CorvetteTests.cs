using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Corvettes (U.SPC-T476): system defence with no FTL drive, the weight saved going to patrol. Each navy stations them
    /// for good at its ports (a planet, moon or station); they ride the hyper lanes of their home system as local law
    /// enforcement, run down the smugglers and pirates working their ground, and never leave that system.
    /// </summary>
    [TestFixture]
    public class CorvetteTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            w.War.Battles.Clear();
            return w;
        }

        static IEnumerable<Faction> Navies(World w) => w.Fleet.Factions.Where(Corvettes.Keeps);

        static Ship Hull(ShipClass c, Faction f) =>
            new Ship { Name = c.ToString(), Spec = ShipClassSpec.Of(c), Faction = f, Role = ShipRole.Patrol, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        // ---------------------------------------------------------------- the class

        [Test]
        public void TheCorvetteIsASmallPatrolHullThatPutsTheWeightOfAnFtlDriveIntoPatrol()
        {
            var spec = ShipClassSpec.Of(ShipClass.Corvette);
            Assert.That(spec.IsMilitary, Is.True);
            Assert.That(spec.DisplayName, Is.EqualTo("Corvette"));
            var design = CombatSpec.Of(ShipClass.Corvette);
            Assert.That(design.DesignName, Is.EqualTo("Corvette"));
            Assert.That(design.LengthKm, Is.LessThan(CombatSpec.Of(ShipClass.Frigate).LengthKm), "a corvette is smaller than a frigate");
            Assert.That(design.Hull, Is.LessThan(CombatSpec.Of(ShipClass.Destroyer).Hull));
            Assert.That(Crews.Complement(ShipClass.Corvette), Is.LessThan(Crews.Complement(ShipClass.Frigate)));
            Assert.That(Economy.HullPrice(ShipClass.Corvette), Is.GreaterThan(0).And.LessThan(Economy.HullPrice(ShipClass.Frigate)));
            Assert.That(HullFrames.ForClass(ShipClass.Corvette), Is.Not.Null, "no hull frame to build it from");
            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.Corvette), Is.EqualTo(FlightFamily.Capital));
            // Armed to stop what it finds: guns that bear, not only point defence.
            Assert.That(design.Layout.Any(d => d.Kind == SubsystemKind.LightRailgun), Is.True);
            Assert.That(design.Layout.Count(d => d.Kind == SubsystemKind.LaserTurret), Is.EqualTo(2));

            // No FTL drive, and no drive room: its tanks are as deep as it is heavy, for weeks on its beat...
            Assert.That(FacilityLayouts.For(ShipClass.Corvette).Any(s => s.Kind == FacilityKinds.FtlDrive), Is.False);
            Assert.That(design.PropellantTonnes / design.DryMassTonnes,
                Is.GreaterThan(CombatSpec.Of(ShipClass.Destroyer).PropellantTonnes / CombatSpec.Of(ShipClass.Destroyer).DryMassTonnes),
                "a corvette carries deeper tanks for its size than a destroyer");
            // ...and its search suite reaches twice as far as a warship's.
            var navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true };
            var corvette = Hull(ShipClass.Corvette, navy);
            var destroyer = Hull(ShipClass.Destroyer, navy);
            Assert.That(SensorSuite.Of(corvette).RangeKm, Is.EqualTo(SensorSuite.Of(destroyer).RangeKm * 2).Within(1));
            Assert.That(SensorSuite.Of(corvette).Resolution, Is.GreaterThan(SensorSuite.Of(destroyer).Resolution));
        }

        [Test]
        public void ACorvetteNeverLeavesItsHomeSystemByDriveGateOrCarriage()
        {
            var navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true };
            var corvette = Hull(ShipClass.Corvette, navy);
            Assert.That(FtlRoster.Of(ShipClass.Corvette), Is.EqualTo(FtlAccess.HomeSystem));
            Assert.That(FtlRoster.LeavesHome(corvette), Is.False);
            Assert.That(CruiseDrives.Of(corvette), Is.Null, "a corvette has no drive of its own");
            Assert.That(InterstellarTraffic.CanCruise(corvette), Is.False);
            Assert.That(HelmFtl.Refusal(corvette), Does.Contain("home system"), "the helm will not take one out of its system");
            // Nor does a carrier take it through its jump, docked or clamped.
            Assert.That(FtlCarriage.Carriable(corvette), Is.False);
            var carrier = Hull(ShipClass.Carrier, navy);
            Assert.That(FtlCarriage.Stow(new[] { carrier }, new List<Ship> { corvette }), Is.Empty);
        }

        // ---------------------------------------------------------------- stationed at the ports

        [Test]
        public void EveryNavyStationsItsCorvettesForGoodAtItsPortsWithABeatDownTheLanes()
        {
            var w = CreateWorld();
            var lanes = w.Fleet.Lanes;
            bool rideALane = false;
            int total = 0;
            foreach (var f in Navies(w))
            {
                var ports = PatrolWings.Ports(w, f);
                var mine = Corvettes.Of(w, f);
                Assert.That(mine.Count, Is.EqualTo(Corvettes.Wanted(ports.Count)), $"{f.Name} stations the wrong number of corvettes");
                Assert.That(mine.Select(c => c.CorvetteStation), Is.EquivalentTo(ports.Take(mine.Count)),
                    $"{f.Name}'s corvettes are not at its most populous ports, one to a port");
                foreach (var c in mine)
                {
                    total++;
                    Assert.That(c.Spec.Class, Is.EqualTo(ShipClass.Corvette));
                    Assert.That(c.PatrolWing, Is.True, $"{c.Name} could be merged, sent away or counted as a warship");
                    Assert.That(c.IsEscort, Is.False);
                    Assert.That(c.Route[0], Is.EqualTo(c.CorvetteStation), $"{c.Name}'s beat does not start from its station");
                    Assert.That(c.Route.All(ports.Contains), Is.True, $"{c.Name}'s beat strays off its own navy's ports");
                    Assert.That(c.Route.Distinct().Count(), Is.EqualTo(c.Route.Length));
                    // Held at its station, or already out on its beat (the world has run its first step).
                    int where = Fleet.IsAt(c, c.CorvetteStation) ? c.CorvetteStation : c.DestinationBody;
                    Assert.That(Corvettes.Polices(w, c, where), Is.True, $"{c.Name} starts off its ground, at {w.Catalog[where].Name}");
                    if (c.Route.Any(b => lanes.SystemOf(b) != lanes.SystemOf(c.CorvetteStation))) rideALane = true;
                }
                // Distinct call signs.
                Assert.That(mine.Select(c => c.Name).Distinct().Count(), Is.EqualTo(mine.Count));
            }
            Assert.That(total, Is.GreaterThanOrEqualTo(6), "Sol's navies keep too few corvettes");
            Assert.That(rideALane, Is.True, "no corvette's beat runs down a hyper lane out of its home planetary system");
            // The pirates keep none.
            Assert.That(Corvettes.Of(w, w.Pirates), Is.Empty);
        }

        [Test]
        public void OnPatrolACorvetteRunsDownEachLaneOfItsBeatInTurnAndComesBackToItsStation()
        {
            var w = CreateWorld();
            var c = Navies(w).SelectMany(f => Corvettes.Of(w, f)).First(s => s.Route.Length >= 3);
            int station = c.CorvetteStation;
            var visited = new List<int>();
            int here = station;
            for (int i = 0; i < 2 * (c.Route.Length - 1); i++)
            {
                int next = Corvettes.NextOnBeat(c, here);
                Assert.That(next, Is.Not.EqualTo(station), "it stayed at its station rather than going out on patrol");
                visited.Add(next);
                Assert.That(Corvettes.NextOnBeat(c, next), Is.EqualTo(station), "it did not come back to its station");
            }
            Assert.That(visited.Distinct(), Is.EquivalentTo(c.Route.Skip(1)), "it left part of its beat unpatrolled");

            // Commanded to fight anywhere on its beat, as a wing is; and its orders keep it there.
            foreach (int b in c.Route) Assert.That(w.War.Commanded(c, b), Is.True);
            int order = w.War.ChooseNext(c, station, StartJd);
            Assert.That(Corvettes.Polices(w, c, order), Is.True);
            Assert.That(w.War.OrderOf(c).Kind, Is.AnyOf(OrderKind.Patrol, OrderKind.Defend, OrderKind.Hunt));
        }

        // ---------------------------------------------------------------- the law

        [Test]
        public void ACorvetteRunsDownTheSmugglersAndPiratesItsNavyHasSeenOnItsGround()
        {
            var w = CreateWorld();
            var lanes = w.Fleet.Lanes;
            // A corvette, and a port round its home planet that is not on its beat (another power's moon or station).
            Ship corvette = null;
            int ground = -1;
            foreach (var c in Navies(w).SelectMany(f => Corvettes.Of(w, f)))
            {
                var spot = w.Economy.Settlements.Select(s => s.Body)
                    .FirstOrDefault(b => !c.Route.Contains(b) && lanes.SystemOf(b) == lanes.SystemOf(c.CorvetteStation));
                if (spot <= 0) continue;
                corvette = c;
                ground = spot;
                break;
            }
            Assert.That(corvette, Is.Not.Null, "no corvette in Sol has a port round its planet off its beat");
            Assert.That(Corvettes.Polices(w, corvette, ground), Is.True, "the ground round its home planet is not its to police");
            var f = corvette.Faction;
            int station = corvette.CorvetteStation;

            // A clan's smuggler, carrying loot to market, lying off that port in plain sight of the navy.
            var smuggler = w.Fleet.AddShip("Quiet Profit", ShipClass.Courier, w.Pirates, ShipRole.Free);
            smuggler.Runner = true;
            Fleet.PinAt(smuggler, ground, new DVec3(30000, 0, 0), DVec3.UnitX, StartJd);
            w.Sensors.Sweep(StartJd);
            double jd = StartJd + 0.1;
            var at = w.Ephemeris.HelioAt(station, jd);
            Assert.That(w.War.KnowsOfRaidersAt(ground, f, at, jd), Is.True, "the navy never saw the smuggler");

            int next = w.War.ChooseNext(corvette, station, jd);
            Assert.That(next, Is.EqualTo(ground), "the corvette did not go after the smuggler");
            Assert.That(w.War.OrderOf(corvette).Kind, Is.EqualTo(OrderKind.Hunt));
            Assert.That(w.War.Commanded(corvette, ground), Is.True, "it is not commanded to fight the smuggler where it found it");

            // Gone: it goes back on its beat.
            smuggler.Destroyed = true;
            w.Sensors.Sweep(jd);
            next = w.War.ChooseNext(corvette, station, jd + 0.1);
            Assert.That(w.War.OrderOf(corvette).Kind, Is.EqualTo(OrderKind.Patrol));
            Assert.That(corvette.Route.Contains(next), Is.True);

            // A raider band working a port on its beat: it goes to defend the port.
            if (corvette.Route.Length >= 2)
            {
                int port = corvette.Route[1];
                var raider = w.Fleet.AddShip("Gutter Knife", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
                w.Fleet.Station(raider, port, jd);
                Assert.That(w.War.ChooseNext(corvette, station, jd + 0.2), Is.EqualTo(port));
                Assert.That(w.War.OrderOf(corvette).Kind, Is.AnyOf(OrderKind.Defend, OrderKind.Hunt));
            }
        }

        [Test]
        public void CorvettesKeepToTheirSystemAndTheirGroundAndAreNeverMergedOrGivenObjectives()
        {
            var w = CreateWorld();
            var all = Navies(w).SelectMany(f => Corvettes.Of(w, f)).ToList();
            var stations = all.ToDictionary(c => c, c => c.CorvetteStation);
            var lanes = w.Fleet.Lanes;
            bool outOnALane = false;
            for (int day = 1; day <= 20; day++)
            {
                w.Update(StartJd + day);
                foreach (var c in all.Where(c => !c.Destroyed))
                {
                    var o = w.War.OrderOf(c);
                    if (o != null && o.Kind != OrderKind.Repair)
                        Assert.That(Corvettes.Polices(w, c, o.Body), Is.True, $"{c.Name} was sent off its ground to {w.Catalog[o.Body].Name}");
                    if (o != null && lanes.SystemOf(o.Body) != lanes.SystemOf(c.CorvetteStation)) outOnALane = true;
                }
            }
            foreach (var c in all.Where(c => !c.Destroyed))
            {
                Assert.That(w.Fleet.IsPresent(c), Is.True, $"{c.Name} left its home system");
                Assert.That(c.Voyage, Is.Null, $"{c.Name} was sent through a gate");
                Assert.That(c.GateKm, Is.Null, $"{c.Name} was sent to a gate");
                Assert.That(c.IsEscort, Is.False, $"{c.Name} was merged into a battle group");
                Assert.That(w.Fleet.Ships.Any(s => !s.Destroyed && s.Leader == c), Is.False, $"{c.Name} was given a battle group");
                Assert.That(c.CorvetteStation, Is.EqualTo(stations[c]), $"{c.Name} changed station though its port stands");
                Assert.That(w.War.IsHomeGuard(c), Is.False);
                Assert.That(InterstellarTraffic.Idle(w, c), Is.False, "a corvette is free for the traffic between the stars");
                Assert.That(w.Polities.Command.ObjectiveOf(c), Is.Null, $"{c.Name} was given an objective");
            }
            Assert.That(outOnALane, Is.True, "in three weeks no corvette rode a lane out of its home planetary system");
        }

        // ---------------------------------------------------------------- upkeep

        [Test]
        public void ALostCorvetteIsReplacedAtItsPortAtTheNavysExpense()
        {
            var w = CreateWorld();
            var f = Navies(w).First(n => Corvettes.Of(w, n).Count > 0);
            var mine = Corvettes.Of(w, f);
            var lost = mine[0];
            int port = lost.CorvetteStation;
            lost.Destroyed = true;
            f.Treasury = 1e9;
            var shortSince = new Dictionary<Faction, double>();
            Corvettes.Upkeep(w, shortSince, StartJd);
            Assert.That(Corvettes.Of(w, f).Count, Is.EqualTo(mine.Count - 1), "replaced before the navy could commission one");
            Corvettes.Upkeep(w, shortSince, StartJd + Corvettes.RebuildDays - 1);
            Assert.That(Corvettes.Of(w, f).Count, Is.EqualTo(mine.Count - 1));
            Corvettes.Upkeep(w, shortSince, StartJd + Corvettes.RebuildDays + 0.5);
            var now = Corvettes.Of(w, f);
            Assert.That(now.Count, Is.EqualTo(mine.Count), "the lost corvette was never replaced");
            var fresh = now.Single(c => !mine.Contains(c));
            Assert.That(fresh.CorvetteStation, Is.EqualTo(port), "the new corvette went somewhere other than the port left without one");
            Assert.That(f.Treasury, Is.EqualTo(1e9 - Economy.HullPrice(ShipClass.Corvette)).Within(1e-3), "the navy did not pay for it");
            Assert.That(shortSince.ContainsKey(f), Is.False);
        }

        [Test]
        public void ACorvetteWhosePortChangesHandsIsPostedToAnotherOfItsNavysPorts()
        {
            var w = CreateWorld();
            var f = Navies(w).First(n => Corvettes.Of(w, n).Count > 0 && PatrolWings.Ports(w, n).Count >= 2);
            var c = Corvettes.Of(w, f)[0];
            int lostPort = c.CorvetteStation;
            var other = Navies(w).First(n => n != f);
            foreach (var s in w.Economy.At(lostPort))
                if (s.Owner == f) s.Owner = other;
            Corvettes.Upkeep(w, new Dictionary<Faction, double>(), StartJd + 1);
            Assert.That(c.CorvetteStation, Is.Not.EqualTo(lostPort), "it stayed at a port its navy has lost");
            Assert.That(PatrolWings.Ports(w, f), Does.Contain(c.CorvetteStation));
            Assert.That(c.Route[0], Is.EqualTo(c.CorvetteStation), "its beat was not drawn again from its new station");
        }
    }
}
