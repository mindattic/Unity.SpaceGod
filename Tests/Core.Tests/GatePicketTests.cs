using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Gate pickets (U.SPC-T480): small, sensor-heavy hulls a navy stations at the exit of every gate it holds, which warn it
    /// of hostiles coming through the gate or dropping out of FTL nearby, so its command sends a group to meet them at the
    /// gate; raiders go for the pickets first.
    /// </summary>
    [TestFixture]
    public class GatePicketTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true };
        static readonly Faction Pirates = new Faction("Pirates", "PIR", new Rgb(1, 0, 0)) { IsPirate = true };
        static readonly Faction Traders = new Faction("Traders", "TRD", new Rgb(0, 1, 0));

        static Ship Hull(ShipClass c, Faction f, int id, ShipRole role = ShipRole.Patrol) =>
            new Ship { Id = id, Name = c + "" + id, Spec = ShipClassSpec.Of(c), Faction = f, Role = role, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        /// <summary>Sol, a gate in it a navy holds, and that gate's picket.</summary>
        static (StarSystem Sol, World W, JumpGate Gate, Ship Picket) HeldGate()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var gate = sol.Gates.FirstOrDefault(GatePickets.Keeps);
            Assert.That(gate, Is.Not.Null, "no navy holds a gate in Sol");
            var picket = GatePickets.Of(gate).FirstOrDefault();
            Assert.That(picket, Is.Not.Null, "the gate has no picket");
            return (sol, sol.World, gate, picket);
        }

        /// <summary>A raider band of <paramref name="w"/>'s pirates come out at <paramref name="gate"/>: a destroyer and its wingman.</summary>
        static List<Ship> RaidersAt(World w, JumpGate gate, double jd)
        {
            int root = w.Catalog.Root.Index;
            var lead = w.Fleet.AddShip("Black Gull", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            var wing = w.Fleet.AddShip("Black Gull Two", ShipClass.Fighter, w.Pirates, ShipRole.Escort);
            w.Fleet.AddEscort(lead, wing, Fleet.SlotFor(0));
            var at = gate.PositionKm - gate.PositionKm.Normalized * JumpGate.ArrivalInsetKm;
            Fleet.PinAt(lead, root, at, -gate.PositionKm.Normalized, jd);
            Fleet.PinAt(wing, root, at + new DVec3(2, 0, 0), -gate.PositionKm.Normalized, jd);
            return new List<Ship> { lead, wing };
        }

        // ---------------------------------------------------------------- the class

        [Test]
        public void ThePicketIsASmallSensorHeavyHullWithItsOwnFrameAndPrice()
        {
            var spec = ShipClassSpec.Of(ShipClass.GatePicket);
            Assert.That(spec.IsMilitary, Is.True);
            Assert.That(spec.DisplayName, Is.EqualTo("Gate picket"));
            var design = CombatSpec.Of(ShipClass.GatePicket);
            Assert.That(design.DesignName, Is.EqualTo("Gate picket"));
            Assert.That(design.LengthKm, Is.LessThan(CombatSpec.Of(ShipClass.Destroyer).LengthKm), "a picket is a small hull");
            Assert.That(design.Hull, Is.LessThan(CombatSpec.Of(ShipClass.Destroyer).Hull));
            Assert.That(Crews.Complement(ShipClass.GatePicket), Is.LessThan(Crews.Complement(ShipClass.Destroyer)));
            Assert.That(Economy.HullPrice(ShipClass.GatePicket), Is.GreaterThan(0).And.LessThan(Economy.HullPrice(ShipClass.Destroyer)));
            Assert.That(HullFrames.ForClass(ShipClass.GatePicket), Is.Not.Null, "no hull frame to build it from");
            Assert.That(CruiseDrives.Of(ShipClass.GatePicket), Is.Null, "a picket keeps to its own system");

            // Sensor-heavy: it sees a coasting freighter's heat six times as far as a warship does, and resolves it finer.
            var picket = Hull(ShipClass.GatePicket, Navy, 1);
            var destroyer = Hull(ShipClass.Destroyer, Navy, 2);
            Assert.That(SensorSuite.Of(picket).RangeKm, Is.EqualTo(SensorSuite.Of(destroyer).RangeKm * 6).Within(1));
            Assert.That(SensorSuite.Of(picket).Resolution, Is.GreaterThan(SensorSuite.Of(destroyer).Resolution));
        }

        // ---------------------------------------------------------------- stationed at the gates

        [Test]
        public void EveryGateANavyHoldsHasItsPicketOnPostAtTheExit()
        {
            var g = Galaxy.CreateDefault(StartJd);
            int held = 0;
            foreach (var link in g.Network.Links)
                foreach (var gate in new[] { link.AtA, link.AtB })
                {
                    var pickets = GatePickets.Of(gate);
                    Assert.That(pickets.Count, Is.EqualTo(GatePickets.Wanted(gate)), gate.Name + " at " + gate.Home.Name);
                    if (!GatePickets.Keeps(gate)) continue;
                    held++;
                    var p = pickets[0];
                    var w = gate.Home.World;
                    Assert.That(p.Faction, Is.SameAs(gate.Owner));
                    Assert.That(p.Spec.Class, Is.EqualTo(ShipClass.GatePicket));
                    Assert.That(p.PatrolWing, Is.True, "a picket is counted as a warship");
                    Assert.That(GatePickets.OnPost(w, p), Is.True, p.Name + " is not on its post");
                    // Just inside the gate, nearer it than raiders lying off its approach.
                    double fromGate = (p.Orbit.OffsetAt(w.Jd) - gate.PositionKm).Length;
                    Assert.That(fromGate, Is.LessThan(JumpGate.ApproachInsetKm));
                    Assert.That(p.Orbit.OffsetAt(w.Jd).Length, Is.LessThan(gate.PositionKm.Length), "the picket is outside its gate");
                }
            Assert.That(held, Is.GreaterThan(0), "no navy holds a gate anywhere");
        }

        [Test]
        public void APicketHoldsItsPostAndNeverOpensAFight()
        {
            var (sol, w, gate, picket) = HeldGate();
            int root = sol.Catalog.Root.Index;
            Assert.That(w.War.ChooseNext(picket, root, StartJd + 0.1), Is.EqualTo(root));
            Assert.That(w.War.OrderOf(picket).Kind, Is.EqualTo(OrderKind.Patrol));
            Assert.That(w.War.Commanded(picket, root), Is.False, "a lookout was ordered to fight at its gate");
            Assert.That(picket.StakeoutKm, Is.Null);

            // Badly hurt, it puts in at a yard and comes back.
            foreach (var s in picket.Combat.Subsystems) s.Hp *= 0.2;
            picket.Combat.Hull *= 0.2;
            int yard = w.War.ChooseNext(picket, root, StartJd + 0.2);
            Assert.That(yard, Is.Not.EqualTo(root));
            Assert.That(w.War.OrderOf(picket).Kind, Is.EqualTo(OrderKind.Repair));
        }

        [Test]
        public void ALostPicketIsReplacedAtItsNavysExpenseAndAGateThatChangesHandsStandsTheOldOneDown()
        {
            var (sol, w, gate, picket) = HeldGate();
            var owner = gate.Owner;
            owner.Treasury = 1e12;
            picket.Destroyed = true;
            GatePickets.Upkeep(gate, StartJd);
            Assert.That(GatePickets.Of(gate), Is.Empty, "replaced the same day it was lost");
            GatePickets.Upkeep(gate, StartJd + GatePickets.RebuildDays - 1);
            Assert.That(GatePickets.Of(gate), Is.Empty);
            double before = owner.Treasury;
            GatePickets.Upkeep(gate, StartJd + GatePickets.RebuildDays);
            var fresh = GatePickets.Of(gate);
            Assert.That(fresh.Count, Is.EqualTo(GatePickets.Wanted(gate)));
            Assert.That(fresh[0].Faction, Is.SameAs(owner));
            Assert.That(owner.Treasury, Is.EqualTo(before - Economy.HullPrice(ShipClass.GatePicket)).Within(1));

            // A picket of a navy that no longer holds the gate (as after the gate is taken in a war) goes off to patrol its own
            // ports here, and the holder posts its own in its place.
            var old = w.Fleet.Factions.First(f => f.IsMilitary && !f.IsPirate && f != owner && f.Kind == PolityKind.State &&
                                                  w.Economy.Settlements.Any(s => s.Owner == f && s.Kind != SettlementKind.Haven));
            fresh[0].Faction = old;
            GatePickets.Upkeep(gate, StartJd + GatePickets.RebuildDays + 1);
            Assert.That(fresh[0].PicketGate, Is.Null, "a picket stayed on a gate no longer its navy's");
            Assert.That(fresh[0].Route, Is.Not.Null.And.Not.Empty, "the stood-down picket was given no ports to patrol");
            Assert.That(GatePickets.Of(gate), Is.Empty);
            GatePickets.PostFull(gate, StartJd + GatePickets.RebuildDays + 1);
            Assert.That(GatePickets.Of(gate).All(p => p.Faction == owner), Is.True);
            Assert.That(GatePickets.Of(gate).Count, Is.EqualTo(GatePickets.Wanted(gate)));
        }

        // ---------------------------------------------------------------- warning

        [Test]
        public void APicketWarnsItsNavyOfRaidersComingThroughAndCommandSendsAGroupToMeetThemAtTheGate()
        {
            var (sol, w, gate, picket) = HeldGate();
            int root = sol.Catalog.Root.Index;
            var navy = gate.Owner;

            // A strong group of the gate's navy, idle at one of its ports, with nothing more pressing to do: at peace with
            // the other states, and no raiders or enemy warships at its ports.
            int port = w.Economy.Settlements.First(s => s.Owner == navy && s.Kind != SettlementKind.Haven).Body;
            var leader = w.Fleet.AddShip("Gatekeeper", ShipClass.Battleship, navy, ShipRole.Patrol);
            w.Fleet.Station(leader, port, StartJd);
            Assert.That(w.War.IsHomeGuard(leader), Is.False);
            w.War.Battles.Clear();
            foreach (var f in w.Fleet.Factions.Where(f => f != navy && !f.IsPirate)) w.Diplomacy.Set(navy, f, Relation.Peace);
            var ports = new HashSet<int>(w.Economy.Settlements.Where(s => s.Owner == navy).Select(s => s.Body));
            foreach (var s in w.Fleet.Ships.Where(s => !s.Destroyed && s.Faction != navy && (s.Spec.IsMilitary || s.Faction.IsPirate)).ToList())
                if (s.Controller.Orbit != null && ports.Contains(s.Controller.Orbit.Body)) s.Destroyed = true;

            double jd = StartJd + 0.05;
            var band = RaidersAt(w, gate, jd);
            // Unwarned, command does not know to go: they are no band staked out off the gate, just hulls come through it.
            w.War.ChooseNext(leader, port, jd);
            Assert.That(w.War.OrderOf(leader).Kind, Is.Not.EqualTo(OrderKind.HoldGate));

            // A merchant coming through is no threat: no warning.
            var trader = w.Fleet.AddShip("Plain Dealer", ShipClass.Freighter, w.Independents, ShipRole.Trader);
            Fleet.PinAt(trader, root, gate.PositionKm * 0.97, DVec3.UnitX, jd);
            Assert.That(GatePickets.OnArrival(gate, new[] { trader }, jd), Is.Null);
            Assert.That(w.War.PicketWarnings, Is.Empty);

            // The raiders: the picket warns its navy at once, in its picture and in the news.
            var warning = GatePickets.OnArrival(gate, band, jd);
            Assert.That(warning, Is.Not.Null, "the picket said nothing of raiders coming through its gate");
            Assert.That(warning.Picket, Is.SameAs(picket));
            Assert.That(warning.Owner, Is.SameAs(navy));
            Assert.That(warning.By, Is.SameAs(w.Pirates));
            Assert.That(warning.Hulls, Is.EqualTo(2));
            Assert.That(warning.SelfJump, Is.False);
            Assert.That(w.War.PicketWarnings, Does.Contain(warning));
            Assert.That(w.Sensors.PictureOf(navy).LevelOf(band[0]), Is.GreaterThanOrEqualTo(ContactLevel.Class), "the navy's picture has no track of them");
            Assert.That(w.Events.Any(e => e.Text.Contains(picket.Name) && e.Text.Contains("warns")), Is.True, "no news of the warning");
            Assert.That(w.War.WarnedOffGate(gate, navy, jd), Is.GreaterThan(0));

            // And command sends the group to meet them at the gate, on its approach.
            Assert.That(w.War.ChooseNext(leader, port, jd + 0.01), Is.EqualTo(root), "command did not send the group to the gate");
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.HoldGate));
            Assert.That(leader.StakeoutKm, Is.Not.Null);
            Assert.That((leader.StakeoutKm.Value - gate.ApproachKm).Length, Is.LessThan(500), "the group took station off some other gate");

            // The warning goes stale once they are gone.
            foreach (var s in band) s.Destroyed = true;
            Assert.That(w.War.WarnedOffGate(gate, navy, jd + 0.02), Is.EqualTo(0));
            Assert.That(w.War.WarnedOffGate(gate, navy, jd + GatePickets.WarningDays + 1), Is.EqualTo(0));
        }

        [Test]
        public void APicketMarksAHostileGroupDroppingOutOfFtlNearbyButNotFarOff()
        {
            var (sol, w, gate, picket) = HeldGate();
            double jd = StartJd + 0.05;
            var band = RaidersAt(w, gate, jd);
            var dir = gate.PositionKm.Normalized;
            var far = GatePickets.OnDropOut(sol, band, picket.PicketPostKm + dir * (GatePickets.JumpWatchAu.Value + 2) * Astro.AuKm, jd);
            Assert.That(far, Is.Null, "a picket saw a drop-out well past its watch");
            var near = GatePickets.OnDropOut(sol, band, picket.PicketPostKm + dir * 1.0 * Astro.AuKm, jd);
            Assert.That(near, Is.Not.Null, "the picket missed raiders dropping out of FTL an AU off");
            Assert.That(near.SelfJump, Is.True);
            Assert.That(near.Owner, Is.SameAs(gate.Owner));
        }

        [Test]
        public void ADeadPicketWarnsNobody()
        {
            var (sol, w, gate, picket) = HeldGate();
            double jd = StartJd + 0.05;
            var band = RaidersAt(w, gate, jd);
            foreach (var p in GatePickets.Of(gate)) p.Destroyed = true;
            Assert.That(GatePickets.OnArrival(gate, band, jd), Is.Null, "a blind gate warned of raiders");
        }

        // ---------------------------------------------------------------- raiders go for the pickets first

        [Test]
        public void RaidersShootAPicketBeforeANearerMerchantOrWarship()
        {
            Assert.That(RaidDoctrine.TargetBias(Pirates, Hull(ShipClass.GatePicket, Navy, 9)), Is.GreaterThan(0));
            Assert.That(RaidDoctrine.TargetBias(Navy, Hull(ShipClass.GatePicket, Pirates, 9)), Is.EqualTo(0), "only raiders hunt pickets first");
            Assert.That(RaidDoctrine.TargetBias(Pirates, Hull(ShipClass.Destroyer, Navy, 9)), Is.EqualTo(0));

            for (int seed = 0; seed < 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var raider = b.AddShip(Hull(ShipClass.Destroyer, Pirates, 1), 1, new DVec3(-4, 0, 0), DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Freighter, Traders, 2, ShipRole.Trader), 0, new DVec3(2, 0, 0), -DVec3.UnitX);
                b.AddShip(Hull(ShipClass.Destroyer, Navy, 3), 0, new DVec3(4, 0, 0), -DVec3.UnitX);
                var picket = b.AddShip(Hull(ShipClass.GatePicket, Navy, 4), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
                Assert.That(b.PickTarget(raider), Is.SameAs(picket), "seed " + seed + ": raiders left the picket for later");
            }
        }

        [Test]
        public void ABandLyingOffAGateFallsOnItsPicket()
        {
            var (sol, w, gate, picket) = HeldGate();
            int root = sol.Catalog.Root.Index;
            w.War.Battles.Clear();
            // A band free to go (not caught up in a fight somewhere else).
            var band = w.Fleet.Ships.First(s => s.Faction == w.Pirates && !s.IsEscort && !s.Destroyed && !s.Runner && s.Spec.IsMilitary && s.Battle == null &&
                                                !w.Fleet.Ships.Any(e => e.Leader == s && e.Battle != null));
            w.War.StakeOut(band, gate, StartJd + 30);
            Fleet.PinAt(band, root, band.StakeoutKm.Value, -gate.PositionKm.Normalized, StartJd);
            foreach (var e in w.Fleet.Ships.Where(s => s.Leader == band)) Fleet.PinAt(e, root, band.StakeoutKm.Value + new DVec3(1, 1, 0), -gate.PositionKm.Normalized, StartJd);

            w.War.GateAmbushes(StartJd + 0.01);
            var fight = w.War.BattleAt(root);
            Assert.That(fight, Is.Not.Null, "the band let the picket be");
            Assert.That(fight.UnitOf(picket), Is.Not.Null, "the picket was not in the fight");
            Assert.That(w.Events.Any(e => e.Text.Contains("fall on the picket")), Is.True, "the band went for something else first");
        }
    }
}
