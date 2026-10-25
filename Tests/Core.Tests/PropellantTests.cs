using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T57, propellant and freight cost: the rocket equation sets every manoeuvre's propellant from
    /// each design's exhaust velocity and dry/wet mass; transits are charged it, a ship short of it waits
    /// in port or runs dry and drifts until a tanker comes; ports sell it (water or hydrocarbons and the
    /// helium-3 that burns them) and feel the demand; traders price a run by its propellant, days and lane
    /// tolls and leave out runs their tanks cannot cover; the player's hand-flown thrust is paid for too.
    /// </summary>
    [TestFixture]
    public class PropellantTests
    {
        const double StartJd = 2461324.0;

        static Ship Freighter(Faction f = null) => new Ship
        {
            Spec = ShipClassSpec.Of(ShipClass.Freighter),
            Faction = f ?? new Faction("Traders", "MV", default),
            Role = ShipRole.Trader,
            CapacityTonnes = Economy.CapacityOf(ShipClass.Freighter),
        };

        // ---------------------------------------------------------------- delta-v budgets

        [Test]
        public void EveryHullHasADriveAndTanksAndANewHullLeavesFull()
        {
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
            {
                var spec = CombatSpec.Of(c);
                Assert.That(spec.DryMassTonnes, Is.GreaterThan(0), c + " dry mass");
                Assert.That(spec.PropellantTonnes, Is.GreaterThan(0), c + " tanks");
                Assert.That(spec.ExhaustVelocityKmS, Is.InRange(1000, 10000), c + " exhaust velocity");
                var ship = new Ship { Spec = ShipClassSpec.Of(c) };
                Assert.That(ship.Propellant, Is.EqualTo(spec.PropellantTonnes), c + " starts full");
            }
        }

        [Test]
        public void TheDeltaVBudgetIsTsiolkovskyAndAFullHoldShrinksIt()
        {
            var ship = Freighter();
            var spec = CombatSpec.Of(ShipClass.Freighter);
            double ve = spec.ExhaustVelocityKmS, dry = spec.DryMassTonnes, tanks = spec.PropellantTonnes;
            Assert.That(ship.DeltaVBudgetKmS, Is.EqualTo(ve * Math.Log((dry + tanks) / dry)).Within(1e-9));
            // 2,500 km/s through a mass ratio of 15,000 / 9,000.
            Assert.That(ship.DeltaVBudgetKmS, Is.EqualTo(1277.07).Within(0.01));

            ship.HasCargo = true;
            ship.CargoTonnes = 20000;
            Assert.That(ship.MassTonnes, Is.EqualTo(dry + tanks + 20000));
            Assert.That(ship.DeltaVBudgetKmS, Is.EqualTo(ve * Math.Log((dry + tanks + 20000) / (dry + 20000))).Within(1e-9));
            Assert.That(ship.DeltaVBudgetKmS, Is.LessThan(500), "a loaded freighter has well under half the delta-v of an empty one");
        }

        [Test]
        public void ABurnThrowsOutWhatTheRocketEquationSaysAndNoMore()
        {
            double ve = 2500, m0 = 35000, dv = 180;
            double burned = Propulsion.PropellantBurned(dv, ve, m0);
            Assert.That(m0 / (m0 - burned), Is.EqualTo(Math.Exp(dv / ve)).Within(1e-12));
            Assert.That(Propulsion.DeltaV(ve, m0, m0 - burned), Is.EqualTo(dv).Within(1e-9));
            Assert.That(Propulsion.PropellantBurned(0, ve, m0), Is.EqualTo(0));

            // Spending part of the tanks, then the rest: a dry ship gets only what was left.
            var ship = Freighter();
            double share = Propulsion.Spend(ship, 100);
            Assert.That(share, Is.EqualTo(1));
            Assert.That(ship.Propellant, Is.EqualTo(6000 - Propulsion.PropellantBurned(100, 2500, 15000)).Within(1e-9));
            double left = ship.DeltaVBudgetKmS;
            share = Propulsion.Spend(ship, left * 2);
            Assert.That(share, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(ship.Propellant, Is.EqualTo(0));
            Assert.That(Propulsion.Spend(ship, 10), Is.EqualTo(0));
        }

        [Test]
        public void AFlipAndBurnCostsItsAccelerationTimesItsFlightTime()
        {
            double a = 0.1 * 9.80665e-3, d = 400000;
            double t = 2 * Math.Sqrt(d / a);
            Assert.That(Propulsion.BrachistochroneDeltaV(a, d), Is.EqualTo(a * t).Within(1e-9));
            var ship = new Ship
            {
                AccelKmPerDay2 = a * Astro.SecondsPerDay * Astro.SecondsPerDay,
                PhaseStartJd = StartJd,
                PhaseEndJd = StartJd + t / Astro.SecondsPerDay,
                DepartureVelocity = new DVec3(2, 0, 0) * Astro.SecondsPerDay,
                ArrivalVelocity = new DVec3(-1, 4, 0) * Astro.SecondsPerDay,
            };
            Assert.That(Propulsion.TransitDeltaV(ship), Is.EqualTo(a * t + 5).Within(1e-6), "the burn plus the velocity match (|(-3, 4)| = 5 km/s)");
        }

        // ---------------------------------------------------------------- transits on real propellant

        sealed class Run
        {
            public Ephemeris Eph;
            public Fleet Fleet;
            public Ship Ship;
            public int Earth, Moon;
            public int Dry, Tankers;
            public double Delivered;
        }

        /// <summary>A freighter shuttling Earth to the Moon and back on its own tanks (no lanes, no rings).</summary>
        static Run Shuttle(double propellant, Func<Ship, int, bool> canRefuel = null)
        {
            var r = new Run { Eph = new Ephemeris(BodyCatalog.CreateSolSystem()) };
            r.Eph.Update(StartJd);
            r.Fleet = new Fleet(r.Eph, 5) { BurnsPropellant = s => true, CanRefuelAt = canRefuel, AvoidCollisions = false };
            r.Earth = r.Eph.Catalog["Earth"].Index;
            r.Moon = r.Eph.Catalog["Moon"].Index;
            int earth = r.Earth, moon = r.Moon;
            r.Fleet.ChooseNext = (s, here, jd) => here == earth ? moon : earth;
            r.Fleet.RanDry = (s, jd) => r.Dry++;
            r.Fleet.TankerArrived = (s, t, jd) => { r.Tankers++; r.Delivered += t; };
            var f = r.Fleet.AddFaction("Traders", "MV", default);
            r.Ship = r.Fleet.AddShip("Shuttle", ShipClass.Freighter, f, ShipRole.Trader);
            r.Ship.Ports = new[] { earth, moon };
            r.Ship.Propellant = propellant;
            r.Fleet.Station(r.Ship, earth, StartJd);
            return r;
        }

        static double StepUntil(Run r, Func<bool> done, double days, double step = 0.01)
        {
            for (double jd = StartJd; jd < StartJd + days; jd += step)
            {
                r.Eph.Update(jd);
                r.Fleet.Update(jd);
                if (done()) return jd;
            }
            Assert.Fail("never happened in " + days + " days");
            return double.NaN;
        }

        [Test]
        public void ATransitIsChargedItsDeltaVFromTheTanksAtTheShipsMass()
        {
            var r = Shuttle(propellant: 6000);
            var ship = r.Ship;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 5);
            double dv = Propulsion.TransitDeltaV(ship);
            Assert.That(dv, Is.InRange(20, 80), "Earth to the Moon at 0.1 g is a few tens of km/s");
            double expected = Propulsion.PropellantBurned(dv, ship.ExhaustVelocityKmS, ship.DryMassTonnes + 6000);
            Assert.That(ship.LegPropellant, Is.EqualTo(expected).Within(1e-6));
            Assert.That(ship.Propellant, Is.EqualTo(6000 - expected).Within(1e-6));
            Assert.That(double.IsNaN(ship.StrandJd), Is.True, "a full tank covers the hop");
            // And the planner's estimate is in the same range as what the planned burn actually takes.
            double estimate = r.Fleet.EstimateTrip(ship, r.Earth, r.Moon, ship.PhaseStartJd).DeltaVKmS;
            Assert.That(estimate, Is.InRange(dv * 0.8, dv * 2));
        }

        [Test]
        public void AShipThatRunsDryDriftsCallsATankerAndThenCarriesOn()
        {
            var r = Shuttle(propellant: 100);
            var ship = r.Ship;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 5);
            Assert.That(ship.Propellant, Is.EqualTo(0), "it set out on what it had");
            Assert.That(ship.StrandJd, Is.GreaterThan(ship.PhaseStartJd).And.LessThan(ship.PhaseEndJd));
            double strandJd = ship.StrandJd;
            var expectedSpot = r.Fleet.StateOf(ship, strandJd);

            StepUntil(r, () => ship.OutOfPropellant, 5);
            Assert.That(r.Dry, Is.EqualTo(1));
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Parked));
            Assert.That(ship.Orbit.Fixed, Is.True, "dead in space, not in orbit");
            r.Eph.Update(strandJd);
            Assert.That((r.Fleet.StateOf(ship, strandJd).HelioKm - expectedSpot.HelioKm).Length, Is.LessThan(1), "it drifts where the tanks gave out");
            Assert.That(ship.DestinationBody, Is.EqualTo(r.Moon), "still bound where it was going");

            StepUntil(r, () => r.Tankers > 0, 10);
            Assert.That(r.Delivered, Is.EqualTo(ship.PropellantCapacity).Within(1), "the tanker fills it");
            Assert.That(ship.OutOfPropellant, Is.False);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Transit));
            int trips = ship.TripsCompleted;
            StepUntil(r, () => ship.TripsCompleted > trips, 10);
            Assert.That(ship.Orbit.Body, Is.EqualTo(r.Moon));
        }

        [Test]
        public void AHoledTankLeavesAFullShipToRunDryPartWay()
        {
            var r = Shuttle(propellant: 6000);
            r.Fleet.LeakChance = 1;
            double vented = 0;
            r.Fleet.Leaked = (s, t, jd) => vented += t;
            var ship = r.Ship;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 5);
            Assert.That(vented, Is.GreaterThan(3000), "most of a full tank went");
            Assert.That(ship.StrandJd, Is.LessThan(ship.PhaseEndJd), "what was left would not see the leg through");
            StepUntil(r, () => ship.OutOfPropellant, 5);
            Assert.That(r.Dry, Is.EqualTo(1));
            // The tanker stays alongside for the rest of the leg: no second leak, no second strand.
            StepUntil(r, () => r.Tankers > 0, 10);
            Assert.That(double.IsNaN(ship.StrandJd), Is.True);
        }

        [Test]
        public void AShipShortOfPropellantInPortWaitsThereForItInsteadOfSettingOut()
        {
            var r = Shuttle(propellant: 100, canRefuel: (s, body) => true);
            var ship = r.Ship;
            StepUntil(r, () => ship.WaitingForPropellant, 5);
            Assert.That(ship.Phase, Is.EqualTo(ShipPhase.Parked));
            Assert.That(ship.DestinationBody, Is.EqualTo(r.Earth));
            ship.Propellant = ship.PropellantCapacity;
            StepUntil(r, () => ship.Phase == ShipPhase.Transit, 5);
            Assert.That(r.Dry, Is.EqualTo(0));
        }

        [Test]
        public void AnEmptyShipAwayFromAnyPortCallsATankerWhereItIs()
        {
            var r = Shuttle(propellant: 0);
            Assert.That(r.Ship.OutOfPropellant, Is.True);
            Assert.That(r.Dry, Is.EqualTo(1));
            Assert.That(r.Ship.PhaseEndJd - StartJd, Is.InRange(Propulsion.TankerDays, 2 * Propulsion.TankerDays + 2));
        }

        [Test]
        public void AChangeOfCourseGivesBackThePropellantOfTheLegNotFlown()
        {
            var r = Shuttle(propellant: 6000);
            var ship = r.Ship;
            double departed = StepUntil(r, () => ship.Phase == ShipPhase.Transit, 5);
            double leg = ship.LegPropellant, before = ship.Propellant;
            double jd = ship.PhaseStartJd + (ship.PhaseEndJd - ship.PhaseStartJd) * 0.25;
            r.Eph.Update(jd);
            Assert.That(r.Fleet.Divert(ship, r.Earth, jd), Is.True);
            // Three quarters of the first leg came back, then the new leg was charged.
            Assert.That(ship.Propellant + ship.LegPropellant, Is.EqualTo(before + leg * 0.75).Within(1e-6));
        }

        // ---------------------------------------------------------------- ports sell it

        static (Economy economy, Settlement port, Faction owner) Depot(double water = 50000, double hydrocarbons = 0)
        {
            var economy = new Economy(new Diplomacy(), startJd: StartJd);
            var owner = new Faction("Depot Co", "DC", default);
            var port = economy.Add("Depot", body: 3, SettlementKind.Station, owner, population: 0);
            port.Supply[(int)Good.Water] = 1000;
            port.Stock[(int)Good.Water] = water;
            port.Supply[(int)Good.Hydrocarbons] = 1000;
            port.Stock[(int)Good.Hydrocarbons] = hydrocarbons;
            port.Supply[(int)Good.HeliumThree] = 5;
            port.Stock[(int)Good.HeliumThree] = 100;
            port.RefreshPrices();
            return (economy, port, owner);
        }

        [Test]
        public void RefuellingBuysTheCheaperReactionMassWithItsHeliumAndThePortFeelsTheDemand()
        {
            var (economy, port, owner) = Depot(water: 50000, hydrocarbons: 50000);
            var ship = Freighter();
            ship.Propellant = 1000;
            double waterPrice = port.Price[(int)Good.Water], hcPrice = port.Price[(int)Good.Hydrocarbons];
            Assert.That(waterPrice, Is.LessThan(hcPrice), "water is the cheaper reaction mass here");
            double treasury = ship.Faction.Treasury;

            double taken = economy.Refuel(ship, 3, StartJd);

            Assert.That(taken, Is.EqualTo(5000).Within(1e-6));
            Assert.That(ship.Propellant, Is.EqualTo(ship.PropellantCapacity).Within(1e-6));
            Assert.That(port.Stock[(int)Good.Water], Is.EqualTo(45000).Within(1e-6));
            Assert.That(port.Stock[(int)Good.Hydrocarbons], Is.EqualTo(50000), "hydrocarbons cost more, so none were bought");
            Assert.That(port.Stock[(int)Good.HeliumThree], Is.EqualTo(100 - 5000 * Propulsion.HeliumThreePerTonne).Within(1e-9));
            Assert.That(ship.Faction.Treasury, Is.LessThan(treasury));
            Assert.That(owner.Treasury, Is.GreaterThan(0), "the port took its tax");
            Assert.That(port.Price[(int)Good.Water], Is.GreaterThan(waterPrice), "prices follow the stock");

            // Over the next ticks the sale shows up as propellant demand, part of the market's demand.
            economy.AdvanceTo(StartJd + 1);
            Assert.That(port.PropellantDemand[(int)Good.Water], Is.GreaterThan(0));
            Assert.That(port.Demand[(int)Good.Water], Is.EqualTo(port.PropellantDemand[(int)Good.Water]).Within(1e-9));
        }

        [Test]
        public void AFullShipBuysNothingAndAPlayerPaysOnlyWhatTheyCanAfford()
        {
            var (economy, port, _) = Depot();
            var ship = Freighter();
            Assert.That(economy.Refuel(ship, 3, StartJd), Is.EqualTo(0));
            Assert.That(economy.RefuelAt(ship, port, out _, out _), Is.EqualTo(Economy.TradeOutcome.NoRoom));

            ship.Propellant = 0;
            ship.Faction.Treasury = 0;
            Assert.That(economy.RefuelAt(ship, port, out double none, out _), Is.EqualTo(Economy.TradeOutcome.CantAfford));
            Assert.That(none, Is.EqualTo(0));

            ship.Faction.Treasury = 20000;
            Assert.That(economy.RefuelAt(ship, port, out double tonnes, out double cost), Is.EqualTo(Economy.TradeOutcome.Ok));
            Assert.That(tonnes, Is.GreaterThan(0).And.LessThan(ship.PropellantCapacity), "a part fill, as far as the credits went");
            Assert.That(cost, Is.LessThanOrEqualTo(20000));
            Assert.That(ship.Faction.Treasury, Is.GreaterThanOrEqualTo(0));
            Assert.That(ship.Propellant, Is.EqualTo(tonnes).Within(1e-9));
        }

        // ---------------------------------------------------------------- fuel-aware route choice

        const int Home = 10, NearPort = 11, FarPort = 12;

        /// <summary>
        /// A depot selling food (and water for the tanks) and two markets that want it: the near one (100 km/s
        /// of burns away) already fairly stocked, the far one (300 km/s, plus <paramref name="farHops"/> lanes) emptier.
        /// </summary>
        static (Economy economy, Ship ship) Markets(int seed, double farStock, bool priced, int farHops = 0)
        {
            var e = new Economy(new Diplomacy(), 0, seed);
            var owner = new Faction("Port", "P", default);
            var home = e.Add("Home", Home, SettlementKind.Station, owner, 0);
            home.Supply[(int)Good.Food] = 1000;
            home.Stock[(int)Good.Food] = 40000;
            home.Supply[(int)Good.Water] = 1000;
            home.Stock[(int)Good.Water] = 30000;
            var near = e.Add("Near", NearPort, SettlementKind.Station, owner, 0);
            near.Demand[(int)Good.Food] = 1000;
            near.Stock[(int)Good.Food] = 8000;
            var far = e.Add("Far", FarPort, SettlementKind.Station, owner, 0);
            far.Demand[(int)Good.Food] = 1000;
            far.Stock[(int)Good.Food] = farStock;
            foreach (var s in e.Settlements) s.RefreshPrices();
            if (priced) e.EstimateTrip = (s, from, to, jd) => to == NearPort ? new TripEstimate(3, 100, 0) : new TripEstimate(3, 300, farHops);
            else e.EstimateTravelDays = (s, from, to, jd) => 3;
            return (e, Freighter());
        }

        [Test]
        public void ATraderLeavesOutARunItsTanksCannotCoverAndTakesOneTheyCan()
        {
            int farFull = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var (e, ship) = Markets(seed, farStock: 0, priced: true);
                if (e.PlanTrade(ship, Home, 0) == FarPort) farFull++;
            }
            Assert.That(farFull, Is.GreaterThanOrEqualTo(30), "with full tanks the emptier far market is the usual pick");

            for (int seed = 1; seed <= 40; seed++)
            {
                var (e, ship) = Markets(seed, farStock: 0, priced: true);
                // Enough (with its reserve) for 100 km/s loaded, nowhere near 300.
                ship.Propellant = 2000;
                Assert.That(e.PlanTrade(ship, Home, 0), Is.EqualTo(NearPort), "seed " + seed);
                Assert.That(ship.CargoDestination.Name, Is.EqualTo("Near"));
            }
        }

        [Test]
        public void ARunIsPricedByItsPropellantAndTollsSoACostlyFarOneLosesItsAppeal()
        {
            int farUnpriced = 0, farPriced = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var (a, shipA) = Markets(seed, farStock: 6000, priced: false);
                if (a.PlanTrade(shipA, Home, 0) == FarPort) farUnpriced++;
                // A bulk freighter rides the lanes at its bulk share of the toll (U.SPC-T482), so sixty rides cost what thirty did.
                var (b, shipB) = Markets(seed, farStock: 6000, priced: true, farHops: 60);
                if (b.PlanTrade(shipB, Home, 0) == FarPort) farPriced++;
            }
            Assert.That(farUnpriced, Is.GreaterThanOrEqualTo(20), "on days alone the far market pays best");
            Assert.That(farPriced, Is.LessThanOrEqualTo(farUnpriced / 2), "its propellant and sixty lane tolls eat into that");
        }

        [Test]
        public void AStrandedTraderWithNothingToDoMakesForTheNearestPortThatSellsPropellant()
        {
            var e = new Economy(new Diplomacy(), 0, 3);
            var owner = new Faction("Port", "P", default);
            e.Add("Empty Rock", Home, SettlementKind.MiningOutpost, owner, 0);
            var close = e.Add("Close Depot", NearPort, SettlementKind.Station, owner, 0);
            close.Supply[(int)Good.Water] = 1000;
            close.Stock[(int)Good.Water] = 30000;
            var far = e.Add("Far Depot", FarPort, SettlementKind.Station, owner, 0);
            far.Supply[(int)Good.Water] = 1000;
            far.Stock[(int)Good.Water] = 30000;
            foreach (var s in e.Settlements) s.RefreshPrices();
            e.EstimateTrip = (s, from, to, jd) => to == NearPort ? new TripEstimate(1, 50, 0) : new TripEstimate(4, 60, 0);
            var ship = Freighter();
            ship.Propellant = 800;
            Assert.That(e.PlanTrade(ship, Home, 0), Is.EqualTo(NearPort));
            Assert.That(ship.Cargo, Does.Contain("propellant").And.Contain("Close Depot"));
        }

        // ---------------------------------------------------------------- tolls

        [Test]
        public void ACivilianShipPaysItsLaneTollToWhoeverKeepsTheRamps()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            var w = World.CreateDefault(eph, StartJd);
            int earth = eph.Catalog["Earth"].Index, mars = eph.Catalog["Mars"].Index;
            Assert.That(w.LaneKeeper(earth), Is.SameAs(w.Sol));
            var lane = w.Fleet.Lanes.Lanes.First(l => (l.A == earth && l.B == mars) || (l.A == mars && l.B == earth));
            var trader = w.Fleet.Ships.First(s => s.Role == ShipRole.Trader && s.Faction == w.Independents && !s.Destroyed);
            Assert.That(Economy.LaneToll(trader.MassTonnes), Is.EqualTo(Economy.LaneTollBase + Economy.LaneTollPerTonne * trader.MassTonnes));
            // A bulk freighter rides at its bulk share of that (U.SPC-T482).
            double toll = Economy.LaneToll(trader.Spec.Class, trader.MassTonnes);
            Assert.That(toll, Is.EqualTo(Economy.LaneToll(trader.MassTonnes) * BulkFreighters.LaneTollShareOf(trader.Spec.Class)).Within(1e-6));
            double payer = trader.Faction.Treasury, keeper = w.Sol.Treasury;
            w.Fleet.LaneJoined(trader, new LaneHop(lane, earth, mars), StartJd);
            Assert.That(trader.Faction.Treasury, Is.EqualTo(payer - toll).Within(1e-3));
            Assert.That(w.Sol.Treasury, Is.EqualTo(keeper + toll).Within(1e-3));

            // Navies ride free.
            var warship = w.Fleet.Ships.First(s => s.Spec.IsMilitary && s.Faction == w.Sol);
            keeper = w.Sol.Treasury;
            w.Fleet.LaneJoined(warship, new LaneHop(lane, earth, mars), StartJd);
            Assert.That(w.Sol.Treasury, Is.EqualTo(keeper));
        }

        [Test]
        public void TheDefaultSystemsMerchantsKeepTheirTanksUpAndNoneFreeze()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            var w = World.CreateDefault(eph, StartJd);
            for (double jd = StartJd; jd < StartJd + 60; jd += 0.25) w.Update(jd);
            var civil = w.Fleet.Ships.Where(s => !s.Destroyed && w.BurnsPropellant(s)).ToList();
            Assert.That(civil, Is.Not.Empty);
            Assert.That(civil.Average(s => s.PropellantFraction), Is.GreaterThan(0.6), "ships refuel in port");
            Assert.That(civil.Count(s => s.OutOfPropellant), Is.LessThanOrEqualTo(civil.Count / 10), "running dry is the exception");
            Assert.That(w.Fleet.Ships.Where(s => w.Fleet.BurnsPropellant(s)).All(s => !s.Spec.IsMilitary), Is.True, "navies' own supply is not drawn from the markets");
            Assert.That(w.Economy.Settlements.Sum(s => s.PropellantDemand[(int)Good.Water] + s.PropellantDemand[(int)Good.Hydrocarbons]), Is.GreaterThan(0),
                "ports see propellant demand");
        }

        // ---------------------------------------------------------------- the player's own tanks

        [Test]
        public void HandFlownThrustIsPaidForAndADryHullCoastsOn()
        {
            var ship = Freighter();
            var velBefore = new DVec3(0.1, 0, 0);
            var posBefore = DVec3.Zero;
            var vel = new DVec3(0.1, 0.03, 0);
            var pos = posBefore + vel;
            double start = ship.Propellant;
            Propulsion.ApplyManoeuvre(ship, ref pos, ref vel, posBefore, velBefore, 1);
            Assert.That(vel, Is.EqualTo(new DVec3(0.1, 0.03, 0)), "a fuelled ship flies the manoeuvre");
            Assert.That(start - ship.Propellant, Is.EqualTo(Propulsion.PropellantBurned(0.03, ship.ExhaustVelocityKmS, ship.DryMassTonnes + start)).Within(1e-9));

            ship.Propellant = 0;
            vel = new DVec3(0.3, 0, 0);
            pos = new DVec3(0.3, 0, 0);
            Propulsion.ApplyManoeuvre(ship, ref pos, ref vel, posBefore, velBefore, 1);
            Assert.That(vel, Is.EqualTo(velBefore), "dry, it keeps the velocity it had");
            Assert.That(pos.X, Is.EqualTo(0.1).Within(1e-12), "and drifts on with it");
        }

        [Test]
        public void APilotCanGiveADryShipSomeOfTheirTanks()
        {
            var giver = Freighter();
            var dry = Freighter();
            dry.Propellant = 0;
            double moved = Propulsion.Transfer(giver, dry, 1800);
            Assert.That(moved, Is.EqualTo(1800));
            Assert.That(giver.Propellant, Is.EqualTo(6000 - 1800));
            Assert.That(dry.Propellant, Is.EqualTo(1800));
            giver.Propellant = 10;
            Assert.That(Propulsion.Transfer(giver, dry, 1800), Is.EqualTo(10), "only what it has");
        }

        [Test]
        public void ATankerChargesACallOutFeeAndAPremiumAndAShipKeepsItsTanksThroughAJumpGate()
        {
            Assert.That(Propulsion.TankerFee(1000), Is.GreaterThan(1000 * Propulsion.PropellantBasePrice * 2));

            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Systems[0];
            var ac = g.Systems[1];
            var ship = sol.World.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort);
            ship.Propellant = 1234;

            Assert.That(GateTransit.TryPerform(sol.GateTo(ac), sol.GateTo(ac).PositionKm, ship, sol.World, StartJd, out var arrived), Is.True);

            // The same hull crosses (U.SPC-T453, never a copy), tanks and all.
            Assert.That(arrived, Is.SameAs(ship));
            Assert.That(arrived.Propellant, Is.EqualTo(1234));
        }
    }
}
