using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// The system's markets. Every settlement produces through its facilities (each limited by its
    /// scarcest input and by warehouse space), feeds its people, and prices every good from its stock
    /// against what it uses. Merchant ships do the rest: at each port a trader looks for the cargo and
    /// destination that earn the most per day of travel, counting deliveries already on the way, so
    /// goods flow from surplus to shortage on their own. Ships' profits go to their faction, ports tax
    /// the trade they host, and shipyards turn alloys, electronics and machinery into new hulls.
    /// </summary>
    public sealed class Economy
    {
        public readonly List<Settlement> Settlements = new List<Settlement>();
        public readonly Diplomacy Diplomacy;

        /// <summary>Economic time; advanced in ticks by <see cref="AdvanceTo"/>.</summary>
        public double Jd { get; private set; }

        /// <summary>Estimated days for a ship to fly between two bodies (ship, from, to, departure date).</summary>
        public Func<Ship, int, int, double, double> EstimateTravelDays;

        /// <summary>Extra cost a trader puts on a body (pirates, battles), as a fraction of a trip's value.</summary>
        public Func<int, double> Danger;

        /// <summary>Called when a yard has banked enough to lay down a hull. Return the class to build, or null to wait.</summary>
        public Func<Settlement, ShipClass?> ChooseBuild;

        /// <summary>Raised when a yard completes a hull; the world puts it into service.</summary>
        public Action<Settlement, ShipClass, double> ShipBuilt;

        /// <summary>Port tax on every sale and purchase, paid to the port's owner.</summary>
        public const double PortTax = 0.04;

        public const double TickDays = 0.25;

        readonly Random rng;
        double nextReviewJd;

        public Economy(Diplomacy diplomacy, double startJd, int seed = 2077)
        {
            Diplomacy = diplomacy;
            Jd = startJd;
            nextReviewJd = startJd + 10;
            rng = new Random(seed);
        }

        public Settlement Add(string name, int body, SettlementKind kind, Faction owner, double population)
        {
            var s = new Settlement { Id = Settlements.Count, Name = name, Body = body, Kind = kind, Owner = owner, Population = population, InitialPopulation = population };
            Settlements.Add(s);
            return s;
        }

        /// <summary>Recomputes every settlement's full-capacity demand and supply and stocks it at its target cover.</summary>
        public void Initialise()
        {
            foreach (var s in Settlements)
            {
                RecomputeFlows(s);
                for (int i = 0; i < GoodInfo.Count; i++)
                {
                    s.Stock[i] = s.Target((Good)i) * (0.8 + rng.NextDouble() * 0.5);
                    s.Satisfaction[i] = 1;
                }
                s.RefreshPrices();
            }
        }

        public IEnumerable<Settlement> At(int body)
        {
            foreach (var s in Settlements)
                if (s.Body == body) yield return s;
        }

        // ---------------------------------------------------------------- production and consumption

        /// <summary>Runs the markets forward to <paramref name="jd"/> in ticks of about <see cref="TickDays"/>.</summary>
        public void AdvanceTo(double jd)
        {
            double gap = jd - Jd;
            if (gap <= 0) return;
            // Very large jumps (a year per second) take coarser ticks; every update is stable at any step.
            double tick = Math.Max(TickDays, gap / 600);
            while (Jd < jd)
            {
                double dt = Math.Min(tick, jd - Jd);
                Tick(dt);
                Jd += dt;
                if (Jd >= nextReviewJd)
                {
                    Review();
                    nextReviewJd = Jd + 10;
                }
            }
        }

        void Tick(double dt)
        {
            foreach (var s in Settlements)
            {
                RecomputeFlows(s);
                Produce(s, dt);
                Consume(s, dt);
                if (s.Kind == SettlementKind.Haven) Fence(s, dt);
                s.DaysSinceCapture += dt;
                s.RefreshPrices();
            }
        }

        static void RecomputeFlows(Settlement s)
        {
            Array.Clear(s.Demand, 0, s.Demand.Length);
            Array.Clear(s.Supply, 0, s.Supply.Length);
            foreach (var (g, perPerson) in Settlement.PersonNeeds) s.Demand[(int)g] += perPerson * s.Population;
            for (int i = 0; i < GoodInfo.Count; i++) s.Demand[i] += s.ExtraDemand[i];
            foreach (var f in s.Facilities)
            {
                foreach (var (g, rate) in f.Recipe.Inputs) s.Demand[(int)g] += rate * f.Level;
                foreach (var (g, rate) in f.Recipe.Outputs) s.Supply[(int)g] += rate * f.Level;
            }
        }

        void Produce(Settlement s, double dt)
        {
            // A recently captured settlement is in disarray: output recovers over 60 days.
            double disruption = Math.Min(1, 0.25 + s.DaysSinceCapture / 60);
            foreach (var f in s.Facilities)
            {
                // Life support keeps running through a change of owner; industry does not.
                bool lifeSupport = f.Recipe.Kind == IndustryKind.Farm || (f.Recipe.Kind == IndustryKind.Mine && f.Recipe.Outputs[0].good == Good.Water);
                double run = lifeSupport ? 1 : disruption, efficiency = 1;
                var inputs = f.Recipe.Inputs;
                for (int k = 0; k < inputs.Length; k++)
                {
                    double need = inputs[k].perDay * f.Level * dt;
                    if (need <= 0) continue;
                    double have = Math.Min(1, s.Stock[(int)inputs[k].good] / need);
                    if (f.Recipe.Limiting[k]) run = Math.Min(run, have);
                    else if (!lifeSupport) efficiency = Math.Min(efficiency, Recipe.MinorFloor + (1 - Recipe.MinorFloor) * have);
                }
                foreach (var (g, rate) in f.Recipe.Outputs)
                {
                    // Full warehouses throttle production rather than overflow.
                    double room = s.Capacity(g) - s.Stock[(int)g];
                    double make = rate * f.Level * dt * efficiency;
                    if (make > 0) run = Math.Min(run, Math.Max(0, room) / make);
                }
                run = Math.Max(0, Math.Min(1, run));
                f.Utilisation = run * efficiency;
                foreach (var (g, rate) in f.Recipe.Inputs) s.Stock[(int)g] = Math.Max(0, s.Stock[(int)g] - rate * f.Level * dt * run * efficiency);
                foreach (var (g, rate) in f.Recipe.Outputs) s.Stock[(int)g] += rate * f.Level * dt * run * efficiency;
                if (f.Recipe.Kind == IndustryKind.Shipyard)
                {
                    double points = f.Recipe.Inputs[0].perDay * f.Level * dt * run;
                    s.BuildPoints = Math.Min(s.BuildPoints + points, 400000);
                }
            }
        }

        static void Consume(Settlement s, double dt)
        {
            for (int i = 0; i < s.Satisfaction.Length; i++) s.Satisfaction[i] = 1;
            foreach (var (g, perPerson) in Settlement.PersonNeeds)
            {
                int i = (int)g;
                double need = perPerson * s.Population * dt;
                if (need <= 0) continue;
                double eaten = Math.Min(need, s.Stock[i]);
                s.Stock[i] -= eaten;
                s.Satisfaction[i] = eaten / need;
            }

            // Fed and watered settlements grow up to 4% a year, less without medicine (down to a slow
            // decline of about 5% a year with none); hunger or thirst shrinks them fast, to a hard core.
            double sustenance = Math.Min(s.Satisfaction[(int)Good.Food], s.Satisfaction[(int)Good.Water]);
            double health = s.Satisfaction[(int)Good.MedicalSupplies];
            double rate = sustenance >= 0.98 ? 0.00011 * (0.5 + 0.5 * health) - (1 - health) * 0.0002 : (sustenance - 0.98) * 0.004;
            s.Population = Math.Max(s.InitialPopulation * 0.25, Math.Min(s.InitialPopulation * 4, s.Population * (1 + rate * dt)));
        }

        /// <summary>A haven's fences smuggle loot onward to the black market: stock drains, the owner is paid.</summary>
        static void Fence(Settlement s, double dt)
        {
            double share = 1 - Math.Exp(-0.03 * dt);
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                double moved = Math.Max(0, s.Stock[i] - s.Target((Good)i)) * share;
                if (moved <= 0) continue;
                s.Stock[i] -= moved;
                s.Owner.Treasury += moved * GoodInfo.Of((Good)i).BasePrice * 0.5;
            }
        }

        /// <summary>Every 10 days: facilities that sell dear expand, those glutted shrink; yards lay down hulls.</summary>
        void Review()
        {
            foreach (var s in Settlements)
            {
                foreach (var f in s.Facilities)
                {
                    if (f.Recipe.Kind == IndustryKind.Shipyard) continue;
                    double margin = Margin(s, f.Recipe);
                    double step = 0;
                    if (margin > 1.4 && f.Utilisation > 0.8) step = 0.03;
                    else if (margin < 0.7 || f.Utilisation < 0.2) step = -0.02;
                    f.Level = Math.Max(f.BaseLevel * 0.4, Math.Min(f.BaseLevel * 3, f.Level * (1 + step)));
                }

                var yard = s.Shipyard;
                if (yard == null || ChooseBuild == null) continue;
                var cls = ChooseBuild(s);
                s.OnSlip = cls;
                if (cls == null) continue;
                double cost = HullCost(cls.Value);
                if (s.BuildPoints < cost) continue;
                double price = HullPrice(cls.Value);
                if (s.Owner.Treasury < price) continue;
                s.BuildPoints -= cost;
                s.Owner.Treasury -= price;
                ShipBuilt?.Invoke(s, cls.Value, Jd);
            }
        }

        /// <summary>Value of a recipe's outputs over its inputs at local prices (mines count only their running costs).</summary>
        static double Margin(Settlement s, Recipe r)
        {
            double input = 0, output = 0;
            foreach (var (g, rate) in r.Inputs) input += rate * s.Price[(int)g];
            foreach (var (g, rate) in r.Outputs) output += rate * s.Price[(int)g];
            return input <= 0 ? 2 : output / input;
        }

        /// <summary>Build points (tonnes of alloys through the yard) to lay down a hull.</summary>
        public static double HullCost(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier: return 90000;
                case ShipClass.Battleship: return 70000;
                case ShipClass.Cruiser: return 30000;
                case ShipClass.Destroyer: return 12000;
                case ShipClass.Freighter: return 9000;
                case ShipClass.Miner: return 7000;
                case ShipClass.Courier: return 1500;
                default: return 3000;
            }
        }

        /// <summary>Credits the owner pays the yard (crew, weapons, fit-out) on top of the materials.</summary>
        public static double HullPrice(ShipClass c) => HullCost(c) * 1200;

        // ---------------------------------------------------------------- trade

        public static double CapacityOf(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Freighter: return 20000;
                case ShipClass.Miner: return 30000;
                case ShipClass.Courier: return 400;
                default: return 0;
            }
        }

        /// <summary>The goods a hull is fitted to carry: bulk holds for haulers, secure holds for couriers.</summary>
        public static bool Carries(ShipClass c, Good g)
        {
            var cat = GoodInfo.Of(g).Category;
            switch (c)
            {
                case ShipClass.Freighter: return true;
                case ShipClass.Miner: return cat == GoodCategory.Mineral || g == Good.Water || g == Good.Nitrogen || g == Good.Hydrocarbons;
                case ShipClass.Courier:
                    return g == Good.MedicalSupplies || g == Good.Electronics || g == Good.Platinum || g == Good.RareEarths || g == Good.HeliumThree;
                default: return false;
            }
        }

        static double RunningCostPerDay(ShipClass c) => c == ShipClass.Courier ? 400 : c == ShipClass.Miner ? 1200 : 1500;

        public bool CanTrade(Ship ship, Settlement s) => Diplomacy.CanDock(ship.Faction, s.Owner);

        /// <summary>Sells whatever is in the hold at the best market at the body the ship has reached.</summary>
        public void OnArrive(Ship ship, int body, double jd)
        {
            if (!ship.HasCargo) return;
            Settlement best = null;
            double bestValue = 0;
            foreach (var s in At(body))
            {
                if (!CanTrade(ship, s) || s.Demand[(int)ship.CargoGood] <= 0) continue;
                double v = s.SaleValue(ship.CargoGood, ship.CargoTonnes);
                if (s == ship.CargoDestination) v *= 1.05;
                if (v > bestValue)
                {
                    bestValue = v;
                    best = s;
                }
            }
            Release(ship);
            if (best == null)
            {
                ship.CargoDestination = null;
                return; // Nobody here will buy; the trader carries it on.
            }
            Sell(ship, best, bestValue);
        }

        void Sell(Ship ship, Settlement s, double value)
        {
            int g = (int)ship.CargoGood;
            s.Stock[g] += ship.CargoTonnes;
            double tax = value * PortTax;
            s.Owner.Treasury += tax;
            if (s == ship.CargoDestination && ship.CargoBonus > 0)
            {
                s.Owner.Treasury -= ship.CargoBonus;
                value += ship.CargoBonus;
            }
            ship.CargoBonus = 0;
            double profit = value - tax - ship.CargoCost;
            ship.Credits += profit;
            ship.Faction.Treasury += value - tax;
            s.TradeVolumeCredits += value;
            ship.HasCargo = false;
            ship.CargoTonnes = 0;
            ship.CargoCost = 0;
            ship.CargoDestination = null;
            ship.Cargo = null;
            s.RefreshPrices();
        }

        /// <summary>Cancels a ship's booking of its cargo at its destination (it arrived, died or changed plans).</summary>
        public void Release(Ship ship)
        {
            if (ship.CargoDestination != null && ship.HasCargo)
                ship.CargoDestination.Inbound[(int)ship.CargoGood] = Math.Max(0, ship.CargoDestination.Inbound[(int)ship.CargoGood] - ship.CargoTonnes);
        }

        /// <summary>Rebuilds every market's book of deliveries on the way from what ships are actually carrying.</summary>
        public void RecountInbound(IEnumerable<Ship> ships)
        {
            foreach (var s in Settlements) Array.Clear(s.Inbound, 0, s.Inbound.Length);
            foreach (var ship in ships)
                if (!ship.Destroyed && ship.HasCargo && ship.CargoDestination != null)
                    ship.CargoDestination.Inbound[(int)ship.CargoGood] += ship.CargoTonnes;
        }

        struct Option
        {
            public Settlement From, To;
            public Good Good;
            public double Tonnes, Score, Cost, Bonus;
            public bool Empty;
        }

        /// <summary>
        /// Picks a trader's next port from <paramref name="here"/> and loads its cargo: the trade with the
        /// best profit per day, among the top few (so the fleet does not stampede), or an empty run to
        /// where the best trades start. Returns -1 if nothing is worth doing.
        /// </summary>
        public int PlanTrade(Ship ship, int here, double jd)
        {
            var cls = ship.Spec.Class;
            var options = new List<Option>();

            if (ship.HasCargo)
            {
                // Already loaded (nobody here wanted it): find where it sells best.
                foreach (var to in Settlements)
                {
                    if (to.Body == here || !CanTrade(ship, to) || to.Demand[(int)ship.CargoGood] <= 0) continue;
                    double days = TravelDays(ship, here, to.Body, jd);
                    double value = to.SaleValue(ship.CargoGood, ship.CargoTonnes) * (1 - DangerAt(to.Body));
                    options.Add(new Option { To = to, Good = ship.CargoGood, Tonnes = ship.CargoTonnes, Score = (value - days * RunningCostPerDay(cls)) / days });
                }
                if (options.Count == 0) return -1;
                var pick = Choose(options);
                Release(ship);
                ship.CargoBonus = 0;
                ship.CargoDestination = pick.To;
                pick.To.Inbound[(int)pick.Good] += pick.Tonnes;
                ship.Cargo = CargoText(ship);
                return pick.To.Body;
            }

            foreach (var from in At(here))
            {
                if (!CanTrade(ship, from)) continue;
                AddLoadedOptions(ship, from, here, jd, 0, options);
            }

            if (options.Count == 0 || Best(options) <= 0)
            {
                // Nothing worth carrying from here: reposition, empty, to where the trades are.
                foreach (var from in Settlements)
                {
                    if (from.Body == here || !CanTrade(ship, from)) continue;
                    double leg = TravelDays(ship, here, from.Body, jd);
                    var onward = new List<Option>();
                    AddLoadedOptions(ship, from, from.Body, jd + leg, leg, onward);
                    double best = Best(onward);
                    if (best > 0) options.Add(new Option { To = from, Score = best * 0.85, Empty = true });
                }
            }

            options.RemoveAll(o => o.Score <= 0);
            if (options.Count == 0) return -1;
            var choice = Choose(options);
            if (!choice.Empty) Load(ship, choice);
            else ship.Cargo = "Empty, repositioning to " + choice.To.Name;
            return choice.To.Body;
        }

        void AddLoadedOptions(Ship ship, Settlement from, int fromBody, double jd, double extraDays, List<Option> into)
        {
            var cls = ship.Spec.Class;
            double capacity = ship.CapacityTonnes;
            for (int g = 0; g < GoodInfo.Count; g++)
            {
                var good = (Good)g;
                if (!Carries(cls, good)) continue;
                double available = from.Available(good);
                double minimum = Math.Min(capacity * 0.02, 20);
                if (available < minimum) continue;
                foreach (var to in Settlements)
                {
                    if (to.Body == fromBody || !CanTrade(ship, to)) continue;
                    double tonnes = Math.Min(Math.Min(capacity, available), to.Absorb(good));
                    if (tonnes < minimum) continue;
                    double cost = from.PurchaseCost(good, tonnes);
                    double days = TravelDays(ship, fromBody, to.Body, jd) + extraDays;
                    double bonus = ContractBonus(to, good, tonnes);
                    double value = (SaleEstimate(to, good, tonnes) + bonus) * (1 - DangerAt(to.Body));
                    double profit = value - cost - days * RunningCostPerDay(cls);
                    into.Add(new Option { From = from, To = to, Good = good, Tonnes = tonnes, Cost = cost, Bonus = bonus, Score = profit / days });
                }
            }
        }

        /// <summary>
        /// Owners post supply contracts for essentials their settlements are short of: a flat fee per
        /// run plus a premium per tonne, scaled by how short they are, paid on delivery.
        /// </summary>
        static double ContractBonus(Settlement to, Good g, double tonnes)
        {
            if (GoodInfo.Of(g).Category != GoodCategory.Essential || to.Owner.IsPirate || to.Owner.Treasury <= 0) return 0;
            double expected = to.Stock[(int)g] + to.Inbound[(int)g];
            double target = to.Target(g);
            if (target <= 0 || expected >= target * 0.7) return 0;
            double shortfall = 1 - expected / (target * 0.7);
            return shortfall * (600000 + tonnes * GoodInfo.Of(g).BasePrice * 1.5);
        }

        /// <summary>What a load will fetch once the deliveries already heading there have landed.</summary>
        static double SaleEstimate(Settlement to, Good g, double tonnes)
        {
            const int steps = 4;
            double value = 0, stock = to.Stock[(int)g] + to.Inbound[(int)g];
            for (int k = 0; k < steps; k++)
            {
                double chunk = tonnes / steps;
                value += to.PriceAt(g, stock + chunk * 0.5) * chunk;
                stock += chunk;
            }
            return value * 0.97 * (1 - PortTax);
        }

        void Load(Ship ship, Option o)
        {
            int g = (int)o.Good;
            o.From.Stock[g] = Math.Max(0, o.From.Stock[g] - o.Tonnes);
            o.From.Owner.Treasury += o.Cost * PortTax;
            o.From.TradeVolumeCredits += o.Cost;
            o.To.Inbound[g] += o.Tonnes;
            ship.HasCargo = true;
            ship.CargoGood = o.Good;
            ship.CargoTonnes = o.Tonnes;
            ship.CargoCost = o.Cost * (1 + PortTax);
            ship.Faction.Treasury -= ship.CargoCost;
            ship.CargoBonus = o.Bonus;
            ship.CargoDestination = o.To;
            ship.Cargo = CargoText(ship);
            o.From.RefreshPrices();
        }

        public static string CargoText(Ship ship)
        {
            if (!ship.HasCargo) return null;
            return ship.CargoTonnes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " t " + GoodInfo.Of(ship.CargoGood).Name +
                   (ship.CargoDestination != null ? " for " + ship.CargoDestination.Name : "");
        }

        double TravelDays(Ship ship, int from, int to, double jd) =>
            Math.Max(0.05, EstimateTravelDays != null ? EstimateTravelDays(ship, from, to, jd) : 10) + 1.0;

        double DangerAt(int body) => Danger != null ? Math.Max(0, Math.Min(0.9, Danger(body))) : 0;

        static double Best(List<Option> options)
        {
            double best = double.NegativeInfinity;
            foreach (var o in options) best = Math.Max(best, o.Score);
            return best;
        }

        /// <summary>A weighted pick among the best few options, so traders spread across good routes.</summary>
        Option Choose(List<Option> options)
        {
            options.Sort((a, b) => b.Score.CompareTo(a.Score));
            int n = Math.Min(4, options.Count);
            double top = Math.Max(options[0].Score, 1e-9);
            double total = 0;
            var weights = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = Math.Max(0, options[i].Score / top);
                weights[i] = w * w * w;
                total += weights[i];
            }
            double pick = rng.NextDouble() * total;
            for (int i = 0; i < n; i++)
            {
                pick -= weights[i];
                if (pick <= 0) return options[i];
            }
            return options[0];
        }

        // ---------------------------------------------------------------- player trade

        /// <summary>Result of one manual buy/sell click, for the HUD to pick a toast from. <see cref="Ok"/>
        /// also covers a partial fill (ran out of stock, credits or hold room partway through a multi-crate
        /// request), since part of the trade still went through.</summary>
        public enum TradeOutcome { Ok, CannotDock, NoStock, NoRoom, CantAfford, NothingToSell }

        /// <summary>
        /// Buys up to <paramref name="crates"/> standard crates of <paramref name="good"/> (see
        /// <see cref="CargoFootprint"/>) at <paramref name="s"/> for the player's piloted <paramref name="ship"/>:
        /// walks the price up and drains stock exactly as <see cref="Load"/> does for an AI trader's purchase,
        /// paying from the ship's own faction treasury, and places each crate into the ship's personal
        /// <see cref="CargoHold"/> -- never the single-slot cargo fields above, which belong to the AI trade
        /// loop and a player-flown ship does not use. Stops at the first crate that cannot be bought; reports
        /// how many actually went through in <paramref name="bought"/>.
        /// </summary>
        public TradeOutcome BuyIntoHold(Ship ship, Settlement s, Good good, int crates, out int bought)
        {
            bought = 0;
            if (!CanTrade(ship, s)) return TradeOutcome.CannotDock;
            double unitTonnes = CargoFootprint.Of(good).Tonnes;
            for (int i = 0; i < crates; i++)
            {
                if (s.Available(good) < unitTonnes) return bought > 0 ? TradeOutcome.Ok : TradeOutcome.NoStock;
                double cost = s.PurchaseCost(good, unitTonnes);
                double total = cost * (1 + PortTax);
                if (ship.Faction.Treasury < total) return bought > 0 ? TradeOutcome.Ok : TradeOutcome.CantAfford;
                if (ship.PersonalHold.AddFirstFit(good) == null) return bought > 0 ? TradeOutcome.Ok : TradeOutcome.NoRoom;

                s.Stock[(int)good] = Math.Max(0, s.Stock[(int)good] - unitTonnes);
                s.Owner.Treasury += cost * PortTax;
                s.TradeVolumeCredits += cost;
                ship.Faction.Treasury -= total;
                bought++;
            }
            if (bought > 0) s.RefreshPrices();
            return TradeOutcome.Ok;
        }

        /// <summary>
        /// Sells up to <paramref name="crates"/> crates of <paramref name="good"/> out of the player's
        /// piloted <paramref name="ship"/>'s own <see cref="CargoHold"/> at <paramref name="s"/>: walks the
        /// price down and credits treasuries exactly as <see cref="Sell"/> does for an AI trader's cargo.
        /// Stops at the first crate of that good no longer in the hold; reports how many actually sold in
        /// <paramref name="sold"/>.
        /// </summary>
        public TradeOutcome SellFromHold(Ship ship, Settlement s, Good good, int crates, out int sold)
        {
            sold = 0;
            if (!CanTrade(ship, s)) return TradeOutcome.CannotDock;
            var hold = ship.PersonalHold;
            for (int i = 0; i < crates; i++)
            {
                CargoItem item = null;
                foreach (var it in hold.Items)
                    if (it.Good == good) { item = it; break; }
                if (item == null) return sold > 0 ? TradeOutcome.Ok : TradeOutcome.NothingToSell;

                double tonnes = item.Tonnes;
                double value = s.SaleValue(good, tonnes);
                double tax = value * PortTax;
                hold.Remove(item);
                s.Stock[(int)good] += tonnes;
                s.Owner.Treasury += tax;
                s.TradeVolumeCredits += value;
                ship.Faction.Treasury += value - tax;
                sold++;
            }
            if (sold > 0) s.RefreshPrices();
            return TradeOutcome.Ok;
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Systemwide average price relative to base for a good (1 = normal).</summary>
        public double PriceIndex(Good g)
        {
            double sum = 0;
            int n = 0;
            foreach (var s in Settlements)
            {
                if (s.Demand[(int)g] <= 0) continue;
                sum += s.Price[(int)g] / GoodInfo.Of(g).BasePrice;
                n++;
            }
            return n > 0 ? sum / n : 1;
        }

        /// <summary>Captures a settlement: the owner changes and production is disrupted for a while.</summary>
        public void Capture(Settlement s, Faction newOwner)
        {
            s.Owner = newOwner;
            s.DaysSinceCapture = 0;
            s.BuildPoints *= 0.3;
        }
    }
}
