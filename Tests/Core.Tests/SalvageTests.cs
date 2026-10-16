using System.Linq;
using NUnit.Framework;

namespace SolarSystem.Core.Tests
{
    /// <summary>
    /// Player salvage and boarding of derelicts (U.SPC-T45): the piloted ship matches velocity with a
    /// derelict or a wreck, latches on, fights through any raiders left aboard compartment by compartment,
    /// rescues survivors, and then salvages cargo into its grid hold, strips components (fitted in place of
    /// its own shot-away hardpoints, or scrapped into crates) or claims the hull if it can still fly.
    /// </summary>
    [TestFixture]
    public class SalvageTests
    {
        const double Jd = 2461324.0;

        static readonly Faction Navy = new Faction("Navy", "NS", new Rgb(0, 0, 1)) { IsMilitary = true, MarineQuality = 1.25 };
        static readonly Faction Traders = new Faction("Free Traders", "MV", new Rgb(1, 1, 1));
        static readonly Faction Raiders = new Faction("Black Banner", "", new Rgb(1, 0, 0)) { IsPirate = true, MarineQuality = 1.1 };

        static Ship Hull(ShipClass c, Faction f, int id, string name = null) =>
            new Ship { Id = id, Name = name ?? c + " " + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        static Ship Derelict(ShipClass c, double survivors, double lurkers, int id = 1)
        {
            var s = Hull(c, Traders, id, "MV Kestrel");
            s.Derelict = true;
            s.DerelictSinceJd = Jd;
            s.DerelictBy = Raiders;
            s.Combat.Interior.Abandon(survivors, lurkers, Raiders.MarineQuality);
            return s;
        }

        // ---------------------------------------------------------------- matching and latching

        [Test]
        public void TheClampsTakeHoldOnlyAlongsideWithVelocitiesMatched()
        {
            double range = SalvageSession.LatchRangeKm(0.6, 0.3);
            Assert.That(range, Is.EqualTo(SalvageSession.LatchClearanceKm + 0.45).Within(1e-9));
            var slow = new DVec3(0.005, 0, 0);
            Assert.That(SalvageSession.Check(new DVec3(range * 3, 0, 0), slow, range), Is.EqualTo(LatchState.TooFar));
            Assert.That(SalvageSession.Check(new DVec3(range * 0.5, 0, 0), new DVec3(0, 0.2, 0), range), Is.EqualTo(LatchState.TooFast), "close but still closing at 200 m/s");
            Assert.That(SalvageSession.Check(new DVec3(range * 0.5, 0, 0), slow, range), Is.EqualTo(LatchState.Ready));
        }

        [Test]
        public void OnlyAGenuineDerelictCanBeLatchedOnTo()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            var live = Hull(ShipClass.Freighter, Traders, 1);
            Assert.That(SalvageSession.Latch(player, live), Is.Null);
            var gone = Derelict(ShipClass.Freighter, 0, 0);
            gone.Destroyed = true;
            Assert.That(SalvageSession.Latch(player, gone), Is.Null);
            Assert.That(SalvageSession.Latch(player, Derelict(ShipClass.Freighter, 0, 0)), Is.Not.Null);
        }

        // ---------------------------------------------------------------- left adrift

        [Test]
        public void AStrandedHullCarriesWhatTheRaidersLeftAndItsStoresTheSameEveryTime()
        {
            var a = Hull(ShipClass.Freighter, Traders, 5);
            var b = Hull(ShipClass.Freighter, Traders, 5);
            SalvageSession.Strand(a, Raiders, Good.Copper, 300, Jd);
            SalvageSession.Strand(b, Raiders, Good.Copper, 300, Jd);
            Assert.That(a.Salvage[0].Good, Is.EqualTo(Good.Copper));
            Assert.That(a.Salvage[0].Tonnes, Is.EqualTo(300));
            Assert.That(a.Salvage.Select(s => s.Good), Does.Contain(Good.Food).And.Contain(Good.MedicalSupplies), "the ship's own stores");
            Assert.That(a.Combat.Interior.Survivors, Is.EqualTo(b.Combat.Interior.Survivors), "fixed per hull and time");
            Assert.That(a.Combat.Interior.Marines, Is.EqualTo(b.Combat.Interior.Marines));
            Assert.That(a.Combat.Interior.Crew, Is.Zero, "the crew are gone");
        }

