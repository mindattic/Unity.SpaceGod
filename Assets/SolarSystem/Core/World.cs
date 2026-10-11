using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// Everything that happens in the system, advanced together in date order: the fleet's
    /// schedules, the markets, and the wars. The orbital mechanics are real; the settlements,
    /// factions and their economy are the game layer, placed on worlds that plausibly hold what they
    /// produce (ice moons sell water, Titan hydrocarbons, Io copper and zinc from its sulphide flows).
    /// </summary>
    public sealed class World
    {
        public Ephemeris Ephemeris { get; }
        public BodyCatalog Catalog => Ephemeris.Catalog;
        public Fleet Fleet { get; }
        public Economy Economy { get; }
        public Diplomacy Diplomacy { get; } = new Diplomacy();
        public War War { get; private set; }

        public Faction Sol, Belt, Reach, Independents, Pirates;

        /// <summary>Things worth telling an observer about (a battle, a capture, a new hull), newest last.</summary>
        public readonly List<WorldEvent> Events = new List<WorldEvent>();

        /// <summary>Raised when a ship joins the fleet after the start (a yard finished it).</summary>
        public event Action<Ship> ShipAdded;

        int builtCount;

        World(Ephemeris ephemeris, double startJd, int seed)
        {
            Ephemeris = ephemeris;
            Fleet = new Fleet(ephemeris, seed);
            Economy = new Economy(Diplomacy, startJd, seed + 1);
        }

        double nextRecountJd;

        public void Update(double jd)
        {
            Fleet.Update(jd);
            Economy.AdvanceTo(jd);
            War.Update(jd);
            if (jd >= nextRecountJd)
            {
                Economy.RecountInbound(Fleet.Ships);
                nextRecountJd = jd + 1;
            }
        }

        public void NotifyAdded(Ship ship) => ShipAdded?.Invoke(ship);

        public void Log(double jd, string text, WorldEventKind kind = WorldEventKind.Info, int body = -1)
        {
            Events.Add(new WorldEvent(jd, text, kind, body));
            if (Events.Count > 400) Events.RemoveRange(0, Events.Count - 400);
        }

        public bool IsTrader(Ship s) => !s.IsEscort && (s.Role == ShipRole.Trader || s.Role == ShipRole.Miner || s.Role == ShipRole.Courier);

        // ---------------------------------------------------------------- the default system

        public static World CreateDefault(Ephemeris ephemeris, double jd, int seed = 1977)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed);
            var fleet = w.Fleet;
            var economy = w.Economy;

            w.Sol = fleet.AddFaction("Sol Federation", "SFS", new Rgb(0.35f, 0.65f, 1f));
            w.Belt = fleet.AddFaction("Belt Consortium", "BCS", new Rgb(1f, 0.6f, 0.25f));
            w.Reach = fleet.AddFaction("Outer Reach Compact", "ORS", new Rgb(0.72f, 0.5f, 1f));
            w.Independents = fleet.AddFaction("Independent", "MV", new Rgb(0.85f, 0.85f, 0.78f));
            w.Pirates = fleet.AddFaction("Black Banner", "", new Rgb(0.95f, 0.22f, 0.2f));
            w.Sol.IsMilitary = w.Belt.IsMilitary = w.Reach.IsMilitary = true;
            w.Pirates.IsPirate = true;
            w.Sol.Treasury = 4e9;
            w.Belt.Treasury = 2.5e9;
            w.Reach.Treasury = 3.5e9;
            w.Independents.Treasury = 6e8;
            w.Pirates.Treasury = 2e8;
            w.Diplomacy.Set(w.Sol, w.Reach, Relation.War);

            w.BuildSettlements();
            economy.Initialise();
            w.War = new War(w, startJd, seed + 2);
            var war = w.War;

            fleet.Lanes = Hyperlanes.CreateDefault(ephemeris);
            economy.EstimateTravelDays = fleet.EstimateTravelDays;
            economy.Danger = body => war.Danger(body) * 0.08;
            economy.ChooseBuild = yard => war.ChooseWarship(yard) ?? w.ChooseBuild(yard);
            economy.ShipBuilt = w.OnShipBuilt;
            fleet.BeforeEvent = economy.AdvanceTo;
            fleet.IsHeld = ship => ship.Battle != null;
            fleet.Dwell = war.Dwell;
            fleet.Arrived = (ship, when) =>
            {
                if (w.IsTrader(ship)) economy.OnArrive(ship, ship.Orbit.Body, when);
                war.OnArrived(ship, when);
            };
            fleet.ChooseNext = (ship, here, when) =>
            {
                if (!w.IsTrader(ship)) return war.ChooseNext(ship, here, when);
                int next = economy.PlanTrade(ship, here, when);
                if (next < 0 && !ship.HasCargo) ship.Cargo = "Empty, looking for work";
                return next;
            };

            w.BuildMilitary(startJd);
            w.BuildMerchants(startJd);
            int haven = w.Catalog["Deimos"].Index;
            war.RaisePirateGroup(startJd, haven);
            war.RaisePirateGroup(startJd, haven);

            w.Update(jd);
            return w;
        }

        void BuildSettlements()
        {
            var e = Economy;
            int B(string name) => Catalog[name].Index;
            void Mine(Settlement s, Good g, double perDay) => s.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1));
            void Make(Settlement s, IndustryKind k, double level) => s.Facilities.Add(new Facility(Recipe.Of(k), level));

            // Sol Federation: Earth's orbital industry and the inner worlds.
            var earth = e.Add("Earth Orbital", B("Earth"), SettlementKind.Colony, Sol, 12e6);
            Mine(earth, Good.Water, 82000);
            Mine(earth, Good.Nitrogen, 3500);
            Mine(earth, Good.Hydrocarbons, 1500);
            Make(earth, IndustryKind.Farm, 48);
            Make(earth, IndustryKind.PharmaLab, 12);
            Make(earth, IndustryKind.ElectronicsFab, 7);
            Make(earth, IndustryKind.MachineWorks, 3);
            Make(earth, IndustryKind.MunitionsPlant, 3);
            Make(earth, IndustryKind.Shipyard, 2);

            var luna = e.Add("Tycho City", B("Moon"), SettlementKind.Colony, Sol, 500e3);
            Mine(luna, Good.HeliumThree, 45);
            Mine(luna, Good.Titanium, 400);
            Mine(luna, Good.RareEarths, 70);
            Mine(luna, Good.Platinum, 6);
            Mine(luna, Good.Water, 1500);
            Make(luna, IndustryKind.Farm, 1.5);

            var mercury = e.Add("Caloris Works", B("Mercury"), SettlementKind.MiningOutpost, Sol, 100e3);
            Mine(mercury, Good.NickelIron, 6500);
            Mine(mercury, Good.Copper, 700);
            Mine(mercury, Good.Titanium, 300);
            Make(mercury, IndustryKind.Smelter, 2);

            var venus = e.Add("Aphrodite Aerostat", B("Venus"), SettlementKind.Station, Sol, 150e3);
            Mine(venus, Good.Nitrogen, 1500);
            Mine(venus, Good.Hydrocarbons, 1200);
            Make(venus, IndustryKind.PharmaLab, 4);

            var mars = e.Add("Ares Prime", B("Mars"), SettlementKind.Colony, Sol, 1.5e6);
            Mine(mars, Good.Water, 10500);
            Mine(mars, Good.NickelIron, 4200);
            Mine(mars, Good.Copper, 500);
            Mine(mars, Good.Zinc, 200);
            Make(mars, IndustryKind.Farm, 6);
            Make(mars, IndustryKind.Smelter, 1.5);
            Make(mars, IndustryKind.MunitionsPlant, 2);
            Make(mars, IndustryKind.Shipyard, 1.5);

            var phobos = e.Add("Stickney Depot", B("Phobos"), SettlementKind.Station, Sol, 25e3);
            Mine(phobos, Good.Water, 900);
            Mine(phobos, Good.Hydrocarbons, 400);

            e.Add("Blackwater", B("Deimos"), SettlementKind.Haven, Pirates, 15e3);

            // Belt Consortium: Ceres and the Jovian moons.
            var ceres = e.Add("Ceres Hub", B("Ceres"), SettlementKind.Colony, Belt, 700e3);
            Mine(ceres, Good.Water, 9000);
            Mine(ceres, Good.NickelIron, 9000);
            Mine(ceres, Good.Platinum, 15);
            Mine(ceres, Good.RareEarths, 50);
            Make(ceres, IndustryKind.Smelter, 3);
            Make(ceres, IndustryKind.Farm, 2.5);
            Make(ceres, IndustryKind.MunitionsPlant, 1.5);
            Make(ceres, IndustryKind.Shipyard, 1.5);

            var europa = e.Add("Conamara Station", B("Europa"), SettlementKind.Station, Belt, 120e3);
            Mine(europa, Good.Water, 14000);

            var ganymede = e.Add("Ganymede Union", B("Ganymede"), SettlementKind.Colony, Belt, 800e3);
            Mine(ganymede, Good.Water, 6500);
            Mine(ganymede, Good.Titanium, 250);
            Make(ganymede, IndustryKind.Farm, 5);
            Make(ganymede, IndustryKind.ElectronicsFab, 4);
            Make(ganymede, IndustryKind.MachineWorks, 3);
            Make(ganymede, IndustryKind.PharmaLab, 3);
            Make(ganymede, IndustryKind.Shipyard, 2);

            var callisto = e.Add("Valhalla Mines", B("Callisto"), SettlementKind.MiningOutpost, Belt, 150e3);
            Mine(callisto, Good.NickelIron, 7000);
            Mine(callisto, Good.Water, 2000);
            Make(callisto, IndustryKind.Smelter, 2);

            var io = e.Add("Loki Sulphide Works", B("Io"), SettlementKind.MiningOutpost, Belt, 40e3);
            Mine(io, Good.Copper, 2000);
            Mine(io, Good.Zinc, 600);

            // Outer Reach Compact: Saturn and beyond.
            var titan = e.Add("Kraken Port", B("Titan"), SettlementKind.Colony, Reach, 500e3);
            Mine(titan, Good.Hydrocarbons, 2500);
            Mine(titan, Good.Nitrogen, 2000);
            Mine(titan, Good.Water, 3500);
            Make(titan, IndustryKind.Farm, 3);
            Make(titan, IndustryKind.PharmaLab, 4);
            Make(titan, IndustryKind.MunitionsPlant, 3);
            Make(titan, IndustryKind.Shipyard, 2);

            var enceladus = e.Add("Tiger Stripe", B("Enceladus"), SettlementKind.Station, Reach, 40e3);
            Mine(enceladus, Good.Water, 6000);
            Mine(enceladus, Good.Hydrocarbons, 500);

            var titania = e.Add("Gertrude Base", B("Titania"), SettlementKind.Station, Reach, 60e3);
            Mine(titania, Good.HeliumThree, 50);
            Mine(titania, Good.Water, 1500);

            var oberon = e.Add("Oberon Deep", B("Oberon"), SettlementKind.MiningOutpost, Reach, 40e3);
            Mine(oberon, Good.RareEarths, 150);
            Mine(oberon, Good.Titanium, 250);
            Mine(oberon, Good.Platinum, 6);

            var triton = e.Add("Triton Station", B("Triton"), SettlementKind.Station, Reach, 70e3);
            Mine(triton, Good.Nitrogen, 1200);
            Mine(triton, Good.HeliumThree, 35);
            Mine(triton, Good.Water, 1000);

            var charon = e.Add("Charon Outpost", B("Charon"), SettlementKind.MiningOutpost, Reach, 30e3);
            Mine(charon, Good.Water, 1500);
            Mine(charon, Good.Nitrogen, 600);

            foreach (var s in e.Settlements) Subsist(s, Mine, Make);

            // Orbital defence platforms: the big colonies are fortresses, outposts have one or none.
            foreach (var s in e.Settlements)
            {
                s.MaxPlatforms = s.Population >= 5e6 ? 4 : s.Population >= 1e6 ? 3 : s.Population >= 400e3 ? 2 : 1;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        /// <summary>
        /// Every settlement grows most of its own food and melts most of its own water (a haven all of
        /// it, since nobody else will deliver there); imports and supply contracts cover the rest.
        /// </summary>
        static void Subsist(Settlement s, Action<Settlement, Good, double> mine, Action<Settlement, IndustryKind, double> make)
        {
            double share = s.Kind == SettlementKind.Haven ? 1.1 : 0.92;
            var farm = Recipe.Of(IndustryKind.Farm);
            double food = 0, water = 0, nitrogen = 0;
            foreach (var f in s.Facilities)
            {
                foreach (var (g, rate) in f.Recipe.Outputs)
                {
                    if (g == Good.Food) food += rate * f.Level;
                    if (g == Good.Water) water += rate * f.Level;
                    if (g == Good.Nitrogen) nitrogen += rate * f.Level;
                }
                foreach (var (g, rate) in f.Recipe.Inputs)
                {
                    if (g == Good.Water) water -= rate * f.Level;
                    if (g == Good.Nitrogen) nitrogen -= rate * f.Level;
                }
            }
            double foodNeed = s.Population * 0.0016 * share;
            if (food < foodNeed)
            {
                double level = (foodNeed - food) / farm.Outputs[0].perDay;
                make(s, IndustryKind.Farm, level);
                water -= farm.Inputs[0].perDay * level;
                nitrogen -= farm.Inputs[1].perDay * level;
            }
            double waterNeed = s.Population * 0.004 * share;
            if (water < waterNeed) mine(s, Good.Water, (waterNeed - water) * 1.05);
            if (nitrogen < 0) mine(s, Good.Nitrogen, -nitrogen * share);
        }

        void BuildMilitary(double jd)
        {
            var fleet = Fleet;
            fleet.Group(Sol, jd, ShipClass.Carrier, "Resolute", new[] { "Earth", "Moon", "Mars" },
                (ShipClass.Battleship, "Dauntless"), (ShipClass.Cruiser, "Aegis"), (ShipClass.Cruiser, "Halberd"),
                (ShipClass.Destroyer, "Kestrel"), (ShipClass.Destroyer, "Merlin"));
            fleet.Group(Sol, jd, ShipClass.Battleship, "Vanguard", new[] { "Mars", "Ceres", "Earth" },
                (ShipClass.Cruiser, "Lance"), (ShipClass.Destroyer, "Swift"), (ShipClass.Destroyer, "Talon"));
            fleet.Group(Belt, jd, ShipClass.Carrier, "Tycho Ascendant", new[] { "Ceres", "Ganymede", "Callisto", "Europa" },
                (ShipClass.Battleship, "Iron Ledger"), (ShipClass.Cruiser, "Ore Hawk"), (ShipClass.Cruiser, "Pallas"),
                (ShipClass.Destroyer, "Flint"));
            fleet.Group(Belt, jd, ShipClass.Cruiser, "Hygiea", new[] { "Ceres", "Mars" },
                (ShipClass.Destroyer, "Shard"), (ShipClass.Destroyer, "Spall"));
            fleet.Group(Reach, jd, ShipClass.Battleship, "Long Night", new[] { "Titan", "Titania", "Triton" },
                (ShipClass.Cruiser, "Umbra"), (ShipClass.Cruiser, "Perihelion"), (ShipClass.Destroyer, "Wisp"));
            fleet.Group(Reach, jd, ShipClass.Carrier, "Endurance", new[] { "Triton", "Charon", "Titan", "Ganymede" },
                (ShipClass.Cruiser, "Aphelion"), (ShipClass.Destroyer, "Rime"), (ShipClass.Destroyer, "Hoarfrost"));
        }

        static readonly string[] FreighterNames =
        {
            "Bountiful Harvest", "Kepler's Due", "Slow Boat", "Red Ledger", "Ice Mule", "Long Haul", "Patience",
            "Margin of Error", "Cold Comfort", "Ballast", "Heavy Lifting", "Second Wind", "Tin Can", "Far Shore",
            "Due Diligence", "Fair Winds", "Hard Bargain", "Payload", "Quiet Profit", "Steady State", "Tramp Royal",
            "Volume Discount", "Wanderer", "Yield Curve", "Small Mercies", "Cargo Cult", "Dead Reckoning", "Night Market",
        };

        static readonly string[] MinerNames = { "Pickaxe", "Rockhound", "Prospect", "Tailings", "Motherlode", "Seam", "Slag Heap", "Assay", "Lode Star", "Gangue" };
        static readonly string[] CourierNames = { "Quicksilver", "Hermes", "Dispatch", "Lightfoot", "Express", "Swiftsure" };

        void BuildMerchants(double jd)
        {
            var ports = PortBodies();
            Faction[] owners = { Independents, Independents, Sol, Belt, Reach, Independents };
            for (int i = 0; i < FreighterNames.Length; i++)
                AddMerchant(FreighterNames[i], ShipClass.Freighter, owners[i % owners.Length], ShipRole.Trader, ports, jd);
            for (int i = 0; i < MinerNames.Length; i++)
                AddMerchant(MinerNames[i], ShipClass.Miner, i % 3 == 2 ? Reach : Belt, ShipRole.Miner, ports, jd);
            foreach (var name in CourierNames)
                AddMerchant(name, ShipClass.Courier, Independents, ShipRole.Courier, ports, jd);
        }

        int[] PortBodies()
        {
            var list = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Kind != SettlementKind.Haven && !list.Contains(s.Body)) list.Add(s.Body);
            return list.ToArray();
        }

        Ship AddMerchant(string name, ShipClass cls, Faction owner, ShipRole role, int[] ports, double jd, int at = -1)
        {
            var ship = Fleet.AddShip(name, cls, owner, role);
            ship.CapacityTonnes = Economy.CapacityOf(cls);
            ship.Ports = ports;
            if (at < 0)
            {
                // Start at a port the owner may trade at.
                for (int tries = 0; tries < 20; tries++)
                {
                    at = ports[Fleet.Rng.Next(ports.Length)];
                    bool ok = false;
                    foreach (var s in Economy.At(at)) ok |= Diplomacy.CanDock(owner, s.Owner);
                    if (ok) break;
                }
            }
            Fleet.Station(ship, at, jd);
            return ship;
        }

        // ---------------------------------------------------------------- shipbuilding

        int TraderCount()
        {
            int n = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && IsTrader(s)) n++;
            return n;
        }

        /// <summary>Yards build merchant hulls while the merchant fleet is under strength.</summary>
        ShipClass? ChooseBuild(Settlement yard)
        {
            if (TraderCount() >= 70) return null;
            return builtCount % 3 == 2 ? ShipClass.Miner : ShipClass.Freighter;
        }

        void OnShipBuilt(Settlement yard, ShipClass cls, double jd)
        {
            Ship ship;
            if (ShipClassSpec.Of(cls).IsMilitary)
            {
                ship = War.LaunchWarship(yard, cls, jd);
                ShipAdded?.Invoke(ship);
                return;
            }
            builtCount++;
            if (cls == ShipClass.Freighter || cls == ShipClass.Miner || cls == ShipClass.Courier)
            {
                var role = cls == ShipClass.Miner ? ShipRole.Miner : cls == ShipClass.Courier ? ShipRole.Courier : ShipRole.Trader;
                var names = cls == ShipClass.Miner ? MinerNames : FreighterNames;
                string name = names[builtCount % names.Length] + " " + Roman(2 + builtCount / names.Length);
                ship = AddMerchant(name, cls, yard.Owner, role, PortBodies(), jd, yard.Body);
            }
            else return;
            Log(jd, $"{yard.Name} launches the {ship.Spec.DisplayName.ToLowerInvariant()} {ship.Name}", WorldEventKind.Launch, yard.Body);
            ShipAdded?.Invoke(ship);
        }

        static string Roman(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n < r.Length ? r[n] : n.ToString();
        }
    }

    public enum WorldEventKind
    {
        Info,
        Launch,
        Battle,
        Capture,
        Diplomacy,
        Loss,
    }

    public readonly struct WorldEvent
    {
        public readonly double Jd;
        public readonly string Text;
        public readonly WorldEventKind Kind;
        public readonly int Body;

        public WorldEvent(double jd, string text, WorldEventKind kind, int body)
        {
            Jd = jd;
            Text = text;
            Kind = kind;
            Body = body;
        }
    }
}
