using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum SettlementKind
    {
        Colony,
        Station,
        MiningOutpost,
        Haven,
    }

    /// <summary>
    /// A populated place with a market: a colony on a world, a station in orbit, a mining outpost or a
    /// pirate haven. It produces through its facilities, consumes through its people and industry,
    /// and prices every good from how its stock compares with what it uses.
    /// </summary>
    public sealed class Settlement
    {
        public int Id;
        public string Name;
        public int Body;
        public SettlementKind Kind;
        public Faction Owner;
        public double Population;
        public double InitialPopulation;
        public readonly List<Facility> Facilities = new List<Facility>();

        public readonly double[] Stock = new double[GoodInfo.Count];
        public readonly double[] Price = new double[GoodInfo.Count];

        /// <summary>Tonnes already bought for delivery here by ships still on their way.</summary>
        public readonly double[] Inbound = new double[GoodInfo.Count];

        /// <summary>Full-capacity daily use (people plus facility inputs) and production, tonnes.</summary>
        public readonly double[] Demand = new double[GoodInfo.Count];
        public readonly double[] Supply = new double[GoodInfo.Count];

        /// <summary>Daily use by things other than people and industry (navies refitting and rearming here), tonnes.</summary>
        public readonly double[] ExtraDemand = new double[GoodInfo.Count];

        /// <summary>
        /// Ships' propellant bought here, tonnes a day, smoothed over about <see cref="Economy.PropellantDemandDays"/>:
        /// reaction mass (water, hydrocarbons) and the helium-3 that burns it, part of what the market expects to sell.
        /// </summary>
        public readonly double[] PropellantDemand = new double[GoodInfo.Count];

        /// <summary>Propellant sold since the last market tick, tonnes (folded into <see cref="PropellantDemand"/>).</summary>
        public readonly double[] PropellantSold = new double[GoodInfo.Count];

        /// <summary>Share of the last tick's needs that was met, per good (1 where nothing was needed).</summary>
        public readonly double[] Satisfaction = new double[GoodInfo.Count];

        /// <summary>Build points banked by a shipyard, spent when a hull is laid down.</summary>
        public double BuildPoints;

        /// <summary>The hull the yard is working toward (what it last chose to build), or null.</summary>
        public ShipClass? OnSlip;

        /// <summary>Days since the owner last changed; production is disrupted for a while after a capture.</summary>
        public double DaysSinceCapture = 9999;

        public double TradeVolumeCredits;

        /// <summary>Orbital defence platforms (gun stations) that fight any hostile force at this body.</summary>
        public readonly List<ShipCombat> Platforms = new List<ShipCombat>();
        public int MaxPlatforms;

        public Facility Shipyard
        {
            get
            {
                foreach (var f in Facilities)
                    if (f.Recipe.Kind == IndustryKind.Shipyard) return f;
                return null;
            }
        }

        public bool HasShipyard => Shipyard != null;

        /// <summary>
        /// A hidden haven is off every chart: nobody knows it is there until a ship's scanners pass
        /// within <see cref="War.ScanRangeKm"/> of it or a navy follows raiders home (see
        /// <see cref="War.Discover"/>). An ordinary settlement is known to all.
        /// </summary>
        public bool Hidden;

        /// <summary>The factions that have found this hidden haven.</summary>
        public readonly HashSet<Faction> KnownTo = new HashSet<Faction>();

        /// <summary>On the map: not hidden, or found by somebody (a find is news across the system).</summary>
        public bool Discovered => !Hidden || KnownTo.Count > 0;

        /// <summary>Whether <paramref name="f"/> knows where this settlement is.</summary>
        public bool KnownBy(Faction f) => !Hidden || (f != null && KnownTo.Contains(f));

        /// <summary>A haven that has been taken or destroyed: out of action (no raiders sail from it, its own
        /// raiders lie low) until the pirates rebuild it at <see cref="RebuildJd"/>.</summary>
        public bool Fallen;
        public double RebuildJd;

        /// <summary>Days of use a market likes to hold; prices are base at this cover.</summary>
        public const double CoverDays = 20;

        /// <summary>Warehouses hold this many days of flow; production stops when they are full.</summary>
        public const double StorageDays = 90;

        /// <summary>
        /// Daily per-person needs, tonnes, after recycling, for this kind of settlement (see
        /// <see cref="People.NeedsOf"/>: food, water lost from a closed loop, nitrogen top-up for air
        /// that leaks away, medical supplies, and the upkeep of the habitat itself).
        /// </summary>
        public (Good good, double perPersonDay)[] PersonNeeds => People.NeedsOf(Kind);

        /// <summary>People of working age in work or looking for it (<see cref="People.LabourForceShare"/>).</summary>
        public double Workforce => Population * People.LabourForceShare;

        /// <summary>Workers the facilities need at their current levels.</summary>
        public double Jobs
        {
            get
            {
                double jobs = 0;
                foreach (var f in Facilities) jobs += f.Workers;
                return jobs;
            }
        }

        /// <summary>Share of the facilities' jobs filled over the last tick (1 where there are none).</summary>
        public double Staffing = 1;

        /// <summary>Credits a day paid to the people working the facilities, over the last tick.</summary>
        public double WageBill;

        /// <summary>Credits a day the owner collected in tax on those wages, over the last tick.</summary>
        public double WageTax;

        /// <summary>
        /// What a member of the labour force here can expect to earn a day: the facility wage times the
        /// chance of a facility job, the rest at the service wage (<see cref="People.ExpectedEarnings"/>).
        /// Set at each review.
        /// </summary>
        public double Earnings;

        /// <summary>
        /// How well people here have been living, 0..1: food and water met, and medicine, smoothed over
        /// about a month (<see cref="People.WellbeingDays"/>). 1 for a settlement that has wanted for nothing.
        /// </summary>
        public double Wellbeing = 1;

        /// <summary>Medical needs met, smoothed the same way as <see cref="Wellbeing"/>.</summary>
        public double HealthWellbeing = 1;

        /// <summary>How strongly this place pulls migrants (living standard times earnings), set at each review.</summary>
        public double Attraction = 1;

        /// <summary>Net people a day arriving (positive) or leaving (negative) by migration, over the last review.</summary>
        public double NetMigration;

        public double Flow(Good g) => Math.Max(Demand[(int)g], Supply[(int)g]);

        /// <summary>The stock a market wants on hand: <see cref="CoverDays"/> of its larger flow.</summary>
        public double Target(Good g) => Flow(g) * CoverDays;

        public double Capacity(Good g) => Math.Max(Flow(g) * StorageDays, 1);

        /// <summary>Price at a hypothetical stock level, with the same curve the market uses.</summary>
        public double PriceAt(Good g, double stock)
        {
            int i = (int)g;
            double basePrice = GoodInfo.Of(g).BasePrice;
            double target = Target(g);
            if (target <= 0) return basePrice * 0.25;
            double ratio = target / (Math.Max(stock, 0) + 0.12 * target);
            double p = basePrice * Math.Pow(ratio, 0.75);
            // A market that only produces a good sells it at a discount: nobody local wants it.
            if (Demand[i] <= 0) p *= 0.6;
            return Math.Max(basePrice * 0.2, Math.Min(basePrice * 6, p));
        }

        public void RefreshPrices()
        {
            for (int i = 0; i < GoodInfo.Count; i++) Price[i] = PriceAt((Good)i, Stock[i]);
        }

        /// <summary>Tonnes a trader can buy without dipping into the local reserve.</summary>
        public double Available(Good g)
        {
            int i = (int)g;
            double reserve = Demand[i] > 0 ? Target(g) * 0.35 : 0;
            return Math.Max(0, Stock[i] - reserve);
        }

        /// <summary>Tonnes this market would still take before it is over-supplied, counting deliveries on the way.</summary>
        public double Absorb(Good g)
        {
            int i = (int)g;
            if (Demand[i] <= 0) return 0;
            return Math.Max(0, Target(g) * 1.6 - Stock[i] - Inbound[i]);
        }

        /// <summary>Credits for selling <paramref name="tonnes"/>, walking the price down as stock rises.</summary>
        public double SaleValue(Good g, double tonnes)
        {
            const int steps = 6;
            double value = 0, stock = Stock[(int)g];
            for (int k = 0; k < steps; k++)
            {
                double chunk = tonnes / steps;
                value += PriceAt(g, stock + chunk * 0.5) * chunk;
                stock += chunk;
            }
            return value * 0.97;
        }

        /// <summary>Credits for buying <paramref name="tonnes"/>, walking the price up as stock falls.</summary>
        public double PurchaseCost(Good g, double tonnes)
        {
            const int steps = 6;
            double value = 0, stock = Stock[(int)g];
            for (int k = 0; k < steps; k++)
            {
                double chunk = tonnes / steps;
                value += PriceAt(g, stock - chunk * 0.5) * chunk;
                stock -= chunk;
            }
            return value * 1.03;
        }

        public override string ToString() => Name;
    }
}
