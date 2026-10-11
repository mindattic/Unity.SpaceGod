using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum IndustryKind
    {
        Mine,
        Farm,
        PharmaLab,
        Smelter,
        ElectronicsFab,
        MachineWorks,
        MunitionsPlant,
        Shipyard,
    }

    /// <summary>A production recipe: tonnes per day at level 1. Mines take their output from the site.</summary>
    public sealed class Recipe
    {
        public IndustryKind Kind { get; }
        public string Name { get; }
        public (Good good, double perDay)[] Inputs { get; }
        public (Good good, double perDay)[] Outputs { get; }

        /// <summary>
        /// Per input: true if it is a bulk feedstock that caps output outright; false for a consumable
        /// (fuel, spares) whose shortage only cuts efficiency, down to <see cref="MinorFloor"/>.
        /// </summary>
        public bool[] Limiting { get; }

        public const double MinorFloor = 0.55;

        public Recipe(IndustryKind kind, string name, (Good, double)[] inputs, (Good, double)[] outputs)
        {
            Kind = kind;
            Name = name;
            Inputs = inputs;
            Outputs = outputs;
            double total = 0;
            foreach (var (_, rate) in inputs) total += rate;
            Limiting = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) Limiting[i] = kind != IndustryKind.Mine && inputs[i].Item2 >= total * 0.02;
        }

        static readonly Dictionary<IndustryKind, Recipe> Fixed = new Dictionary<IndustryKind, Recipe>
        {
            [IndustryKind.Farm] = new Recipe(IndustryKind.Farm, "Hydroponic farms",
                new[] { (Good.Water, 700.0), (Good.Nitrogen, 60.0), (Good.HeliumThree, 0.4) },
                new[] { (Good.Food, 420.0) }),
            [IndustryKind.PharmaLab] = new Recipe(IndustryKind.PharmaLab, "Pharmaceutical labs",
                new[] { (Good.Water, 60.0), (Good.Hydrocarbons, 50.0), (Good.RareEarths, 0.6) },
                new[] { (Good.MedicalSupplies, 22.0) }),
            [IndustryKind.Smelter] = new Recipe(IndustryKind.Smelter, "Smelters",
                new[] { (Good.NickelIron, 2200.0), (Good.Titanium, 90.0), (Good.HeliumThree, 0.8) },
                new[] { (Good.Alloys, 800.0) }),
            [IndustryKind.ElectronicsFab] = new Recipe(IndustryKind.ElectronicsFab, "Electronics fabs",
                new[] { (Good.Copper, 160.0), (Good.Zinc, 40.0), (Good.RareEarths, 9.0), (Good.Platinum, 1.2), (Good.Alloys, 25.0) },
                new[] { (Good.Electronics, 55.0) }),
            [IndustryKind.MachineWorks] = new Recipe(IndustryKind.MachineWorks, "Machine works",
                new[] { (Good.Alloys, 280.0), (Good.Electronics, 18.0), (Good.Copper, 40.0) },
                new[] { (Good.Machinery, 190.0) }),
            [IndustryKind.MunitionsPlant] = new Recipe(IndustryKind.MunitionsPlant, "Munitions plants",
                new[] { (Good.Alloys, 90.0), (Good.Hydrocarbons, 60.0), (Good.Electronics, 8.0) },
                new[] { (Good.Munitions, 55.0) }),
            // A yard turns materials into build points (1 per tonne of alloys) rather than a good.
            [IndustryKind.Shipyard] = new Recipe(IndustryKind.Shipyard, "Shipyard",
                new[] { (Good.Alloys, 300.0), (Good.Electronics, 25.0), (Good.Machinery, 40.0) },
                new (Good, double)[0]),
        };

        public static Recipe Of(IndustryKind kind) => Fixed[kind];

        public static Recipe Mine(Good ore, double perDay) =>
            new Recipe(IndustryKind.Mine, GoodInfo.Of(ore).Name + " extraction",
                new[] { (Good.Machinery, perDay * 0.0012), (Good.HeliumThree, perDay * 0.00008) },
                new[] { (ore, perDay) });
    }

    /// <summary>One industry at a settlement. Level scales the recipe; it drifts with profitability.</summary>
    public sealed class Facility
    {
        public Recipe Recipe;
        public double Level;
        public double BaseLevel;

        /// <summary>Fraction of capacity achieved over the last tick (inputs or storage permitting).</summary>
        public double Utilisation;

        public Facility(Recipe recipe, double level)
        {
            Recipe = recipe;
            Level = BaseLevel = level;
        }
    }
}
