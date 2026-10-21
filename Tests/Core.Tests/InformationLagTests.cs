using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T100: information lag. What anyone knows of another place is as old as the news that brought it: inside a
    /// system a scanner sees a ship where it was when the light left it, and a sighting crosses to a fleet elsewhere at
    /// light speed (minutes an AU), so a navy far off acts on an old look; between systems, markets and news come only by
    /// the gate relays and the ships that cross, stale by the trip, and traders plan their runs on that, not on the truth.
    /// </summary>
    [TestFixture]
    public class InformationLagTests
    {
        const double StartJd = 2461324.0;
        const double Minute = 1.0 / 1440;

        /// <summary>Forty AU straight up out of the ecliptic from Earth: nobody else is anywhere near.</summary>
        static readonly DVec3 Deep = new DVec3(0, 0, 40 * Astro.AuKm);

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Ship Park(World w, string name, ShipClass c, Faction f, int body, DVec3 offset)
        {
            var s = w.Fleet.AddShip(name, c, f, ShipRole.Patrol);
            s.Combat = new ShipCombat(CombatSpec.Of(c), c);
            s.Orbit = new ParkingOrbit { Body = body, Center = offset };
            s.Phase = ShipPhase.Parked;
            s.PhaseStartJd = StartJd;
            return s;
        }

        static void ClearShips(World w)
        {
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            w.War.Battles.Clear();
        }

        // ---------------------------------------------------------------- light inside a system

        [Test]
        public void LightTakesAboutEightMinutesAnAu()
        {
            Assert.That(LightSpeed.DelaySeconds(Astro.AuKm), Is.EqualTo(499.004784).Within(1e-5));
            Assert.That(LightSpeed.DelaySeconds(LightSpeed.SpeedKmS * 3), Is.EqualTo(3).Within(1e-12));
            Assert.That(LightSpeed.DelayDays(Astro.AuKm * 5.2) * 1440, Is.EqualTo(43.25).Within(0.01), "Jupiter at opposition is three quarters of an hour away");
            Assert.That(LightSpeed.Describe(LightSpeed.DelayDays(Astro.AuKm)), Is.EqualTo("8.3 min"));
        }

        [Test]
        public void AContactIsSeenWhereItWasWhenItsLightLeftByTheLightTimeAcrossTheGap()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = w.Catalog["Earth"].Index;
            Park(w, "Lookout", ShipClass.Destroyer, w.Sol, earth, Deep);
            foreach (double km in new[] { 1e5, 3e5 })
            {
                var raider = Park(w, "Shade " + km, ShipClass.Destroyer, w.Pirates, earth, Deep + new DVec3(km, 0, 0));
                w.Sensors.Sweep(StartJd);
                var c = w.Sensors.PictureOf(w.Sol).Of(raider);
                Assert.That(c, Is.Not.Null, "in reach");
                var then = w.Fleet.StateOf(raider, c.SeenJd).HelioKm;
                var now = w.Fleet.StateOf(raider, StartJd).HelioKm;
                double gap = (c.Sightings.Last().ObserverHelioKm - now).Length;
                Assert.That(gap, Is.EqualTo(km).Within(km * 1e-3));
                Assert.That(c.LagSeconds, Is.EqualTo(gap / LightSpeed.SpeedKmS).Within(1e-4), "the light delay is the distance over c");
                Assert.That(c.SeenJd, Is.EqualTo(StartJd - gap / LightSpeed.SpeedKmS / Astro.SecondsPerDay).Within(1e-9));
                // The track holds it where it was when that light left it (its schedule at that moment), not where it is now.
                Assert.That((c.LastHelioKm - then).Length, Is.LessThan(1e-3));
                raider.Destroyed = true;
                w.Sensors.Sweep(StartJd + 1);
            }
        }

        [Test]
        public void ANavyFarOffActsOnALookAsOldAsTheLightTimeAndCanBeWrong()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = w.Catalog["Earth"].Index, mars = w.Catalog["Mars"].Index, jupiter = w.Catalog["Jupiter"].Index;
            Park(w, "Earth Picket", ShipClass.Destroyer, w.Sol, earth, new DVec3(60000, 0, 0));
            Park(w, "Mars Picket", ShipClass.Destroyer, w.Sol, mars, new DVec3(60000, 0, 0));
            var raider = Park(w, "Shade", ShipClass.Destroyer, w.Pirates, earth, new DVec3(30000, 0, 0));
            var sensors = w.Sensors;
            var war = w.War;
            var atJupiter = w.Ephemeris.HelioAt(jupiter, StartJd);
            double earthLag = LightSpeed.DelayDays(w.Ephemeris.HelioAt(earth, StartJd), atJupiter);
            double marsLag = LightSpeed.DelayDays(w.Ephemeris.HelioAt(mars, StartJd), atJupiter);
            Assert.That(earthLag * 1440, Is.GreaterThan(30), "Jupiter is over half an hour of light from Earth");

            sensors.Sweep(StartJd);
            Assert.That(war.KnowsOfRaidersAt(earth, w.Sol, w.Ephemeris.HelioAt(earth, StartJd), StartJd + 1.0 / Astro.SecondsPerDay), Is.True,
                "the picket at Earth knows within a second");
            Assert.That(war.KnowsOfRaidersAt(earth, w.Sol, atJupiter, StartJd + Minute), Is.False, "word has not crossed to Jupiter yet");
            Assert.That(war.KnowsOfRaidersAt(earth, w.Sol, atJupiter, StartJd + earthLag + Minute), Is.True, "it has, a light time later");

            // The raider slips off to Mars. Until word of that crosses, a group at Jupiter still thinks it is at Earth.
            raider.Orbit = new ParkingOrbit { Body = mars, Center = new DVec3(30000, 0, 0) };
            double jd2 = StartJd + sensors.SweepDays;
            sensors.Sweep(jd2);
            Assert.That(war.KnowsOfRaidersAt(earth, w.Sol, atJupiter, jd2 + Minute), Is.True, "acting on the old look: wrong, the raider has gone");
            Assert.That(war.KnowsOfRaidersAt(mars, w.Sol, atJupiter, jd2 + Minute), Is.False);
            Assert.That(war.KnowsOfRaidersAt(earth, w.Sol, atJupiter, jd2 + marsLag + Minute), Is.False, "the new look has arrived");
            Assert.That(war.KnowsOfRaidersAt(mars, w.Sol, atJupiter, jd2 + marsLag + Minute), Is.True);
        }

        // ---------------------------------------------------------------- news between the systems

        [Test]
        public void ASystemKnowsTheOthersOnlyAsTheRelayLeftThemAsOldAsTheTripThrough()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var news = g.Traffic.News;
            double relay = news.RelayDays(sol, ac, StartJd);
            var route = g.Network.Route(sol, ac);
            Assert.That(relay, Is.EqualTo(GateNetwork.RouteDays(route) + Newswire.InnerLightDays(sol) + Newswire.InnerLightDays(ac)).Within(1e-9));
            Assert.That(relay, Is.GreaterThan(GateNetwork.RouteDays(route)), "light out to the gate and in from the far one adds to the crossing");
            Assert.That(Newswire.InnerLightDays(sol) * 1440, Is.InRange(1, 30), "Sol's nearest market is minutes of light from the gates");
            Assert.That(Newswire.InnerLightDays(ac) * 1440, Is.InRange(60, 120), "Rigil Gate is over an hour of light out");

            var known = news.Known(ac, sol);
            Assert.That(known, Is.Not.Null);
            Assert.That(known.AgeDays(StartJd), Is.EqualTo(relay).Within(1e-9), "what Centauri knows of Sol is as old as the relay");
            Assert.That(news.Known(sol, sol), Is.Null, "a system's own markets are not news");

            // A relay sent now is on its way for the trip, and is what Centauri knows only when it lands.
            news.Relay(StartJd + 1);
            Assert.That(news.InFlight, Is.GreaterThan(0));
            news.Update(StartJd + 1 + relay - Minute);
            Assert.That(news.Known(ac, sol).AsOfJd, Is.LessThan(StartJd + 1));
            news.Update(StartJd + 1 + relay + Minute);
            Assert.That(news.Known(ac, sol).AsOfJd, Is.EqualTo(StartJd + 1));
            Assert.That(news.Known(ac, sol).Carrier, Is.EqualTo(NewsCarrier.Relay));

            // A blockaded gate passes nothing: the news goes the long way round, or not at all.
            var gate = sol.GateTo(ac);
            g.Traffic.Blockade(gate, StartJd + 2, 30, "test");
            Assert.That(news.RelayDays(sol, ac, StartJd + 2), Is.GreaterThan(relay));
        }

        [Test]
        public void AShipThroughTheGatesCarriesTheNewsAndItsHeadlines()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var news = g.Traffic.News;
            double jd = StartJd + 0.3;
            sol.World.Log(jd, "The Federation and the Compact sign a ceasefire", WorldEventKind.Diplomacy);
            var bundle = news.Bundle(sol, jd);
            Assert.That(bundle.Any(r => r.Source == sol && r.AsOfJd == jd), Is.True, "its own system's boards as it went in");

            news.Carried(ac, bundle, "Swift Courier", jd + 0.6);
            news.Update(jd + 0.6 + Newswire.InnerLightDays(ac) + Minute);
            var known = news.Known(ac, sol);
            Assert.That(known.Carrier, Is.EqualTo(NewsCarrier.Ship));
            Assert.That(known.CarriedBy, Is.EqualTo("Swift Courier"));
            Assert.That(known.AsOfJd, Is.EqualTo(jd));
            var headline = ac.World.Events.Last(e => e.Text.StartsWith(SystemReport.ForeignPrefix));
            Assert.That(headline.Text, Does.Contain("ceasefire"), "the newest headline is logged last");
            Assert.That(headline.Text, Does.Contain("Sol System").And.Contain("ceasefire").And.Contain("Swift Courier"));
            Assert.That(headline.Jd, Is.GreaterThan(jd), "dated as it arrives, not as it happened");

            // Older news than what is known is not taken: what is known stays the newest.
            var stale = news.Bundle(sol, jd - 1);
            news.Carried(ac, stale, "Slow Barge", jd + 2);
            news.Update(jd + 3);
            Assert.That(news.Known(ac, sol).AsOfJd, Is.GreaterThanOrEqualTo(jd));
        }

        [Test]
        public void ATraderPlansOnTheStaleBoardsAndIsWrongWhenTheFarMarketHasMoved()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var news = g.Traffic.News;
            var earth = sol.World.Economy.Settlements.First(s => s.Name == "Earth Orbital");
            var ship = sol.World.Fleet.Ships.First(s => s.Spec.Class == ShipClass.Freighter && !s.Destroyed);
            var route = g.Network.Route(sol, ac, ship.Faction, StartJd);

            // Centauri starves, and the relay carries word of it to Sol.
            foreach (var s in ac.World.Economy.Settlements) Array.Clear(s.Stock, 0, s.Stock.Length);
            double relay = news.RelayDays(ac, sol, StartJd);
            news.Relay(StartJd);
            news.Update(StartJd + relay + Minute);
            news.NextRelayJd = double.PositiveInfinity;
            var report = news.Known(sol, ac);
            Assert.That(report.AsOfJd, Is.EqualTo(StartJd));

            // Then Centauri's warehouses fill. Sol does not know.
            foreach (var s in ac.World.Economy.Settlements)
                for (int i = 0; i < GoodInfo.Count; i++) s.Stock[i] = s.Capacity((Good)i);
            for (int i = 0; i < GoodInfo.Count; i++) earth.Stock[i] = earth.Capacity((Good)i);

            Assert.That(InterstellarTraffic.BestCargo(sol, ac, ship, earth.Body, route, StartJd + 1, out _, out _, out _, out _), Is.False,
                "on the truth, nothing is worth the trip");
            Assert.That(InterstellarTraffic.BestCargo(sol, ac, ship, earth.Body, route, StartJd + 1, report.Markets,
                out var src, out var good, out double tonnes, out double profit, out var market, out double sale), Is.True,
                "on the news, Centauri is starving and pays");
            Assert.That(profit, Is.GreaterThan(0));
            var live = ac.World.Economy.Settlements.First(s => s.Name == market.Name);
            Assert.That(market, Is.Not.SameAs(live), "planned on the board as it was, not the market");
            double truth = live.SaleValue(good, tonnes) * (1 - Economy.PortTax);
            Assert.That(sale, Is.GreaterThan(truth * 1.5), "the news promises far more than the market will now pay");

            // The traffic sends freighters out on that news.
            Voyage sent = null;
            double jd = StartJd + relay + Minute;
            while (sent == null && jd < StartJd + 6)
            {
                // An idle freighter waiting at Earth, which is glutted.
                ship.Voyage = null;
                ship.GateKm = null;
                ship.HasCargo = false;
                ship.Role = ShipRole.Trader;
                ship.Orbit = new ParkingOrbit { Body = earth.Body, Center = new DVec3(20000, 0, 0), EpochJd = jd };
                ship.Phase = ShipPhase.Parked;
                ship.PhaseStartJd = jd;
                ship.PhaseEndJd = jd + 30;
                for (int i = 0; i < GoodInfo.Count; i++) earth.Stock[i] = earth.Capacity((Good)i);
                jd += 0.25;
                g.Update(jd);
                foreach (var s in ac.World.Economy.Settlements)
                    for (int i = 0; i < GoodInfo.Count; i++) s.Stock[i] = s.Capacity((Good)i);
                sent = sol.World.Fleet.Ships.Select(s => s.Voyage).FirstOrDefault(v => v != null && v.Purpose == VoyagePurpose.Trade && v.Destination == ac);
            }
            Assert.That(sent, Is.Not.Null, "a freighter set out for Centauri on the stale news");
            Assert.That(sent.PlannedOnNewsJd, Is.EqualTo(StartJd), "on the board the relay brought");
            Assert.That(sent.ExpectedSale, Is.GreaterThan(0));
        }
    }
}
