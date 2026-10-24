using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The Breacher as a fired boarding round (U.SPC-T466): a whole marine team in an armoured round, fired from an Assault
    /// Bay fitted beside a Marine Habitat that supplies the teams, flying like a bullet with only a slight course correction
    /// until it strikes a hull, drills in and lets its team out into the compartment it hit, where the boarding fight takes
    /// over. The bay reloads only as fast as the habitats ready the next team, and the Breacher is no longer a flown class.
    /// </summary>
    [TestFixture]
    public class AssaultBayTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.2 };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };

        static Ship Hull(ShipClass c, Faction f, int id) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Battle NewBattle(int seed) => new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed)
        {
            Launch = FighterAI.Launch,
            FighterStep = FighterAI.Step,
        };

        static Ship Raider()
        {
            var s = Hull(ShipClass.Cruiser, Red, 0);
            Assert.That(AssaultBays.Equip(s), Is.True, "the cruiser took no assault bay");
            return s;
        }

        /// <summary>A merchant lying dead in space, its shields gone: open to a round, and held fire on by raiders robbing it.</summary>
        static BattleUnit Prize(Battle b, DVec3 at)
        {
            var u = b.AddShip(Hull(ShipClass.Freighter, Blue, 1), 1, at, DVec3.UnitX);
            foreach (var s in u.Combat.Subsystems)
            {
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Hp = 0;
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            }
            u.Combat.ZeroAllShields();
            u.MoraleThreshold = double.MaxValue;
            return u;
        }

        static Missile Round(Battle b) => b.Missiles.FirstOrDefault(m => m.Kind == MissileKind.Breacher && !m.Gone);

        // ---------------------------------------------------------------- the facility

        [Test]
        public void TheAssaultBayIsACapitalsFacilityFittedOnlyBesideAMarineHabitat()
        {
            var bay = FacilityKinds.Of(FacilityKinds.AssaultBay);
            Assert.That(bay, Is.SameAs(AssaultBays.Kind));
            Assert.That(bay.Fittable, Is.True);
            Assert.That(bay.Size, Is.EqualTo(2));
            Assert.That(bay.Compartment, Is.EqualTo(CompartmentKind.AssaultBay));
            Assert.That(bay.Requires, Is.EqualTo(FacilityKinds.MarineHabitat));
            Assert.That(bay.Bill.Length, Is.GreaterThan(0));

            var cruiser = new ConstructionPlan(HullFrames.ForClass(ShipClass.Cruiser));
            Assert.That(cruiser.FitFacility(bay), Is.EqualTo(ConstructionOutcome.Ok), "a cruiser has a habitat and the room");
            // Its habitat supplies the bay, so it cannot be stripped from under it.
            int habitat = cruiser.Facilities.ToList().FindIndex(f => f.Kind == FacilityKinds.MarineHabitat);
            Assert.That(cruiser.RemoveFacility(habitat), Is.EqualTo(ConstructionOutcome.WontFit));

            // A destroyer stripped of its habitat: no bay until one is fitted to supply it.
            var stripped = new ConstructionPlan(HullFrames.ForClass(ShipClass.Destroyer));
            Assert.That(stripped.RemoveFacility(stripped.Facilities.ToList().FindIndex(f => f.Kind == FacilityKinds.MarineHabitat)), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(stripped.FitFacility(bay), Is.EqualTo(ConstructionOutcome.WontFit));
            Assert.That(stripped.FitFacility(MarineHabitats.Kind), Is.EqualTo(ConstructionOutcome.Ok));
            Assert.That(stripped.FitFacility(bay), Is.EqualTo(ConstructionOutcome.Ok));

            // Fighters, small craft and haulers fire no rounds.
            foreach (var c in new[] { ShipClass.Gunship, ShipClass.Courier, ShipClass.Freighter, ShipClass.Fighter })
                Assert.That(new ConstructionPlan(HullFrames.ForClass(c)).FitFacility(bay), Is.Not.EqualTo(ConstructionOutcome.Ok), c.ToString());
        }

        [Test]
        public void AHabitatReadiesTeamsAndTheBayFiresNoFasterThanItDoes()
        {
            var cruiser = Raider();
            Assert.That(AssaultBays.Count(cruiser), Is.EqualTo(1));
            Assert.That(AssaultBays.Habitats(cruiser), Is.EqualTo(1));
            Assert.That(AssaultBays.IntervalSeconds(cruiser),
                Is.EqualTo(Math.Max(AssaultBays.CycleSeconds.Value, AssaultBays.ReadySeconds.Value)).Within(1e-9));
            double one = AssaultBays.IntervalSeconds(cruiser);
            Assert.That(Outfitting.Install(cruiser, MarineHabitats.Kind), Is.True);
            Assert.That(AssaultBays.IntervalSeconds(cruiser), Is.LessThan(one), "a second habitat readies teams faster");

            // A destroyer's one habitat quarters fewer than a team: a raider's fits a second to supply its bay.
            var destroyer = Hull(ShipClass.Destroyer, Red, 3);
            Assert.That(AssaultBays.Equip(destroyer), Is.True);
            Assert.That(AssaultBays.Habitats(destroyer), Is.EqualTo(2));
            Assert.That(destroyer.Combat.Spec.Marines, Is.GreaterThanOrEqualTo(AssaultBays.TeamSize.Value));

            Assert.That(AssaultBays.IntervalSeconds(Hull(ShipClass.Cruiser, Red, 4)), Is.EqualTo(double.PositiveInfinity), "no bay, no rounds");
        }

        // ---------------------------------------------------------------- in battle

        [Test]
        public void ABreacherRoundIsFiredFromTheBayDrillsInAndItsTeamTakesTheShip()
        {
            var b = NewBattle(5);
            var raider = b.AddShip(Raider(), 0, new DVec3(8, 0, 0), -DVec3.UnitX);
            var prize = Prize(b, new DVec3(-4, 0, 0));
            double ready = MarineHabitats.Ready(raider.Combat.Interior);
            double team = AssaultBays.TeamSize.Value;

            Missile round = null;
            for (int i = 0; i < 50 && round == null; i++)
            {
                b.Advance(0.1);
                round = Round(b);
            }
            Assert.That(round, Is.Not.Null, "the assault bay never fired");
            Assert.That(round.Marines, Is.EqualTo(team).Within(1e-9), "a whole team rides in the round");
            Assert.That(round.Source, Is.SameAs(raider));
            Assert.That(raider.AssaultBayReload, Is.GreaterThan(AssaultBays.IntervalSeconds(raider.Ship) - 1));
            Assert.That((round.Vel - raider.Vel).Length, Is.EqualTo(AssaultBays.RoundSpeedKmS.Value).Within(1e-6), "fired at the bay's velocity");

            for (int i = 0; i < 200 && !round.Gone; i++) b.Advance(0.1);
            Assert.That(round.Gone, Is.True);
            Assert.That(b.Embedded.Any(e => e.Breacher && e.Unit == prize), Is.True, "the round is not stuck in the hull it struck");
            Assert.That(prize.Combat.Interior.Boarder, Is.SameAs(Red), "the team did not storm out");
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.Breach && f.Unit == prize), Is.True);

            for (int i = 0; i < 3000 && !prize.Captured; i++) b.Advance(1);
            foreach (var note in prize.Combat.Interior.Log) TestContext.Out.WriteLine(note.Text);
            Assert.That(prize.Captured, Is.True, "the team did not take the freighter");
            Assert.That(prize.Side, Is.EqualTo(0));
        }

        [Test]
        public void TheTeamStormsOutIntoTheCompartmentTheRoundStruck()
        {
            // Two rounds into a battleship, one from ahead and one from astern: each team lands where its round went in.
            foreach (var (from, nearBow) in new[] { (new DVec3(10, 0, 0), true), (new DVec3(-10, 0, 0), false) })
            {
                var b = NewBattle(9);
                var raider = b.AddShip(Raider(), 0, from, -from.Normalized);
                var target = b.AddShip(Hull(ShipClass.Battleship, Blue, 1), 1, DVec3.Zero, DVec3.UnitX);
                foreach (var s in target.Combat.Subsystems)
                {
                    if (s.Kind == SubsystemKind.ShieldGenerator || s.Kind == SubsystemKind.Engine) s.Hp = 0;
                    if (s.Def.IsWeapon) s.Hp = 0;
                }
                target.Combat.ZeroAllShields();
                target.MoraleThreshold = double.MaxValue;
                for (int i = 0; i < 300 && target.Combat.Interior.Boarder == null; i++)
                {
                    target.Pos = DVec3.Zero;
                    target.Vel = DVec3.Zero;
                    target.Forward = DVec3.UnitX;
                    b.Advance(0.1);
                }
                var breach = b.Fx.Last(f => f.Kind == FxKind.Breach && f.Unit == target);
                var landed = target.Combat.Interior.Compartments[breach.Sub];
                TestContext.Out.WriteLine($"from {from}: landed in {landed.Name} at {landed.Z:0.00}");
                Assert.That(landed.Z > 0, Is.EqualTo(nearBow), "the team landed at the wrong end of the ship");
            }
        }

        [Test]
        public void ARoundFliesLikeABulletAndATargetThatJinksAsideIsMissedAndTheTeamLost()
        {
            var b = NewBattle(6);
            var raider = b.AddShip(Raider(), 0, new DVec3(12, 0, 0), -DVec3.UnitX);
            var prize = Prize(b, new DVec3(-2, 0, 0));
            Missile round = null;
            for (int i = 0; i < 50 && round == null; i++)
            {
                b.Advance(0.1);
                round = Round(b);
            }
            Assert.That(round, Is.Not.Null);
            double ready = MarineHabitats.Ready(raider.Combat.Interior);
            // Two km aside the moment it is fired: a missile would turn after it, the round can barely bend its track.
            prize.Pos += new DVec3(0, 2, 0);
            for (int i = 0; i < 400 && !round.Gone; i++)
            {
                prize.Vel = DVec3.Zero;
                b.Advance(0.1);
            }
            Assert.That(round.Gone, Is.True, "the round never ran out its flight");
            Assert.That(b.Embedded.Any(e => e.Breacher), Is.False, "the round struck a target it could not have reached");
            Assert.That(prize.Combat.Interior.Boarder, Is.Null);
            Assert.That(MarineHabitats.Ready(raider.Combat.Interior), Is.LessThanOrEqualTo(ready + 1e-9), "the lost team came home");
        }

        [Test]
        public void ShieldsHoldARoundOffAndTheBayWaitsForThemToFall()
        {
            var b = NewBattle(7);
            var raider = b.AddShip(Raider(), 0, new DVec3(8, 0, 0), -DVec3.UnitX);
            var target = b.AddShip(Hull(ShipClass.Destroyer, Blue, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
            foreach (var s in target.Combat.Subsystems)
            {
                if (s.Def.IsWeapon) s.Hp = 0;
                if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            }
            target.MoraleThreshold = double.MaxValue;
            // Shields up: the bay holds its team.
            raider.Combat.Subsystems.Where(s => s.Def.IsWeapon).ToList().ForEach(s => s.Hp = 0);
            for (int i = 0; i < 30; i++)
            {
                foreach (var s in target.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator)) s.Shield = s.MaxShield;
                b.Advance(0.1);
            }
            Assert.That(b.Missiles.Any(m => m.Kind == MissileKind.Breacher), Is.False, "fired into full shields");

            // Down: it fires. Raised again before it lands: it glances off.
            target.Combat.ZeroAllShields();
            Missile round = null;
            for (int i = 0; i < 50 && round == null; i++)
            {
                b.Advance(0.1);
                round = Round(b);
            }
            Assert.That(round, Is.Not.Null, "the bay did not fire once the shields fell");
            for (int i = 0; i < 200 && !round.Gone; i++)
            {
                foreach (var s in target.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.ShieldGenerator)) s.Shield = s.MaxShield;
                b.Advance(0.1);
            }
            Assert.That(b.Fx.Any(f => f.Kind == FxKind.PodBounce && f.Unit == target), Is.True, "the round was not turned aside");
            Assert.That(target.Combat.Interior.Boarder, Is.Null);
        }

        [Test]
        public void NoBayFiresWithoutAWholeTeamReady()
        {
            var b = NewBattle(8);
            var raider = b.AddShip(Raider(), 0, new DVec3(8, 0, 0), -DVec3.UnitX);
            Prize(b, new DVec3(-4, 0, 0));
            MarineHabitats.Draw(raider.Combat.Interior, MarineHabitats.Ready(raider.Combat.Interior) - AssaultBays.TeamSize.Value + 1);
            for (int i = 0; i < 50; i++) b.Advance(0.1);
            Assert.That(b.Missiles.Any(m => m.Kind == MissileKind.Breacher), Is.False);
        }

        // ---------------------------------------------------------------- no longer a flown class

        [Test]
        public void TheBreacherIsAFiredRoundNotAFlownShip()
        {
            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.Breacher), Is.EqualTo(FlightFamily.Ballistic));

            // At the helm the round coasts: no throttle, and the stick only bends its track a little.
            var model = new BallisticFlightModel();
            var s = PilotState.At(DVec3.Zero, new DVec3(2, 0, 0), DVec3.UnitX);
            var input = new PilotInput { DesiredForward = DVec3.UnitY, Throttle = 1, Boost = true, StrafeRight = 1 };
            var spec = CombatSpec.Of(ShipClass.Breacher);
            for (int i = 0; i < 100; i++)
                model.Step(ref s, null, HullDynamics.Of(spec), PilotTuning.Of(ShipClass.Breacher), input, DVec3.Zero, 0.1);
            Assert.That(s.Vel.Length, Is.EqualTo(2).Within(1e-9), "the round sped up or slowed");
            double turned = Math.Acos(Math.Min(1, s.Vel.Normalized.X)) * Astro.Rad2Deg;
            Assert.That(turned, Is.GreaterThan(0).And.LessThan(10), "more than a slight correction in ten seconds");
            Assert.That(DVec3.Dot(s.Forward, s.Vel.Normalized), Is.EqualTo(1).Within(1e-9), "its nose is along its track");

            // No wing, patrol or raider band flies one.
            for (int i = 0; i < 4; i++)
            {
                Assert.That(PatrolWings.Composition(i), Has.No.Member(ShipClass.Breacher));
                Assert.That(PatrolWings.ConvoyEscort(i), Has.No.Member(ShipClass.Breacher));
            }
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            Assert.That(w.Fleet.Ships.Any(x => x.Spec.Class == ShipClass.Breacher), Is.False, "a breacher was raised as a ship");
            // A raider band's leader is the hull its lane interdictor (the escort fitted with a field projector) keeps station on.
            var leaders = w.Fleet.Ships.Where(x => x.Faction.IsPirate && w.Fleet.Ships.Any(e => e.Leader == x && LaneInterdictors.Carries(e))).ToList();
            Assert.That(leaders, Is.Not.Empty);
            Expect.All(leaders, l => Assert.That(AssaultBays.Count(l), Is.EqualTo(1), l.Name + " leads a band with no assault bay"));
        }

        [Test]
        public void TheBreacherTunablesAreInTheSettingsMenu()
        {
            SettingsRegistry.DiscoverLoadedDeclarations(); // the menu runs this at startup
            foreach (var key in new[] { "war.breacherTeam", "war.breacherReady", "war.breacherCycle", "war.breacherSpeed", "war.breacherSteer",
                                        "war.breacherRange", "war.breacherImpact" })
                Assert.That(SettingsRegistry.Default.Find(key), Is.Not.Null, key);
            Assert.That(SettingsRegistry.Default.Find("war.breacherTeam"), Is.SameAs(AssaultBays.TeamSize));
        }
    }
}
