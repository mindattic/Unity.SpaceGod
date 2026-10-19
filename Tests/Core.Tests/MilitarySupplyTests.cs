using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// U.SPC-T93: military logistics. War fleets burn propellant, munitions and spares; naval bases send supply ships after
    /// them over the gates, bought from the base's market; a fleet out of supply loses readiness (rate of fire, repair,
    /// morale) and withdraws; a cut gate or a blockaded base starves the fleets behind it.
    /// </summary>
    [TestFixture]
    public class MilitarySupplyTests
    {
        const double StartJd = 2461324.0;

        static SystemSpec Spec(string name) => NearbySystems.All().First(s => s.Name == name);

        /// <summary>Alpha Centauri, Barnard's Star and Luhman 16 on a line of gates (as in <see cref="InterstellarWarTests"/>).</summary>
        static Galaxy Line(out StarSystem ac, out StarSystem barnard, out StarSystem luhman)
        {
            var g = new Galaxy();
            ac = StarSystem.CreateAlphaCentauri(StartJd, g.Polities);
            barnard = StarSystem.CreateSettled(Spec("Barnard's Star"), StartJd, g.Polities);
            luhman = StarSystem.CreateSettled(Spec("Luhman 16"), StartJd, g.Polities);
            g.Systems.Add(ac);
            g.Systems.Add(barnard);
            g.Systems.Add(luhman);
            var net = new GateNetwork();
            net.Link(ac, barnard);
            net.Link(barnard, luhman);
            net.AssignOwners();
            g.Connect(StartJd, net);
            g.Wars.AutoDeclare = false;
            return g;
        }

        static double Run(Galaxy g, double fromJd, double days, Func<bool> done, double step = 0.05)
        {
            double jd = fromJd;
            while (jd < fromJd + days && !done())
            {
                jd += step;
                g.Update(jd);
            }
            return jd;
        }

        // ------------------------------------------------------------ stores and readiness

        [Test]
        public void StoresDrainFasterOnTheMarchAndInBattleThanLyingInASystem()
        {
            var idle = new FleetSupply();
            var march = new FleetSupply();
            var fight = new FleetSupply();
            idle.Drain(10, moving: false, fighting: false);
            march.Drain(10, moving: true, fighting: false);
            fight.Drain(2, moving: false, fighting: true);

            Assert.That(idle.Propellant, Is.EqualTo(1 - 10 / FleetSupply.IdleDays).Within(1e-9));
            Assert.That(march.Propellant, Is.EqualTo(1 - 10 / FleetSupply.MarchDays).Within(1e-9));
            Assert.That(march.Propellant, Is.LessThan(idle.Propellant), "marching burns more propellant than station-keeping");
            Assert.That(idle.Munitions, Is.EqualTo(1), "no shot fired, none spent");
            Assert.That(fight.Munitions, Is.EqualTo(1 - 2 / FleetSupply.BattleDays).Within(1e-9));
            Assert.That(idle.Spares, Is.EqualTo(1 - 10 / FleetSupply.WearDays).Within(1e-9), "spares wear away regardless");

            // Run dry, and nothing goes below empty.
            march.Drain(1000, true, true);
            Assert.That(march.Propellant, Is.EqualTo(0));
            Assert.That(march.Munitions, Is.EqualTo(0));
            Assert.That(march.Spares, Is.EqualTo(0));
        }

        [Test]
        public void ReadinessFallsWithTheLowestStoreAndARunDownFleetMustWithdraw()
        {
            var s = new FleetSupply();
            Assert.That(s.Readiness, Is.EqualTo(1));
            Assert.That(s.MustWithdraw, Is.False);

            s.Munitions = FleetSupply.LowSupply;
            Assert.That(s.Readiness, Is.EqualTo(1), "a store at the low mark does not yet tell");
            s.Munitions = FleetSupply.LowSupply / 2;
            Assert.That(s.Readiness, Is.EqualTo(FleetSupply.ReadinessFloor + (1 - FleetSupply.ReadinessFloor) / 2).Within(1e-9));
            s.Munitions = 1;
            s.Propellant = 0;
            Assert.That(s.Readiness, Is.EqualTo(FleetSupply.ReadinessFloor), "dry tanks alone are enough to cripple it");
            Assert.That(s.MustWithdraw, Is.True);

            // A convoy on its way: it holds on for it.
            s.Convoy = new SupplyConvoy();
            Assert.That(s.MustWithdraw, Is.False);
        }

        [Test]
        public void FieldRepairsEatSparesAndGoSlowerOutOfSupply()
        {
            var full = new FleetSupply();
            double mended = full.FieldRepair(0.3, hulls: 1);
            Assert.That(mended, Is.EqualTo(FleetSupply.FieldRepairPerDay).Within(1e-9));
            Assert.That(full.Spares, Is.EqualTo(1 - mended * FleetSupply.SparesPerRepair).Within(1e-9), "mending is paid for in spares");

            var starved = new FleetSupply { Propellant = 0 };
            Assert.That(starved.FieldRepair(0.3, 1), Is.EqualTo(FleetSupply.FieldRepairPerDay * FleetSupply.ReadinessFloor).Within(1e-9), "a starved fleet mends slower");

            var noSpares = new FleetSupply { Spares = 0 };
            Assert.That(noSpares.FieldRepair(0.3, 1), Is.EqualTo(0), "nothing to mend with");
        }

        [Test]
        public void ADeliveryRefillsTheStoresUpToAFullLoad()
        {
            var s = new FleetSupply { Propellant = 0.2, Munitions = 0.5, Spares = 0.9 };
            s.Deliver(new SupplyConvoy { Propellant = 0.8, Munitions = 0.3, Spares = 0.5 });
            Assert.That(s.Propellant, Is.EqualTo(1).Within(1e-9));
            Assert.That(s.Munitions, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(s.Spares, Is.EqualTo(1), "no more than a full load");
        }

        // ------------------------------------------------------------ penalties in battle

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

        static bool IsShot(FxKind k) => k == FxKind.Beam || k == FxKind.BeamMiss || k == FxKind.Railgun || k == FxKind.RailgunMiss || k == FxKind.Guns ||
                                         k == FxKind.MissileLaunch || k == FxKind.PointDefense;

        [Test]
        public void AFleetOutOfSupplyFiresSlowerInBattle()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, 7);
            b.AddShip(Hull(ShipClass.Battleship, blue, 0), 0, DVec3.Zero, DVec3.UnitX);
            b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(20, 0, 0), -DVec3.UnitX);
            var starved = b.Units[0];
            var supplied = b.Units[1];
            starved.Combat.Readiness = FleetSupply.ReadinessFloor;
            int[] shots = new int[2];
            for (int i = 0; i < 1200; i++)
            {
                b.Advance(0.1);
                foreach (var fx in b.Fx)
                    if (IsShot(fx.Kind) && fx.Side >= 0 && fx.Side < 2) shots[fx.Side]++;
                b.Fx.Clear();
            }
            TestContext.Out.WriteLine($"shots: starved {shots[0]}, supplied {shots[1]}");
            Assert.That(shots[1], Is.GreaterThan(20), "the supplied ship fought");
            Assert.That(shots[0], Is.LessThan(shots[1] * 0.6), "the ship out of supply reloads slower");
        }

        [Test]
        public void ACrewOutOfSupplyIsQuickerToStrike()
        {
            Assert.That(Morale.SupplyResolve(1), Is.EqualTo(1));
            Assert.That(Morale.SupplyResolve(FleetSupply.ReadinessFloor), Is.LessThan(0.6));
            var navy = new Faction("Navy", "NAV", new Rgb(0, 0, 1)) { IsMilitary = true };
            double resolve = Morale.Resolve(navy, true);
            // Battered and outgunned alike, the starved crew strikes where the supplied one fights on.
            const double toll = 0.45, outgunned = 0.4, threshold = 0.35;
            Assert.That(Morale.StrikesColours(toll, outgunned, resolve, threshold), Is.False);
            Assert.That(Morale.StrikesColours(toll, outgunned, resolve * Morale.SupplyResolve(FleetSupply.ReadinessFloor), threshold), Is.True);
        }

        // ------------------------------------------------------------ the supply line

        /// <summary>A war between the Accord and the Authority for Luhman 16, and the Accord's strike group out on campaign.</summary>
        static (Conflict war, Campaign c) Campaign(Galaxy g, StarSystem ac, StarSystem luhman, out double jd)
        {
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            ac.World.Fleet.Group(accord, StartJd, ShipClass.Battleship, "Long Reach", new[] { "Rigil Gate" },
                (ShipClass.Cruiser, "Vanguard"), (ShipClass.Destroyer, "Spur"), (ShipClass.Destroyer, "Rowel"));
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            jd = Run(g, StartJd, 10, () => war.Campaigns.Count > 0);
            Assert.That(war.Campaigns, Is.Not.Empty, "no campaign set out");
            return (war, war.Campaigns[0]);
        }

        [Test]
        public void ANavalBaseSuppliesItsFleetOverOpenGatesFromItsOwnMarket()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var bases = ac.World.Economy.Settlements.Where(s => s.Owner == accord && s.HasShipyard).ToList();
            Assert.That(bases, Is.Not.Empty, "the Accord keeps a naval base at home");

            // From home, a base reaches Barnard's Star over the open gates; Luhman 16, behind the Authority's shut gate, it cannot.
            var (station, route) = g.Wars.Logistics.LineTo(accord, barnard, StartJd);
            Assert.That(station, Is.Not.Null);
            Assert.That(bases, Does.Contain(station));
            Assert.That(route, Has.Count.EqualTo(1));
            Assert.That(g.Wars.Logistics.LineTo(accord, ac, StartJd).Route, Is.Empty, "at home, no gate between");
            Assert.That(g.Wars.Declare(accord, luhman.World.Sol, StartJd, luhman), Is.Not.Null);
            Assert.That(g.Wars.Logistics.LineTo(accord, luhman, StartJd).Base, Is.Null, "behind the enemy's gate, out of supply");

            // A blockaded base supplies nobody; with every one blockaded, the fleets in Barnard's Star are starved.
            foreach (var s in bases) Assert.That(g.Polities.Diplomacy.DeclareBlockade(s, luhman.World.Sol), Is.True);
            Assert.That(g.Wars.Logistics.LineTo(accord, barnard, StartJd).Base, Is.Null, "the bases are blockaded");
            foreach (var s in bases) g.Polities.Diplomacy.LiftBlockade(s);
            Assert.That(g.Wars.Logistics.LineTo(accord, barnard, StartJd).Base, Is.Not.Null);

            // Cutting the gate between home and Barnard's Star starves it too.
            var link = g.Network.Between(ac, barnard);
            link.AtB.Damage(1);
            Assert.That(g.Wars.Logistics.LineTo(accord, barnard, StartJd).Base, Is.Null, "the gate is cut");
        }

        [Test]
        public void ARunDownFleetIsResuppliedByConvoyFromItsBaseAndTheEconomyPaysForIt()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var (war, c) = Campaign(g, ac, luhman, out double jd);
            var accord = c.Faction;
            Assert.That(c.Supply.Propellant, Is.LessThanOrEqualTo(1));

            // Run its stores down on the way out (it has not yet reached the enemy's gate: its way home is open).
            c.Supply.Propellant = 0.4;
            c.Supply.Munitions = 0.4;
            c.Supply.Spares = 0.4;
            var bases = ac.World.Economy.Settlements.Where(s => s.Owner == accord && s.HasShipyard).ToList();
            double munitionsBefore = bases.Sum(s => s.Stock[(int)Good.Munitions]);
            double treasuryBefore = accord.Treasury;
            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add(e.Text);

            jd = Run(g, jd, 2, () => c.Supply.Convoy != null);
            var convoy = c.Supply.Convoy;
            Assert.That(convoy, Is.Not.Null, "no supply ships sent");
            Assert.That(bases, Does.Contain(convoy.Base));
            Assert.That(convoy.Tankers, Is.GreaterThan(0));
            Assert.That(convoy.Colliers, Is.GreaterThan(0));
            Assert.That(convoy.Munitions, Is.GreaterThan(0));
            Assert.That(log.Any(l => l.Contains("tanker") && l.Contains("collier")), Is.True, "the base's sending is logged");
            Assert.That(bases.Sum(s => s.Stock[(int)Good.Munitions]), Is.LessThan(munitionsBefore), "the munitions come off the base's market");
            Assert.That(accord.Treasury, Is.LessThan(treasuryBefore), "and the navy pays for them");

            double before = c.Supply.Lowest;
            Run(g, jd, 30, () => c.Supply.ConvoysArrived > 0 || !c.Active);
            TestContext.Out.WriteLine(string.Join("\n", log));
            Assert.That(c.Supply.ConvoysArrived, Is.GreaterThanOrEqualTo(1), "the convoy never came up with the fleet");
            Assert.That(c.Supply.Lowest, Is.GreaterThan(before), "its stores are filled again");
            Assert.That(c.Supply.Readiness, Is.EqualTo(1));
        }

        [Test]
        public void AFleetCutOffBehindABrokenGateRunsDownAndWithdraws()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var (war, c) = Campaign(g, ac, luhman, out double jd);
            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add($"{e.Jd - StartJd,7:0.00} [{s.Name}] {e.Text}");

            // Into Barnard's Star, then the gate behind it is cut: no base can reach it.
            jd = Run(g, jd, 60, () => g.Systems.Any(s => s != ac && s.World.Fleet.Ships.Any(x => !x.Destroyed && x.Voyage?.Campaign == c)));
            Assert.That(g.Systems.Any(s => s != ac && s.World.Fleet.Ships.Any(x => !x.Destroyed && x.Voyage?.Campaign == c)), Is.True, "the fleet never left home");
            g.Network.Between(ac, barnard).AtA.Damage(1);
            // Low on stores already: what it has will not last.
            c.Supply.Munitions = 0.35;
            c.Supply.Spares = 0.35;
            c.Supply.Propellant = 0.35;

            jd = Run(g, jd, 60, () => c.Stage == CampaignStage.Returning || !c.Active);
            TestContext.Out.WriteLine(string.Join("\n", log));
            Assert.That(c.Supply.OutOfSupply, Is.True, "the fleet is cut off");
            Assert.That(log.Any(l => l.Contains("out of supply")), Is.True);
            Assert.That(c.Supply.Readiness, Is.LessThan(FleetSupply.WithdrawReadiness));
            Assert.That(c.Stage, Is.EqualTo(CampaignStage.Returning), "it withdraws");
            Assert.That(log.Any(l => l.Contains("turns for home, out of supply")), Is.True);
            // Its hulls carry its readiness into any fight.
            var flagship = g.Systems.SelectMany(s => s.World.Fleet.Ships).FirstOrDefault(x => !x.Destroyed && x.Voyage?.Campaign == c);
            if (flagship != null) Assert.That(flagship.Combat.Readiness, Is.EqualTo(c.Supply.Readiness).Within(0.05));
        }

        [Test]
        public void AConvoyThatFindsTheWayCutBehindTheFleetCannotGetThrough()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var (war, c) = Campaign(g, ac, luhman, out double jd);
            Ship Flagship() => barnard.World.Fleet.Ships.FirstOrDefault(x => !x.Destroyed && !x.IsEscort && x.Voyage?.Campaign == c);
            jd = Run(g, jd, 60, () => Flagship() != null);
            var leader = Flagship();
            Assert.That(leader, Is.Not.Null, "the fleet never came through Barnard's Star");

            // In Barnard's Star, a gate from home: its base sends after it.
            var logistics = g.Wars.Logistics;
            int arrived = c.Supply.ConvoysArrived, lost = c.Supply.ConvoysLost;
            if (c.Supply.Convoy != null) logistics.Ended(c);
            c.Supply.Munitions = 0.5;
            logistics.Day(c, barnard, leader, jd);
            var convoy = c.Supply.Convoy;
            Assert.That(convoy, Is.Not.Null, "no supply ships sent");
            Assert.That(convoy.From, Is.SameAs(ac));
            Assert.That(logistics.Convoys, Does.Contain(convoy));
            double munitions = c.Supply.Munitions;

            // The gate behind the fleet is cut while the supply ships are on their way: when they are due, they cannot get through.
            g.Network.Between(ac, barnard).AtB.Damage(1);
            convoy.DueJd = jd + 1;
            logistics.Day(c, barnard, leader, jd + 1);
            Assert.That(c.Supply.ConvoysLost, Is.EqualTo(lost + 1), "the convoy got through a cut gate");
            Assert.That(c.Supply.ConvoysArrived, Is.EqualTo(arrived));
            Assert.That(c.Supply.Munitions, Is.LessThanOrEqualTo(munitions), "nothing delivered");
            Assert.That(c.Supply.OutOfSupply, Is.True);
            Assert.That(c.Supply.Convoy, Is.Null, "and no base can send another");
            Assert.That(logistics.Convoys, Does.Not.Contain(convoy));
        }
    }
}
