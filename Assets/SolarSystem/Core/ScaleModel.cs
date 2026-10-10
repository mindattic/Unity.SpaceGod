using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// Maps true (km) geometry into scene units, blending between two presentations:
    ///
    ///  - Realistic (Blend = 0): one linear scale for everything, 1 unit = 1000 km. The planets are
    ///    specks; Neptune is 4.5 million units out.
    ///  - Gameplay (Blend = 1): radii and distances are each compressed by power laws so the system
    ///    is navigable and the planets read as worlds rather than dots.
    ///
    /// Every mapping is radial about a parent (directions are never changed, only distances), so
    /// orbit shapes, phases and alignments stay true in both modes. Each mapping also sends a
    /// parent's surface to its displayed surface (x = 1 → 1 display radius), so things in low orbit
    /// stay outside the body at every blend. Intermediate blends interpolate in log space, which
    /// reads as a smooth zoom across the factor-of-40 differences.
    /// </summary>
    public sealed class ScaleModel
    {
        public const double KmPerUnit = 1000.0;

        // Gameplay radii: Earth = 255 units, others by (R / R_earth)^0.45.
        public const double GameEarthRadius = 255.0;
        public const double GameRadiusExponent = 0.45;

        // Gameplay heliocentric distance: 20000 units per AU^0.55 from the solar surface.
        public const double GameAuScale = 20000.0;
        public const double GameAuExponent = 0.55;

        // Gameplay distance around a planet: R_display · (1 + c·(x - 1)^p), x in body radii.
        public const double GameLocalC = 1.17;
        public const double GameLocalP = 0.44;

        // Gameplay ship length: 12 units per km^0.5 (a 3 km carrier is ~21 units against Earth's 255 radius).
        public const double GameShipScale = 12.0;
        public const double GameShipExponent = 0.5;

        const double EarthRadiusKm = 6371.0;

        double blend = 1.0;

        /// <summary>0 = realistic, 1 = gameplay.</summary>
        public double Blend
        {
            get => blend;
            set => blend = Math.Max(0, Math.Min(1, value));
        }

        public static double LogLerp(double a, double b, double t)
        {
            if (a <= 0 || b <= 0) return a + (b - a) * t;
            return Math.Exp(Math.Log(a) + (Math.Log(b) - Math.Log(a)) * t);
        }

        public static double RealisticRadius(double radiusKm) => radiusKm / KmPerUnit;

        public static double GameplayRadius(double radiusKm) =>
            GameEarthRadius * Math.Pow(radiusKm / EarthRadiusKm, GameRadiusExponent);

        public double DisplayRadius(double radiusKm) =>
            LogLerp(RealisticRadius(radiusKm), GameplayRadius(radiusKm), blend);

        public double DisplayRadius(BodyDefinition body) => DisplayRadius(body.RadiusKm);

        public double ShipLength(double lengthKm) =>
            LogLerp(lengthKm / KmPerUnit, GameShipScale * Math.Pow(lengthKm, GameShipExponent), blend);

        /// <summary>Scene distance from the centre of <paramref name="body"/> for a true distance in km.</summary>
        public double LocalDistance(BodyDefinition body, double distanceKm) =>
            LogLerp(distanceKm / KmPerUnit, GameplayLocalDistance(body, distanceKm), blend);

        public static double GameplayLocalDistance(BodyDefinition body, double distanceKm)
        {
            double r = body.RadiusKm;
            double rDisplay = GameplayRadius(r);
            if (distanceKm <= r) return rDisplay * distanceKm / r;

            if (body.IsStar)
            {
                return rDisplay + GameAuScale *
                    (Math.Pow(distanceKm / Astro.AuKm, GameAuExponent) - Math.Pow(r / Astro.AuKm, GameAuExponent));
            }

            double x = distanceKm / r;
            return rDisplay * (1.0 + GameLocalC * Math.Pow(x - 1.0, GameLocalP));
        }

        /// <summary>Inverse of <see cref="LocalDistance"/> (monotonic, so a bisection in log space is exact enough).</summary>
        public double InverseLocalDistance(BodyDefinition body, double sceneDistance)
        {
            if (sceneDistance <= 0) return 0;
            double lo = Math.Log(body.RadiusKm * 1e-6);
            double hi = Math.Log(1e13);
            for (int i = 0; i < 90; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (LocalDistance(body, Math.Exp(mid)) < sceneDistance) lo = mid;
                else hi = mid;
            }
            return Math.Exp(0.5 * (lo + hi));
        }

        /// <summary>d(scene distance)/d(true km) at a true distance; used to convert camera motion.</summary>
        public double LocalDistanceDerivative(BodyDefinition body, double distanceKm)
        {
            double h = Math.Max(distanceKm * 1e-5, 1e-6);
            double lo = Math.Max(distanceKm - h, 0);
            return (LocalDistance(body, distanceKm + h) - LocalDistance(body, lo)) / (distanceKm + h - lo);
        }

        /// <summary>Scene offset (Unity axes) from a body for a true ecliptic offset from it.</summary>
        public DVec3 LocalOffset(BodyDefinition body, DVec3 trueOffsetKm)
        {
            double d = trueOffsetKm.Length;
            if (d <= 0) return DVec3.Zero;
            return trueOffsetKm.SwapYZ * (LocalDistance(body, d) / d);
        }

        /// <summary>Inverse of <see cref="LocalOffset"/>: a scene offset (Unity axes) to a true ecliptic offset (km).</summary>
        public DVec3 InverseLocalOffset(BodyDefinition body, DVec3 sceneOffset)
        {
            double s = sceneOffset.Length;
            if (s <= 0) return DVec3.Zero;
            return sceneOffset.SwapYZ * (InverseLocalDistance(body, s) / s);
        }

        /// <summary>
        /// Lays every body out in scene space (Unity axes, Sun at the origin) from the ephemeris.
        /// Each body is placed relative to its parent's already-placed position.
        /// </summary>
        public void Layout(Ephemeris ephemeris, DVec3[] scenePositions)
        {
            var catalog = ephemeris.Catalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                var b = catalog[i];
                if (b.ParentIndex < 0)
                {
                    scenePositions[i] = DVec3.Zero;
                    continue;
                }
                scenePositions[i] = scenePositions[b.ParentIndex] + LocalOffset(catalog[b.ParentIndex], ephemeris.Relative(i));
            }
        }

        /// <summary>Scene radius of a body's sphere of influence, used for camera reference frames.</summary>
        public double SceneSoi(BodyDefinition body) =>
            double.IsInfinity(body.SoiKm) ? double.PositiveInfinity : LocalDistance(body, body.SoiKm);
    }
}
