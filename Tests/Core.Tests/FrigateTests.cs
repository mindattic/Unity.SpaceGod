using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The frigate (U.SPC-T475): a fast, lightly armoured, engine-heavy missile and torpedo boat with deep magazines of
    /// long-range anti-ship missiles and antimatter (photon) torpedoes, whose doctrine is the alpha strike: burn in, loose one
    /// devastating volley, flip and burn back out. No FTL drive: the gates, or clamped to a carrier's hull.
    /// </summary>
    [TestFixture]
    public class FrigateTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static void Knock(BattleUnit u, Func<SubsystemKind, bool> which)
        {
            foreach (var s in u.Combat.Subsystems)
                if (which(s.Kind)) s.Hp = 0;
        }

        static bool IsWeapon(SubsystemKind k) => new SubsystemDef(k, 0, 0, 0).IsWeapon;

        /// <summary>An unarmed, unmoving capital-sized mark: a defence platform with its guns knocked out.</summary>
        static BattleUnit Hulk(Battle b, DVec3 pos)
        {
            var u = b.NewUnit(UnitKind.Platform, 1, Red, new ShipCombat(CombatSpec.Platform), CombatSpec.PlatformLengthKm, pos, -DVec3.UnitX);
            Knock(u, IsWeapon);
            return u;
        }

        static double Gap(BattleUnit a, BattleUnit b) => (b.Pos - a.Pos).Length - b.Radius;

        // ---------------------------------------------------------------- the class

        [Test]
        public void ItIsAFastLightlyArmouredEngineHeavyWarship()
        {
            var frigate = CombatSpec.Of(ShipClass.Frigate);
            var destroyer = CombatSpec.Of(ShipClass.Destroyer);
            Assert.That(ShipClassSpec.Of(ShipClass.Frigate).IsMilitary, Is.True);
            Assert.That(ShipClassSpec.Of(ShipClass.Frigate).DisplayName, Is.EqualTo("Frigate"));
            Assert.That(ShipClassSpec.Of(ShipClass.Frigate).AccelG, Is.GreaterThan(ShipClassSpec.Of(ShipClass.Destroyer).AccelG));

            Assert.That(frigate.MaxSpeed, Is.GreaterThan(destroyer.MaxSpeed), "faster than any warship");
            Assert.That(frigate.Accel, Is.GreaterThan(destroyer.Accel));
            Assert.That(HullDynamics.Of(frigate).MainAccel, Is.GreaterThan(HullDynamics.Of(destroyer).MainAccel));
            Assert.That(frigate.Layout.Count(d => d.Kind == SubsystemKind.Engine), Is.GreaterThan(destroyer.Layout.Count(d => d.Kind == SubsystemKind.Engine)),
                "engine-heavy");
            Assert.That(frigate.Hull, Is.LessThan(destroyer.Hull), "lightly armoured");
            Assert.That(frigate.Shield, Is.LessThan(destroyer.Shield));
            Assert.That(frigate.Armor, Is.LessThan(destroyer.Armor));
            Assert.That(frigate.LengthKm, Is.LessThan(destroyer.LengthKm));

            Assert.That(FlightModelRegistry.Default.FamilyOf(ShipClass.Frigate), Is.EqualTo(FlightFamily.Capital));
            Assert.That(HullFrames.ForClass(ShipClass.Frigate), Is.Not.Null);
            Assert.That(HullFrames.CategoryOf(ShipClass.Frigate), Is.EqualTo(HullCategory.Warship));
            Assert.That(Crews.Complement(ShipClass.Frigate), Is.EqualTo(frigate.Crew + frigate.Marines));
            Assert.That(frigate.Marines, Is.EqualTo(0), "no marines: it does not board");
        }

        [Test]
        public void ItsBatteryIsLongRangeMissilesAndAntimatterTorpedoesFromDeepMagazines()
        {
            var layout = CombatSpec.Of(ShipClass.Frigate).Layout;
            var racks = layout.Where(d => d.Kind == SubsystemKind.MissileLauncher).ToList();
            var tubes = layout.Where(d => d.Kind == SubsystemKind.AntimatterTorpedo).ToList();
            Assert.That(racks.Count, Is.EqualTo(4));
            Assert.That(tubes.Count, Is.EqualTo(2));
            // Nothing but launchers and point defence: no guns to trade fire with.
            Assert.That(layout.Where(d => d.IsWeapon && d.Kind != SubsystemKind.PointDefense).All(d => d.Weapon.Missile), Is.True);

            foreach (var r in racks)
            {
                Assert.That(r.Weapon.RangeKm, Is.GreaterThan(WeaponSpec.Missiles.RangeKm), "long-range anti-ship missiles");
                Assert.That(r.Weapon.Ammo, Is.GreaterThan(WeaponSpec.Missiles.Ammo), "a deep magazine");
            }
            foreach (var t in tubes)
            {
                Assert.That(t.Weapon.Damage, Is.EqualTo(WeaponSpec.AntimatterTorpedo.Damage), "the capitals' antimatter warhead");
                Assert.That(t.Weapon.Ammo, Is.GreaterThan(WeaponSpec.AntimatterTorpedo.Ammo), "a deep magazine");
            }
            // Every launcher bears forward: it strikes nose-on.
            var frigate = CombatSpec.Of(ShipClass.Frigate);
            Assert.That(racks.Concat(tubes).All(d => d.Covers(frigate, FireCones.Bow) && !d.Covers(frigate, FireCones.Stern)), Is.True);

            // A hull keeps its deep magazines: a new hull's launchers are loaded to them.
            var ship = Hull(ShipClass.Frigate, Blue, 1);
            Assert.That(ship.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.MissileLauncher).All(s => s.Ammo == AlphaStrike.AntiShipMissile.Ammo), Is.True);
            Assert.That(ship.Combat.Subsystems.Where(s => s.Kind == SubsystemKind.AntimatterTorpedo).All(s => s.Ammo == AlphaStrike.MagazineTorpedo.Ammo), Is.True);

            // The magazines are spaces inside the hull a boarder fights through.
            Assert.That(ship.Combat.Interior.Compartments.Count(c => c.Kind == CompartmentKind.Battery), Is.EqualTo(2));
        }

        [Test]
        public void ItHasNoFtlDriveAndCrossesByTheGatesOrClampedToACarrier()
        {
            Assert.That(FtlRoster.Of(ShipClass.Frigate), Is.EqualTo(FtlAccess.Gates));
            Assert.That(FtlRoster.LeavesHome(ShipClass.Frigate), Is.True);
            Assert.That(CruiseDrives.Of(ShipClass.Frigate), Is.Null);
            var navy = new Faction("Navy", "NV", new Rgb(0, 0, 1)) { IsMilitary = true };
            var frigate = new Ship { Name = "Kite", Spec = ShipClassSpec.Of(ShipClass.Frigate), Faction = navy };
            Assert.That(CruiseDrives.Of(frigate), Is.Null);
            Assert.That(InterstellarTraffic.CanCruise(frigate), Is.False);
            Assert.That(FtlRoster.NoDriveReason(frigate), Does.Contain("gates").And.Contain("carrier"));

            // A carrier takes it clamped to its hull (U.SPC-T473); a battleship or command cruiser has no clamps.
            Assert.That(FtlCarriage.Carriable(frigate), Is.True);
            Assert.That(FtlCarriage.RidesClamped(ShipClass.Frigate), Is.True, "a frigate never rides in a hangar");
            Assert.That(FtlCarriage.ClampSizeOf(ShipClass.Frigate), Is.LessThanOrEqualTo(FtlCarriage.ClampsOf(ShipClass.Carrier)));
            Assert.That(FtlCarriage.ClampsOf(ShipClass.Battleship), Is.EqualTo(0));
            Assert.That(FtlCarriage.ClampsOf(ShipClass.CommandCruiser), Is.EqualTo(0));
            var carrier = new Ship { Name = "Deck", Spec = ShipClassSpec.Of(ShipClass.Carrier), Faction = navy };
            var battleship = new Ship { Name = "Wall", Spec = ShipClassSpec.Of(ShipClass.Battleship), Faction = navy };
            Assert.That(FtlCarriage.Stow(new[] { carrier }, new[] { frigate })[frigate], Is.SameAs(carrier));
            Assert.That(FtlCarriage.Stow(new[] { battleship }, new[] { frigate }).ContainsKey(frigate), Is.False);
        }

        // ---------------------------------------------------------------- the alpha strike

        [Test]
        public void ItBurnsInHoldingItsLaunchersLoosesOneVolleyThenFlipsAndBurnsBackOut()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            var frigate = b.AddShip(Hull(ShipClass.Frigate, Blue, 0), 0, new DVec3(-22, 0, 0), DVec3.UnitX);
            var mark = Hulk(b, new DVec3(22, 0, 0));
            Assert.That(AlphaStrike.Flies(frigate), Is.True);
            double volleyKm = AlphaStrike.VolleyRangeKm(frigate);
            Assert.That(volleyKm, Is.EqualTo(WeaponSpec.AntimatterTorpedo.RangeKm * AlphaStrike.VolleyShare.Value).Within(1e-9),
                "it closes until its shortest-ranged launcher, the torpedo, reaches");

            // Burning in: nothing goes until the whole volley can.
            double start = Gap(frigate, mark);
            int launched = 0;
            double releasedAt = -1;
            for (int i = 0; i < 120 && launched == 0; i++)
            {
                double before = Gap(frigate, mark);
                b.Advance(1);
                launched = b.Missiles.Count(m => m.Side == 0);
                if (launched == 0)
                {
                    Assert.That(frigate.Strike, Is.EqualTo(StrikePhase.RunIn));
                    Assert.That(Gap(frigate, mark), Is.LessThan(before), "it burns straight in");
                }
                else releasedAt = before;
            }
            Assert.That(launched, Is.GreaterThan(0), "it never loosed its volley");
            Assert.That(releasedAt, Is.LessThan(start), "it closed first");
            Assert.That(releasedAt, Is.LessThanOrEqualTo(volleyKm + 1), "not before its mark was in reach of every launcher");

            // One volley: every rack's salvo and both torpedoes in the same moment.
            Assert.That(launched, Is.EqualTo(4 * AlphaStrike.AntiShipMissile.Salvo + 2));
            Assert.That(b.Missiles.Count(m => m.Side == 0 && m.Kind == MissileKind.Antimatter), Is.EqualTo(2));
            Assert.That(frigate.Strike, Is.EqualTo(StrikePhase.Egress));

            // Then it flips and burns back out, launchers silent.
            double closest = Gap(frigate, mark);
            int rounds = frigate.Combat.Subsystems.Where(AlphaStrike.IsStrikeMount).Sum(s => s.Ammo);
            double nearest = closest;
            for (int i = 0; i < 15; i++)
            {
                b.Advance(1);
                nearest = Math.Min(nearest, Gap(frigate, mark));
            }
            Assert.That(DVec3.Dot(frigate.Forward, (mark.Pos - frigate.Pos).Normalized), Is.LessThan(0), "flipped: its tail to the mark");
            for (int i = 0; i < 60 && Gap(frigate, mark) <= closest + 3; i++)
            {
                b.Advance(1);
                nearest = Math.Min(nearest, Gap(frigate, mark));
            }
            Assert.That(frigate.Strike, Is.EqualTo(StrikePhase.Egress));
            Assert.That(frigate.Combat.Subsystems.Where(AlphaStrike.IsStrikeMount).Sum(s => s.Ammo), Is.EqualTo(rounds), "nothing more goes on the way out");
            Assert.That(Gap(frigate, mark), Is.GreaterThan(closest + 3), "it burns back out");
            Assert.That(nearest, Is.GreaterThan(volleyKm * 0.5), "its burn sheds the run-in's speed before it overruns its mark");

            // Out of reach and reloaded, it comes round for another run.
            bool again = false;
            for (int i = 0; i < 200 && !again; i++)
            {
                b.Advance(1);
                again = frigate.Strike == StrikePhase.RunIn;
            }
            Assert.That(again, Is.True, "it never came round for a second run");
            Assert.That(AlphaStrike.Loaded(frigate), Is.True);
        }

        [Test]
        public void ItPrefersACapitalShipForItsMark()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var frigate = b.AddShip(Hull(ShipClass.Frigate, Blue, 0), 0, DVec3.Zero, DVec3.UnitX);
                var battleship = b.AddShip(Hull(ShipClass.Battleship, Red, 1), 1, new DVec3(20, 0, 0), -DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Destroyer, Red, 2), 1, new DVec3(0, 12, 0), -DVec3.UnitX);
                Assert.That(b.PickTarget(frigate), Is.SameAs(battleship), $"seed {seed}");
            }
        }

        [Test]
        public void WithItsMagazinesSpentItHasNothingToFightWithAndWithdraws()
        {
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 1);
            var frigate = b.AddShip(Hull(ShipClass.Frigate, Blue, 0), 0, new DVec3(-10, 0, 0), DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Destroyer, Red, 1), 1, new DVec3(30, 0, 0), -DVec3.UnitX);
            foreach (var s in frigate.Combat.Subsystems)
                if (AlphaStrike.IsStrikeMount(s)) s.Ammo = 0;
            Assert.That(AlphaStrike.Flies(frigate), Is.False);
            Assert.That(frigate.Combat.Armed, Is.False, "point defence alone is no battery");
            b.Advance(1);
            Assert.That(frigate.Fleeing, Is.True);
        }

        // ---------------------------------------------------------------- navies build it

        [Test]
        public void NaviesBuildFrigatesForCarrierGroupsAndStrikeFlotillas()
        {
            Assert.That(BuildPlan.Carriers.Cycle, Does.Contain(ShipClass.Frigate));
            Assert.That(BuildPlan.Strike.Cycle, Does.Contain(ShipClass.Frigate));
            Assert.That(BuildPlan.All, Does.Contain(BuildPlan.Strike));
            Assert.That(Economy.HullCost(ShipClass.Frigate), Is.GreaterThan(0));
        }

        [Test]
        public void ItsTunablesAreInTheSettingsMenu()
        {
            SettingsRegistry.DiscoverLoadedDeclarations();
            var keys = SettingsRegistry.Default.All.Where(s => s.Group == SettingGroups.Broadsides).Select(s => s.Key).ToList();
            foreach (var key in new[] { "frigate.volleyRange", "frigate.egressKm", "frigate.burn", "frigate.capitalBias" })
                Assert.That(keys, Does.Contain(key));
        }
    }
}
