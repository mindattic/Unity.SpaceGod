using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// The bodies of the simulation, with their real physical and orbital data.
    ///
    /// Sources:
    ///  - Planets: JPL "Keplerian Elements for Approximate Positions of the Major Planets", table 1
    ///    (valid 1800-2050), E. M. Standish. Earth's entry is the Earth-Moon barycentre.
    ///  - Ceres, Eris: JPL Small-Body Database osculating elements, epoch JD 2459000.5.
    ///  - The Moon: mean elements at J2000 with nodal regression (18.6 yr) and apsidal precession (8.85 yr).
    ///  - Other moons: semi-major axis, eccentricity, inclination and period from JPL satellite mean
    ///    elements. Their phase (node, perifocus, mean anomaly) is a fixed placeholder, so their
    ///    motion is physically right but where they are on the orbit today is not ephemeris-accurate.
    ///  - Radii, GM, poles and rotation: IAU WGCCRE report and NASA planetary fact sheets.
    /// </summary>
    public sealed class BodyCatalog
    {
        public IReadOnlyList<BodyDefinition> Bodies => bodies;
        public BodyDefinition Sun => bodies[0];
        public int Count => bodies.Count;

        readonly List<BodyDefinition> bodies;
        readonly Dictionary<string, BodyDefinition> byName = new Dictionary<string, BodyDefinition>(StringComparer.OrdinalIgnoreCase);
        readonly List<int>[] children;

        public BodyCatalog(IEnumerable<BodyDefinition> definitions)
        {
            bodies = new List<BodyDefinition>(definitions);
            for (int i = 0; i < bodies.Count; i++)
            {
                bodies[i].Index = i;
                byName[bodies[i].Name] = bodies[i];
            }

            children = new List<int>[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) children[i] = new List<int>();

            foreach (var b in bodies)
            {
                if (b.ParentName != null)
                {
                    if (!byName.TryGetValue(b.ParentName, out var parent))
                        throw new InvalidOperationException($"{b.Name}: unknown parent {b.ParentName}");
                    if (parent.Index > b.Index)
                        throw new InvalidOperationException($"{b.Name}: parent {parent.Name} must be listed first");
                    b.ParentIndex = parent.Index;
                    children[parent.Index].Add(b.Index);
                }

                if (!double.IsNaN(b.PoleRaDeg))
                    b.Pole = Astro.PoleFromRaDec(b.PoleRaDeg, b.PoleDecDeg);
                else if (b.Plane == OrbitPlane.ParentEquator && b.ParentIndex >= 0)
                    b.Pole = bodies[b.ParentIndex].SpinAxis; // synchronous moon: spins about its orbit normal
                else
                    b.Pole = DVec3.UnitZ;
            }

            foreach (var b in bodies)
            {
                if (b.ParentIndex < 0) continue;
                var parent = bodies[b.ParentIndex];
                double soi = b.Elements.AKm * Math.Pow(b.Gm / parent.Gm, 0.4);
                b.SoiKm = Math.Max(soi, b.RadiusKm * 3.0);
            }
        }

        public BodyDefinition this[int index] => bodies[index];
        public BodyDefinition this[string name] => byName[name];
        public bool TryGet(string name, out BodyDefinition body) => byName.TryGetValue(name, out body);
        public IReadOnlyList<int> ChildrenOf(int index) => children[index];

        public static BodyCatalog CreateSolSystem() => new BodyCatalog(SolDefinitions());

        static IEnumerable<BodyDefinition> SolDefinitions()
        {
            const double sbdbEpoch = 2459000.5;

            yield return new BodyDefinition
            {
                Name = "Sun", Kind = BodyKind.Star, RadiusKm = 695700, Gm = 1.32712440018e11,
                PoleRaDeg = 286.13, PoleDecDeg = 63.87, RotationHours = 609.12,
                Color = new Rgb(1f, 0.86f, 0.6f),
                Summary = "G2V main-sequence star. 99.86% of the system's mass.",
            };
            yield return new BodyDefinition
            {
                Name = "Mercury", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 2439.7, Gm = 22031.8,
                Elements = OrbitalElements.FromJpl(0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593,
                    0.00000037, 0.00001906, -0.00594749, 149472.67411175, 0.16047689, -0.12534081),
                PoleRaDeg = 281.01, PoleDecDeg = 61.42, RotationHours = 1407.6,
                Color = new Rgb(0.6f, 0.58f, 0.55f), Texture = "mercury_color", TexturePoleMargin = 0.035f,
                Summary = "Smallest planet; a 3:2 spin-orbit resonance gives it a 176-day solar day.",
            };
            yield return new BodyDefinition
            {
                Name = "Venus", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 6051.8, Gm = 324858.6,
                Elements = OrbitalElements.FromJpl(0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255,
                    0.00000390, -0.00004107, -0.00078890, 58517.81538729, 0.00268329, -0.27769418),
                PoleRaDeg = 272.76, PoleDecDeg = 67.16, RotationHours = -5832.6,
                Color = new Rgb(0.93f, 0.84f, 0.62f),
                AtmosphereColor = new Rgb(1f, 0.88f, 0.6f), AtmosphereStrength = 0.7f,
                Summary = "Spins backwards under a 92-bar CO2 atmosphere. No true-colour surface map exists.",
            };
            yield return new BodyDefinition
            {
                Name = "Earth", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 6371.0, Gm = 398600.435,
                Elements = OrbitalElements.FromJpl(1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0,
                    0.00000562, -0.00004392, -0.01294668, 35999.37244981, 0.32327364, 0.0),
                PoleRaDeg = 0, PoleDecDeg = 90, RotationHours = 23.9344696,
                Color = new Rgb(0.3f, 0.45f, 0.7f), Texture = "earth_color",
                AtmosphereColor = new Rgb(0.4f, 0.65f, 1f), AtmosphereStrength = 0.65f,
                Summary = "Home. Elements are for the Earth-Moon barycentre; Earth is offset from it by the Moon.",
            };
            yield return new BodyDefinition
            {
                Name = "Moon", Kind = BodyKind.Moon, ParentName = "Earth", RadiusKm = 1737.4, Gm = 4902.8,
                // Mean elements at J2000: L = 218.3165, perigee longitude 83.3532, node 125.0445.
                Elements = OrbitalElements.FromClassical(Astro.J2000, 384400, 0.0549, 5.145, 125.0445, 83.3532 - 125.0445,
                    218.3165 - 83.3532, 27.321661, -360.0 / 6798.38, 360.0 / 3232.6),
                PoleRaDeg = 269.99, PoleDecDeg = 66.54, RotationHours = 655.72,
                Color = new Rgb(0.62f, 0.61f, 0.6f),
                Summary = "Tidally locked. Slowly receding from Earth at 3.8 cm a year.",
            };
            yield return new BodyDefinition
            {
                Name = "Mars", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 3389.5, Gm = 42828.37,
                Elements = OrbitalElements.FromJpl(1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891,
                    0.00001847, 0.00007882, -0.00813131, 19140.30268499, 0.44441088, -0.29257343),
                PoleRaDeg = 317.68, PoleDecDeg = 52.89, RotationHours = 24.6229,
                Color = new Rgb(0.75f, 0.45f, 0.3f), Texture = "mars_color",
                AtmosphereColor = new Rgb(0.95f, 0.6f, 0.4f), AtmosphereStrength = 0.2f,
                Summary = "Home of Olympus Mons and Valles Marineris.",
            };
            yield return Moon("Phobos", "Mars", 11.27, 7.087e-4, 9376, 0.0151, 1.08, 0.31891, 10, 40, 150, new Rgb(0.42f, 0.38f, 0.35f));
            yield return Moon("Deimos", "Mars", 6.2, 9.6e-5, 23463, 0.00033, 0.93, 1.26244, 80, 20, 300, new Rgb(0.55f, 0.5f, 0.45f));

            yield return new BodyDefinition
            {
                Name = "Ceres", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 469.7, Gm = 62.6284,
                Elements = OrbitalElements.FromClassical(sbdbEpoch, 2.769165 * Astro.AuKm, 0.07600902, 10.59407, 80.30553, 73.59764,
                    77.37209589, OrbitalElements.PeriodFromGm(2.769165 * Astro.AuKm, 1.32712440018e11)),
                PoleRaDeg = 291.42, PoleDecDeg = 66.76, RotationHours = 9.074,
                Color = new Rgb(0.5f, 0.49f, 0.47f),
                Summary = "Largest object in the asteroid belt. Bright salt deposits in Occator crater.",
            };

            yield return new BodyDefinition
            {
                Name = "Jupiter", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 69911, Gm = 1.26686534e8,
                Elements = OrbitalElements.FromJpl(5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909,
                    -0.00011607, -0.00013253, -0.00183714, 3034.74612775, 0.21252668, 0.20469106),
                PoleRaDeg = 268.06, PoleDecDeg = 64.50, RotationHours = 9.925,
                Color = new Rgb(0.8f, 0.7f, 0.58f), Texture = "jupiter_color", TexturePoleMargin = 0.06f, // Hubble map ends near ±80°
                AtmosphereColor = new Rgb(0.9f, 0.8f, 0.65f), AtmosphereStrength = 0.3f,
                Summary = "Two and a half times the mass of every other planet combined.",
            };
            yield return Moon("Io", "Jupiter", 1821.6, 5959.9, 421700, 0.0041, 0.05, 1.769138, 0, 0, 30, new Rgb(0.93f, 0.83f, 0.45f));
            yield return Moon("Europa", "Jupiter", 1560.8, 3202.7, 671034, 0.009, 0.47, 3.551181, 0, 0, 120, new Rgb(0.86f, 0.8f, 0.72f));
            yield return Moon("Ganymede", "Jupiter", 2634.1, 9887.8, 1070412, 0.0013, 0.2, 7.154553, 0, 0, 250, new Rgb(0.6f, 0.57f, 0.52f));
            yield return Moon("Callisto", "Jupiter", 2410.3, 7179.3, 1882709, 0.0074, 0.192, 16.689017, 0, 0, 330, new Rgb(0.42f, 0.39f, 0.35f));

            yield return new BodyDefinition
            {
                Name = "Saturn", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 58232, Gm = 3.7931187e7,
                Elements = OrbitalElements.FromJpl(9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448,
                    -0.00125060, -0.00050991, 0.00193609, 1222.49362201, -0.41897216, -0.28867794),
                PoleRaDeg = 40.59, PoleDecDeg = 83.54, RotationHours = 10.656,
                Color = new Rgb(0.85f, 0.77f, 0.6f), Texture = "saturn_color",
                AtmosphereColor = new Rgb(0.95f, 0.85f, 0.6f), AtmosphereStrength = 0.3f,
                // C ring inner edge to A ring outer edge.
                RingInnerKm = 74658, RingOuterKm = 136775,
                Summary = "Less dense than water. Its main rings are ~10 m thick and 270,000 km across.",
            };
            yield return Moon("Enceladus", "Saturn", 252.1, 7.21, 237948, 0.0047, 0.009, 1.370218, 0, 0, 60, new Rgb(0.95f, 0.95f, 0.97f));
            var titan = Moon("Titan", "Saturn", 2574.7, 8978.1, 1221870, 0.0288, 0.348, 15.945421, 0, 186, 200, new Rgb(0.85f, 0.62f, 0.32f));
            titan.AtmosphereColor = new Rgb(0.9f, 0.65f, 0.3f);
            titan.AtmosphereStrength = 0.9f;
            titan.Summary = "Only moon with a thick atmosphere; methane lakes at its poles.";
            yield return titan;

            yield return new BodyDefinition
            {
                Name = "Uranus", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 25362, Gm = 5.793939e6,
                Elements = OrbitalElements.FromJpl(19.18916464, 0.04725744, 0.77263783, 313.23810451, 170.95427630, 74.01692503,
                    -0.00196176, -0.00004397, -0.00242939, 428.48202785, 0.40805281, 0.04240589),
                PoleRaDeg = 257.31, PoleDecDeg = -15.18, RotationHours = -17.24,
                Color = new Rgb(0.65f, 0.85f, 0.9f), Texture = "uranus_color",
                AtmosphereColor = new Rgb(0.6f, 0.9f, 0.95f), AtmosphereStrength = 0.4f,
                Summary = "Rolls around the Sun on its side, axial tilt 98 degrees.",
            };
            yield return Moon("Miranda", "Uranus", 235.8, 4.4, 129390, 0.0013, 4.232, 1.413479, 30, 60, 10, new Rgb(0.7f, 0.7f, 0.72f));
            yield return Moon("Titania", "Uranus", 788.4, 228.2, 435910, 0.0011, 0.34, 8.706234, 0, 0, 170, new Rgb(0.68f, 0.63f, 0.6f));
            yield return Moon("Oberon", "Uranus", 761.4, 205.3, 583520, 0.0014, 0.058, 13.463234, 0, 0, 280, new Rgb(0.6f, 0.55f, 0.53f));

            yield return new BodyDefinition
            {
                Name = "Neptune", Kind = BodyKind.Planet, ParentName = "Sun", RadiusKm = 24622, Gm = 6.836529e6,
                Elements = OrbitalElements.FromJpl(30.06992276, 0.00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574,
                    0.00026291, 0.00005105, 0.00035372, 218.45945325, -0.32241464, -0.00508664),
                PoleRaDeg = 299.36, PoleDecDeg = 43.46, RotationHours = 16.11,
                Color = new Rgb(0.35f, 0.5f, 0.9f), Texture = "neptune_color",
                AtmosphereColor = new Rgb(0.4f, 0.6f, 1f), AtmosphereStrength = 0.4f,
                Summary = "Fastest winds in the system, over 2,000 km/h.",
            };
            var triton = Moon("Triton", "Neptune", 1353.4, 1427.6, 354759, 0.000016, 156.885, 5.876854, 0, 0, 90, new Rgb(0.85f, 0.8f, 0.78f));
            triton.RotationHours = -triton.RotationHours;
            triton.Summary = "Orbits backwards: a captured Kuiper belt object.";
            yield return triton;

            yield return new BodyDefinition
            {
                Name = "Pluto", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 1188.3, Gm = 869.6,
                Elements = OrbitalElements.FromJpl(39.48211675, 0.24882730, 17.14001206, 238.92903833, 224.06891629, 110.30393684,
                    -0.00031596, 0.00005170, 0.00004818, 145.20780515, -0.04062942, -0.01183482),
                PoleRaDeg = 132.99, PoleDecDeg = -6.16, RotationHours = -153.2935,
                Color = new Rgb(0.8f, 0.68f, 0.55f),
                Summary = "Its eccentric orbit dips inside Neptune's for 20 years of every 248.",
            };
            yield return Moon("Charon", "Pluto", 606, 105.88, 19591, 0.0002, 0.08, 6.387221, 0, 0, 0, new Rgb(0.6f, 0.58f, 0.56f));

            yield return new BodyDefinition
            {
                Name = "Eris", Kind = BodyKind.DwarfPlanet, ParentName = "Sun", RadiusKm = 1163, Gm = 1108,
                Elements = OrbitalElements.FromClassical(sbdbEpoch, 67.864 * Astro.AuKm, 0.43607, 44.040, 35.951, 151.639,
                    205.989, OrbitalElements.PeriodFromGm(67.864 * Astro.AuKm, 1.32712440018e11)),
                RotationHours = 378.9,
                Color = new Rgb(0.92f, 0.92f, 0.9f),
                Summary = "Most massive known dwarf planet; its discovery got Pluto reclassified.",
            };
        }

        /// <summary>A regular satellite: elements in the parent's equatorial plane, synchronous rotation.</summary>
        static BodyDefinition Moon(string name, string parent, double radiusKm, double gm, double aKm, double e, double iDeg,
            double periodDays, double nodeDeg, double argPeriDeg, double meanAnomalyDeg, Rgb color)
        {
            return new BodyDefinition
            {
                Name = name, Kind = BodyKind.Moon, ParentName = parent, RadiusKm = radiusKm, Gm = gm,
                Elements = OrbitalElements.FromClassical(Astro.J2000, aKm, e, iDeg, nodeDeg, argPeriDeg, meanAnomalyDeg, periodDays),
                Plane = OrbitPlane.ParentEquator,
                RotationHours = periodDays * 24.0,
                Color = color,
                PhaseIsApproximate = true,
            };
        }
    }
}
