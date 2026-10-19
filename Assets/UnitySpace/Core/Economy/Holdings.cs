using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnitySpace.Core
{
    /// <summary>What a hired captain does with an owned ship while the player is not flying it.</summary>
    public enum StandingOrderKind
    {
        /// <summary>Lie in port where it is.</summary>
        Hold,

        /// <summary>Work a fixed round of ports, at each stop buying what pays best at the next.</summary>
        TradeRoute,

        /// <summary>Cut ore on one of the player's mining claims and sell it wherever it fetches most.</summary>
        Mining,

        /// <summary>Shadow another ship port to port and fight beside it.</summary>
        Escort,

        /// <summary>Make the round of a list of waypoints, fighting whatever hostile it finds there.</summary>
        Patrol,
    }

    /// <summary>A captain's standing orders: what to do, and where (the stops of a route or patrol, a claim, a charge).</summary>
    public sealed class StandingOrder
    {
        public StandingOrderKind Kind;

        /// <summary>A trade route's ports or a patrol's waypoints (body indices), in order.</summary>
        public int[] Stops;

        /// <summary>The stop the ship is bound for (or will make for next).</summary>
        public int StopIndex;

        /// <summary>The mining claim worked (a <see cref="PropertyKind.MiningClaim"/>).</summary>
        public Property Claim;

        /// <summary>The ship escorted.</summary>
        public Ship Charge;

        public static StandingOrder Hold() => new StandingOrder { Kind = StandingOrderKind.Hold };
        public static StandingOrder TradeRoute(params int[] ports) => new StandingOrder { Kind = StandingOrderKind.TradeRoute, Stops = ports };
        public static StandingOrder Patrol(params int[] waypoints) => new StandingOrder { Kind = StandingOrderKind.Patrol, Stops = waypoints };
        public static StandingOrder Mining(Property claim) => new StandingOrder { Kind = StandingOrderKind.Mining, Claim = claim };
        public static StandingOrder Escort(Ship charge) => new StandingOrder { Kind = StandingOrderKind.Escort, Charge = charge };

        /// <summary>The orders in a line: "trade route Earth - Mars - Ceres", "escorting MV Plenty".</summary>
        public string Describe(BodyCatalog catalog)
        {
            string Stops(string sep)
            {
                if (this.Stops == null || this.Stops.Length == 0) return "nowhere";
                var names = new List<string>();
                foreach (int b in this.Stops) names.Add(catalog != null ? catalog[b].Name : b.ToString(CultureInfo.InvariantCulture));
                return string.Join(sep, names);
            }
            switch (Kind)
            {
                case StandingOrderKind.TradeRoute: return "trade route " + Stops(" - ");
                case StandingOrderKind.Patrol: return "patrol " + Stops(", ");
                case StandingOrderKind.Mining: return Claim == null ? "mining" : "mining " + Claim.Name;
                case StandingOrderKind.Escort: return Charge == null ? "escort" : "escorting " + Charge.Name;
                default: return "holding in port";
            }
        }

        public override string ToString() => Describe(null);
    }

    /// <summary>A captain the player has hired: a name, how good they are, and what they cost a day.</summary>
    public sealed class Captain
    {
        public string Name;

        /// <summary>0.5 (green) to 1.5 (a veteran): a better captain turns a ship round faster in port, and costs more.</summary>
        public double Skill;

        /// <summary>The captain's own pay a day, credits (the crew's is on top: <see cref="Economy.RunningCostPerDay"/>).</summary>
        public double WagePerDay;

        public double HiredJd;

        /// <summary>Days of pay owed and not paid: at <see cref="Holdings.QuitDays"/> the captain walks off.</summary>
        public int DaysUnpaid;

        public override string ToString() => Name;
    }

    /// <summary>One of the player's ships: the hull, who commands it, their orders and the ship's books.</summary>
    public sealed class OwnedShip
    {
        public Ship Ship;
        public Captain Captain;
        public StandingOrder Orders = StandingOrder.Hold();
        public double SinceJd, PricePaid;

        /// <summary>Its books, credits: trade profit and ore sold; propellant, tolls and tankers; wages; yard bills.</summary>
        public double Earned, Spent, Wages, Repairs;

        /// <summary>Ore cut from claims, tonnes, over its life.</summary>
        public double TonnesMined;

        /// <summary>Gone from the player's fleet: destroyed, taken, cast adrift or out through a gate.</summary>
        public bool Lost;
        public double LostJd;
        public string LostHow;

        /// <summary>The ship's running profit the books last caught up with.</summary>
        internal double SeenCredits;

        /// <summary>Mining time owed before it leaves the claim, days (set when it loads, spent as its dwell).</summary>
        internal double MiningDays;

        public double Net => Earned - Spent - Wages - Repairs;
        public string Name => Ship.Name;
        public override string ToString() => Name;
    }

    public enum PropertyKind
    {
        /// <summary>Storage at a port: goods bought into it wait there to be sold when the price is right.</summary>
        Warehouse,

        /// <summary>A share of a station or colony: a dividend of the port taxes its trade pays.</summary>
        StationShare,

        /// <summary>The right to cut one ore on one world: the player's ore haulers mine it under standing orders.</summary>
        MiningClaim,
    }

    /// <summary>Something the player owns that is not a ship.</summary>
    public sealed class Property
    {
        public PropertyKind Kind;

        /// <summary>The port a warehouse stands at, or the settlement a share is in (null for a claim).</summary>
        public Settlement Settlement;

        /// <summary>Where it is.</summary>
        public int Body;

        /// <summary>A station share: the fraction owned (0..1).</summary>
        public double Share;

        /// <summary>A claim: what it yields, and how fast a hauler on it cuts it, tonnes a day.</summary>
        public Good Ore;
        public double YieldPerDay;

        /// <summary>A warehouse: what it holds and how much, tonnes.</summary>
        public double CapacityTonnes;
        public readonly double[] Stock = new double[GoodInfo.Count];

        public double PricePaid, SinceJd;

        /// <summary>What it has brought in (dividends, ore sold off it) and cost to keep (rent, claim fees), credits.</summary>
        public double Income, Upkeep;

        /// <summary>Seized (a pirate took the port) or given up.</summary>
        public bool Lost;
        public double LostJd;

        /// <summary>The settlement's trade the dividend last caught up with.</summary>
        internal double SeenTradeVolume;

        public string Name { get; internal set; }

        public double StoredTonnes
        {
            get
            {
                double t = 0;
                foreach (var s in Stock) t += s;
                return t;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>A line in the player's books: when, what, and the credits in (+) or out (-).</summary>
    public sealed class LedgerEntry
    {
        public double Jd;
        public string Text;
        public double Amount;

        /// <summary>False for a loss of an asset (a hull, a cargo) rather than of cash from the account.</summary>
        public bool Cash = true;

        public override string ToString() => Text + " " + Amount.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The player's own fleet and property (U.SPC-T102). The player can buy hulls at any shipyard that will deal with
    /// them (or put a ship they have taken into their fleet), hire captains to fly them under standing orders -- a
    /// trade route, mining one of their claims, escorting another ship, a patrol -- and own property: a warehouse at
    /// a port, a share in a station, a mining claim. Owned ships fly under the player's house flag
    /// (<see cref="House"/>) and are simulated like any other: they plan real runs, burn real propellant, pay tolls,
    /// land through docking rings, are raided and fight. Every credit they make or lose goes to the house account
    /// (the house's treasury); every captain and crew is paid from it daily, and a captain unpaid for
    /// <see cref="QuitDays"/> walks off. A ship lost is a loss on the books at what it was worth.
    /// </summary>
    public sealed class Holdings
    {
        readonly World world;
        readonly Random rng;

        /// <summary>The flag the player's ships fly: lawful, at war with nobody, every pirate's prey.</summary>
        public readonly Faction House;

        public readonly List<OwnedShip> Ships = new List<OwnedShip>();
        public readonly List<Property> Properties = new List<Property>();

        /// <summary>The books, newest last (the latest <see cref="LedgerLength"/>).</summary>
        public readonly List<LedgerEntry> Ledger = new List<LedgerEntry>();

        /// <summary>Over the house's life, credits: everything its ships and property brought in, everything they cost to
        /// run, and the value of what was lost (hulls and cargoes).</summary>
        public double TotalEarned, TotalCosts, TotalLost;

        /// <summary>What the account should hold by the books (every cash line summed), against which the daily round
        /// finds money that moved without a line of its own (a ransom paid, a rescue fee, prize money).</summary>
        public double BookBalance { get; private set; }

        readonly Dictionary<Ship, OwnedShip> byShip = new Dictionary<Ship, OwnedShip>();
        double nextTickJd = double.NaN;
        int captainNames;

        // ---------------------------------------------------------------- tuning

        public const int LedgerLength = 300;

        /// <summary>The books are kept, wages paid and dividends drawn once a day.</summary>
        public const double TickDays = 1;

        /// <summary>A captain's pay a day at skill 1, credits (more for a warship's).</summary>
        public const double CaptainWage = 600;

        /// <summary>Days unpaid before a captain walks off the ship.</summary>
        public const int QuitDays = 5;

        /// <summary>Damage at which a captain takes the ship to a yard, and the share a yard mends a day (at the hull's
        /// price times <see cref="RepairRate"/> for each share).</summary>
        public const double RepairDamage = 0.25, RepairPerDay = 0.2, RepairRate = 0.3;

        /// <summary>A warehouse: its price, rent a day, and what it holds.</summary>
        public const double WarehousePrice = 2e6, WarehouseRentPerDay = 1500, WarehouseTonnes = 20000;

        /// <summary>What a settlement is worth per resident (a share is priced at that fraction of it).</summary>
        public const double ValuePerResident = 250;

        /// <summary>A claim: its price per tonne-a-day of yield at the ore's base price, and its fee a day.</summary>
        public const double ClaimPricePerYield = 60, ClaimFeePerDay = 300;

        /// <summary>How fast a hauler on a claim cuts ore, tonnes a day, before the ore's richness (rarer ores come slower).</summary>
        public const double BaseYieldPerDay = 4000;

        static readonly string[] CaptainNames =
        {
            "Ada Okonkwo", "Bram Teller", "Calla Voss", "Dmitri Hale", "Esme Rourke", "Farouk Bey", "Greta Lind", "Hollis Crane",
            "Ines Navarro", "Jory Kade", "Kaito Mbeki", "Lena Strand", "Marek Oduya", "Nell Quarry", "Oren Sallow", "Priya Haldane",
        };

        public Holdings(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
            House = new Faction("Your fleet", "", new Rgb(0.45f, 0.95f, 0.55f));
            // No trained reserve of its own: crews sign on in port.
            House.Manpower = double.PositiveInfinity;
        }

        /// <summary>The house account, credits (the house's treasury).</summary>
        public double Balance => House.Treasury;

        Economy Economy => world.Economy;

        /// <summary>The house's record of <paramref name="ship"/>, while it is still the house's: one taken (or lost) since
        /// the last daily round no longer follows the house's orders, even before that round writes it off.</summary>
        public OwnedShip Of(Ship ship) =>
            ship != null && byShip.TryGetValue(ship, out var o) && !o.Lost && ship.Faction == House && !ship.Destroyed && !ship.Derelict && !ship.GateDeparted ? o : null;

        public bool Owns(Ship ship) => Of(ship) != null;

        public IEnumerable<Property> OfKind(PropertyKind kind)
        {
            foreach (var p in Properties)
                if (p.Kind == kind && !p.Lost) yield return p;
        }

        // ---------------------------------------------------------------- the account

        /// <summary>Moves credits from <paramref name="from"/>'s treasury (the purse of whatever the player flies) into the house account.</summary>
        public bool Deposit(Faction from, double amount, double jd)
        {
            if (from == null || from == House || amount <= 0 || from.Treasury < amount) return false;
            from.Treasury -= amount;
            House.Treasury += amount;
            Book(jd, "Deposited from the " + from.Name + "'s purse", amount, earned: false);
            return true;
        }

        /// <summary>Moves credits out of the house account to <paramref name="to"/>'s treasury.</summary>
        public bool Withdraw(Faction to, double amount, double jd)
        {
            if (to == null || to == House || amount <= 0 || House.Treasury < amount) return false;
            House.Treasury -= amount;
            to.Treasury += amount;
            Book(jd, "Withdrawn to the " + to.Name + "'s purse", -amount, earned: false);
            return true;
        }

        void Book(double jd, string text, double amount, bool earned = true, bool cash = true)
        {
            Ledger.Add(new LedgerEntry { Jd = jd, Text = text, Amount = amount, Cash = cash });
            if (cash) BookBalance += amount;
            if (Ledger.Count > LedgerLength) Ledger.RemoveRange(0, Ledger.Count - LedgerLength);
            if (!earned) return;
            if (!cash) TotalLost += -amount;
            else if (amount >= 0) TotalEarned += amount;
            else TotalCosts += -amount;
        }

        // ---------------------------------------------------------------- ships

        /// <summary>Whether <paramref name="yard"/> will sell the house a hull.</summary>
        public bool CanBuyAt(Settlement yard) =>
            yard != null && yard.HasShipyard && !yard.Fallen && yard.Owner != null && !yard.Owner.IsPirate && world.Diplomacy.CanDockAt(House, yard);

        /// <summary>
        /// Buys a new <paramref name="cls"/> off <paramref name="yard"/>'s slip at its price (<see cref="Economy.HullPrice"/>),
        /// paid from the house account to the yard's owner, and puts it into space there under the house flag, its
        /// crew signed on in port. It lies in port until a captain is hired. Null if the yard will not deal or the
        /// account cannot pay.
        /// </summary>
        public OwnedShip Buy(Settlement yard, ShipClass cls, string name, double jd)
        {
            if (!CanBuyAt(yard)) return null;
            double price = Economy.HullPrice(cls);
            if (House.Treasury < price) return null;
            House.Treasury -= price;
            yard.Owner.Treasury += price;
            var ship = world.Fleet.AddShip(string.IsNullOrEmpty(name) ? "Venture " + (Ships.Count + 1) : name, cls, House, RoleFor(StandingOrderKind.Hold, cls));
            ship.CapacityTonnes = Economy.CapacityOf(cls);
            ship.Ports = world.PortBodies();
            ship.Route = new[] { yard.Body };
            // The house's before it is in space: its first plan is its (captainless) own, not a merchant's.
            var owned = Add(ship, jd, price);
            world.Launch(ship, yard, jd);
            Book(jd, $"Bought the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name} at {yard.Name}", -price, earned: false);
            world.Log(jd, $"{yard.Name} sells the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name} to your fleet", WorldEventKind.Launch, yard.Body);
            world.NotifyAdded(ship);
            return owned;
        }

        /// <summary>
        /// Takes a ship the player already has (the hull they fly, a prize they claimed) into the house fleet: it
        /// changes to the house flag, its cargo and running profit with it. Null if it cannot (lost, adrift, an escort,
        /// a pirate's, or already the house's).
        /// </summary>
        public OwnedShip Enlist(Ship ship, double jd)
        {
            if (ship == null || ship.Destroyed || ship.Derelict || ship.GateDeparted || ship.IsEscort || ship.Battle != null) return null;
            if (Owns(ship) || ship.Faction.IsPirate) return null;
            if (ship.Faction != House)
            {
                // Not a crew-less hull any more: whatever escorts flew with it stay with their own flag.
                foreach (var s in world.Fleet.Ships)
                    if (s.Leader == ship) s.Leader = null;
                world.Economy.Release(ship);
                ship.CargoDestination = null;
                // Whatever is in its hold the house came by for nothing: all it fetches is profit.
                ship.CargoCost = 0;
                // Out of any trading company's books (what it took since their last review is still theirs): its takings are the house's now.
                world.Companies?.SettleOut(ship.Company, ship);
                ship.Company?.Ships.Remove(ship);
                ship.Company = null;
                ship.Faction = House;
            }
            if (ship.CapacityTonnes <= 0) ship.CapacityTonnes = Economy.CapacityOf(ship.Spec.Class);
            ship.Ports ??= world.PortBodies();
            ship.Role = RoleFor(StandingOrderKind.Hold, ship.Spec.Class);
            var owned = Add(ship, jd, 0);
            Book(jd, $"The {ship.Name} joins your fleet", 0, earned: false);
            world.Log(jd, $"The {ship.Name} joins your fleet", WorldEventKind.Info, ship.Orbit?.Body ?? -1);
            return owned;
        }

        OwnedShip Add(Ship ship, double jd, double price)
        {
            var owned = new OwnedShip { Ship = ship, SinceJd = jd, PricePaid = price, SeenCredits = ship.Credits };
            Ships.Add(owned);
            byShip[ship] = owned;
            if (double.IsNaN(nextTickJd)) nextTickJd = jd + TickDays;
            return owned;
        }

        /// <summary>Signs on a captain for <paramref name="owned"/> (replacing any it had). Their pay starts today.</summary>
        public Captain Hire(OwnedShip owned, double jd, double skill = double.NaN)
        {
            if (owned == null || owned.Lost) return null;
            if (double.IsNaN(skill)) skill = 0.6 + rng.NextDouble() * 0.8;
            skill = Math.Max(0.5, Math.Min(1.5, skill));
            int n = captainNames++;
            var c = new Captain
            {
                Name = CaptainNames[n % CaptainNames.Length] + (n >= CaptainNames.Length ? " " + (n / CaptainNames.Length + 1) : ""),
                Skill = skill,
                WagePerDay = CaptainWage * skill * (owned.Ship.Spec.IsMilitary ? 2 : 1),
                HiredJd = jd,
            };
            owned.Captain = c;
            Book(jd, $"Captain {c.Name} takes command of the {owned.Name}", 0, earned: false);
            return c;
        }

        /// <summary>Lets the captain go: the ship lies in port (unless the player flies it) until another is hired.</summary>
        public void Dismiss(OwnedShip owned, double jd)
        {
            if (owned?.Captain == null) return;
            Book(jd, $"Captain {owned.Captain.Name} leaves the {owned.Name}", 0, earned: false);
            owned.Captain = null;
        }

        /// <summary>
        /// Gives <paramref name="owned"/>'s captain standing orders; false if the ship cannot carry them out (a hull
        /// that cannot carry the claim's ore, a route of under two stops, escorting itself). They take effect when it
        /// next plans a trip.
        /// </summary>
        public bool Order(OwnedShip owned, StandingOrder orders)
        {
            if (owned == null || owned.Lost || orders == null || !CanFollow(owned.Ship, orders)) return false;
            owned.Orders = orders;
            orders.StopIndex = 0;
            owned.MiningDays = 0;
            owned.Ship.Role = RoleFor(orders.Kind, owned.Ship.Spec.Class);
            if (orders.Kind == StandingOrderKind.Patrol) owned.Ship.Route = orders.Stops;
            return true;
        }

        public static bool CanFollow(Ship ship, StandingOrder o)
        {
            switch (o.Kind)
            {
                case StandingOrderKind.TradeRoute:
                    return o.Stops != null && o.Stops.Length >= 2 && Economy.CapacityOf(ship.Spec.Class) > 0;
                case StandingOrderKind.Patrol:
                    return o.Stops != null && o.Stops.Length >= 1;
                case StandingOrderKind.Mining:
                    return o.Claim != null && !o.Claim.Lost && o.Claim.Kind == PropertyKind.MiningClaim && Economy.Carries(ship.Spec.Class, o.Claim.Ore);
                case StandingOrderKind.Escort:
                    return o.Charge != null && o.Charge != ship && !o.Charge.Destroyed;
                default:
                    return true;
            }
        }

        /// <summary>The role the fleet and markets treat a ship under these orders as: a trader or miner is a merchant
        /// (it sells in port and lands through rings); a patrol or escort is not.</summary>
        static ShipRole RoleFor(StandingOrderKind kind, ShipClass cls)
        {
            switch (kind)
            {
                case StandingOrderKind.TradeRoute: return ShipRole.Trader;
                case StandingOrderKind.Mining: return ShipRole.Miner;
                case StandingOrderKind.Patrol: return ShipRole.Patrol;
                case StandingOrderKind.Escort: return ShipRole.Escort;
                default: return Economy.CapacityOf(cls) > 0 ? ShipRole.Trader : ShipRole.Patrol;
            }
        }

        // ---------------------------------------------------------------- property

        /// <summary>Buys a warehouse at <paramref name="port"/> (one per port), paid to its owner.</summary>
        public Property BuyWarehouse(Settlement port, double jd)
        {
            if (port == null || port.Owner == null || port.Owner.IsPirate || !world.Diplomacy.CanDockAt(House, port)) return null;
            foreach (var p in OfKind(PropertyKind.Warehouse))
                if (p.Settlement == port) return null;
            if (House.Treasury < WarehousePrice) return null;
            House.Treasury -= WarehousePrice;
            port.Owner.Treasury += WarehousePrice;
            var w = new Property
            {
                Kind = PropertyKind.Warehouse, Settlement = port, Body = port.Body, CapacityTonnes = WarehouseTonnes,
                PricePaid = WarehousePrice, SinceJd = jd, Name = "warehouse at " + port.Name,
            };
            AddProperty(w, jd);
            return w;
        }

        /// <summary>What <paramref name="share"/> of <paramref name="s"/> costs.</summary>
        public static double SharePrice(Settlement s, double share) => s == null ? 0 : s.Population * ValuePerResident * share;

        /// <summary>
        /// Buys <paramref name="share"/> (up to half) of a station or colony from its owner: from then on the house is
        /// paid that share of the port taxes the settlement's trade pays its owner, daily. Null if it will not sell
        /// (a pirate's, a haven, more than half in other hands) or the account cannot pay.
        /// </summary>
        public Property BuyShare(Settlement s, double share, double jd)
        {
            if (s == null || s.Owner == null || s.Owner.IsPirate || s.Kind == SettlementKind.Haven || share <= 0) return null;
            Property held = null;
            foreach (var p in OfKind(PropertyKind.StationShare))
                if (p.Settlement == s) held = p;
            double already = held?.Share ?? 0;
            if (already + share > 0.5 + 1e-9) return null;
            double price = SharePrice(s, share);
            if (House.Treasury < price) return null;
            House.Treasury -= price;
            s.Owner.Treasury += price;
            if (held != null)
            {
                held.Share += share;
                held.PricePaid += price;
                held.Name = ShareName(s, held.Share);
                Book(jd, $"Bought {share:P0} more of {s.Name}", -price, earned: false);
                return held;
            }
            var prop = new Property
            {
                Kind = PropertyKind.StationShare, Settlement = s, Body = s.Body, Share = share, PricePaid = price, SinceJd = jd,
                SeenTradeVolume = s.TradeVolumeCredits, Name = ShareName(s, share),
            };
            AddProperty(prop, jd);
            return prop;
        }

        static string ShareName(Settlement s, double share) => share.ToString("P0", CultureInfo.InvariantCulture) + " share of " + s.Name;

        /// <summary>The tonnes a day a hauler cuts of <paramref name="ore"/> on a claim: rarer, dearer ores come slower
        /// (nickel-iron at the full <see cref="BaseYieldPerDay"/>, platinum-group metals at under a twentieth of it).</summary>
        public static double YieldOf(Good ore) => BaseYieldPerDay * Math.Sqrt(40 / Math.Max(1, GoodInfo.Of(ore).BasePrice));

        public static double ClaimPrice(Good ore) => YieldOf(ore) * GoodInfo.Of(ore).BasePrice * ClaimPricePerYield;

        /// <summary>
        /// Stakes a claim to cut <paramref name="ore"/> on <paramref name="body"/> (a world, moon or rock, never a star or a
        /// haven's rock), bought from the registry. Only an ore hauler or freighter that carries it can work it.
        /// Null if it cannot be staked (not a mineral or volatile, already claimed, no money).
        /// </summary>
        public Property StakeClaim(int body, Good ore, double jd)
        {
            var cat = GoodInfo.Of(ore).Category;
            if (cat != GoodCategory.Mineral && cat != GoodCategory.Volatile && ore != Good.Water) return null;
            if (body < 0 || body >= world.Catalog.Bodies.Count || world.Catalog[body].IsStar) return null;
            foreach (var s in Economy.At(body))
                if (s.Kind == SettlementKind.Haven) return null;
            foreach (var p in OfKind(PropertyKind.MiningClaim))
                if (p.Body == body && p.Ore == ore) return null;
            double price = ClaimPrice(ore);
            if (House.Treasury < price) return null;
            House.Treasury -= price;
            var claim = new Property
            {
                Kind = PropertyKind.MiningClaim, Body = body, Ore = ore, YieldPerDay = YieldOf(ore), PricePaid = price, SinceJd = jd,
                Name = GoodInfo.Of(ore).Name.ToLowerInvariant() + " claim on " + world.Catalog[body].Name,
            };
            AddProperty(claim, jd);
            return claim;
        }

        void AddProperty(Property p, double jd)
        {
            Properties.Add(p);
            Book(jd, "Bought the " + p.Name, -p.PricePaid, earned: false);
            world.Log(jd, "Your fleet buys the " + p.Name, WorldEventKind.Economy, p.Body);
            if (double.IsNaN(nextTickJd)) nextTickJd = jd + TickDays;
        }

        /// <summary>Buys <paramref name="tonnes"/> of <paramref name="good"/> from the warehouse's port into it, at the market's price
        /// with tax. Returns the tonnes bought (fewer if the market, the warehouse or the account runs short).</summary>
        public double StoreFromMarket(Property warehouse, Good good, double tonnes, double jd)
        {
            if (warehouse == null || warehouse.Lost || warehouse.Kind != PropertyKind.Warehouse || tonnes <= 0) return 0;
            var s = warehouse.Settlement;
            tonnes = Math.Min(tonnes, Math.Min(s.Available(good), warehouse.CapacityTonnes - warehouse.StoredTonnes));
            if (tonnes < 0.01) return 0;
            double price = s.PurchaseCost(good, tonnes);
            if (price * (1 + Economy.PortTax) > House.Treasury)
            {
                tonnes *= House.Treasury / (price * (1 + Economy.PortTax)) * 0.99;
                if (tonnes < 0.01) return 0;
                price = s.PurchaseCost(good, tonnes);
            }
            double cost = price * (1 + Economy.PortTax);
            s.Stock[(int)good] = Math.Max(0, s.Stock[(int)good] - tonnes);
            s.Owner.Treasury += price * Economy.PortTax;
            s.TradeVolumeCredits += price;
            House.Treasury -= cost;
            warehouse.Stock[(int)good] += tonnes;
            s.RefreshPrices();
            Book(jd, $"Stored {tonnes:N0} t of {GoodInfo.Of(good).Name.ToLowerInvariant()} in the {warehouse.Name}", -cost);
            return tonnes;
        }

        /// <summary>Sells <paramref name="tonnes"/> of what the warehouse holds into its port's market; returns the credits (after tax).</summary>
        public double SellFromStore(Property warehouse, Good good, double tonnes, double jd)
        {
            if (warehouse == null || warehouse.Lost || warehouse.Kind != PropertyKind.Warehouse) return 0;
            tonnes = Math.Min(tonnes, warehouse.Stock[(int)good]);
            var s = warehouse.Settlement;
            if (tonnes < 0.01 || s.Demand[(int)good] <= 0) return 0;
            double value = s.SaleValue(good, tonnes);
            double tax = value * Economy.PortTax;
            s.Stock[(int)good] += tonnes;
            s.Owner.Treasury += tax;
            s.TradeVolumeCredits += value;
            warehouse.Stock[(int)good] -= tonnes;
            House.Treasury += value - tax;
            warehouse.Income += value - tax;
            s.RefreshPrices();
            Book(jd, $"Sold {tonnes:N0} t of {GoodInfo.Of(good).Name.ToLowerInvariant()} out of the {warehouse.Name}", value - tax);
            return value - tax;
        }

        // ---------------------------------------------------------------- the daily round

        /// <summary>Keeps the books to <paramref name="jd"/>, a day at a time: each ship's takings and costs, losses, wages, yard
        /// bills, dividends, rent and claim fees.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextTickJd)) return;
            for (int guard = 0; guard < 4000 && nextTickJd <= jd; guard++)
            {
                Tick(nextTickJd);
                nextTickJd += TickDays;
            }
        }

        void Tick(double jd)
        {
            foreach (var o in Ships)
            {
                if (o.Lost) continue;
                Settle(o, jd);
                if (CheckLost(o, jd)) continue;
                PayWages(o, jd);
                RepairInPort(o, jd);
            }
            foreach (var p in Properties)
            {
                if (p.Lost) continue;
                switch (p.Kind)
                {
                    case PropertyKind.Warehouse:
                        if (Seized(p, jd)) break;
                        Charge(p, WarehouseRentPerDay, jd, "Rent on the " + p.Name);
                        break;
                    case PropertyKind.StationShare:
                        if (Seized(p, jd)) break;
                        PayDividend(p, jd);
                        break;
                    case PropertyKind.MiningClaim:
                        Charge(p, ClaimFeePerDay, jd, "Fee on the " + p.Name);
                        break;
                }
            }
            Reconcile(jd);
        }

        /// <summary>Cargo the house's ships have paid for and not yet sold, credits (out of the account, not yet a loss or a gain).</summary>
        public double CargoAfloat
        {
            get
            {
                double t = 0;
                foreach (var o in Ships)
                    if (!o.Lost && o.Ship.HasCargo) t += o.Ship.CargoCost;
                return t;
            }
        }

        /// <summary>What the ships have made or spent since the books last caught up with them (settled at the next daily round), credits.</summary>
        public double Unsettled
        {
            get
            {
                double t = 0;
                foreach (var o in Ships)
                    if (!o.Lost) t += o.Ship.Credits - o.SeenCredits;
                return t;
            }
        }

        /// <summary>Money that came or went without a line of its own (a ransom a raider took, a rescue fee for pods brought
        /// in, prize money) goes on the books as it is found, so the account and the books always agree.</summary>
        void Reconcile(double jd)
        {
            double diff = House.Treasury + CargoAfloat - BookBalance;
            if (Math.Abs(diff) < 1) return;
            Book(jd, diff > 0 ? "Other income (rescue fees, prizes, trade at the helm)" : "Other costs (ransoms, fines, trade at the helm)", diff);
        }

        /// <summary>The ship's running profit moved since the books last looked: trade and ore sold in, propellant, tolls and tankers out.</summary>
        void Settle(OwnedShip o, double jd)
        {
            double delta = o.Ship.Credits - o.SeenCredits;
            o.SeenCredits = o.Ship.Credits;
            if (Math.Abs(delta) < 0.5) return;
            if (delta > 0)
            {
                o.Earned += delta;
                if (o.Orders.Kind == StandingOrderKind.Mining && o.Orders.Claim != null) o.Orders.Claim.Income += delta;
                Book(jd, $"The {o.Name}'s takings", delta);
            }
            else
            {
                o.Spent += -delta;
                Book(jd, $"The {o.Name}'s propellant, tolls and losses on cargo", delta);
            }
        }

        bool CheckLost(OwnedShip o, double jd)
        {
            var s = o.Ship;
            string how = s.GateDeparted ? "left the system through a jump gate"
                : s.Destroyed ? "was destroyed"
                : s.Derelict ? "was boarded and cast adrift"
                : s.Faction != House ? "was taken by the " + s.Faction.Name
                : null;
            if (how == null) return false;
            o.Lost = true;
            o.LostJd = jd;
            o.LostHow = how;
            if (!s.GateDeparted)
            {
                double value = Economy.HullPrice(s.Spec.Class) * Math.Max(0.1, 1 - (s.Combat?.Damage ?? 0)) + (s.HasCargo ? s.CargoCost : 0);
                Book(jd, $"The {o.Name} {how}", -value, cash: false);
                // The cargo it was carrying was paid for out of the account: that cash is gone with it.
                if (s.HasCargo) BookBalance -= s.CargoCost;
                world.Log(jd, $"Your ship the {o.Name} {how}", WorldEventKind.Loss, s.Orbit?.Body ?? -1);
            }
            else Book(jd, $"The {o.Name} {how}", 0, earned: false);
            // Anyone escorting it stands down.
            foreach (var other in Ships)
                if (!other.Lost && other.Orders.Kind == StandingOrderKind.Escort && other.Orders.Charge == s) Order(other, StandingOrder.Hold());
            return true;
        }

        void PayWages(OwnedShip o, double jd)
        {
            var c = o.Captain;
            if (c == null) return;
            double wage = (c.WagePerDay + Economy.RunningCostPerDay(o.Ship.Spec.Class)) * TickDays;
            if (House.Treasury < wage)
            {
                if (++c.DaysUnpaid >= QuitDays)
                {
                    world.Log(jd, $"Captain {c.Name} walks off the {o.Name}: {c.DaysUnpaid} days unpaid", WorldEventKind.Info, o.Ship.Orbit?.Body ?? -1);
                    Book(jd, $"Captain {c.Name} quits the {o.Name}, unpaid", 0, earned: false);
                    o.Captain = null;
                }
                return;
            }
            c.DaysUnpaid = 0;
            House.Treasury -= wage;
            o.Wages += wage;
            Book(jd, $"Wages on the {o.Name}", -wage);
        }

        /// <summary>A damaged ship in port at a yard that will serve the house is mended a share a day, at the house's expense:
        /// in orbit, or landed through one of the world's rings (a merchant goes down to the surface, and waits there).</summary>
        void RepairInPort(OwnedShip o, double jd)
        {
            var s = o.Ship;
            if (s.Combat == null || s.Combat.Damage <= 0.001 || s.Battle != null || s == world.PilotedShip || (s.Phase != ShipPhase.Parked && s.Phase != ShipPhase.Landed)) return;
            var yard = YardAt(s.Orbit.Body);
            if (yard == null) return;
            double share = Math.Min(RepairPerDay, s.Combat.Damage);
            double bill = Economy.HullPrice(s.Spec.Class) * share * RepairRate;
            if (House.Treasury < bill) return;
            s.Combat.Repair(share);
            House.Treasury -= bill;
            yard.Owner.Treasury += bill;
            o.Repairs += bill;
            Book(jd, $"Repairs to the {o.Name} at {yard.Name}", -bill);
        }

        Settlement YardAt(int body)
        {
            foreach (var st in Economy.At(body))
                if (CanBuyAt(st)) return st;
            return null;
        }

        void Charge(Property p, double amount, double jd, string text)
        {
            House.Treasury -= amount;
            p.Upkeep += amount;
            Book(jd, text, -amount);
        }

        /// <summary>A pirate took the port: the warehouse and its stock, or the share, are gone.</summary>
        bool Seized(Property p, double jd)
        {
            var owner = p.Settlement?.Owner;
            if (owner != null && !owner.IsPirate) return false;
            p.Lost = true;
            p.LostJd = jd;
            double value = p.PricePaid;
            if (p.Kind == PropertyKind.Warehouse)
                for (int g = 0; g < GoodInfo.Count; g++) value += p.Stock[g] * GoodInfo.Of((Good)g).BasePrice;
            Book(jd, "The " + p.Name + " is seized", -value, cash: false);
            world.Log(jd, "Your " + p.Name + " is seized", WorldEventKind.Loss, p.Body);
            return true;
        }

        /// <summary>The share of the port taxes the settlement's trade has paid since the last dividend, from its owner's treasury.</summary>
        void PayDividend(Property p, double jd)
        {
            var s = p.Settlement;
            double traded = s.TradeVolumeCredits - p.SeenTradeVolume;
            p.SeenTradeVolume = s.TradeVolumeCredits;
            double dividend = Math.Max(0, traded) * Economy.PortTax * p.Share;
            if (dividend < 1) return;
            dividend = Math.Min(dividend, Math.Max(0, s.Owner.Treasury));
            if (dividend < 1) return;
            s.Owner.Treasury -= dividend;
            House.Treasury += dividend;
            p.Income += dividend;
            Book(jd, "Dividend on the " + p.Name, dividend);
        }

        // ---------------------------------------------------------------- the captains' choices

        /// <summary>
        /// Fleet hook: where an owned ship goes next under its captain's standing orders (always a body; its own to
        /// stay), or -1 if it is not the house's. With no captain it lies where it is.
        /// </summary>
        public int ChooseNext(Ship ship, int here, double jd)
        {
            var o = Of(ship);
            if (o == null) return -1;
            if (o.Captain == null)
            {
                if (!ship.HasCargo) ship.Cargo = "Laid up, no captain";
                return here;
            }
            // Low on propellant with none to be had here: to the nearest port that sells it.
            if (world.BurnsPropellant(ship) && ship.PropellantFraction < 0.3 && !Economy.HasPropellantAt(ship, here))
            {
                int fuel = Economy.NearestPropellant(ship, here, jd);
                if (fuel >= 0) return fuel;
            }
            // Badly hurt: to a yard, and wait there to be mended.
            if (ship.Combat != null && ship.Combat.Damage > RepairDamage)
            {
                if (YardAt(here) != null) return here;
                int yard = NearestYard(ship, here, jd);
                if (yard >= 0) return yard;
            }
            var orders = o.Orders;
            switch (orders.Kind)
            {
                case StandingOrderKind.TradeRoute: return NextOnRoute(o, here, jd);
                case StandingOrderKind.Patrol: return NextWaypoint(o, here);
                case StandingOrderKind.Mining: return NextForMining(o, here, jd);
                case StandingOrderKind.Escort: return NextForEscort(o, here);
                default:
                    if (!ship.HasCargo) ship.Cargo = "Holding in port";
                    return here;
            }
        }

        /// <summary>Fleet hook: how long an owned ship stays where it is: mining time on a claim, a short wait while
        /// escorting (it keeps checking on its charge), and a better captain turns round faster.</summary>
        public double Dwell(Ship ship, double normal)
        {
            var o = Of(ship);
            if (o == null) return normal;
            if (o.MiningDays > 0)
            {
                double d = o.MiningDays;
                o.MiningDays = 0;
                return d;
            }
            if (o.Captain == null || o.Orders.Kind == StandingOrderKind.Hold) return 1;
            if (o.Orders.Kind == StandingOrderKind.Escort) return 0.1;
            return normal * (1.5 - 0.5 * o.Captain.Skill);
        }

        int NextOnRoute(OwnedShip o, int here, double jd)
        {
            var orders = o.Orders;
            var stops = orders.Stops;
            var ship = o.Ship;
            int n = stops.Length;
            int idx = orders.StopIndex % n;
            if (stops[idx] != here)
            {
                // Not where it was bound: if it is at another stop, go on from there; if off the route, make for the stop it was bound for.
                int at = Array.IndexOf(stops, here);
                if (at < 0)
                {
                    if (!ship.HasCargo) ship.Cargo = "Empty, joining its route at " + world.Catalog[stops[idx]].Name;
                    return stops[idx];
                }
                idx = at;
            }
            int next = (idx + 1) % n;
            orders.StopIndex = next;
            int to = stops[next];
            if (!ship.HasCargo) Economy.LoadFor(ship, here, to, jd);
            if (!ship.HasCargo) ship.Cargo = "Empty, on its route to " + world.Catalog[to].Name;
            return to;
        }

        int NextWaypoint(OwnedShip o, int here)
        {
            var orders = o.Orders;
            var stops = orders.Stops;
            int n = stops.Length;
            int idx = orders.StopIndex % n;
            if (stops[idx] == here) idx = (idx + 1) % n;
            orders.StopIndex = idx;
            o.Ship.Cargo = o.Ship.HasCargo ? o.Ship.Cargo : "On patrol";
            return stops[idx];
        }

        int NextForMining(OwnedShip o, int here, double jd)
        {
            var ship = o.Ship;
            var claim = o.Orders.Claim;
            if (claim == null || claim.Lost)
            {
                Order(o, StandingOrder.Hold());
                return here;
            }
            if (ship.HasCargo)
            {
                // Ore aboard nobody here bought: on to where it sells.
                int sell = Economy.PlanTrade(ship, here, jd);
                return sell >= 0 ? sell : here;
            }
            if (here != claim.Body)
            {
                ship.Cargo = "Empty, bound for the " + claim.Name;
                return claim.Body;
            }
            // On the claim: cut a holdful (as much as the tanks can still haul away) and sell it wherever it fetches most.
            double tonnes = ship.CapacityTonnes;
            if (tonnes <= 0) return here;
            int dest = Economy.LoadOwn(ship, claim.Ore, tonnes, here, jd);
            if (dest < 0)
            {
                // Nowhere it can reach that takes it: leave it in the ground for now.
                ship.HasCargo = false;
                ship.CargoTonnes = 0;
                ship.Cargo = "Idle on the " + claim.Name + ": no market for its ore";
                return here;
            }
            o.TonnesMined += tonnes;
            o.MiningDays = tonnes / Math.Max(1, claim.YieldPerDay);
            return dest;
        }

        int NextForEscort(OwnedShip o, int here)
        {
            var charge = o.Orders.Charge;
            if (charge == null || charge.Destroyed || charge.Derelict || charge.GateDeparted)
            {
                Order(o, StandingOrder.Hold());
                return here;
            }
            o.Ship.Cargo = o.Ship.HasCargo ? o.Ship.Cargo : "Escorting " + charge.Name;
            int where = WhereBound(charge.Controller);
            return where >= 0 ? where : here;
        }

        /// <summary>Where a ship is or is going: the fight it is in, the body it holds at, or its trip's end.</summary>
        int WhereBound(Ship c)
        {
            if (c.Battle != null) return c.Battle.Body;
            if (c.Phase == ShipPhase.Transit || c.Phase == ShipPhase.Lane || c.Phase == ShipPhase.Lurk || c.Phase == ShipPhase.Adrift)
                return c.FinalBody >= 0 && c.Journey != null ? c.FinalBody : c.DestinationBody;
            if ((c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null) return c.Ring.Body;
            return c.Orbit?.Body ?? -1;
        }

        int NearestYard(Ship ship, int here, double jd)
        {
            int best = -1;
            double bestDays = double.MaxValue;
            foreach (var st in Economy.Settlements)
            {
                if (!CanBuyAt(st)) continue;
                double days = world.Fleet.EstimateTravelDays(ship, here, st.Body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    best = st.Body;
                }
            }
            return best;
        }
    }
}
