using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The strategy layer (U.SPC-T423, <see cref="StrategicCommand"/>): a polity's production focus steers which of its
    /// industries grow, its build plan what its yards lay down and how big a navy they keep, its research accrues from its
    /// people and pays off, and its fleet groups carry out the objectives command gives them (take, hold, blockade, escort,
    /// explore) through the faction command layer, one squadron, a system's groups or the whole navy at once, and across the
    /// gates to another system.
    /// </summary>
    [TestFixture]
    public class StrategicCommandTests
    {
        const double StartJd = 2461324.0;

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static Faction MakeFaction(World w, string name)
        {
            var f = w.Fleet.AddFaction(name, name.Substring(0, System.Math.Min(3, name.Length)).ToUpperInvariant(), default);
            f.IsMilitary = true;
            return f;
        }

        static Ship ParkWarship(World w, Faction faction, ShipClass cls, int body)
        {
            var ship = w.Fleet.AddShip(cls + " " + faction.Name, cls, faction, ShipRole.Patrol);
            ship.Orbit = new ParkingOrbit { Body = body };
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = StartJd;
            return ship;
        }

        // ---------------------------------------------------------------- priorities

        [Test]
        public void TheBuildPlanSetsWhatTheYardsLayDownAndHowBigANavyTheyKeep()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var yard = w.Economy.Settlements.First(s => s.HasShipyard && s.Owner == w.Sol);
            Assert.That(BuildPlan.Balanced.Cycle, Does.Contain(w.War.ChooseWarship(yard)), "untold, a navy builds as it always has");

            command.SetBuild(w.Sol, BuildPlan.BattleLine);
            for (int i = 0; i < 6; i++)
            {
                var cls = w.War.ChooseWarship(yard);
                Assert.That(BuildPlan.BattleLine.Cycle, Does.Contain(cls));
                w.War.LaunchWarship(yard, cls.Value, StartJd);
            }

            command.SetBuild(w.Sol, BuildPlan.Merchant);
            Assert.That(w.War.ChooseWarship(yard), Is.Null, "a merchant-marine plan leaves the slips to the freighters");

            // At its cap a navy lays down nothing more, unless it mobilises.
            command.SetBuild(w.Sol, BuildPlan.Balanced);
            int navy = w.Fleet.Ships.Count(s => !s.Destroyed && s.Faction == w.Sol && s.Spec.IsMilitary && !s.PatrolWing);
            int cap = w.War.AtWar(w.Sol) ? w.NavyCapAtWar : w.NavyCap;
            for (int i = navy; i < cap; i++) w.Fleet.AddShip("Filler " + i, ShipClass.Destroyer, w.Sol, ShipRole.Patrol);
            Assert.That(w.War.ChooseWarship(yard), Is.Null);
            command.SetBuild(w.Sol, BuildPlan.Mobilise);
            Assert.That(w.War.ChooseWarship(yard), Is.Not.Null, "mobilised, it keeps half again the navy");
        }

        [Test]
        public void AProductionFocusFavoursItsIndustriesInTheEconomy()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var port = w.Economy.Settlements.First(s => s.Owner == w.Sol && s.Kind != SettlementKind.Haven);
            Assert.That(w.Economy.Priority(port, IndustryKind.MunitionsPlant).Favoured, Is.False, "untold, every industry follows its market");

            command.SetProduction(w.Sol, ProductionFocus.War);
            Assert.That(w.Economy.Priority(port, IndustryKind.MunitionsPlant).Favoured, Is.True);
            Assert.That(w.Economy.Priority(port, IndustryKind.Farm).Favoured, Is.False);
            var other = w.Economy.Settlements.First(s => s.Owner != w.Sol && s.Kind != SettlementKind.Haven);
            Assert.That(w.Economy.Priority(other, IndustryKind.MunitionsPlant).Favoured, Is.False, "one polity's focus is its own");

            // Industry research makes a favoured industry grow faster.
            command.Of(w.Sol).Levels[ResearchTrack.Industry] = 2;
            Assert.That(w.Economy.Priority(port, IndustryKind.Smelter).Bonus, Is.EqualTo(2 * ResearchTrack.IndustryPerLevel).Within(1e-12));
        }

        [Test]
        public void ResearchAccruesFromAPolitysPeopleAndPaysOff()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            double before = w.Sol.MarineQuality;
            command.SetResearch(w.Sol, ResearchTrack.Marines);
            double jd = StartJd;
            for (int day = 0; day < 400 && command.Level(w.Sol, ResearchTrack.Marines) == 0; day++)
            {
                jd += 1;
                command.Update(jd);
            }
            Assert.That(command.Level(w.Sol, ResearchTrack.Marines), Is.EqualTo(1), "a populous polity reaches the first level within the year");
            Assert.That(w.Sol.MarineQuality, Is.EqualTo(before + ResearchTrack.MarinesPerLevel).Within(1e-9));
            Assert.That(command.Level(w.Reach, ResearchTrack.Marines), Is.Zero, "a polity told nothing researches nothing");

            // Shipyards research raises the navy a polity keeps.
            Assert.That(command.NavyShare(w.Sol), Is.EqualTo(1).Within(1e-12));
            command.Of(w.Sol).Levels[ResearchTrack.Shipyards] = 3;
            Assert.That(command.NavyShare(w.Sol), Is.EqualTo(1 + 3 * ResearchTrack.YardsPerLevel).Within(1e-12));
        }

        // ---------------------------------------------------------------- objectives

        [Test]
        public void AGroupHoldsWhereItIsToldAheadOfItsOwnPlans()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            int pluto = w.Catalog["Pluto"].Index, mars = w.Catalog["Mars"].Index;
            w.Economy.Add("Pluto Station", pluto, SettlementKind.Colony, home, population: 1000);
            var leader = ParkWarship(w, home, ShipClass.Cruiser, mars);

            Assert.That(command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Hold, w, StartJd, out int given, pluto), Is.Null);
            Assert.That(given, Is.EqualTo(1));
            Assert.That(w.War.ChooseNext(leader, mars, StartJd + 0.1), Is.EqualTo(pluto));
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Defend));
            Assert.That(w.War.Commanded(leader, pluto), Is.True);

            // Cleared, it goes back to its own command's plans.
            Assert.That(command.Clear(home, CommandScope.Group, leader), Is.EqualTo(1));
            Assert.That(command.ObjectiveOf(leader), Is.Null);
        }

        [Test]
        public void AGroupTakesAnEnemyPortAndThenHoldsIt()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            var enemy = MakeFaction(w, "Foe");
            int pluto = w.Catalog["Pluto"].Index, mars = w.Catalog["Mars"].Index;
            var port = w.Economy.Add("Foe Port", pluto, SettlementKind.Colony, enemy, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, home, ShipClass.Battleship, mars);

            Assert.That(command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Take, w, StartJd, out _, pluto), Does.Contain("enemy"),
                "a port of a polity it is at peace with is no one's to take");
            w.Diplomacy.Set(home, enemy, Relation.War);
            Assert.That(command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Take, w, StartJd, out _, pluto), Is.Null);

            Assert.That(w.War.ChooseNext(leader, mars, StartJd + 0.1), Is.EqualTo(pluto));
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Attack));
            // There, with the port's defences down: it lays siege.
            leader.Orbit = new ParkingOrbit { Body = pluto };
            w.War.ChooseNext(leader, pluto, StartJd + 0.2);
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Siege));

            // The port falls: the group holds it.
            w.Economy.Capture(port, home);
            w.War.ChooseNext(leader, pluto, StartJd + 0.3);
            Assert.That(command.ObjectiveOf(leader).Kind, Is.EqualTo(ObjectiveKind.Hold));
            Assert.That(command.ObjectiveOf(leader).Body, Is.EqualTo(pluto));
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Defend));
        }

        [Test]
        public void ABlockadingGroupClosesAnEnemyPortWithoutTakingIt()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            var enemy = MakeFaction(w, "Foe");
            w.Diplomacy.Set(home, enemy, Relation.War);
            int pluto = w.Catalog["Pluto"].Index;
            var port = w.Economy.Add("Foe Port", pluto, SettlementKind.Colony, enemy, population: 0);
            port.MaxPlatforms = 0;
            var leader = ParkWarship(w, home, ShipClass.Destroyer, pluto);
            foreach (var sub in leader.Combat.Subsystems)
                if (sub.Def.Weapon != null) sub.Hp = 0;

            Assert.That(command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Blockade, w, StartJd, out _), Is.Null);
            Assert.That(command.ObjectiveOf(leader).Body, Is.EqualTo(pluto), "command picks the enemy's port to blockade");
            Assert.That(w.War.ChooseNext(leader, pluto, StartJd + 0.1), Is.EqualTo(pluto));
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Blockade));

            double jd = StartJd;
            for (int day = 0; day < 10; day++)
            {
                jd += 1;
                w.War.Update(jd);
            }
            Assert.That(w.War.Besieger(port), Is.EqualTo(home), "the blockaders hold the space over the port");
            Assert.That(port.Owner, Is.EqualTo(enemy), "a blockade lands nobody: the port is not taken");
            Assert.That(w.War.SiegeDays(port), Is.Zero);
        }

        [Test]
        public void AnEscortGoesWhereItsChargeIsBound()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var trader = w.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && w.IsTrader(s) && !w.Diplomacy.Hostile(w.Sol, s.Faction));
            int here = w.Catalog["Mars"].Index;
            var leader = ParkWarship(w, w.Sol, ShipClass.Cruiser, here);

            Assert.That(command.Order(w.Sol, CommandScope.Group, leader, null, ObjectiveKind.Escort, w, StartJd, out _, -1, trader), Is.Null);
            int next = w.War.ChooseNext(leader, here, StartJd + 0.01);
            var c = trader.Controller;
            bool moving = c.Phase == ShipPhase.Transit || c.Phase == ShipPhase.Lane || c.Phase == ShipPhase.Lurk || c.Phase == ShipPhase.Adrift;
            int bound = c.Battle != null ? c.Battle.Body
                : moving ? (c.FinalBody >= 0 && c.Journey != null ? c.FinalBody : c.DestinationBody)
                : (c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null ? c.Ring.Body : c.Orbit?.Body ?? here;
            Assert.That(next, Is.EqualTo(bound >= 0 ? bound : here), "the escort went somewhere its charge is not");
            Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Escort));

            // Its charge lost, it stands down.
            trader.Destroyed = true;
            command.Update(StartJd + 1);
            command.Update(StartJd + 2);
            Assert.That(command.ObjectiveOf(leader), Is.Null);
        }

        [Test]
        public void AnExploringGroupVisitsEveryWorldAndStandsDown()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            int here = w.Catalog["Mars"].Index;
            var leader = ParkWarship(w, home, ShipClass.Destroyer, here);
            Assert.That(command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Explore, w, StartJd, out _), Is.Null);

            var visited = new System.Collections.Generic.HashSet<int>();
            double jd = StartJd;
            for (int i = 0; i < 500 && command.ObjectiveOf(leader) != null; i++)
            {
                jd += 0.5;
                int next = w.War.ChooseNext(leader, here, jd);
                if (command.ObjectiveOf(leader) == null) break;
                Assert.That(w.War.OrderOf(leader).Kind, Is.EqualTo(OrderKind.Explore));
                Assert.That(visited.Add(next), Is.True, "it went back to a world it had already explored");
                here = next;
            }
            Assert.That(command.ObjectiveOf(leader), Is.Null, "it never finished exploring");
            Assert.That(visited.Count, Is.GreaterThan(5));
            Assert.That(visited, Is.SupersetOf(w.Economy.Settlements.Select(s => s.Body).Distinct().Where(b => b != w.Catalog["Mars"].Index)));
        }

        [Test]
        public void OneOrderReachesASquadronASystemsGroupsOrTheWholeNavy()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            int pluto = w.Catalog["Pluto"].Index, mars = w.Catalog["Mars"].Index;
            var a = ParkWarship(w, home, ShipClass.Cruiser, mars);
            var b = ParkWarship(w, home, ShipClass.Destroyer, mars);
            var escort = w.Fleet.AddShip("Wingman", ShipClass.Destroyer, home, ShipRole.Escort);
            w.Fleet.AddEscort(a, escort, Fleet.SlotFor(0));

            command.Order(home, CommandScope.Group, escort, null, ObjectiveKind.Hold, w, StartJd, out int one, pluto);
            Assert.That(one, Is.EqualTo(1));
            Assert.That(command.ObjectiveOf(a), Is.Not.Null, "an order to one of a group's hulls goes to its flagship");
            Assert.That(command.ObjectiveOf(b), Is.Null);

            command.Order(home, CommandScope.System, null, w, ObjectiveKind.Hold, w, StartJd, out int all, pluto);
            Assert.That(all, Is.EqualTo(2), "every group of it in the system, not the escorts");
            Assert.That(command.ObjectiveOf(escort), Is.Null);
            Assert.That(command.Clear(home, CommandScope.Polity), Is.EqualTo(2));

            var report = command.Report(home, StartJd);
            Assert.That(report.Groups, Is.EqualTo(2));
            Assert.That(report.Warships, Is.EqualTo(3));
            Assert.That(report.Fleet.Select(g => g.Leader), Is.EquivalentTo(new[] { a, b }));
            Assert.That(report.Fleet.First(g => g.Leader == a).Hulls, Is.EqualTo(2));
        }

        [Test]
        public void AnObjectiveGoesOnUnderTheHeirWhenTheFlagshipIsLost()
        {
            var w = CreateWorld();
            var command = w.Polities.Command;
            var home = MakeFaction(w, "Homeland");
            int pluto = w.Catalog["Pluto"].Index, mars = w.Catalog["Mars"].Index;
            var leader = ParkWarship(w, home, ShipClass.Cruiser, mars);
            var escort = w.Fleet.AddShip("Wingman", ShipClass.Destroyer, home, ShipRole.Escort);
            w.Fleet.AddEscort(leader, escort, Fleet.SlotFor(0));
            command.Order(home, CommandScope.Group, leader, null, ObjectiveKind.Hold, w, StartJd, out _, pluto);

            w.War.HandOverCommand(leader);
            Assert.That(command.ObjectiveOf(leader), Is.Null);
            Assert.That(command.ObjectiveOf(escort)?.Body, Is.EqualTo(pluto));
        }

        [Test]
        public void AGroupWithAnObjectiveInAnotherSystemIsSentThere()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var command = g.Polities.Command;
            var sol = g.Find("Sol System");
            var f = sol.World.Sol;
            // A system the navy can reach through open gates.
            var target = g.Systems.First(s => s != sol && g.Network.Route(sol, s, f, StartJd) is var r && r != null && r.Count > 0);
            var leader = sol.World.Fleet.AddShip("Envoy", ShipClass.Cruiser, f, ShipRole.Patrol);
            int port = sol.World.Economy.Settlements.First(s => s.Owner == f && s.Kind != SettlementKind.Haven).Body;
            sol.World.Fleet.Station(leader, port, StartJd);

            Assert.That(command.Order(f, CommandScope.Group, leader, null, ObjectiveKind.Explore, target.World, StartJd, out int given), Is.Null);
            Assert.That(given, Is.EqualTo(1));
            Assert.That(leader.Voyage, Is.Not.Null, "command did not send the group through the gates");
            Assert.That(leader.Voyage.Purpose, Is.EqualTo(VoyagePurpose.Directed));
            Assert.That(leader.Voyage.Destination, Is.EqualTo(target));
            Assert.That(command.WorldOf(leader), Is.EqualTo(sol.World));
            Assert.That(command.Report(f, StartJd).Fleet.First(x => x.Leader == leader).Order, Does.Contain(target.Name));
        }
    }
}