        [Test]
        public void SomeDerelictsHoldSurvivorsSomeLurkingRaidersMostNeither()
        {
            int withSurvivors = 0, withRaiders = 0, empty = 0;
            for (int id = 0; id < 400; id++)
            {
                var s = Hull(ShipClass.Freighter, Traders, id);
                SalvageSession.Strand(s, Raiders, Good.Food, 0, Jd + id * 0.37);
                var i = s.Combat.Interior;
                if (i.Survivors > 0) withSurvivors++;
                if (i.Marines > 0) withRaiders++;
                if (i.Survivors <= 0 && i.Marines <= 0) empty++;
                Assert.That(i.Survivors, Is.LessThanOrEqualTo(s.Combat.Spec.Crew * 0.2 + 1));
                Assert.That(i.Marines, Is.InRange(0, 8));
            }
            TestContext.Out.WriteLine($"survivors {withSurvivors}, raiders {withRaiders}, empty {empty}");
            Assert.That(withSurvivors, Is.InRange(90, 190));
            Assert.That(withRaiders, Is.InRange(75, 165));
            Assert.That(empty, Is.GreaterThan(150));
        }

        // ---------------------------------------------------------------- fighting through

        [Test]
        public void RaidersAboardMustBeFoughtThroughBeforeAnythingIsTaken()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            var hulk = Derelict(ShipClass.Freighter, 5, 6);
            hulk.Salvage.Add(new SalvageStack(Good.Electronics, 50));
            double marinesBefore = player.Combat.Interior.Marines;
            var session = SalvageSession.Latch(player, hulk);
            Assert.That(session.Hostile, Is.True);
            Assert.That(session.SurvivorsRescued, Is.Zero, "they cannot be reached past the raiders");
            Assert.That(session.SalvageCargo(), Is.Zero);
            Assert.That(session.Strip(session.BestPart()).Use, Is.EqualTo(StripUse.None));
            Assert.That(session.CanClaim(out var why), Is.False);
            Assert.That(why, Does.Contain("raiders"));

            Assert.That(session.SendBoarders(), Is.True);
            Assert.That(session.Fighting, Is.True);
            Assert.That(player.Combat.MarinesAvailable, Is.Zero, "every marine went across");
            for (int s = 0; s < 3600 && session.Hostile; s++) session.Step(1);
            foreach (var line in session.Log) TestContext.Out.WriteLine(line);

            Assert.That(session.Hostile, Is.False, "fifty navy marines failed to clear six raiders");
            Assert.That(session.HostilesCleared, Is.True);
            Assert.That(session.Fighting, Is.False);
            Assert.That(player.Combat.Interior.Marines, Is.GreaterThan(0).And.LessThanOrEqualTo(marinesBefore), "the survivors of the boarding party came home");
            Assert.That(session.SurvivorsRescued, Is.EqualTo(5), "found once the raiders were beaten");
            Assert.That(session.SalvageCargo(), Is.GreaterThan(0));
        }

        [Test]
        public void ABoardingPartyThatIsBeatenLeavesTheRaidersHoldingTheHulk()
        {
            var player = Hull(ShipClass.Freighter, Traders, 0); // four marines against eight
            var hulk = Derelict(ShipClass.Freighter, 0, 8);
            var session = SalvageSession.Latch(player, hulk);
            Assert.That(session.SendBoarders(), Is.True);
            for (int s = 0; s < 3600 && session.Fighting; s++) session.Step(1);
            Assert.That(session.Hostile, Is.True);
            Assert.That(session.Fighting, Is.False);
            Assert.That(session.Log.Last(), Does.Contain("wiped out"));
            Assert.That(session.SendBoarders(), Is.False, "no marines left to send");
            Assert.That(session.Worked, Is.False);
        }

