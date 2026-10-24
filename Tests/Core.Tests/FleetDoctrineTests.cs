using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// U.SPC-T474: AI fleet doctrine. Navies raise two kinds of force: a gate fleet, large but bound to come through the gates
    /// (predictable, met at the gate exits), and a jump strike group (a carrier, the FTL capitals and the escorts clamped to the
    /// carrier) that jumps in anywhere under its own drives to relieve, raid or open a second front. Strategic command picks
    /// between them by objective, and defenders keep pickets at their gates and a reserve at home against strike groups.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class FleetDoctrineTests
    {
        const double StartJd = 2461324.0;

        static SystemSpec Spec(string name) => NearbySystems.All().First(s => s.Name == name);

        /// <summary>Alpha Centauri (the Accord's), Barnard's Star (the Federation's) and Luhman 16 (the Authority's), on a line of gates.</summary>
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

        /// <summary>A jump strike group of <paramref name="f"/> in <paramref name="w"/>: a carrier with a battleship (its own drive)
        /// and a destroyer to clamp to the carrier's hull.</summary>
        static Ship StrikeGroup(World w, Faction f, string name, string port) =>
            w.Fleet.Group(f, StartJd, ShipClass.Carrier, name, new[] { port }, (ShipClass.Battleship, name + " Sabre"), (ShipClass.Destroyer, name + " Swift"));

        /// <summary>A gate fleet of <paramref name="f"/> in <paramref name="w"/>: a battleship leading cruisers and destroyers.</summary>
        static Ship GateFleet(World w, Faction f, string name, string port) =>
            w.Fleet.Group(f, StartJd, ShipClass.Battleship, name, new[] { port },
                (ShipClass.Cruiser, name + " Vanguard"), (ShipClass.Cruiser, name + " Gaff"), (ShipClass.Destroyer, name + " Spur"), (ShipClass.Destroyer, name + " Rowel"));

        // ---------------------------------------------------------------- the two kinds of force

        [Test]
        public void AJumpStrikeGroupIsACarrierItsFtlCapitalsAndTheEscortsItCanTakeThroughItsJump()
        {
            var g = Line(out var ac, out _, out _);
            var w = ac.World;
            var accord = w.Sol;
            var strike = StrikeGroup(w, accord, "Long Reach", "Rigil Gate");
            Assert.That(FleetDoctrine.KindOf(w, strike), Is.EqualTo(ForceKind.JumpStrikeGroup));

            // A cruiser cannot ride the clamps: the group would leave it behind, so it is a gate fleet.
            var withCruiser = w.Fleet.Group(accord, StartJd, ShipClass.Carrier, "Far Reach", new[] { "Rigil Gate" }, (ShipClass.Cruiser, "Lance"));
            Assert.That(FleetDoctrine.KindOf(w, withCruiser), Is.EqualTo(ForceKind.GateFleet));

            // More destroyers than the clamps take: a gate fleet too.
            var crowded = w.Fleet.Group(accord, StartJd, ShipClass.Carrier, "Wide Reach", new[] { "Rigil Gate" },
                (ShipClass.Destroyer, "Kite"), (ShipClass.Destroyer, "Merlin"), (ShipClass.Destroyer, "Hobby"));
            Assert.That(FleetDoctrine.KindOf(w, crowded), Is.EqualTo(ForceKind.GateFleet));

            // FTL capitals with no carrier among them: no strike group, however far they could jump.
            var battleships = w.Fleet.Group(accord, StartJd, ShipClass.Battleship, "Bulwark", new[] { "Rigil Gate" }, (ShipClass.Battleship, "Rampart"));
            Assert.That(InterstellarTraffic.CanCruise(battleships), Is.True);
            Assert.That(FleetDoctrine.KindOf(w, battleships), Is.EqualTo(ForceKind.GateFleet));

            Assert.That(FleetDoctrine.KindOf(w, GateFleet(w, accord, "Hammer", "Rigil Gate")), Is.EqualTo(ForceKind.GateFleet));

            var report = w.Polities.Command.Report(accord, StartJd);
            Assert.That(report.StrikeGroups, Is.EqualTo(1));
            Assert.That(report.GateFleets, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void AStrikeGroupDropsOutOnTheFlankAwayFromTheGatesAndPicketsHeldAgainstIt()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            Assert.That(g.Wars.Declare(accord, authority, StartJd, luhman), Is.Not.Null);
            var gate = luhman.GateTo(barnard);
            Assert.That(gate.Owner, Is.SameAs(authority));

            var straight = barnard.Origin.KmFrom(luhman.Origin).Normalized;
            var dir = FleetDoctrine.DropDirection(luhman, barnard.Origin, accord);
            double wellKm = CruiseDrives.WellLimitKm(luhman);
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(dir, straight)))) * 180 / Math.PI;
            Assert.That(angle, Is.GreaterThan(1).And.LessThanOrEqualTo(FleetDoctrine.MaxFlankDegrees + 1e-6), "it came straight in at the gate");
            double Clear(DVec3 d) => GatePickets.Of(gate).Select(p => (p.PicketPostKm - d * wellKm).Length).DefaultIfEmpty((gate.PositionKm - d * wellKm).Length).Min();
            Assert.That(Clear(dir), Is.GreaterThan(Clear(straight)), "the flank is no farther from the pickets than the straight way in");

            // Its own side's gate is nothing to slip past: straight in.
            var home = FleetDoctrine.DropDirection(ac, barnard.Origin, accord);
            Assert.That(DVec3.Dot(home, barnard.Origin.KmFrom(ac.Origin).Normalized), Is.GreaterThan(0.999));
        }

        // ---------------------------------------------------------------- strategic command picks by objective

        [Test]
        public void CommandMarchesTheGateFleetThroughTheGatesAndJumpsTheStrikeGroupInOnASecondFront()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            var fleet = GateFleet(ac.World, accord, "Long Reach", "Rigil Gate");
            var strike = StrikeGroup(ac.World, accord, "Bright Lance", "Rigil Gate");
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add($"{e.Jd - StartJd,7:0.00} [{s.Name}] {e.Text}");

            double jd = Run(g, StartJd, 10, () => war.Campaigns.Count >= 2);
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Accord") || l.Contains("Authority"))));
            var main = war.Campaigns.Single(c => c.Force == ForceKind.GateFleet);
            var second = war.Campaigns.Single(c => c.Force == ForceKind.JumpStrikeGroup);
            Assert.That(main.Mission, Is.EqualTo(CampaignMission.Invade));
            Assert.That(main.Flagship, Is.Not.EqualTo(strike.Name), "the strike group was marched through the gates");
            Assert.That(second.Mission, Is.EqualTo(CampaignMission.SecondFront));
            Assert.That(second.Goal, Is.SameAs(luhman));
            Assert.That(second.Flagship, Is.EqualTo(strike.Name));
            Assert.That(strike.Voyage, Is.Not.Null);
            Assert.That(strike.Voyage.ByCruise, Is.True, "the strike group went by the gates");
            Assert.That(strike.Voyage.Route, Is.Empty);
            Assert.That(strike.Voyage.DropDir, Is.Not.Null);
            Assert.That(fleet.Voyage?.Route, Is.Not.Empty, "the gate fleet is not on its way through the gates");

            // It jumps, its destroyer clamped to the carrier, and comes in under its own drive, never through a gate.
            Run(g, jd, 40, () => luhman.World.Fleet.IsPresent(strike));
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Bright Lance"))));
            Assert.That(luhman.World.Fleet.IsPresent(strike), Is.True, "the strike group never reached Luhman 16");
            Assert.That(log.Any(l => l.Contains("Bright Lance") && l.Contains("clamped to the hull")), Is.True, "its destroyer did not ride the clamps");
            Assert.That(log.Any(l => l.Contains("[Luhman 16]") && l.Contains("Bright Lance") && l.Contains("comes through the")), Is.False, "it came through a gate");
            Assert.That(second.Active, Is.True);
            Assert.That(second.Lost, Is.False);
        }

        [Test]
        public void AStrikeGroupRelievesASystemOfItsOwnTheEnemyIsAttacking()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            var strike = StrikeGroup(ac.World, accord, "Bright Lance", "Rigil Gate");
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);

            // An Authority warship is loose at Barnard's Star, over a station the Accord keeps there with no navy of its own.
            var bw = barnard.World;
            int body = bw.Economy.Settlements.First(s => s.Kind != SettlementKind.Haven).Body;
            var station = bw.Economy.Add("Accord Outpost", body, SettlementKind.Station, accord, population: 0);
            bw.Fleet.SpawnAt("TDA Grim", ShipClass.Destroyer, authority, body, new DVec3(2e4, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(bw.War.EnemiesAt(body, accord), Is.True);
            double garrison = InterstellarWars.GarrisonOf(station);
            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add($"{e.Jd - StartJd,7:0.00} [{s.Name}] {e.Text}");

            double jd = Run(g, StartJd, 3, () => war.Campaigns.Any(c => c.Force == ForceKind.JumpStrikeGroup));
            var relief = war.Campaigns.SingleOrDefault(c => c.Force == ForceKind.JumpStrikeGroup);
            Assert.That(relief, Is.Not.Null, "no strike group went");
            Assert.That(relief.Mission, Is.EqualTo(CampaignMission.Relieve));
            Assert.That(relief.Goal, Is.SameAs(barnard));
            Assert.That(strike.Voyage?.ByCruise, Is.True);

            // It jumps in under its own drives, puts its marines down to stiffen the station's garrison and goes home.
            Run(g, jd, 90, () => !relief.Active);
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Bright Lance") || l.Contains("Grim") || l.Contains("Outpost"))));
            Assert.That(log.Any(l => l.Contains("[Barnard's Star]") && l.Contains("Bright Lance") && l.Contains("under its own drive")), Is.True);
            Assert.That(relief.Relieved, Is.GreaterThanOrEqualTo(1), "no marines went down to the garrison");
            Assert.That(station.Garrison, Is.GreaterThan(garrison));
            Assert.That(station.Owner, Is.SameAs(accord));
            Assert.That(relief.Active, Is.False, "it never went home");
            Assert.That(relief.Lost, Is.False);
        }

        [Test]
        public void AStrikeGroupWithNoMarinesRaidsAnEnemySystemAndJumpsHome()
        {
            var g = Line(out var ac, out _, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            var strike = StrikeGroup(ac.World, accord, "Bright Lance", "Rigil Gate");
            foreach (var s in ac.World.Fleet.Ships.Where(s => s == strike || s.Leader == strike))
                s.Combat.Interior.TakeMarines(s.Combat.MarinesAvailable);
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add($"{e.Jd - StartJd,7:0.00} [{s.Name}] {e.Text}");

            double jd = Run(g, StartJd, 3, () => war.Campaigns.Any(c => c.Force == ForceKind.JumpStrikeGroup));
            var raid = war.Campaigns.SingleOrDefault(c => c.Force == ForceKind.JumpStrikeGroup);
            Assert.That(raid, Is.Not.Null, "no strike group went");
            Assert.That(raid.Mission, Is.EqualTo(CampaignMission.Raid));
            Assert.That(raid.Goal, Is.SameAs(luhman));

            Run(g, jd, 90, () => !raid.Active);
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Bright Lance") || l.Contains("strike group"))));
            Assert.That(log.Any(l => l.Contains("[Luhman 16]") && l.Contains("falls on")), Is.True, "it never fell on a port");
            Assert.That(log.Any(l => l.Contains("[Luhman 16]") && l.Contains("Battle of") && l.Contains("Centauri Accord (CAS Bright Lance")), Is.True,
                "it never fought the port's defences");
            Assert.That(raid.Active, Is.False, "it never came home");
            Assert.That(raid.Lost, Is.False);
            Assert.That(ac.World.Fleet.IsPresent(strike), Is.True, "the strike group is not home");
            Assert.That(strike.Voyage, Is.Null);
        }

        // ---------------------------------------------------------------- the defender's reserve

        [Test]
        public void ADefenderKeepsAReserveAtHomeAgainstTheEnemysStrikeGroups()
        {
            var g = Line(out var ac, out _, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            Assert.That(FleetDoctrine.StrikeThreat(g, authority), Is.EqualTo(0));
            var strike = StrikeGroup(ac.World, accord, "Bright Lance", "Rigil Gate");
            Assert.That(FleetDoctrine.StrikeThreat(g, authority), Is.EqualTo(0), "no war, no threat");
            g.Wars.Declare(accord, authority, StartJd, luhman);
            double threat = FleetDoctrine.StrikeThreat(g, authority);
            Assert.That(threat, Is.EqualTo(ac.World.War.GroupStrength(strike)).Within(1e-6));

            // The Authority's groups at home: whatever it has, plus a strong one it might send out.
            var lw = luhman.World;
            var sortie = GateFleet(lw, authority, "Sortie", lw.Catalog[lw.Economy.Settlements.First(s => s.Owner == authority).Body].Name);
            double left = lw.Fleet.Ships.Where(s => s != sortie && s.Faction == authority && !s.IsEscort && !s.Destroyed && s.Spec.IsMilitary && !s.PatrolWing && s.PicketGate == null)
                .Sum(s => lw.War.GroupStrength(s));
            Assert.That(FleetDoctrine.ReserveWanted(lw, authority, threat), Is.EqualTo(threat * FleetDoctrine.ReserveShare.Value).Within(1e-6));
            Assert.That(FleetDoctrine.ReserveWanted(ac.World, authority, threat), Is.EqualTo(0), "it holds nothing in Centauri to keep a reserve for");
            Assert.That(FleetDoctrine.LeavesReserve(lw, sortie, authority, threat), Is.EqualTo(left >= threat * FleetDoctrine.ReserveShare.Value));
            Assert.That(FleetDoctrine.LeavesReserve(lw, sortie, authority, left * 10), Is.False, "it would leave home bare against a strike group");
            Assert.That(FleetDoctrine.LeavesReserve(lw, sortie, authority, 0), Is.True);
            try
            {
                FleetDoctrine.ReserveShare.Set(0);
                Assert.That(FleetDoctrine.LeavesReserve(lw, sortie, authority, left * 10), Is.True, "a reserve of none still held it back");
            }
            finally
            {
                FleetDoctrine.ReserveShare.Set(1);
            }
        }
    }
}
