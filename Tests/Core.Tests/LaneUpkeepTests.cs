using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Lane tenders and lane upkeep (U.SPC-T481): the unarmed tender hull, lanes wearing without tending and slowing, stalling
    /// and failing with it, traffic routing round a failed lane, polities raising and replacing tenders, and raiders and navies
    /// prizing and hunting them. Settings are shared by every fixture, so these read them and never change them.
    /// </summary>
    [TestFixture]
    public class LaneUpkeepTests
    {
        const double StartJd = 2461324.0;

        static (World w, Ephemeris eph) Sol()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return (World.CreateDefault(eph, StartJd), eph);
        }

        static Lane LaneBetween(Hyperlanes lanes, BodyCatalog catalog, string a, string b)
        {
            int x = catalog[a].Index, y = catalog[b].Index;
            return lanes.Lanes.First(l => (l.A == x && l.B == y) || (l.A == y && l.B == x));
        }

        static Ship Hull(World w, string name, ShipClass c, Faction f, ShipRole role = ShipRole.Patrol)
        {
            var s = w.Fleet.AddShip(name, c, f, role);
            s.Combat = new ShipCombat(CombatSpec.Of(c), c);
            return s;
        }

        // ------------------------------------------------------------ the hull

        [Test]
        public void TheLaneTenderIsAnUnarmedSupportHullWithAFrameACrewAndAPrice()
        {
            var spec = ShipClassSpec.Of(ShipClass.LaneTender);
            Assert.That(spec.DisplayName, Is.EqualTo("Lane tender"));
            Assert.That(spec.IsMilitary, Is.False);
            var design = CombatSpec.Of(ShipClass.LaneTender);
            Assert.That(design.DesignName, Is.EqualTo("Lane tender"), "its own design, not the destroyer fallback");
            Assert.That(design.Crew, Is.GreaterThan(0));
            Assert.That(new ShipCombat(design, ShipClass.LaneTender).Armed, Is.False, "a tender carries no guns");
            var frame = HullFrames.ForClass(ShipClass.LaneTender);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Category, Is.EqualTo(HullCategory.Support));
            Assert.That(Economy.HullCost(ShipClass.LaneTender), Is.GreaterThan(Economy.HullCost(ShipClass.Courier)));
            Assert.That(FlightModelRegistry.CreateDefault().FamilyOf(ShipClass.LaneTender), Is.EqualTo(FlightFamily.Hauler));
        }

        [Test]
        public void EveryUpkeepRateIsASetting()
        {
            foreach (var key in new[]
                     {
                         "lanes.decayPerDay", "lanes.tendPerDay", "lanes.failAt", "lanes.slowestSpeed", "lanes.worstReliability",
                         "lanes.stallDays", "lanes.tendersPerLane", "lanes.tenderValue", "lanes.tenderFocus", "lanes.huntDays",
                         "lanes.raiderHuntChance", "lanes.tenderDwellDays",
                     })
                Assert.That(SettingsRegistry.Default.Find(key), Is.Not.Null, key);
        }

        // ------------------------------------------------------------ condition

        [Test]
        public void AWornLaneRidesSlowerAndLessReliablyUntilItFails()
        {
            double fail = LaneCondition.FailAt.Value;
            Assert.That(LaneCondition.SpeedFactor(1), Is.EqualTo(1));
            Assert.That(LaneCondition.Reliability(1), Is.EqualTo(1));
            Assert.That(LaneCondition.SpeedFactor(fail), Is.EqualTo(LaneCondition.SlowestSpeed.Value).Within(1e-9));
            Assert.That(LaneCondition.Reliability(fail), Is.EqualTo(LaneCondition.WorstReliability.Value).Within(1e-9));
            double mid = (1 + fail) / 2;
            Assert.That(LaneCondition.SpeedFactor(mid), Is.LessThan(1).And.GreaterThan(LaneCondition.SlowestSpeed.Value));
            Assert.That(LaneCondition.Reliability(mid), Is.LessThan(1).And.GreaterThan(LaneCondition.WorstReliability.Value));
            Assert.That(LaneCondition.Carries(fail + 0.01), Is.True);
            Assert.That(LaneCondition.Carries(fail), Is.False);

            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateDefault(eph);
            var lane = LaneBetween(lanes, eph.Catalog, "Earth", "Mars");
            double km = lanes.LaneLengthKm(lane, StartJd);
            double fresh = lanes.RideDays(lane, km);
            Assert.That(fresh, Is.EqualTo(Hyperlanes.RideDays(km)), "a lane in full repair rides at its full pace");
            lanes.SetCondition(lane, mid);
            Assert.That(lanes.RideDays(lane, km), Is.GreaterThan(fresh * 1.05));
            Assert.That(lanes.Reliability(lane), Is.LessThan(1));
        }

        [Test]
        public void AFailedLaneCarriesNothingAndRoutesGoRoundIt()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var lanes = Hyperlanes.CreateDefault(eph);
            var c = eph.Catalog;
            int earth = c["Earth"].Index, mars = c["Mars"].Index;
            var lane = LaneBetween(lanes, c, "Earth", "Mars");
            var direct = lanes.Route(earth, mars, StartJd);
            Assert.That(direct.Select(h => h.Lane), Does.Contain(lane));
            var ramp = lanes.RampHelio(lane, earth, StartJd);
            Assert.That(lanes.OnRampAt(ramp, StartJd)?.Lane, Is.EqualTo(lane));

            lanes.SetCondition(lane, 0);
            Assert.That(lanes.Carries(lane), Is.False);
            var round = lanes.Route(earth, mars, StartJd);
            Assert.That(round, Is.Not.Null, "Earth reaches Mars by way of Ceres");
            Assert.That(round.Select(h => h.Lane), Does.Not.Contain(lane));
            Assert.That(round.Count, Is.GreaterThan(1));
            Assert.That(lanes.OnRampAt(ramp, StartJd)?.Lane, Is.Not.EqualTo(lane), "its ring gate is dark");
            Assert.That(lanes.OnwardHops(mars, null).Select(h => h.Lane), Does.Not.Contain(lane));

            lanes.SetCondition(lane, 1);
            Assert.That(lanes.Route(earth, mars, StartJd).Select(h => h.Lane), Does.Contain(lane), "back in service, back on the route");
        }

        [Test]
        public void ARideOfAWornLaneCanStallPartWayAndPickUpAgain()
        {
            var (w, eph) = Sol();
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            var lanes = w.Fleet.Lanes;
            int earth = eph.Catalog["Earth"].Index;
            var lane = LaneBetween(lanes, eph.Catalog, "Earth", "Mars");
            var ramp = lanes.RampHelio(lane, earth, StartJd);
            var hop = lanes.OnRampAt(ramp, StartJd).Value;
            var ship = Hull(w, "Plodder", ShipClass.Freighter, w.Independents, ShipRole.Trader);
            Fleet.PinAt(ship, earth, ramp - eph.HelioAt(earth, StartJd), DVec3.UnitX, StartJd);
            w.Fleet.JoinLane(ship, hop, ramp, StartJd);
            double fullEnd = ship.PhaseEndJd;
            Assert.That(w.Fleet.StallRide(ship, 0.5, 0.3), Is.True);
            Assert.That(ship.LaneEndProgress, Is.EqualTo(0.5));
            Assert.That(ship.PhaseEndJd, Is.LessThan(fullEnd));
            Assert.That(w.Fleet.StallRide(ship, 0.5, 0.3), Is.False, "one stall a ride");
            w.Fleet.Update(ship.PhaseEndJd + 1e-3);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Lurk), "it waits off the lane");
            Assert.That(ship.Stalled, Is.True);
            Assert.That(ship.Ambushing, Is.False, "a stall is no ambush (U.SPC-T572)");
        }

        // ------------------------------------------------------------ tending

        [Test]
        public void EveryKeptLaneStartsWithItsTendersAndTheyAreTheKeepersOwn()
        {
            var (w, _) = Sol();
            var up = w.LaneUpkeep;
            var lanes = w.Fleet.Lanes;
            int kept = 0;
            foreach (var lane in lanes.Lanes)
            {
                var owner = up.OwnerOf(lane);
                if (owner == null) continue;
                kept++;
                Assert.That(up.TendersOn(lane), Is.EqualTo(LaneUpkeep.TendersPerLane.IntValue), lane.Name);
                foreach (var t in up.Tending.Where(kv => kv.Value == lane).Select(kv => kv.Key))
                {
                    Assert.That(t.Faction, Is.EqualTo(owner));
                    Assert.That(t.Spec.Class, Is.EqualTo(ShipClass.LaneTender));
                    Assert.That(t.Role, Is.EqualTo(ShipRole.Tender));
                    Assert.That(w.Fleet.Ships, Does.Contain(t));
                }
            }
            Assert.That(kept, Is.GreaterThan(lanes.Lanes.Count / 2), "most of Sol's lanes have a keeper");
        }

        [Test]
        public void AnUntendedLaneWearsOutAndFailsWhileATendedOneHoldsAndTendersBringItBack()
        {
            var (w, eph) = Sol();
            var up = w.LaneUpkeep;
            var lanes = w.Fleet.Lanes;
            var untended = LaneBetween(lanes, eph.Catalog, "Earth", "Mars");
            var tended = LaneBetween(lanes, eph.Catalog, "Jupiter", "Saturn");
            // Everything held still: only the upkeep runs. The tended lane's tender lies at one of its ends, working it.
            var keeper = up.Tending.First(kv => kv.Value == tended).Key;
            foreach (var s in w.Fleet.Ships)
                if (s != keeper) s.Destroyed = true;
            w.Fleet.Station(keeper, tended.A, w.Jd);
            Assert.That(up.Working(keeper, tended), Is.True);

            var changes = new List<(Lane lane, bool failed)>();
            up.LaneChanged += (lane, failed, _) => changes.Add((lane, failed));
            double jd = w.Jd;
            double days = (1 - LaneCondition.FailAt.Value) / LaneCondition.DecayPerDay.Value;
            for (int d = 1; d <= (int)days + 2; d++) up.Update(jd + d);
            jd += (int)days + 2;
            Assert.That(lanes.Carries(untended), Is.False, "nobody tends it: it fails");
            Assert.That(changes, Does.Contain((untended, true)));
            Assert.That(lanes.ConditionOf(tended), Is.EqualTo(1), "its tender keeps it in full repair");
            Assert.That(w.Events.Any(e => e.Text.Contains(untended.Name) && e.Text.Contains("failed")), Is.True);

            // A new tender of its keeper's sent to it brings it back into service.
            var fresh = Hull(w, "Fresh Start", ShipClass.LaneTender, up.OwnerOf(untended), ShipRole.Tender);
            up.Tending[fresh] = untended;
            w.Fleet.Station(fresh, untended.A, jd);
            Assert.That(up.Working(fresh, untended), Is.True);
            for (int d = 1; d <= 10 && !lanes.Carries(untended); d++) up.Update(jd + d);
            Assert.That(lanes.Carries(untended), Is.True);
            Assert.That(changes, Does.Contain((untended, false)));
        }

        [Test]
        public void ATenderShuttlesBetweenItsLanesEndsAndHoldsAtOneWhileItsLaneIsDown()
        {
            var (w, _) = Sol();
            var up = w.LaneUpkeep;
            var kv = up.Tending.First();
            var (tender, lane) = (kv.Key, kv.Value);
            Assert.That(up.ChooseNext(tender, lane.A, w.Jd), Is.EqualTo(lane.B));
            Assert.That(up.ChooseNext(tender, lane.B, w.Jd), Is.EqualTo(lane.A));
            w.Fleet.Lanes.SetCondition(lane, 0);
            Assert.That(up.ChooseNext(tender, lane.A, w.Jd), Is.EqualTo(lane.A), "it stays to work the rings at the end it is at");
        }

        [Test]
        public void AKeeperOrdersAReplacementForALostTenderAndItGoesToThatLane()
        {
            var (w, _) = Sol();
            var up = w.LaneUpkeep;
            var yard = w.Economy.Settlements.First(s => s.HasShipyard && s.Owner != null && !s.Owner.IsPirate &&
                                                       w.Fleet.Lanes.Lanes.Any(l => up.OwnerOf(l) == s.Owner));
            Assert.That(up.OrderAt(yard), Is.Null, "every lane has its tenders");
            var lane = w.Fleet.Lanes.Lanes.First(l => up.OwnerOf(l) == yard.Owner);
            foreach (var t in up.Tending.Where(p => p.Value == lane).Select(p => p.Key).ToList()) t.Destroyed = true;
            Assert.That(up.OrderAt(yard), Is.EqualTo(ShipClass.LaneTender));
            var built = up.Launch(yard, w.Jd);
            Assert.That(built, Is.Not.Null);
            Assert.That(up.LaneOf(built), Is.EqualTo(lane));
            Assert.That(built.Faction, Is.EqualTo(yard.Owner));
            Assert.That(up.OrderAt(yard), Is.Null);
        }

        // ------------------------------------------------------------ routing round a failed lane

        [Test]
        public void TrafficAndTradeGoRoundAFailedLaneAndARideAlreadyBoundForItTakesAnotherWay()
        {
            var (w, eph) = Sol();
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            w.War.Battles.Clear();
            var lanes = w.Fleet.Lanes;
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var lane = LaneBetween(lanes, eph.Catalog, "Earth", "Mars");
            var ship = Hull(w, "Round The Long Way", ShipClass.Courier, w.Sol);
            ship.Ports = new[] { earth, mars };
            ship.Route = new[] { mars };
            w.Fleet.Station(ship, earth, w.Jd);

            double before = w.Fleet.EstimateTravelDays(ship, earth, mars, w.Jd);
            var ridden = new List<Lane>();
            var toll = w.Fleet.LaneJoined;
            w.Fleet.LaneJoined = (s, hop, when) =>
            {
                toll(s, hop, when);
                if (s == ship) ridden.Add(hop.Lane);
            };

            // Off it goes, bound for the Earth-Mars lane's ramp; the lane fails before it gets there.
            double jd = w.Jd;
            for (int k = 0; k < 2000 && ship.Journey == null; k++) w.Update(jd += 0.01);
            Assert.That(ship.Journey, Is.Not.Null, "it set out by the lanes");
            Assert.That(ship.Journey.Select(h => h.Lane), Does.Contain(lane));
            lanes.SetCondition(lane, 0);
            Assert.That(w.Fleet.EstimateTravelDays(ship, earth, mars, jd), Is.GreaterThan(before), "the planners price the long way round");
            for (int k = 0; k < 3000 && !(ship.Phase == ShipPhase.Parked && ship.Orbit.Body == mars); k++)
            {
                lanes.SetCondition(lane, 0);
                w.Update(jd += 0.01);
            }
            Assert.That(ridden, Does.Not.Contain(lane), "nobody rides a failed lane");
            Assert.That(ship.Orbit.Body, Is.EqualTo(mars), "it got there anyway");
        }

        // ------------------------------------------------------------ in war

        [Test]
        public void RaidersPrizeTendersAndMeanToDestroyThemAndGunnersFavourThem()
        {
            var (w, _) = Sol();
            var tender = w.LaneUpkeep.Tending.Keys.First();
            var freighter = Hull(w, "Fat Purse", ShipClass.Freighter, tender.Faction, ShipRole.Trader);
            Assert.That(RaidDoctrine.PreyValue(tender), Is.GreaterThanOrEqualTo(LaneUpkeep.TenderValue.Value));
            Assert.That(RaidDoctrine.PreyValue(tender), Is.GreaterThan(RaidDoctrine.PreyValue(freighter)));
            Assert.That(RaidDoctrine.Robs(w.Pirates, tender), Is.False, "a tender is sunk, not robbed");
            Assert.That(RaidDoctrine.Robs(w.Pirates, freighter), Is.True);

            for (int seed = 0; seed < 8; seed++)
            {
                var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
                var raider = b.AddShip(Hull(w, "Shade", ShipClass.Destroyer, w.Pirates), 0, new DVec3(0, 0, 0), DVec3.UnitX);
                b.AddShip(freighter, 1, new DVec3(10, 0, 0), -DVec3.UnitX);
                var t = b.AddShip(tender, 1, new DVec3(0, 10, 0), -DVec3.UnitX);
                Assert.That(b.PickTarget(raider), Is.SameAs(t), "seed " + seed);
            }
        }

        [Test]
        public void NaviesAtWarAndRaidersHuntTendersInReachButNotTheirOwnOrInPeace()
        {
            var (w, eph) = Sol();
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            var up = w.LaneUpkeep;
            var lanes = w.Fleet.Lanes;
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            var lane = LaneBetween(lanes, eph.Catalog, "Earth", "Mars");
            var tender = Hull(w, "Lamp", ShipClass.LaneTender, w.Sol, ShipRole.Tender);
            up.Tending[tender] = lane;
            w.Fleet.Station(tender, mars, w.Jd);

            var enemy = Hull(w, "Hunter", ShipClass.Destroyer, w.Reach);
            w.Fleet.Station(enemy, earth, w.Jd);
            var friend = Hull(w, "Friend", ShipClass.Destroyer, w.Sol);
            w.Fleet.Station(friend, earth, w.Jd);
            var raider = Hull(w, "Shade", ShipClass.Destroyer, w.Pirates);
            w.Fleet.Station(raider, earth, w.Jd);

            Assert.That(w.War.AtWar(w.Reach), Is.True);
            Assert.That(up.HuntTarget(enemy, earth, w.Jd), Is.EqualTo(mars));
            Assert.That(up.HuntTarget(raider, earth, w.Jd), Is.EqualTo(mars));
            Assert.That(up.HuntTarget(friend, earth, w.Jd), Is.EqualTo(-1), "nobody hunts their own");
            tender.Destroyed = true;
            Assert.That(up.HuntTarget(enemy, earth, w.Jd), Is.EqualTo(-1));
            tender.Destroyed = false;

            w.Diplomacy.Set(w.Sol, w.Reach, Relation.Peace);
            Assert.That(up.HuntTarget(enemy, earth, w.Jd), Is.EqualTo(-1), "in peace a navy leaves them be");
            Assert.That(up.HuntTarget(raider, earth, w.Jd), Is.EqualTo(mars), "raiders never do");
        }
    }
}