        [Test]
        public void WithNobodyHostileAboardSurvivorsAreFoundAtOnceAndComeAwayOnCastOff()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            double crew = player.Combat.Interior.Crew;
            var session = SalvageSession.Latch(player, Derelict(ShipClass.Freighter, 4, 0));
            Assert.That(session.SurvivorsRescued, Is.EqualTo(4));
            Assert.That(session.Worked, Is.True);
            session.CastOff();
            Assert.That(session.Ended, Is.True);
            Assert.That(player.Combat.Interior.Crew, Is.EqualTo(crew + 4).Within(1e-9), "taken aboard");
        }

        // ---------------------------------------------------------------- salvage, strip, claim

        [Test]
        public void SalvageGoesIntoTheGridHoldUntilItIsFullAndTheRestStaysAboard()
        {
            var player = Hull(ShipClass.Courier, Navy, 0); // a 3x3 hold
            var hulk = Derelict(ShipClass.Freighter, 0, 0);
            hulk.Salvage.Add(new SalvageStack(Good.Platinum, 400)); // a hundred 1x1 crates
            var session = SalvageSession.Latch(player, hulk);
            int moved = session.SalvageCargo();
            Assert.That(moved, Is.EqualTo(9));
            Assert.That(player.PersonalHold.Items.Count(i => i.Good == Good.Platinum), Is.EqualTo(9));
            Assert.That(hulk.Salvage.Single().Tonnes, Is.EqualTo(400 - 9 * CargoFootprint.Of(Good.Platinum).Tonnes).Within(1e-9));
            Assert.That(session.SalvageCargo(), Is.Zero, "the hold is full");
            Assert.That(session.Log.Last(), Does.Contain("No room"));
        }

        [Test]
        public void AStrippedPartReplacesAShotAwayHardpointOfTheSameKindElseItIsScrapped()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            var ours = player.Combat.Subsystems.First(s => s.Kind == SubsystemKind.Engine);
            ours.Hp = 0;
            var hulk = Derelict(ShipClass.Destroyer, 0, 0);
            var session = SalvageSession.Latch(player, hulk);

            var part = session.BestPart();
            Assert.That(part.Kind, Is.EqualTo(SubsystemKind.Engine), "what fixes our own hull comes first");
            part.Hp = part.Def.MaxHp * 0.8;
            var fitted = session.Strip(part);
            Assert.That(fitted.Use, Is.EqualTo(StripUse.Fitted));
            Assert.That(fitted.FittedTo, Is.SameAs(ours));
            Assert.That(ours.Health, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(part.Destroyed, Is.True, "the hulk lost it");
            Assert.That(session.Strippable, Does.Not.Contain(part));

            var shield = session.Strippable.First(s => s.Kind == SubsystemKind.ShieldGenerator);
            Assert.That(session.FitFor(shield), Is.Null, "ours are all sound");
            var scrapped = session.Strip(shield);
            Assert.That(scrapped.Use, Is.EqualTo(StripUse.Scrapped));
            Assert.That(scrapped.Good, Is.EqualTo(Good.Electronics));
            Assert.That(player.PersonalHold.Items.Count(i => i.Good == Good.Electronics), Is.EqualTo(1));
            Assert.That(session.PartsStripped, Is.EqualTo(2));
            Assert.That(session.Strippable.Any(s => s.Kind == SubsystemKind.Bridge || s.Kind == SubsystemKind.Reactor), Is.False, "the bridge and reactor stay with the hull");
        }

        [Test]
        public void AFlyableDerelictIsClaimedWithAPrizeCrewSurvivorsFirst()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            double crew = player.Combat.Interior.Crew;
            var hulk = Derelict(ShipClass.Freighter, 5, 0);
            var session = SalvageSession.Latch(player, hulk);
            Assert.That(session.CanClaim(out var why), Is.True, why);
            Assert.That(session.Claim(null, Jd), Is.True);

            Assert.That(hulk.Derelict, Is.False);
            Assert.That(hulk.Faction, Is.SameAs(Navy));
            Assert.That(hulk.Name, Is.EqualTo("NS Kestrel"));
            Assert.That(hulk.DerelictBy, Is.Null);
            Assert.That(hulk.Combat.Interior.Crew, Is.EqualTo(session.PrizeCrewNeeded).Within(1e-9));
            Assert.That(player.Combat.Interior.Crew, Is.EqualTo(crew - (session.PrizeCrewNeeded - 5)).Within(1e-9), "the five survivors make up part of it");
            Assert.That(session.Ended && session.Claimed, Is.True);
        }

        [Test]
        public void AHullWithoutEnginesOrAWreckCannotBeClaimed()
        {
            var player = Hull(ShipClass.Destroyer, Navy, 0);
            var hulk = Derelict(ShipClass.Freighter, 0, 0);
            foreach (var s in hulk.Combat.Subsystems) if (s.Kind == SubsystemKind.Engine) s.Hp = 0;
            var session = SalvageSession.Latch(player, hulk);
            Assert.That(session.CanClaim(out var why), Is.False);
            Assert.That(why, Does.Contain("engines"));
            Assert.That(session.Claim(null, Jd), Is.False);
            Assert.That(hulk.Derelict, Is.True);

            var wreck = new Wreck { Id = 3, Name = "NS Ardent", Faction = Navy, Ship = Hull(ShipClass.Cruiser, Navy, 9), LengthKm = 0.4 };
            var w = SalvageSession.Latch(player, wreck);
            Assert.That(w.Hostile, Is.False);
            Assert.That(w.Interior.Compartments.All(c => c.Vented), Is.True, "a gutted shell open to space");
            Assert.That(w.CanClaim(out why), Is.False);
            Assert.That(why, Does.Contain("wreck"));
        }

        [Test]
        public void AClaimNeedsHandsToSpare()
        {
            var player = Hull(ShipClass.Courier, Navy, 0);
            var hulk = Derelict(ShipClass.Battleship, 0, 0);
            var session = SalvageSession.Latch(player, hulk);
            Assert.That(session.CanClaim(out var why), Is.False);
            Assert.That(why, Does.Contain("prize crew"));
        }

        // ---------------------------------------------------------------- the live system

        [Test]
        public void InTheLiveSystemAHullLeftAdriftLiesFixedWhereItWasTakenReadyToBeSalvaged()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(Jd);
            var w = World.CreateDefault(eph, Jd);
            Ship found = null;
            double jd = Jd;
            for (; jd <= Jd + 365 && found == null; jd += 0.05)
            {
                eph.Update(jd);
                w.Update(jd);
                found = w.Fleet.Ships.FirstOrDefault(s => s.Derelict && !s.Destroyed);
            }
            Assert.That(found, Is.Not.Null, "nobody was left adrift in a year");
            TestContext.Out.WriteLine($"{found.Name}: {found.Salvage.Count} stacks, {found.Combat.Interior.Survivors:0} survivors, {found.Combat.Interior.Marines:0} raiders aboard");
            Assert.That(found.Orbit.Fixed && found.Phase == ShipPhase.Parked, Is.True, "drifting where it was taken, not still orbiting");
            Assert.That(found.DerelictBy.IsPirate, Is.True);
            Assert.That(found.Salvage, Is.Not.Empty, "at least its stores are still aboard");
            Assert.That(found.Combat.Interior.Captured || found.Combat.Interior.UnderBoarding, Is.False, "the boarding that took it is over");

            // A piloted hull alongside latches on, takes what it can and lets go; the event log has it.
            var player = w.Fleet.Ships.First(s => !s.Destroyed && !s.Derelict && s.Faction == w.Sol && s.Spec.Class == ShipClass.Destroyer);
            var session = SalvageSession.Latch(player, found);
            if (session.Hostile)
            {
                session.SendBoarders();
                for (int s = 0; s < 3600 && session.Fighting; s++) session.Step(1);
            }
            if (!session.Hostile) session.SalvageCargo();
            w.FinishSalvage(session, jd);
            Assert.That(session.Ended, Is.True);
            if (session.Worked) Assert.That(w.Events.Last().Text, Does.Contain(found.Name));
        }
    }
}
