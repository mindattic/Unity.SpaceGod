using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// U.SPC-T92: interstellar war. Powers go to war over systems; war fleets plan campaigns over the gate graph, force
    /// and take the enemy's gates, break a settlement's defences and land marines to occupy it; and the war ends in a
    /// treaty that cedes the systems taken, through the one diplomacy table every system shares.
    /// </summary>
    [TestFixture]
    public class InterstellarWarTests
    {
        const double StartJd = 2461324.0;

        static SystemSpec Spec(string name) => NearbySystems.All().First(s => s.Name == name);

        /// <summary>
        /// Three systems on a line of gates: Alpha Centauri, the Federation's Barnard's Star, and Luhman 16, where the Twin
        /// Dark Authority governs; a campaign from Centauri to Luhman 16 has to pass through Barnard's Star.
        /// </summary>
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

        [Test]
        public void AWarFleetPlansItsWayThroughTheEnemysGatesWhereTrafficFindsThemShut()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            Assert.That(g.Network.Route(ac, luhman, accord, StartJd), Has.Count.EqualTo(2), "at peace the way is open");
            Assert.That(g.Wars.Declare(accord, authority, StartJd, luhman), Is.Not.Null);

            // At war, the Authority's gate at Luhman 16 is shut to the Accord's traffic, but a war fleet may force it.
            var farGate = luhman.GateTo(barnard);
            Assert.That(farGate.Admits(accord, StartJd), Is.False);
            Assert.That(farGate.HostileTo(accord), Is.True);
            Assert.That(g.Network.Route(ac, luhman, accord, StartJd), Is.Null);
            var route = g.Network.Route(ac, luhman, accord, StartJd, force: true);
            Assert.That(route.Select(x => x.Home), Is.EqualTo(new[] { ac, barnard }), "over the gate graph, by way of Barnard's Star");
            // The Federation's gates, neutral, stay open to both; a gate under a neutral's blockade is shut, not forcible.
            var federationGate = barnard.GateTo(luhman);
            Assert.That(federationGate.HostileTo(accord), Is.False);
            g.Traffic.Blockade(federationGate, StartJd, 30);
            Assert.That(g.Network.Route(ac, luhman, accord, StartJd + 1, force: true), Is.Null);
            // Nobody else is at war: pirates and the Federation are not the Authority's enemies by this declaration.
            Assert.That(farGate.HostileTo(barnard.World.Sol), Is.False);
            Assert.That(g.Wars.Declare(barnard.World.Sol, authority, StartJd), Is.Null, "the Authority is already fighting a war");
        }

        /// <summary>
        /// U.SPC-T295: a war fleet on its way home that came out beside its own home gate, now the enemy's and fortified,
        /// held there to take it; once released it is home, not "arrived" at its home system to stay there for good.
        /// </summary>
        [Test]
        public void AFleetHeldAtAGateInItsOwnHomeSystemGoesHomeWhenReleased()
        {
            var g = Line(out var ac, out var barnard, out _);
            var accord = ac.World.Sol;
            var leader = ac.World.Fleet.Group(accord, StartJd, ShipClass.Battleship, "Long Reach", new[] { "Rigil Gate" });
            var homeGate = ac.GateTo(barnard);
            leader.Voyage = new Voyage
            {
                Purpose = VoyagePurpose.Campaign, Home = ac, Destination = ac, Route = new List<JumpGate>(), Faction = accord,
                HomeRole = ShipRole.Patrol, HomeRoute = leader.Route, HomePorts = leader.Ports, HoldGate = homeGate, SetOutJd = StartJd,
            };
            Assert.That(leader.Voyage.Homeward, Is.True);
            g.Traffic.Release(ac, leader, StartJd + 1);
            Assert.That(leader.Voyage, Is.Null, "the fleet stayed on its voyage at home, for good");
            Assert.That(leader.Role, Is.EqualTo(ShipRole.Patrol));
        }

        /// <summary>
        /// U.SPC-T296: a war fleet whose way on is shut by a neutral's blockade, and stays shut, gives up and turns for home,
        /// instead of waiting where it gave up for the rest of the war.
        /// </summary>
        [Test]
        public void AWarFleetWhoseWayIsShutForGoodTurnsForHome()
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            ac.World.Fleet.Group(accord, StartJd, ShipClass.Battleship, "Long Reach", new[] { "Rigil Gate" },
                (ShipClass.Cruiser, "Vanguard"), (ShipClass.Destroyer, "Spur"));
            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            double jd = Run(g, StartJd, 30, () => war.Campaigns.Count > 0);
            var campaign = war.Campaigns.FirstOrDefault();
            Assert.That(campaign, Is.Not.Null, "no campaign set out");
            // The Federation shuts its gate on the way at Barnard's Star, for longer than any war fleet will wait.
            g.Traffic.Blockade(barnard.GateTo(luhman), jd, 400);
            Run(g, jd, 60, () => campaign.Stage != CampaignStage.Marching);
            Assert.That(campaign.Stage, Is.Not.EqualTo(CampaignStage.Marching), "the fleet waits where it gave up, for good");
            Assert.That(campaign.Lost, Is.False);
        }

        [TestCase(false, TestName = "AScriptedCampaignInvadesOccupiesAndEndsInATreaty(unwatched)")]
        [TestCase(true, TestName = "AScriptedCampaignInvadesOccupiesAndEndsInATreaty(watched)")]
        public void AScriptedCampaignInvadesOccupiesAndEndsInATreaty(bool watched)
        {
            var g = Line(out var ac, out var barnard, out var luhman);
            // Unwatched, the player is at home in Centauri and the invasion is fought at coarse detail; watched, the player
            // is at Luhman 16 and it is fought at full detail.
            g.Focus = watched ? luhman : ac;
            var accord = ac.World.Sol;
            var authority = luhman.World.Sol;
            Assert.That(accord.Name, Is.EqualTo("Centauri Accord"));
            Assert.That(authority.Name, Is.EqualTo("Twin Dark Authority"));
            Assert.That(luhman.Holder, Is.SameAs(authority));
            var cloudbreak = luhman.World.Economy.Settlements.Single(s => s.Owner == authority);
            var farGate = luhman.GateTo(barnard);
            Assert.That(farGate.Owner, Is.SameAs(authority));
            Assert.That(farGate.Forts, Has.Count.EqualTo(GateNetwork.GateForts), "the Authority's gate is fortified");

            // A strike group for the war, besides the Accord's home guard.
            ac.World.Fleet.Group(accord, StartJd, ShipClass.Battleship, "Long Reach", new[] { "Rigil Gate" },
                (ShipClass.Cruiser, "Vanguard"), (ShipClass.Destroyer, "Spur"), (ShipClass.Destroyer, "Rowel"));

            var war = g.Wars.Declare(accord, authority, StartJd, luhman);
            Assert.That(war, Is.Not.Null);
            Assert.That(war.Goals, Is.EqualTo(new[] { luhman }));
            foreach (var s in g.Systems) Assert.That(s.World.Diplomacy.Get(accord, authority), Is.EqualTo(Relation.War), s.Name);

            var log = new List<string>();
            foreach (var s in g.Systems) s.World.Logged += e => log.Add($"{e.Jd - StartJd,7:0.00} [{s.Name}] {e.Text}");
            bool gateBattle = false, occupied = false, landed = false;
            double occupiedUnrest = 0, occupiedGarrison = 0, garrisonBefore = double.NaN, garrisonMustered = 0;
            Faction occupiedFrom = null;
            luhman.World.War.BattleStarted += b => gateBattle |= b.Units.Any(u => u.Fort == farGate);
            luhman.World.Logged += e =>
            {
                if (e.Text.Contains("marines land on Cloudbreak"))
                {
                    landed = true;
                    garrisonBefore = cloudbreak.Garrison;
                    garrisonMustered = InterstellarWars.FullGarrison(cloudbreak);
                }
                if (e.Text.StartsWith("Cloudbreak") && e.Text.Contains("falls to the"))
                {
                    // The moment it is occupied: in the Accord's hands, held by its marines, its people restive.
                    occupied = true;
                    occupiedUnrest = cloudbreak.Unrest;
                    occupiedGarrison = cloudbreak.Garrison;
                    occupiedFrom = cloudbreak.OccupiedFrom;
                    Assert.That(cloudbreak.Owner, Is.SameAs(accord));
                }
            };
            double jd = Run(g, StartJd, 200, () => war.Over);
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Accord") || l.Contains("Authority") || l.Contains("Gate") || l.Contains("Treaty"))));
            var campaign = war.Campaigns.FirstOrDefault();
            TestContext.Out.WriteLine(campaign?.ToString());

            Assert.That(campaign, Is.Not.Null, "no campaign set out");
            Assert.That(campaign.Home, Is.SameAs(ac));
            Assert.That(gateBattle, Is.True, "no battle at the Authority's gate");
            Assert.That(campaign.GatesTaken, Is.GreaterThanOrEqualTo(1));
            Assert.That(landed, Is.True, "no marines went down");
            Assert.That(garrisonBefore, Is.EqualTo(garrisonMustered).Within(1e-6), "the garrison mustered from its people");
            Assert.That(occupied, Is.True, "Cloudbreak never fell");
            Assert.That(occupiedFrom, Is.SameAs(authority));
            Assert.That(occupiedUnrest, Is.EqualTo(InterstellarWars.OccupationUnrest));
            Assert.That(occupiedGarrison, Is.GreaterThan(0));
            Assert.That(war.Over, Is.True, "no peace");

            // The treaty cedes Luhman 16: its settlement and gates are the Accord's, and the two are at peace everywhere.
            var t = war.Treaty;
            Assert.That(ac.World.Diplomacy.Treaties, Does.Contain(t));
            Assert.That(t.Winner, Is.SameAs(accord));
            Assert.That(t.Ceded, Is.EqualTo(new[] { "Luhman 16" }));
            Assert.That(t.Terms, Does.StartWith("The Treaty of Cloudbreak"));
            Assert.That(ac.World.Diplomacy.InTruce(accord, authority, t.Jd + 100), Is.True);
            Assert.That(g.Wars.Declare(accord, authority, t.Jd + 100), Is.Null, "the truce holds");
            foreach (var s in g.Systems) Assert.That(s.World.Diplomacy.Get(accord, authority), Is.EqualTo(Relation.Peace), s.Name);
            Assert.That(cloudbreak.Owner, Is.SameAs(accord));
            Assert.That(cloudbreak.OccupiedFrom, Is.Null);
            Assert.That(luhman.Holder, Is.SameAs(accord));
            foreach (var gate in luhman.Gates) Assert.That(gate.Owner, Is.SameAs(accord), gate.Name);
            Assert.That(g.Polities.CapitalOf(authority), Is.Null.Or.Not.SameAs(cloudbreak));

            // And the fleet goes home.
            Run(g, jd, 120, () => !campaign.Active);
            Assert.That(campaign.Stage, Is.EqualTo(CampaignStage.Over));
            Assert.That(campaign.Lost, Is.False);
        }
    }
}
