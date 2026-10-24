using System;
using Unity.SpaceGod.Core;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>
    /// The capital ship flight model (U.SPC-T499, docs/FLIGHT_MODELS.md, Capital ships): Black Flag's handling in full 3D.
    /// An engine telegraph that holds, steerage that peaks at Half and pivots slowly at Stop, a keel that makes a turn a
    /// turning circle, pitch, yaw and roll free in every direction with no battle plane, each class's come-about, and the
    /// Destroyer as the skirmisher.
    /// </summary>
    [TestFixture]
    public class CapitalFlightTests
    {
        const double Dt = 0.02;

        static readonly ShipClass[] Line = { ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Battleship, ShipClass.Carrier };

        static double AngleDeg(DVec3 a, DVec3 b) => Math.Acos(Math.Max(-1, Math.Min(1, DVec3.Dot(a.Normalized, b.Normalized)))) * Astro.Rad2Deg;

        sealed class Rig
        {
            public PilotState S;
            public readonly ShipCombat Combat;
            public readonly HullDynamics Hull;
            public readonly PilotTuning Tuning;
            public readonly IFlightModel Model;
            public CapitalFlightModel Capital => (CapitalFlightModel)Model;

            public Rig(ShipClass c, DVec3 vel = default, IFlightModel model = null)
            {
                Combat = new ShipCombat(CombatSpec.Of(c), c);
                Hull = HullDynamics.Of(CombatSpec.Of(c));
                Tuning = PilotTuning.Of(c);
                Model = model ?? FlightModelRegistry.CreateDefault().For(c);
                S = PilotState.At(DVec3.Zero, vel, DVec3.UnitX);
            }

            public void Fly(PilotInput input, int steps = 1)
            {
                for (int i = 0; i < steps; i++) Model.Step(ref S, Combat, Hull, Tuning, input, DVec3.Zero, Dt);
            }

            /// <summary>Rings the telegraph <paramref name="steps"/> steps (one ring a frame), stick centred.</summary>
            public void Ring(int steps)
            {
                for (int i = 0; i < Math.Abs(steps); i++) Fly(new PilotInput { TelegraphStep = Math.Sign(steps) });
            }

            public double HalfSpeed => Capital.SpeedOf(EngineTelegraph.Half, Tuning);
        }

        /// <summary>Flies a 180 deg turn toward <paramref name="toward"/> and returns the seconds until the nose is on it and
        /// has stopped swinging, the worst slip of the track off the nose on the way, and how far the hull moved across its
        /// first heading (the turning circle's diameter).</summary>
        static (double seconds, double worstSlipDeg, double diameterKm) ComeAbout(Rig rig, DVec3 toward, DVec3 across)
        {
            var input = new PilotInput { DesiredForward = toward };
            double worst = 0;
            for (int i = 1; i <= 5000; i++)
            {
                rig.Fly(input);
                if (rig.S.Vel.Length > 1e-9) worst = Math.Max(worst, AngleDeg(rig.S.Vel, rig.S.Forward));
                if (AngleDeg(rig.S.Forward, toward) < 1 && rig.S.TurnRateDegS < 0.5)
                    return (i * Dt, worst, Math.Abs(DVec3.Dot(rig.S.Pos, across)));
            }
            return (double.PositiveInfinity, worst, 0);
        }

        // ------------------------------------------------------------ the registry

        [Test]
        public void EveryCapitalClassFliesTheCapitalModelOnItsOwnHandling()
        {
            var reg = FlightModelRegistry.CreateDefault();
            foreach (var c in new[] { ShipClass.Carrier, ShipClass.Battleship, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.CommandCruiser, ShipClass.Frigate })
            {
                Assert.That(reg.FamilyOf(c), Is.EqualTo(FlightFamily.Capital), c.ToString());
                var model = reg.For(c) as CapitalFlightModel;
                Assert.That(model, Is.Not.Null, c.ToString());
                Assert.That(model.Handling.ComeAboutS, Is.EqualTo(CapitalHandling.Of(c).ComeAboutS), c.ToString());
            }
            Assert.That(reg.ModelOf(FlightFamily.Capital), Is.InstanceOf<CapitalFlightModel>(), "a capital class with no numbers of its own flies the default");
            Assert.That(CapitalHandling.Of(ShipClass.Destroyer).ComeAboutS, Is.EqualTo(6));
            Assert.That(CapitalHandling.Of(ShipClass.Cruiser).ComeAboutS, Is.EqualTo(10));
            Assert.That(CapitalHandling.Of(ShipClass.Battleship).ComeAboutS, Is.EqualTo(14));
            Assert.That(CapitalHandling.Of(ShipClass.Carrier).ComeAboutS, Is.EqualTo(18));
        }

        // ------------------------------------------------------------ the come-about

        [Test]
        public void EachClassComesAboutAtHalfInItsTargetTimeTracingATurningCircle()
        {
            foreach (var c in Line)
            {
                var probe = new Rig(c);
                var rig = new Rig(c, DVec3.UnitX * probe.HalfSpeed);
                double target = rig.Capital.Handling.ComeAboutS;
                var (seconds, slip, _) = ComeAbout(rig, new DVec3(-1, 1e-3, 0).Normalized, DVec3.UnitY);
                TestContext.Out.WriteLine($"{c}: come-about {seconds:F2} s (target {target} s), worst slip {slip:F2} deg");
                Assert.That(seconds, Is.EqualTo(target).Within(target * 0.1), c + " come-about");
                Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Half), c + " was taken in hand at Half and held it");
                Assert.That(slip, Is.LessThan(5), c + ": the keel holds the track to the nose, a turning circle and not a pivot-and-slide");
                Assert.That(rig.S.Vel.Length, Is.EqualTo(rig.HalfSpeed).Within(rig.HalfSpeed * 0.1), c + " keeps its way through the turn");
                Assert.That(AngleDeg(rig.S.Vel, -DVec3.UnitX), Is.LessThan(5), c + " is making way on its new heading");
            }
        }

        [Test]
        public void ANewtonianCapitalSlidesThroughTheSameTurn()
        {
            var probe = new Rig(ShipClass.Cruiser);
            var keel = new Rig(ShipClass.Cruiser, DVec3.UnitX * probe.HalfSpeed);
            var newton = new Rig(ShipClass.Cruiser, DVec3.UnitX * probe.HalfSpeed, NewtonianFlightModel.Instance);
            var input = new PilotInput { DesiredForward = -DVec3.UnitY, Throttle = 0.5 };
            keel.Fly(input, 250);
            newton.Fly(input, 250);
            double keelSlip = AngleDeg(keel.S.Vel, keel.S.Forward), newtonSlip = AngleDeg(newton.S.Vel, newton.S.Forward);
            Assert.That(keelSlip, Is.LessThan(3));
            Assert.That(newtonSlip, Is.GreaterThan(keelSlip * 5), $"Newtonian slip {newtonSlip:F1} deg against the keel's {keelSlip:F1} deg");
        }

        // ------------------------------------------------------------ steerage

        [Test]
        public void SteeragePeaksAtHalfFallsAtFullAndFlankAndIsOnlyAPivotAtStop()
        {
            var rig = new Rig(ShipClass.Battleship);
            var m = rig.Capital;
            var t = rig.Tuning;
            var h = m.Handling;
            double peak = m.PeakTurnRateDegS(rig.Hull);
            Assert.That(m.TurnRateAt(0, rig.Hull, t), Is.EqualTo(peak * h.PivotFactor).Within(1e-9), "a slow pivot at Stop");
            Assert.That(h.PivotFactor, Is.EqualTo(0.25));
            double slow = m.TurnRateAt(m.SpeedOf(EngineTelegraph.Slow, t), rig.Hull, t);
            double half = m.TurnRateAt(m.SpeedOf(EngineTelegraph.Half, t), rig.Hull, t);
            double full = m.TurnRateAt(m.SpeedOf(EngineTelegraph.Full, t), rig.Hull, t);
            double flank = m.TurnRateAt(m.SpeedOf(EngineTelegraph.Flank, t), rig.Hull, t);
            Assert.That(half, Is.EqualTo(peak).Within(1e-9), "the peak is at Half, the fighting speed");
            Assert.That(slow, Is.LessThan(half).And.GreaterThan(peak * h.PivotFactor));
            Assert.That(full, Is.LessThan(half).And.GreaterThan(flank));
            Assert.That(m.TurnRateAt(-m.SpeedOf(EngineTelegraph.FullAstern, t), rig.Hull, t), Is.GreaterThan(peak * h.PivotFactor), "way astern steers too");

            // In flight: the same come-about from a dead stop takes far longer than at Half.
            var stopped = new Rig(ShipClass.Battleship);
            var (atStop, _, _) = ComeAbout(stopped, new DVec3(-1, 1e-3, 0).Normalized, DVec3.UnitY);
            Assert.That(stopped.S.Telegraph, Is.EqualTo(EngineTelegraph.Stop));
            Assert.That(atStop, Is.GreaterThan(h.ComeAboutS * 2), $"a pivot at Stop took {atStop:F1} s");
        }

        // ------------------------------------------------------------ the engine telegraph

        [Test]
        public void TheTelegraphStepsAndHoldsWithoutAKeyHeld()
        {
            var rig = new Rig(ShipClass.Cruiser);
            rig.Fly(new PilotInput());
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Stop), "a hull at rest is taken in hand at Stop");
            rig.Ring(2);
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Half));
            rig.Fly(new PilotInput(), 3000);
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Half), "the step holds");
            Assert.That(DVec3.Dot(rig.S.Vel, rig.S.Forward), Is.EqualTo(rig.HalfSpeed).Within(rig.HalfSpeed * 0.01), "and the hull settles on its speed");

            rig.Ring(-10);
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.FullAstern), "rung down, it stops at Full Astern");
            rig.Fly(new PilotInput(), 4000);
            double astern = rig.Capital.SpeedOf(EngineTelegraph.FullAstern, rig.Tuning);
            Assert.That(astern, Is.LessThan(0));
            Assert.That(DVec3.Dot(rig.S.Vel, rig.S.Forward), Is.EqualTo(astern).Within(Math.Abs(astern) * 0.01), "and backs");
            Assert.That(CapitalFlightModel.Ring(EngineTelegraph.Full, 5), Is.EqualTo(EngineTelegraph.Flank));
        }

        [Test]
        public void AHullAlreadyUnderWayIsTakenInHandAtTheNearestStep()
        {
            var probe = new Rig(ShipClass.Battleship);
            var rig = new Rig(ShipClass.Battleship, DVec3.UnitX * probe.Tuning.MaxSpeed * 0.97);
            rig.Fly(new PilotInput());
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Full));
        }

        [Test]
        public void FlankIsTheBoostAndRingsDownToFullOnceTheChargeIsSpent()
        {
            var rig = new Rig(ShipClass.Destroyer);
            rig.Ring(5);
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Flank));
            rig.Fly(new PilotInput(), (int)(rig.Tuning.BoostDurationS * 0.9 / Dt));
            double flank = rig.Capital.SpeedOf(EngineTelegraph.Flank, rig.Tuning);
            Assert.That(rig.Combat.BoostFraction, Is.LessThan(1), "Flank drains the boost");
            Assert.That(rig.S.Vel.Length, Is.GreaterThan(rig.Tuning.MaxSpeed * 1.05), "and drives past Full");
            rig.Fly(new PilotInput(), (int)(rig.Tuning.BoostDurationS / Dt) + 10);
            Assert.That(rig.S.Telegraph, Is.EqualTo(EngineTelegraph.Full), "spent, the telegraph rings down to Full");
            Assert.That(rig.Combat.BoostFraction, Is.GreaterThan(0).And.LessThan(1), "and the charge builds again");
            rig.Fly(new PilotInput(), 3000);
            Assert.That(rig.S.Vel.Length, Is.EqualTo(rig.Tuning.MaxSpeed).Within(rig.Tuning.MaxSpeed * 0.01));
            Assert.That(flank, Is.GreaterThan(rig.Tuning.MaxSpeed));
        }

        // ------------------------------------------------------------ full 3D, no battle plane

        [Test]
        public void ACapitalClimbsDivesAndPitchesAsReadilyAsItYaws()
        {
            var probe = new Rig(ShipClass.Cruiser);
            // A come-about over the top (a pitch) takes as long as one to starboard (a yaw): no plane, no preferred axis.
            var yaw = new Rig(ShipClass.Cruiser, DVec3.UnitX * probe.HalfSpeed);
            var pitch = new Rig(ShipClass.Cruiser, DVec3.UnitX * probe.HalfSpeed);
            var (yawS, _, _) = ComeAbout(yaw, new DVec3(-1, 1e-3, 0).Normalized, DVec3.UnitY);
            var (pitchS, pitchSlip, climbKm) = ComeAbout(pitch, new DVec3(-1, 0, 1e-3).Normalized, DVec3.UnitZ);
            Assert.That(pitchS, Is.EqualTo(yawS).Within(yawS * 0.02));
            Assert.That(pitchSlip, Is.LessThan(5));
            Assert.That(climbKm, Is.GreaterThan(0.05), "it looped up out of its plane");
            Assert.That(DVec3.Dot(pitch.S.Up, DVec3.UnitZ), Is.LessThan(-0.99), "and came out of the loop inverted: nothing levelled it");

            // Nose up into a climb, and it holds the climb.
            var climb = new Rig(ShipClass.Battleship, DVec3.UnitX * new Rig(ShipClass.Battleship).HalfSpeed);
            climb.Fly(new PilotInput { DesiredForward = DVec3.UnitZ }, 2000);
            climb.Fly(new PilotInput(), 2000);
            Assert.That(AngleDeg(climb.S.Forward, DVec3.UnitZ), Is.LessThan(1), "the nose stays where it was put");
            Assert.That(AngleDeg(climb.S.Vel, DVec3.UnitZ), Is.LessThan(2), "and the hull climbs along it");

            // And straight down.
            var dive = new Rig(ShipClass.Battleship, DVec3.UnitX * new Rig(ShipClass.Battleship).HalfSpeed);
            dive.Fly(new PilotInput { DesiredForward = -DVec3.UnitZ }, 2000);
            Assert.That(AngleDeg(dive.S.Vel, -DVec3.UnitZ), Is.LessThan(2));
        }

        [Test]
        public void ARolledCapitalStaysRolled()
        {
            var rig = new Rig(ShipClass.Battleship);
            rig.Fly(new PilotInput { Roll = 1 }, 300);
            double rolledDeg = AngleDeg(rig.S.Up, DVec3.UnitZ);
            Assert.That(rolledDeg, Is.GreaterThan(20), "roll is free");
            rig.Fly(new PilotInput(), 2000);
            Assert.That(rig.S.RollRateDegS, Is.EqualTo(0).Within(1e-6), "letting go stops the roll");
            Assert.That(AngleDeg(rig.S.Up, DVec3.UnitZ), Is.GreaterThan(rolledDeg), "and nothing rolls it back level");
        }

        [Test]
        public void ARolledHullsBroadsideBearsOnAShipAboveOrBelowIt()
        {
            // Flown at the helm: the fire cones are reckoned in the hull's own rolled frame (U.SPC-T533).
            var u = new BattleUnit { LengthKm = 1, Forward = DVec3.UnitX, PlayerControlled = true };
            var starboard = new SubsystemDef(SubsystemKind.LaserTurret, 0.1, 0, 0, coneAxis: FireCones.Starboard, coneHalfDeg: 75);
            var port = new SubsystemDef(SubsystemKind.LaserTurret, -0.1, 0, 0, coneAxis: FireCones.Port, coneHalfDeg: 75);
            var above = DVec3.UnitZ * 10;
            var below = -DVec3.UnitZ * 10;
            u.Up = DVec3.UnitZ;
            Assert.That(u.Bears(starboard, above + DVec3.UnitY * -10), Is.True, "level, a ship above and to starboard is in the starboard battery's cone");
            Assert.That(u.Bears(port, below + DVec3.UnitY * 10), Is.True, "and one below and to port in the port battery's");
            Assert.That(u.Bears(starboard, above), Is.False, "but one straight overhead is in neither");
            Assert.That(u.Bears(port, below), Is.False);

            // Rolled 90 deg to port (starboard side up): the starboard battery looks straight up, the port one straight down.
            u.Up = DVec3.UnitY;
            Assert.That(u.Up, Is.Not.EqualTo(DVec3.Zero));
            u.Basis(out var right, out _);
            Assert.That(AngleDeg(right, DVec3.UnitZ), Is.LessThan(1e-6));
            Assert.That(u.Bears(starboard, above), Is.True, "rolled, the starboard broadside bears on a ship overhead");
            Assert.That(u.Bears(port, below), Is.True, "and the port one on a ship beneath");
            Assert.That(u.Bears(port, above), Is.False);

            // A hull the battle flies is held level whatever roof it was last given.
            u.PlayerControlled = false;
            u.Basis(out right, out _);
            Assert.That(AngleDeg(right, -DVec3.UnitY), Is.LessThan(1e-6));
        }

        // ------------------------------------------------------------ the skirmisher

        [Test]
        public void TheDestroyerTurnsTheTightestCircleAndRunsFastestAtFlank()
        {
            double destroyerDiameter = 0, destroyerFlank = 0;
            foreach (var c in Line)
            {
                var probe = new Rig(c);
                var rig = new Rig(c, DVec3.UnitX * probe.HalfSpeed);
                var (_, _, diameter) = ComeAbout(rig, new DVec3(-1, 1e-3, 0).Normalized, DVec3.UnitY);
                double flank = rig.Capital.SpeedOf(EngineTelegraph.Flank, rig.Tuning);
                TestContext.Out.WriteLine($"{c}: turning circle {diameter * 1000:F0} m across at Half, Flank {flank * 1000:F0} m/s");
                if (c == ShipClass.Destroyer)
                {
                    destroyerDiameter = diameter;
                    destroyerFlank = flank;
                }
                else
                {
                    Assert.That(destroyerDiameter, Is.LessThan(diameter), "the destroyer's circle is tighter than the " + c + "'s");
                    Assert.That(destroyerFlank, Is.GreaterThan(flank), "and its Flank faster");
                }
            }
            var d = CapitalHandling.Of(ShipClass.Destroyer);
            var line = CapitalHandling.Of(ShipClass.Battleship);
            Assert.That(d.FullFactor, Is.GreaterThan(line.FullFactor), "it keeps more of its turn at speed");
            Assert.That(d.FlankFactor, Is.GreaterThan(line.FlankFactor));
        }

        // ------------------------------------------------------------ assist off

        [Test]
        public void ACapitalWithAssistOffIsTheNewtonianHull()
        {
            var input = new PilotInput { DesiredForward = -DVec3.UnitY, Throttle = 0.7, Roll = 0.3, FlightAssistOff = true };
            var cap = new Rig(ShipClass.Cruiser, new DVec3(0.05, 0, 0));
            var newton = new Rig(ShipClass.Cruiser, new DVec3(0.05, 0, 0), NewtonianFlightModel.Instance);
            cap.Fly(input, 200);
            newton.Fly(input, 200);
            Assert.That((cap.S.Vel - newton.S.Vel).Length, Is.EqualTo(0));
            Assert.That((cap.S.Pos - newton.S.Pos).Length, Is.EqualTo(0));
            Assert.That((cap.S.Forward - newton.S.Forward).Length, Is.EqualTo(0));
        }
    }
}
