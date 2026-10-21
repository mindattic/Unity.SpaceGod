using System;
using System.Linq;
using UnitySpace.Core;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// The chase (U.SPC-T273): hostiles that make the piloted ship out on their scanners turn to run it down, show up a
    /// few hundred km off, close faster than it can cruise, charge an EMP that knocks its drives out if it is still in
    /// reach when it goes off, and start the fight where it is, in open space as readily as near a world; a chase that
    /// runs too long is given up and the hunters go back to their business.
    /// </summary>
    [TestFixture]
    public class PursuitTests
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

        static int Earth(World w) => w.Catalog["Earth"].Index;

        static Ship Pilot(World w, DVec3 offset)
        {
            var ship = w.Fleet.AddShip("Runner", ShipClass.Destroyer, w.Independents, ShipRole.Free);
            Fleet.PinAt(ship, Earth(w), offset, DVec3.UnitX, StartJd);
            w.PilotedShip = ship;
            return ship;
        }

        static Ship Raider(World w)
        {
            var ship = w.Fleet.AddShip("Gutter Knife", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            ship.Route = new[] { Earth(w) };
            w.Fleet.Station(ship, Earth(w), StartJd);
            return ship;
        }

        [Test]
        public void ARaiderThatSeesThePlayerTurnsToRunItDownAndShowsUpAFewHundredKmOff()
        {
            var w = CreateWorld();
            var pilot = Pilot(w, new DVec3(60000, 0, 0));
            var raider = Raider(w);
            Assert.That(w.Diplomacy.Hostile(w.Pirates, pilot.Faction), Is.True);
            Pursuer g = null;
            for (int i = 0; i < 100 && g == null; i++)
                g = w.Pursuit.Consider(pilot, Earth(w), new DVec3(60000, 0, 0), DVec3.Zero, StartJd).FirstOrDefault(x => x.Leader == raider);
            Assert.That(g, Is.Not.Null, "a raider beside the player never turned to chase it");
            Assert.That((g.Pos - new DVec3(60000, 0, 0)).Length, Is.EqualTo(Pursuit.ContactKm).Within(1e-6));
            Assert.That(w.Fleet.IsHeld(raider), Is.True, "the schedule still flies a ship that is chasing the player");
            Assert.That(w.Sensors.HelioOf(raider, StartJd).Equals(w.Pursuit.HelioOf(raider, StartJd).Value), Is.True, "the scanners see the raider where its schedule was, not where it is");
        }

        [Test]
        public void TwoHostileGroupsCanBothTurnToChaseOnOneLook()
        {
            // Each look, every raider in reach rolls to chase: up to MaxGroups (two) may start at once, not just the
            // first. The world is seeded, so a dozen raiders in reach make a double on the first look a certainty in
            // practice (each turns about one time in three) unless a look can only ever start one.
            var offset = new DVec3(60000, 0, 0);
            var w = CreateWorld();
            var pilot = Pilot(w, offset);
            for (int i = 0; i < 12; i++) Raider(w);
            int started = 0;
            for (int i = 0; i < 200 && started == 0; i++)
                started = w.Pursuit.Consider(pilot, Earth(w), offset, DVec3.Zero, StartJd).Count;
            Assert.That(started, Is.EqualTo(Pursuit.MaxGroups), "one look started only one chase, though two groups may run the player down at once");
        }

        [Test]
        public void NobodyChasesAShipOfTheirOwnFlagOrWithNoGuns()
        {
            var w = CreateWorld();
            var pilot = Pilot(w, new DVec3(60000, 0, 0));
            var merchant = w.Fleet.AddShip("Trader", ShipClass.Freighter, w.Pirates, ShipRole.Trader);
            w.Fleet.Station(merchant, Earth(w), StartJd);
            for (int i = 0; i < 100; i++) w.Pursuit.Consider(pilot, Earth(w), new DVec3(60000, 0, 0), DVec3.Zero, StartJd);
            Assert.That(w.Pursuit.Groups.Any(g => g.Leader == merchant), Is.False, "an unarmed freighter ran the player down");
        }

        [Test]
        public void TheHuntersCloseChargeAnEmpThatCatchesAShipSittingStillAndEngage()
        {
            var w = CreateWorld();
            // Far out in open space, well clear of any world's neighbourhood.
            var at = new DVec3(4e6, 0, 0);
            var pilot = Pilot(w, at);
            var raider = Raider(w);
            var g = w.Pursuit.Begin(raider, at + new DVec3(Pursuit.ContactKm, 0, 0), DVec3.Zero, StartJd);
            w.Pursuit.Body = Earth(w);
            bool charging = false, hit = false, engage = false;
            double t = 0;
            for (; t < 300 && !engage; t += 0.1)
                foreach (var (_, what) in w.Pursuit.Step(pilot, Earth(w), at, DVec3.Zero, 0.1, StartJd))
                {
                    charging |= what == PursuitEvent.EmpCharging;
                    hit |= what == PursuitEvent.EmpHit;
                    engage |= what == PursuitEvent.Engage;
                }
            Assert.That(charging && hit, Is.True, "a ship sitting still was never caught by the hunters' EMP");
            Assert.That(engage, Is.True, "the hunters never closed to fight");
            Assert.That(t, Is.LessThan(150), "the hunters took far too long to close " + Pursuit.ContactKm + " km");

            var b = w.War.TryStartPursuit(pilot, g, Earth(w), at, DVec3.UnitX, StartJd);
            Assert.That(b, Is.Not.Null, "no fight started in open space");
            w.Pursuit.Engaged(g);
            var mine = b.UnitOf(pilot);
            var theirs = b.UnitOf(raider);
            Assert.That(mine.PlayerControlled, Is.True);
            Assert.That(theirs.Side, Is.Not.EqualTo(mine.Side));
            Assert.That((theirs.Pos - mine.Pos).Length, Is.LessThanOrEqualTo(Pursuit.EngageKm + 1), "the hunter started the fight far from where the chase had it");
            Assert.That(w.Pursuit.Active, Is.False);
        }

        [Test]
        public void AShipThatGetsClearWhileTheEmpChargesIsMissed()
        {
            var w = CreateWorld();
            var at = new DVec3(4e6, 0, 0);
            var pilot = Pilot(w, at);
            var g = w.Pursuit.Begin(Raider(w), at + new DVec3(Pursuit.EmpRangeKm - 2, 0, 0), DVec3.Zero, StartJd);
            w.Pursuit.Body = Earth(w);
            var first = w.Pursuit.Step(pilot, Earth(w), at, DVec3.Zero, 0.1, StartJd);
            Assert.That(first.Any(e => e.what == PursuitEvent.EmpCharging), Is.True);
            var away = at - new DVec3(Pursuit.EmpRangeKm * 3, 0, 0);
            bool missed = false, hit = false;
            for (double t = 0; t < Pursuit.EmpChargeSeconds + 0.5; t += 0.1)
                foreach (var (_, what) in w.Pursuit.Step(pilot, Earth(w), away, DVec3.Zero, 0.1, StartJd))
                {
                    missed |= what == PursuitEvent.EmpMissed;
                    hit |= what == PursuitEvent.EmpHit;
                }
            Assert.That(missed && !hit, Is.True, "an EMP caught a ship that had got well clear while it charged");
        }

        [Test]
        public void AChaseThatRunsTooLongIsGivenUpAndTheHuntersGoBackToTheirSchedule()
        {
            var w = CreateWorld();
            var at = new DVec3(4e6, 0, 0);
            var pilot = Pilot(w, at);
            var raider = Raider(w);
            w.Pursuit.Begin(raider, at + new DVec3(Pursuit.ContactKm, 0, 0), DVec3.Zero, StartJd);
            w.Pursuit.Body = Earth(w);
            // Running flat out directly away at the hunters' own top speed: they never close.
            var vel = new DVec3(-Pursuit.TopSpeedKmS, 0, 0);
            bool gaveUp = false;
            for (double t = 0; t < Pursuit.MaxChaseSeconds + 5 && !gaveUp; t += 0.5)
            {
                at += vel * 0.5;
                gaveUp = w.Pursuit.Step(pilot, Earth(w), at, vel, 0.5, StartJd).Any(e => e.what == PursuitEvent.GaveUp);
            }
            Assert.That(gaveUp, Is.True, "a chase that could never close was never given up");
            Assert.That(w.Pursuit.Active, Is.False);
            Assert.That(w.Fleet.IsHeld(raider), Is.False, "the hunter was left off its schedule");
        }

        /// <summary>The private rechase cooldown book, by reflection: it is never exposed, but a destroyed ship
        /// can never chase again, so it should not still be sitting in it over a long game (U.SPC-T321).</summary>
        static System.Collections.Generic.IDictionary<Ship, double> LastChased(Pursuit p) =>
            (System.Collections.Generic.IDictionary<Ship, double>)typeof(Pursuit)
                .GetField("lastChased", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(p);

        [Test]
        public void ADestroyedRaidersRechaseCooldownEntryIsDroppedNotKeptForever()
        {
            var w = CreateWorld();
            var pilot = Pilot(w, new DVec3(60000, 0, 0));
            var raider = Raider(w);
            Pursuer g = null;
            for (int i = 0; i < 100 && g == null; i++)
                g = w.Pursuit.Consider(pilot, Earth(w), new DVec3(60000, 0, 0), DVec3.Zero, StartJd).FirstOrDefault(x => x.Leader == raider);
            Assert.That(g, Is.Not.Null);
            Assert.That(LastChased(w.Pursuit).ContainsKey(raider), Is.True, "beginning a chase never booked its cooldown");
            raider.Destroyed = true;
            w.Pursuit.Consider(pilot, Earth(w), new DVec3(60000, 0, 0), DVec3.Zero, StartJd + 1);
            Assert.That(LastChased(w.Pursuit).ContainsKey(raider), Is.False, "a destroyed raider's rechase cooldown was never dropped");
        }

        [Test]
        public void AChaseWhoseLeaderIsPulledIntoAnotherFightEndsAndItsEscortsArePinnedWhereTheChaseLeftThem()
        {
            var w = CreateWorld();
            var at = new DVec3(4e6, 0, 0);
            var pilot = Pilot(w, at);
            var raider = Raider(w);
            var escort = w.Fleet.AddShip("Gutter Pick", ShipClass.Destroyer, w.Pirates, ShipRole.Patrol);
            w.Fleet.Station(escort, Earth(w), StartJd);
            escort.Leader = raider;
            var g = w.Pursuit.Begin(raider, at + new DVec3(Pursuit.ContactKm, 0, 0), DVec3.Zero, StartJd);
            w.Pursuit.Body = Earth(w);
            Assert.That(g.Ships, Does.Contain(escort));
            w.Pursuit.Step(pilot, Earth(w), at, DVec3.Zero, 1, StartJd);
            var escortAt = Pursuit.OffsetOf(g, escort);

            // Another fight takes the leader in; the escort stays out of it.
            raider.Battle = new Battle(1, Earth(w), "Battle", DVec3.Zero, StartJd, 1);
            var events = w.Pursuit.Step(pilot, Earth(w), at, DVec3.Zero, 1, StartJd);
            Assert.That(w.Pursuit.Active, Is.False, "the chase went on with its leader fighting elsewhere");
            Assert.That(events.Any(e => e.what == PursuitEvent.GaveUp), Is.True);
            Assert.That(w.Pursuit.Of(escort), Is.Null);
            Assert.That(escort.Orbit.Fixed && escort.Orbit.Body == Earth(w), Is.True, "the escort was not pinned");
            Assert.That((escort.Orbit.Center - escortAt).Length, Is.LessThan(1e-6), "the escort snapped back from where the chase had flown it");
        }

        [Test]
        public void StepGivesUpAChaseWhenThePilotHasMovedIntoAnotherBodysFrame()
        {
            var w = CreateWorld();
            var at = new DVec3(4e6, 0, 0);
            var pilot = Pilot(w, at);
            var raider = Raider(w);
            w.Pursuit.Begin(raider, at + new DVec3(Pursuit.ContactKm, 0, 0), DVec3.Zero, StartJd);
            w.Pursuit.Body = Earth(w);
            int mars = w.Catalog["Mars"].Index;
            // The pilot's offset is still read in the new frame, but the chase's own position is stale, reckoned
            // from Earth: Step must notice the frame no longer matches rather than steer and judge distance on it.
            var events = w.Pursuit.Step(pilot, mars, at, DVec3.Zero, 0.1, StartJd);
            Assert.That(events, Is.Empty, "a frame change produced events instead of simply dropping the stale chase");
            Assert.That(w.Pursuit.Active, Is.False, "the chase stayed active across a frame it no longer matches");
            Assert.That(w.Pursuit.Body, Is.EqualTo(mars), "Step never adopted the pilot's new frame");
            Assert.That(w.Fleet.IsHeld(raider), Is.False, "the hunter was left off its schedule once the frame changed under it");
        }
    }
}
