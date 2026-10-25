using System;
using System.Collections.Generic;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T505: navies and raiders fit the new weapons in their roles (<see cref="FleetPatterns"/>): phaser arrays on
    /// destroyers and cruisers, plasma cannons on battleships, the tachyon lance on a rare capital, antimatter torpedoes on
    /// frigates and capitals, flak and interceptors on destroyers, each by its owner's doctrine, wealth and yards.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class FleetPatternsTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static bool Carries(Ship s, SubsystemKind k) => s.Combat.Subsystems.Any(x => x.Kind == k);

        static readonly SubsystemKind[] RoleWeapons = { SubsystemKind.PhaserArray, SubsystemKind.PlasmaCannon, SubsystemKind.TachyonLance };

        /// <summary>Lays down <paramref name="n"/> hulls of each class at <paramref name="yard"/>.</summary>
        static List<Ship> Launch(World w, Settlement yard, int n, params ShipClass[] classes)
        {
            var list = new List<Ship>();
            for (int i = 0; i < n; i++)
                foreach (var c in classes)
                    list.Add(w.War.LaunchWarship(yard, c, StartJd));
            return list;
        }

        static Settlement GreatestYard(World w, Faction f) =>
            w.Economy.Settlements.Where(s => s.HasShipyard && s.Owner == f).OrderByDescending(s => s.Shipyard.Level).First();

        [Test]
        public void EveryRoleWeaponHasAPlaceOnItsClassesStockDesigns()
        {
            foreach (var p in FleetPatterns.Patterns)
            {
                var l = HullFrames.ForClass(p.Class).NewLoadout();
                Assert.That(FleetPatterns.Slots(l, p).Count, Is.EqualTo(p.Count), p.Name + ": the stock design has its places for the weapon");
                Assert.That(EnergyWeapons.RoleAllows(p.Weapon, p.Class), Is.True, p.Name);
            }
            Assert.That(FleetPatterns.For(ShipClass.Destroyer).Any(p => p.Weapon == ModuleKind.PhaserArray));
            Assert.That(FleetPatterns.For(ShipClass.Cruiser).Any(p => p.Weapon == ModuleKind.PhaserArray));
            Assert.That(FleetPatterns.For(ShipClass.Battleship).Any(p => p.Weapon == ModuleKind.PlasmaCannon));
            Assert.That(FleetPatterns.Patterns.Where(p => p.Weapon == ModuleKind.TachyonLance).All(p => TachyonLances.CanCarry(p.Class)), "the lance only on a capital");
            // The heavy warheads and the anti-fighter screen are in the stock designs themselves (U.SPC-T489).
            foreach (var c in new[] { ShipClass.Frigate, ShipClass.Battleship, ShipClass.Carrier })
                Assert.That(CombatSpec.Of(c).Layout.Any(d => d.Kind == SubsystemKind.AntimatterTorpedo), c.ToString());
            Assert.That(CombatSpec.Of(ShipClass.Destroyer).Layout.Count(d => d.Kind == SubsystemKind.FlakCannon), Is.EqualTo(2));
            Assert.That(CombatSpec.Of(ShipClass.Destroyer).Layout.Any(d => d.Kind == SubsystemKind.InterceptorMissile));
            foreach (var key in new[] { "patterns.wealth", "patterns.spread", "patterns.lanceShare" })
                Assert.That(SettingsRegistry.Default.Find(key), Is.Not.Null, key);
        }

        [Test]
        public void AiNaviesFieldEachWeaponInItsRole()
        {
            var w = CreateWorld();
            var navies = new[] { w.Sol, w.Belt, w.Reach };
            foreach (var f in navies) f.Treasury = 1e13;
            var built = new List<(Ship ship, Settlement yard)>();
            foreach (var f in navies)
            {
                var yard = GreatestYard(w, f);
                foreach (var s in Launch(w, yard, 16, ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Battleship, ShipClass.Carrier, ShipClass.Frigate))
                    built.Add((s, yard));
            }
            List<Ship> Of(ShipClass c) => built.Where(b => b.ship.Spec.Class == c).Select(b => b.ship).ToList();

            Assert.That(Of(ShipClass.Destroyer).Count(s => Carries(s, SubsystemKind.PhaserArray)), Is.GreaterThan(0), "destroyers carry phaser arrays");
            Assert.That(Of(ShipClass.Cruiser).Count(s => Carries(s, SubsystemKind.PhaserArray)), Is.GreaterThan(0), "cruisers carry phaser arrays");
            Assert.That(Of(ShipClass.Battleship).Count(s => Carries(s, SubsystemKind.PlasmaCannon)), Is.GreaterThan(0), "battleships carry plasma cannons");
            int lances = Of(ShipClass.Battleship).Count(s => Carries(s, SubsystemKind.TachyonLance));
            Assert.That(lances, Is.GreaterThan(0), "a rare capital carries a tachyon lance");
            Assert.That(lances, Is.LessThan(Of(ShipClass.Battleship).Count / 3), "and it stays rare");
            Assert.That(Of(ShipClass.Destroyer).Count(s => !Carries(s, SubsystemKind.PhaserArray)), Is.GreaterThan(0), "not every hull is built to the pattern");

            // Antimatter torpedoes on frigates and capitals, flak and interceptors on every destroyer.
            foreach (var c in new[] { ShipClass.Frigate, ShipClass.Battleship, ShipClass.Carrier })
                Assert.That(Of(c).All(s => Carries(s, SubsystemKind.AntimatterTorpedo)), Is.True, c + " carries antimatter torpedoes");
            Assert.That(Of(ShipClass.Destroyer).All(s => Carries(s, SubsystemKind.FlakCannon) && Carries(s, SubsystemKind.InterceptorMissile)), Is.True);

            // Each weapon only in its role, and the lance only from a yard that can build one.
            foreach (var (s, yard) in built)
            {
                var c = s.Spec.Class;
                if (Carries(s, SubsystemKind.PhaserArray)) Assert.That(c == ShipClass.Destroyer || c == ShipClass.Cruiser, Is.True, c + " with phasers");
                if (Carries(s, SubsystemKind.PlasmaCannon)) Assert.That(c, Is.EqualTo(ShipClass.Battleship));
                if (Carries(s, SubsystemKind.TachyonLance))
                {
                    Assert.That(TachyonLances.CanCarry(c), Is.True);
                    Assert.That(TachyonLances.YardBuilds(yard), Is.True);
                }
                var l = Outfitting.LoadoutOf(s);
                for (int i = 0; i < l.Slots.Count; i++)
                    Assert.That(Outfitting.CanFit(l, i, l.Fitted[i]), Is.True, s.Name + ": a pattern obeys each mount's type and size");
            }
        }

        [Test]
        public void AFleetsMixDiffersByDoctrineAndByFaction()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            command.SetBuild(w.Sol, BuildPlan.BattleLine);
            command.SetBuild(w.Belt, BuildPlan.Carriers);
            foreach (var f in new[] { w.Sol, w.Belt }) f.Treasury = 1e13;
            var line = Launch(w, GreatestYard(w, w.Sol), 24, ShipClass.Destroyer, ShipClass.Battleship);
            var carriers = Launch(w, GreatestYard(w, w.Belt), 24, ShipClass.Destroyer, ShipClass.Battleship);
            double Share(List<Ship> ships, ShipClass c, SubsystemKind k) =>
                ships.Count(s => s.Spec.Class == c && Carries(s, k)) / (double)ships.Count(s => s.Spec.Class == c);
            Assert.That(Share(line, ShipClass.Battleship, SubsystemKind.PlasmaCannon), Is.GreaterThan(Share(carriers, ShipClass.Battleship, SubsystemKind.PlasmaCannon)),
                "a battle line builds more brawlers");
            Assert.That(Share(carriers, ShipClass.Destroyer, SubsystemKind.PhaserArray), Is.GreaterThan(Share(line, ShipClass.Destroyer, SubsystemKind.PhaserArray)),
                "a carrier navy screens with more phaser arrays");

            // Under the same doctrine each navy still leans its own way.
            var leanings = new[] { w.Sol, w.Belt, w.Reach }.Select(f => FleetPatterns.Leaning(f, ModuleKind.PhaserArray, BuildPlan.Balanced)).ToList();
            Assert.That(leanings.Distinct().Count(), Is.GreaterThan(1));
            Assert.That(FleetPatterns.Leaning(w.Sol, ModuleKind.PhaserArray, BuildPlan.Balanced), Is.EqualTo(FleetPatterns.Leaning(w.Sol, ModuleKind.PhaserArray, BuildPlan.Balanced)),
                "and always the same way");
            Assert.That(FleetPatterns.Leaning(w.Sol, ModuleKind.PlasmaCannon, BuildPlan.BattleLine), Is.GreaterThan(FleetPatterns.Leaning(w.Sol, ModuleKind.PlasmaCannon, BuildPlan.Carriers)));
        }

        [Test]
        public void APoorNavyLaysDownThePlainStockDesign()
        {
            var w = CreateWorld();
            w.Sol.Treasury = 0;
            var ships = Launch(w, GreatestYard(w, w.Sol), 12, ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Battleship);
            foreach (var s in ships)
                foreach (var k in RoleWeapons)
                    Assert.That(Carries(s, k), Is.False, s.Name + " " + k);
            Assert.That(w.Sol.Treasury, Is.EqualTo(0), "and pays nothing for weapons it did not fit");
        }

        [Test]
        public void RaidersFitPhaserArraysButNeverALance()
        {
            var w = CreateWorld();
            w.Pirates.Treasury = 1e13;
            int deimos = w.Catalog["Deimos"].Index;
            int before = w.Fleet.Ships.Count;
            for (int i = 0; i < 16; i++) w.War.RaisePirateGroup(StartJd, deimos);
            var raiders = w.Fleet.Ships.Skip(before).Where(s => s.Faction == w.Pirates).ToList();
            Assert.That(raiders.Count(s => Carries(s, SubsystemKind.PhaserArray)), Is.GreaterThan(0), "raiders fit phaser arrays");
            Assert.That(raiders.Count(s => s.Spec.Class == ShipClass.Destroyer && !Carries(s, SubsystemKind.PhaserArray)), Is.GreaterThan(0));
            Assert.That(raiders.Any(s => Carries(s, SubsystemKind.TachyonLance) || Carries(s, SubsystemKind.PlasmaCannon)), Is.False);

            var poor = CreateWorld();
            poor.Pirates.Treasury = 0;
            before = poor.Fleet.Ships.Count;
            for (int i = 0; i < 8; i++) poor.War.RaisePirateGroup(StartJd, deimos);
            Assert.That(poor.Fleet.Ships.Skip(before).Any(s => Carries(s, SubsystemKind.PhaserArray)), Is.False, "a band with no plunder flies stock hulls");
        }

        // ---------------------------------------------------------------- refits

        static (Economy economy, Settlement yard) Yard(Faction owner)
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, owner, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 2));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions, Good.Platinum, Good.RareEarths }) yard.Stock[(int)g] = 1e6;
            yard.RefreshPrices();
            return (economy, yard);
        }

        static Ship Hull(ShipClass c, Faction f)
        {
            f.Treasury = 1e13;
            return new Ship { Id = 1, Name = c.ToString(), Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
        }

        static List<IPartSpec> RefitFully(Economy economy, Ship ship, Settlement yard, BuildPlan plan) =>
            Enumerable.Range(0, 60).Select(_ => Outfitting.AiRefit(economy, ship, yard, plan)).Where(p => p != null).ToList();

        [Test]
        public void NaviesRefitTheirOlderHullsWithTheWeaponsTheirDoctrineFavours()
        {
            double spread = FleetPatterns.Spread.Value;
            try
            {
                FleetPatterns.Spread.Set(0);
                var navy = new Faction("Refit Navy", "RN", default) { IsMilitary = true };
                var (economy, yard) = Yard(navy);

                var screen = Hull(ShipClass.Destroyer, navy);
                RefitFully(economy, screen, yard, BuildPlan.Carriers);
                Assert.That(Outfitting.LoadoutOf(screen).Count(ModuleKind.PhaserArray), Is.EqualTo(2), "a carrier navy's destroyers take phaser arrays");
                Assert.That(Carries(screen, SubsystemKind.PhaserArray), Is.True);

                var line = Hull(ShipClass.Battleship, navy);
                var fitted = RefitFully(economy, line, yard, BuildPlan.BattleLine);
                var l = Outfitting.LoadoutOf(line);
                Assert.That(l.Count(ModuleKind.PlasmaCannon), Is.EqualTo(2), "a battle line's battleships take plasma cannons");
                Assert.That(l.Count(ModuleKind.TachyonLance), Is.EqualTo(1));
                int lance = fitted.FindIndex(p => p is ModuleSpec m && m.Kind == ModuleKind.TachyonLance);
                int torch = fitted.FindIndex(p => p is ModuleSpec m && m.Kind == ModuleKind.TorchEngine);
                Assert.That(lance, Is.LessThan(torch), "a navy that favours the lance fits it before better drives");

                var plain = Hull(ShipClass.Destroyer, navy);
                RefitFully(economy, plain, yard, BuildPlan.BattleLine);
                Assert.That(Outfitting.LoadoutOf(plain).Count(ModuleKind.PhaserArray), Is.EqualTo(0), "a battle line keeps its destroyers' point defence");

                var carrierLine = Hull(ShipClass.Battleship, navy);
                fitted = RefitFully(economy, carrierLine, yard, BuildPlan.Carriers);
                Assert.That(Outfitting.LoadoutOf(carrierLine).Count(ModuleKind.PlasmaCannon), Is.EqualTo(0));
                lance = fitted.FindIndex(p => p is ModuleSpec m && m.Kind == ModuleKind.TachyonLance);
                torch = fitted.FindIndex(p => p is ModuleSpec m && m.Kind == ModuleKind.TorchEngine);
                Assert.That(lance, Is.GreaterThan(torch), "any other navy fits the lance last of all");
            }
            finally
            {
                FleetPatterns.Spread.Set(spread);
            }
        }
    }
}
