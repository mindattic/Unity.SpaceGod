using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// Every ship in the system and its schedule. Ships alternate between parking in a circular
    /// orbit (real Keplerian rate for the body's GM) and brachistochrone transits: constant thrust
    /// toward the destination, flip at the midpoint, constant braking. Each transit is planned
    /// once at departure as an intercept against where the destination will be on arrival, so the
    /// whole schedule is deterministic and can be evaluated at any time. A hop within one planet's
    /// system is flown in that planet's frame, which carries the ship along the planet's orbit.
    /// </summary>
    public sealed class Fleet
    {
        public IReadOnlyList<Ship> Ships => ships;
        public IReadOnlyList<Faction> Factions => factions;

        readonly Ephemeris ephemeris;
        readonly BodyCatalog catalog;
        readonly List<Ship> ships = new List<Ship>();
        readonly List<Faction> factions = new List<Faction>();
        readonly Random rng;

        /// <summary>Transits are routed around the Sun at this distance.</summary>
        public const double SunKeepOutKm = 0.15 * Astro.AuKm;

        /// <summary>Hops within a planet's system are routed around the planet at this many radii.</summary>
        public const double PlanetKeepOutRadii = 2.0;

        /// <summary>A ship waits for its parking orbit to face the way out only if that takes less than this (days).</summary>
        public const double MaxFacingWaitDays = 2.0;

        public Fleet(Ephemeris ephemeris, int seed)
        {
            this.ephemeris = ephemeris;
            catalog = ephemeris.Catalog;
            rng = new Random(seed);
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>The hyperlane network; null for the classic all-brachistochrone fleet.</summary>
        public Hyperlanes Lanes;

        /// <summary>Called with each event's date before it is processed, so the world can catch up to it.</summary>
        public Action<double> BeforeEvent;

        /// <summary>Called when a ship reaches its destination's parking orbit, before it picks the next one.</summary>
        public Action<Ship, double> Arrived;

        /// <summary>Picks a ship's next destination body (never the one it is at), or returns -1 for the built-in choice.</summary>
        public Func<Ship, int, double, int> ChooseNext;

        /// <summary>Overrides how long a ship lingers in port, given the default; null keeps the default.</summary>
        public Func<Ship, double, double> Dwell;

        /// <summary>Ships that run their own schedule (not escorts, not destroyed, not held by something else).</summary>
        public Func<Ship, bool> IsHeld;

        /// <summary>Docking rings: ships that <see cref="Lands"/> reach a ringed world through one of its rings.</summary>
        public readonly List<DockingRing> Rings = new List<DockingRing>();

        /// <summary>Which ships go down to the surface at ringed worlds (merchants); null for none.</summary>
        public Func<Ship, bool> Lands;

        /// <summary>A ship at a body and able to fight there: holding orbit, or waiting at its ring.</summary>
        public static bool IsAt(Ship ship, int body) =>
            (ship.Phase == ShipPhase.Parked || ship.Phase == ShipPhase.Queued) && ship.Orbit != null && ship.Orbit.Body == body;

        /// <summary>
        /// Advances every ship's schedule up to <paramref name="jd"/>, processing departures and
        /// arrivals in date order across the whole fleet so that what one ship does (buying out a
        /// market, say) is seen by the next.
        /// </summary>
        public void Update(double jd)
        {
            for (int guard = 0; guard < 100000; guard++)
            {
                Ship next = null;
                double when = jd;
                foreach (var ship in ships)
                {
                    if (!RunsSchedule(ship)) continue;
                    if (ship.PhaseEndJd <= when)
                    {
                        when = ship.PhaseEndJd;
                        next = ship;
                    }
                }
                if (next == null) return;
                BeforeEvent?.Invoke(when);
                AdvancePhase(next);
            }
        }

        bool RunsSchedule(Ship ship) => !ship.IsEscort && !ship.Destroyed && (IsHeld == null || !IsHeld(ship));

        /// <summary>State at <paramref name="jd"/>; parked ships use the ephemeris' current frame, which must be at the same date.</summary>
        public ShipState StateOf(Ship ship, double jd)
        {
            var s = ship.Controller;
            if (s.Phase == ShipPhase.Parked)
            {
                var helio = ephemeris.Helio(s.Orbit.Body) + s.Orbit.OffsetAt(jd);
                return new ShipState(helio, s.Orbit.TangentAt(jd), s.Orbit.SpeedKmS, false, 0);
            }
            if (s.Phase == ShipPhase.Queued || s.Planetside) return RingState(s, jd);
            if (s.Phase == ShipPhase.Lurk || s.Phase == ShipPhase.Adrift)
            {
                // Slide out of the lane over the first couple of hours rather than jump.
                double ease = Astro.SmoothStep(0, 0.08, jd - s.PhaseStartJd);
                var held = LanePoint(s, jd, s.LaneHold) + s.LaneHoldSide * ease;
                var along = LanePoint(s, jd, Math.Min(1, s.LaneHold + 1e-4)) - LanePoint(s, jd, Math.Max(0, s.LaneHold - 1e-4));
                return new ShipState(held, along.LengthSquared > 0 ? along.Normalized : DVec3.UnitX, 0, false, s.LaneHold);
            }
            if (s.Phase == ShipPhase.Lane)
            {
                double progress = LaneProgress(s, jd);
                var here = LanePoint(s, jd, progress);
                const double h = 1e-3;
                var ahead = LanePoint(s, jd + h, LaneProgress(s, jd + h));
                var behind = LanePoint(s, jd - h, LaneProgress(s, jd - h));
                var v = (ahead - behind) / (2 * h);
                var facing = v.LengthSquared > 0 ? v.Normalized : (ahead - here).Normalized;
                return new ShipState(here, facing, v.Length / Astro.SecondsPerDay, true, progress);
            }

            double total = s.PhaseEndJd - s.PhaseStartJd;
            double tau = Math.Max(0, Math.Min(total, jd - s.PhaseStartJd));
            double half = total * 0.5;
            double dist = tau < half
                ? 0.5 * s.AccelKmPerDay2 * tau * tau
                : s.PathLength - 0.5 * s.AccelKmPerDay2 * (total - tau) * (total - tau);

            PointAtDistance(s, dist, out var pos, out var dir);
            var burn = dir * (s.AccelKmPerDay2 * Math.Min(tau, total - tau));
            var velocity = burn + DriftVelocity(s, tau);
            // The drive points along the thrust: the flip-and-burn plus the steady velocity-matching push.
            var thrust = (tau < half ? dir : -dir) * s.AccelKmPerDay2 + DriftAcceleration(s);
            var at = ephemeris.HelioAt(s.FrameBody, jd) + pos + Drift(s, tau);
            return new ShipState(at, thrust.Normalized, velocity.Length / Astro.SecondsPerDay, true,
                s.PathLength > 0 ? dist / s.PathLength : 1);
        }

        /// <summary>
        /// A ship at a docking ring, in the body's turning frame: queued (easing in from its holding
        /// point to the ring over the wait), descending (straight down under the ring to the city,
        /// heating as it passes the entry interface), landed, or climbing back up.
        /// </summary>
        ShipState RingState(Ship s, double jd)
        {
            var ring = s.Ring;
            var centre = ephemeris.Helio(ring.Body);
            double total = Math.Max(s.PhaseEndJd - s.PhaseStartJd, 1e-9);
            double u = Math.Max(0, Math.Min(1, (jd - s.PhaseStartJd) / total));
            if (s.Phase == ShipPhase.Queued)
            {
                double e = Astro.SmoothStep(0, 1, u);
                var from = s.HoldPoint.OffsetAt(jd);
                var to = ring.RingPoint.OffsetAt(jd);
                var p = DVec3.Lerp(from, to, e);
                var drift = (to - from).Length * 6 * u * (1 - u) / (total * Astro.SecondsPerDay);
                return new ShipState(centre + p, (centre - (centre + p)).Normalized, drift, false, 0);
            }
            var up = ring.Up(jd);
            double r = RingRadiusAt(s, u);
            double rate = (RingRadiusAt(s, Math.Min(1, u + 1e-4)) - RingRadiusAt(s, Math.Max(0, u - 1e-4))) / (2e-4 * total * Astro.SecondsPerDay);
            float heat = 0;
            if (s.Phase != ShipPhase.Landed)
            {
                double altitude = r - ring.SurfaceKm;
                // Brightest on the way down, a little after the interface; a gentler glow climbing out.
                double depth = 1 - altitude / Math.Max(ring.EntryInterfaceKm, 1);
                heat = (float)(Math.Max(0, Math.Min(1, depth * 1.6)) * Math.Max(0, Math.Min(1, altitude / (ring.EntryInterfaceKm * 0.15))) * (s.Phase == ShipPhase.Descent ? 1 : 0.35));
            }
            // Tail-first like a landing rocket: the drive points at the ground, braking on the way
            // down and lifting on the way up.
            var facing = up;
            return new ShipState(centre + up * r, facing, Math.Abs(rate), s.Phase != ShipPhase.Landed, u, heat);
        }

        /// <summary>Distance from the ringed body's centre during a descent or climb at fraction u.</summary>
        static double RingRadiusAt(Ship s, double u)
        {
            var ring = s.Ring;
            if (s.Phase == ShipPhase.Landed) return ring.SurfaceKm;
            // Fast through the empty upper part, slow and careful through the air near the ground.
            double e = s.Phase == ShipPhase.Descent ? 1 - Math.Pow(1 - u, 2.2) : Math.Pow(u, 2.2);
            double top = ring.RadiusKm, bottom = ring.SurfaceKm;
            return s.Phase == ShipPhase.Descent ? top + (bottom - top) * e : bottom + (top - bottom) * e;
        }

        /// <summary>Distance fraction along the lane at a date (eased in and out).</summary>
        public static double LaneProgress(Ship s, double jd) =>
            s.LaneStartProgress + (s.LaneEndProgress - s.LaneStartProgress) * Hyperlanes.Progress((jd - s.PhaseStartJd) / Math.Max(s.PhaseEndJd - s.PhaseStartJd, 1e-9));

        /// <summary>Heliocentric position on the ship's lane at a date and distance fraction.</summary>
        public DVec3 LanePoint(Ship s, double jd, double progress)
        {
            var a = ephemeris.HelioAt(s.LaneLeg.From, jd) + s.LaneFromOffset;
            var b = ephemeris.HelioAt(s.LaneLeg.To, jd) + s.LaneToOffset;
            return Lanes.CurveKm(s.LaneLeg.Lane, a, b, progress, jd);
        }

        /// <summary>
        /// Position relative to the ship's <see cref="Ship.FrameBody"/> at a fraction of its transit
        /// (by distance along the path, like <see cref="ShipState.Progress"/>).
        /// </summary>
        public static DVec3 TransitOffset(Ship s, double progress)
        {
            double dist = Math.Max(0, Math.Min(1, progress)) * s.PathLength;
            PointAtDistance(s, dist, out var pos, out _);
            double total = s.PhaseEndJd - s.PhaseStartJd;
            double tau = s.AccelKmPerDay2 <= 0 ? progress * total
                : dist < s.PathLength * 0.5
                    ? Math.Sqrt(2 * dist / s.AccelKmPerDay2)
                    : total - Math.Sqrt(Math.Max(0, 2 * (s.PathLength - dist) / s.AccelKmPerDay2));
            return pos + Drift(s, tau);
        }

        /// <summary>Position relative to the ship's <see cref="Ship.FrameBody"/> a time into its transit (days).</summary>
        public static DVec3 TransitOffsetAt(Ship s, double tau)
        {
            double total = s.PhaseEndJd - s.PhaseStartJd;
            tau = Math.Max(0, Math.Min(total, tau));
            double dist = tau < total * 0.5
                ? 0.5 * s.AccelKmPerDay2 * tau * tau
                : s.PathLength - 0.5 * s.AccelKmPerDay2 * (total - tau) * (total - tau);
            PointAtDistance(s, dist, out var pos, out _);
            return pos + Drift(s, tau);
        }

        // A steady acceleration (v1 - v0) / T on top of the flip-and-burn takes the ship from its
        // departure velocity to its arrival velocity; its displacement is v0·t + (v1 - v0)·t² / 2T.
        static DVec3 Drift(Ship s, double tau)
        {
            double total = s.PhaseEndJd - s.PhaseStartJd;
            return s.DepartureVelocity * tau + (s.ArrivalVelocity - s.DepartureVelocity) * (tau * tau / (2 * total));
        }

        static DVec3 DriftVelocity(Ship s, double tau) =>
            DVec3.Lerp(s.DepartureVelocity, s.ArrivalVelocity, tau / (s.PhaseEndJd - s.PhaseStartJd));

        static DVec3 DriftAcceleration(Ship s) =>
            (s.ArrivalVelocity - s.DepartureVelocity) / (s.PhaseEndJd - s.PhaseStartJd);

        /// <summary>
        /// Position (relative to the ship's <see cref="Ship.FrameBody"/>, before drift) and travel
        /// direction at a distance along a transiting ship's flip-and-burn path.
        /// </summary>
        public static void PointAtDistance(Ship s, double dist, out DVec3 pos, out DVec3 dir)
        {
            var path = s.Path;
            for (int i = 1; i < path.Length; i++)
            {
                if (dist <= s.PathCumulative[i] || i == path.Length - 1)
                {
                    double segStart = s.PathCumulative[i - 1];
                    double segLen = s.PathCumulative[i] - segStart;
                    double t = segLen > 0 ? Math.Max(0, Math.Min(1, (dist - segStart) / segLen)) : 1;
                    pos = DVec3.Lerp(path[i - 1], path[i], t);
                    dir = (path[i] - path[i - 1]).Normalized;
                    return;
                }
            }
            pos = path[path.Length - 1];
            dir = DVec3.UnitX;
        }

        /// <summary>Time remaining in the current phase, days.</summary>
        public static double RemainingDays(Ship ship, double jd) => Math.Max(0, ship.Controller.PhaseEndJd - jd);

        void AdvancePhase(Ship ship)
        {
            if (ship.Phase == ShipPhase.Parked)
            {
                // Holding position (a siege, a refit, an ambush): decide again rather than fly to itself.
                if (ship.DestinationBody == ship.Orbit.Body) ScheduleNext(ship, ship.PhaseEndJd);
                else Depart(ship);
            }
            else if (ship.Phase == ShipPhase.Lane) EndLane(ship);
            else if (ship.Phase == ShipPhase.Lurk || ship.Phase == ShipPhase.Adrift) ResumeLane(ship, ship.PhaseEndJd);
            else if (ship.Phase == ShipPhase.Queued) BeginDescent(ship);
            else if (ship.Phase == ShipPhase.Descent) Arrive(ship, landed: true);
            else if (ship.Phase == ShipPhase.Landed)
            {
                if (ship.DestinationBody == ship.Ring.Body) ScheduleNext(ship, ship.PhaseEndJd);
                else BeginAscent(ship);
            }
            else if (ship.Phase == ShipPhase.Ascent) Depart(ship);
            else if (ship.Journey != null && ship.JourneyIndex < ship.Journey.Count && ship.Orbit.Fixed) StartLane(ship, ship.PhaseEndJd);
            else if (ship.Ring != null && ship.Orbit == ship.HoldPoint) JoinQueue(ship);
            else Arrive(ship);
        }

        // ---------------------------------------------------------------- docking rings

        /// <summary>The way a ship arrives at a body: in orbit, or to the holding point of the ring with the shortest queue.</summary>
        ParkingOrbit ArrivalFor(Ship ship, int body)
        {
            ship.Ring = null;
            ship.HoldPoint = null;
            if (Lands == null || !Lands(ship)) return NewParkingOrbit(body, ship);
            DockingRing best = null;
            foreach (var r in Rings)
                if (r.Body == body && (best == null || r.NextEntryJd < best.NextEntryJd)) best = r;
            if (best == null) return NewParkingOrbit(body, ship);
            ship.Ring = best;
            ship.HoldPoint = best.HoldPoint(ship);
            return ship.HoldPoint;
        }

        /// <summary>Minimum time from the holding point to the ring (an unhurried approach).</summary>
        public const double MinQueueDays = 12.0 / 1440;

        /// <summary>Arrived off the ring: take the next free slot to descend (rings clear one ship per interval).</summary>
        void JoinQueue(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            var ring = ship.Ring;
            double entry = Math.Max(jd + MinQueueDays, ring.NextEntryJd);
            ring.NextEntryJd = entry + ring.EntryIntervalDays;
            ship.Phase = ShipPhase.Queued;
            ship.Path = null;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = entry;
        }

        void BeginDescent(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            ship.Orbit = ship.Ring.RingPoint;
            ship.Phase = ShipPhase.Descent;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + ship.Ring.DescentDays;
        }

        void BeginAscent(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            ship.Phase = ShipPhase.Ascent;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + ship.Ring.DescentDays;
        }

        /// <summary>Days a ship landing at a ringed world adds for the queue, the descent and the climb out.</summary>
        double RingOverheadDays(Ship ship, int body, double arriveJd)
        {
            if (Lands == null || !Lands(ship)) return 0;
            double best = double.MaxValue;
            foreach (var r in Rings)
                if (r.Body == body) best = Math.Min(best, Math.Max(0, r.NextEntryJd - arriveJd) + MinQueueDays + 2 * r.DescentDays);
            return best == double.MaxValue ? 0 : best;
        }

        /// <summary>
        /// Leaves port. A trip to another planetary system goes by hyperlane when there is one: first
        /// a burn to the first lane's ramp; otherwise (or locally) one burn straight to the destination.
        /// </summary>
        void Depart(Ship ship)
        {
            double departJd = ship.PhaseEndJd;
            int from = ship.Orbit.Body;
            int to = ship.DestinationBody;
            ship.FinalBody = to;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            if (Lanes != null)
            {
                int fromSystem = Lanes.SystemOf(from), toSystem = Lanes.SystemOf(to);
                if (fromSystem != toSystem) ship.Journey = Lanes.Route(fromSystem, toSystem, departJd);
            }
            if (ship.Journey != null)
            {
                var hop = ship.Journey[0];
                Burn(ship, departJd, hop.From, () => RampOrbit(hop, hop.From, departJd));
            }
            else Burn(ship, departJd, to, () => ArrivalFor(ship, to));
        }

        /// <summary>A hyperlane ramp as a fixed point attached to its planet.</summary>
        ParkingOrbit RampOrbit(LaneHop hop, int system, double jd) => FixedPoint(system, Lanes.RampOffset(hop.Lane, system, jd + 0.4));

        static ParkingOrbit FixedPoint(int body, DVec3 offset)
        {
            var u = offset.Normalized;
            var v = DVec3.Cross(Math.Abs(u.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX, u).Normalized;
            return new ParkingOrbit { Body = body, RadiusKm = offset.Length, U = u, V = v, RateRadPerDay = 0, Fixed = true };
        }

        /// <summary>Joins the lane at its ramp (where the burn has just brought the ship).</summary>
        void StartLane(Ship ship, double jd)
        {
            var hop = ship.Journey[ship.JourneyIndex];
            ship.LaneLeg = hop;
            ship.LaneFromOffset = ship.Orbit.OffsetAt(jd);
            var a = ephemeris.HelioAt(hop.From, jd) + ship.LaneFromOffset;
            var estimate = Hyperlanes.RideDays(Lanes.LaneLengthKm(hop.Lane, jd));
            ship.LaneToOffset = Lanes.RampOffset(hop.Lane, hop.To, jd + estimate);
            var b = ephemeris.HelioAt(hop.To, jd) + ship.LaneToOffset;
            ship.LaneLengthKm = Lanes.CurveLengthKm(hop.Lane, a, b, jd);
            ship.DepartureOrbit = ship.Orbit;
            ship.OriginBody = hop.From;
            ship.FrameBody = 0;
            ship.Path = null;
            ship.Phase = ShipPhase.Lane;
            ship.LaneStartProgress = 0;
            ship.LaneEndProgress = ship.LurkLane == hop.Lane ? ship.LurkAt : 1;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + Hyperlanes.RideDays(ship.LaneLengthKm) * ship.LaneEndProgress;
        }

        /// <summary>Back into the lane from where the ship stopped (a lurk over, or an ambush survived), and on to its end.</summary>
        public void ResumeLane(Ship ship, double jd)
        {
            ship.Phase = ShipPhase.Lane;
            ship.LaneStartProgress = ship.LaneHold;
            ship.LaneEndProgress = 1;
            ship.LaneHoldSide = DVec3.Zero;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + Hyperlanes.RideDays(ship.LaneLengthKm) * Math.Max(1e-3, 1 - ship.LaneHold);
        }

        /// <summary>Drops out of the lane where the ship is, a little to one side (an interdiction pulse).</summary>
        public void PullOut(Ship ship, double jd, double progress, DVec3 side)
        {
            ship.Phase = ShipPhase.Adrift;
            ship.LaneHold = Math.Max(0, Math.Min(1, progress));
            ship.LaneHoldSide = side;
            ship.PhaseStartJd = jd;
            ship.PhaseEndJd = jd + 0.5;
        }

        /// <summary>A sideways offset (km, ecliptic) from a lane at a point, to wait just outside it.</summary>
        public DVec3 OffLane(Ship s, double jd, double progress, double km, int sign)
        {
            var along = LanePoint(s, jd, Math.Min(1, progress + 1e-3)) - LanePoint(s, jd, Math.Max(0, progress - 1e-3));
            var side = DVec3.Cross(along, DVec3.UnitZ);
            return side.LengthSquared > 0 ? side.Normalized * (km * sign) : DVec3.UnitX * km;
        }

        /// <summary>Leaves the lane at the far ramp: on to the next lane's ramp, or the last burn home.</summary>
        void EndLane(Ship ship)
        {
            double jd = ship.PhaseEndJd;
            var hop = ship.LaneLeg;
            if (ship.LaneEndProgress < 1)
            {
                // Stop part-way and wait just off the lane.
                ship.Phase = ShipPhase.Lurk;
                ship.LaneHold = ship.LaneEndProgress;
                ship.LaneHoldSide = OffLane(ship, jd, ship.LaneHold, 30000, ship.Id % 2 == 0 ? 1 : -1);
                ship.PhaseStartJd = jd;
                ship.PhaseEndJd = jd + ship.LurkDays;
                ship.LurkLane = null;
                return;
            }
            ship.Orbit = FixedPoint(hop.To, ship.LaneToOffset);
            ship.JourneyIndex++;
            if (ship.JourneyIndex < ship.Journey.Count)
            {
                var next = ship.Journey[ship.JourneyIndex];
                Burn(ship, jd, next.From, () => RampOrbit(next, next.From, jd));
            }
            else
            {
                int to = ship.FinalBody;
                Burn(ship, jd, to, () => ArrivalFor(ship, to));
            }
        }

        /// <summary>A flip-and-burn from the ship's current orbit (or ramp) to <paramref name="to"/>, arriving in the orbit the factory gives.</summary>
        void Burn(Ship ship, double departJd, int to, Func<ParkingOrbit> arrivalOrbit)
        {
            int from = ship.Orbit.Body;
            int frame = CommonFrame(from, to);
            double keepOut = FrameKeepOutKm(frame);
            var start = RelativeTo(frame, from, departJd) + ship.Orbit.OffsetAt(departJd);
            var startVelocity = VelocityRelativeTo(frame, from, departJd) + ship.Orbit.VelocityAt(departJd);
            // Match velocities at both ends unless that path runs into something the rest-to-rest
            // one in the frame avoids; if both are blocked, try another parking orbit plane.
            ParkingOrbit arrival = null;
            bool clear = false;
            for (int attempt = 0; attempt < 6 && !clear; attempt++)
            {
                arrival = arrivalOrbit();
                clear = Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, true)
                        || Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, false);
            }
            if (!clear) Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, true);

            ship.DepartureOrbit = ship.Orbit;
            ship.Orbit = arrival;
            ship.OriginBody = from;
            ship.Phase = ShipPhase.Transit;
            ship.FrameBody = frame;
            ship.PhaseStartJd = departJd;
        }

        /// <summary>
        /// How wide a transit flown in a frame gives its centre: <see cref="SunKeepOutKm"/> for a star
        /// (scaled for its luminosity, so Proxima's close planets stay reachable), a couple of radii
        /// for a planet, nothing for an empty barycentre (its stars are steered round instead).
        /// </summary>
        double FrameKeepOutKm(int frame)
        {
            var f = catalog[frame];
            if (f.IsStar) return SunKeepOutKm * Math.Sqrt(Math.Max(f.LuminositySolar, 1e-6));
            if (f.Kind == BodyKind.Barycentre) return 0;
            return f.RadiusKm * PlanetKeepOutRadii;
        }

        /// <summary>
        /// Plans the transit onto <paramref name="ship"/>. Returns false if the path would pass inside
        /// the keep-out around the frame's centre, or through either end's body or the planets they orbit.
        /// </summary>
        bool Plan(Ship ship, int frame, int from, int to, double departJd, DVec3 start, DVec3 startVelocity,
            ParkingOrbit arrival, double keepOut, bool matchVelocity)
        {
            double accel = ship.Spec.AccelG * Astro.GravityKmPerDay2;
            var v0 = matchVelocity ? startVelocity : DVec3.Zero;
            var v1 = DVec3.Zero;
            DVec3[] path = null;

            // For a flight time T, the path to where the target will be then, and how much longer
            // (positive) or shorter the drive needs to fly it than T.
            double Shortfall(double flightDays)
            {
                double arriveJd = departJd + flightDays;
                var target = RelativeTo(frame, to, arriveJd);
                // A fixed point keeps its own epoch: re-dating it would turn a point that rotates with
                // its body (a docking ring) to the wrong place.
                if (!arrival.Fixed)
                {
                    arrival.EpochJd = arriveJd;
                    arrival.PhaseAtEpoch = arrival.PhaseFacing((start - target).Normalized);
                }
                v1 = matchVelocity ? VelocityRelativeTo(frame, to, arriveJd) + arrival.VelocityAt(arriveJd) : DVec3.Zero;
                // The drift covers (v0 + v1)·T / 2 of the way; the flip-and-burn covers the rest.
                var shift = (v0 + v1) * (flightDays * 0.5);
                var end = target + arrival.OffsetAt(arriveJd) - shift;
                path = BuildPath(start, end, keepOut, Avoidances(frame, from, to, departJd, arriveJd, shift));
                return 2 * Math.Sqrt(PolylineLength(path) / accel) - flightDays;
            }

            // Intercept: bracket the flight time, then bisect. The drive can always outrun the
            // target eventually, so the shortfall turns negative for a long enough flight.
            double lo = 1e-4;
            double hi = Math.Max(2 * Math.Sqrt((RelativeTo(frame, to, departJd) - start).Length / accel), 2 * lo);
            for (int i = 0; i < 60 && Shortfall(hi) > 0; i++) hi *= 2;
            for (int i = 0; i < 60 && hi - lo > 1e-9 * hi; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Shortfall(mid) > 0) lo = mid;
                else hi = mid;
            }
            double flight = hi;
            Shortfall(flight);

            ship.DepartureVelocity = v0;
            ship.ArrivalVelocity = v1;
            ship.Path = path;
            ship.PathCumulative = Cumulative(path);
            ship.PathLength = ship.PathCumulative[path.Length - 1];
            // The path was built for exactly this arrival time; trim the drive (by a hair, once
            // converged) so the brachistochrone lands on it then.
            ship.AccelKmPerDay2 = 4 * ship.PathLength / (flight * flight);
            ship.PhaseStartJd = departJd;
            ship.PhaseEndJd = departJd + flight;

            return Clear(ship, frame, from, to, keepOut);
        }

        /// <summary>Samples a planned transit against the frame's keep-out and the bodies near its ends.</summary>
        bool Clear(Ship ship, int frame, int from, int to, double keepOut)
        {
            var obstacles = new List<int>();
            foreach (int end in new[] { from, to })
                for (int b = end; b >= 0 && b != frame; b = catalog[b].ParentIndex)
                    if (!obstacles.Contains(b)) obstacles.Add(b);

            // Bodies sit at the ends, where the ship is slow but a planet is passed in minutes: sample
            // the first and last few percent densely, and test each segment, not just its ends.
            const int samples = 384;
            double total = ship.PhaseEndJd - ship.PhaseStartJd;
            var previous = new DVec3[obstacles.Count];
            for (int k = 0; k <= samples; k++)
            {
                double u = (double)k / samples;
                double tau = total * (u < 0.25 ? 0.12 * u / 0.25 * (u / 0.25) : u > 0.75 ? 1 - 0.12 * ((1 - u) / 0.25) * ((1 - u) / 0.25) : 0.12 + (u - 0.25) / 0.5 * 0.76);
                var offset = TransitOffsetAt(ship, tau);
                if (k > 0 && k < samples && offset.Length < keepOut * 0.8) return false;
                double jd = ship.PhaseStartJd + tau;
                var helio = ephemeris.HelioAt(frame, jd) + offset;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    int b = obstacles[i];
                    var rel = helio - ephemeris.HelioAt(b, jd);
                    double limit = catalog[b].RadiusKm * 1.5;
                    if (k > 0 && k < samples && rel.Length < limit) return false;
                    if (k > 0 && SegmentDistance(previous[i], rel) < limit) return false;
                    previous[i] = rel;
                }
            }
            return true;
        }

        /// <summary>Closest approach to the origin of the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
        static double SegmentDistance(DVec3 a, DVec3 b)
        {
            var d = b - a;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(a, d) / len2)) : 0;
            return (a + d * t).Length;
        }

        void Arrive(Ship ship, bool landed = false)
        {
            double arriveJd = ship.PhaseEndJd;
            ship.Phase = landed ? ShipPhase.Landed : ShipPhase.Parked;
            ship.TripsCompleted++;
            ship.Path = null;
            ship.Journey = null;
            ship.JourneyIndex = 0;
            ship.PhaseStartJd = arriveJd;
            Arrived?.Invoke(ship, arriveJd);
            if (ship.Destroyed || (IsHeld != null && IsHeld(ship))) return;
            ScheduleNext(ship, arriveJd);
        }

        /// <summary>Picks the next destination and the departure time: dwell, then wait for the parking orbit to face the target.</summary>
        public void ScheduleNext(Ship ship, double nowJd)
        {
            // Mid-descent or mid-climb the schedule is fixed; a queued ship (after a fight, say) just
            // takes its turn again.
            if (ship.Phase == ShipPhase.Descent || ship.Phase == ShipPhase.Ascent) return;
            if (ship.Phase == ShipPhase.Queued)
            {
                ship.HoldPoint = FixedHold(ship, nowJd);
                ship.Orbit = ship.HoldPoint;
                double entry = Math.Max(nowJd + MinQueueDays, ship.Ring.NextEntryJd);
                ship.Ring.NextEntryJd = entry + ship.Ring.EntryIntervalDays;
                ship.PhaseStartJd = nowJd;
                ship.PhaseEndJd = entry;
                return;
            }
            int here = ship.Orbit.Body;
            int chosen = ChooseNext != null ? ChooseNext(ship, here, nowJd) : -1;
            double dwell = Dwell != null ? Dwell(ship, DwellDays(ship)) : DwellDays(ship);
            if (chosen == here)
            {
                ship.DestinationBody = here;
                ship.PhaseEndJd = nowJd + Math.Max(dwell, 0.05);
                return;
            }
            int next = chosen >= 0 ? chosen : ChooseDestination(ship, here, nowJd);
            ship.DestinationBody = next;
            if (ChooseNext == null) ship.Cargo = CargoFor(ship, here);

            double windowJd = nowJd + dwell;
            var toTarget = (FirstLegTarget(here, next, windowJd) - ephemeris.HelioAt(here, windowJd)).Normalized;
            double want = ship.Orbit.PhaseFacing(toTarget);
            double have = ship.Orbit.PhaseAt(windowJd);
            double delta = (want - have) % Astro.TwoPi;
            if (delta < 0) delta += Astro.TwoPi;
            // Around a station (no real gravity) or on a fixed point, the "orbit" barely turns: just go.
            double wait = ship.Orbit.RateRadPerDay > 0 && !ship.Orbit.Fixed ? delta / ship.Orbit.RateRadPerDay : 0;
            ship.PhaseEndJd = windowJd + (wait > MaxFacingWaitDays ? 0 : wait);
        }

        /// <summary>A queued ship's current spot, frozen into a new holding point to ease in from.</summary>
        ParkingOrbit FixedHold(Ship ship, double jd)
        {
            var at = RingState(ship, jd).HelioKm - ephemeris.Helio(ship.Ring.Body);
            var hold = ship.Ring.Point(at.Length);
            // Keep it on the ship's own parallel and meridian, not the ring's.
            var north = BodyOrientation.MapNorth(catalog[ship.Ring.Body]);
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, DVec3.Dot(at.Normalized, north))));
            double phase = Math.Atan2(DVec3.Dot(at, hold.V), DVec3.Dot(at, hold.U));
            hold.Center = north * (at.Length * Math.Sin(lat));
            hold.RadiusKm = at.Length * Math.Cos(lat);
            hold.EpochJd = jd;
            hold.PhaseAtEpoch = phase;
            return hold;
        }

        int ChooseDestination(Ship ship, int here, double jd)
        {
            switch (ship.Role)
            {
                case ShipRole.Patrol:
                    ship.RouteIndex = (ship.RouteIndex + 1) % ship.Route.Length;
                    if (ship.Route[ship.RouteIndex] == here)
                        ship.RouteIndex = (ship.RouteIndex + 1) % ship.Route.Length;
                    return ship.Route[ship.RouteIndex];

                case ShipRole.Miner:
                    if (here != ship.Ports[0]) return ship.Ports[0];
                    return ship.Ports[1 + rng.Next(ship.Ports.Length - 1)];

                default:
                {
                    // Prefer nearby ports: weight 1 / (0.3 AU + distance).
                    var herePos = ephemeris.HelioAt(here, jd);
                    double total = 0;
                    var weights = new double[ship.Ports.Length];
                    for (int i = 0; i < ship.Ports.Length; i++)
                    {
                        if (ship.Ports[i] == here) continue;
                        double au = (ephemeris.HelioAt(ship.Ports[i], jd) - herePos).Length / Astro.AuKm;
                        weights[i] = 1.0 / (0.3 + au);
                        total += weights[i];
                    }
                    double pick = rng.NextDouble() * total;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        pick -= weights[i];
                        if (weights[i] > 0 && pick <= 0) return ship.Ports[i];
                    }
                    return ship.Ports[0] == here ? ship.Ports[1] : ship.Ports[0];
                }
            }
        }

        double DwellDays(Ship ship)
        {
            switch (ship.Role)
            {
                case ShipRole.Patrol: return 1.0 + rng.NextDouble() * 3.0;
                case ShipRole.Courier: return 0.1 + rng.NextDouble() * 0.4;
                case ShipRole.Miner: return 1.0 + rng.NextDouble() * 2.0;
                default: return 0.5 + rng.NextDouble() * 1.5;
            }
        }

        ParkingOrbit NewParkingOrbit(int body, Ship ship)
        {
            var b = catalog[body];
            // Military ships hold high orbits; civilian traffic sits lower and more varied.
            double radii = ship.Spec.IsMilitary ? 4.0 + rng.NextDouble() * 3.0 : 2.5 + rng.NextDouble() * 4.5;
            double r = b.RadiusKm * radii;

            var normal = (b.SpinAxis + new DVec3(Jitter(0.12), Jitter(0.12), Jitter(0.12))).Normalized;
            var reference = Math.Abs(normal.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var u = DVec3.Cross(reference, normal).Normalized;
            var v = DVec3.Cross(normal, u);
            return new ParkingOrbit
            {
                Body = body,
                RadiusKm = r,
                U = u,
                V = v,
                RateRadPerDay = Kepler.CircularRate(b.Gm, r),
            };
        }

        double Jitter(double amount) => (rng.NextDouble() * 2 - 1) * amount;

        /// <summary>
        /// The frame a transit is flown in: the lowest body that both ends orbit within (or are), so
        /// Earth for Earth to the Moon, Jupiter for Io to Europa, the Sun between planetary systems.
        /// </summary>
        int CommonFrame(int a, int b)
        {
            for (int x = a; x >= 0; x = catalog[x].ParentIndex)
                for (int y = b; y >= 0; y = catalog[y].ParentIndex)
                    if (x == y) return x;
            return 0;
        }

        DVec3 RelativeTo(int frame, int body, double jd) =>
            frame == body ? DVec3.Zero : ephemeris.HelioAt(body, jd) - ephemeris.HelioAt(frame, jd);

        /// <summary>Velocity of <paramref name="body"/> relative to <paramref name="frame"/>, km/day (central difference).</summary>
        DVec3 VelocityRelativeTo(int frame, int body, double jd)
        {
            const double h = 1e-3;
            return (RelativeTo(frame, body, jd + h) - RelativeTo(frame, body, jd - h)) / (2 * h);
        }

        /// <summary>
        /// The planets that the ends' moons orbit, as spheres to steer around, in path coordinates
        /// (the arrival side is shifted like the path's end, since drift is added on top of the path).
        /// </summary>
        List<(DVec3 center, double radius)> Avoidances(int frame, int from, int to, double departJd, double arriveJd, DVec3 shift)
        {
            var list = new List<(DVec3, double)>();
            if (catalog[frame].Kind == BodyKind.Barycentre)
                foreach (int star in catalog.ChildrenOf(frame))
                    if (catalog[star].IsStar)
                        list.Add((RelativeTo(frame, star, 0.5 * (departJd + arriveJd)), FrameKeepOutKm(star)));
            for (int b = catalog[from].ParentIndex, moon = from; b > 0 && b != frame; moon = b, b = catalog[b].ParentIndex)
                list.Add((RelativeTo(frame, b, departJd), Berth(b, moon, departJd)));
            for (int b = catalog[to].ParentIndex, moon = to; b > 0 && b != frame; moon = b, b = catalog[b].ParentIndex)
                list.Add((RelativeTo(frame, b, arriveJd) - shift, Berth(b, moon, arriveJd)));
            return list;
        }

        /// <summary>
        /// How wide a ship leaving or joining a moon gives its planet: a good part of the moon system,
        /// so it never skims the world, but inside the moon's own orbit so the moon stays reachable.
        /// </summary>
        double Berth(int planet, int moon, double jd)
        {
            var p = catalog[planet];
            double wide = Math.Max(p.RadiusKm * 2.5, p.MoonReachKm * 0.3);
            double moonDistance = (ephemeris.HelioAt(moon, jd) - ephemeris.HelioAt(planet, jd)).Length;
            return Math.Max(p.RadiusKm * 2.5, Math.Min(wide, moonDistance * 0.75));
        }

        /// <summary>
        /// Straight line, or a dog-leg around the frame's centre when the line would pass within
        /// <paramref name="keepOutKm"/>, then detours around any of <paramref name="avoid"/> a leg runs through.
        /// </summary>
        static DVec3[] BuildPath(DVec3 start, DVec3 end, double keepOutKm, List<(DVec3 center, double radius)> avoid = null)
        {
            var points = new List<DVec3> { start, end };
            var d = end - start;
            double t = d.LengthSquared > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(start, d) / d.LengthSquared)) : 0;
            var closest = start + d * t;
            if (closest.Length < keepOutKm && t > 0 && t < 1)
            {
                var away = closest.LengthSquared > 1 ? closest.Normalized : DVec3.Cross(d, DVec3.UnitZ).Normalized;
                points.Insert(1, away * (keepOutKm * 1.4));
            }

            if (avoid != null)
            {
                foreach (var (center, radius) in avoid)
                {
                    for (int i = 0; i + 1 < points.Count; i++)
                    {
                        var a = points[i] - center;
                        var seg = points[i + 1] - points[i];
                        double len2 = seg.LengthSquared;
                        double u = len2 > 0 ? -DVec3.Dot(a, seg) / len2 : 0;
                        if (u <= 0 || u >= 1) continue;
                        var near = a + seg * u;
                        if (near.Length >= radius) continue;
                        var out1 = near.LengthSquared > 1 ? near.Normalized : DVec3.Cross(seg, DVec3.UnitZ).Normalized;
                        points.Insert(i + 1, center + out1 * (radius * 1.4));
                        break;
                    }
                }
            }
            return points.ToArray();
        }

        static double PolylineLength(DVec3[] p)
        {
            double len = 0;
            for (int i = 1; i < p.Length; i++) len += (p[i] - p[i - 1]).Length;
            return len;
        }

        static double[] Cumulative(DVec3[] p)
        {
            var c = new double[p.Length];
            for (int i = 1; i < p.Length; i++) c[i] = c[i - 1] + (p[i] - p[i - 1]).Length;
            return c;
        }

        string CargoFor(Ship ship, int origin)
        {
            if (ship.Spec.IsMilitary) return null;
            if (ship.Role == ShipRole.Courier) return "Priority mail and data cores";
            if (ship.Role == ShipRole.Miner)
                return origin == ship.Ports[0] ? (rng.Next(2) == 0 ? "Nickel-iron ore" : "Platinum-group metals") : "Empty, outbound to the belt";

            switch (catalog[origin].Name)
            {
                case "Earth": return Pick("Machinery", "Medical supplies", "Electronics");
                case "Moon": return Pick("Helium-3", "Regolith glass");
                case "Mars": return Pick("Fabricated alloys", "Hydroponic food");
                case "Ceres": return Pick("Water ice", "Nickel-iron ore");
                case "Europa": return "Water";
                case "Ganymede": return Pick("Habitat modules", "Reactor fuel");
                case "Callisto": return "Refined metals";
                case "Titan": return Pick("Hydrocarbons", "Nitrogen");
                case "Enceladus": return "Volatiles";
                case "Titania": return "Water ice";
                case "Triton": return "Nitrogen ice";
                default: return "General freight";
            }
        }

        string Pick(params string[] options) => options[rng.Next(options.Length)];

        // ---------------------------------------------------------------- setup

        public Faction AddFaction(string name, string prefix, Rgb color)
        {
            var f = new Faction(name, prefix, color);
            factions.Add(f);
            return f;
        }

        public Ship AddShip(string name, ShipClass c, Faction faction, ShipRole role)
        {
            var ship = new Ship
            {
                Id = ships.Count,
                Name = faction.Prefix.Length > 0 ? faction.Prefix + " " + name : name,
                Spec = ShipClassSpec.Of(c),
                Faction = faction,
                Role = role,
                Combat = new ShipCombat(CombatSpec.Of(c), c),
            };
            ships.Add(ship);
            return ship;
        }

        public void AddEscort(Ship leader, Ship escort, DVec3 slot)
        {
            escort.Leader = leader;
            escort.FormationSlot = slot;
            escort.Role = ShipRole.Escort;
        }

        /// <summary>Parks a ship at a body with a randomised departure so a fresh fleet does not move in lockstep.</summary>
        public void Station(Ship ship, int body, double jd) => Station(ship, body, jd, null);

        /// <summary>As <see cref="Station(Ship, int, double)"/>, in a given orbit (a new hull leaving its yard).</summary>
        public void Station(Ship ship, int body, double jd, ParkingOrbit orbit)
        {
            ship.Ring = null;
            ship.HoldPoint = null;
            if (orbit != null)
            {
                ship.Orbit = new ParkingOrbit
                {
                    Body = body, RadiusKm = orbit.RadiusKm, U = orbit.U, V = orbit.V, Center = orbit.Center,
                    EpochJd = jd, PhaseAtEpoch = orbit.PhaseAt(jd) - 0.002, RateRadPerDay = orbit.RateRadPerDay,
                };
                ship.Phase = ShipPhase.Parked;
                ship.PhaseStartJd = jd;
                ScheduleNext(ship, jd + 0.2 + rng.NextDouble() * 0.5);
                return;
            }
            ship.Orbit = NewParkingOrbit(body, ship);
            ship.Orbit.EpochJd = jd;
            ship.Orbit.PhaseAtEpoch = rng.NextDouble() * Astro.TwoPi;
            ship.Phase = ShipPhase.Parked;
            ship.PhaseStartJd = jd;
            ScheduleNext(ship, jd + rng.NextDouble() * 2.0);
        }

        int Body(string name) => catalog[name].Index;

        int[] Bodies(params string[] names)
        {
            var result = new int[names.Length];
            for (int i = 0; i < names.Length; i++) result[i] = Body(names[i]);
            return result;
        }

        /// <summary>
        /// The default traffic: three navies patrolling with carrier and battleship groups, plus
        /// freighters, ore haulers and couriers working the ports. The schedule is pre-rolled for
        /// a few weeks so ships start spread along their routes instead of all parked.
        /// </summary>
        public static Fleet CreateDefault(Ephemeris ephemeris, double jd, int seed = 1977)
        {
            var fleet = new Fleet(ephemeris, seed);
            double startJd = jd - 45;

            var sol = fleet.AddFaction("Sol Federation", "SFS", new Rgb(0.35f, 0.65f, 1f));
            var belt = fleet.AddFaction("Belt Consortium", "BCS", new Rgb(1f, 0.6f, 0.25f));
            var reach = fleet.AddFaction("Outer Reach Compact", "ORS", new Rgb(0.72f, 0.5f, 1f));
            var civil = fleet.AddFaction("Independent", "MV", new Rgb(0.85f, 0.85f, 0.78f));

            fleet.Group(sol, startJd, ShipClass.Carrier, "Resolute", new[] { "Earth", "Moon", "Mars" },
                (ShipClass.Battleship, "Dauntless"), (ShipClass.Cruiser, "Aegis"), (ShipClass.Cruiser, "Halberd"),
                (ShipClass.Destroyer, "Kestrel"), (ShipClass.Destroyer, "Merlin"));
            fleet.Group(sol, startJd, ShipClass.Battleship, "Vanguard", new[] { "Mars", "Ceres", "Earth" },
                (ShipClass.Cruiser, "Lance"), (ShipClass.Destroyer, "Swift"), (ShipClass.Destroyer, "Talon"));
            fleet.Group(belt, startJd, ShipClass.Carrier, "Tycho Ascendant", new[] { "Ceres", "Ganymede", "Callisto", "Europa" },
                (ShipClass.Battleship, "Iron Ledger"), (ShipClass.Cruiser, "Ore Hawk"), (ShipClass.Cruiser, "Pallas"),
                (ShipClass.Destroyer, "Flint"));
            fleet.Group(belt, startJd, ShipClass.Cruiser, "Hygiea", new[] { "Ceres", "Mars" },
                (ShipClass.Destroyer, "Shard"), (ShipClass.Destroyer, "Spall"));
            fleet.Group(reach, startJd, ShipClass.Battleship, "Long Night", new[] { "Titan", "Titania", "Triton" },
                (ShipClass.Cruiser, "Umbra"), (ShipClass.Cruiser, "Perihelion"), (ShipClass.Destroyer, "Wisp"));
            fleet.Group(reach, startJd, ShipClass.Carrier, "Endurance", new[] { "Triton", "Charon", "Titan", "Ganymede" },
                (ShipClass.Cruiser, "Aphelion"), (ShipClass.Destroyer, "Rime"), (ShipClass.Destroyer, "Hoarfrost"));

            var ports = fleet.Bodies("Earth", "Moon", "Mars", "Ceres", "Europa", "Ganymede", "Callisto", "Titan", "Enceladus", "Titania", "Triton");
            var innerPorts = fleet.Bodies("Earth", "Moon", "Mars", "Ceres", "Ganymede");
            string[] freighterNames =
            {
                "Bountiful Harvest", "Kepler's Due", "Slow Boat", "Red Ledger", "Ice Mule", "Long Haul", "Patience",
                "Margin of Error", "Cold Comfort", "Ballast", "Heavy Lifting", "Second Wind", "Tin Can", "Far Shore",
            };
            Faction[] traderFactions = { civil, civil, sol, belt, reach };
            for (int i = 0; i < freighterNames.Length; i++)
            {
                var f = traderFactions[i % traderFactions.Length];
                var ship = fleet.AddShip(freighterNames[i], ShipClass.Freighter, f, ShipRole.Trader);
                ship.Ports = i % 3 == 0 ? ports : innerPorts;
                fleet.Station(ship, ship.Ports[fleet.rng.Next(ship.Ports.Length)], startJd);
            }

            string[] minerNames = { "Pickaxe", "Rockhound", "Prospect", "Tailings", "Motherlode", "Seam" };
            var minerPorts = fleet.Bodies("Ceres", "Mars", "Earth", "Moon");
            foreach (var name in minerNames)
            {
                var ship = fleet.AddShip(name, ShipClass.Miner, belt, ShipRole.Miner);
                ship.Ports = minerPorts;
                fleet.Station(ship, minerPorts[fleet.rng.Next(minerPorts.Length)], startJd);
            }

            string[] courierNames = { "Quicksilver", "Hermes", "Dispatch", "Lightfoot" };
            foreach (var name in courierNames)
            {
                var ship = fleet.AddShip(name, ShipClass.Courier, civil, ShipRole.Courier);
                ship.Ports = ports;
                fleet.Station(ship, ports[fleet.rng.Next(ports.Length)], startJd);
            }

            fleet.Update(jd);
            return fleet;
        }

        public static DVec3 SlotFor(int index) => EscortSlots[index % EscortSlots.Length] * (1 + index / EscortSlots.Length * 0.6);

        static readonly DVec3[] EscortSlots =
        {
            new DVec3(0, 0.45, -1.7),
            new DVec3(-1.5, 0.05, 0.2),
            new DVec3(1.5, 0.05, 0.2),
            new DVec3(-2.6, -0.25, 1.3),
            new DVec3(2.6, -0.25, 1.3),
        };

        /// <summary>
        /// Rough flight time between two bodies for planning: the brachistochrone over their current
        /// separation, plus a day of manoeuvring.
        /// </summary>
        public double EstimateTravelDays(Ship ship, int from, int to, double jd)
        {
            double accel = ship.Spec.AccelG * Astro.GravityKmPerDay2;
            double Burn(double km) => 2 * Math.Sqrt(Math.Max(km, 1) / accel);
            if (Lanes != null)
            {
                int a = Lanes.SystemOf(from), b = Lanes.SystemOf(to);
                var route = a != b ? Lanes.Route(a, b, jd) : null;
                if (route != null)
                {
                    double days = Burn((Lanes.RampHelio(route[0].Lane, a, jd) - ephemeris.HelioAt(from, jd)).Length);
                    for (int i = 0; i < route.Count; i++)
                    {
                        days += Hyperlanes.RideDays(Lanes.LaneLengthKm(route[i].Lane, jd));
                        if (i + 1 < route.Count) days += Burn(Lanes.RampRadiusKm(route[i].To) * 1.5);
                    }
                    days += Burn((Lanes.RampHelio(route[route.Count - 1].Lane, b, jd) - ephemeris.HelioAt(to, jd)).Length);
                    return days + 0.3 + RingOverheadDays(ship, to, jd + days);
                }
            }
            double d = (ephemeris.HelioAt(to, jd) - ephemeris.HelioAt(from, jd)).Length;
            double direct = Burn(d) + 1.0;
            return direct + RingOverheadDays(ship, to, jd + direct);
        }

        /// <summary>Where the first leg of a trip heads: the first lane's ramp, or the destination itself.</summary>
        DVec3 FirstLegTarget(int here, int next, double jd)
        {
            if (Lanes != null)
            {
                int a = Lanes.SystemOf(here), b = Lanes.SystemOf(next);
                var route = a != b ? Lanes.Route(a, b, jd) : null;
                if (route != null) return Lanes.RampHelio(route[0].Lane, a, jd);
            }
            return ephemeris.HelioAt(next, jd);
        }

        public Random Rng => rng;

        public Ship Group(Faction faction, double jd, ShipClass leaderClass, string leaderName, string[] route,
            params (ShipClass cls, string name)[] escorts)
        {
            var leader = AddShip(leaderName, leaderClass, faction, ShipRole.Patrol);
            leader.Route = Bodies(route);
            leader.RouteIndex = 0;
            for (int i = 0; i < escorts.Length; i++)
            {
                var escort = AddShip(escorts[i].name, escorts[i].cls, faction, ShipRole.Escort);
                AddEscort(leader, escort, EscortSlots[i % EscortSlots.Length]);
            }
            Station(leader, leader.Route[0], jd);
            return leader;
        }
    }
}
