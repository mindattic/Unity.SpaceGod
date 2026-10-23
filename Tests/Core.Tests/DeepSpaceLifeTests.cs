using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// Life in deep space (U.SPC-T421): the routes between the stars seeded with rogue planets, nebulae, ice bodies,
    /// derelicts and the powers' relays and listening posts, the same every time; what each does to a group passing it;
    /// distress calls and lures; survey finds that pay most off the gate network; and bands lying in wait on FTL routes.
    /// </summary>
    [TestFixture]
    public class DeepSpaceLifeTests
    {
        const double StartJd = 2461324.0;

        static (Galaxy g, StarSystem a, StarSystem b) Beacons(double ly = 4, bool life = false)
        {
            var g = new Galaxy();
            var a = StarSystem.CreateVoidBase("Void Base West", new DVec3(0, 0, 0), StartJd, g.Polities);
            var b = StarSystem.CreateVoidBase("Void Base East", new DVec3(ly, 0, 0), StartJd, g.Polities);
            g.Systems.Add(a);
            g.Systems.Add(b);
            g.DeepSpace.RouteLife = life;
            return (g, a, b);
        }

        static Ship Hull(string name, ShipClass cls, Faction faction) => new Ship
        {
            Name = name, Spec = ShipClassSpec.Of(cls), Faction = faction, Role = ShipRole.Free, Combat = new ShipCombat(CombatSpec.Of(cls), cls),
        };

        static void Run(Galaxy g, double from, double to, double step = 0.05)
        {
            for (double jd = from + step; jd <= to + 1e-9; jd += step) g.Update(jd);
        }

        /// <summary>A point <paramref name="ly"/> along the line from <paramref name="a"/> toward <paramref name="b"/>, <paramref name="offKm"/> off it.</summary>
        static GalacticPosition Along(StarSystem a, StarSystem b, double ly, double offKm = 0) =>
            a.Origin.Offset(b.Origin.KmFrom(a.Origin).Normalized * (ly * Astro.LightYearKm) + new DVec3(0, offKm, 0));

        // ---------------------------------------------------------------- the routes, seeded

        [Test]
        public void ARouteIsSeededTheSameEveryTimeAndOffTheNetworkItIsRicher()
        {
            string Describe(Galaxy g) => string.Join(";", g.DeepSpace.Objects.Select(o => o.Kind + ":" + o.Name + "@" + o.Position.KmFrom(GalacticPosition.Origin).Length.ToString("R")));
            var (g1, a1, b1) = Beacons(40, life: true);
            var (g2, a2, b2) = Beacons(40, life: true);
            Assert.That(g1.DeepSpace.SeedRoute(a1.Name, a1.Origin, b1.Name, b1.Origin), Is.True);
            // The other way round, in the other galaxy: the same things in the same places.
            Assert.That(g2.DeepSpace.SeedRoute(b2.Name, b2.Origin, a2.Name, a2.Origin), Is.True);
            Assert.That(Describe(g2), Is.EqualTo(Describe(g1)));
            Assert.That(g1.DeepSpace.SeedRoute(b1.Name, b1.Origin, a1.Name, a1.Origin), Is.False, "seeded once");

            // Forty light-years no gate joins: wanderers, clouds and ice out there, and finds worth three times as much.
            var objects = g1.DeepSpace.Objects;
            Assert.That(objects.Count(o => o.Kind == DeepSpaceObjectKind.RoguePlanet), Is.GreaterThan(0));
            Assert.That(objects.Count(o => o.Kind == DeepSpaceObjectKind.IceBody), Is.GreaterThan(0));
            Assert.That(objects.All(o => o.OffNetwork), Is.True);
            var ice = objects.First(o => o.Kind == DeepSpaceObjectKind.IceBody);
            Assert.That(ice.SurveyCredits, Is.EqualTo(DeepSpaceFeatures.Of(DeepSpaceObjectKind.IceBody).SurveyCredits * DeepSpaceFeatures.OffNetworkSurveyFactor));
            // All of it between the two, not in either system.
            foreach (var o in objects)
                Assert.That(o.Position.KmFrom(a1.Origin).X / Astro.LightYearKm, Is.InRange(1, 39), o.Name);

            // A bare galaxy built by hand keeps open space empty unless it asks.
            var (g3, a3, b3) = Beacons(40);
            Assert.That(g3.DeepSpace.SeedRoute(a3.Name, a3.Origin, b3.Name, b3.Origin), Is.False);
            Assert.That(g3.DeepSpace.Objects, Is.Empty);
        }

        [Test]
        public void ANewKindOfFeaturePlugsInByRegisteringIt()
        {
            var old = DeepSpaceFeatures.Of(DeepSpaceObjectKind.IceBody);
            try
            {
                DeepSpaceFeatures.Register(new DeepSpaceFeatureSpec
                {
                    Kind = DeepSpaceObjectKind.IceBody, Noun = "comet swarm", OffNetworkPer10Ly = 5, MaxOffsetKm = 1e8, SurveyCredits = 1,
                    Make = (o, r) =>
                    {
                        o.Name = "a comet swarm";
                        o.IceTonnes = 1;
                    },
                });
                var (g, a, b) = Beacons(10, life: true);
                g.DeepSpace.SeedRoute(a.Name, a.Origin, b.Name, b.Origin);
                Assert.That(g.DeepSpace.Objects.Count(o => o.Name == "a comet swarm"), Is.EqualTo(5));
            }
            finally
            {
                DeepSpaceFeatures.Register(old);
            }
        }

        [Test]
        public void TheDefaultGalaxyHasRelaysAndListeningPostsBetweenItsSystemsAndTheyCarryTheNewsWhenTheGatesCannot()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            Assert.That(g.DeepSpace.Seeded(sol.Name, ac.Name), Is.True);
            var relay = g.DeepSpace.Objects.SingleOrDefault(o => o.Kind == DeepSpaceObjectKind.Relay &&
                new[] { o.RouteA, o.RouteB }.Contains(sol.Name) && new[] { o.RouteA, o.RouteB }.Contains(ac.Name));
            Assert.That(relay, Is.Not.Null, "no relay between Sol and Alpha Centauri");
            Assert.That(relay.Owner, Is.Not.Null);
            Assert.That(g.DeepSpace.Objects.Any(o => o.Kind == DeepSpaceObjectKind.ListeningPost && o.HomeName == sol.Name), Is.True,
                "Sol's navy has no listening post out on its approaches");
            // The gates' own lanes are charted: no rogue planet sits across them.
            Assert.That(g.DeepSpace.Objects.Where(o => o.Kind == DeepSpaceObjectKind.RoguePlanet).All(o => o.OffNetwork), Is.True);

            var news = g.Traffic.News;
            double gates = news.RelayDays(sol, ac, StartJd);
            // The relays are the slow way: the gates still carry the news while they can.
            Assert.That(gates, Is.LessThan(Newswire.InnerLightDays(sol) + g.DeepSpace.RelayLagDays(sol, ac) + Newswire.InnerLightDays(ac)));
            foreach (var gate in sol.Gates) gate.Integrity = 0;
            double relayed = news.RelayDays(sol, ac, StartJd);
            Assert.That(relayed, Is.EqualTo(Newswire.InnerLightDays(sol) + g.DeepSpace.RelayLagDays(sol, ac) + Newswire.InnerLightDays(ac)).Within(1e-9),
                "with every gate out of Sol down, the news still comes by the deep-space relay");
            news.Relay(StartJd + 1);
            news.Update(StartJd + 1 + relayed + 0.01);
            Assert.That(news.Known(ac, sol).Carrier, Is.EqualTo(NewsCarrier.DeepSpaceRelay));
        }

        // ---------------------------------------------------------------- what the features do

        [Test]
        public void ARoguePlanetsWellPullsAGroupOutOfFtl()
        {
            var (g, a, b) = Beacons(4);
            var navy = new Faction("Test Navy", "TNS", default) { IsMilitary = true };
            g.DeepSpace.Add(new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.RoguePlanet, Name = "the rogue planet Nomad 101", Position = Along(a, b, 2, 1e6), Gm = DeepSpaceFeatures.JupiterGm,
                SinceJd = double.NegativeInfinity,
            });
            var ship = Hull("TNS Steady", ShipClass.Cruiser, navy);
            var c = g.DeepSpace.Launch(new[] { ship }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            double plain = c.ArriveJd;
            Run(g, StartJd, plain + 3);
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("pulled out of FTL by the rogue planet Nomad 101")), Is.True, "it flew straight through the planet's well");
            var arrived = b.World.Fleet.Ships.SingleOrDefault(s => s.Name == ship.Name);
            Assert.That(arrived, Is.Not.Null, "it never got there");
            Assert.That(arrived.PhaseStartJd, Is.GreaterThan(plain), "crossing the well and spooling again cost it nothing");
        }

        static (Cruise east, Cruise west) HeadOnOffset(Galaxy g, StarSystem a, StarSystem b)
        {
            var navy = new Faction("Test Navy", "TNS", default) { IsMilitary = true };
            var clan = new Faction("Test Clan", "", default) { IsPirate = true };
            // Their lines pass some 400,000,000 km apart half way: in sight in open space.
            var east = g.DeepSpace.Launch(new[] { Hull("TNS Dauntless", ShipClass.Battleship, navy) },
                a, DeepSpace.DeparturePointKm(a, b.Origin) + new DVec3(0, 4e8, 0), b, CruiseDrives.Capital, StartJd);
            var west = g.DeepSpace.Launch(new[] { Hull("Black Gull", ShipClass.Destroyer, clan) },
                b, DeepSpace.DeparturePointKm(b, a.Origin) + new DVec3(0, -4e8, 0), a, CruiseDrives.Capital, StartJd);
            return (east, west);
        }

        [Test]
        public void InsideANebulaGroupsSlipPastEachOtherUnseen()
        {
            var (g, a, b) = Beacons(4);
            HeadOnOffset(g, a, b);
            Run(g, StartJd, StartJd + 6);
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("pulls")), Is.True, "in open space they should have seen each other");

            var (g2, a2, b2) = Beacons(4);
            g2.DeepSpace.Add(new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Nebula, Name = "the Grey Veil", Position = Along(a2, b2, 2), RadiusKm = 1.5 * Astro.LightYearKm, SinceJd = double.NegativeInfinity,
            });
            Assert.That(g2.DeepSpace.SensorRangeKm(Along(a2, b2, 2), 1e9), Is.EqualTo(1e9 * DeepSpace.NebulaSensorShare).Within(1e-6));
            HeadOnOffset(g2, a2, b2);
            Run(g2, StartJd, StartJd + 6);
            Assert.That(g2.DeepSpace.Events.Any(e => e.Text.Contains("pulls")), Is.False, "they saw each other through the nebula");
        }

        [Test]
        public void ASurveyHullChartsWhatItPassesAndIsPaidForTheFind()
        {
            var (g, a, b) = Beacons(4);
            var service = new Faction("Survey Service", "", default);
            var nebula = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Nebula, Name = "the Amber Veil", Position = Along(a, b, 2, 3e9), RadiusKm = 1e9, SurveyCredits = 1200000,
                OffNetwork = true, SinceJd = double.NegativeInfinity,
            };
            g.DeepSpace.Add(nebula);
            var ship = Hull("Far Look", ShipClass.Explorer, service);
            var c = g.DeepSpace.Launch(new[] { ship }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Survey, StartJd);
            double plain = c.ArriveJd;
            Run(g, StartJd, plain + 2);
            Assert.That(nebula.SurveyedBy, Is.SameAs(service));
            Assert.That(ship.Credits, Is.EqualTo(1200000).Within(1e-6));
            Assert.That(service.Treasury, Is.EqualTo(1200000).Within(1e-6));
            Assert.That(g.DeepSpace.SurveyFindsMade, Is.EqualTo(1));
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.StartsWith("Survey find") && e.Text.Contains("off the gate network")), Is.True);
            var arrived = b.World.Fleet.Ships.Single(s => s.Name == ship.Name);
            Assert.That(arrived.PhaseStartJd, Is.GreaterThan(plain + SurveyFinds.SurveyDays), "it stopped to survey it");

            // A plain star's first survey is a find; the second is not.
            Assert.That(g.DeepSpace.SurveyStar("Wolf 359"), Is.True);
            Assert.That(g.DeepSpace.SurveyStar("wolf 359"), Is.False);
        }

        [Test]
        public void AGroupShortOfReactionMassDropsOutToMineAnIceBody()
        {
            var (g, a, b) = Beacons(4);
            var traders = new Faction("Test Traders", "", default);
            var ice = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.IceBody, Name = "Iceball Tau-12", Position = Along(a, b, 2, 2e8), IceTonnes = 1e6, SinceJd = double.NegativeInfinity,
            };
            g.DeepSpace.Add(ice);
            var ship = Hull("Dry Well", ShipClass.Cruiser, traders);
            ship.Propellant = ship.PropellantCapacity * 0.3;
            g.DeepSpace.Launch(new[] { ship }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            Run(g, StartJd, StartJd + 9);
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("to mine") && e.Text.Contains(ice.Name)), Is.True, "it never stopped for the ice");
            Assert.That(ice.IceTonnes, Is.LessThan(1e6));
            Assert.That(ship.PropellantFraction, Is.GreaterThan(IceRefuelling.RefuelBelow), "its tanks were not filled");
            Assert.That(b.World.Fleet.Ships.Any(s => s.Name == ship.Name), Is.True, "it never went on");
        }

        // ---------------------------------------------------------------- distress calls

        static DeepSpaceObject Call(Galaxy g, StarSystem a, StarSystem b, Faction lure = null, double expires = double.PositiveInfinity)
        {
            var o = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.Distress, Name = "the adrift Mercy", Position = Along(a, b, 2, 3e8), Survivors = 30, SalvageCredits = 100000,
                SinceJd = StartJd - 1, ExpiresJd = expires, LureBy = lure,
            };
            g.DeepSpace.Add(o);
            return o;
        }

        [Test]
        public void APassingGroupAnswersADistressCallAndIsPaidForTheSurvivors()
        {
            var (g, a, b) = Beacons(4);
            var traders = new Faction("Test Traders", "", default);
            Call(g, a, b);
            var ship = Hull("Good Samaritan", ShipClass.Cruiser, traders);
            var c = g.DeepSpace.Launch(new[] { ship }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Capital, StartJd);
            double plain = c.ArriveJd;
            Run(g, StartJd, plain + 3);
            Assert.That(g.DeepSpace.Rescued, Is.EqualTo(30));
            Assert.That(ship.Credits, Is.EqualTo(30 * DistressCalls.BountyPerSoul).Within(1e-6));
            Assert.That(g.DeepSpace.Objects.Any(o => o.Kind == DeepSpaceObjectKind.Distress), Is.False);
            var arrived = b.World.Fleet.Ships.Single(s => s.Name == ship.Name);
            Assert.That(arrived.PhaseStartJd, Is.GreaterThan(plain + DistressCalls.RescueDays), "answering it cost it no time");
        }

        [Test]
        public void ALureRobsATraderWhoAnswersButAWarshipBeatsItOff()
        {
            var clan = new Faction("Test Clan", "", default) { IsPirate = true };
            var (g, a, b) = Beacons(4);
            Call(g, a, b, clan);
            var traders = new Faction("Test Traders", "", default);
            var trader = Hull("Trusting Soul", ShipClass.Explorer, traders);
            trader.Combat = null;
            trader.HasCargo = true;
            trader.CargoGood = (Good)0;
            trader.CargoTonnes = 30;
            trader.CargoCost = 80000;
            g.DeepSpace.Launch(new[] { trader }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Survey, StartJd);
            Run(g, StartJd, StartJd + 6);
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("lure") && e.Text.Contains("rob")), Is.True);
            Assert.That(trader.HasCargo, Is.False);
            Assert.That(clan.Treasury, Is.GreaterThanOrEqualTo(80000));
            Assert.That(trader.Destroyed, Is.False);

            var (g2, a2, b2) = Beacons(4);
            Call(g2, a2, b2, clan);
            var navy = new Faction("Test Navy", "TNS", default) { IsMilitary = true };
            var warship = Hull("TNS Vigilant", ShipClass.Battleship, navy);
            Assume.That(warship.Combat.Strength, Is.GreaterThan(DistressCalls.LureStrength));
            g2.DeepSpace.Launch(new[] { warship }, a2, DeepSpace.DeparturePointKm(a2, b2.Origin), b2, CruiseDrives.Capital, StartJd);
            Run(g2, StartJd, StartJd + 6);
            Assert.That(g2.DeepSpace.Events.Any(e => e.Text.Contains("beats the raiders off")), Is.True);
            Assert.That(warship.Credits, Is.GreaterThan(0));
        }

        [Test]
        public void ACallNobodyAnswersFallsSilentAndLeavesADerelict()
        {
            var (g, a, b) = Beacons(4);
            var o = Call(g, a, b, expires: StartJd + 0.5);
            // Someone is out (far off the call's way), so the deep space runs.
            var navy = new Faction("Test Navy", "TNS", default) { IsMilitary = true };
            g.DeepSpace.Launch(new[] { Hull("TNS Elsewhere", ShipClass.Cruiser, navy) }, b, DeepSpace.DeparturePointKm(b, a.Origin) + new DVec3(0, 0, 5e10), a,
                CruiseDrives.Capital, StartJd);
            Run(g, StartJd, StartJd + 1);
            Assert.That(o.Kind, Is.EqualTo(DeepSpaceObjectKind.Derelict));
            Assert.That(o.Survivors, Is.EqualTo(0));
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("falls silent")), Is.True);
        }

        // ---------------------------------------------------------------- outposts

        [Test]
        public void AListeningPostHearsAnEnemyPassAndAWarshipKnocksItOut()
        {
            var (g, a, b) = Beacons(4);
            var navy = new Faction("Test Navy", "TNS", default) { IsMilitary = true };
            var clan = new Faction("Test Clan", "", default) { IsPirate = true };
            var post = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.ListeningPost, Name = "the Test Navy listening post Kappa 3", Position = Along(a, b, 3, 1e10), Owner = navy,
                HomeName = b.Name, SinceJd = double.NegativeInfinity,
            };
            g.DeepSpace.Add(post);
            // An unarmed clan hull a light-hour or so off: heard, and told to the post's home.
            var sneak = Hull("Quiet Feather", ShipClass.Explorer, clan);
            sneak.Combat = null;
            g.DeepSpace.Launch(new[] { sneak }, a, DeepSpace.DeparturePointKm(a, b.Origin), b, CruiseDrives.Survey, StartJd);
            // (Only until it is heard: an unarmed test hull is not one to fly on into a system's fleet.)
            RunUntil(g, StartJd, 4, () => post.Heard != null && post.Heard.Contains(sneak), 0.05);
            Assert.That(post.Heard, Is.Not.Null.And.Contains(sneak));
            Assert.That(b.World.Events.Any(e => e.Text.Contains("hears Quiet Feather")), Is.True, "the post never told home");
            Assert.That(g.DeepSpace.Objects.Contains(post), Is.True, "an unarmed hull knocked it out");

            // A clan warship passing within a light-hour of it knocks it out.
            var (g2, a2, b2) = Beacons(4);
            var post2 = new DeepSpaceObject
            {
                Kind = DeepSpaceObjectKind.ListeningPost, Name = "the Test Navy listening post Kappa 4", Position = Along(a2, b2, 3, 2e8), Owner = navy,
                HomeName = b2.Name, SinceJd = double.NegativeInfinity,
            };
            g2.DeepSpace.Add(post2);
            g2.DeepSpace.Launch(new[] { Hull("Black Gull", ShipClass.Destroyer, clan) }, a2, DeepSpace.DeparturePointKm(a2, b2.Origin), b2, CruiseDrives.Capital, StartJd);
            Run(g2, StartJd, StartJd + 6);
            Assert.That(g2.DeepSpace.Objects.Contains(post2), Is.False);
            // Its wreck is left out there (or stripped by the raiders going on past it).
            Assert.That(g2.DeepSpace.Objects.Any(o => o.Kind == DeepSpaceObjectKind.Wreck && o.Name.Contains("Kappa 4"))
                        || g2.DeepSpace.Events.Any(e => e.Text.Contains("strip the wreck of the Test Navy listening post Kappa 4")), Is.True);
            Assert.That(b2.World.Events.Any(e => e.Text.Contains("knocks out")), Is.True);
        }

        // ---------------------------------------------------------------- ambushes on the routes

        [Test]
        public void ABandLyingInWaitPullsAPasserOutOfFtlAndRobsIt()
        {
            var (g, a, b) = Beacons(4);
            var clan = new Faction("Test Clan", "", default) { IsPirate = true };
            var traders = new Faction("Test Traders", "", default);
            var from = DeepSpace.DeparturePointKm(a, b.Origin);
            var station = GalacticPosition.Lerp(a.At(from), DeepSpace.ArrivalPoint(b.Origin, a.At(from), CruiseDrives.WellLimitKm(b)), 0.5);
            g.DeepSpace.ReachedStar += (c, jd) =>
            {
                g.DeepSpace.Retarget(c, b, jd + 12);
                g.DeepSpace.LieInWait(c, jd, 12, "on the test run");
            };
            var band = g.DeepSpace.LaunchToPoint(new[] { Hull("Black Gull", ShipClass.Destroyer, clan) }, b, DeepSpace.DeparturePointKm(b, station), station,
                "an ambush on the test run", CruiseDrives.Capital, StartJd);
            double jd0 = band.ArriveJd + 0.1;
            Run(g, StartJd, jd0);
            Assert.That(g.DeepSpace.Ambushes.Count, Is.EqualTo(1));
            Assert.That(g.DeepSpace.Projectors.Count, Is.EqualTo(1));
            Assert.That(g.DeepSpace.Projectors[0].Owner, Is.SameAs(clan));

            var trader = Hull("Fat Purse", ShipClass.Explorer, traders);
            trader.Combat = null;
            trader.HasCargo = true;
            trader.CargoGood = (Good)0;
            trader.CargoTonnes = 25;
            trader.CargoCost = 60000;
            g.DeepSpace.Launch(new[] { trader }, a, from, b, CruiseDrives.Survey, jd0);
            double robbed = RunUntil(g, jd0, 3, () => !trader.HasCargo, 0.05);
            Run(g, robbed, robbed + 0.4);
            Assert.That(g.DeepSpace.Events.Any(e => e.Text.Contains("Fat Purse") && e.Text.Contains("pulled out of FTL by the gravity projector")), Is.True,
                "the band's projector never pulled it out");
            Assert.That(trader.HasCargo, Is.False, "the band never robbed it");
            // The band went home with its take, and its projector went dark behind it.
            Assert.That(g.DeepSpace.Ambushes, Is.Empty);
            Assert.That(g.DeepSpace.Projectors, Is.Empty);
        }

        static double RunUntil(Galaxy g, double fromJd, double days, Func<bool> done, double step = 0.25)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done())
            {
                jd += step;
                g.Update(jd);
            }
            return jd;
        }

        [Test]
        public void ANavyPicketGoesOutUnderItsOwnDriveLiesInWaitAndComesHome()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            Ship Free() => sol.World.Fleet.Ships.FirstOrDefault(s => s.Spec.IsMilitary && InterstellarTraffic.CanCruise(s) && !s.Faction.IsPirate
                && !s.PatrolWing && InterstellarTraffic.Idle(sol.World, s));
            double jd = RunUntil(g, StartJd, 30, () => Free() != null);
            var ship = Free();
            Assume.That(ship, Is.Not.Null, "no warship came free to send");
            string name = ship.Name;
            var station = GalacticPosition.Lerp(sol.Origin, ac.Origin, 0.2);
            var v = g.Traffic.SendToAmbush(ship, sol, station, "on the approach from Alpha Centauri", jd);
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Purpose, Is.EqualTo(VoyagePurpose.Ambush));

            jd = RunUntil(g, jd, 60, () => g.DeepSpace.Ambushes.Any(x => x.Band.Leader.Name == name));
            var ambush = g.DeepSpace.Ambushes.FirstOrDefault(x => x.Band.Leader.Name == name);
            Assert.That(ambush, Is.Not.Null, "it never took up its station");
            Assert.That(ambush.Projector.Position.DistanceKm(station), Is.LessThan(1e3), "it lies in wait where it was sent");
            Assert.That(ambush.Projector.Owner, Is.SameAs(ship.Faction));

            Ship Home() => sol.World.Fleet.Ships.FirstOrDefault(x => x.Name == name && !x.Destroyed);
            jd = RunUntil(g, jd, 80, () => Home() != null);
            Assert.That(Home(), Is.Not.Null, "it never came home");
            Assert.That(g.DeepSpace.Ambushes.Any(x => x.Band.Leader.Name == name), Is.False);
            Assert.That(sol.World.Events.Any(e => e.Text.Contains(name) && e.Text.Contains("lying in wait")), Is.True);
        }

        [Test]
        public void WhatLiesOutThereSavesAndCarriesOn()
        {
            var g = Galaxy.CreateDefault(StartJd);
            int n = g.DeepSpace.Objects.Count;
            Assert.That(n, Is.GreaterThan(0));
            var copy = ObjectGraph.Clone(g);
            Assert.That(copy.DeepSpace.Objects.Count, Is.EqualTo(n));
            Assert.That(copy.DeepSpace.RouteLife, Is.True);
            Assert.That(copy.DeepSpace.Routes.Count, Is.EqualTo(g.DeepSpace.Routes.Count));
            var sol = copy.Find("Sol System");
            var ac = copy.Find("Alpha Centauri");
            Assert.That(copy.DeepSpace.SeedRoute(sol.Name, sol.Origin, ac.Name, ac.Origin), Is.False, "the loaded galaxy seeded a route twice");
        }
    }
}
