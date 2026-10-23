using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// FTL physics and rules (U.SPC-T418): wells about stars and planets set by mass, interdiction by a star's mass or a
    /// gravity projector on the way, spool-up and cooldown, the reaction mass a jump burns, momentum carried through, and a
    /// top speed that makes the galaxy long but finishable, every limit in the settings menu's FTL group.
    /// Non-parallel: some of it turns the menu's FTL settings, which every drive reads.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class FtlPhysicsTests
    {
        const double StartJd = 2461324.0;

        static (Galaxy g, StarSystem a, StarSystem b) Relays(double ly)
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(ly, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            return (g, a, b);
        }

        static Faction Navy() => new Faction("Test Navy", "TNS", default) { IsMilitary = true };

        static Ship Hull(string name, ShipClass cls, Faction faction) => new Ship
        {
            Name = name, Spec = ShipClassSpec.Of(cls), Faction = faction, Role = ShipRole.Free, Combat = new ShipCombat(CombatSpec.Of(cls), cls),
        };

        static Ship Spawn(StarSystem s, string name, ShipClass cls, Faction f, DVec3 atKm, DVec3 forward) =>
            s.World.Fleet.SpawnAt(name, cls, f, s.Catalog.Root.Index, atKm, forward, StartJd);

        static DVec3 BeyondRim(StarSystem s, double sign = 1) => new DVec3(sign * HelmFtl.RimKm(s) * 1.1, 0, 0);

        static void Run(Galaxy g, double from, double to, double step = 0.05)
        {
            for (double jd = from + step; jd <= to + 1e-9; jd += step) g.Update(jd);
        }

        [Test]
        public void AWellIsWhereABodysPullIsAboveTheThresholdSoItScalesWithMass()
        {
            var sol = StarSystem.CreateSol(StartJd);
            double sun = FtlRules.WellRadiusKm(FtlRules.StarsGm(sol));
            Assert.That(sun / Astro.AuKm, Is.InRange(1.8, 2.2), "about two AU about the Sun at the default");
            double jupiter = FtlRules.WellRadiusKm(sol.Catalog["Jupiter"].Gm);
            double earth = FtlRules.WellRadiusKm(sol.Catalog["Earth"].Gm);
            Assert.That(jupiter, Is.InRange(8e6, 1.1e7));
            Assert.That(earth, Is.InRange(4e5, 6e5));
            // A weaker threshold makes every well wider, by the square root.
            Assert.That(FtlRules.WellRadiusKm(sol.Catalog["Earth"].Gm, FtlRules.WellGravity.Value / 4), Is.EqualTo(2 * earth).Within(1));
            // A plain star's mass comes from its class.
            Assert.That(FtlRules.SolarMasses("B3V"), Is.GreaterThan(FtlRules.SolarMasses("G2V")));
            Assert.That(FtlRules.SolarMasses("M5.5Ve"), Is.LessThan(FtlRules.SolarMasses("G2V")));

            // Beside Earth: inside its well; well clear of every planet: none.
            int ei = sol.Catalog["Earth"].Index;
            var nearEarth = sol.Ephemeris.Helio(ei) + new DVec3(1e5, 0, 0);
            Assert.That(FtlRules.BodyWellAt(sol, nearEarth)?.Name, Is.EqualTo("Earth"));
            var cleared = FtlRules.ClearOfBodyWells(sol, nearEarth);
            Assert.That(FtlRules.BodyWellAt(sol, cleared), Is.Null);
            Assert.That((cleared - sol.Ephemeris.Helio(ei)).Length, Is.EqualTo(earth).Within(earth * 0.01), "moved just past the edge");
        }

        [Test]
        public void AGroupWhoseWayRunsThroughAStarsWellIsPulledOutAndCrossesItUnderItsOrdinaryDrive()
        {
            var (g, a, b) = Relays(8);
            // A Sun-like star dead on the line half way.
            g.NearbyStars.Add(new NearbyStar("Midway", new DVec3(4, 0, 0), "G2V", 5, 5800));
            var mid = g.NearbyStars[0];
            double well = FtlRules.StarWell(mid).RadiusKm;
            var ship = Hull("TNS Meridian", ShipClass.Cruiser, Navy());
            var from = DeepSpace.DeparturePointKm(a, b.Origin);
            var c = g.DeepSpace.Launch(new[] { ship }, a, from, b, CruiseDrives.Capital, StartJd);
            double plain = c.ArriveJd;

            double jd = StartJd;
            while (!c.Crossing && jd < plain)
            {
                jd += 0.05;
                g.Update(jd);
            }
            Assert.That(c.Crossing, Is.True, "it flew straight through the star's well");
            Assert.That(c.InterdictedBy, Is.EqualTo("Midway"));
            // Pulled out at the edge of the well, on the near side.
            Assert.That(c.Start.DistanceKm(mid.Origin), Is.EqualTo(well).Within(well * 1e-3));
            Assert.That(c.Start.KmFrom(mid.Origin).X, Is.LessThan(0));
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("pulled out of FTL by Midway's gravity well")), Is.True);
            // Its drive cooling from the drop-out, and crossing at the ordinary drive's pace, out to the far side.
            Assert.That(ship.FtlCoolUntilJd, Is.GreaterThan(c.LegJd));
            Assert.That(c.SpeedKmS(c.LegJd + 0.1), Is.EqualTo(FtlRules.CrossingSpeedKmS.Value).Within(1));
            Assert.That(c.End.DistanceKm(mid.Origin), Is.EqualTo(well).Within(well * 1e-3));
            Assert.That(c.End.KmFrom(mid.Origin).X, Is.GreaterThan(0));
            Assert.That(c.PositionAt((c.LegJd + c.ArriveJd) / 2).DistanceKm(mid.Origin), Is.LessThan(well), "it crosses inside the well");

            Run(g, jd, plain + 6);
            Assert.That(b.World.Fleet.Ships.Contains(ship), Is.True, "it never went on to where it was going");
            Assert.That(ship.PhaseStartJd, Is.GreaterThan(plain + 2 * well / FtlRules.CrossingSpeedKmS.Value / Astro.SecondsPerDay * 0.9),
                "the crossing under the ordinary drive cost it the time");
        }

        [Test]
        public void AGravityProjectorPullsOutEveryoneButItsOwnSide()
        {
            var (g, a, b) = Relays(6);
            var clan = new Faction("Test Clan", "", default) { IsPirate = true };
            var projector = g.DeepSpace.AddProjector(new GravityProjector
            {
                Name = "Snare", Owner = clan, Position = a.Origin.Offset(new DVec3(3 * Astro.LightYearKm, 0, 0)), RadiusKm = 5e7,
            });
            bool Pulled(Galaxy gx, StarSystem from, StarSystem to, Ship hull)
            {
                var c = gx.DeepSpace.Launch(new[] { hull }, from, DeepSpace.DeparturePointKm(from, to.Origin), to, CruiseDrives.Capital, StartJd);
                bool pulled = false;
                for (double jd = StartJd + 0.05; jd < StartJd + 5; jd += 0.05)
                {
                    gx.Update(jd);
                    pulled |= c.Crossing;
                }
                return pulled;
            }

            Assert.That(Pulled(g, a, b, Hull("TNS Meridian", ShipClass.Cruiser, Navy())), Is.True, "the projector never pulled the navy out");
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("the gravity projector Snare")), Is.True);

            // Its own side's hulls fly through it.
            var (g1, a1, b1) = Relays(6);
            g1.DeepSpace.AddProjector(projector);
            Assert.That(Pulled(g1, a1, b1, Hull("Black Gull", ShipClass.Destroyer, clan)), Is.False, "it pulled out its own side");

            // Switched off, it pulls nobody.
            projector.Active = false;
            var (g2, a2, b2) = Relays(6);
            g2.DeepSpace.AddProjector(projector);
            Assert.That(Pulled(g2, a2, b2, Hull("TNS Ardent", ShipClass.Destroyer, Navy())), Is.False);

            // At the helm, inside a live field the drive will not even spool.
            projector.Active = true;
            projector.Position = a2.At(BeyondRim(a2));
            var helm = Spawn(a2, "TNS Helm", ShipClass.Cruiser, Navy(), BeyondRim(a2), DVec3.UnitX);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(helm, a2, FtlTarget.OfStar(b2), helm.Orbit.Center, DVec3.UnitX, false, StartJd, g2), Is.False);
            Assert.That(ftl.AbortReason, Does.Contain("gravity projector"));
        }

        [Test]
        public void AfterAJumpTheDriveCoolsBeforeItCanChargeAgainAndEveryJumpBurnsReactionMass()
        {
            var (g, a, b) = Relays(4);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Cruiser, Navy(), BeyondRim(a), DVec3.UnitX);
            var drive = CruiseDrives.Capital;
            var ftl = new HelmFtl();

            // Too little in the tanks: refused, and how far it would go named.
            ship.Propellant = ship.PropellantCapacity * 0.01;
            Assert.That(ftl.Spool(ship, a, FtlTarget.OfStar(b), ship.Orbit.Center, DVec3.UnitX, false, StartJd), Is.False);
            Assert.That(ftl.AbortReason, Does.Contain("reaction mass"));

            ship.Propellant = ship.PropellantCapacity;
            double full = ship.Propellant;
            Assert.That(ftl.Spool(ship, a, FtlTarget.OfStar(b), ship.Orbit.Center, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            double charged = ftl.ChargedJd;
            var c = ftl.EngageInterstellar(g, a, ship, ship.Orbit.Center, charged);
            Assert.That(c, Is.Not.Null, ftl.AbortReason);
            double burned = full - ship.Propellant;
            Assert.That(burned, Is.EqualTo(FtlRules.PropellantFor(drive, ship, c.Profile.Ly)).Within(1e-6));
            Assert.That(burned / ship.PropellantCapacity, Is.EqualTo(drive.ChargePropellantShare + drive.PropellantSharePerLy * c.Profile.Ly).Within(1e-9));
            Assert.That(FtlRules.RangeLy(drive, ship), Is.GreaterThan(100), "a capital ship's tanks take it far");

            Run(g, charged, c.ArriveJd + 0.1);
            Assert.That(b.World.Fleet.Ships.Contains(ship), Is.True);
            double dropped = c.ArriveJd;
            Assert.That(ship.FtlCoolUntilJd, Is.EqualTo(dropped + drive.CooldownDays).Within(1e-6));

            // Straight back out past the far rim and home: still cooling.
            var back = BeyondRim(b, -1);
            Assert.That(ftl.Spool(ship, b, FtlTarget.OfStar(a), back, -DVec3.UnitX, false, dropped + 0.1), Is.False);
            Assert.That(ftl.AbortReason, Does.Contain("cooling"));
            Assert.That(ftl.Spool(ship, b, FtlTarget.OfStar(a), back, -DVec3.UnitX, false, ship.FtlCoolUntilJd + 1e-6), Is.True, ftl.AbortReason);
        }

        [Test]
        public void AGroupThatDroppedOutWaitsForItsDrivesToCoolBeforeItCharges()
        {
            var (g, a, b) = Relays(4);
            var ship = Hull("TNS Meridian", ShipClass.Cruiser, Navy());
            ship.FtlCoolUntilJd = StartJd + 1;
            var c = g.DeepSpace.Launch(new[] { ship }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            Assert.That(c.Profile.SpoolDays, Is.EqualTo(1 + CruiseDrives.Capital.SpoolDays).Within(1e-9));
            Assert.That(c.Engaged(StartJd + 1), Is.False);
            Assert.That(c.Engaged(StartJd + 1 + CruiseDrives.Capital.SpoolDays + 0.01), Is.True);
        }

        [Test]
        public void TheHullComesOutOfFtlWithTheMomentumItWentInWith()
        {
            var (g, a, b) = Relays(4);
            var ship = Spawn(a, "TNS Meridian", ShipClass.Cruiser, Navy(), BeyondRim(a), DVec3.UnitX);
            var ftl = new HelmFtl();
            Assert.That(ftl.Spool(ship, a, FtlTarget.OfStar(b), ship.Orbit.Center, DVec3.UnitX, false, StartJd), Is.True, ftl.AbortReason);
            var v = new DVec3(12.5, -3, 0.25);
            var c = ftl.EngageInterstellar(g, a, ship, ship.Orbit.Center, ftl.ChargedJd, v);
            Assert.That((c.CarriedVelocityKmS - v).Length, Is.LessThan(1e-12));
            // And straight on: the drive does not turn, only drops out.
            Assert.That(DVec3.Dot(c.Direction, DVec3.UnitX), Is.GreaterThan(0.999999));
        }

        [Test]
        public void TheTopSpeedMakesTheGalaxyLongButFinishable()
        {
            var drive = CruiseDrives.Capital;
            double years = FtlRules.GalaxyCrossingDays(drive) / 365.25;
            Assert.That(years, Is.InRange(200, 400), "centuries at the rated speed");
            var speed = CruiseDrives.SpeedScale;
            try
            {
                speed.Set(speed.Max);
                double fastest = FtlRules.GalaxyCrossingDays(drive) / 365.25;
                Assert.That(fastest, Is.InRange(1, 10), "years at the menu's top speed: long, but a clock at a year a second gets there");
            }
            finally
            {
                speed.Reset();
            }
        }

        [Test]
        public void EveryLimitIsInTheSettingsMenusFtlGroupAndTakesEffect()
        {
            var settings = new GameSetting[]
            {
                FtlRules.WellGravity, FtlRules.SpoolScale, FtlRules.CooldownScale, FtlRules.FuelScale, FtlRules.CrossingSpeedKmS,
                CruiseDrives.SpeedScale, CruiseDrives.WellLimit,
            };
            foreach (var s in settings) Assert.That(s.Group, Is.EqualTo(SettingGroups.Ftl), s.Key);
            Assert.That(settings.Select(s => s.Key).Distinct().Count(), Is.EqualTo(settings.Length));

            var drive = CruiseDrives.Capital;
            var ship = Hull("TNS Meridian", ShipClass.Cruiser, Navy());
            double earth = FtlRules.WellRadiusKm(398600.4418);
            try
            {
                FtlRules.SpoolScale.Set(2);
                Assert.That(drive.SpoolDays, Is.EqualTo(2 * drive.RatedSpoolDays).Within(1e-12));
                FtlRules.CooldownScale.Set(0);
                Assert.That(drive.CooldownDays, Is.EqualTo(0));
                FtlRules.FuelScale.Set(0);
                Assert.That(FtlRules.PropellantFor(drive, ship, 50), Is.EqualTo(0), "free jumps");
                Assert.That(FtlRules.RangeLy(drive, ship), Is.EqualTo(double.PositiveInfinity));
                FtlRules.WellGravity.Set(FtlRules.WellGravity.Default / 3);
                Assert.That(FtlRules.WellRadiusKm(398600.4418), Is.EqualTo(Math.Sqrt(3) * earth).Within(earth * 0.01), "a weaker threshold, a wider well");
            }
            finally
            {
                foreach (var s in settings) s.Reset();
            }
            Assert.That(drive.SpoolDays, Is.EqualTo(drive.RatedSpoolDays));
            Assert.That(FtlRules.PropellantFor(drive, ship, 50), Is.GreaterThan(0));
        }
    }
}
