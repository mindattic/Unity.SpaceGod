using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The helm's own FTL drive, I-War 2 style (U.SPC-T417): a capital ship or a survey hull points at a star, a planet
    /// or a beacon, spools its drive with the nose held on it, engages and later drops out beside it; small craft cannot.
    /// A jump to another star carries the hull itself, never a copy, out through open space and into that system.
    /// </summary>
    [TestFixture]
    public class HelmFtlTests
    {
        const double StartJd = 2461324.0;

        static (Galaxy g, StarSystem a, StarSystem b) Relays(double ly = 4)
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(ly, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            return (g, a, b);
        }

        static Faction Navy() => new Faction("Test Navy", "TNS", default) { IsMilitary = true };

        static Ship Spawn(StarSystem s, string name, ShipClass cls, Faction f, DVec3 atKm, DVec3 forward) =>
            s.World.Fleet.SpawnAt(name, cls, f, s.Catalog.Root.Index, atKm, forward, StartJd);

        [Test]
        public void CapitalShipsAndSurveyHullsCanJumpUnderTheirOwnPowerAndSmallCraftCannot()
        {
            var navy = Navy();
            foreach (var c in new[] { ShipClass.Carrier, ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Courier })
                Assert.That(HelmFtl.Refusal(new Ship { Name = "x", Spec = ShipClassSpec.Of(c), Faction = navy }), Is.Null, c.ToString());
            foreach (var c in Enum.GetValues(typeof(ShipClass)).Cast<ShipClass>().Where(c => CruiseDrives.Of(c) == null))
            {
                var why = HelmFtl.Refusal(new Ship { Name = "x", Spec = ShipClassSpec.Of(c), Faction = navy });
                Assert.That(why, Does.Contain("too small"), c.ToString());
            }

            // And the helm will not even start the charge for one.
            var (g, a, b) = Relays();
            var fighter = Spawn(a, "Gnat", ShipClass.Fighter, navy, new DVec3(1e7, 0, 0), DVec3.UnitX);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(fighter, a, FtlTarget.OfStar(b), fighter.Orbit.Center, DVec3.UnitX, false, StartJd), Is.False);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));
            Assert.That(ftl.AbortReason, Does.Contain("gates"));
        }

        [Test]
        public void TheTargetIsWhateverTheNoseIsOn()
        {
            var g = new Galaxy();
            var sol = StarSystem.CreateSol(StartJd, g.Polities);
            g.Systems.Add(sol);
            var far = StarSystem.CreateVoidBase("Void Base North", new DVec3(0, 0, 6), StartJd, g.Polities);
            g.Systems.Add(far);
            var targets = FtlTargets.All(g, sol);
            Assert.That(targets.Any(t => t.Kind == FtlTargetKind.Star && t.System == far), Is.True, "another system's star");
            Assert.That(targets.Any(t => t.Kind == FtlTargetKind.Star && t.System == sol), Is.False, "not its own");
            Assert.That(targets.Any(t => t.Kind == FtlTargetKind.Body && t.Name == "Mars"), Is.True, "a planet");

            int earth = sol.Catalog["Earth"].Index, mars = sol.Catalog["Mars"].Index;
            // Out beside Earth, nose on Mars: Mars.
            var from = sol.Ephemeris.HelioAt(earth, StartJd) + new DVec3(0, 0, 2e6);
            var toMars = (sol.Ephemeris.HelioAt(mars, StartJd) - from).Normalized;
            var pointed = HelmFtl.Pointed(targets, sol, from, toMars, StartJd);
            Assert.That(pointed?.Name, Is.EqualTo("Mars"));
            // A couple of degrees off still finds it; well off it, nothing.
            var nudged = PilotFlight.RotateAbout(toMars, DVec3.UnitZ, 2 * Astro.Deg2Rad);
            Assert.That(HelmFtl.Pointed(targets, sol, from, nudged, StartJd)?.Name, Is.EqualTo("Mars"));
            var away = PilotFlight.RotateAbout(toMars, DVec3.UnitZ, 40 * Astro.Deg2Rad);
            Assert.That(HelmFtl.Pointed(targets, sol, from, away, StartJd)?.Name, Is.Not.EqualTo("Mars"));
            // Straight up out of the ecliptic, the star six light-years that way.
            var up = (far.Origin.KmFrom(sol.At(from))).Normalized;
            Assert.That(HelmFtl.Pointed(targets, sol, from, up, StartJd)?.System, Is.SameAs(far));
            // Two million km out Earth is a jump away; eighty thousand, it is too near to need the drive, so never picked.
            var toEarth = (sol.Ephemeris.HelioAt(earth, StartJd) - from).Normalized;
            Assert.That(HelmFtl.Pointed(targets, sol, from, toEarth, StartJd)?.Name, Is.EqualTo("Earth"));
            var near = sol.Ephemeris.HelioAt(earth, StartJd) + new DVec3(0, 0, 8e4);
            Assert.That(HelmFtl.Pointed(targets, sol, near, -DVec3.UnitZ, StartJd)?.Name, Is.Not.EqualTo("Earth"));
        }

        [Test]
        public void TheChargeHoldsOnlyWhileTheNoseIsHeldOnTheTargetAndOutOfAFight()
        {
            var (g, a, b) = Relays();
            var navy = Navy();
            var ship = Spawn(a, "TNS Meridian", ShipClass.Cruiser, navy, new DVec3(3e8, 0, 0), DVec3.UnitX);
            var target = FtlTarget.OfStar(b);
            var at = ship.Orbit.Center;
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Spooling));
            Assert.That(ftl.ChargedJd - StartJd, Is.EqualTo(CruiseDrives.Capital.SpoolDays).Within(1e-12), "it charges for its drive's spool");
            Assert.That(ftl.Step(a, at, DVec3.UnitX, false, StartJd + 0.1), Is.EqualTo(FtlEvent.None));
            Assert.That(ftl.SpoolFraction(StartJd + 0.1), Is.EqualTo(0.1 / CruiseDrives.Capital.SpoolDays).Within(1e-9));
            // A small wander is held; a hard turn loses the charge.
            var wander = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 5 * Astro.Deg2Rad);
            Assert.That(ftl.Step(a, at, wander, false, StartJd + 0.15), Is.EqualTo(FtlEvent.None));
            var turned = PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 20 * Astro.Deg2Rad);
            Assert.That(ftl.Step(a, at, turned, false, StartJd + 0.2), Is.EqualTo(FtlEvent.Aborted));
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));
            Assert.That(ftl.AbortReason, Does.Contain("swung off"));

            // Again, and a fight breaks out.
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, false, StartJd + 0.3), Is.True);
            Assert.That(ftl.Step(a, at, DVec3.UnitX, true, StartJd + 0.35), Is.EqualTo(FtlEvent.Aborted));
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, true, StartJd + 0.4), Is.False, "nor will it start in one");

            // Held to the end, it is charged.
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, false, StartJd + 0.5), Is.True);
            Assert.That(ftl.Step(a, at, DVec3.UnitX, false, ftl.ChargedJd), Is.EqualTo(FtlEvent.Charged));
        }

        [Test]
        public void AJumpWithinTheSystemDropsOutBesideAPlanetWhereItWillBe()
        {
            var g = new Galaxy();
            var sol = StarSystem.CreateSol(StartJd, g.Polities);
            g.Systems.Add(sol);
            var navy = Navy();
            int earth = sol.Catalog["Earth"].Index, mars = sol.Catalog["Mars"].Index;
            var from = sol.Ephemeris.HelioAt(earth, StartJd) + new DVec3(0, 0, 2e6);
            var ship = Spawn(sol, "TNS Pathfinder", ShipClass.Courier, navy, from, DVec3.UnitX);
            var target = FtlTarget.OfBody(sol, mars);
            var nose = HelmFtl.Bearing(target, sol, from, StartJd, out double km);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, sol, target, from, nose, false, StartJd), Is.True, ftl.AbortReason);
            double charged = ftl.ChargedJd;
            Assert.That(ftl.Step(sol, from, nose, false, charged), Is.EqualTo(FtlEvent.Charged));
            ftl.EngageInSystem(sol, from, charged);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Engaged));
            Assert.That(ftl.Profile.SpoolDays, Is.EqualTo(0), "no second charge once under way");
            double arrive = ftl.ArriveJd;
            Assert.That(arrive - charged, Is.EqualTo(ftl.Profile.FlightDays).Within(1e-8));
            // Faster than light (light takes km / c), but it takes a while, and it does get there.
            double lightDays = km / 299792.458 / Astro.SecondsPerDay;
            Assert.That(arrive - charged, Is.LessThan(lightDays), "no faster than light");
            Assert.That(arrive - charged, Is.GreaterThan(lightDays / 1000), "instant");

            // Along the line, never back, and out beside Mars where Mars has got to by then, outside its pull.
            double last = -1;
            for (int i = 0; i <= 20; i++)
            {
                double jd = charged + (arrive - charged) * i / 20;
                double along = (ftl.PositionKmAt(jd) - from).Length;
                Assert.That(along, Is.GreaterThanOrEqualTo(last - 1e-6));
                last = along;
            }
            Assert.That(ftl.SpeedKmS(charged + (arrive - charged) / 2), Is.GreaterThan(299792.458), "not faster than light");
            var marsThen = sol.Ephemeris.HelioAt(mars, arrive);
            double standOff = (ftl.PositionKmAt(arrive) - marsThen).Length;
            Assert.That(standOff, Is.EqualTo(target.ClearanceKm).Within(target.ClearanceKm * 0.05));
            Assert.That(standOff, Is.GreaterThan(sol.Catalog[mars].RadiusKm * PilotFlight.GravityReachRadii));
            Assert.That(ftl.Step(sol, ftl.PositionKmAt(arrive), nose, false, arrive - 1e-6), Is.EqualTo(FtlEvent.None));
            Assert.That(ftl.Step(sol, ftl.PositionKmAt(arrive), nose, false, arrive), Is.EqualTo(FtlEvent.Arrived));
            ftl.Arrived();
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));
        }

        [Test]
        public void AJumpToAGateDropsOutWithinReachOfOpeningIt()
        {
            var (g, a, b) = Relays();
            var gateKm = new DVec3(4e8, 1e7, 0);
            var target = FtlTarget.OfBeacon(a, "Test Gate", gateKm, JumpGate.ActivationRangeKm * 0.5);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Cruiser, Navy(), new DVec3(-2e8, 0, 0), DVec3.UnitX);
            var from = ship.Orbit.Center;
            var nose = HelmFtl.Bearing(target, a, from, StartJd, out _);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, target, from, nose, false, StartJd), Is.True, ftl.AbortReason);
            ftl.EngageInSystem(a, from, ftl.ChargedJd);
            Assert.That((ftl.PositionKmAt(ftl.ArriveJd) - gateKm).Length, Is.LessThanOrEqualTo(JumpGate.ActivationRangeKm));
        }

        [Test]
        public void AJumpToAnotherStarCarriesTheHullItselfAcrossAndIntoThatSystem()
        {
            var (g, a, b) = Relays(4);
            var navy = Navy();
            var bystander = Spawn(a, "TNS Bystander", ShipClass.Freighter, navy, new DVec3(1e8, 5e7, 0), DVec3.UnitX);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Cruiser, navy, new DVec3(3e8, 0, 0), DVec3.UnitX);
            var after = Spawn(a, "TNS After", ShipClass.Freighter, navy, new DVec3(1e8, -5e7, 0), DVec3.UnitX);
            var escort = Spawn(a, "TNS Gnat", ShipClass.Fighter, navy, new DVec3(3e8, 10, 0), DVec3.UnitX);
            escort.Leader = ship;
            escort.Role = ShipRole.Escort;
            int oldId = ship.Id, afterId = after.Id;
            var hull = ship.Combat;
            ship.Credits = 12345;

            var target = FtlTarget.OfStar(b);
            Assert.That(target.Interstellar(a), Is.True);
            var at = ship.Orbit.Center;
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            double jd = ftl.ChargedJd;
            var c = ftl.EngageInterstellar(g, a, ship, at, jd);
            Assert.That(c, Is.Not.Null, ftl.AbortReason);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));
            Assert.That(c.Piloted, Is.True);

            // The same hull goes, not a copy; its place here holds a marker, gone, and every other id stays put.
            Assert.That(c.Leader, Is.SameAs(ship));
            Assert.That(ship.Destroyed || ship.GateDeparted, Is.False);
            var marker = a.World.Fleet.Ships[oldId];
            Assert.That(marker, Is.Not.SameAs(ship));
            Assert.That(marker.Destroyed && marker.GateDeparted, Is.True);
            Assert.That(marker.Name, Is.EqualTo(ship.Name));
            Assert.That(a.World.Fleet.Ships[afterId], Is.SameAs(after));
            Assert.That(after.Id, Is.EqualTo(afterId));
            Assert.That(a.World.Fleet.Ships.Contains(ship), Is.False);
            Assert.That(bystander.Destroyed, Is.False);
            // The fighter it led cannot follow: it is left behind, following the marker for the fleet's succession.
            Assert.That(escort.Leader, Is.SameAs(marker));
            Assert.That(c.Ships, Has.Count.EqualTo(1));

            // Already charged: it is under way at once.
            Assert.That(c.Engaged(jd + 1e-6), Is.True);
            Assert.That(c.ArriveJd - jd, Is.EqualTo(c.Profile.FlightDays).Within(1e-9));
            for (double t = jd; t < c.ArriveJd + 0.1; t += 0.05) g.Update(t);
            g.Update(c.ArriveJd + 0.1);

            Assert.That(g.DeepSpace.Cruises, Is.Empty);
            Assert.That(b.World.Fleet.Ships.Contains(ship), Is.True, "it never arrived");
            Assert.That(b.World.Fleet.Ships[ship.Id], Is.SameAs(ship));
            Assert.That(ship.Destroyed || ship.GateDeparted, Is.False);
            Assert.That(ship.Combat, Is.SameAs(hull));
            Assert.That(ship.Credits, Is.EqualTo(12345));
            // At the near edge of the far well, and left where it dropped out for the helm to fly on.
            Assert.That(ship.Orbit.Center.Length, Is.EqualTo(CruiseDrives.WellLimitKm(b)).Within(1e-3));
            Assert.That(ship.Orbit.Center.X, Is.LessThan(0));
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Parked));
            Assert.That(ship.Orbit.Fixed, Is.True);
        }

        [Test]
        public void ABeaconSetOutByNameIsOfferedInItsSystem()
        {
            var (g, a, b) = Relays();
            var spec = new FtlTargets.BeaconSpec(a.Name, "Survey Marker 7", new DVec3(5e8, 0, 0));
            FtlTargets.Beacons.Add(spec);
            try
            {
                Assert.That(FtlTargets.All(g, a).Any(t => t.Kind == FtlTargetKind.Beacon && t.Name == "Survey Marker 7"), Is.True);
                Assert.That(FtlTargets.All(g, b).Any(t => t.Name == "Survey Marker 7"), Is.False);
            }
            finally
            {
                FtlTargets.Beacons.Remove(spec);
            }
        }
    }
}
