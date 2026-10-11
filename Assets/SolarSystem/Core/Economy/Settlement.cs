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

        /// <summary>Share of the last tick's needs that was met, per good (1 where nothing was needed).</summary>
        public readonly double[] Satisfaction = new double[GoodInfo.Count];

        /// <summary>Build points banked by a shipyard, spent when a hull is laid down.</summary>
        public double BuildPoints;

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

        /// <summary>Days of use a market likes to hold; prices are base at this cover.</summary>
        public const double CoverDays = 20;

        /// <summary>Warehouses hold this many days of flow; production stops when they are full.</summary>
        public const double StorageDays = 90;

        /// <summary>Daily per-capita (per person) needs, tonnes, after recycling.</summary>
        public static readonly (Good good, double perPersonDay)[] PersonNeeds =
        {
            (Good.Food, 0.0016),
            (Good.Water, 0.004),
            (Good.MedicalSupplies, 0.00002),
            (Good.HeliumThree, 0.0000025),
            (Good.Machinery, 0.000008),
            (Good.Electronics, 0.0000015),
        };

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
