using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The gate breaker (U.SPC-T478): a heavily armoured siege capital built to take a jump gate intact. It fights a gate's
    /// forts at close range with brawling batteries of plasma cannons and goes for the forts first, carries boarding teams and
    /// engineering teams that storm a silenced gate and make good the damage its ring has taken, and always has its own FTL
    /// drive. It never wrecks a gate (owner 2026-10-24).
    /// </summary>
    [TestFixture]
    public class GateBreakerTests
    {
        const double StartJd = 2461324.0;

        static readonly Faction Blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };

        static SystemSpec Spec(string name) => NearbySystems.All().First(s => s.Name == name);

        static Ship Hull(ShipClass c, Faction f, int id) => new Ship
        {
            Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c),
        };

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

        static readonly MethodInfo SeizeGate = typeof(InterstellarWars).GetMethod("SeizeGate", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo Siege = typeof(InterstellarWars).GetMethod("Siege", BindingFlags.NonPublic | BindingFlags.Instance);

        // ---------------------------------------------------------------- the class

        [Test]
        public void ItIsTheMostHeavilyArmouredHullWithBrawlingPlasmaBatteriesAndItsOwnFtl()
        {
            var gb = CombatSpec.Of(ShipClass.GateBreaker);
            var battleship = CombatSpec.Of(ShipClass.Battleship);
            Assert.That(ShipClassSpec.Of(ShipClass.GateBreaker).IsMilitary, Is.True);
            Assert.That(ShipClassSpec.Of(ShipClass.GateBreaker).DisplayName, Is.EqualTo("Gate breaker"));
            Assert.That(gb.Hull, Is.GreaterThan(battleship.Hull));
            foreach (ShipClass c in Enum.GetValues(typeof(ShipClass)))
                if (c != ShipClass.GateBreaker) Assert.That(gb.Armor, Is.GreaterThan(CombatSpec.Of(c).Armor), c + " is as heavily armoured");
            Assert.That(gb.MaxSpeed, Is.LessThan(battleship.MaxSpeed), "its plate makes it slower than a battleship");

            // Its siege guns are plasma cannons and nothing longer-ranged, besides its point defence and boarding pods.
            var guns = gb.Layout.Where(d => d.IsWeapon && d.Kind != SubsystemKind.PointDefense && d.Kind != SubsystemKind.BoardingPods).ToList();
            Assert.That(guns, Is.Not.Empty);
            Assert.That(guns.Select(d => d.Kind).Distinct(), Is.EqualTo(new[] { SubsystemKind.PlasmaCannon }));
            Assert.That(EnergyWeapons.RoleAllows(ModuleKind.PlasmaCannon, ShipClass.GateBreaker), Is.True, "plasma cannons are its weapon by role");

            // Its boarding and engineering teams: two boarding shuttles, a barracks and a siege engineering bay.
            Assert.That(gb.Shuttles, Is.EqualTo(2));
            var layout = FacilityLayouts.For(ShipClass.GateBreaker);
            Assert.That(FacilityLayouts.CountOf(layout, FacilityKinds.SiegeEngineering), Is.GreaterThan(0));
            Assert.That(FacilityLayouts.CountOf(layout, FacilityKinds.MarineHabitat), Is.GreaterThan(0));
            Assert.That(FacilityLayouts.CountOf(FacilityLayouts.For(ShipClass.Battleship), FacilityKinds.SiegeEngineering), Is.EqualTo(0),
                "only a gate breaker has a siege bay");

            // It always has its own FTL: it attacks the gates the rest of the fleet would use.
            Assert.That(FtlRoster.Of(ShipClass.GateBreaker), Is.EqualTo(FtlAccess.Own));
            Assert.That(CruiseDrives.Of(ShipClass.GateBreaker), Is.Not.Null);
            Assert.That(HullFrames.CategoryOf(ShipClass.GateBreaker), Is.EqualTo(HullCategory.Battleship));

            var hull = Hull(ShipClass.GateBreaker, Blue, 1);
            Assert.That(GateBreakers.Works(hull), Is.True);
            Assert.That(GateBreakers.Works(Hull(ShipClass.Battleship, Blue, 2)), Is.False);
        }

        // ---------------------------------------------------------------- in battle

        [Test]
        public void InAGateBattleItGoesForTheFortsFirst()
        {
            var g = Line(out _, out var barnard, out var luhman);
            var gate = luhman.GateTo(barnard);
            var breaker = new BattleUnit { Ship = Hull(ShipClass.GateBreaker, Blue, 1), Kind = UnitKind.Ship };
            var battleship = new BattleUnit { Ship = Hull(ShipClass.Battleship, Blue, 2), Kind = UnitKind.Ship };
            var fort = new BattleUnit { Kind = UnitKind.Platform, Fort = gate };
            var warship = new BattleUnit { Ship = Hull(ShipClass.Cruiser, luhman.World.Sol, 3), Kind = UnitKind.Ship };

            Assert.That(GateBreakers.TargetBias(breaker, fort), Is.EqualTo(GateBreakers.FortFocus.Value).And.GreaterThan(0));
            Assert.That(GateBreakers.TargetBias(breaker, warship), Is.EqualTo(0), "only a fort draws it");
            Assert.That(GateBreakers.TargetBias(battleship, fort), Is.EqualTo(0), "a battleship has no such preference");
            breaker.PlayerControlled = true;
            Assert.That(GateBreakers.TargetBias(breaker, fort), Is.EqualTo(0), "a player picks their own marks");
        }

        // ---------------------------------------------------------------- taking gates

        [Test]
        public void ItsTeamsStormASilencedGateAndTakeItIntactRepairingItsRing()
        {
            Assert.That(SeizeGate, Is.Not.Null);
            Assert.That(Siege, Is.Not.Null);
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            Assert.That(war, Is.Not.Null);

            // The Authority's gate, its forts already silenced and its ring sabotaged below working order.
            var gate = luhman.GateTo(barnard);
            gate.Forts.Clear();
            gate.Integrity = 0.3;
            Assert.That(gate.Working, Is.False);
            // And the warships that held it beaten off.
            foreach (var s in luhman.World.Fleet.Ships)
                if (s.Faction == authority && s.Spec.IsMilitary) s.Destroyed = true;

            var breaker = Hull(ShipClass.GateBreaker, accord, 9001);
            Assert.That(GateBreakers.Stormer(new[] { breaker }), Is.SameAs(breaker), "its boarding teams are aboard");
            breaker.Voyage = new Voyage { Purpose = VoyagePurpose.Campaign, Home = ac, Destination = luhman, Route = new List<JumpGate>(), Faction = accord, SetOutJd = StartJd };
            var c = new Campaign { War = war, Faction = accord, Home = ac, Goal = luhman, Stage = CampaignStage.Marching, SetOutJd = StartJd, Flagship = breaker.Name };

            // The first day beside it, its boarding and engineering teams storm it.
            Siege.Invoke(g.Wars, new object[] { c, luhman, breaker, gate, StartJd + 1 });
            Assert.That(gate.Owner, Is.SameAs(accord), "the gate breaker's teams did not take the gate the first day");
            Assert.That(gate.Wrecked, Is.False, "it takes gates intact");
            Assert.That(gate.Integrity, Is.EqualTo(1).Within(1e-9), "its engineers make good the ring's damage");
            Assert.That(gate.Working, Is.True);
        }

        [Test]
        public void WithoutAGateBreakerASeizedGateStaysDamaged()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var war = g.Wars.Declare(accord, luhman.World.Sol, StartJd, luhman);
            var gate = luhman.GateTo(barnard);
            gate.Integrity = 0.3;
            var c = new Campaign { War = war, Faction = accord, Home = ac, Goal = luhman, Stage = CampaignStage.Marching, SetOutJd = StartJd, Flagship = "Vanguard" };
            SeizeGate.Invoke(g.Wars, new object[] { c, gate, StartJd, false, null });
            Assert.That(gate.Owner, Is.SameAs(accord));
            Assert.That(gate.Integrity, Is.EqualTo(0.3).Within(1e-9));
        }

        [Test]
        public void ABreakerWithoutItsTeamsOrOutOfActionCannotStormAGate()
        {
            var breaker = Hull(ShipClass.GateBreaker, Blue, 1);
            breaker.Combat.Interior.TakeMarines(breaker.Combat.Interior.Marines - (GateBreakers.TeamMarines.Value - 1));
            Assert.That(GateBreakers.Stormer(new[] { breaker }), Is.Null, "too few marines for its boarding teams");

            var sunk = Hull(ShipClass.GateBreaker, Blue, 2);
            sunk.Destroyed = true;
            Assert.That(GateBreakers.Stormer(new[] { sunk }), Is.Null);
            Assert.That(GateBreakers.Stormer(new[] { Hull(ShipClass.Battleship, Blue, 3) }), Is.Null, "a battleship has no engineering teams");
        }

        // ---------------------------------------------------------------- building them

        [Test]
        public void ANavyAtWarWantsAGateBreaker()
        {
            Assert.That(GateBreakers.Wanted(Blue, true, 0), Is.True);
            Assert.That(GateBreakers.Wanted(Blue, false, 0), Is.False, "not at peace");
            Assert.That(GateBreakers.Wanted(Blue, true, (int)GateBreakers.PerNavy.Value), Is.False, "it has its share");
            var raiders = new Faction("Raiders", "RAI", new Rgb(1, 1, 0)) { IsMilitary = true, IsPirate = true };
            Assert.That(GateBreakers.Wanted(raiders, true, 0), Is.False, "raiders do not take gates");
        }
    }
}
