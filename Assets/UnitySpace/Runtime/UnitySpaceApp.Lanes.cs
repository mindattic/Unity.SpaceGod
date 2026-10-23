using System;
using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The player riding the hyperlanes (U.SPC-T41). Flying the hull in through an on-ramp's ring gate
    /// (drawn by <see cref="Views.LaneView"/>) joins the lane by the same <see cref="Fleet.JoinLane"/> /
    /// <see cref="Fleet.LaneProgress"/> path AI traffic rides: the hull is drawn on the lane and the chase
    /// camera rides the tube with it, the clock runs fast enough that a ride of hours takes under a minute,
    /// and at the far ramp the pilot comes off (or, having picked one, carries on along another lane from
    /// there). Going in and coming out the gate flashes and the view kicks wide; on the lane, tunnel
    /// streaks stream past and the field of view opens with the ride's speed, closing again as it eases
    /// to rest at the off-ramp.
    ///
    /// The lane is a chain of rings the hull flies through (U.SPC-T379, drawn by <see cref="Views.LaneView"/> when
    /// near enough to see), speeding it up through the first half and braking it through the second; each
    /// system's network leads on to its jump gates by a spur per gate, joined by flying through the ring at
    /// either end the way it runs, so a ride can end in range of a gate and, through it, pick up the far
    /// system's own network at the spur beside its gate.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>Whether the piloted hull is riding a hyperlane right now.</summary>
        public bool RidingLane => IsPiloting && Piloted.Ship != null && Piloted.Ship.Phase == ShipPhase.Lane;

        /// <summary>The lanes the ride's off-ramp leads on to (keys 2 and up), and the one picked (-1: come off, key 1).</summary>
        public List<LaneHop> RideOnward { get; } = new List<LaneHop>();
        public int RideChoice { get; private set; } = -1;

        /// <summary>The ring chain of the lane ridden (U.SPC-T379): how many rings it strings, how many the hull has flown
        /// through, and whether they are speeding it up or braking it; 0 rings when not riding.</summary>
        public int RideRingCount(out int passed, out bool accelerating)
        {
            passed = 0;
            accelerating = false;
            return RidingLane ? Fleet.RideRings(Piloted.Ship, Clock.Jd, out passed, out accelerating) : 0;
        }

        /// <summary>The ride's speed as a fraction of its peak (0 at either ramp, 1 midway).</summary>
        public float LaneRideSpeed { get; private set; }

        /// <summary>The ring gate's flash as the hull goes through it (1, fading to 0).</summary>
        public float GateFlash { get; private set; }

        /// <summary>Tunnel streaks still fading after coming off a lane (1, fading to 0).</summary>
        public float LaneExitFade { get; private set; }

        /// <summary>How strongly the tunnel streaks show: with the ride's speed, and briefly after it.</summary>
        public float LaneStreaks => RidingLane ? 0.3f + 0.7f * LaneRideSpeed : LaneExitFade * 0.35f;

        /// <summary>Real seconds left of the ride at the clock's present rate, or 0 when not riding.</summary>
        public double RideSecondsLeft =>
            RidingLane ? Math.Max(0, Piloted.Ship.PhaseEndJd - Clock.Jd) * Astro.SecondsPerDay / Math.Max(Clock.Warp.SimSecondsPerSecond, 1e-9) : 0;

        /// <summary>A ride of hours runs at the slowest warp that gets it over in this many real seconds.</summary>
        const double RideRealSeconds = 45;

        const float LaneFov = 104f, GateFovKick = 24f, GateFlashS = 0.7f, LaneExitFadeS = 1.6f;

        /// <summary>The warp the clock ran at before the ride (restored coming off), or -1 when not riding.</summary>
        int rideWarpBefore = -1;

        /// <summary>Whether the hull was inside a ring gate last frame: a ride starts only on flying in, not on
        /// taking the helm at a ramp or sitting at the off-ramp just come out of.</summary>
        bool insideRampGate;

        float fovKick;

        // ---------------------------------------------------------------- interdiction (U.SPC-T42)

        /// <summary>The armed interdictor the ride's scanners have picked up ahead, or null (see <see cref="War.ThreatAhead"/>).</summary>
        public LaneThreat? RideThreat { get; private set; }

        /// <summary>Real seconds, at the clock's present rate, until the pulse ahead can reach the hull (0 with none).</summary>
        public double ThreatRealSeconds =>
            RideThreat is LaneThreat t ? t.SecondsToPulse / Math.Max(Clock.Warp.SimSecondsPerSecond, 1e-9) : 0;

        /// <summary>Seconds left with drives and shields knocked out by a pulse (0 when they are up).</summary>
        public double PilotStunSeconds
        {
            get
            {
                var ship = IsPiloting ? Piloted.Ship : null;
                var unit = ship?.Battle?.UnitOf(ship);
                // A hunter's EMP out of battle (U.SPC-T273) knocks the drives out just as a pulse or an EMP round in one.
                return Math.Max(unit != null && unit.Stunned > 0 ? unit.Stunned : 0, pilotEmpSeconds);
            }
        }

        /// <summary>Whether the hull, out of its lane part-way (dropped out, or thrown out and fought clear), can get back in where it left (Y).</summary>
        public bool CanRejoinLane =>
            IsPiloting && laneRejoinable && nearGate == null && Piloted.Ship != null &&
            Fleet.CanRejoin(Piloted.Ship, Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm, Clock.Jd);

        /// <summary>A warning shows long enough to answer: the clock slows until the countdown takes at least this many real seconds.</summary>
        const double WarningRealSeconds = 10;

        /// <summary>The interdictor last warned of (a new one warns afresh), and whether this hull left a lane part-way and may get back in.</summary>
        Ship warnedOf;
        bool laneRejoinable;

        bool rideBoostHeld, rideDropHeld;

        /// <summary>
        /// The scanners' look ahead along the lane: an interdictor's field picked up slows the clock so its countdown can be
        /// read and answered (Shift to surge through, Ctrl to drop out short of it); once it is behind, the ride's pace comes back.
        /// </summary>
        void UpdateRideThreat(Ship ship, double jd)
        {
            var threat = World.War?.ThreatAhead(ship, jd);
            RideThreat = threat;
            if (threat is LaneThreat t)
            {
                if (t.Lurker == warnedOf) return;
                warnedOf = t.Lurker;
                int i = Clock.WarpIndex;
                while (i > 0 && t.SecondsToPulse / SimClock.Levels[i].SimSecondsPerSecond < WarningRealSeconds) i--;
                Clock.SetWarp(i);
                ShowToast("SENSORS: interdiction field ahead on the " + ship.LaneLeg.Lane.Name + " lane, " + Math.Round(t.DistanceKm / 1000) + " thousand km" +
                          (t.Braced ? " (a raider's hull made out: braced for the pulse)" : "") +
                          "  ·  Shift surge through, Ctrl drop out, or ride on");
                return;
            }
            if (warnedOf == null) return;
            warnedOf = null;
            // Clear of it: back to the pace that gets the rest of the ride over within the minute.
            double simSeconds = Math.Max(0, ship.PhaseEndJd - jd) * Astro.SecondsPerDay;
            Clock.SetWarp(SimClock.WarpToCover(simSeconds, RideRealSeconds, Math.Max(0, rideWarpBefore)));
        }

        /// <summary>Shift: the rest of the ride surged through at <see cref="Fleet.SurgeFactor"/> times the pace, the boost charge spent on it.</summary>
        void SurgeThroughLane(Ship ship)
        {
            if (!Fleet.SurgeRide(ship, Clock.Jd))
            {
                if (ship.LaneSurged) ShowToast("Already surging: the drive has nothing more to give on this lane");
                return;
            }
            ship.Combat.BoostFraction = 0;
            fovKick = GateFovKick * 0.6f;
            ShowToast("SURGE: the drive pours everything into the lane, " + Fleet.SurgeFactor.ToString("0.#") + "x the pace" +
                      (RideThreat.HasValue ? " -- through the field before the pulse closes, with luck" : ""));
        }

        /// <summary>Ctrl: off the lane right here, on the pilot's own terms, coasting out at the hull's own top speed.</summary>
        void DropOutOfLane(Ship ship)
        {
            double jd = Clock.Jd;
            var hop = ship.LaneLeg;
            var facing = pilot.Forward;
            bool threatened = RideThreat.HasValue;
            Fleet.DropOut(ship, jd, facing);
            EndRide();
            pilotFrameBody = ship.Orbit.Body;
            var tuning = PilotTuning.Of(ship.Spec.Class);
            var up = pilot.Up;
            pilot = PilotState.At(ship.Orbit.OffsetAt(jd), facing * tuning.MaxSpeed, facing);
            var level = up - facing * DVec3.Dot(up, facing);
            if (level.LengthSquared > 1e-9) pilot.Up = level.Normalized;
            ResetPilotAim();
            insideRampGate = false;
            laneRejoinable = true;
            LaneExitFade = 1;
            fovKick = GateFovKick * 0.5f;
            ShowToast("Dropped out of the " + hop.Lane.Name + " lane" + (threatened ? " short of the field" : "") +
                      "  (Y to get back in where you left it)");
        }

        /// <summary>Y, out of a lane near where the hull left it: back in, easing onto the lane line, and on to the far ramp.</summary>
        void UpdateLaneRejoin(Ship ship)
        {
            if (!Input.ActivateGate || !CanRejoinLane) return;
            if (!Fleet.RejoinLane(ship, Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm, Clock.Jd)) return;
            laneRejoinable = false;
            SalvageApproach = null;
            BeginRide(ship, false);
            fovKick = GateFovKick;
            ShowToast("Back into the " + ship.LaneLeg.Lane.Name + " lane, bound for " + Fleet.Lanes.EndName(ship.LaneLeg.Lane, ship.LaneLeg.To));
        }

        /// <summary>Where the hull was last frame, and in which system, for flying through a gate spur's ring (U.SPC-T379); null after a ride or a jump.</summary>
        DVec3? lastSpurCheckHelio;
        StarSystem lastSpurCheckSystem;

        /// <summary>
        /// The gate spur ring the hull has just flown through this frame, heading along the spur (U.SPC-T379), or null.
        /// A spur is joined by threading its ring the way it runs rather than by being inside it, so a hull just come out
        /// of a gate (right beside the spur's ring there) or just brought off at its ramp is not swept back in.
        /// </summary>
        LaneHop? SpurRingFlownThrough(Hyperlanes lanes, DVec3 helio)
        {
            var before = lastSpurCheckSystem == ActiveSystem ? lastSpurCheckHelio : null;
            lastSpurCheckHelio = helio;
            lastSpurCheckSystem = ActiveSystem;
            if (!before.HasValue || lanes.GateSpurs.Count == 0) return null;
            var from = before.Value;
            // A jump across the system in one frame (a ride's end, a teleport) is not a flight through anything.
            if ((helio - from).Length > Hyperlanes.GateRadiusKm * 4) return null;
            return lanes.GateSpurRingCrossed(from, helio, Clock.Jd);
        }

        /// <summary>What the war has to say about the lane ridden (a pulse slipped, or one that caught the hull), as toasts.</summary>
        void ShowWarNotices()
        {
            var notices = World.War?.TakePilotNotices();
            if (notices != null && notices.Count > 0) ShowToast(string.Join("   ·   ", notices));
        }

        /// <summary>Flying in through an on-ramp's ring gate, out of any fight or salvage, starts a ride.</summary>
        void UpdateLaneEntry(Ship ship)
        {
            var lanes = Fleet.Lanes;
            if (lanes == null) return;
            var helio = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var hop = lanes.OnRampAt(helio, Clock.Jd);
            bool inside = hop.HasValue;
            bool enter = inside && !insideRampGate;
            insideRampGate = inside;
            // Not inside a lane's ramp: perhaps through the ring at either end of a spur to a jump gate (U.SPC-T379).
            var spur = SpurRingFlownThrough(lanes, helio);
            if (!enter && spur.HasValue)
            {
                hop = spur;
                enter = true;
            }
            if (!enter || ship.Battle != null || Salvage != null) return;
            Fleet.JoinLane(ship, hop.Value, helio, Clock.Jd);
            SalvageApproach = null;
            BeginRide(ship, true);
            var lane = hop.Value.Lane;
            ShowToast("Into the " + lane.Name + (lane.IsGateSpur ? " spur" : " lane") + ", bound for " + lanes.EndName(lane, hop.Value.To) +
                      (lane.IsGateSpur && hop.Value.To == lane.B ? "  (Y at the gate to jump, P hands the ride to the crew)"
                          : "  (number keys pick the off-ramp's exit, P hands the ride to the crew)"));
        }

        /// <summary>Sets the helm up for a ride the hull has just joined (flying in, carrying on at a junction, or taken mid-ride).</summary>
        void BeginRide(Ship ship, bool throughGate)
        {
            var hop = ship.LaneLeg;
            // Into a lane, an FTL charge is lost (U.SPC-T417).
            StopFtl();
            if (rideWarpBefore < 0) rideWarpBefore = Clock.WarpIndex;
            double simSeconds = Math.Max(0, ship.PhaseEndJd - Clock.Jd) * Astro.SecondsPerDay;
            Clock.SetWarp(SimClock.WarpToCover(simSeconds, RideRealSeconds, rideWarpBefore));
            RideOnward.Clear();
            RideOnward.AddRange(Fleet.Lanes.OnwardHops(hop.To, hop.Lane));
            RideChoice = -1;
            warnedOf = null;
            RideThreat = null;
            laneRejoinable = false;
            // A key already held as the ride starts (boosting in through the ring) is not a press.
            rideBoostHeld = rideDropHeld = true;
            pilotFrameBody = hop.From;
            pilotCruise = default;
            pilotBoosting = false;
            nearGate = null;
            lastSpurCheckHelio = null;
            if (throughGate) GateEffect(hop.Lane, hop.From);
        }

        /// <summary>The ride goes on, or is over: the clock back to the pace it had before.</summary>
        void EndRide()
        {
            if (rideWarpBefore >= 0) Clock.SetWarp(rideWarpBefore);
            rideWarpBefore = -1;
            RideOnward.Clear();
            RideChoice = -1;
            LaneRideSpeed = 0;
            RideThreat = null;
            warnedOf = null;
            lastSpurCheckHelio = null;
        }

        void GateEffect(Lane lane, int at)
        {
            GateFlash = 1;
            fovKick = GateFovKick;
            laneView?.Flash(lane, at);
        }

        /// <summary>
        /// A frame of the ride: the hull goes where its lane has it (so the sensors, the law and the HUD see it
        /// there), the number keys pick the off-ramp's exit, and at the far ramp it comes off or carries on.
        /// </summary>
        void UpdateLaneRide(Ship ship)
        {
            double jd = Clock.Jd;
            if (ship.Battle != null)
            {
                // Pulled into a fight mid-ride (an interdiction pulse, U.SPC-T42): off the lane where the hull is, and the
                // fight is flown from there, at the pace of a fight; out of it, the hull can get back into the lane.
                var at = Fleet.StateOf(ship, jd).HelioKm;
                EndRide();
                pilotFrameBody = Catalog.Root.Index;
                pilotOffsetKm = at - Ephemeris.Helio(pilotFrameBody);
                PinWherePiloted(ship);
                EnterPiloting(Piloted);
                laneRejoinable = true;
                GateFlash = 0.8f;
                fovKick = GateFovKick;
                LaneExitFade = 1;
                return;
            }
            UpdateRideThreat(ship, jd);
            bool boost = Input.Boost, drop = Input.Slow;
            bool boostPressed = boost && !rideBoostHeld, dropPressed = drop && !rideDropHeld;
            rideBoostHeld = boost;
            rideDropHeld = drop;
            if (dropPressed)
            {
                DropOutOfLane(ship);
                return;
            }
            if (boostPressed) SurgeThroughLane(ship);
            if (Input.NumberKey == 1 && RideChoice != -1)
            {
                RideChoice = -1;
                ShowToast("At the off-ramp: come off at " + Fleet.Lanes.EndName(ship.LaneLeg.Lane, ship.LaneLeg.To));
            }
            else if (Input.NumberKey >= 2 && Input.NumberKey - 2 < RideOnward.Count)
            {
                RideChoice = Input.NumberKey - 2;
                var next = RideOnward[RideChoice];
                ShowToast("At the off-ramp: on along the " + next.Lane.Name + (next.Lane.IsGateSpur ? " spur" : " lane") + " to " +
                          Fleet.Lanes.EndName(next.Lane, next.To) + "  (1 to come off instead)");
            }

            if (Fleet.RideOver(ship, jd))
            {
                ReachRamp(ship);
                return;
            }

            var st = Fleet.StateOf(ship, jd);
            pilotOffsetKm = st.HelioKm - Ephemeris.Helio(pilotFrameBody);
            pilotVelKmS = st.Facing * st.SpeedKmS;
            pilot.Forward = st.Facing;
            var level = pilot.Up - st.Facing * DVec3.Dot(pilot.Up, st.Facing);
            pilot.Up = level.LengthSquared > 1e-9 ? level.Normalized : PilotState.LevelUp(st.Facing);
            pilot.AngularVelocity = DVec3.Zero;
            ResetPilotAim();
            double span = Math.Max(ship.PhaseEndJd - ship.PhaseStartJd, 1e-9);
            double u = Math.Max(0, Math.Min(1, (jd - ship.PhaseStartJd) / span));
            LaneRideSpeed = (float)((1 - Math.Cos(Astro.TwoPi * u)) * 0.5);
            // A rider's drive is lit (as every rider's is) for whoever is watching.
            World.Sensors?.UpdatePilot(ship, st.HelioKm, true, jd);
        }

        /// <summary>The far ramp: on along the lane picked, or off through the gate into the system there, coasting out at the hull's own speed.</summary>
        void ReachRamp(Ship ship)
        {
            var hop = ship.LaneLeg;
            double jd = Clock.Jd;
            LaneHop? next = RideChoice >= 0 && RideChoice < RideOnward.Count ? RideOnward[RideChoice] : (LaneHop?)null;
            var facing = pilot.Forward;
            Fleet.RampReached(ship, next, facing, jd);
            GateEffect(hop.Lane, hop.To);
            if (next is LaneHop onward)
            {
                BeginRide(ship, false);
                ShowToast("On along the " + onward.Lane.Name + (onward.Lane.IsGateSpur ? " spur" : " lane") + ", bound for " +
                          Fleet.Lanes.EndName(onward.Lane, onward.To));
                return;
            }
            EndRide();
            pilotFrameBody = hop.To;
            // Out of the gate still moving: flight assist brings the hull down from there.
            var tuning = PilotTuning.Of(ship.Spec.Class);
            var up = pilot.Up;
            pilot = PilotState.At(ship.Orbit.OffsetAt(jd), facing * tuning.MaxSpeed, facing);
            var level = up - facing * DVec3.Dot(up, facing);
            if (level.LengthSquared > 1e-9) pilot.Up = level.Normalized;
            ResetPilotAim();
            insideRampGate = true;
            LaneExitFade = 1;
            // A spur's far end is at its gate, in range to open it (U.SPC-T379).
            if (hop.Lane.IsGateSpur && hop.To == hop.Lane.B)
                ShowToast("Off the " + hop.Lane.Name + " spur at " + hop.Lane.Gate.Name.ToUpperInvariant() +
                          (hop.Lane.Gate.Destination != null ? ", to " + hop.Lane.Gate.Destination.Name.ToUpperInvariant() + " (Y to jump)" : ""));
            else ShowToast("Off the " + hop.Lane.Name + (hop.Lane.IsGateSpur ? " spur" : " lane") + " at " + Fleet.Lanes.EndName(hop.Lane, hop.To));
        }
    }
}
