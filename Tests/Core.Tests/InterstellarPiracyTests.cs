using System;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// Covers U.SPC-T94: pirate clans across the gates. Raider bands lie in wait off a system's busiest jump gate and fall
    /// on the traffic bunched there; loot comes home to the clan's haven and is smuggled through the gates to the black
    /// market of a lawless station, which pays its own prices and passes the goods on; a rich clan founds a haven in a
    /// frontier disc and moves a band there, and a haven abroad can break away as a clan of its own; and the states a
    /// clan robs send navies and bounty hunters after it through the gates.
    /// </summary>
    [TestFixture]
    public class InterstellarPiracyTests
    {
        const double StartJd = 2461324.0;

        /// <summary>Runs the whole galaxy forward in half-day steps until <paramref name="done"/> or <paramref name="days"/> have gone by; returns the date reached.</summary>
        static double RunUntil(Galaxy g, double fromJd, double days, Func<double, bool> done)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done(jd))
            {
                jd += 0.5;
                g.Update(jd);
            }
            return jd;
        }

        static Ship Band(World w, Faction clan) =>
            w.Fleet.Ships.First(s => s.Faction == clan && !s.IsEscort && !s.Destroyed && !s.Runner && s.Spec.IsMilitary);

        static Settlement Named(World w, string name) => w.Economy.Settlements.Single(s => s.Name == name);

        [Test]
        public void RaidersLyingOffAGateFallOnTheTrafficBunchedThereAndNobodyElse()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var w = sol.World;
            var gate = sol.GateTo(ac);
            int root = sol.Catalog.Root.Index;
            // A band fit to fight: in port, whole, not pulling back to refit.
            Ship band = null;
            double jd = RunUntil(g, StartJd, 40, _ => (band = w.Fleet.Ships.FirstOrDefault(s => s.Faction == w.Pirates && !s.IsEscort && !s.Destroyed && !s.Runner &&
                s.Phase == ShipPhase.Parked && s.Battle == null && s.Combat.Damage < 0.05 && w.War.OrderOf(s) is GroupOrder o && o.Kind != OrderKind.Repair)) != null);
            Assert.That(band, Is.Not.Null, "no raider band fit to send");

            // The band out on the Centauri Gate's approach, lying in wait.
            w.War.StakeOut(band, gate, jd + 60);
            Assert.That((band.StakeoutKm.Value - gate.ApproachKm).Length, Is.LessThan(500));
            Fleet.PinAt(band, root, band.StakeoutKm.Value, -gate.PositionKm.Normalized, jd);

            // A yacht held off another gate: a fight at this one is none of its business.
            var other = sol.Gates.First(x => x != gate);
            var bystander = w.Fleet.Ships.First(s => s.Role == ShipRole.Free && !s.Destroyed && s.Faction == w.Independents && s.Battle == null);
            Fleet.PinAt(bystander, root, other.ApproachKm, DVec3.UnitX, jd);
            bystander.PhaseEndJd = jd + 200;

            // A courier for Alpha Centauri, by the Centauri Gate.
            Ship courier = null;
            jd = RunUntil(g, jd, 20, _ => (courier = w.Fleet.Ships.FirstOrDefault(s => s.Role == ShipRole.Courier && !s.Destroyed &&
                s.Faction == w.Independents && s.Phase == ShipPhase.Parked && s.Battle == null && s.Voyage == null && !s.OutOfPropellant)) != null);
            Assert.That(courier, Is.Not.Null, "no courier free to send");
            var v = g.Traffic.Send(courier, sol, ac, VoyagePurpose.Courier, jd);
            Assert.That(v, Is.Not.Null);
            Assert.That(v.Route.First(), Is.SameAs(gate));

            Battle Fight() => w.War.Battles.Concat(w.War.Ended).FirstOrDefault(b => b.Body == root && b.Units.Any(u => u.Ship == courier));
            jd = RunUntil(g, jd, 40, _ => Fight() != null || courier.Destroyed);
            var battle = Fight();
            Assert.That(battle, Is.Not.Null, "the raiders never fell on the courier at the gate");
            Assert.That(battle.Name, Does.Contain("Centauri Gate"));
            Assert.That(battle.Units.Any(u => u.Ship == band), Is.True, "the band lying in wait fought it");
            Assert.That(battle.Units.Any(u => u.Ship == bystander), Is.False, "a ship at another gate was swept into the fight");
            Assert.That(bystander.Battle, Is.Null);
            Assert.That(gate.Ambushes, Is.GreaterThanOrEqualTo(1));
            Assert.That(w.Events.Any(e => e.Text.Contains("lying off the Centauri Gate")), Is.True);
        }

        [Test]
        public void RaiderBandsGoToLieInWaitOffTheBusiestGate()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var w = sol.World;
            var busy = sol.GateTo(g.Find("Alpha Centauri"));
            foreach (var gate in sol.Gates) gate.Traffic = 0;
            Assert.That(w.War.BusiestGate(w.Pirates, StartJd), Is.Null, "no gate has seen traffic enough to watch");
            busy.Traffic = 60;
            Assert.That(w.War.BusiestGate(w.Pirates, StartJd), Is.SameAs(busy));

            int root = sol.Catalog.Root.Index;
            Ship watcher = null;
            RunUntil(g, StartJd, 150, jd => (watcher = w.Fleet.Ships.FirstOrDefault(s => !s.Destroyed && !s.IsEscort && s.Faction.IsPirate && s.StakeoutKm != null &&
                Fleet.IsAt(s, root) && sol.Gates.Any(x => (s.Orbit.OffsetAt(jd) - x.ApproachKm).Length < War.GateAmbushRangeKm))) != null);
            Assert.That(watcher, Is.Not.Null, "no raider band went to lie off a gate");
            Assert.That(w.War.OrderOf(watcher)?.Kind ?? OrderKind.GateWatch, Is.EqualTo(OrderKind.GateWatch));
        }

        [Test]
        public void LootComesHomeToTheClanHavenAndIsSmuggledToABlackMarketAbroad()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var barnard = g.Find("Barnard's Star");
            var w = barnard.World;
            var drift = w.Pirates;
            var slipstream = Named(w, "Slipstream");
            Assert.That(slipstream.Owner, Is.SameAs(drift));
            drift.Treasury = 1e9;
            drift.Manpower = Math.Max(drift.Manpower, 1e4);

            // A band home from a raid abroad with electronics in its holds.
            var band = Band(w, drift);
            band.Plunder = new double[GoodInfo.Count];
            band.Plunder[(int)Good.Electronics] = 2500;
            double before = slipstream.Stock[(int)Good.Electronics];
            Ship Runner() => g.Systems.SelectMany(s => s.World.Fleet.Ships).Concat(g.Traffic.InTransit())
                .FirstOrDefault(s => s.Runner && !s.Destroyed && s.Faction == drift && s.Voyage?.Purpose == VoyagePurpose.Smuggle);
            double jd = RunUntil(g, StartJd, 80, _ => band.Destroyed || band.Plunder == null);
            Assert.That(band.Plunder, Is.Null, "the band never put its plunder in the haven's stores");
            Assert.That(slipstream.Stock[(int)Good.Electronics] > before + 1000 || Runner() != null, Is.True, "the loot went neither into the haven nor out with a smuggler");

            // The haven's loot goes out with a smuggler, through the gates, to a lawless station's black market.
            jd = RunUntil(g, jd, 20, _ => Runner() != null);
            var runner = Runner();
            Assert.That(runner, Is.Not.Null, "no smuggler set out from Slipstream");
            Assert.That(runner.Plunder[(int)Good.Electronics], Is.GreaterThan(0));
            var voyage = runner.Voyage;
            var market = voyage.Destination.World.Economy.At(voyage.TargetBody).Single(s => s.Lawless);
            double price = BlackMarket.Price(market, Good.Electronics);
            Assert.That(price, Is.GreaterThan(GoodInfo.Of(Good.Electronics).BasePrice * BlackMarket.HavenShare), "the black market pays no more than the haven's fences");
            int sales = g.Traffic.Clans.Sales;

            RunUntil(g, jd, 120, _ => g.Traffic.Clans.Sales > sales && market.BlackStock[(int)Good.Electronics] > 0);
            Assert.That(market.BlackStock[(int)Good.Electronics], Is.GreaterThan(0), $"the smuggler never sold at {market.Name}");
            Assert.That(BlackMarket.Price(market, Good.Electronics), Is.LessThan(price), "the market's price did not fall as it filled");
            Assert.That(voyage.Destination.World.Events.Any(e => e.Text.Contains("on the black market at " + market.Name)), Is.True);
        }

        [Test]
        public void AClanWaitsForItsSmugglerHomeFromTheGatesRatherThanBuyingAnotherHull()
        {
            // U.SPC-T304: a smuggler back through the gates is home (off its voyage) the moment it comes out, but still has to
            // cross from the gate to the haven; the clan used to buy a fresh hull for the next run in the meantime.
            var g = Galaxy.CreateDefault(StartJd);
            var barnard = g.Find("Barnard's Star");
            var w = barnard.World;
            var drift = w.Pirates;
            var slipstream = Named(w, "Slipstream");
            drift.Treasury = 1e9;
            drift.Manpower = Math.Max(drift.Manpower, 1e5);
            var (to, market) = g.Systems.Where(s => s != barnard)
                .SelectMany(s => s.World.Economy.Settlements.Where(st => st.Lawless && !st.Fallen).Select(st => (s, st)))
                .First(x => g.Network.Route(barnard, x.s, drift, StartJd)?.Count > 0);
            slipstream.Stock[(int)Good.Electronics] = 50000;
            int Runners() => w.Fleet.Ships.Count(s => s.Runner && !s.Destroyed && s.Faction == drift);

            var runner = g.Traffic.Clans.SendRunner(barnard, slipstream, to, market, null, StartJd);
            Assert.That(runner, Is.Not.Null, "the clan sent no smuggler");
            Assert.That(Runners(), Is.EqualTo(1));

            // Home again, out of the gate and parked beside it, not yet in at the haven.
            double jd = StartJd + 1;
            runner.Voyage = null;
            runner.GateKm = null;
            runner.Plunder = null;
            runner.Route = new[] { slipstream.Body };
            runner.Ports = new[] { slipstream.Body };
            int gateSide = barnard.Catalog.Root.Index;
            Assert.That(gateSide, Is.Not.EqualTo(slipstream.Body));
            w.Fleet.Station(runner, gateSide, jd);
            double treasury = drift.Treasury;
            Assert.That(g.Traffic.Clans.SendRunner(barnard, slipstream, to, market, null, jd), Is.Null, "a new hull went out while the last was still coming in");
            Assert.That(Runners(), Is.EqualTo(1), "the clan bought a second smuggler");
            Assert.That(drift.Treasury, Is.EqualTo(treasury), "the clan paid for a hull it did not need");

            // In at the haven, the same hull takes the next run.
            w.Fleet.Station(runner, slipstream.Body, jd);
            Assert.That(g.Traffic.Clans.SendRunner(barnard, slipstream, to, market, null, jd), Is.SameAs(runner));
            Assert.That(Runners(), Is.EqualTo(1));
        }

        [Test]
        public void ABlackMarketPaysItsOwnPricesAndPassesStolenGoodsOn()
        {
            var tauCeti = StarSystem.CreateSettled(NearbySystems.TauCeti(), StartJd);
            var w = tauCeti.World;
            var comet = Named(w, "Cometreach");
            var hub = Named(w, "Cetus Station");
            Assert.That(comet.Lawless, Is.True);
            Assert.That(BlackMarket.Takes(comet), Is.True);
            Assert.That(BlackMarket.Takes(hub), Is.False, "the Federation's hub keeps no black market");
            // An empty hide in the debris disc, waiting for a clan.
            Assert.That(w.HavenSites.Select(b => tauCeti.Catalog[b].Name), Is.EqualTo(new[] { "Driftglass" }));
            Assert.That(tauCeti.Discs, Is.Not.Empty);

            var g = Good.Electronics;
            double empty = BlackMarket.Price(comet, g);
            Assert.That(empty, Is.GreaterThan(GoodInfo.Of(g).BasePrice * BlackMarket.StationShare), "an empty black market pays over its usual share");
            double owner = comet.Owner.Treasury;
            double paid = BlackMarket.Sell(comet, g, 2000);
            Assert.That(paid, Is.GreaterThan(0));
            Assert.That(comet.Owner.Treasury, Is.EqualTo(owner - paid).Within(1));
            Assert.That(comet.BlackStock[(int)g], Is.EqualTo(2000));
            Assert.That(BlackMarket.Price(comet, g), Is.LessThan(empty), "a full black market pays less");

            // Its middlemen pass a little of it a day into the station's own stores.
            w.Update(StartJd + 10);
            double left = comet.BlackStock[(int)g];
            Assert.That(left, Is.InRange(2000 * Math.Exp(-0.04 * 10.5), 2000 * Math.Exp(-0.04 * 9.5)));
        }

        [Test]
        public void ARichClanFoundsAHavenInAFrontierDiscMovesABandThereAndItCanBreakAway()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var banner = sol.World.Pirates;
            banner.Treasury = 5e8;
            Ship band = null;
            double jd = StartJd;
            for (; jd < StartJd + 30 && band == null; jd += 0.5)
            {
                g.Update(jd);
                band = g.Traffic.Clans.Expand(banner, jd);
            }
            Assert.That(band, Is.Not.Null, "no band set out to found a haven");
            var v = band.Voyage;
            Assert.That(v.Purpose, Is.EqualTo(VoyagePurpose.Relocate));
            var target = v.Destination;
            Assert.That(target, Is.Not.SameAs(sol));
            Assert.That(target.Discs, Is.Not.Empty, "a haven abroad is founded in a belt or a debris disc");
            Assert.That(target.World.HavenSites, Does.Contain(v.TargetBody));
            Assert.That(banner.Treasury, Is.LessThan(5e8));
            bool hadClan = target.World.Economy.Settlements.Any(s => s.Kind == SettlementKind.Haven && !s.Fallen && s.Owner == target.World.Pirates);

            Settlement Founded() => target.World.Economy.Settlements.FirstOrDefault(s => s.Kind == SettlementKind.Haven && s.Owner == banner);
            jd = RunUntil(g, jd, 150, _ => Founded() != null);
            var haven = Founded();
            Assert.That(haven, Is.Not.Null, $"the band never founded its haven at {target.Name}");
            Assert.That(haven.Body, Is.EqualTo(v.TargetBody));
            Assert.That(haven.Hidden, Is.True);
            Assert.That(haven.Platforms, Is.Not.Empty);
            Assert.That(target.World.HavenSites, Does.Not.Contain(haven.Body));
            var settled = target.World.Fleet.Ships.Single(s => s.Name == band.Name && !s.Destroyed);
            Assert.That(settled.Voyage, Is.Null, "the band stays: it belongs to the new haven now");
            Assert.That(settled.Route[0], Is.EqualTo(haven.Body));
            Assert.That(target.World.War.HomeHaven(settled), Is.SameAs(haven));
            // One clan, two systems.
            var held = g.Polities.SystemsWithHoldings(banner);
            Assert.That(held, Does.Contain(sol.World));
            Assert.That(held, Does.Contain(target.World));
            if (!hadClan) Assert.That(target.World.Pirates, Is.SameAs(banner), "the newcomers are the system's raiders now");
            Assert.That(g.Traffic.Clans.Foundings, Is.EqualTo(1));

            // The hold abroad breaks away: a clan of its own, with the band based there.
            var breakaway = g.Traffic.Clans.Split(haven, jd);
            Assert.That(breakaway, Is.Not.Null);
            Assert.That(breakaway.IsPirate, Is.True);
            Assert.That(breakaway.Kind, Is.EqualTo(PolityKind.PirateClan));
            Assert.That(g.Polities.Find(breakaway.Name), Is.SameAs(breakaway));
            Assert.That(haven.Owner, Is.SameAs(breakaway));
            Assert.That(settled.Faction, Is.SameAs(breakaway));
            Assert.That(target.World.Fleet.Ships.Where(s => s.Leader == settled && !s.Destroyed).All(s => s.Faction == breakaway), Is.True, "its escorts went with it");
            Assert.That(sol.World.Economy.Settlements.Where(s => s.Kind == SettlementKind.Haven).All(s => s.Owner == banner), Is.True, "the clan keeps its home havens");
            Assert.That(g.Polities.Diplomacy.Hostile(breakaway, banner), Is.True);
            if (!hadClan) Assert.That(target.World.Pirates, Is.SameAs(breakaway));
            Assert.That(breakaway.Treasury, Is.GreaterThan(0));
        }

        [Test]
        public void ExpandNeverSendsASecondBandWhileTheFirstRidesInsideAGate()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var banner = sol.World.Pirates;
            banner.Treasury = 5e8;
            // Extra bands fit to sail, besides the clan's own: were the first missed while it rides inside a
            // gate (retired out of every system's fleet there, see InterstellarTraffic.InTransit), Expand would
            // wrongly send one of these too.
            int deimos = sol.Catalog["Deimos"].Index;
            sol.World.War.RaisePirateGroup(StartJd, deimos);
            sol.World.War.RaisePirateGroup(StartJd, deimos);

            Ship band = null;
            double jd = StartJd;
            for (; jd < StartJd + 30 && band == null; jd += 0.5)
            {
                g.Update(jd);
                band = g.Traffic.Clans.Expand(banner, jd);
            }
            Assert.That(band, Is.Not.Null, "no band set out to found a haven");
            int expansions = g.Traffic.Clans.Expansions;

            bool InsideGate() => g.Traffic.InTransit().Any(s => s.Faction == banner && s.Voyage?.Purpose == VoyagePurpose.Relocate);
            jd = RunUntil(g, jd, 150, _ => InsideGate());
            Assert.That(InsideGate(), Is.True, "the band never rode inside a gate");

            double treasuryBefore = banner.Treasury;
            var second = g.Traffic.Clans.Expand(banner, jd);
            Assert.That(second, Is.Null, "Expand sent a second band while the first was still inside a gate");
            Assert.That(g.Traffic.Clans.Expansions, Is.EqualTo(expansions), "Expand founded a second time while one band was inside a gate");
            Assert.That(banner.Treasury, Is.EqualTo(treasuryBefore), "Expand charged the founding cost again");
        }

        [Test]
        public void ExpandWontFoundWithoutTheCostInTreasuryEvenWithABandAndRouteReady()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var banner = sol.World.Pirates;

            Ship band = null;
            double jd = StartJd;
            for (; jd < StartJd + 30 && band == null; jd += 0.5)
            {
                g.Update(jd);
                // Pinned short of the founding cost against whatever the day's smuggling and racketeering
                // brought in (the clan's takings otherwise creep the treasury back over the line on their own).
                banner.Treasury = PirateClans.FoundingCost - 1;
                band = g.Traffic.Clans.Expand(banner, jd);
                Assert.That(band, Is.Null, "Expand founded a haven without the cost in the treasury to pay for it");
            }
            Assert.That(g.Traffic.Clans.Expansions, Is.EqualTo(0));
            Assert.That(banner.Treasury, Is.EqualTo(PirateClans.FoundingCost - 1), "Expand touched the treasury despite refusing to found");

            // With the cost restored, a band and route are ready again before long: the refusals above were the
            // treasury check at work, not a lack of anything to send (see the sibling test with treasury in hand
            // from the start, which founds a haven inside the same 30 days).
            for (; jd < StartJd + 60 && band == null; jd += 0.5)
            {
                g.Update(jd);
                banner.Treasury = 5e8;
                band = g.Traffic.Clans.Expand(banner, jd);
            }
            Assert.That(band, Is.Not.Null, "no band and route were ever ready, so the treasury check was never really exercised");
        }

        [Test]
        public void OnlyOneBandIsSentToAHavenLeftEmptyAtATime()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var banner = sol.World.Pirates;
            banner.Treasury = 5e8;
            Ship founder = null;
            double jd = StartJd;
            for (; jd < StartJd + 30 && founder == null; jd += 0.5)
            {
                g.Update(jd);
                founder = g.Traffic.Clans.Expand(banner, jd);
            }
            Assert.That(founder, Is.Not.Null, "no band set out to found a haven");
            var target = founder.Voyage.Destination;

            Settlement Founded() => target.World.Economy.Settlements.FirstOrDefault(s => s.Kind == SettlementKind.Haven && s.Owner == banner);
            jd = RunUntil(g, jd, 150, _ => Founded() != null);
            Assert.That(Founded(), Is.Not.Null, $"the band never founded its haven at {target.Name}");
            var settled = target.World.Fleet.Ships.Single(s => s.Name == founder.Name && !s.Destroyed);

            // The new haven's band is lost: it stands empty again, while the home system is kept manned.
            settled.Destroyed = true;
            foreach (var s in g.Systems.SelectMany(sys => sys.World.Fleet.Ships))
                if (s.Faction == banner && s.Voyage != null && s.Voyage.Purpose == VoyagePurpose.Relocate) s.Voyage = null;

            var moveBand = typeof(PirateClans).GetMethod("MoveBand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Ship Move() => (Ship)moveBand.Invoke(g.Traffic.Clans, new object[] { banner, g.Traffic.Clans.Havens(banner), jd });
            Ship first = null;
            for (int i = 0; i < 40 && first == null; i++)
            {
                jd += 0.5;
                g.Update(jd);
                if (g.Traffic.Clans.Bands(banner, target).Count == 0) first = Move();
            }
            Assert.That(first, Is.Not.Null, "no band ever set out to refill the empty haven");
            Assert.That(first.Voyage.Purpose, Is.EqualTo(VoyagePurpose.Relocate));
            Assert.That(first.Voyage.Destination, Is.SameAs(target));
            var home = first.Voyage.Home;

            // The home haven still has bands to spare, one of them in port and fit to sail, so only the band already on
            // its way can keep a second from being sent after it (U.SPC-T302: the check looked at moves from the empty
            // haven, not to it).
            Assert.That(g.Traffic.Clans.Bands(banner, home).Count, Is.GreaterThan(1), "the home haven has no band to spare");
            var spare = g.Traffic.Clans.Bands(banner, home).First(s => s.Orbit != null && s.Battle == null && s.Combat.Armed && s.Combat.Damage <= 0.2);
            spare.Phase = ShipPhase.Parked;
            spare.GateKm = null;
            spare.OutOfPropellant = false;
            Assert.That(g.Traffic.Clans.Bands(banner, target), Is.Empty, "the haven was refilled before the check");
            Assert.That(Move(), Is.Null, "a second band was sent to the haven the first is already filling");
        }

        [Test]
        public void ARobbedStateSendsANavyThroughTheGatesAfterAClansFoundHaven()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ee = g.Find("Epsilon Eridani");
            var daughters = ee.World.Pirates;
            var wavecrest = Named(ee.World, "Wavecrest");
            Assert.That(wavecrest.Hidden, Is.True);
            var federation = sol.World.Sol;
            Assert.That(g.Traffic.Clans.Bounty(daughters), Is.EqualTo(0));

            // The Daughters have robbed the Federation, and the Union has found their hold.
            g.Traffic.Clans.Pledge(federation, daughters, 1e8);
            Assert.That(g.Traffic.Clans.Bounty(daughters), Is.EqualTo(1e8));
            wavecrest.KnownTo.Add(ee.World.Sol);

            Ship Expedition() => g.Systems.SelectMany(s => s.World.Fleet.Ships).Concat(g.Traffic.InTransit())
                .FirstOrDefault(s => !s.Destroyed && s.Voyage?.Purpose == VoyagePurpose.Expedition && s.Voyage.Quarry == daughters);
            RunUntil(g, StartJd, 40, jd =>
            {
                if (Expedition() != null) return true;
                g.Traffic.Clans.Pursue(daughters, jd);
                return Expedition() != null;
            });
            var expedition = Expedition();
            Assert.That(expedition, Is.Not.Null, "no navy went after the Daughters' hold");
            Assert.That(expedition.Faction, Is.SameAs(federation));
            Assert.That(expedition.Voyage.Destination, Is.SameAs(ee));
            Assert.That(wavecrest.KnownBy(federation), Is.True, "word of the hold reached the Federation");
        }

        [Test]
        public void AnExpeditionSentAfterOneClanLeavesAnotherClansHoldAlone()
        {
            // U.SPC-T309: the quarry filter let any pirate's haven through, so a navy sent after one clan went for whichever hold it knew.
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ee = g.Find("Epsilon Eridani");
            var barnard = g.Find("Barnard's Star");
            var daughters = ee.World.Pirates;
            var drift = barnard.World.Pirates;
            Assert.That(drift, Is.Not.SameAs(daughters));
            var wavecrest = Named(ee.World, "Wavecrest");
            Assert.That(wavecrest.Owner, Is.SameAs(daughters));
            Assert.That(ee.World.PortBodies(), Does.Not.Contain(wavecrest.Body), "no port shares the hold's body");

            var ship = ee.World.Fleet.Ships.First(s => !s.Destroyed && s.Faction == ee.World.Sol && s.Spec.IsMilitary);
            wavecrest.KnownTo.Add(ship.Faction);
            int here = ee.World.PortBodies().First(b => b != wavecrest.Body);
            ship.GateKm = null;
            ship.Voyage = new Voyage { Purpose = VoyagePurpose.Expedition, Home = sol, Destination = ee, Faction = ship.Faction, Quarry = drift };

            Assert.That(ee.World.VoyageOrders(ship, here, StartJd), Is.Not.EqualTo(wavecrest.Body), "an expedition after the Drift made for the Daughters' hold");
            ship.Voyage.Quarry = daughters;
            Assert.That(ee.World.VoyageOrders(ship, here, StartJd), Is.EqualTo(wavecrest.Body), "an expedition after the Daughters passed their known hold by");
        }

        [Test]
        public void BountyHuntersGoThroughTheGatesAfterAClanWithAPriceOnIt()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var barnard = g.Find("Barnard's Star");
            var drift = barnard.World.Pirates;
            var hunter = sol.World.Outlaws.Commission(Named(sol.World, "Earth Orbital"), StartJd);
            g.Traffic.Clans.Pledge(sol.World.Sol, drift, 1e8);

            Ship Hunt() => g.Systems.SelectMany(s => s.World.Fleet.Ships).Concat(g.Traffic.InTransit())
                .FirstOrDefault(s => !s.Destroyed && s.Voyage?.Purpose == VoyagePurpose.Hunt && s.Voyage.Quarry == drift);
            RunUntil(g, StartJd, 40, jd =>
            {
                if (Hunt() != null) return true;
                // Free of the home boards' contracts for the test: there is bigger money beyond the gates.
                foreach (var h in sol.World.Outlaws.Hunters) h.Contract = null;
                g.Traffic.Clans.Pursue(drift, jd);
                return Hunt() != null;
            });
            var hunt = Hunt();
            Assert.That(hunt, Is.Not.Null, "no bounty hunter went after the Drift");
            Assert.That(hunt.Voyage.Destination, Is.SameAs(barnard));
            Assert.That(sol.World.Outlaws.Hunters.Any(h => h.Ship == hunt.Voyage.Origin), Is.True, "a hunter off the home boards");
            Assert.That(hunter, Is.Not.Null);
            Assert.That(g.Traffic.Clans.Hunts, Is.EqualTo(1));
        }
    }
}
