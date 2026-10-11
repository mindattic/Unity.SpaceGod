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

        public Fleet(Ephemeris ephemeris, int seed)
        {
            this.ephemeris = ephemeris;
            catalog = ephemeris.Catalog;
            rng = new Random(seed);
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Advances every ship's schedule up to <paramref name="jd"/>.</summary>
        public void Update(double jd)
        {
            foreach (var ship in ships)
            {
                if (ship.IsEscort) continue;
                for (int guard = 0; guard < 4096 && jd >= ship.PhaseEndJd; guard++)
                    AdvancePhase(ship);
            }
        }

        /// <summary>State at <paramref name="jd"/>; parked ships use the ephemeris' current frame, which must be at the same date.</summary>
        public ShipState StateOf(Ship ship, double jd)
        {
            var s = ship.Controller;
            if (s.Phase == ShipPhase.Parked)
            {
                var helio = ephemeris.Helio(s.Orbit.Body) + s.Orbit.OffsetAt(jd);
                return new ShipState(helio, s.Orbit.TangentAt(jd), s.Orbit.SpeedKmS, false, 0);
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
            if (ship.Phase == ShipPhase.Parked) Depart(ship);
            else Arrive(ship);
        }

        void Depart(Ship ship)
        {
            double departJd = ship.PhaseEndJd;
            int from = ship.Orbit.Body;
            int to = ship.DestinationBody;
            int frame = CommonFrame(from, to);
            double keepOut = frame == 0 ? SunKeepOutKm : catalog[frame].RadiusKm * PlanetKeepOutRadii;
            var start = RelativeTo(frame, from, departJd) + ship.Orbit.OffsetAt(departJd);
            var startVelocity = VelocityRelativeTo(frame, from, departJd) + ship.Orbit.VelocityAt(departJd);
            var arrival = NewParkingOrbit(to, ship);

            // Match velocities at both ends unless that path runs into something the rest-to-rest
            // one in the frame avoids.
            if (!Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, true)
                && !Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, false))
                Plan(ship, frame, from, to, departJd, start, startVelocity, arrival, keepOut, true);

            ship.DepartureOrbit = ship.Orbit;
            ship.Orbit = arrival;
            ship.OriginBody = from;
            ship.Phase = ShipPhase.Transit;
            ship.FrameBody = frame;
            ship.PhaseStartJd = departJd;
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
                arrival.EpochJd = arriveJd;
                arrival.PhaseAtEpoch = arrival.PhaseFacing((start - target).Normalized);
                v1 = matchVelocity ? VelocityRelativeTo(frame, to, arriveJd) + arrival.VelocityAt(arriveJd) : DVec3.Zero;
                // The drift covers (v0 + v1)·T / 2 of the way; the flip-and-burn covers the rest.
                var end = target + arrival.OffsetAt(arriveJd) - (v0 + v1) * (flightDays * 0.5);
                path = BuildPath(start, end, keepOut);
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

            const int samples = 256;
            double total = ship.PhaseEndJd - ship.PhaseStartJd;
            for (int k = 1; k < samples; k++)
            {
                double tau = total * k / samples;
                var offset = TransitOffsetAt(ship, tau);
                if (offset.Length < keepOut * 0.8) return false;
                double jd = ship.PhaseStartJd + tau;
                var helio = ephemeris.HelioAt(frame, jd) + offset;
                foreach (int b in obstacles)
                    if ((helio - ephemeris.HelioAt(b, jd)).Length < catalog[b].RadiusKm * 1.5) return false;
            }
            return true;
        }

        void Arrive(Ship ship)
        {
            double arriveJd = ship.PhaseEndJd;
            ship.Phase = ShipPhase.Parked;
            ship.TripsCompleted++;
            ship.Path = null;
            ship.PhaseStartJd = arriveJd;
            ScheduleNext(ship, arriveJd);
        }

        /// <summary>Picks the next destination and the departure time: dwell, then wait for the parking orbit to face the target.</summary>
        void ScheduleNext(Ship ship, double nowJd)
        {
            int here = ship.Orbit.Body;
            int next = ChooseDestination(ship, here, nowJd);
            ship.DestinationBody = next;
            ship.Cargo = CargoFor(ship, here);

            double dwell = DwellDays(ship);
            double windowJd = nowJd + dwell;
            var toTarget = (ephemeris.HelioAt(next, windowJd) - ephemeris.HelioAt(here, windowJd)).Normalized;
            double want = ship.Orbit.PhaseFacing(toTarget);
            double have = ship.Orbit.PhaseAt(windowJd);
            double delta = (want - have) % Astro.TwoPi;
            if (delta < 0) delta += Astro.TwoPi;
            ship.PhaseEndJd = windowJd + delta / ship.Orbit.RateRadPerDay;
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

        /// <summary>Straight line, or a dog-leg around the frame's centre when the line would pass within <paramref name="keepOutKm"/>.</summary>
        static DVec3[] BuildPath(DVec3 start, DVec3 end, double keepOutKm)
        {
            var d = end - start;
            double t = d.LengthSquared > 0 ? Math.Max(0, Math.Min(1, -DVec3.Dot(start, d) / d.LengthSquared)) : 0;
            var closest = start + d * t;
            if (closest.Length >= keepOutKm || t <= 0 || t >= 1)
                return new[] { start, end };

            var away = closest.LengthSquared > 1 ? closest.Normalized : DVec3.Cross(d, DVec3.UnitZ).Normalized;
            return new[] { start, away * (keepOutKm * 1.4), end };
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
        public void Station(Ship ship, int body, double jd)
        {
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

        static readonly DVec3[] EscortSlots =
        {
            new DVec3(0, 0.45, -1.7),
            new DVec3(-1.5, 0.05, 0.2),
            new DVec3(1.5, 0.05, 0.2),
            new DVec3(-2.6, -0.25, 1.3),
            new DVec3(2.6, -0.25, 1.3),
        };

        void Group(Faction faction, double jd, ShipClass leaderClass, string leaderName, string[] route,
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
        }
    }
}
