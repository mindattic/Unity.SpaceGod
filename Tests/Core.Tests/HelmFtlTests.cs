using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The helm's own FTL drive, I-War 2 style (U.SPC-T417): a capital ship or a survey hull, out beyond its system's outer
    /// rim (canon, U.SPC-T455: no jump is possible inside a solar system's gravity wells), points at another star, spools
    /// its drive with the nose held on it, and engages, carrying the hull itself, never a copy, out through open space and
    /// into that system. Small craft cannot, and nothing jumps to somewhere in its own system.
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

        /// <summary>Just beyond <paramref name="s"/>'s outer rim, toward +X.</summary>
        static DVec3 BeyondRim(StarSystem s) => new DVec3(HelmFtl.RimKm(s) * 1.1, 0, 0);

        [Test]
        public void CapitalShipsAndSurveyHullsCanJumpUnderTheirOwnPowerAndSmallCraftCannot()
        {
            var navy = Navy();
            // The owner's roster (U.SPC-T472): carriers, battleships designed with a drive and explorers jump; cruisers and
            // destroyers have no FTL of their own.
            foreach (var c in new[] { ShipClass.Carrier, ShipClass.Battleship, ShipClass.Explorer })
                Assert.That(HelmFtl.Refusal(new Ship { Name = "x", Spec = ShipClassSpec.Of(c), Faction = navy }), Is.Null, c.ToString());
            foreach (var c in new[] { ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Interdictor })
                Assert.That(HelmFtl.Refusal(new Ship { Name = "x", Spec = ShipClassSpec.Of(c), Faction = navy }), Does.Contain("no FTL drive").And.Contain("gates"), c.ToString());
            foreach (var c in Enum.GetValues(typeof(ShipClass)).Cast<ShipClass>().Where(c => CruiseDrives.Of(c) == null))
                Assert.That(HelmFtl.Refusal(new Ship { Name = "x", Spec = ShipClassSpec.Of(c), Faction = navy }), Does.Contain("no FTL drive"), c.ToString());

            // And the helm will not even start the charge for one, even beyond the rim.
            var (g, a, b) = Relays();
            var fighter = Spawn(a, "Gnat", ShipClass.Fighter, navy, BeyondRim(a), DVec3.UnitX);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(fighter, a, FtlTarget.OfStar(b), fighter.Orbit.Center, DVec3.UnitX, false, StartJd), Is.False);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));
            Assert.That(ftl.AbortReason, Does.Contain("gates"));
        }

        [Test]
        public void NoJumpIsPossibleInsideASolarSystemsGravityWells()
        {
            var g = new Galaxy();
            var sol = StarSystem.CreateSol(StartJd, g.Polities);
            g.Systems.Add(sol);
            var far = StarSystem.CreateVoidBase("Void Base North", new DVec3(0, 0, 6), StartJd, g.Polities);
            g.Systems.Add(far);
            // Only other stars are ever offered: no planet, moon, gate or the system's own star.
            var targets = FtlTargets.All(g, sol);
            Assert.That(targets, Is.Not.Empty);
            Assert.That(targets.All(t => t.System != sol), Is.True);
            Assert.That(targets.Any(t => t.System == far), Is.True);
            Assert.That(targets.Any(t => t.Name == "Mars" || t.Name == "Earth"), Is.False);

            // Beside Earth, nose on the far star: refused, the rim named.
            var ship = Spawn(sol, "TNS Meridian", ShipClass.Battleship, Navy(), sol.Ephemeris.HelioAt(sol.Catalog["Earth"].Index, StartJd) + new DVec3(0, 0, 2e6), DVec3.UnitZ);
            var at = ship.Orbit.Center;
            var nose = HelmFtl.Bearing(FtlTarget.OfStar(far), sol, at, out _);
            Assert.That(HelmFtl.BeyondRim(sol, at), Is.False);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, sol, FtlTarget.OfStar(far), at, nose, false, StartJd), Is.False);
            Assert.That(ftl.AbortReason, Does.Contain("gravity wells").And.Contain("rim"));
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Idle));

            // Out past the rim the same jump spools.
            var out_ = at.Normalized * HelmFtl.RimKm(sol) * 1.05;
            Assert.That(HelmFtl.BeyondRim(sol, out_), Is.True);
            nose = HelmFtl.Bearing(FtlTarget.OfStar(far), sol, out_, out _);
            Assert.That(ftl.Spool(ship, sol, FtlTarget.OfStar(far), out_, nose, false, StartJd), Is.True, ftl.AbortReason);

            // Fall back inside the rim while charging and the charge is lost.
            Assert.That(ftl.Step(sol, at, nose, false, StartJd + 0.01), Is.EqualTo(FtlEvent.Aborted));
            Assert.That(ftl.AbortReason, Does.Contain("gravity wells"));

            // Never to its own star, even from beyond the rim.
            Assert.That(ftl.Spool(ship, sol, FtlTarget.OfStar(sol), out_, -out_.Normalized, false, StartJd), Is.False);
        }

        [Test]
        public void TheTargetIsTheStarTheNoseIsOn()
        {
            var g = new Galaxy();
            var home = StarSystem.CreateVoidBase("Void Base Home", new DVec3(0, 0, 0), StartJd, g.Polities);
            var east = StarSystem.CreateVoidBase("Void Base East", new DVec3(5, 0, 0), StartJd, g.Polities);
            var north = StarSystem.CreateVoidBase("Void Base North", new DVec3(0, 6, 0), StartJd, g.Polities);
            g.Systems.Add(home);
            g.Systems.Add(east);
            g.Systems.Add(north);
            var targets = FtlTargets.All(g, home);
            var from = BeyondRim(home);
            Assert.That(HelmFtl.Pointed(targets, home, from, DVec3.UnitX)?.System, Is.SameAs(east));
            var toNorth = HelmFtl.Bearing(FtlTarget.OfStar(north), home, from, out _);
            Assert.That(HelmFtl.Pointed(targets, home, from, toNorth)?.System, Is.SameAs(north));
            // A couple of degrees off still finds it; well off it, nothing.
            Assert.That(HelmFtl.Pointed(targets, home, from, PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 2 * Astro.Deg2Rad))?.System, Is.SameAs(east));
            Assert.That(HelmFtl.Pointed(targets, home, from, PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 40 * Astro.Deg2Rad)), Is.Null);
        }

        [Test]
        public void TheChargeHoldsOnlyWhileTheNoseIsHeldOnTheStarAndOutOfAFight()
        {
            var (g, a, b) = Relays();
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, Navy(), BeyondRim(a), DVec3.UnitX);
            var target = FtlTarget.OfStar(b);
            var at = ship.Orbit.Center;
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, target, at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            Assert.That(ftl.Stage, Is.EqualTo(FtlStage.Spooling));
            Assert.That(ftl.ChargedJd - StartJd, Is.EqualTo(CruiseDrives.Capital.SpoolDays).Within(1e-9), "it charges for its drive's spool");
            Assert.That(ftl.Step(a, at, DVec3.UnitX, false, StartJd + 0.1), Is.EqualTo(FtlEvent.None));
            Assert.That(ftl.SpoolFraction(StartJd + 0.1), Is.EqualTo(0.1 / CruiseDrives.Capital.SpoolDays).Within(1e-6));
            // A small wander is held; a hard turn loses the charge.
            Assert.That(ftl.Step(a, at, PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 5 * Astro.Deg2Rad), false, StartJd + 0.15), Is.EqualTo(FtlEvent.None));
            Assert.That(ftl.Step(a, at, PilotFlight.RotateAbout(DVec3.UnitX, DVec3.UnitZ, 20 * Astro.Deg2Rad), false, StartJd + 0.2), Is.EqualTo(FtlEvent.Aborted));
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
        public void AJumpToAnotherStarCarriesTheHullItselfAcrossAndIntoThatSystem()
        {
            var (g, a, b) = Relays(4);
            var navy = Navy();
            var bystander = Spawn(a, "TNS Bystander", ShipClass.Freighter, navy, new DVec3(1e8, 5e7, 0), DVec3.UnitX);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Battleship, navy, BeyondRim(a), DVec3.UnitX);
            var after = Spawn(a, "TNS After", ShipClass.Freighter, navy, new DVec3(1e8, -5e7, 0), DVec3.UnitX);
            var escort = Spawn(a, "TNS Gnat", ShipClass.Fighter, navy, BeyondRim(a) + new DVec3(0, 10, 0), DVec3.UnitX);
            escort.Leader = ship;
            escort.Role = ShipRole.Escort;
            int oldId = ship.Id, afterId = after.Id;
            var hull = ship.Combat;
            ship.Credits = 12345;

            var at = ship.Orbit.Center;
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, FtlTarget.OfStar(b), at, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
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
            // The fighter it led has no drive of its own, so it goes docked aboard (U.SPC-T419), the same hull too.
            Assert.That(c.Ships, Is.EqualTo(new[] { ship, escort }));
            Assert.That(c.Aboard[escort], Is.SameAs(ship));
            Assert.That(escort.Leader, Is.SameAs(ship));
            Assert.That(a.World.Fleet.Ships.Contains(escort), Is.False);

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
            // Its fighter comes out with it, still its wing.
            Assert.That(b.World.Fleet.Ships.Contains(escort), Is.True, "the docked fighter never arrived");
            Assert.That(escort.Leader, Is.SameAs(ship));
            Assert.That(escort.Destroyed, Is.False);
        }
    }
}
