using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    [TestFixture]
    public class SaveGameTests
    {
        const double StartJd = 2461324.0;
        const double Step = 0.02;

        static void Run(Galaxy g, SimClock clock, double days)
        {
            double end = clock.Jd + days;
            while (clock.Jd < end - 1e-9)
            {
                clock.SetJd(Math.Min(end, clock.Jd + Step));
                g.Update(clock.Jd);
            }
        }

        static void Run(World w, ref double jd, double days)
        {
            double end = jd + days;
            while (jd < end - 1e-9)
            {
                jd = Math.Min(end, jd + Step);
                w.Ephemeris.Update(jd);
                w.Update(jd);
            }
        }

        /// <summary>A player in the middle of things: flying a Sol merchant, with a purse and a reputation.</summary>
        static Ship MakePlayer(World w)
        {
            var ship = w.Fleet.Ships.First(s => !s.Destroyed && !s.IsEscort && s.Role == ShipRole.Trader && s.Faction == w.Sol);
            w.PilotedShip = ship;
            ship.Credits = 4321.5;
            ship.Faction.Treasury = 1234567.25;
            w.Adventures.Reputation[w.Sol] = 17.5;
            w.Adventures.Reputation[w.Pirates] = -4;
            return ship;
        }

        static byte[] Snapshot(object root) => ObjectGraph.ToBytes(root);

        static void AssertSame(byte[] a, byte[] b, string what)
        {
            Assert.That(b.Length, Is.EqualTo(a.Length), what + ": the two simulations differ in size");
            int first = -1;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) { first = i; break; }
            Assert.That(first, Is.EqualTo(-1), what + ": the two simulations differ from byte " + first);
        }

        [Test]
        public void ASavedWorldLoadsAsAnExactCopyAndBothRunOnInStep()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            double jd = StartJd;
            eph.Update(jd);
            var w = World.CreateDefault(eph, jd);
            Run(w, ref jd, 30);
            MakePlayer(w);

            var copy = ObjectGraph.Clone(w);
            Assert.That(copy, Is.Not.SameAs(w));
            AssertSame(Snapshot(w), Snapshot(copy), "just loaded");

            double jdA = jd, jdB = jd;
            Run(w, ref jdA, 20);
            Run(copy, ref jdB, 20);
            AssertSame(Snapshot(w), Snapshot(copy), "20 days on");
            Assert.That(copy.Fleet.Ships.Count, Is.EqualTo(w.Fleet.Ships.Count));
            Assert.That(copy.Events.Count, Is.EqualTo(w.Events.Count));
        }

        [Test]
        public void TheCopyIsWiredToItselfAndSharesTheStaticDesigns()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            double jd = StartJd;
            eph.Update(jd);
            var w = World.CreateDefault(eph, jd);
            Run(w, ref jd, 5);
            var copy = ObjectGraph.Clone(w);

            // The callbacks the world wires between its parts point at the copy's parts, not the original's.
            Assert.That(copy.Economy.EstimateTrip.Target, Is.SameAs(copy.Fleet));
            Assert.That(copy.Economy.DockAllowed.Target, Is.SameAs(copy.Law));
            Assert.That(copy.Fleet.BeforeEvent.Target, Is.SameAs(copy.Economy));
            Assert.That(copy.Ephemeris, Is.Not.SameAs(w.Ephemeris));
            Assert.That(copy.Fleet.Ships[0], Is.Not.SameAs(w.Fleet.Ships[0]));

            // A hull's design is the shared table entry, as it was before the save.
            int shared = 0;
            foreach (var s in copy.Fleet.Ships)
                if (s.Combat != null && ReferenceEquals(s.Combat.Spec, w.Fleet.Ships[s.Id].Combat.Spec)) shared++;
            Assert.That(shared, Is.GreaterThan(copy.Fleet.Ships.Count / 2), "designs were copied instead of shared");
        }

        [Test]
        public void TheWholeGalaxySavesToASlotMidBattleAndResumesInStep()
        {
            var clock = new SimClock(StartJd);
            var g = Galaxy.CreateDefault(clock.Jd);
            var sol = g.Systems[0].World;
            Run(g, clock, 10);
            // Save with a fight on, so battles, their units and their callbacks go through the file too.
            for (int i = 0; i < 400 && g.Systems.All(s => s.World.War == null || s.World.War.Battles.Count == 0); i++) Run(g, clock, 0.25);
            var war = g.Systems.Select(s => s.World).First(x => x.War != null && x.War.Battles.Count > 0).War;
            int battleIndex = g.Systems.FindIndex(s => s.World.War == war);
            var player = MakePlayer(sol);
            clock.SetWarp(2);

            var data = new SaveData { Clock = clock, Galaxy = g };
            data.Session.ActiveSystem = g.Systems[0].Name;
            data.Session.Piloted = player;
            data.Session.PilotFrameBody = 3;
            data.Session.Pilot = PilotState.At(new DVec3(7000, 0, 0), new DVec3(0, 7.5, 0), DVec3.UnitY);

            string dir = Path.Combine(Path.GetTempPath(), "uss-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                var slots = new SaveSlots(dir);
                var saveWatch = System.Diagnostics.Stopwatch.StartNew();
                var header = slots.Save(SaveSlots.Manual(1), data, "Before the battle");
                saveWatch.Stop();
                var loadWatch = System.Diagnostics.Stopwatch.StartNew();
                var loaded = slots.Load(SaveSlots.Manual(1), out var loadedHeader);
                loadWatch.Stop();
                TestContext.Out.WriteLine($"save {new FileInfo(header.Path).Length / 1024} KiB in {saveWatch.ElapsedMilliseconds} ms, load in {loadWatch.ElapsedMilliseconds} ms");

                Assert.That(header.Label, Is.EqualTo("Before the battle"));
                Assert.That(loadedHeader.SimJd, Is.EqualTo(clock.Jd));
                Assert.That(loadedHeader.ActiveSystem, Is.EqualTo("Sol System"));
                Assert.That(loadedHeader.PilotedShip, Is.EqualTo(player.Name));
                Assert.That(loadedHeader.Credits, Is.EqualTo(1234567.25));

                var lw = loaded.Galaxy.Systems[0].World;
                Assert.That(loaded.Clock.Jd, Is.EqualTo(clock.Jd));
                Assert.That(loaded.Clock.WarpIndex, Is.EqualTo(2));
                Assert.That(loaded.Galaxy.Systems.Count, Is.EqualTo(14));
                Assert.That(lw.PilotedShip, Is.SameAs(loaded.Session.Piloted), "the session's ship is the world's piloted ship");
                Assert.That(lw.PilotedShip.Credits, Is.EqualTo(4321.5));
                Assert.That(lw.PilotedShip.Faction.Treasury, Is.EqualTo(player.Faction.Treasury));
                Assert.That(lw.PilotedShip.Faction, Is.SameAs(lw.Sol));
                Assert.That(lw.Adventures.ReputationWith(lw.Sol), Is.EqualTo(17.5));
                Assert.That(lw.Adventures.ReputationWith(lw.Pirates), Is.EqualTo(-4));
                Assert.That(loaded.Session.Pilot.Vel.Y, Is.EqualTo(7.5));
                var lwar = loaded.Galaxy.Systems[battleIndex].World.War;
                Assert.That(lwar.Battles.Count, Is.EqualTo(war.Battles.Count));
                Assert.That(lwar.Battles[0].UnitDestroyed.Target, Is.SameAs(lwar), "a battle in progress reports to the loaded war");
                Assert.That(loaded.Galaxy.Network, Is.Not.Null);
                Assert.That(loaded.Galaxy.Traffic, Is.Not.Null);

                AssertSame(Snapshot(data), Snapshot(loaded), "just loaded");
                Run(g, clock, 10);
                Run(loaded.Galaxy, loaded.Clock, 10);
                AssertSame(Snapshot(data), Snapshot(loaded), "10 days on");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static SaveData Small(double jd, double scaleGoal)
        {
            // Slots only care that a save goes in and comes out: one unsettled system is plenty.
            var g = new Galaxy();
            g.Systems.Add(StarSystem.Create(NearbySystems.All().First(), jd));
            var data = new SaveData { Clock = new SimClock(jd), Galaxy = g };
            data.Session.ActiveSystem = g.Systems[0].Name;
            data.Session.ScaleGoal = scaleGoal;
            return data;
        }

        [Test]
        public void AutosavesTakeTurnsAndTheSlotsListNewestFirst()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uss-slots-" + Guid.NewGuid().ToString("N"));
            try
            {
                var slots = new SaveSlots(dir);
                Assert.That(slots.List(), Is.Empty);
                Assert.That(slots.Latest(), Is.Null);
                var t0 = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

                Assert.That(slots.Autosave(Small(StartJd, 1), t0).Slot, Is.EqualTo(SaveSlots.Autosave(1)));
                Assert.That(slots.Autosave(Small(StartJd + 1, 2), t0.AddMinutes(5)).Slot, Is.EqualTo(SaveSlots.Autosave(2)));
                Assert.That(slots.Autosave(Small(StartJd + 2, 3), t0.AddMinutes(10)).Slot, Is.EqualTo(SaveSlots.Autosave(3)));
                // The fourth overwrites the oldest, and the next goes to the oldest after that.
                Assert.That(slots.Autosave(Small(StartJd + 3, 4), t0.AddMinutes(15)).Slot, Is.EqualTo(SaveSlots.Autosave(1)));
                Assert.That(slots.NextAutosave(), Is.EqualTo(SaveSlots.Autosave(2)));
                Assert.That(slots.Header(SaveSlots.Autosave(1)).SimJd, Is.EqualTo(StartJd + 3));

                slots.Save(SaveSlots.Quick, Small(StartJd + 9, 9), null, t0.AddMinutes(20));
                slots.Save(SaveSlots.Manual(2), Small(StartJd + 7, 7), "Before the jump", t0.AddMinutes(1));
                var list = slots.List();
                Assert.That(list.Select(h => h.Slot), Is.EqualTo(new[]
                {
                    SaveSlots.Quick, SaveSlots.Autosave(1), SaveSlots.Autosave(3), SaveSlots.Autosave(2), SaveSlots.Manual(2),
                }));
                Assert.That(slots.Latest().Slot, Is.EqualTo(SaveSlots.Quick));
                Assert.That(slots.Header(SaveSlots.Manual(2)).Label, Is.EqualTo("Before the jump"));
                Assert.That(slots.Load(SaveSlots.Quick).Session.ScaleGoal, Is.EqualTo(9));
                Assert.That(slots.Load(SaveSlots.Manual(2)).Clock.Jd, Is.EqualTo(StartJd + 7));

                // Something that is not a save is listed as empty and refused when loaded.
                File.WriteAllText(slots.PathOf(SaveSlots.Manual(3)), "not a save");
                Assert.That(slots.Header(SaveSlots.Manual(3)), Is.Null);
                Assert.Throws<InvalidDataException>(() => slots.Load(SaveSlots.Manual(3)));
                Assert.That(slots.List().Count, Is.EqualTo(5));
                Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty, "a finished save leaves no temporary file");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void SharedReferencesCyclesCollectionsAndRandomStateComeBackIntact()
        {
            var a = new Node { Name = "a", Rng = new Random(42) };
            var b = new Node { Name = "b", Next = a, Rng = a.Rng };
            a.Next = b;
            a.Tags.Add("x");
            a.ByName["B"] = b;
            a.Seen.Add(b);
            a.Queue.Enqueue(3);
            a.Queue.Enqueue(4);
            a.Stack.Push(1);
            a.Stack.Push(2);
            a.Grid = new double[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
            a.Maybe = 5;
            a.Boxed = new DVec3(1, 2, 3);
            a.When = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            a.Pair = (b, 7);
            a.OnPoke = a.Poke;
            a.Rng.Next();
            a.None = new int[0];
            b.None = new List<int>().ToArray();

            var c = ObjectGraph.Clone(a);
            Assert.That(c.Next.Next, Is.SameAs(c), "the cycle closes");
            Assert.That(c.Next.Rng, Is.SameAs(c.Rng), "a shared generator stays shared");
            Assert.That(c.Rng.Next(), Is.EqualTo(a.Rng.Next()), "the generator carries on where it was");
            Assert.That(c.Tags, Is.EqualTo(new[] { "x" }));
            Assert.That(c.ByName["b"], Is.SameAs(c.Next), "the dictionary keeps its case-blind comparer");
            Assert.That(c.Seen.Contains(c.Next), Is.True);
            Assert.That(c.Queue.ToArray(), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(c.Stack.Pop(), Is.EqualTo(2));
            Assert.That(c.Grid[1, 2], Is.EqualTo(6));
            Assert.That(c.Maybe, Is.EqualTo(5));
            Assert.That(c.Boxed, Is.EqualTo(new DVec3(1, 2, 3)));
            Assert.That(c.When, Is.EqualTo(a.When));
            Assert.That(c.Pair.node, Is.SameAs(c.Next));
            Assert.That(c.Pair.n, Is.EqualTo(7));
            c.OnPoke();
            Assert.That(c.Pokes, Is.EqualTo(1));
            Assert.That(a.Pokes, Is.EqualTo(0), "the copied callback calls the copy");
            // Empty arrays all load as the runtime's shared one, whichever empty array they were.
            Assert.That(c.None, Is.SameAs(Array.Empty<int>()));
            Assert.That(c.Next.None, Is.SameAs(Array.Empty<int>()));
        }

        sealed class Node
        {
            public string Name;
            public Node Next;
            public Random Rng;
            public readonly List<string> Tags = new List<string>();
            public readonly Dictionary<string, Node> ByName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<Node> Seen = new HashSet<Node>();
            public readonly Queue<int> Queue = new Queue<int>();
            public readonly Stack<int> Stack = new Stack<int>();
            public double[,] Grid;
            public int? Maybe;
            public object Boxed;
            public DateTime When;
            public (Node node, int n) Pair;
            public Action OnPoke;
            public int Pokes;
            public int[] None;
            public void Poke() => Pokes++;
        }
    }
}
