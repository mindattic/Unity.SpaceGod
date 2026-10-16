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

        /// <summary>Stations, yards, factories and spacecraft in orbit, and the docking rings over populous worlds.</summary>
        public Infrastructure Infrastructure { get; private set; }

        public Faction Sol, Belt, Reach, Independents, Pirates;

        /// <summary>The two powers whose wars and ceasefires come and go (Sol: the Federation and the Reach).</summary>
        public Faction RivalA, RivalB;

        /// <summary>Names the pirates give their raiders; null for the default list.</summary>
        public string[] RaiderNames;

        /// <summary>The merchant fleet yards keep up before navies get their slips, and the most they build to.</summary>
        public int MerchantTarget = 100, MerchantCap = 170;

        /// <summary>How many warships a navy keeps in peace and in war.</summary>
        public int NavyCap = 20, NavyCapAtWar = 34;

        /// <summary>The most raider groups the pirates field, and what smuggling and rackets bring them a day.</summary>
        public int PirateGroupCap = 4;
        public double PirateIncomePerDay = 1.5e6;

        /// <summary>Things worth telling an observer about (a battle, a capture, a new hull), newest last.</summary>
        public readonly List<WorldEvent> Events = new List<WorldEvent>();

        /// <summary>The ship under direct player control, if any; its schedule is suspended while flown
        /// (see <see cref="Fleet.IsHeld"/>) and a pilot-flight controller owns its position instead.</summary>
        public Ship PilotedShip;

        /// <summary>The dynamic event system: adventures near the player drawn from what the sim is doing
        /// (see <see cref="EventDirector"/>), advanced by <see cref="UpdateAdventures"/>.</summary>
        public EventDirector Adventures { get; private set; }

        /// <summary>Raised when a ship joins the fleet after the start (a yard finished it, or it came through a gate).</summary>
        public event Action<Ship> ShipAdded;

        /// <summary>Raised when a ship bound for a jump gate reaches it (see <see cref="Ship.GateKm"/> and <see cref="InterstellarTraffic"/>).</summary>
        public event Action<Ship, double> ReachedGate;

        /// <summary>
        /// Orders for a ship here on an interstellar voyage (raiders from another system, a navy's
        /// expedition): the body to go to next, or -1 to fall back on the usual choice.
        /// </summary>
        public Func<Ship, int, double, int> VoyageOrders;

        int builtCount;

        World(Ephemeris ephemeris, double startJd, int seed)
        {
            Ephemeris = ephemeris;
            Fleet = new Fleet(ephemeris, seed);
            Economy = new Economy(Diplomacy, startJd, seed + 1);
            Adventures = new EventDirector(seed + 5);
            Adventures.Spawned += op => Log(op.SpawnJd, op.MarkerLabel + ": " + op.Hook, WorldEventKind.Opportunity, op.Ship?.Orbit?.Body ?? op.Body);
            Adventures.Resolved += op => Log(op.ResolvedJd, op.MarkerLabel + (op.State == OpportunityState.Completed ? " done: " : " lost: ") + op.Outcome,
                WorldEventKind.Opportunity, op.Ship?.Orbit?.Body ?? op.Body);
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

        /// <summary>
        /// Advances the dynamic event system to <paramref name="jd"/> around the player, who is at
        /// <paramref name="playerHelioKm"/> (the piloted ship, or the camera when only watching); only a
        /// piloted ship (<see cref="PilotedShip"/>) can arrive on the scene and collect.
        /// </summary>
        public void UpdateAdventures(double jd, DVec3 playerHelioKm)
        {
            if (War == null) return;
            Adventures.Update(jd, EventSources.Of(this, jd), playerHelioKm, PilotedShip);
        }

        /// <summary>Where a wreck is now, heliocentric km: fixed in its body's frame, or beside the lane it died by.</summary>
        public DVec3 WreckHelio(Wreck w, double jd)
        {
            var lanes = Fleet.Lanes;
            if (w.Site == null || lanes == null) return Ephemeris.HelioAt(w.Body, jd) + w.Pos;
            var hop = w.Site.Hop;
            var a = Ephemeris.HelioAt(hop.From, jd) + w.Site.FromOffset;
            var b = Ephemeris.HelioAt(hop.To, jd) + w.Site.ToOffset;
            return lanes.CurveKm(hop.Lane, a, b, w.Site.Progress, jd) + w.Site.Side + w.Local;
        }

        /// <summary>
        /// The player's ship lets go of a hulk (or has just claimed it): the clamps come off, what came of it
        /// goes in the event log, and the derelict or wreck adventure on it, if one is open, is seen through
        /// (<see cref="EventDirector.HulkWorked"/>). Returns that adventure, or null.
        /// </summary>
        public Opportunity FinishSalvage(SalvageSession session, double jd)
        {
            if (session == null) return null;
            session.CastOff();
            if (!session.Worked) return null;
            int body = session.Derelict?.Orbit?.Body ?? session.Wreck?.Body ?? -1;
            Log(jd, $"The {session.Player.Name} {session.Summary()} aboard {session.Name}", session.Claimed ? WorldEventKind.Capture : WorldEventKind.Info, body);
            return Adventures?.HulkWorked(session, jd);
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
            w.Sol.MarineQuality = 1.25;
            w.Reach.MarineQuality = 1.15;
            w.Belt.MarineQuality = 1.0;
            w.Pirates.MarineQuality = 1.1;
            w.Independents.MarineQuality = 0.6;
            w.Sol.Treasury = 4e9;
            w.Belt.Treasury = 2.5e9;
            w.Reach.Treasury = 3.5e9;
            w.Independents.Treasury = 6e8;
            w.Pirates.Treasury = 2e8;
            w.Diplomacy.Set(w.Sol, w.Reach, Relation.War);
            w.RivalA = w.Sol;
            w.RivalB = w.Reach;

            w.BuildSettlements();
            w.Wire(startJd, seed, Hyperlanes.CreateDefault(ephemeris));
            var war = w.War;
            w.Infrastructure.AddSolSpacecraft();
            w.Infrastructure.AddSolRings(w.Economy);
            w.Fleet.Rings.AddRange(w.Infrastructure.Rings);

            w.BuildMilitary(startJd);
            w.BuildMerchants(startJd, FreighterNames, MinerNames, CourierNames, new[] { w.Independents, w.Independents, w.Sol, w.Belt, w.Reach, w.Independents },
                i => i % 3 == 2 ? w.Reach : w.Belt);
            w.AddFreeShips(startJd, new[] { "Kestrel's Folly", "Wanderlust", "Far Horizon", "Stargazer", "Solitaire", "Driftwood", "Errant", "Idle Hours" },
                new[] { "Prospector Nine", "Lucky Strike" });
            int haven = w.Catalog["Deimos"].Index;
            war.RaisePirateGroup(startJd, haven);
            // The Banner's second band sails from its biggest belt hideout; recruiting fills the other
            // havens first (it raises each new band at the standing haven with the fewest).
            war.RaisePirateGroup(startJd, w.Catalog[BeltHavens[0].body].Index);

            w.Update(jd);
            return w;
        }

        /// <summary>Connects the markets, the wars and the fleet's schedule (the same in every system).</summary>
        void Wire(double startJd, int seed, Hyperlanes lanes)
        {
            var economy = Economy;
            var fleet = Fleet;
            economy.Initialise();
            War = new War(this, startJd, seed + 2);
            var war = War;
            Infrastructure = new Infrastructure(Catalog, Ephemeris);
            Infrastructure.AddSettlementStructures(economy, seed + 3);
            // Merchants and couriers go down to the surface through the rings; warships and pirates hold orbit.
            fleet.Lands = IsTrader;

            fleet.Lanes = lanes;
            economy.EstimateTravelDays = fleet.EstimateTravelDays;
            economy.Danger = body => war.Danger(body) * 0.08;
            // Yards keep the merchant fleet up first; then the navies take their slips.
            economy.ChooseBuild = yard => TraderCount() < MerchantTarget ? ChooseBuild(yard) : war.ChooseWarship(yard) ?? ChooseBuild(yard);
            economy.ShipBuilt = OnShipBuilt;
            fleet.BeforeEvent = economy.AdvanceTo;
            fleet.IsHeld = ship => ship.Battle != null || ship.Derelict || ship == PilotedShip || (ship.Voyage != null && ship.Voyage.AtGate);
            fleet.Dwell = war.Dwell;
            fleet.Arrived = (ship, when) =>
            {
                // At a jump gate, not a port: the interstellar traffic takes it from here.
                if (ship.GateKm != null && ship.Orbit.Body == Catalog.Root.Index)
                {
                    ReachedGate?.Invoke(ship, when);
                    return;
                }
                if (IsTrader(ship)) economy.OnArrive(ship, ship.Orbit.Body, when);
                war.OnArrived(ship, when);
            };
            fleet.ReachedRing = war.OnArrived;
            // Merchants heading into a fight that has just broken out turn aside for somewhere safe.
            war.BattleStarted += b => DivertAround(b, b.StartJd);
            fleet.ChooseNext = (ship, here, when) =>
            {
                if (ship.Voyage != null && VoyageOrders != null)
                {
                    int ordered = VoyageOrders(ship, here, when);
                    if (ordered >= 0) return ordered;
                }
                // Free ships follow their own whims (the fleet's built-in choice), not orders or markets.
                if (ship.Role == ShipRole.Free) return -1;
                if (!IsTrader(ship)) return war.ChooseNext(ship, here, when);
                int next = economy.PlanTrade(ship, here, when);
                if (next < 0 && !ship.HasCargo) ship.Cargo = "Empty, looking for work";
                return next;
            };
        }

        // ---------------------------------------------------------------- Alpha Centauri

        /// <summary>
        /// Alpha Centauri, a century or so after settlement: the same machinery as Sol, but young and
        /// thin. The Centauri Accord governs the Proxima colonies with a small navy; the Toliman
        /// Combine, a corporate concern, runs the stations around A and B with no warships of its own;
        /// Kentaurus Free Traders carry most of the freight; and the Long Dark raid the long lanes
        /// from a derelict station out by Proxima. All fiction, on real (and candidate) worlds.
        /// </summary>
        public static World CreateAlphaCentauri(Ephemeris ephemeris, double jd, int seed = 4243)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed);
            var fleet = w.Fleet;

            var accord = w.Sol = fleet.AddFaction("Centauri Accord", "CAS", new Rgb(0.3f, 0.88f, 0.75f));
            var combine = w.Belt = fleet.AddFaction("Toliman Combine", "TCV", new Rgb(1f, 0.78f, 0.3f));
            w.Independents = fleet.AddFaction("Kentaurus Free Traders", "KFT", new Rgb(0.78f, 0.84f, 0.92f));
            w.Pirates = fleet.AddFaction("Long Dark", "", new Rgb(0.85f, 0.25f, 0.6f));
            accord.IsMilitary = true;
            w.Pirates.IsPirate = true;
            accord.MarineQuality = 1.15;
            combine.MarineQuality = 0.8;
            w.Independents.MarineQuality = 0.6;
            w.Pirates.MarineQuality = 1.2;
            accord.Treasury = 9e8;
            combine.Treasury = 7e8;
            w.Independents.Treasury = 1.2e8;
            w.Pirates.Treasury = 8e7;
            // Rivals, not enemies: the hook for a war later.
            w.RivalA = accord;
            w.RivalB = combine;
            w.MerchantTarget = 50;
            w.MerchantCap = 80;
            w.NavyCap = 10;
            w.NavyCapAtWar = 16;
            // A thin system feeds few raiders.
            w.PirateGroupCap = 2;
            w.PirateIncomePerDay = 4e5;
            w.RaiderNames = new[]
            {
                "Lightless", "Event Horizon", "Cold Sleep", "Red Shift", "Penumbra", "Dead Air", "Null Return", "Last Light",
                "Occultation", "Silent Running", "Umbral", "Long Fall",
            };

            w.BuildCentauriSettlements();
            w.Wire(startJd, seed, Hyperlanes.CreateAlphaCentauri(ephemeris));
            var war = w.War;
            w.Infrastructure.AddCentauriRings(w.Economy);
            w.Fleet.Rings.AddRange(w.Infrastructure.Rings);

            fleet.Group(accord, startJd, ShipClass.Battleship, "Terminator", new[] { "Proxima b", "Proxima d", "Proxima c" },
                (ShipClass.Cruiser, "Long Noon"), (ShipClass.Destroyer, "Flare"), (ShipClass.Destroyer, "Ember"));
            fleet.Group(accord, startJd, ShipClass.Cruiser, "Centaur", new[] { "Proxima b", "Rigil Gate", "Toliman Forge" },
                (ShipClass.Destroyer, "Hadar"), (ShipClass.Destroyer, "Agena"));

            string[] freighters =
            {
                "Second Sun", "Tidal Lock", "Far Side", "Kentaur Pride", "Twilight Band", "Nine Lights", "Slow Fuse",
                "Toliman's Due", "Red Noon", "Barycentre",
            };
            string[] haulers = { "Dross", "Cinder", "Ore Moth", "Spoil" };
            string[] couriers = { "Long Shot", "Lightspeed Promise" };
            w.BuildMerchants(startJd, freighters, haulers, couriers,
                new[] { w.Independents, w.Independents, combine, w.Independents, accord }, i => combine);

            w.AddFreeShips(startJd, new[] { "First Light", "Long Look", "Terminator Dawn" }, new[] { "Red Dust" });
            int haven = w.Catalog["Blackhollow"].Index;
            war.RaisePirateGroup(startJd, haven);

            w.Update(jd);
            return w;
        }

        /// <summary>
        /// A system nobody has settled yet: real stars and worlds, the same machinery, but no
        /// settlements, lanes or ships. Two factions exist only so the game layer has someone to
        /// hand a ship to (a survey service and the raiders it will one day meet); both are fiction.
        /// </summary>
        public static World CreateUnsettled(Ephemeris ephemeris, double jd, int seed = 1)
        {
            double startJd = jd - 45;
            var w = new World(ephemeris, startJd, seed);
            var fleet = w.Fleet;
            w.Sol = w.Independents = fleet.AddFaction("Survey Service", "SSV", new Rgb(0.7f, 0.85f, 0.95f));
            w.Pirates = fleet.AddFaction("Unknown", "", new Rgb(0.8f, 0.3f, 0.3f));
            w.Pirates.IsPirate = true;
            w.MerchantTarget = w.MerchantCap = 0;
            w.NavyCap = w.NavyCapAtWar = 0;
            w.PirateGroupCap = 0;
            w.PirateIncomePerDay = 0;
            w.Wire(startJd, seed, new Hyperlanes(ephemeris));
            w.Update(jd);
            return w;
        }

        void BuildCentauriSettlements()
        {
            var e = Economy;
            int B(string name) => Catalog[name].Index;
            void Mine(Settlement s, Good g, double perDay) => s.Facilities.Add(new Facility(Recipe.Mine(g, perDay), 1));
            void Make(Settlement s, IndustryKind k, double level) => s.Facilities.Add(new Facility(Recipe.Of(k), level));

            // The Accord: Proxima's worlds.
            var kentaurus = e.Add("New Kentaurus", B("Proxima b"), SettlementKind.Colony, Sol, 400e3);
            Mine(kentaurus, Good.Water, 2500);
            Mine(kentaurus, Good.Nitrogen, 700);
            Mine(kentaurus, Good.Hydrocarbons, 300);
            Make(kentaurus, IndustryKind.PharmaLab, 2);
            Make(kentaurus, IndustryKind.MunitionsPlant, 1);
            Make(kentaurus, IndustryKind.Shipyard, 0.8);

            var ember = e.Add("Ember Station", B("Proxima d"), SettlementKind.MiningOutpost, Sol, 30e3);
            Mine(ember, Good.NickelIron, 4500);
            Mine(ember, Good.Titanium, 220);
            Mine(ember, Good.Copper, 450);
            Mine(ember, Good.Zinc, 120);
            Make(ember, IndustryKind.Smelter, 1);

            var farReach = e.Add("Far Reach", B("Proxima c"), SettlementKind.Station, Sol, 20e3);
            Mine(farReach, Good.Water, 4000);
            Mine(farReach, Good.Nitrogen, 800);
            Mine(farReach, Good.HeliumThree, 8);

            // The Combine: stations around A and B.
            var rigil = e.Add("Rigil Gate", B("Rigil Gate"), SettlementKind.Station, Belt, 120e3);
            Make(rigil, IndustryKind.ElectronicsFab, 1.5);
            Make(rigil, IndustryKind.MachineWorks, 1);
            Make(rigil, IndustryKind.PharmaLab, 1);

            var aurora = e.Add("Aurora Skimmer", B("Alpha Centauri Ab"), SettlementKind.Station, Belt, 15e3);
            Mine(aurora, Good.HeliumThree, 30);
            Mine(aurora, Good.Hydrocarbons, 800);

            var forge = e.Add("Toliman Forge", B("Toliman Forge"), SettlementKind.Station, Belt, 80e3);
            // Ore from B's debris (fiction: no asteroid belt is known there).
            Mine(forge, Good.NickelIron, 1500);
            Mine(forge, Good.Titanium, 120);
            Mine(forge, Good.RareEarths, 40);
            Mine(forge, Good.Platinum, 4);
            Make(forge, IndustryKind.Smelter, 1.5);
            Make(forge, IndustryKind.MachineWorks, 1);
            Make(forge, IndustryKind.Shipyard, 1);

            e.Add("Blackhollow", B("Blackhollow"), SettlementKind.Haven, Pirates, 8e3);

            foreach (var s in e.Settlements) Subsist(s, Mine, Make);
            // The Combine has no navy, so its stations are well gunned.
            foreach (var s in e.Settlements)
            {
                s.MaxPlatforms = s.Population >= 400e3 ? 3 : s.Owner == Belt && s.Population >= 50e3 ? 3 : s.Kind == SettlementKind.Haven ? 1 : 2;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
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

            // The Banner's hidden belt bases, off the lanes on real asteroids, unknown until someone finds them.
            foreach (var (name, body, people) in BeltHavens)
                e.Add(name, B(body), SettlementKind.Haven, Pirates, people).Hidden = true;

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
                // A haven's garrison: two gun platforms over the rock.
                s.MaxPlatforms = s.Kind == SettlementKind.Haven ? 2 : s.Population >= 5e6 ? 4 : s.Population >= 1e6 ? 3 : s.Population >= 400e3 ? 2 : 1;
                for (int i = 0; i < s.MaxPlatforms; i++) s.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        /// <summary>The hidden pirate havens of Sol's main belt: name, asteroid, people.</summary>
        public static readonly (string name, string body, double people)[] BeltHavens =
        {
            ("Hollow Iron", "Psyche", 6e3),
            ("Lantern Rock", "Lutetia", 4e3),
            ("Dactyl's Rest", "Ida", 3e3),
        };

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
                (ShipClass.Destroyer, "Kestrel"), (ShipClass.Destroyer, "Merlin"),
                (ShipClass.Fighter, "Mayfly"), (ShipClass.Fighter, "Wasp"));
            fleet.Group(Sol, jd, ShipClass.Battleship, "Vanguard", new[] { "Mars", "Ceres", "Earth" },
                (ShipClass.Cruiser, "Lance"), (ShipClass.Destroyer, "Swift"), (ShipClass.Destroyer, "Talon"),
                (ShipClass.Fighter, "Gadfly"), (ShipClass.Fighter, "Hornet"));
            fleet.Group(Belt, jd, ShipClass.Carrier, "Tycho Ascendant", new[] { "Ceres", "Ganymede", "Callisto", "Europa" },
                (ShipClass.Battleship, "Iron Ledger"), (ShipClass.Cruiser, "Ore Hawk"), (ShipClass.Cruiser, "Pallas"),
                (ShipClass.Destroyer, "Flint"), (ShipClass.Fighter, "Firefly"), (ShipClass.Fighter, "Cinder"));
            fleet.Group(Belt, jd, ShipClass.Cruiser, "Hygiea", new[] { "Ceres", "Mars" },
                (ShipClass.Destroyer, "Shard"), (ShipClass.Destroyer, "Spall"),
                (ShipClass.Fighter, "Grit"), (ShipClass.Fighter, "Pebble"));
            fleet.Group(Reach, jd, ShipClass.Battleship, "Long Night", new[] { "Titan", "Titania", "Triton" },
                (ShipClass.Cruiser, "Umbra"), (ShipClass.Cruiser, "Perihelion"), (ShipClass.Destroyer, "Wisp"),
                (ShipClass.Fighter, "Nightjar"), (ShipClass.Fighter, "Moth"));
            fleet.Group(Reach, jd, ShipClass.Carrier, "Endurance", new[] { "Triton", "Charon", "Titan", "Ganymede" },
                (ShipClass.Cruiser, "Aphelion"), (ShipClass.Destroyer, "Rime"), (ShipClass.Destroyer, "Hoarfrost"),
                (ShipClass.Fighter, "Sleet"), (ShipClass.Fighter, "Driftglass"));
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

        void BuildMerchants(double jd, string[] freighters, string[] miners, string[] couriers, Faction[] owners, Func<int, Faction> minerOwner)
        {
            var ports = PortBodies();
            merchantNames = freighters;
            minerNames = miners;
            for (int i = 0; i < freighters.Length; i++)
                AddMerchant(freighters[i], ShipClass.Freighter, owners[i % owners.Length], ShipRole.Trader, ports, jd);
            for (int i = 0; i < miners.Length; i++)
                AddMerchant(miners[i], ShipClass.Miner, minerOwner(i), ShipRole.Miner, ports, jd);
            foreach (var name in couriers)
                AddMerchant(name, ShipClass.Courier, Independents, ShipRole.Courier, ports, jd);
        }

        string[] merchantNames = FreighterNames, minerNames = MinerNames;

        /// <summary>The worlds with a port a merchant may trade at (every settlement but a haven).</summary>
        public int[] PortBodies()
        {
            var list = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Kind != SettlementKind.Haven && !list.Contains(s.Body)) list.Add(s.Body);
            return list.ToArray();
        }

        /// <summary>Puts a new hull in space: out of its yard's orbit if the yard has one, else in a parking orbit.</summary>
        public void Launch(Ship ship, Settlement yard, double jd)
        {
            var slip = Infrastructure?.YardOf(yard);
            Fleet.Station(ship, yard.Body, jd, slip?.Orbit);
        }

        /// <summary>
        /// A battle has broken out at a body: merchants still on their way there change course for the
        /// nearest other port they can reach without a hyperlane (often turning back where they came from).
        /// </summary>
        void DivertAround(Battle b, double jd)
        {
            if (b.Site != null) return;
            var lanes = Fleet.Lanes;
            foreach (var s in Fleet.Ships)
            {
                if (!IsTrader(s) || s.Destroyed || s.Phase != ShipPhase.Transit || s.Orbit == null || s.Orbit.Body != b.Body) continue;
                if (s.Journey != null && s.JourneyIndex < s.Journey.Count) continue;
                if (s.PhaseEndJd - jd < 0.05 || s.Ports == null) continue;
                var here = Fleet.StateOf(s, jd).HelioKm;
                int best = -1;
                double bestKm = double.MaxValue;
                foreach (int p in s.Ports)
                {
                    if (p == b.Body || War.BattleAt(p) != null) continue;
                    bool reachable = lanes == null || lanes.SystemOf(p) == lanes.SystemOf(b.Body) || lanes.SystemOf(p) == lanes.SystemOf(s.OriginBody);
                    if (!reachable) continue;
                    double km = (Ephemeris.HelioAt(p, jd) - here).Length;
                    if (km < bestKm)
                    {
                        bestKm = km;
                        best = p;
                    }
                }
                if (best < 0 || !Fleet.Divert(s, best, jd)) continue;
                s.Cargo = s.HasCargo ? s.Cargo : "Empty, diverted";
                Log(jd, $"{s.Name} turns aside for {Catalog[best].Name}: fighting at {Catalog[b.Body].Name}", WorldEventKind.Info, best);
            }
        }

        /// <summary>Where a free ship may go: any visible world but a star -- and but a hidden haven's rock,
        /// which nobody knows to go to. Shared by the free roamers, sandbox spawns and gate arrivals.</summary>
        public int[] FreeShipPorts()
        {
            var hidden = new HashSet<int>();
            foreach (var s in Economy.Settlements)
                if (s.Hidden) hidden.Add(s.Body);
            var anywhere = new List<int>();
            foreach (var b in Catalog.Bodies)
                if (b.IsVisible && !b.IsStar && !hidden.Contains(b.Index)) anywhere.Add(b.Index);
            return anywhere.ToArray();
        }

        /// <summary>
        /// Independent pilots who go where they please: yachts and survey ships (fast courier hulls) and
        /// prospectors (ore haulers), roaming every world in the system, not just its ports.
        /// </summary>
        void AddFreeShips(double jd, string[] yachts, string[] prospectors)
        {
            var places = FreeShipPorts();
            void Add(string name, ShipClass cls)
            {
                var ship = Fleet.AddShip(name, cls, Independents, ShipRole.Free);
                ship.Ports = places;
                Fleet.Station(ship, places[Fleet.Rng.Next(places.Length)], jd);
            }
            foreach (var n in yachts) Add(n, ShipClass.Courier);
            foreach (var n in prospectors) Add(n, ShipClass.Miner);
        }

        Ship AddMerchant(string name, ShipClass cls, Faction owner, ShipRole role, int[] ports, double jd, int at = -1, Settlement yard = null)
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
            if (yard != null) Launch(ship, yard, jd);
            else Fleet.Station(ship, at, jd);
            return ship;
        }

        // ---------------------------------------------------------------- shipbuilding

        public int TraderCount()
        {
            int n = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && IsTrader(s)) n++;
            return n;
        }

        /// <summary>Yards build merchant hulls while the merchant fleet is under strength.</summary>
        ShipClass? ChooseBuild(Settlement yard)
        {
            if (TraderCount() >= MerchantCap) return null;
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
                var names = cls == ShipClass.Miner ? minerNames : merchantNames;
                string name = names[builtCount % names.Length] + " " + Roman(2 + builtCount / names.Length);
                ship = AddMerchant(name, cls, yard.Owner, role, PortBodies(), jd, yard.Body, yard);
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

        /// <summary>An adventure offered to the player, or how one ended (see <see cref="EventDirector"/>).</summary>
        Opportunity,
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
