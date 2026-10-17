using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T99: trading companies. NPC corporations own the merchant fleet, take its trade profit, pay its running
    /// costs, buy new hulls at yards when the profit pays them back, steer their ships to the runs that pay and away
    /// from factions that shun them, and are bought out or go bankrupt when they cannot pay their way, so the merchant
    /// fleet's size and trade come out of the economics rather than fixed spawns.
    /// </summary>
    [TestFixture]
    public class CompanyTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Settlement Port(World w, string name) => w.Economy.Settlements.First(s => s.Name == name);

        static Company Named(World w, string name) => w.Companies.All.First(c => c.Name == name);

        /// <summary>Parks every hull of <paramref name="c"/> at <paramref name="body"/>, out of any fight, so it can be sold.</summary>
        static void ParkFleet(World w, Company c, string body, double jd)
        {
            w.War.Battles.Clear();
            foreach (var s in c.Ships)
            {
                s.Battle = null;
                w.Economy.Release(s);
                s.HasCargo = false;
                s.Voyage = null;
                w.Fleet.Station(s, w.Catalog[body].Index, jd);
            }
        }

        /// <summary>
        /// Nobody else is in a position to buy anything: all the other companies are broke and losing money. The books
        /// are brought up to <paramref name="jd"/> first, so a review at the same moment books nothing more.
        /// </summary>
        static void BreakTheRivals(World w, Company except, double jd)
        {
            w.Companies.Review(jd);
            foreach (var o in w.Companies.All)
            {
                if (o == except) continue;
                o.Cash = 0;
                o.IncomePerDay = -1;
                o.OnOrder = null;
                o.OrderYard = null;
                o.OrderPrice = 0;
            }
        }

        // ---------------------------------------------------------------- who owns the fleet

        [Test]
        public void TheMerchantFleetBelongsToNamedCompaniesWithAHomePortALiveryAndStanding()
        {
            var w = CreateWorld();
            Assert.That(w.Companies.Enabled, Is.True);
            Assert.That(w.Companies.All.Count, Is.GreaterThanOrEqualTo(5));
            foreach (var c in w.Companies.All)
            {
                Assert.That(c.Name, Is.Not.Empty);
                Assert.That(c.HomePort, Is.Not.Null, c.Name);
                Assert.That(c.HomePort.Kind, Is.Not.EqualTo(SettlementKind.Haven));
                Assert.That(c.StandingWith(c.Flag), Is.GreaterThan(0), "its flag thinks well of it");
                Assert.That(c.StandingWith(w.Pirates), Is.EqualTo(-1));
            }
            // Liveries tell them apart.
            var liveries = w.Companies.All.Select(c => (c.Livery.R, c.Livery.G, c.Livery.B)).ToList();
            Assert.That(liveries.Distinct().Count(), Is.EqualTo(liveries.Count));
            // A Federation company is shunned by the Reach the Federation is at war with.
            Assert.That(Named(w, "Kepler Freight Lines").StandingWith(w.Reach), Is.LessThan(Companies.ShunnedStanding));
            Assert.That(Named(w, "Kepler Freight Lines").StandingWith(w.Belt), Is.GreaterThanOrEqualTo(0));

            // Every merchant hull whose flag has a company belongs to one, sails under its flag and is in its list.
            var flags = w.Companies.All.Select(c => c.Flag).ToHashSet();
            var merchants = w.Fleet.Ships.Where(s => !s.Destroyed && w.IsTrader(s) && flags.Contains(s.Faction)).ToList();
            Assert.That(merchants, Is.Not.Empty);
            foreach (var s in merchants)
            {
                Assert.That(s.Company, Is.Not.Null, s.Name);
                Assert.That(s.Faction, Is.SameAs(s.Company.Flag));
                Assert.That(s.Company.Ships, Does.Contain(s));
            }
            // Ore haulers went to the ore companies.
            foreach (var s in merchants.Where(s => s.Spec.Class == ShipClass.Miner && s.Faction == w.Belt))
                Assert.That(s.Company.Name, Is.EqualTo("Vesta Ore and Haulage"));
            // Free roamers and warships are nobody's company.
            Assert.That(w.Fleet.Ships.Where(s => s.Role == ShipRole.Free || s.Spec.IsMilitary).All(s => s.Company == null), Is.True);
        }

        // ---------------------------------------------------------------- the books

        [Test]
        public void ACompanyTakesItsHullsTradeProfitAndPaysRunningCostsAndTaxToItsFlag()
        {
            var w = CreateWorld();
            var c = Named(w, "Jovian Bulk Carriers");
            double jd = StartJd + 1;
            w.Companies.Review(jd);
            var ship = c.Ships[0];
            double cash = c.Cash, flag = c.Flag.Treasury;
            double running = c.Ships.Sum(s => Economy.RunningCostPerDay(s.Spec.Class)) * 10;
            // The flag's treasury booked a sale's profit for the ship, as the trade code does.
            ship.Credits += 4e6;
            c.Flag.Treasury += 4e6;
            w.Companies.Review(jd + 10);
            double tax = 4e6 * Companies.CorporateTax;
            Assert.That(c.Cash, Is.EqualTo(cash + 4e6 - tax - running).Within(1));
            Assert.That(c.Flag.Treasury, Is.EqualTo(flag + tax).Within(1), "the profit is the company's; the flag keeps its tax");
            Assert.That(c.ClassPerDay[(int)ShipClass.Freighter], Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- growth from profit

        [Test]
        public void AProfitableCompanyBuysANewHullAtAYardAndTakesDelivery()
        {
            var w = CreateWorld();
            var c = Named(w, "Jovian Bulk Carriers");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            c.Cash = 60e6;
            c.OnOrder = null;
            // Its freighters earn well: one more pays its price back well inside the payback horizon.
            c.ClassPerDay[(int)ShipClass.Freighter] = 30000;
            int hulls = c.Hulls;
            w.Companies.Review(jd);
            Assert.That(c.OnOrder, Is.EqualTo(ShipClass.Freighter));
            Assert.That(c.OrderYard, Is.SameAs(Port(w, "Ganymede Union")), "it orders at its home port's yard");
            Assert.That(c.Cash, Is.EqualTo(60e6 - c.OrderPrice).Within(1), "the price is held from its cash");
            Assert.That(w.Companies.OrderAt(c.OrderYard), Is.EqualTo(ShipClass.Freighter));

            // The yard lays it down from the company's money, not its owner's, and delivers it to the company.
            var yard = c.OrderYard;
            double price = c.OrderPrice;
            yard.BuildPoints = 1e6;
            Assert.That(w.Companies.PaidFor(yard, ShipClass.Freighter, price), Is.True);
            int before = w.Fleet.Ships.Count;
            for (double t = jd; t < jd + 12 && c.Hulls == hulls; t += 0.5) w.Economy.AdvanceTo(t);
            Assert.That(c.Hulls, Is.EqualTo(hulls + 1));
            Assert.That(c.HullsBought, Is.EqualTo(1));
            Assert.That(c.OnOrder, Is.Null);
            var built = w.Fleet.Ships.Skip(before).Single(s => s.Company == c);
            Assert.That(built.Spec.Class, Is.EqualTo(ShipClass.Freighter));
            Assert.That(built.Faction, Is.SameAs(c.Flag));
            Assert.That(built.Name, Does.Contain("Jovian"));
            // The company paid the yard its price; the owner was not charged for the hull.
            Assert.That(c.HullsSpent, Is.EqualTo(price).Within(1));
            Assert.That(w.Events.Any(e => e.Kind == WorldEventKind.Launch && e.Text.Contains("for Jovian Bulk Carriers")), Is.True);
        }

        [Test]
        public void ANavyAtWarWornBelowStrengthTakesItsYardsSlipsAheadOfTheCompanies()
        {
            var w = CreateWorld();
            var c = Named(w, "Kepler Freight Lines");
            var earth = Port(w, "Earth Orbital");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            c.Cash = 60e6;
            c.OnOrder = null;
            c.ClassPerDay[(int)ShipClass.Freighter] = 30000;
            w.Companies.Review(jd);
            Assert.That(c.OrderYard, Is.SameAs(earth));
            Assert.That(w.War.AtWar(w.Sol), Is.True);
            // The Federation's navy is cut down below its peacetime strength: its yard builds warships first.
            int navy = 0;
            foreach (var s in w.Fleet.Ships.Where(s => !s.Destroyed && s.Faction == w.Sol && s.Spec.IsMilitary))
                if (++navy > 3) s.Destroyed = true;
            var cls = w.Economy.ChooseBuild(earth);
            Assert.That(cls, Is.Not.Null);
            Assert.That(ShipClassSpec.Of(cls.Value).IsMilitary, Is.True);
            // At peace, the company's order comes first.
            w.Diplomacy.Set(w.Sol, w.Reach, Relation.Peace);
            if (!w.War.AtWar(w.Sol)) Assert.That(w.Economy.ChooseBuild(earth), Is.EqualTo(ShipClass.Freighter));
        }

        [Test]
        public void ACompanyWhoseHullsDoNotPayBuysNoMoreHowEverMuchCashItHas()
        {
            var w = CreateWorld();
            var c = Named(w, "Vesta Ore and Haulage");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            c.Cash = 500e6;
            c.OnOrder = null;
            for (int i = 0; i < c.ClassPerDay.Length; i++) c.ClassPerDay[i] = 500; // less than a hull's running costs
            w.Companies.Review(jd);
            Assert.That(c.OnOrder, Is.Null);
            Assert.That(c.Cash, Is.EqualTo(500e6));
        }

        [Test]
        public void ACompanyThatCannotAffordAHullAndItsReserveWaits()
        {
            var w = CreateWorld();
            var c = Named(w, "Kepler Freight Lines");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            c.OnOrder = null;
            c.ClassPerDay[(int)ShipClass.Freighter] = 30000;
            c.Cash = Economy.HullPrice(ShipClass.Freighter) * 1.1;
            w.Companies.Review(jd);
            Assert.That(c.OnOrder, Is.Null, "the price and a reserve, or nothing");
        }

        [Test]
        public void OverAYearTheFleetIsBuiltFromCompanyProfitsAndTheLaunchesAreTheirs()
        {
            var w = CreateWorld();
            int start = w.Fleet.Ships.Count;
            for (double jd = StartJd; jd <= StartJd + 300; jd += 0.5)
            {
                w.Ephemeris.Update(jd);
                w.Update(jd);
            }
            int bought = w.Companies.All.Sum(c => c.HullsBought);
            TestContext.Out.WriteLine(string.Join("\n", w.Companies.All.Select(c => $"{c.Name,-28} {c.Fate,-9} hulls {c.Hulls,3} bought {c.HullsBought,3} cash {c.Cash / 1e6,7:N1}M  {c.IncomePerDay,9:N0}/day  routes {c.Routes.Count}")));
            Assert.That(bought, Is.GreaterThan(5), "profit pays for new hulls");
            // Every merchant hull a yard launched was a company's order (none comes from a fixed spawn).
            var launches = w.Events.Where(e => e.Kind == WorldEventKind.Launch && e.Text.Contains(" launches the ")).ToList();
            Assert.That(launches, Is.Not.Empty);
            foreach (var e in launches) Assert.That(e.Text, Does.Contain(" for "), e.Text);
            Assert.That(w.Fleet.Ships.Skip(start).Count(s => s.Company != null && !s.Destroyed), Is.GreaterThan(5));
            // They work runs across the system's planetary systems, and point their fleets at the best of them.
            Assert.That(w.Companies.All.Any(c => c.Routes.Any(r => r.CrossSystem)), Is.True);
            Assert.That(w.Companies.All.Where(c => c.Trading && c.Hulls > 0).All(c => c.Chosen.Count > 0), Is.True);
            Assert.That(w.Companies.All.Sum(c => c.Earned), Is.GreaterThan(0));
        }

        // ---------------------------------------------------------------- route choice

        [Test]
        public void RouteChoiceFavoursTheRunsThatPayAndShunsHostilePorts()
        {
            var w = CreateWorld();
            var c = Named(w, "Meridian Free Traders");
            var earth = Port(w, "Earth Orbital");
            var ceres = Port(w, "Ceres Hub");
            var mars = Port(w, "Ares Prime");
            var titan = Port(w, "Kraken Port");
            c.Routes.Clear();
            double jd = StartJd + 1;
            w.Companies.Record(c, earth, ceres, 6e6, 30, jd);
            w.Companies.Record(c, earth, mars, 1e6, 20, jd);
            w.Companies.Record(c, mars, ceres, -2e6, 25, jd);
            w.Companies.Record(c, mars, ceres, -2e6, 25, jd);
            w.Companies.Review(jd);

            // The best paying run a day is chosen first; a losing one never.
            Assert.That(c.Chosen[0].From, Is.SameAs(earth));
            Assert.That(c.Chosen[0].To, Is.SameAs(ceres));
            Assert.That(c.Chosen.Any(r => r.Profit < 0), Is.False);
            Assert.That(c.RouteOf(earth, ceres).CrossSystem, Is.True, "Earth to Ceres crosses between planetary systems");

            var ship = c.Ships.First(s => s.Spec.Class == ShipClass.Freighter);
            double chosen = Companies.RouteFactor(ship, earth, ceres);
            double plain = Companies.RouteFactor(ship, ceres, titan);
            double losing = Companies.RouteFactor(ship, mars, ceres);
            Assert.That(chosen, Is.GreaterThan(plain));
            Assert.That(losing, Is.LessThan(plain));
            // A ship nobody's company owns plans as it always did.
            var navy = w.Fleet.Ships.First(s => s.Spec.IsMilitary);
            Assert.That(Companies.RouteFactor(navy, earth, ceres), Is.EqualTo(1));

            // A faction that shuns the company: its ports are off the map, even ones its flag may dock at.
            c.Standing[w.Belt] = -0.9;
            Assert.That(Companies.RouteFactor(ship, earth, ceres), Is.EqualTo(0));
            Assert.That(Companies.RouteFactor(ship, ceres, earth), Is.EqualTo(0));
        }

        [Test]
        public void ACaptainPlanningARunNeverPicksThePortsOfAFactionThatShunsItsCompany()
        {
            var w = CreateWorld();
            var c = Named(w, "Meridian Free Traders");
            int earth = w.Catalog["Earth"].Index;
            var ship = c.Ships.First(s => s.Spec.Class == ShipClass.Freighter);
            w.Economy.Release(ship);
            ship.HasCargo = false;
            ship.Propellant = ship.PropellantCapacity;
            w.Fleet.Station(ship, earth, StartJd);
            foreach (var f in w.Fleet.Factions)
                if (f != w.Sol) c.Standing[f] = -0.9;
            for (int i = 0; i < 6; i++)
            {
                w.Economy.Release(ship);
                ship.HasCargo = false;
                int next = w.Economy.PlanTrade(ship, earth, StartJd);
                if (next < 0) continue;
                Assert.That(w.Economy.At(next).Any(s => s.Owner == w.Sol), Is.True, w.Catalog[next].Name);
                if (ship.HasCargo) Assert.That(ship.CargoDestination.Owner, Is.SameAs(w.Sol));
            }
        }

        [Test]
        public void DeliveriesRaiseACompanysStandingWithThePortsOwner()
        {
            var w = CreateWorld();
            var c = Named(w, "Meridian Free Traders");
            var ship = c.Ships.First(s => s.Spec.Class == ShipClass.Freighter);
            var ceres = Port(w, "Ceres Hub");
            double before = c.StandingWith(w.Belt);
            w.Companies.OnSold(ship, ceres, Good.Food, 5000);
            Assert.That(c.StandingWith(w.Belt), Is.GreaterThan(before));
        }

        // ---------------------------------------------------------------- trouble

        [Test]
        public void ACompanyDeepInDebtSellsItsHullsAndGoesBankrupt()
        {
            var w = CreateWorld();
            var c = Named(w, "Kraken Hydrocarbon Company");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            ParkFleet(w, c, "Titan", jd);
            var fleet = c.Ships.ToList();
            Assert.That(fleet, Is.Not.Empty);
            c.Cash = -400e6;
            // One hull a review goes to the breakers (nobody can buy it), until there are none left to sell ...
            w.Companies.Review(jd);
            Assert.That(c.Hulls, Is.EqualTo(fleet.Count - 1));
            Assert.That(c.HullsScrapped, Is.EqualTo(1));
            var gone = fleet.First(s => s.Scrapped);
            Assert.That(gone.Destroyed && gone.GateDeparted, Is.True, "retired without a wreck");
            Assert.That(gone.Company, Is.Null);
            // ... or the debt has gone on long enough: it is wound up.
            for (int i = 1; i <= fleet.Count + 20 && c.Trading; i++) w.Companies.Review(jd);
            Assert.That(c.Fate, Is.EqualTo(CompanyFate.Bankrupt));
            Assert.That(c.Ships, Is.Empty);
            Assert.That(fleet.All(s => s.Company == null), Is.True);
            Assert.That(w.Events.Any(e => e.Text.Contains("Kraken Hydrocarbon Company") && e.Text.Contains("bankrupt")), Is.True);
            // A bankrupt company buys and trades no more.
            w.Companies.Review(jd + 400);
            Assert.That(c.OnOrder, Is.Null);
        }

        [Test]
        public void ACompanyThatStaysInDebtTooLongIsWoundUpEvenWithHullsLeft()
        {
            var w = CreateWorld();
            var c = Named(w, "Ares Haulage");
            double jd = StartJd + 1;
            BreakTheRivals(w, c, jd);
            ParkFleet(w, c, "Mars", jd);
            // Too small a debt to sell hulls over, but it never comes right.
            c.Cash = -1e6;
            w.Companies.Review(jd);
            Assert.That(c.Trading, Is.True);
            Assert.That(c.HullsScrapped + c.HullsSold, Is.EqualTo(0));
            w.Companies.Review(jd + Companies.BankruptDays - 1);
            c.Cash = -1e6;
            Assert.That(c.Trading, Is.True);
            w.Companies.Review(jd + Companies.BankruptDays);
            Assert.That(c.Fate, Is.EqualTo(CompanyFate.Bankrupt));
            Assert.That(c.FateJd, Is.EqualTo(jd + Companies.BankruptDays));
        }

        [Test]
        public void ABankruptCompanysHullsGoToARivalThatCanUseThem()
        {
            var w = CreateWorld();
            var c = Named(w, "Long Haul Carrying Company");
            var rival = Named(w, "Meridian Free Traders");
            double jd = StartJd + 1;
            BreakTheRivals(w, rival, jd);
            ParkFleet(w, c, "Mars", jd);
            int hulls = c.Hulls;
            var fleet = c.Ships.ToList();
            // The rival is rich and its freighters pay, but it is losing money overall, so it buys hulls, not the company.
            rival.Cash = 400e6;
            rival.IncomePerDay = -1;
            rival.ClassPerDay[(int)ShipClass.Freighter] = 30000;
            rival.ClassPerDay[(int)ShipClass.Courier] = 30000;
            c.Cash = -50e6;
            w.Companies.Bankrupt(c, jd);
            Assert.That(c.Fate, Is.EqualTo(CompanyFate.Bankrupt));
            Assert.That(c.HullsSold, Is.EqualTo(hulls));
            foreach (var s in fleet)
            {
                Assert.That(s.Company, Is.SameAs(rival), s.Name);
                Assert.That(rival.Ships, Does.Contain(s));
                Assert.That(s.Destroyed, Is.False);
            }
            Assert.That(rival.Cash, Is.LessThan(400e6));
        }

        [Test]
        public void AStrugglingCompanyIsBoughtOutByAHealthyRivalFlyingFriendlyColours()
        {
            var w = CreateWorld();
            var target = Named(w, "Long Haul Carrying Company");
            var buyer = Named(w, "Meridian Free Traders");
            double jd = StartJd + 1;
            BreakTheRivals(w, buyer, jd);
            var hulls = target.Ships.ToList();
            buyer.Cash = 600e6;
            buyer.IncomePerDay = 200000;
            target.Cash = -10e6;
            double price = target.Value;
            double flag = target.Flag.Treasury;
            int buyerHulls = buyer.Hulls;
            w.Companies.Review(jd);

            Assert.That(target.Fate, Is.EqualTo(CompanyFate.Merged));
            Assert.That(target.MergedInto, Is.SameAs(buyer));
            Assert.That(buyer.Hulls, Is.EqualTo(buyerHulls + hulls.Count));
            foreach (var s in hulls)
            {
                Assert.That(s.Company, Is.SameAs(buyer));
                Assert.That(s.Faction, Is.SameAs(buyer.Flag));
            }
            Assert.That(target.Ships, Is.Empty);
            Assert.That(buyer.Cash, Is.LessThan(600e6 - price + 1e6), "it paid for the company and took on its debt");
            Assert.That(target.Flag.Treasury, Is.GreaterThan(flag), "the shareholders were paid");
            Assert.That(w.Events.Any(e => e.Text.Contains("Meridian Free Traders buys out Long Haul Carrying Company")), Is.True);
        }

        [Test]
        public void NoCompanyBuysOutOneUnderTheColoursOfAnEnemy()
        {
            var w = CreateWorld();
            var target = Named(w, "Kraken Hydrocarbon Company"); // the Reach
            var enemy = Named(w, "Kepler Freight Lines");        // the Federation, at war with it
            double jd = StartJd + 1;
            BreakTheRivals(w, enemy, jd);
            enemy.Cash = 2e9;
            enemy.IncomePerDay = 500000;
            target.Cash = -1e6;
            Assert.That(w.Companies.TryMerge(target, jd), Is.False);
            Assert.That(target.Trading, Is.True);
        }

        // ---------------------------------------------------------------- new companies

        [Test]
        public void WhenTheTradePaysANewCompanyIsFoundedAtTheBusiestPortWithoutOne()
        {
            var w = CreateWorld();
            foreach (var c in w.Companies.All) c.ClassPerDay[(int)ShipClass.Freighter] = 30000;
            var homes = w.Companies.All.Select(c => c.HomePort).ToHashSet();
            var expected = w.Economy.Settlements
                .Where(s => !s.Fallen && s.Owner != null && !s.Owner.IsPirate && s.Kind != SettlementKind.Haven && !homes.Contains(s) && s.Owner.Treasury >= w.Companies.FoundingCapital * 5)
                .OrderByDescending(s => s.TradeVolumeCredits).First();
            double treasury = expected.Owner.Treasury;
            int count = w.Companies.All.Count;
            var founded = w.Companies.TryFound(StartJd + 1);
            Assert.That(founded, Is.Not.Null);
            Assert.That(w.Companies.All.Count, Is.EqualTo(count + 1));
            Assert.That(founded.HomePort, Is.SameAs(expected));
            Assert.That(founded.Flag, Is.SameAs(expected.Owner));
            Assert.That(founded.Cash, Is.EqualTo(w.Companies.FoundingCapital));
            Assert.That(expected.Owner.Treasury, Is.EqualTo(treasury - w.Companies.FoundingCapital).Within(1), "the investors' capital");
            Assert.That(founded.Name, Is.Not.Empty);
            Assert.That(w.Companies.All.Count(c => c.Name == founded.Name), Is.EqualTo(1));

            // When the trade does not pay, nobody puts money into a new one.
            foreach (var c in w.Companies.All) c.ClassPerDay[(int)ShipClass.Freighter] = 100;
            Assert.That(w.Companies.TryFound(StartJd + 2), Is.Null);
        }

        [Test]
        public void AlphaCentauriHasItsOwnCompanies()
        {
            var eph = new Ephemeris(BodyCatalog.CreateAlphaCentauri());
            eph.Update(StartJd);
            var w = World.CreateAlphaCentauri(eph, StartJd);
            Assert.That(w.Companies.All.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(w.Fleet.Ships.Where(s => w.IsTrader(s) && s.Role != ShipRole.Free).Count(s => s.Company != null), Is.GreaterThan(5));
        }
    }
}
