using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// U.SPC-T104: every system's world runs at once, the one the player is in at full detail and the rest at
    /// coarse detail on a fixed six-hour grid, promoted to full detail on arrival without a jump.
    /// </summary>
    [TestFixture]
    public class MultiSystemLodTests
    {
        const double StartJd = 2461324.0;

        /// <summary>Frames of a little under half an hour of game time: a day a second at about fifty frames a second.</summary>
        const double FrameDays = 0.02;

        internal static void Run(Galaxy g, double fromJd, double toJd)
        {
            int n = (int)Math.Round((toJd - fromJd) / FrameDays);
            for (int k = 1; k <= n; k++) g.Update(fromJd + k * FrameDays);
            g.Update(toJd);
        }

        [Test]
        public void TheFocusRunsToTheMomentAndEveryOtherSystemOnItsCoarseGrid()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            g.Focus = sol;
            double end = StartJd + 3.13;
            Run(g, StartJd, end);

            Assert.That(sol.World.Jd, Is.EqualTo(end));
            Assert.That(sol.World.Coarse, Is.False);
            Assert.That(sol.Ephemeris.Jd, Is.EqualTo(end));
            foreach (var s in g.Systems.Where(s => s != sol))
            {
                Assert.That(s.World.Coarse, Is.True, s.Name);
                Assert.That(s.World.Fleet.Coarse, Is.True, s.Name);
                // On the last six-hour mark, not wherever the last frame happened to fall.
                Assert.That(s.World.Jd, Is.EqualTo(g.CoarseMark(end)), s.Name);
                double steps = s.World.Jd / g.CoarseStepDays;
                Assert.That(Math.Abs(steps - Math.Round(steps)), Is.LessThan(1e-6), s.Name);
                Assert.That(end - s.World.Jd, Is.InRange(0, g.CoarseStepDays), s.Name);
            }

            // No focus: everything at full detail, to the moment, as before.
            g.Focus = null;
            g.Update(end + 0.01);
            foreach (var s in g.Systems)
            {
                Assert.That(s.World.Coarse, Is.False, s.Name);
                Assert.That(s.World.Jd, Is.EqualTo(end + 0.01), s.Name);
            }
        }

        [Test]
        public void ACoarseBattleIsTheSameFightInFifteenSecondSteps()
        {
            int blueWins = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var b = Skirmish(seed);
                // One six-hour coarse step sees the whole fight through.
                b.Advance(0.25 * Astro.SecondsPerDay, coarse: true);
                Assert.That(b.Over, Is.True, $"seed {seed}: a coarse step did not see the battle through");
                if (b.Winner == 0) blueWins++;
                TestContext.Out.WriteLine($"seed {seed}: winner {b.Winner} after {b.Seconds / 60:0} min, dead {b.Units.Count(u => u.Dead)}, fled {b.Units.Count(u => u.Fled)}");
            }
            // The battleship group carries the day, as it does fought second by second (CombatTests).
            Assert.That(blueWins, Is.GreaterThanOrEqualTo(5), $"the battleship group won only {blueWins} of 6 at coarse detail");

            // Promoted mid-fight, the same battle goes on second by second from where it stands.
            var mid = Skirmish(3);
            mid.Advance(60, coarse: true);
            Assert.That(mid.Over, Is.False);
            double seconds = mid.Seconds;
            var hp = mid.Units.Select(u => u.Combat.Hull).ToList();
            mid.Advance(1);
            Assert.That(mid.Seconds, Is.EqualTo(seconds + 1).Within(1e-9));
            for (int i = 0; i < hp.Count; i++) Assert.That(mid.Units[i].Combat.Hull, Is.LessThanOrEqualTo(hp[i]));
            for (int i = 0; i < 20000 && !mid.Over; i++) mid.Advance(1);
            Assert.That(mid.Over, Is.True);
        }

        static Battle Skirmish(int seed)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed);
            int id = 0;
            foreach (var (c, side) in new[] { (ShipClass.Battleship, 0), (ShipClass.Cruiser, 0), (ShipClass.Destroyer, 0), (ShipClass.Destroyer, 1), (ShipClass.Destroyer, 1) })
            {
                var f = side == 0 ? blue : red;
                var ship = new Ship { Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
                id++;
                b.AddShip(ship, side, new DVec3(side == 0 ? 13 : -13, (id % 3 - 1) * 3.0, 0), side == 0 ? -DVec3.UnitX : DVec3.UnitX);
            }
            return b;
        }

        [Test]
        public void ArrivingInASystemPromotesItToFullDetailWithoutAJump()
        {
            // Twin galaxies, run alike with the player in Sol until Alpha Centauri is well into its own business.
            var a = Galaxy.CreateDefault(StartJd);
            var b = Galaxy.CreateDefault(StartJd);
            a.Focus = a.Find("Sol System");
            b.Focus = b.Find("Sol System");
            double arrive = StartJd + 12.37;
            Run(a, StartJd, arrive);
            Run(b, StartJd, arrive);
            var acA = a.Find("Alpha Centauri");
            var acB = b.Find("Alpha Centauri");
            Assert.That(acA.World.Jd, Is.LessThan(arrive), "a coarse system lags to its last six-hour mark");
            Assert.That(acA.World.Fleet.Ships.Count, Is.GreaterThan(20));
            Assert.That(acA.World.Fleet.Ships.Select(s => s.Name), Is.EqualTo(acB.World.Fleet.Ships.Select(s => s.Name)));

            // The player arrives in Alpha Centauri in one; in the other it is only brought up to the same moment, coarse.
            var battlesBefore = acA.World.War.Battles.ToList();
            a.Promote(acA, arrive);
            acB.Update(arrive);
            Assert.That(a.Focus, Is.SameAs(acA));
            Assert.That(acA.World.Coarse, Is.False);
            Assert.That(acA.World.Jd, Is.EqualTo(arrive), "promoted to the moment, not left at its last mark");
            Assert.That(acA.Ephemeris.Jd, Is.EqualTo(arrive));
            // Nothing it was doing is cut short: every ship's schedule runs on from the moment, and every fight
            // still under way is the same fight. A ship the schedule is holding (a derelict hulk, one in a fight)
            // has no schedule running to be overdue.
            foreach (var s in acA.World.Fleet.Ships.Where(s => !s.Destroyed && !s.IsEscort && !acA.World.Fleet.IsHeld(s)))
                Assert.That(Math.Min(s.PhaseEndJd, double.IsNaN(s.StrandJd) ? double.MaxValue : s.StrandJd), Is.GreaterThanOrEqualTo(arrive - 1e-9), s.Name + " overdue");
            foreach (var battle in battlesBefore)
                if (!battle.Over) Assert.That(acA.World.War.Battles, Does.Contain(battle), battle.Name);

            // Promotion changes where nothing is: every ship stands exactly where the coarse twin has it.
            var shipsA = acA.World.Fleet.Ships;
            var shipsB = acB.World.Fleet.Ships;
            Assert.That(shipsA.Count, Is.EqualTo(shipsB.Count));
            for (int i = 0; i < shipsA.Count; i++)
            {
                if (shipsA[i].Destroyed) continue;
                var pa = acA.World.Fleet.StateOf(shipsA[i], arrive).HelioKm;
                var pb = acB.World.Fleet.StateOf(shipsB[i], arrive).HelioKm;
                Assert.That((pa - pb).Length, Is.LessThan(1e-6), shipsA[i].Name);
            }

            // And from there it flies on at full detail, minute by minute, along the same paths: over the next half
            // hour a ship with nothing scheduled in it is where its coarse twin's schedule puts it, give or take
            // the few km a pilot sidesteps another ship by.
            a.Focus = acA;
            double end = arrive + 0.02;
            var steady = new List<int>();
            for (int i = 0; i < shipsB.Count; i++)
                if (!shipsB[i].Destroyed && !shipsB[i].IsEscort && shipsB[i].Battle == null && shipsB[i].PhaseEndJd > end && !(shipsB[i].StrandJd <= end)) steady.Add(i);
            Assert.That(steady.Count, Is.GreaterThan(10));
            double worst = 0;
            for (double jd = arrive + 1.0 / 1440; jd <= end + 1e-9; jd += 1.0 / 1440)
            {
                a.Update(jd);
                acB.Ephemeris.Update(jd);
                foreach (int i in steady)
                {
                    var pa = acA.World.Fleet.StateOf(shipsA[i], jd).HelioKm;
                    var pb = acB.World.Fleet.StateOf(shipsB[i], jd).HelioKm;
                    worst = Math.Max(worst, (pa - pb).Length);
                }
            }
            TestContext.Out.WriteLine($"{steady.Count} ships followed for half an hour after promotion; the farthest any strayed from its coarse twin: {worst:0.0} km");
            Assert.That(worst, Is.LessThan(200));
        }

        [Test]
        public void HelioAtGivesTheSameAnswerAskedInAnyOrder()
        {
            var cached = new Ephemeris(BodyCatalog.CreateSolSystem());
            var rng = new Random(5);
            var dates = new[] { StartJd, StartJd + 0.5, StartJd + 17.25 };
            for (int k = 0; k < 400; k++)
            {
                int body = rng.Next(cached.Catalog.Count);
                double jd = dates[rng.Next(dates.Length)];
                var fresh = new Ephemeris(cached.Catalog);
                Assert.That(cached.HelioAt(body, jd), Is.EqualTo(fresh.HelioAt(body, jd)), cached.Catalog[body].Name);
            }
        }
    }

    /// <summary>
    /// U.SPC-T104's performance budget. Run on its own (not beside the other fixtures), so the clock it reads is this
    /// simulation's and nobody else's.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class MultiSystemBudgetTests
    {
        const double StartJd = 2461324.0;

        /// <summary>
        /// The budget: all fourteen systems with their full traffic (fleets, markets, wars, the gate traffic between
        /// them) stepped through a simulated year at a day a second's frame rate, the player in Sol (the busiest system,
        /// at full detail) and the rest at coarse detail, in under <see cref="BudgetSeconds"/>. A debug build does it in
        /// about a minute and a release build in under half that. Held to is the lesser of its wall-clock time and the
        /// processor time it took, so another program busy on the same machine does not count against it; both are reported.
        /// </summary>
        [Test]
        public void EverySystemRunsAYearOfFullTrafficWithinTheBudget()
        {
            var g = Galaxy.CreateDefault(StartJd);
            var sol = g.Find("Sol System");
            var ac = g.Find("Alpha Centauri");
            g.Focus = sol;
            var people = g.Systems.Skip(2).SelectMany(s => s.World.Economy.Settlements).ToDictionary(s => s, s => s.Population);
            int coarseBattles = 0;
            ac.World.War.BattleStarted += _ => coarseBattles++;

            var process = Process.GetCurrentProcess();
            var cpuBefore = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            double end = StartJd + 365.25;
            MultiSystemLodTests.Run(g, StartJd, end);
            watch.Stop();
            process.Refresh();
            double cpu = (process.TotalProcessorTime - cpuBefore).TotalSeconds;

            int solShips = sol.World.Fleet.Ships.Count(s => !s.Destroyed), acShips = ac.World.Fleet.Ships.Count(s => !s.Destroyed);
            TestContext.Out.WriteLine($"a year of {g.Systems.Count} systems in {watch.Elapsed.TotalSeconds:0.0} s wall-clock, {cpu:0.0} s of processor time " +
                $"(budget {BudgetSeconds} s): Sol {solShips} ships at full detail; Alpha Centauri {acShips} ships and {coarseBattles} battles at coarse detail; " +
                $"{g.Traffic.Crossings} gate crossings");

            // The year was really lived everywhere: every world reached it, traffic crossed, and the coarse system fought.
            foreach (var s in g.Systems) Assert.That(end - s.World.Jd, Is.InRange(0, g.CoarseStepDays), s.Name);
            Assert.That(solShips, Is.GreaterThan(100));
            Assert.That(acShips, Is.GreaterThan(30));
            Assert.That(g.Traffic.Crossings, Is.GreaterThan(20));
            Assert.That(coarseBattles, Is.GreaterThan(0));
            // A fight in a system nobody is watching is seen through within a coarse step or two of its four-hour limit.
            foreach (var b in ac.World.War.Battles)
                Assert.That(end - b.StartJd, Is.LessThan(1), b.Name + " dragging on at coarse detail");
            // The frontier societies (U.SPC-T90) lived through it alongside everyone else's traffic: nobody starved out.
            foreach (var (s, p) in people)
                if (s.Kind != SettlementKind.Haven) Assert.That(s.Population, Is.GreaterThan(p * 0.8), s.Name);
            Assert.That(Math.Min(cpu, watch.Elapsed.TotalSeconds), Is.LessThan(BudgetSeconds));
        }

        /// <summary>The year's budget, seconds.</summary>
        public const double BudgetSeconds = 150;
    }
}
