using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>A hyperlane between two planetary systems (each named by its planet, the body orbiting the Sun).</summary>
    public sealed class Lane
    {
        public int Id;
        public int A, B;
        public string Name;

        /// <summary>Sideways bow of the lane as a fraction of its length; lanes always bow away from the Sun.</summary>
        public double Bend;

        /// <summary>
        /// The nearest star (or barycentre) both ends orbit within: the Sun for every lane in Sol, a
        /// star for a lane inside its own system, the A-B barycentre for Alpha Centauri's long lanes.
        /// Lanes bow away from it.
        /// </summary>
        public int Hub;

        public int Other(int system) => system == A ? B : A;
    }

    /// <summary>One lane ridden in one direction.</summary>
    public readonly struct LaneHop
    {
        public readonly Lane Lane;
        public readonly int From, To;

        public LaneHop(Lane lane, int from, int to)
        {
            Lane = lane;
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// The X3-style highway network. Each planetary system has an on-ramp for each of its lanes, a
    /// fixed distance out past its moons, facing the system at the lane's other end. A ship crosses
    /// to the ramp under its own drive, rides the lane at up to <see cref="CruiseKmS"/> (it eases in
    /// and out, arriving at rest relative to the far ramp), then crosses from the far ramp to its
    /// destination, again under its own drive. Lanes bow gently to one side and swing wide of the
    /// Sun. Within one planet's system (Earth to the Moon, Io to Callisto) ships just fly.
    /// </summary>
    public sealed class Hyperlanes
    {
        public readonly List<Lane> Lanes = new List<Lane>();

        /// <summary>
        /// How dangerous each lane has been lately (ambushes), in days of extra cost when planning a
        /// route, so traffic shifts to safer lanes until it fades.
        /// </summary>
        public readonly Dictionary<Lane, double> Danger = new Dictionary<Lane, double>();
        readonly BodyCatalog catalog;
        readonly Ephemeris ephemeris;

        /// <summary>Peak speed in a lane: 4% of light, so Earth to Mars takes hours, not weeks.</summary>
        public const double CruiseKmS = 12000;

        /// <summary>Lanes stay this far from the Sun; other stars scale it with the square root of their luminosity (equal heating).</summary>
        public const double SunKeepOutKm = 0.3 * Astro.AuKm;

        /// <summary>
        /// The longest a ride takes, days. A lane's peak speed is raised past <see cref="CruiseKmS"/>
        /// when it is that long: the 13,000 AU between Alpha Centauri A-B and Proxima is crossed in days
        /// (deep fiction), while in Sol only the longest outer lanes are sped up a little.
        /// </summary>
        public const double MaxRideDays = 5;

        public Hyperlanes(Ephemeris ephemeris)
        {
            this.ephemeris = ephemeris;
            catalog = ephemeris.Catalog;
        }

        public static Hyperlanes CreateDefault(Ephemeris ephemeris)
        {
            var h = new Hyperlanes(ephemeris);
            (string, string, double)[] links =
            {
                ("Mercury", "Venus", 0.06), ("Venus", "Earth", -0.05), ("Earth", "Mars", 0.07), ("Mars", "Ceres", -0.06),
                ("Ceres", "Jupiter", 0.05), ("Earth", "Ceres", -0.04), ("Mars", "Jupiter", 0.04), ("Jupiter", "Saturn", -0.05),
                ("Saturn", "Uranus", 0.05), ("Uranus", "Neptune", -0.06), ("Neptune", "Pluto", 0.07), ("Saturn", "Neptune", 0.03),
            };
            foreach (var (a, b, bend) in links) h.Add(a, b, bend);
            return h;
        }

        /// <summary>
        /// Alpha Centauri's network: short lanes inside each star's system, and two long trunk lanes
        /// across the 13,000 AU between the A-B pair and Proxima (the only practical way across).
        /// </summary>
        public static Hyperlanes CreateAlphaCentauri(Ephemeris ephemeris)
        {
            var h = new Hyperlanes(ephemeris);
            (string, string, double)[] links =
            {
                ("Rigil Gate", "Alpha Centauri Ab", 0.06), ("Rigil Gate", "Toliman Forge", -0.05),
                ("Toliman Forge", "Proxima b", 0.02), ("Rigil Gate", "Proxima b", -0.02),
                ("Proxima b", "Proxima c", 0.05), ("Proxima b", "Proxima d", -0.06), ("Proxima b", "Blackhollow", 0.07),
            };
            foreach (var (a, b, bend) in links) h.Add(a, b, bend);
            return h;
        }

        public Lane Add(string a, string b, double bend)
        {
            var lane = new Lane { Id = Lanes.Count, A = catalog[a].Index, B = catalog[b].Index, Bend = bend, Name = a + "–" + b };
            lane.Hub = CommonStar(lane.A, lane.B);
            Lanes.Add(lane);
            return lane;
        }

        /// <summary>The nearest star or barycentre that both bodies orbit within.</summary>
        int CommonStar(int a, int b)
        {
            for (int x = catalog[a].ParentIndex; x >= 0; x = catalog[x].ParentIndex)
            {
                if (!catalog[x].UsesStellarMapping) continue;
                for (int y = catalog[b].ParentIndex; y >= 0; y = catalog[y].ParentIndex)
                    if (x == y) return x;
            }
            return 0;
        }

        /// <summary>
        /// The planetary system a body belongs to: the body itself, or the planet it orbits. Anything
        /// orbiting a star directly (a planet, a station) is a system of its own.
        /// </summary>
        public int SystemOf(int body)
        {
            while (body > 0 && !catalog[catalog[body].ParentIndex].UsesStellarMapping) body = catalog[body].ParentIndex;
            return body;
        }

        /// <summary>How close lanes may pass a star (km): <see cref="SunKeepOutKm"/> scaled for its luminosity.</summary>
        public double KeepOutKm(int star) => SunKeepOutKm * Math.Sqrt(Math.Max(catalog[star].LuminositySolar, 1e-6));

        /// <summary>
        /// The star a lane between two points (true km) must swing round: the one the straight line
        /// passes closest to, measured in its own keep-out radii. In Sol it is always the Sun.
        /// </summary>
        public int KeepOutStar(DVec3 aHelio, DVec3 bHelio, double jd)
        {
            int best = 0;
            double bestRatio = double.MaxValue;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (!catalog[i].IsStar) continue;
                var c = ephemeris.HelioAt(i, jd);
                var chord = bHelio - aHelio;
                double t = Math.Max(0, Math.Min(1, DVec3.Dot(c - aHelio, chord) / Math.Max(chord.LengthSquared, 1e-12)));
                double ratio = (aHelio + chord * t - c).Length / KeepOutKm(i);
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>A point on a lane in true km, between ramp points <paramref name="a"/> and <paramref name="b"/> (heliocentric).</summary>
        public DVec3 CurveKm(Lane lane, DVec3 a, DVec3 b, double s, double jd)
        {
            int star = KeepOutStar(a, b, jd);
            return Curve(a, b, lane.Bend, ephemeris.HelioAt(lane.Hub, jd), ephemeris.HelioAt(star, jd), KeepOutKm(star), s, DVec3.UnitZ);
        }

        public double CurveLengthKm(Lane lane, DVec3 a, DVec3 b, double jd)
        {
            int star = KeepOutStar(a, b, jd);
            return CurveLength(a, b, lane.Bend, ephemeris.HelioAt(lane.Hub, jd), ephemeris.HelioAt(star, jd), KeepOutKm(star), DVec3.UnitZ);
        }

        public bool HasLanes(int system)
        {
            foreach (var l in Lanes)
                if (l.A == system || l.B == system) return true;
            return false;
        }

        /// <summary>How far out a system's ramps sit: past its moons, inside the planet's drawn neighbourhood.</summary>
        public double RampRadiusKm(int system)
        {
            var p = catalog[system];
            double wanted = Math.Max(p.MoonReachKm * 1.1, p.RadiusKm * 6);
            return Math.Min(wanted, ScaleModel.NeighbourhoodKm(p) * 0.9);
        }

        /// <summary>The ramp's direction from its planet: toward the system at the lane's other end.</summary>
        public DVec3 RampDirection(Lane lane, int at, double jd) =>
            (ephemeris.HelioAt(lane.Other(at), jd) - ephemeris.HelioAt(at, jd)).Normalized;

        public DVec3 RampOffset(Lane lane, int at, double jd) => RampDirection(lane, at, jd) * RampRadiusKm(at);

        public DVec3 RampHelio(Lane lane, int at, double jd) => ephemeris.HelioAt(at, jd) + RampOffset(lane, at, jd);

        /// <summary>
        /// A point along a lane from <paramref name="a"/> to <paramref name="b"/> (s from 0 to 1): a
        /// quadratic curve bowed outward by <paramref name="bend"/> and pushed out to stay
        /// <paramref name="keepOut"/> from the origin (the Sun). Works in true km or scene units alike.
        /// </summary>
        public static DVec3 Curve(DVec3 a, DVec3 b, double bend, double keepOut, double s, DVec3 up) =>
            Curve(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, s, up);

        /// <summary>
        /// The general lane curve: bowed away from <paramref name="hub"/> (the star or barycentre the
        /// lane belongs to), and swung round <paramref name="keepOutCentre"/> (the star the line passes
        /// nearest) at <paramref name="keepOut"/>. In Sol both are the Sun at the origin.
        /// </summary>
        public static DVec3 Curve(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, double s, DVec3 up)
        {
            var chord = b - a;
            double length = chord.Length;
            var dir = length > 0 ? chord / length : DVec3.UnitX;
            var mid = (a + b) * 0.5;
            var side = DVec3.Cross(chord, up);
            side = side.LengthSquared > 0 ? side.Normalized : DVec3.UnitX;
            // Bow away from the hub, so the curve never sags toward the star.
            if (DVec3.Dot(side, mid - hub) < 0) side = -side;

            // Closest approach of the straight line to the star.
            var ac = a - keepOutCentre;
            double t0 = Math.Max(0, Math.Min(1, -DVec3.Dot(ac, chord) / Math.Max(length * length, 1e-12)));
            var nearest = ac + chord * t0;
            if (nearest.Length > keepOut * 1.5 || t0 <= 0 || t0 >= 1)
            {
                var apex = mid + side * (Math.Abs(bend) * length);
                // The control point that puts the curve's middle at the apex.
                var control = apex * 2 - mid;
                return Quadratic(a, control, b, s);
            }

            // The line passes near the star: go round through a waypoint out on the keep-out circle,
            // as two quadratics that join smoothly there (tangent along the chord).
            var outward = nearest - dir * DVec3.Dot(nearest, dir);
            outward = outward.LengthSquared > 1e-6 * keepOut * keepOut ? outward.Normalized : side;
            var w = keepOutCentre + nearest - outward * DVec3.Dot(nearest, outward) + outward * (keepOut * 1.4);
            if (s < 0.5)
            {
                var c1 = w - dir * ((w - a).Length * 0.5);
                return Quadratic(a, c1, w, s * 2);
            }
            var c2 = w + dir * ((b - w).Length * 0.5);
            return Quadratic(w, c2, b, s * 2 - 1);
        }

        static DVec3 Quadratic(DVec3 a, DVec3 c, DVec3 b, double t)
        {
            double u = 1 - t;
            return a * (u * u) + c * (2 * u * t) + b * (t * t);
        }

        public static DVec3 CurveTangent(DVec3 a, DVec3 b, double bend, double keepOut, double s, DVec3 up) =>
            CurveTangent(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, s, up);

        public static DVec3 CurveTangent(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, double s, DVec3 up)
        {
            const double h = 1e-4;
            double s0 = Math.Max(0, s - h), s1 = Math.Min(1, s + h);
            var d = Curve(a, b, bend, hub, keepOutCentre, keepOut, s1, up) - Curve(a, b, bend, hub, keepOutCentre, keepOut, s0, up);
            return d.LengthSquared > 0 ? d.Normalized : (b - a).Normalized;
        }

        public static double CurveLength(DVec3 a, DVec3 b, double bend, double keepOut, DVec3 up) =>
            CurveLength(a, b, bend, DVec3.Zero, DVec3.Zero, keepOut, up);

        public static double CurveLength(DVec3 a, DVec3 b, double bend, DVec3 hub, DVec3 keepOutCentre, double keepOut, DVec3 up)
        {
            double len = 0;
            var prev = a;
            for (int i = 1; i <= 32; i++)
            {
                var p = Curve(a, b, bend, hub, keepOutCentre, keepOut, i / 32.0, up);
                len += (p - prev).Length;
                prev = p;
            }
            return len;
        }

        /// <summary>Ease in, cruise, ease out: distance fraction at time fraction u, at rest at both ends.</summary>
        public static double Progress(double u)
        {
            u = Math.Max(0, Math.Min(1, u));
            return u - Math.Sin(Astro.TwoPi * u) / Astro.TwoPi;
        }

        /// <summary>
        /// Days to ride a whole lane of this length (the eased profile peaks at twice the mean speed),
        /// never more than <see cref="MaxRideDays"/>. Part of a lane takes that fraction of the time.
        /// </summary>
        public static double RideDays(double lengthKm) => Math.Min(2 * lengthKm / CruiseKmS / Astro.SecondsPerDay, MaxRideDays);

        // Lane geometry changes over days, and planning asks for it constantly: cache per half day.
        readonly Dictionary<(int, long), double> lengthCache = new Dictionary<(int, long), double>();
        readonly Dictionary<(int, int, long), List<LaneHop>> routeCache = new Dictionary<(int, int, long), List<LaneHop>>();
        long cacheBucket = long.MinValue;

        long Bucket(double jd)
        {
            long bucket = (long)Math.Floor(jd * 2);
            if (bucket != cacheBucket)
            {
                cacheBucket = bucket;
                lengthCache.Clear();
                routeCache.Clear();
            }
            return bucket;
        }

        public double LaneLengthKm(Lane lane, double jd)
        {
            var key = (lane.Id, Bucket(jd));
            if (lengthCache.TryGetValue(key, out double km)) return km;
            km = CurveLengthKm(lane, RampHelio(lane, lane.A, jd), RampHelio(lane, lane.B, jd), jd);
            lengthCache[key] = km;
            return km;
        }

        /// <summary>Fewest-hours route between two systems (Dijkstra on ride times at the given date), or null.</summary>
        public List<LaneHop> Route(int fromSystem, int toSystem, double jd)
        {
            if (fromSystem == toSystem) return null;
            var key = (fromSystem, toSystem, Bucket(jd));
            if (routeCache.TryGetValue(key, out var cached)) return cached;
            var route = FindRoute(fromSystem, toSystem, jd);
            routeCache[key] = route;
            return route;
        }

        List<LaneHop> FindRoute(int fromSystem, int toSystem, double jd)
        {
            var dist = new Dictionary<int, double> { [fromSystem] = 0 };
            var via = new Dictionary<int, LaneHop>();
            var open = new List<int> { fromSystem };
            var done = new HashSet<int>();
            while (open.Count > 0)
            {
                open.Sort((x, y) => dist[x].CompareTo(dist[y]));
                int here = open[0];
                open.RemoveAt(0);
                if (!done.Add(here)) continue;
                if (here == toSystem) break;
                foreach (var lane in Lanes)
                {
                    if (lane.A != here && lane.B != here) continue;
                    int next = lane.Other(here);
                    // Each hop also costs the crossing between ramps at a junction: about half a day.
                    Danger.TryGetValue(lane, out double danger);
                    double cost = dist[here] + RideDays(LaneLengthKm(lane, jd)) + 0.5 + danger;
                    if (dist.TryGetValue(next, out double old) && old <= cost) continue;
                    dist[next] = cost;
                    via[next] = new LaneHop(lane, here, next);
                    open.Add(next);
                }
            }
            if (!via.ContainsKey(toSystem)) return null;
            var route = new List<LaneHop>();
            for (int at = toSystem; at != fromSystem; at = via[at].From) route.Insert(0, via[at]);
            return route;
        }
    }
}
