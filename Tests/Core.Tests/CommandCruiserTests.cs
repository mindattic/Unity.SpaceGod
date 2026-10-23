using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The command cruiser (U.SPC-T477): a capital built round a sensor array, a communications suite and targeting
    /// computers rather than guns; every ship on its side within its reach fights coordinated, losing it throws them into
    /// confusion, hostile gunners go for it first, and navies build it to lead their groups.
    /// </summary>
    [TestFixture]
    public class CommandCruiserTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static int Weapons(CombatSpec s) => s.Layout.Count(d => d.IsWeapon && d.Kind != SubsystemKind.PointDefense);

        // ---------------------------------------------------------------- the class

        [Test]
        public void ItIsACapitalBetweenACruiserAndABattleshipThatGivesUpGunsForCommand()
        {
            var cc = CombatSpec.Of(ShipClass.CommandCruiser);
            var cruiser = CombatSpec.Of(ShipClass.Cruiser);
            var battleship = CombatSpec.Of(ShipClass.Battleship);
            Assert.That(ShipClassSpec.Of(ShipClass.CommandCruiser).IsMilitary, Is.True);
            Assert.That(ShipClassSpec.Of(ShipClass.CommandCruiser).DisplayName, Is.EqualTo("Command cruiser"));
            Assert.That(cc.Hull, Is.GreaterThan(cruiser.Hull).And.LessThan(battleship.Hull));
            Assert.That(cc.LengthKm, Is.GreaterThan(cruiser.LengthKm).And.LessThan(battleship.LengthKm));
            Assert.That(Weapons(cc), Is.LessThan(Weapons(cruiser)), "it gives up guns for its command facilities");
            Assert.That(cc.Crew, Is.GreaterThan(cruiser.Crew), "the fleet's staff is aboard");
            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.CommandCruiser), Is.EqualTo(FlightFamily.Capital), "it flies as a capital, in full 3D");
            Assert.That(Economy.HullCost(ShipClass.CommandCruiser), Is.GreaterThan(Economy.HullCost(ShipClass.Cruiser)));
            Assert.That(Crews.Complement(ShipClass.CommandCruiser), Is.EqualTo(cc.Crew + cc.Marines));
            Assert.That(HullFrames.ForClass(ShipClass.CommandCruiser), Is.Not.Null);
            Assert.That(HullFrames.CategoryOf(ShipClass.CommandCruiser), Is.EqualTo(HullCategory.Warship));
        }

        [Test]
        public void ItsSensorArrayCommunicationsSuiteAndTargetingComputersAreFacilitiesInsideTheHull()
        {
            var layout = FacilityLayouts.For(ShipClass.CommandCruiser);
            Assert.That(layout.Count(f => f.Kind == FacilityKinds.SensorArray), Is.EqualTo(1));
            Assert.That(layout.Count(f => f.Kind == FacilityKinds.CommsSuite), Is.EqualTo(1));
            Assert.That(layout.Count(f => f.Kind == FacilityKinds.TargetingComputers), Is.EqualTo(2));
            Assert.That(FleetCoordination.IsCommandClass(ShipClass.CommandCruiser), Is.True);
            Assert.That(FleetCoordination.IsCommandClass(ShipClass.Cruiser), Is.False);

            foreach (var id in new[] { FacilityKinds.SensorArray, FacilityKinds.CommsSuite, FacilityKinds.TargetingComputers })
            {
                var kind = FacilityKinds.Of(id);
                Assert.That(kind, Is.Not.Null, id);
                Assert.That(kind.Fittable, Is.True, id + " is a yard's to fit into another capital");
                Assert.That(kind.AllowedOn(HullCategory.Battleship), Is.True);
                Assert.That(kind.AllowedOn(HullCategory.Fighter), Is.False);
                Assert.That(kind.AllowedOn(HullCategory.Freighter), Is.False);
                Assert.That(kind.Compartment, Is.Null, "equipment, not a space of its own");
            }
            Assert.That(FacilityKinds.Of(FacilityKinds.SensorArray).Size, Is.EqualTo(2));

            // A battleship with a suite fitted into its free space commands too.
            var bb = Hull(ShipClass.Battleship, Blue, 1);
            Assert.That(FleetCoordination.IsCommandShip(bb), Is.False);
            Assert.That(Outfitting.Install(bb, FacilityKinds.Of(FacilityKinds.CommsSuite)), Is.True);
            Assert.That(FleetCoordination.IsCommandShip(bb), Is.True);

            // The interior is built from the facilities: the flag bridge and the staff's quarters are there.
            var cc = Hull(ShipClass.CommandCruiser, Blue, 2);
            Assert.That(cc.Combat.Interior.Compartments.Any(c => c.Name == "Flag bridge" && c.Kind == CompartmentKind.Bridge), Is.True);
            Assert.That(cc.Combat.Interior.Compartments.Any(c => c.Name == "Staff quarters"), Is.True);
        }

        [Test]
        public void ItsSensorArrayOutranksAWarshipsScanners()
        {
            var cc = SensorSuite.Of(Hull(ShipClass.CommandCruiser, Blue, 1));
            var cruiser = SensorSuite.Of(Hull(ShipClass.Cruiser, Blue, 2));
            Assert.That(cc.RangeKm, Is.GreaterThan(cruiser.RangeKm * 2));
            Assert.That(cc.Resolution, Is.GreaterThan(cruiser.Resolution));
        }

        [Test]
        public void ItCrossesBetweenTheStarsOnlyWithAnFtlDriveAndCanCarrySmallCraft()
        {
            // As a battleship does: by the drive its design is fitted with (U.SPC-T472 sets who has one).
            Assert.That(CruiseDrives.Of(ShipClass.CommandCruiser), Is.Not.Null);
            Assert.That(FtlCarriage.BerthsOf(ShipClass.CommandCruiser), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- coordination in battle

        static (Battle b, BattleUnit cmd, BattleUnit near, BattleUnit far, BattleUnit enemy) Line(int seed = 1)
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            var cmd = b.AddShip(Hull(ShipClass.CommandCruiser, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            var near = b.AddShip(Hull(ShipClass.Destroyer, Blue, 1), 0, new DVec3(0, 4, 3), DVec3.UnitX);
            var far = b.AddShip(Hull(ShipClass.Destroyer, Blue, 2), 0, new DVec3(-FleetCoordination.ReachKm.Value - 15, 0, 0), DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, Red, 3), 1, new DVec3(12, 0, 0), -DVec3.UnitX);
            return (b, cmd, near, far, enemy);
        }

        [Test]
        public void ShipsWithinItsReachFightCoordinatedAndThoseBeyondItDoNot()
        {
            var (b, cmd, near, far, enemy) = Line();
            FleetCoordination.Step(b, 1);
            Assert.That(near.CommandedBy, Is.SameAs(cmd));
            Assert.That(cmd.CommandedBy, Is.SameAs(cmd), "its own staff coordinate its guns too");
            Assert.That(far.CommandedBy, Is.Null, "beyond its reach");
            Assert.That(enemy.CommandedBy, Is.Null, "never the other side's");

            Assert.That(FleetCoordination.AccuracyFactor(near), Is.EqualTo(1 + 2 * FleetCoordination.AccuracyPerComputer.Value).Within(1e-9),
                "two targeting computers");
            Assert.That(FleetCoordination.RetargetFactor(near), Is.LessThan(1), "it finds its marks sooner");
            Assert.That(FleetCoordination.SharedPicture(near), Is.GreaterThan(0), "it lays its guns on the shared picture");
            Assert.That(FleetCoordination.AccuracyFactor(far), Is.EqualTo(1));
            Assert.That(FleetCoordination.RetargetFactor(far), Is.EqualTo(1));
            Assert.That(FleetCoordination.SharedPicture(far), Is.EqualTo(0));
            // Everything in full 3D: the escort above and astern of the line is as much in reach as one beside it.
            Assert.That(near.Pos.Z, Is.Not.EqualTo(0));
        }

        [Test]
        public void ACoordinatedShipConcentratesOnTheHullItsCommandShipIsFighting()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var cmd = b.AddShip(Hull(ShipClass.CommandCruiser, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
                var escort = b.AddShip(Hull(ShipClass.Destroyer, Blue, 1), 0, new DVec3(0, 0, 3), DVec3.UnitX);
                var mark = b.AddShip(Hull(ShipClass.Cruiser, Red, 2), 1, new DVec3(13, 0, 3), -DVec3.UnitX);
                var nearer = b.AddShip(Hull(ShipClass.Cruiser, Red, 3), 1, new DVec3(-6, 0, 3), DVec3.UnitX);
                cmd.Target = mark;
                FleetCoordination.Step(b, 1);
                Assert.That(FleetCoordination.FocusTarget(escort), Is.SameAs(mark));
                Assert.That(b.PickTarget(escort), Is.SameAs(mark), $"seed {seed}: the command ship's mark, not the nearer hull");
            }
        }

        [Test]
        public void HostileGunnersGoForTheCommandShipFirst()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var cmd = b.AddShip(Hull(ShipClass.CommandCruiser, Blue, 0), 0, new DVec3(0, 10, 0), DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Battleship, Blue, 1), 0, new DVec3(0, -10, 0), DVec3.UnitX);
                var gunner = b.AddShip(Hull(ShipClass.Destroyer, Red, 2), 1, DVec3.Zero, -DVec3.UnitX);
                Assert.That(b.PickTarget(gunner), Is.SameAs(cmd), $"seed {seed}");
            }

            // A command ship that no longer coordinates (its flag bridge shot away) is just another hull.
            var b2 = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            var broken = b2.AddShip(Hull(ShipClass.CommandCruiser, Blue, 0), 0, new DVec3(0, 10, 0), DVec3.UnitX);
            foreach (var s in broken.Combat.Subsystems)
                if (s.Kind == SubsystemKind.Bridge) s.Hp = 0;
            Assert.That(FleetCoordination.Coordinates(broken), Is.False);
        }

        [Test]
        public void LosingTheCommandShipThrowsItsFleetIntoConfusionForAWhile()
        {
            var (b, cmd, near, far, _) = Line();
            FleetCoordination.Step(b, 1);
            Assert.That(near.CommandedBy, Is.SameAs(cmd));

            cmd.Dead = true;
            FleetCoordination.Step(b, 1);
            Assert.That(near.CommandedBy, Is.Null);
            Assert.That(near.Disrupted, Is.EqualTo(FleetCoordination.DisruptionSeconds.Value));
            Assert.That(far.Disrupted, Is.EqualTo(0), "a ship it was not coordinating loses nothing");
            Assert.That(FleetCoordination.AccuracyFactor(near), Is.LessThan(1), "its gunners shoot worse");
            Assert.That(FleetCoordination.RetargetFactor(near), Is.GreaterThan(1), "and find their targets slower");

            // The confusion wears off.
            for (int i = 0; i < FleetCoordination.DisruptionSeconds.Value + 1; i++) FleetCoordination.Step(b, 1);
            Assert.That(near.Disrupted, Is.EqualTo(0));
            Assert.That(FleetCoordination.AccuracyFactor(near), Is.EqualTo(1));
        }

        [Test]
        public void SailingOutOfItsReachIsNoLossOfCommand()
        {
            var (b, cmd, near, _, _) = Line();
            FleetCoordination.Step(b, 1);
            near.Pos = new DVec3(0, FleetCoordination.ReachKm.Value + 5, 0);
            FleetCoordination.Step(b, 1);
            Assert.That(near.CommandedBy, Is.Null);
            Assert.That(near.Disrupted, Is.EqualTo(0));
            Assert.That(FleetCoordination.AccuracyFactor(near), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- navies field it

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        [Test]
        public void NaviesBuildCommandCruisersAndANewOneTakesCommandOfAGroupAtItsYard()
        {
            Assert.That(BuildPlan.Balanced.Cycle, Does.Contain(ShipClass.CommandCruiser));
            Assert.That(BuildPlan.BattleLine.Cycle, Does.Contain(ShipClass.CommandCruiser));
            Assert.That(BuildPlan.Mobilise.Cycle, Does.Contain(ShipClass.CommandCruiser));

            var w = CreateWorld();
            var f = w.Fleet.AddFaction("Command Navy", "CMD", default);
            f.IsMilitary = true;
            int body = w.Catalog["Pluto"].Index;
            var yard = w.Economy.Add("Command Yard", body, SettlementKind.Station, f, population: 1000);

            var flag = w.Fleet.AddShip("Old Flag", ShipClass.Battleship, f, ShipRole.Patrol);
            flag.Orbit = new ParkingOrbit { Body = body };
            flag.Phase = ShipPhase.Parked;
            flag.PhaseStartJd = StartJd;
            flag.Route = new[] { body };
            var escort = w.Fleet.AddShip("Picket", ShipClass.Destroyer, f, ShipRole.Escort);
            w.Fleet.AddEscort(flag, escort, Core.Fleet.SlotFor(0));

            var cc = w.War.LaunchWarship(yard, ShipClass.CommandCruiser, StartJd);
            Assert.That(cc.Spec.Class, Is.EqualTo(ShipClass.CommandCruiser));
            Assert.That(cc.IsEscort, Is.False, "it leads the group");
            Assert.That(flag.Leader, Is.SameAs(cc), "the old flagship falls in as its escort");
            Assert.That(escort.Leader, Is.SameAs(cc));
            Assert.That(cc.Route, Is.EqualTo(new[] { body }), "it takes over the group's patrol");
            Assert.That(cc.Phase, Is.EqualTo(ShipPhase.Parked));

            // A second one finds the group already led from a command ship, and joins it.
            var second = w.War.LaunchWarship(yard, ShipClass.CommandCruiser, StartJd);
            Assert.That(second.Leader, Is.SameAs(cc));
        }
    }
}
