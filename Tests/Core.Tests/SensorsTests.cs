using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UnitySpace.Core.Tests
{
    /// <summary>
    /// U.SPC-T66: sensors, signatures and stealth. Ships give off heat and EM (a drive burn and weapons fire bright,
    /// silent running dim); scanners have range and resolution; what anyone knows of another ship comes from detection:
    /// a contact starts as an unknown blip, resolves to its class and then its identity as it closes (at once from a
    /// transponder), and is lost once it has gone cold. The pictures drive haven discovery, witnesses, patrol scans
    /// (and so smuggling) and whether a lane ambush catches its victims unawares.
    /// </summary>
    [TestFixture]
    public class SensorsTests
    {
        const double StartJd = 2461324.0;
        const double Second = 1.0 / Astro.SecondsPerDay;

        /// <summary>Forty AU straight up out of the ecliptic from Earth: nobody else is anywhere near.</summary>
        static readonly DVec3 Deep = new DVec3(0, 0, 40 * Astro.AuKm);

        static World CreateWorld()
        {
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(StartJd);
            return World.CreateDefault(eph, StartJd);
        }

        static int Body(World w, string name) => w.Catalog[name].Index;

        static Ship Hull(ShipClass c, Faction f) =>
            new Ship { Id = 1, Name = c.ToString(), Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };

        /// <summary>A ship of the design as built, held at <paramref name="offset"/> from <paramref name="body"/>'s centre.</summary>
        static Ship Park(World w, string name, ShipClass c, Faction f, int body, DVec3 offset)
        {
            var s = w.Fleet.AddShip(name, c, f, c == ShipClass.Freighter ? ShipRole.Trader : ShipRole.Patrol);
            s.Combat = new ShipCombat(CombatSpec.Of(c), c);
            s.Orbit = new ParkingOrbit { Body = body, Center = offset };
            s.Phase = ShipPhase.Parked;
            s.PhaseStartJd = StartJd;
            return s;
        }

        static void Move(Ship s, DVec3 offset) => s.Orbit.Center = offset;

        /// <summary>Takes every ship out of the picture, and every fight with them.</summary>
        static void ClearShips(World w)
        {
            foreach (var s in w.Fleet.Ships) s.Destroyed = true;
            w.War.Battles.Clear();
        }

        static Ship WithScanner(Ship ship)
        {
            var economy = new Economy(new Diplomacy(), 0);
            var yard = economy.Add("Yard", body: 3, SettlementKind.Station, ship.Faction, 0);
            yard.Facilities.Add(new Facility(Recipe.Of(IndustryKind.Shipyard), 1));
            foreach (var g in new[] { Good.Alloys, Good.Electronics, Good.Machinery, Good.Munitions }) yard.Stock[(int)g] = 1e6;
            yard.RefreshPrices();
            ship.Faction.Treasury = 1e12;
            var l = Outfitting.LoadoutOf(ship);
            int bay = Enumerable.Range(0, l.Slots.Count).First(i => l.Slots[i].Kind == SlotKind.InternalBay && l.Fitted[i] == null);
            Assert.That(Outfitting.Fit(economy, ship, yard, bay, ModuleKind.Scanner, out _), Is.EqualTo(OutfitOutcome.Ok));
            return ship;
        }

        // ---------------------------------------------------------------- signatures

        [Test]
        public void DriveBurnsAndWeaponsFireAreBrightAndSilentRunningIsDim()
        {
            var traders = new Faction("Traders", "T", default);
            var freighter = Hull(ShipClass.Freighter, traders);
            double coasting = Signatures.Of(freighter, false);
            Assert.That(coasting, Is.EqualTo(1 + Signatures.CivilEm).Within(1e-9), "a freighter's hull is the unit, plus its nav systems");
            Assert.That(Signatures.Of(freighter, true), Is.GreaterThan(coasting * 20), "a burn lights it up");
            Assert.That(Signatures.Of(freighter, false, true), Is.GreaterThan(coasting * 10), "so does a fight");

            freighter.SilentRunning = true;
            Assert.That(Signatures.Of(freighter, false), Is.LessThan(coasting * 0.1), "silent running damps its heat and kills its EM");
            Assert.That(Signatures.Of(freighter, true), Is.GreaterThan(coasting * 20), "but cannot hide a burn");
            Assert.That(Signatures.Transponder(freighter), Is.False);
            freighter.SilentRunning = false;
            Assert.That(Signatures.Transponder(freighter), Is.True, "a lawful ship squawks its identity");

            Assert.That(Signatures.Of(Hull(ShipClass.Battleship, traders), false), Is.GreaterThan(coasting * 3), "a bigger hull is warmer");
            Assert.That(Signatures.Of(Hull(ShipClass.Courier, traders), false), Is.LessThan(coasting));

            var hulk = Hull(ShipClass.Freighter, traders);
            hulk.Derelict = true;
            Assert.That(Signatures.Of(hulk, false), Is.LessThan(coasting * 0.05), "a hulk is cold");
            var landed = Hull(ShipClass.Freighter, traders);
            landed.Phase = ShipPhase.Landed;
            Assert.That(Signatures.Of(landed, true), Is.EqualTo(0), "nobody sees a ship on the ground from space");

            var raider = Hull(ShipClass.Destroyer, new Faction("Raiders", "", default) { IsPirate = true });
            Assert.That(Signatures.Transponder(raider), Is.False, "pirates never squawk");
            raider.Phase = ShipPhase.Lurk;
            Assert.That(Signatures.Silent(raider), Is.True, "raiders lie dark beside a lane");
        }

        // ---------------------------------------------------------------- scanners and resolution

        [Test]
        public void AContactResolvesFromABlipToItsClassToItsIdentityAsItCloses()
        {
            var suite = SensorSuite.Military;
            double sig = 2;
            double reach = suite.ReachKm(sig);
            Assert.That(reach, Is.EqualTo(suite.RangeKm * System.Math.Sqrt(sig)).Within(1e-6), "reach goes as the root of the signature");
            double classAt = reach * suite.Resolution / Sensors.ClassQuality, idAt = reach * suite.Resolution / Sensors.IdentifyQuality;

            Assert.That(Sensors.Resolve(suite, sig, reach * 1.1, false), Is.EqualTo(ContactLevel.None));
            Assert.That(Sensors.Resolve(suite, sig, reach * 0.9, false), Is.EqualTo(ContactLevel.Blip));
            Assert.That(Sensors.Resolve(suite, sig, classAt * 0.9, false), Is.EqualTo(ContactLevel.Class));
            Assert.That(Sensors.Resolve(suite, sig, idAt * 0.9, false), Is.EqualTo(ContactLevel.Identified));
            Assert.That(Sensors.Resolve(suite, sig, reach * 0.9, true), Is.EqualTo(ContactLevel.Identified), "a transponder names itself");
            Assert.That(Sensors.Resolve(suite, 0, 1, true), Is.EqualTo(ContactLevel.None), "nothing to see");
        }

        [Test]
        public void ScannerSuitesSeeFurtherAndResolveFiner()
        {
            var f = new Faction("Traders", "T", default);
            var plain = Hull(ShipClass.Freighter, f);
            var fitted = WithScanner(Hull(ShipClass.Freighter, f));
            var a = SensorSuite.Of(plain);
            var b = SensorSuite.Of(fitted);
            Assert.That(a.RangeKm, Is.EqualTo(SensorSuite.Civil.RangeKm));
            Assert.That(b.RangeKm, Is.EqualTo(SensorSuite.Civil.RangeKm * (1 + SensorSuite.ScannerRangeShare)).Within(1e-6));
            Assert.That(b.Resolution, Is.EqualTo(SensorSuite.Civil.Resolution + SensorSuite.ScannerResolution).Within(1e-9));
            Assert.That(SensorSuite.Of(Hull(ShipClass.Destroyer, f)).RangeKm, Is.GreaterThan(a.RangeKm), "a warship's suite is better");
            // Where the merchant's nav sensors see only a blip, the scanner suite makes out a hull.
            double sig = 1, d = a.ReachKm(sig) * 0.6;
            Assert.That(Sensors.Resolve(a, sig, d, false), Is.EqualTo(ContactLevel.Blip));
            Assert.That(Sensors.Resolve(b, sig, d, false), Is.EqualTo(ContactLevel.Class));
        }

        // ---------------------------------------------------------------- pictures

        [Test]
        public void ThePlayersShipTracksAContactInAndLosesItOnceItHasGoneCold()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = Body(w, "Earth");
            var player = Park(w, "Lookout", ShipClass.Courier, w.Independents, earth, Deep);
            w.PilotedShip = player;
            var raider = Park(w, "Shade", ShipClass.Destroyer, w.Pirates, earth, Deep);
            var lost = new List<Ship>();
            w.Sensors.Lost += (by, s) => { if (by == null) lost.Add(s); };
            var at = w.Ephemeris.HelioAt(earth, StartJd) + Deep;
            var suite = SensorSuite.Of(player);
            double sig = Signatures.Of(raider, false);
            double reach = suite.ReachKm(sig);
            double jd = StartJd;

            ContactLevel Look(double km)
            {
                Move(raider, Deep + new DVec3(km, 0, 0));
                jd += 2 * Second;
                w.Sensors.UpdatePilot(player, at, false, jd);
                return w.Sensors.Player.Of(raider)?.Now ?? ContactLevel.None;
            }

            Assert.That(Look(reach * 1.2), Is.EqualTo(ContactLevel.None));
            Assert.That(w.Sensors.Player.Of(raider), Is.Null);
            Assert.That(Look(reach * 0.9), Is.EqualTo(ContactLevel.Blip));
            Assert.That(w.Sensors.Player.Of(raider).Label, Is.EqualTo("unknown contact"));
            Assert.That(Look(reach * suite.Resolution / Sensors.ClassQuality * 0.9), Is.EqualTo(ContactLevel.Class));
            Assert.That(w.Sensors.Player.Of(raider).Label, Does.Contain("destroyer"));
            Assert.That(Look(reach * suite.Resolution / Sensors.IdentifyQuality * 0.9), Is.EqualTo(ContactLevel.Identified));
            Assert.That(w.Sensors.Player.Of(raider).Label, Is.EqualTo(raider.Name));

            // It runs: the track goes cold but is held, still known, for a while...
            Assert.That(Look(reach * 5), Is.EqualTo(ContactLevel.None));
            var track = w.Sensors.Player.Of(raider);
            Assert.That(track.Cold, Is.True);
            Assert.That(track.Level, Is.EqualTo(ContactLevel.Identified));
            Assert.That(w.Sensors.Player.CountLive(ContactLevel.Identified), Is.EqualTo(0));
            // ...picked up again as a blip within the hold, it is still the ship it was...
            Assert.That(Look(reach * 0.9), Is.EqualTo(ContactLevel.Blip));
            Assert.That(w.Sensors.Player.LevelOf(raider), Is.EqualTo(ContactLevel.Identified));
            // ...and once it has been cold past the hold, it is lost.
            Look(reach * 5);
            jd += w.Sensors.PlayerHoldSeconds * 1.5 * Second;
            w.Sensors.UpdatePilot(player, at, false, jd);
            Assert.That(w.Sensors.Player.Of(raider), Is.Null);
            Assert.That(lost, Is.EqualTo(new[] { raider }));
        }

        [Test]
        public void AFactionKnowsOnlyWhatItsShipsAndStationsHaveSeenAndForgetsWhatGoesCold()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = Body(w, "Earth");
            Park(w, "Lookout", ShipClass.Destroyer, w.Sol, earth, Deep);
            var raider = Park(w, "Shade", ShipClass.Destroyer, w.Pirates, earth, Deep + new DVec3(20000, 0, 0));
            var sensors = w.Sensors;
            sensors.Sweep(StartJd);
            Assert.That(sensors.PictureOf(w.Sol).LevelOf(raider), Is.EqualTo(ContactLevel.Identified), "close aboard, it is made out");
            Assert.That(sensors.PictureOf(w.Belt).LevelOf(raider), Is.EqualTo(ContactLevel.None), "nobody of the Belt is anywhere near");
            Assert.That(sensors.Knows(w.Pirates, raider, StartJd), Is.EqualTo(ContactLevel.Identified), "its own know where it is");

            // A merchant at Earth is known to the Federation through Earth's traffic control, by its transponder.
            var merchant = Park(w, "Hauler", ShipClass.Freighter, w.Independents, earth, new DVec3(30000, 0, 0));
            Assert.That(sensors.Knows(w.Sol, merchant, StartJd), Is.EqualTo(ContactLevel.Identified));

            Move(raider, Deep + new DVec3(5e7, 0, 0));
            sensors.Sweep(StartJd + sensors.SweepDays);
            Assert.That(sensors.PictureOf(w.Sol).Of(raider).Cold, Is.True);
            Assert.That(sensors.Knows(w.Sol, raider, StartJd + sensors.SweepDays), Is.EqualTo(ContactLevel.Identified), "held while the track is fresh");
            sensors.Sweep(StartJd + sensors.SweepDays + sensors.FactionHoldDays * 1.5);
            Assert.That(sensors.PictureOf(w.Sol).Of(raider), Is.Null, "lost");
        }

        // ---------------------------------------------------------------- what the pictures drive

        [Test]
        public void ANavyThatTracksARaiderToItsHiddenHavenFindsIt()
        {
            var w = CreateWorld();
            ClearShips(w);
            var haven = w.Economy.Settlements.Single(s => s.Name == "Lantern Rock");
            Park(w, "Lookout", ShipClass.Destroyer, w.Sol, haven.Body, new DVec3(60000, 0, 0));
            Park(w, "Shade", ShipClass.Destroyer, w.Pirates, haven.Body, DVec3.Zero);
            Assert.That(haven.KnownBy(w.Sol), Is.False);
            w.Sensors.Sweep(StartJd);
            Assert.That(haven.KnownBy(w.Sol), Is.True);
            Assert.That(haven.KnownBy(w.Belt), Is.False);
            Assert.That(w.Events.Any(e => e.Text.Contains("Lantern Rock") && e.Text.Contains("sensors")), Is.True);
        }

        [Test]
        public void AHavenSweepReachesAsFarAsTheShipsScanners()
        {
            var navy = new Faction("Navy", "N", default) { IsMilitary = true };
            Assert.That(Outfitting.ScanRangeKm(Hull(ShipClass.Destroyer, navy)), Is.EqualTo(War.ScanRangeKm));
            Assert.That(Outfitting.ScanRangeKm(Hull(ShipClass.Freighter, navy)), Is.LessThan(War.ScanRangeKm), "a merchant's nav sensors see less");
            Assert.That(Outfitting.ScanRangeKm(WithScanner(Hull(ShipClass.Freighter, navy))), Is.GreaterThan(Outfitting.ScanRangeKm(Hull(ShipClass.Freighter, navy))));
        }

        [Test]
        public void AFightIsWitnessedFurtherOffThanSomethingDim()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = Body(w, "Earth");
            var at = w.Ephemeris.HelioAt(earth, StartJd) + Deep;
            var merchant = Park(w, "Hauler", ShipClass.Freighter, w.Independents, earth, Deep);
            double sees = SensorSuite.Civil.ReachKm(Signatures.Battle);
            Assert.That(w.Law.FindWitness(at + new DVec3(sees * 0.9, 0, 0), StartJd), Is.EqualTo("the " + merchant.Name));
            Assert.That(w.Law.FindWitness(at + new DVec3(sees * 0.9, 0, 0), StartJd, null, 1), Is.Null, "a cold hull that far off goes unseen");
            Assert.That(w.Law.FindWitness(at + new DVec3(sees * 1.1, 0, 0), StartJd), Is.Null);
            var warship = Park(w, "Lookout", ShipClass.Destroyer, w.Sol, earth, Deep);
            Assert.That(w.Law.FindWitness(at + new DVec3(sees * 1.1, 0, 0), StartJd), Is.EqualTo("the " + warship.Name), "a warship's suite sees further");
        }

        [Test]
        public void ALurkingRaiderCatchesAMerchantUnawaresButNotAWarshipOrAMerchantWithScanners()
        {
            var w = CreateWorld();
            ClearShips(w);
            int earth = Body(w, "Earth");
            var lurker = Park(w, "Shade", ShipClass.Destroyer, w.Pirates, earth, Deep);
            lurker.Phase = ShipPhase.Lurk;
            lurker.LaneHoldSide = new DVec3(30000, 0, 0);
            var merchant = Park(w, "Hauler", ShipClass.Freighter, w.Independents, earth, Deep);
            var navy = Park(w, "Lookout", ShipClass.Destroyer, w.Sol, earth, Deep);
            var fitted = WithScanner(Park(w, "Watcher", ShipClass.Freighter, w.Independents, earth, Deep));
            Assert.That(w.War.Forewarned(merchant, lurker, StartJd), Is.False, "its nav sensors see a blip, if that");
            Assert.That(w.War.Forewarned(navy, lurker, StartJd), Is.True);
            Assert.That(w.War.Forewarned(fitted, lurker, StartJd), Is.True);
            // An escort's scanners warn the whole group.
            var escort = Park(w, "Picket", ShipClass.Destroyer, w.Independents, earth, Deep);
            escort.Leader = merchant;
            Assert.That(w.War.Forewarned(merchant, lurker, StartJd), Is.True);
        }

        [Test]
        public void SilentRunningSlipsPastAPatrolThatOnlyHoldsABlip()
        {
            var w = CreateWorld();
            foreach (var s in w.Fleet.Ships) if (s.Spec.IsMilitary) s.Destroyed = true;
            w.War.Battles.Clear();
            int earth = Body(w, "Earth");
            var ship = Park(w, "Smuggler", ShipClass.Courier, w.Independents, earth, DVec3.Zero);
            w.PilotedShip = ship;
            var picket = Park(w, "Picket", ShipClass.Destroyer, w.Sol, earth, DVec3.Zero);
            var crate = ship.PersonalHold.AddFirstFit(Good.Platinum);
            crate.Stolen = true;

            var suite = SensorSuite.Of(picket);
            ship.SilentRunning = true;
            double reach = suite.ReachKm(Signatures.Of(ship, false));
            double classAt = reach * suite.Resolution / Sensors.ClassQuality;
            double slip = (reach + classAt) / 2;
            Assert.That(slip, Is.LessThan(w.Law.PatrolHailKm), "inside hailing range");

            w.Law.UpdatePilot(ship, earth, new DVec3(slip, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(w.Law.Hail, Is.Null, "running dark, it is only a blip to the patrol");
            w.Law.UpdatePilot(ship, earth, new DVec3(classAt * 0.5, 0, 0), DVec3.UnitX, StartJd + Second);
            Assert.That(w.Law.Hail, Is.Not.Null, "too close: the patrol makes out a hull and hails it");
            Assert.That(w.Law.Hail.Patrol, Is.EqualTo(picket));
        }

        [Test]
        public void GoingDarkUnderAPatrolsNoseDuringAScanIsRunningFromIt()
        {
            var w = CreateWorld();
            foreach (var s in w.Fleet.Ships) if (s.Spec.IsMilitary) s.Destroyed = true;
            w.War.Battles.Clear();
            int earth = Body(w, "Earth");
            var ship = Park(w, "Smuggler", ShipClass.Courier, w.Independents, earth, DVec3.Zero);
            w.PilotedShip = ship;
            var picket = Park(w, "Picket", ShipClass.Destroyer, w.Sol, earth, DVec3.Zero);
            ship.SilentRunning = true;
            double darkReach = SensorSuite.Of(picket).ReachKm(Signatures.Of(ship, false));
            ship.SilentRunning = false;
            double d = (darkReach + w.Law.PatrolHailKm) / 2;
            Assert.That(d, Is.LessThan(w.Law.PatrolHoldKm));

            w.Law.UpdatePilot(ship, earth, new DVec3(d, 0, 0), DVec3.UnitX, StartJd);
            Assert.That(w.Law.Hail, Is.Not.Null, "squawking, it is hailed");
            ship.SilentRunning = true;
            w.Law.UpdatePilot(ship, earth, new DVec3(d, 0, 0), DVec3.UnitX, StartJd + Second);
            Assert.That(w.Law.Hail, Is.Null);
            Assert.That(w.Law.Player.Crimes.Single().Kind, Is.EqualTo(CrimeKind.FleeingScan));
        }
    }
}
