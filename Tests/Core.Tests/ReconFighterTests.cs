using System;
using System.Linq;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The recon / electronic warfare fighter (U.SPC-T485, <see cref="ElectronicWarfare"/>): a strike craft of its own class
    /// that a carrier flies out ahead of its squadrons, which jams the targeting of the enemy ships near it so they fire less
    /// accurately and paints a capital for its bombers, whose torpedoes then home on it harder and ignore its decoys; berthed
    /// aboard, it stretches its carrier's sensor reach.
    /// </summary>
    [TestFixture]
    public class ReconFighterTests
    {
        const double StartJd = 2461324.0;

        static Ship Hull(ShipClass c, Faction f, int id, int fighters = -1)
        {
            var ship = new Ship { Id = id, Name = f.Prefix + " " + c + id, Spec = ShipClassSpec.Of(c), Faction = f, Combat = new ShipCombat(CombatSpec.Of(c), c) };
            if (fighters >= 0) ship.Combat.Fighters = fighters;
            return ship;
        }

        static (Battle b, Faction blue, Faction red) NewBattle(int seed = 5)
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var red = new Faction("Red", "RED", new Rgb(1, 0, 0)) { IsMilitary = true };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), StartJd, seed) { Launch = FighterAI.Launch, FighterStep = FighterAI.Step };
            return (b, blue, red);
        }

        static BattleUnit LeadOf(Battle b, FighterRole role, int side = 0) =>
            b.Units.First(u => u.Kind == UnitKind.Fighter && u.Side == side && u.Fighter.Role == role && u.Fighter.Lead == null);

        /// <summary>Takes every gun, launcher and tube on <paramref name="u"/> out of the fight.</summary>
        static void Disarm(BattleUnit u)
        {
            foreach (var s in u.Combat.Subsystems)
                if (s.Def.IsWeapon) s.Hp = 0;
        }

        /// <summary>Everything of <paramref name="u"/>'s mended and recharged, so a test's fight goes on as long as it needs.</summary>
        static void Mend(BattleUnit u)
        {
            u.Combat.Hull = u.Combat.Spec.Hull;
            foreach (var s in u.Combat.Subsystems)
            {
                if (s.Kind == SubsystemKind.ShieldGenerator) s.Shield = s.MaxShield;
                if (!s.Def.IsWeapon) s.Hp = s.Def.MaxHp;
            }
        }

        static void WithSetting(RangeSetting setting, double value, Action body)
        {
            double saved = setting.Value;
            try
            {
                setting.Set(value);
                body();
            }
            finally
            {
                setting.Set(saved);
            }
        }

        // ------------------------------------------------------------ the hull

        [Test]
        public void TheReconFighterIsAStrikeCraftHullOfItsOwn()
        {
            var c = ShipClass.Recon;
            var design = CombatSpec.Of(c);
            Assert.That(design.DesignName, Is.EqualTo("Recon"));
            Assert.That(ShipClassSpec.Of(c).DisplayName, Is.EqualTo("Recon fighter"));
            Assert.That(ShipClassSpec.Of(c).IsMilitary, Is.True);
            Assert.That(design.Crew, Is.EqualTo(2), "a pilot and an electronic warfare officer");
            Assert.That(design.MassKt, Is.GreaterThan(0));
            Assert.That(HullFrames.ForClass(c), Is.Not.Null, "it can be laid down at a yard");
            Assert.That(HullFrames.ForClass(c).Category, Is.EqualTo(HullCategory.Fighter));
            Assert.That(Economy.HullCost(c), Is.GreaterThan(Economy.HullCost(ShipClass.Interceptor)), "its electronics are dear");
            Assert.That(Loadout.IsSmallCraft(c), Is.True);
            Assert.That(FtlCarriage.SizeOf(c), Is.GreaterThan(0), "it rides in a carrier's hangar");
            Assert.That(CruiseDrives.Of(c), Is.Null, "with no FTL of its own");
            Assert.That(PilotTuning.ControlOf(c), Is.EqualTo(PilotControl.FreeAim));
            Assert.That(FlightModelRegistry.Default.FamilyOf(c), Is.EqualTo(FlightFamily.Interceptor), "an interceptor's airframe");
            Assert.That(design.Hull, Is.LessThan(CombatSpec.Of(ShipClass.Fighter).Hull), "lightly built: it does not fight");
            Assert.That(design.Layout.Any(d => d.Kind == SubsystemKind.TorpedoTube || d.Kind == SubsystemKind.MissileLauncher), Is.False);

            Assert.That(FighterCraft.OfClass(c), Is.SameAs(FighterCraft.Recon));
            Assert.That(FighterCraft.Recon.Role, Is.EqualTo(FighterRole.Recon));
            Assert.That(FighterCraft.SquadronMix, Has.No.Member(FighterRole.Recon), "a flight of its own, not a squadron");

            var carrier = CombatSpec.Of(ShipClass.Carrier);
            Assert.That(carrier.ReconCraft, Is.EqualTo(2), "a carrier carries a recon pair");
            Assert.That(carrier.HangarCraft, Is.EqualTo(carrier.Squadrons * 6 + 2));
            Assert.That(new ShipCombat(carrier, ShipClass.Carrier).Fighters, Is.EqualTo(carrier.HangarCraft), "berthed beside the squadrons");
        }

        // ------------------------------------------------------------ the recon flight

        [Test]
        public void ACarrierLaunchesItsReconPairBesideItsSquadronsAndTheyComeHomeWithThem()
        {
            var (b, blue, red) = NewBattle();
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var aloft = b.Units.Where(u => u.Kind == UnitKind.Fighter).ToList();
            Assert.That(aloft.Count, Is.EqualTo(20));
            var recon = aloft.Where(u => u.Fighter.Role == FighterRole.Recon).ToList();
            Assert.That(recon.Count, Is.EqualTo(2));
            Assert.That(recon.All(u => u.Fighter.Squadron == 3), Is.True, "numbered after the squadrons");
            Assert.That(recon.Count(u => u.Fighter.Lead == null), Is.EqualTo(1), "a lead and its wingman");
            Assert.That(recon.All(u => FighterCraft.Of(u.Fighter.Role).Class == ShipClass.Recon && !u.Fighter.Bomber), Is.True);
            Assert.That(aloft.Count(u => u.Fighter.Squadron < 3), Is.EqualTo(18), "every squadron still launches full");

            b.Advance(5);
            enemy.Surrendering = true;
            for (int i = 0; i < 600 && b.Units.Any(u => u.Kind == UnitKind.Fighter && u.Present); i++) b.Advance(1);
            Assert.That(b.Units.Where(u => u.Kind == UnitKind.Fighter).All(u => !u.Present), Is.True, "every craft is back aboard");
            Assert.That(carrier.Combat.Fighters, Is.EqualTo(carrier.Combat.Spec.HangarCraft), "recon flight included");
        }

        [Test]
        public void TheReconFlightScoutsAheadToTheEnemyCapitalAndHoldsOffItJammingAndPainting()
        {
            var (b, blue, red) = NewBattle();
            var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(14, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            Disarm(carrier);
            Disarm(enemy);
            // Only the recon flight flies.
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u.Fighter.Role != FighterRole.Recon) u.Docked = true;
            var lead = LeadOf(b, FighterRole.Recon);
            double startGap = (lead.Pos - enemy.Pos).Length;
            Assert.That(ElectronicWarfare.Jammed(b, enemy), Is.False, "out of reach, nothing is jammed yet");
            for (int i = 0; i < 150; i++)
            {
                b.Advance(1);
                Mend(enemy);
            }
            double gap = (lead.Pos - enemy.Pos).Length - enemy.Radius;
            TestContext.Out.WriteLine($"recon lead {startGap:0.0} km off the battleship at launch, {gap:0.0} km off its hull after 150 s");
            Assert.That(lead.Target, Is.SameAs(enemy), "its mark is the enemy capital");
            Assert.That(gap, Is.EqualTo(ElectronicWarfare.StandoffKm).Within(2.5), "it holds off at its standoff, outside point defence");
            Assert.That(gap, Is.GreaterThan(WeaponSpec.PointDefense.RangeKm));
            Assert.That(ElectronicWarfare.Jammed(b, enemy), Is.True, "jamming the battleship");
            Assert.That(ElectronicWarfare.Painted(b, enemy, 0), Is.True, "and painting it for the bombers");
            Assert.That(ElectronicWarfare.Painted(b, enemy, 1), Is.False, "for its own side only");
            Assert.That(ElectronicWarfare.Jammed(b, carrier), Is.False, "never its own side");
        }

        [Test]
        public void TheReconFighterMarksTheCapitalItsBombersAreGoingFor()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var big = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 6, 0), DVec3.UnitX);
            var small = b.AddShip(Hull(ShipClass.Destroyer, red, 2), 1, new DVec3(-14, -6, 0), DVec3.UnitX);
            var recon = LeadOf(b, FighterRole.Recon);
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u.Fighter.Bomber) u.Target = null;
            recon.Pos = DVec3.Zero;
            Assert.That(ElectronicWarfare.ChooseMark(b, recon), Is.SameAs(big), "left to itself, the biggest capital");
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u.Fighter.Bomber) u.Target = small;
            Assert.That(ElectronicWarfare.ChooseMark(b, recon), Is.SameAs(small), "the one its bombers are going for");
            Assert.That(FighterAI.ChooseTarget(b, recon), Is.SameAs(small), "a recon pilot never picks a fight of its own");
        }

        // ------------------------------------------------------------ jamming

        /// <summary>The share of a red battleship's gun and beam shots that hit, held 8 km off a blue carrier for five minutes
        /// (in three fights) with a blue recon pair jamming it, at <see cref="ElectronicWarfare.JamAccuracy"/> set to
        /// <paramref name="cut"/>.</summary>
        static double HitShare(double cut)
        {
            int hits = 0, misses = 0;
            WithSetting(ElectronicWarfare.JamAccuracy, cut, () =>
            {
                for (int seed = 11; seed < 14; seed++)
                {
                    var (b, blue, red) = NewBattle(seed);
                    var carrier = b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(9, 0, 0), -DVec3.UnitX);
                    var shooter = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(1, 0, 0), DVec3.UnitX);
                    var recon = b.Units.Where(u => u.Kind == UnitKind.Fighter && u.Fighter.Role == FighterRole.Recon).ToList();
                    foreach (var u in b.Units)
                        if (u.Kind == UnitKind.Fighter && u.Fighter.Role != FighterRole.Recon) u.Docked = true;
                    foreach (var r in recon) r.Pos = shooter.Pos + new DVec3(5, 2, 0);
                    int seen = b.FxSeq;
                    for (int i = 0; i < 300; i++)
                    {
                        // The battleship held in range of the carrier, its gunners the only thing that changes.
                        shooter.Pos = carrier.Pos - new DVec3(8, 0, 0);
                        shooter.Vel = carrier.Vel;
                        b.Advance(1);
                        foreach (var fx in b.Fx)
                        {
                            if (fx.Seq <= seen || fx.Side != 1) continue;
                            if (fx.Kind == FxKind.Beam || fx.Kind == FxKind.Railgun) hits++;
                            else if (fx.Kind == FxKind.BeamMiss || fx.Kind == FxKind.RailgunMiss) misses++;
                        }
                        seen = b.FxSeq;
                        Mend(carrier);
                        Mend(shooter);
                        foreach (var r in recon) Mend(r);
                        Assert.That(ElectronicWarfare.Jammed(b, shooter), Is.EqualTo(cut > 0), "the battleship stays inside the jammers' reach");
                    }
                }
            });
            TestContext.Out.WriteLine($"accuracy cut {cut:0.00}: {hits} hits, {misses} misses");
            Assert.That(hits + misses, Is.GreaterThan(150), "enough shots to judge by");
            return (double)hits / (hits + misses);
        }

        [Test]
        public void AJammedShipFiresLessAccurately()
        {
            double clear = HitShare(0), jammed = HitShare(ElectronicWarfare.JamAccuracy.Value);
            TestContext.Out.WriteLine($"hit share: clear {clear:P0}, jammed {jammed:P0}");
            Assert.That(jammed, Is.LessThan(clear * 0.85), "a jammed ship's gunners miss far more often");
        }

        [Test]
        public void JammingReachesOnlyAsFarAsItsRangeAndNotWhileTheReconCraftIsKnockedOut()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var enemy = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var recon = LeadOf(b, FighterRole.Recon);
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u != recon) u.Docked = true;
            ElectronicWarfare.Sweep(b);
            recon.Pos = enemy.Pos + new DVec3(enemy.Radius + ElectronicWarfare.JamRangeKm.Value - 0.5, 0, 0);
            Assert.That(ElectronicWarfare.JamFactor(b, enemy), Is.EqualTo(1 - ElectronicWarfare.JamAccuracy.Value).Within(1e-9));
            recon.Pos = enemy.Pos + new DVec3(enemy.Radius + ElectronicWarfare.JamRangeKm.Value + 0.5, 0, 0);
            Assert.That(ElectronicWarfare.JamFactor(b, enemy), Is.EqualTo(1), "out of reach");
            recon.Pos = enemy.Pos + new DVec3(enemy.Radius + 2, 0, 0);
            recon.Stunned = 10;
            Assert.That(ElectronicWarfare.Jammed(b, enemy), Is.False, "a recon craft knocked out by a pulse jams nothing");
            recon.Stunned = 0;
            WithSetting(ElectronicWarfare.JamRangeKm, 0, () => Assert.That(ElectronicWarfare.Jammed(b, enemy), Is.False, "the setting switches it off"));
            Assert.That(ElectronicWarfare.Jammed(b, enemy), Is.True);
            Assert.That(SettingsRegistry.Default.Find("dogfight.jamAccuracy"), Is.SameAs(ElectronicWarfare.JamAccuracy));
            Assert.That(SettingsRegistry.Default.Find("dogfight.jamRange"), Is.SameAs(ElectronicWarfare.JamRangeKm));
        }

        // ------------------------------------------------------------ painting

        [Test]
        public void ABomberGoesForThePaintedCapital()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var near = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-10, 4, 0), DVec3.UnitX);
            var painted = b.AddShip(Hull(ShipClass.Battleship, red, 2), 1, new DVec3(-14, -6, 0), DVec3.UnitX);
            var recon = LeadOf(b, FighterRole.Recon);
            var bomber = LeadOf(b, FighterRole.Bomber);
            bomber.Pos = DVec3.Zero;
            recon.Pos = painted.Pos + new DVec3(6, 0, 0);
            recon.Target = painted;
            ElectronicWarfare.Sweep(b);
            Assert.That(ElectronicWarfare.Painted(b, painted, 0), Is.True);
            Assert.That(ElectronicWarfare.Painted(b, near, 0), Is.False, "only its mark");
            int onPaint = 0;
            for (int i = 0; i < 30; i++)
                if (FighterAI.ChooseTarget(b, bomber) == painted) onPaint++;
            Assert.That(onPaint, Is.GreaterThan(25), "though the other is nearer");
            // Out of painting range, the paint is gone.
            recon.Pos = painted.Pos + new DVec3(painted.Radius + ElectronicWarfare.PaintRangeKm.Value + 1, 0, 0);
            Assert.That(ElectronicWarfare.Painted(b, painted, 0), Is.False);
        }

        [Test]
        public void APaintedTargetsDecoysSeldomDrawOffABombersTorpedoAndItSteersHarder()
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(30, 0, 0), -DVec3.UnitX);
            var target = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            var recon = LeadOf(b, FighterRole.Recon);
            recon.Pos = target.Pos + new DVec3(6, 0, 0);
            recon.Target = target;
            ElectronicWarfare.Sweep(b);
            var strike = new Missile { Target = target, Side = 0, Kind = MissileKind.Torpedo, SmallCraft = true, Torpedo = true };
            var capital = new Missile { Target = target, Side = 0, Kind = MissileKind.Torpedo, SmallCraft = false, Torpedo = true };
            double plain = Countermeasures.SeduceChance(MissileKind.Torpedo);
            Assert.That(plain, Is.GreaterThan(0));
            Assert.That(ElectronicWarfare.SeduceChance(b, strike), Is.EqualTo(plain * (1 - ElectronicWarfare.PaintDecoyResist.Value)).Within(1e-9));
            Assert.That(ElectronicWarfare.Guidance(b, strike), Is.EqualTo(ElectronicWarfare.PaintGuidance.Value));
            Assert.That(ElectronicWarfare.SeduceChance(b, capital), Is.EqualTo(plain), "a capital's own torpedoes are not strike torpedoes");
            Assert.That(ElectronicWarfare.Guidance(b, capital), Is.EqualTo(1));
            recon.Target = null;
            Assert.That(ElectronicWarfare.SeduceChance(b, strike), Is.EqualTo(plain), "unpainted, the decoys work as ever");
            Assert.That(ElectronicWarfare.Guidance(b, strike), Is.EqualTo(1));
        }

        /// <summary>
        /// How many of a fan of bomber torpedoes, each launched 1.6 km off a battleship but flying away across its bow at
        /// full speed and with only a few seconds of fuel, come round and strike it, with a recon fighter painting it or not.
        /// </summary>
        static int TorpedoesThatComeRound(bool painted)
        {
            var (b, blue, red) = NewBattle();
            b.AddShip(Hull(ShipClass.Carrier, blue, 0), 0, new DVec3(40, 0, 0), -DVec3.UnitX);
            var target = b.AddShip(Hull(ShipClass.Battleship, red, 1), 1, new DVec3(-14, 0, 0), DVec3.UnitX);
            Disarm(target);
            Disarm(b.Units[0]);
            target.Stunned = 1e6;
            var recon = LeadOf(b, FighterRole.Recon);
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u != recon) u.Docked = true;
            int struck = 0;
            for (int k = 0; k < 12; k++)
            {
                b.Missiles.Clear();
                recon.Pos = target.Pos + new DVec3(0, painted ? 6 : 40, 0);
                recon.Vel = target.Vel;
                recon.Target = target;
                Mend(target);
                double angle = k * Math.PI / 6;
                var off = new DVec3(Math.Cos(angle), Math.Sin(angle), 0);
                var across = new DVec3(-off.Y, off.X, 0);
                b.Missiles.Add(new Missile
                {
                    Pos = target.Pos + off * 1.6, Vel = across * WeaponSpec.Torpedo.MissileSpeedKmS, Target = target, Damage = 1,
                    Speed = WeaponSpec.Torpedo.MissileSpeedKmS, Hp = 25, Life = 2.5, Side = 0, Torpedo = true, Kind = MissileKind.Torpedo, SmallCraft = true,
                });
                var m = b.Missiles[0];
                for (int i = 0; i < 4 && !m.Gone; i++)
                {
                    b.Advance(1);
                    // Gone with fuel left: it struck (one that runs dry is gone with none).
                    if (m.Gone && m.Life > 0) struck++;
                }
            }
            return struck;
        }

        [Test]
        public void APaintedTargetIsFarEasierForTheTorpedoesToHit()
        {
            int plain = TorpedoesThatComeRound(false), painted = TorpedoesThatComeRound(true);
            TestContext.Out.WriteLine($"of 12 torpedoes flying across the bow: {plain} strike unpainted, {painted} painted");
            Assert.That(painted, Is.GreaterThan(plain), "the paint brings them round onto it");
            Assert.That(painted, Is.GreaterThanOrEqualTo(6));
        }

        // ------------------------------------------------------------ scouting

        [Test]
        public void ReconFightersAboardStretchTheirCarriersSensorReach()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var carrier = Hull(ShipClass.Carrier, blue, 0);
            var withRecon = SensorSuite.Of(carrier);
            Assert.That(ElectronicWarfare.ReconAboard(carrier), Is.EqualTo(2));
            carrier.Combat.Fighters = 0;
            var without = SensorSuite.Of(carrier);
            TestContext.Out.WriteLine($"carrier reach: {withRecon.RangeKm:0} km with its recon pair aboard, {without.RangeKm:0} km without");
            Assert.That(withRecon.RangeKm, Is.GreaterThan(without.RangeKm * 1.4), "its recon flight scouts ahead");
            Assert.That(withRecon.Resolution, Is.GreaterThan(without.Resolution));
            var battleship = Hull(ShipClass.Battleship, blue, 1);
            Assert.That(ElectronicWarfare.ReconAboard(battleship), Is.EqualTo(0), "a hull with no recon flight sees as before");
        }
    }
}
