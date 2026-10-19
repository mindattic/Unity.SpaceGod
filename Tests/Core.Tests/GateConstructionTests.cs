using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>Gate construction and expansion (U.SPC-T97): what a gate costs and takes, and a network that grows.</summary>
    [TestFixture]
    public class GateConstructionTests
    {
        const double StartJd = 2461324.0;
        const double Year = 365.25;

        static double Stock(IEnumerable<Settlement> where, Good g) => where.Sum(s => s.Stock[(int)g]);

        [Test]
        public void AGateCostsYearsOfWorkAndALargeBillThatGrowWithItsReach()
        {
            for (double ly = 1; ly < GateConstruction.MaxLinkLy; ly += 1)
            {
                var near = GateConstruction.Cost(ly);
                var far = GateConstruction.Cost(ly + 1);
                for (int i = 0; i < near.Length; i++) Assert.That(far[i], Is.GreaterThan(near[i]));
                Assert.That(GateConstruction.BuildDays(ly + 1), Is.GreaterThan(GateConstruction.BuildDays(ly)));
                Assert.That(GateConstruction.SurveyDays(ly + 1), Is.GreaterThan(GateConstruction.SurveyDays(ly)));
            }
            // Years of work, never less than two; a five light-year gate takes three.
            Assert.That(GateConstruction.BuildDays(0.1), Is.GreaterThan(2 * Year));
            Assert.That(GateConstruction.BuildDays(5), Is.EqualTo(3 * Year).Within(1e-9));
            // A large bill: tens of thousands of tonnes of alloys and the best part of two hundred million credits for five light-years.
            var c = GateConstruction.Cost(5);
            Assert.That(c[0], Is.EqualTo(70000).Within(1e-9));
            Assert.That(c[1], Is.EqualTo(9000).Within(1e-9));
            Assert.That(c[2], Is.EqualTo(14000).Within(1e-9));
            Assert.That(GateConstruction.CostCredits(5), Is.EqualTo(70000 * 900 + 9000 * 6000 + 14000 * 3500).Within(1e-6));
            Assert.That(GateConstruction.CostCredits(5), Is.GreaterThan(1.5e8));
            Assert.That(GateConstruction.Materials, Is.EqualTo(new[] { Good.Alloys, Good.Electronics, Good.Machinery }));
        }

        [Test]
        public void AFullySuppliedGateTakesItsSurveyAndBuildTimeAndExactlyItsBillFromRealStock()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var wolf = g.Find("Wolf 359");
            var fed = sol.World.Sol;
            Assert.That(g.Network.Between(sol, wolf), Is.Null, "no direct gate yet");
            int linksBefore = g.Network.Links.Count;
            var works = new GateConstruction(g, StartJd);
            var p = works.Start(fed, sol, wolf, null, StartJd);
            double ly = Galaxy.DistanceLy(sol, wolf);
            Assert.That(p.Ly, Is.EqualTo(ly).Within(1e-9));
            Assert.That(p.SurveyDays, Is.EqualTo(GateConstruction.SurveyDays(ly)));
            Assert.That(p.WorkDays, Is.EqualTo(GateConstruction.BuildDays(ly)));
            Assert.That(p.Need, Is.EqualTo(GateConstruction.Cost(ly)));

            var markets = sol.World.Economy.Settlements;
            var before = GateConstruction.Materials.Select(m => Stock(markets, m)).ToArray();
            double treasury = fed.Treasury;
            int shipsBefore = sol.World.Fleet.Ships.Count;

            double jd = StartJd;
            while (p.Active && jd < StartJd + 10 * Year)
            {
                jd += 1;
                works.Step(p, jd, 1);
                if (p.Stage == GateStage.Building && p.Constructors.Count > 0 && jd < p.BuildStartJd + 2)
                {
                    // Constructor ships out at the site, the builder's own, holding there.
                    Assert.That(p.Constructors.Count, Is.EqualTo(GateConstruction.ConstructorsPerProject));
                    Assert.That(sol.World.Fleet.Ships.Count, Is.EqualTo(shipsBefore + GateConstruction.ConstructorsPerProject));
                    foreach (var c in p.Constructors)
                    {
                        Assert.That(c.Faction, Is.SameAs(fed));
                        Assert.That((c.Orbit.Center - p.SiteKm).Length, Is.LessThan(100));
                        Assert.That(c.PhaseEndJd, Is.GreaterThan(StartJd + 100 * Year));
                    }
                }
            }
            Assert.That(p.Stage, Is.EqualTo(GateStage.Done));
            // The survey, then the years of work, to the day.
            Assert.That(p.BuildStartJd - StartJd, Is.EqualTo(p.SurveyDays).Within(1));
            Assert.That(p.DoneJd - p.BuildStartJd, Is.EqualTo(p.WorkDays).Within(1));
            Assert.That(p.DoneJd - StartJd, Is.GreaterThan(3 * Year));
            // Every tonne of the bill came out of Sol's markets, and the Federation paid for it.
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(p.Used[i], Is.EqualTo(p.Need[i]).Within(p.Need[i] * 1e-9));
                Assert.That(before[i] - Stock(markets, GateConstruction.Materials[i]), Is.EqualTo(p.Need[i]).Within(p.Need[i] * 1e-9));
            }
            Assert.That(p.Spent, Is.GreaterThan(GateConstruction.CostCredits(ly) * 0.2));
            Assert.That(fed.Treasury, Is.EqualTo(treasury - p.Spent).Within(1));
            // The new pair is open: one gate straight there, the Federation's at its end, Wolf 359's leading power's at the other.
            Assert.That(g.Network.Links.Count, Is.EqualTo(linksBefore + 1));
            var gate = sol.GateTo(wolf);
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate.Name, Is.EqualTo("Wolf 359 Gate"));
            Assert.That(gate.Owner, Is.SameAs(fed));
            Assert.That(gate.Partner.Owner, Is.SameAs(wolf.Holder));
            Assert.That(gate.PositionKm.Length, Is.EqualTo(Galaxy.GateDistanceKm(sol)).Within(1e-6));
            Assert.That(g.Network.Route(sol, wolf), Is.EqualTo(new List<JumpGate> { gate }));
            Assert.That(GateJump.TryChoose(g.Network, sol, wolf, out var dep, out var arr), Is.True);
            Assert.That(dep, Is.SameAs(gate));
            Assert.That(arr, Is.SameAs(gate.Partner));
            // The constructors are released to go about their business.
            foreach (var c in p.Constructors) Assert.That(c.PhaseEndJd, Is.LessThan(jd + 2));
        }

        [Test]
        public void AGateShortOfMaterialOrConstructorsGoesSlower()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var wolf = g.Find("Wolf 359");
            var works = new GateConstruction(g, StartJd);
            var p = works.Start(sol.World.Sol, sol, wolf, null, StartJd);
            p.SurveyDays = 0;
            works.Step(p, StartJd + 1, 1);
            Assert.That(p.Stage, Is.EqualTo(GateStage.Building));

            // No electronics anywhere it can buy: not a day's work is done, and nothing else is taken either.
            var saved = sol.World.Economy.Settlements.Select(s => s.Stock[(int)Good.Electronics]).ToArray();
            foreach (var s in sol.World.Economy.Settlements) s.Stock[(int)Good.Electronics] = 0;
            double alloys = Stock(sol.World.Economy.Settlements, Good.Alloys);
            for (int d = 2; d <= 101; d++) works.Step(p, StartJd + d, 1);
            Assert.That(p.WorkDone, Is.EqualTo(0));
            Assert.That(Stock(sol.World.Economy.Settlements, Good.Alloys), Is.EqualTo(alloys));
            int k = 0;
            foreach (var s in sol.World.Economy.Settlements) s.Stock[(int)Good.Electronics] = saved[k++];

            // Supplied again: a day's work a day.
            for (int d = 102; d <= 201; d++) works.Step(p, StartJd + d, 1);
            Assert.That(p.WorkDone, Is.EqualTo(100).Within(1e-6));

            // One of its two constructors lost: half pace.
            p.Constructors[0].Destroyed = true;
            for (int d = 202; d <= 301; d++) works.Step(p, StartJd + d, 1);
            Assert.That(p.WorkDone, Is.EqualTo(150).Within(1e-6));

            // Both lost: work stops, until new hulls come out to the site.
            p.Constructors[1].Destroyed = true;
            int d2 = 302;
            for (; d2 < 302 + GateConstruction.ReplaceCrewDays - 1; d2++) works.Step(p, StartJd + d2, 1);
            Assert.That(p.WorkDone, Is.EqualTo(150).Within(1e-6));
            for (; d2 < 302 + GateConstruction.ReplaceCrewDays + 10; d2++) works.Step(p, StartJd + d2, 1);
            Assert.That(p.Constructors.Count(c => !c.Destroyed), Is.EqualTo(GateConstruction.ConstructorsPerProject));
            Assert.That(p.WorkDone, Is.GreaterThan(150));

            // A builder that cannot pay does no work either.
            double done = p.WorkDone;
            sol.World.Sol.Treasury = 0;
            for (int d = 0; d < 20; d++, d2++) works.Step(p, StartJd + d2, 1);
            Assert.That(p.WorkDone, Is.EqualTo(done));
        }

        /// <summary>
        /// Ten years of the gate builders on the galaxy as it starts (the economy itself is not stepped: each month the markets'
        /// stocks of the three materials are put back where they began, standing in for a decade of production). The network
        /// grows outward from the systems the Federation and the Accord hold, a gate at a time and years apart, and the first
        /// gates to frontier stars open them to settlement.
        /// </summary>
        [Test]
        public void TheNetworkGrowsOutwardOverASimulatedDecade()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            int systemsBefore = g.Systems.Count;
            int linksBefore = g.Network.Links.Count;
            var original = new HashSet<StarSystem>(g.Systems);
            var stocks = g.Systems.SelectMany(s => s.World.Economy.Settlements)
                .ToDictionary(s => s, s => GateConstruction.Materials.Select(m => s.Stock[(int)m]).ToArray());
            var opened = new List<(StarSystem system, Faction by, double jd)>();
            var added = new List<StarSystem>();
            g.SystemOpened += (s, f, jd) => opened.Add((s, f, jd));
            g.SystemAdded += (s, jd) => added.Add(s);
            var finished = new List<(GateProject p, double jd)>();
            g.Construction.Completed += p => finished.Add((p, p.DoneJd));
            var growth = new List<(double jd, int links)>();

            double end = StartJd + 10 * Year;
            for (double jd = StartJd + 1; jd <= end; jd += 1)
            {
                g.Construction.Update(jd);
                if (((int)(jd - StartJd)) % 30 == 0)
                {
                    foreach (var (s, st) in stocks)
                        for (int i = 0; i < st.Length; i++) s.Stock[(int)GateConstruction.Materials[i]] = Math.Max(s.Stock[(int)GateConstruction.Materials[i]], st[i]);
                    growth.Add((jd, g.Network.Links.Count));
                }
            }

            var built = finished.Where(f => f.p.Work == GateJob.NewLink).ToList();
            TestContext.Out.WriteLine($"{built.Count} gates built in ten years, {g.Network.Links.Count - linksBefore} new links, {opened.Count} systems opened:");
            foreach (var (p, jd) in built)
                TestContext.Out.WriteLine($"  {p.Builder.Name}: {p.From.Name} - {p.To.Name}, {p.Ly:0.0} ly, begun year {(p.StartJd - StartJd) / Year:0.0}, open year {(jd - StartJd) / Year:0.0}, {p.Spent / 1e6:0} M cr");
            foreach (var p in g.Construction.Active)
                TestContext.Out.WriteLine($"  under way: {p}, {p.Stage}, {p.Progress * 100:0}%");

            // It grew, by several gates, none sooner than two years after the planning began, each a gate pair on the network.
            Assert.That(g.Network.Links.Count - linksBefore, Is.GreaterThanOrEqualTo(3));
            Assert.That(built.Count, Is.EqualTo(g.Network.Links.Count - linksBefore));
            foreach (var (p, jd) in built)
            {
                Assert.That(jd - p.StartJd, Is.GreaterThan(2 * Year), p.ToString());
                Assert.That(p.Ly, Is.LessThanOrEqualTo(GateConstruction.MaxLinkLy));
                Assert.That(g.Network.Between(p.From, p.To), Is.Not.Null);
                // Built from a system its builder holds, by a state.
                Assert.That(p.From.Holder, Is.SameAs(p.Builder), p.ToString());
                Assert.That(p.Builder.Kind, Is.EqualTo(PolityKind.State));
            }
            Assert.That(growth.First().links, Is.EqualTo(linksBefore));
            Assert.That(growth.Where(x => x.jd < StartJd + 2 * Year).All(x => x.links == linksBefore), Is.True, "nothing finished inside two years");
            // The Federation, the richest, built from Sol's own region outward.
            Assert.That(built.Any(f => f.p.Builder == sol.World.Sol), Is.True);

            // Outward: frontier stars reached and opened to settlement, as empty systems on the network.
            Assert.That(opened.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Systems.Count, Is.EqualTo(systemsBefore + opened.Count));
            Assert.That(added, Is.EquivalentTo(opened.Select(o => o.system)));
            foreach (var (s, by, jd) in opened)
            {
                Assert.That(original.Contains(s), Is.False);
                Assert.That(FrontierSystems.Find(s.Name), Is.Not.Null);
                Assert.That(s.Settled, Is.False);
                Assert.That(s.OpenToSettlement, Is.True);
                Assert.That(s.OpenedJd, Is.EqualTo(jd));
                Assert.That(s.Gates.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(g.NearbyStars.Any(n => n.Name == s.Name), Is.False, "shown once, as a system");
                Assert.That(by.Kind, Is.EqualTo(PolityKind.State));
            }
            // Still one network: every system, old and new, is reachable from Sol, and the Sol-Centauri pair stands.
            foreach (var s in g.Systems) Assert.That(g.Network.Route(sol, s), Is.Not.Null, s.Name);
            Assert.That(sol.GateTo(ac).Name, Is.EqualTo("Centauri Gate"));
            // No two gates in a system share a spot.
            foreach (var s in g.Systems)
                foreach (var a in s.Gates)
                    foreach (var b in s.Gates)
                        if (a != b) Assert.That((a.PositionKm - b.PositionKm).Length, Is.GreaterThan(JumpGate.ActivationRangeKm * 2), s.Name);
        }

        /// <summary>
        /// U.SPC-T299: damage done to a ring while it is being rebuilt stands: the next day's work adds to what is left of it,
        /// rather than putting it back to where the work had got to, and the finished repair leaves the new damage to mend.
        /// </summary>
        [Test]
        public void DamageDoneToAGateUnderRepairIsNotUndoneByTheNextDaysWork()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var gate = sol.GateTo(ac);
            g.Construction.Damage(gate, 0.6, StartJd);
            double jd = StartJd;
            while (gate.Integrity < 0.7 && jd < StartJd + 5 * Year)
            {
                jd += 1;
                g.Construction.Update(jd);
            }
            var repair = g.Construction.RepairOf(gate);
            Assert.That(repair, Is.Not.Null);
            g.Construction.Damage(gate, 0.3, jd, "saboteurs");
            double damaged = gate.Integrity;
            jd += 1;
            g.Construction.Update(jd);
            Assert.That(gate.Integrity, Is.LessThan(damaged + 0.05), "a day's work undid the sabotage");
            while (repair.Active && jd < StartJd + 5 * Year)
            {
                jd += 1;
                g.Construction.Update(jd);
            }
            Assert.That(gate.Integrity, Is.LessThan(1), "the finished repair mended damage it was never sized for");
        }

        [Test]
        public void AWreckedGateShutsItsLinkUntilItsOwnerRebuildsIt()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var gate = sol.GateTo(ac);
            var link = gate.Link;
            g.Construction.Damage(gate, 1, StartJd);
            Assert.That(gate.Wrecked, Is.True);
            Assert.That(link.Working, Is.False);
            Assert.That(gate.CanActivate(gate.PositionKm), Is.False);
            Assert.That(gate.Partner.CanActivate(gate.Partner.PositionKm), Is.False, "nor from the far side");
            Assert.That(link.Open(sol, sol.World.Sol, StartJd), Is.False);
            // Traffic and the camera go round by other gates.
            var round = g.Network.Route(sol, ac);
            Assert.That(round, Is.Not.Null);
            Assert.That(round.Contains(gate), Is.False);
            Assert.That(round.Count, Is.GreaterThan(1));
            Assert.That(GateJump.TryChoose(g.Network, sol, ac, out var dep, out _), Is.True);
            Assert.That(dep, Is.Not.SameAs(gate));

            // Its owner sends constructors at the next planning and rebuilds it, for the repair's share of a new gate.
            double jd = StartJd;
            double backJd = double.NaN;
            while (gate.Integrity < 1 && jd < StartJd + 5 * Year)
            {
                jd += 1;
                g.Construction.Update(jd);
                if (double.IsNaN(backJd) && link.Working) backJd = jd;
            }
            var repair = g.Construction.Projects.Single(p => p.Work == GateJob.Repair);
            Assert.That(repair.Builder, Is.SameAs(gate.Owner));
            Assert.That(repair.Stage, Is.EqualTo(GateStage.Done));
            Assert.That(repair.StartJd - StartJd, Is.LessThanOrEqualTo(GateConstruction.PlanIntervalDays + 1));
            double full = GateConstruction.BuildDays(link.Ly) * GateConstruction.RepairShare;
            Assert.That(repair.DoneJd - repair.StartJd, Is.EqualTo(full).Within(2));
            Assert.That(repair.Used[0], Is.EqualTo(GateConstruction.Cost(link.Ly)[0] * GateConstruction.RepairShare).Within(1));
            // Back in action half way through the work, whole at the end.
            Assert.That(backJd - repair.StartJd, Is.EqualTo(full * JumpGate.OperationalIntegrity).Within(2));
            Assert.That(gate.Integrity, Is.EqualTo(1));
            Assert.That(g.Network.Route(sol, ac), Is.EqualTo(new List<JumpGate> { gate }));
        }

        /// <summary>
        /// U.SPC-T294: a gate the builders finish is armed like every other held gate (U.SPC-T92), both ends by the rule
        /// <see cref="GateNetwork.Fortify()"/> uses, so a war fleet has forts to fight there, not an open door.
        /// </summary>
        [Test]
        public void ANewGateOpensWithItsForts()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var wolf = g.Find("Wolf 359");
            var fed = sol.World.Sol;
            Assert.That(fed.IsMilitary && fed.Kind == PolityKind.State, Is.True, "the Federation is a state with a navy");
            var works = new GateConstruction(g, StartJd);
            var p = works.Start(fed, sol, wolf, null, StartJd);
            for (double jd = StartJd; p.Active && jd < StartJd + 10 * Year; jd += 1) works.Step(p, jd + 1, 1);
            Assert.That(p.Stage, Is.EqualTo(GateStage.Done));
            var gate = sol.GateTo(wolf);
            Assert.That(gate.MaxForts, Is.EqualTo(GateNetwork.GateForts));
            Assert.That(gate.Forts.Count, Is.EqualTo(GateNetwork.GateForts), "the builder's end opens guarded");
            var far = gate.Partner;
            int farForts = far.Owner != null && far.Owner.IsMilitary && far.Owner.Kind == PolityKind.State ? GateNetwork.GateForts : 0;
            Assert.That(far.MaxForts, Is.EqualTo(farForts));
            Assert.That(far.Forts.Count, Is.EqualTo(farForts), "the far end by the same rule as every held gate");
        }

        /// <summary>U.SPC-T294: a wrecked gate whose forts went down with it is rebuilt with its forts, not left open to the next fleet.</summary>
        [Test]
        public void ARebuiltGateGetsItsFortsBack()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var gate = sol.GateTo(ac);
            Assert.That(gate.Forts.Count, Is.EqualTo(GateNetwork.GateForts));
            g.Construction.Damage(gate, 1, StartJd);
            gate.Forts.Clear();
            for (double jd = StartJd; gate.Integrity < 1 && jd < StartJd + 5 * Year; jd += 1) g.Construction.Update(jd + 1);
            Assert.That(gate.Integrity, Is.EqualTo(1));
            Assert.That(gate.MaxForts, Is.EqualTo(GateNetwork.GateForts));
            Assert.That(gate.Forts.Count, Is.EqualTo(GateNetwork.GateForts));
        }

        [Test]
        public void GatesOnAWarsFrontLineAreSabotagedAndOnlyRebuiltInPeace()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            var fed = sol.World.Sol;
            var accord = ac.World.Sol;
            g.Polities.Diplomacy.Set(fed, accord, Relation.War);
            // The front: the Federation's rings whose far end the Accord holds, and the Accord's whose far end the Federation holds.
            var front = g.Network.Links.SelectMany(l => new[] { l.AtA, l.AtB })
                .Where(x => (x.Owner == fed && x.Partner.Home.Holder == accord) || (x.Owner == accord && x.Partner.Home.Holder == fed)).ToList();
            var behind = g.Network.Links.SelectMany(l => new[] { l.AtA, l.AtB })
                .Where(x => x.Owner == null || x.Partner.Home.Holder == null || g.Polities.Diplomacy.Get(x.Owner, x.Partner.Home.Holder) != Relation.War).ToList();
            Assert.That(front.Count, Is.GreaterThanOrEqualTo(2));

            double jd = StartJd;
            for (; jd < StartJd + 3 * Year; jd += 1) g.Construction.Update(jd);
            Assert.That(front.Any(x => x.Integrity < 1), Is.True, "three years of war and not a ring touched");
            Assert.That(behind.All(x => x.Integrity == 1), Is.True, "only the front line is hit");
            Assert.That(g.Construction.Projects.Any(p => p.Work == GateJob.Repair && front.Contains(p.Gate)), Is.False, "nothing rebuilt under the enemy's guns");

            // Peace: the damage is put right (the markets kept stocked, as a running economy would).
            g.Polities.Diplomacy.Set(fed, accord, Relation.Peace);
            var stocks = g.Systems.SelectMany(s => s.World.Economy.Settlements)
                .ToDictionary(s => s, s => GateConstruction.Materials.Select(m => s.Stock[(int)m]).ToArray());
            for (double stop = jd + 4 * Year; jd < stop && front.Any(x => x.Integrity < 1); jd += 1)
            {
                g.Construction.Update(jd);
                foreach (var (s, st) in stocks)
                    for (int i = 0; i < st.Length; i++) s.Stock[(int)GateConstruction.Materials[i]] = Math.Max(s.Stock[(int)GateConstruction.Materials[i]], st[i]);
            }
            Assert.That(front.All(x => x.Integrity == 1), Is.True);
        }

        /// <summary>The whole galaxy running (fleets, markets, traffic): the planners start real work and the constructors hold at their sites.</summary>
        [Test]
        public void TheBuildersWorkInTheRunningGalaxy()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            g.Focus = sol;
            double jd = StartJd;
            for (; jd < StartJd + 3; jd += 0.25) g.Update(jd);
            var started = g.Construction.Active.ToList();
            Assert.That(started.Any(p => p.Builder == sol.World.Sol), Is.True, "the Federation plans its first gate");
            foreach (var p in started) p.SurveyDays = 0;
            for (; jd < StartJd + 30; jd += 0.25) g.Update(jd);
            foreach (var p in started)
            {
                Assert.That(p.Stage, Is.EqualTo(GateStage.Building), p.ToString());
                Assert.That(p.WorkDone, Is.GreaterThan(0), p.ToString());
                Assert.That(p.Used.Sum(), Is.GreaterThan(0), p.ToString());
                var fleet = p.From.World.Fleet;
                foreach (var c in p.Constructors)
                {
                    Assert.That(fleet.Ships.Contains(c), Is.True);
                    if (c.Destroyed) continue;
                    Assert.That(c.Phase, Is.EqualTo(ShipPhase.Parked), c.Name);
                    Assert.That((c.Orbit.Center - p.SiteKm).Length, Is.LessThan(100), c.Name);
                }
            }
        }
    }
}
