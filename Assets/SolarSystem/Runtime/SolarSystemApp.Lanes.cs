using System;
using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem
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
    /// </summary>
    public sealed partial class SolarSystemApp
    {
        /// <summary>Whether the piloted hull is riding a hyperlane right now.</summary>
        public bool RidingLane => IsPiloting && Piloted.Ship != null && Piloted.Ship.Phase == ShipPhase.Lane;

        /// <summary>The lanes the ride's off-ramp leads on to (keys 2 and up), and the one picked (-1: come off, key 1).</summary>
        public List<LaneHop> RideOnward { get; } = new List<LaneHop>();
        public int RideChoice { get; private set; } = -1;

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

        /// <summary>Flying in through an on-ramp's ring gate, out of any fight or salvage, starts a ride.</summary>
        void UpdateLaneEntry(Ship ship)
        {
            var lanes = Fleet.Lanes;
            if (lanes == null) return;
            var helio = Ephemeris.Helio(pilotFrameBody) + pilotOffsetKm;
            var hop = lanes.OnRampAt(helio, Clock.Jd);
            bool inside = hop.HasValue;
            bool enter = inside && !insideRampGate && ship.Battle == null && Salvage == null;
            insideRampGate = inside;
            if (!enter) return;
            Fleet.JoinLane(ship, hop.Value, helio, Clock.Jd);
            SalvageApproach = null;
            BeginRide(ship, true);
            ShowToast("Into the " + hop.Value.Lane.Name + " lane, bound for " + Catalog[hop.Value.To].Name +
                      "  (number keys pick the off-ramp's exit, P hands the ride to the crew)");
        }

        /// <summary>Sets the helm up for a ride the hull has just joined (flying in, carrying on at a junction, or taken mid-ride).</summary>
        void BeginRide(Ship ship, bool throughGate)
        {
            var hop = ship.LaneLeg;
            if (rideWarpBefore < 0) rideWarpBefore = Clock.WarpIndex;
            double simSeconds = Math.Max(0, ship.PhaseEndJd - Clock.Jd) * Astro.SecondsPerDay;
            Clock.SetWarp(SimClock.WarpToCover(simSeconds, RideRealSeconds, rideWarpBefore));
            RideOnward.Clear();
            RideOnward.AddRange(Fleet.Lanes.OnwardHops(hop.To, hop.Lane));
            RideChoice = -1;
            pilotFrameBody = hop.From;
            pilotCruise = default;
            pilotBoosting = false;
            nearGate = null;
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
                // Pulled into a fight mid-ride: off the lane where the hull is, and the fight is flown from there.
                var at = Fleet.StateOf(ship, jd).HelioKm;
                EndRide();
                pilotFrameBody = Catalog.Root.Index;
                pilotOffsetKm = at - Ephemeris.Helio(pilotFrameBody);
                PinWherePiloted(ship);
                EnterPiloting(Piloted);
                return;
            }
            if (Input.NumberKey == 1 && RideChoice != -1)
            {
                RideChoice = -1;
                ShowToast("At the off-ramp: come off at " + Catalog[ship.LaneLeg.To].Name);
            }
            else if (Input.NumberKey >= 2 && Input.NumberKey - 2 < RideOnward.Count)
            {
                RideChoice = Input.NumberKey - 2;
                var next = RideOnward[RideChoice];
                ShowToast("At the off-ramp: on along the " + next.Lane.Name + " lane to " + Catalog[next.To].Name + "  (1 to come off instead)");
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
            pilotAim = pilot.Forward;
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
                ShowToast("On along the " + onward.Lane.Name + " lane, bound for " + Catalog[onward.To].Name);
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
            pilotAim = pilot.Forward;
            insideRampGate = true;
            LaneExitFade = 1;
            ShowToast("Off the " + hop.Lane.Name + " lane at " + Catalog[hop.To].Name);
        }
    }
}
