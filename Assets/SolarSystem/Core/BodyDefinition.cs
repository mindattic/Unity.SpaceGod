using System;

namespace SolarSystem.Core
{
    public enum BodyKind
    {
        Star,
        Planet,
        DwarfPlanet,
        Moon,
    }

    /// <summary>Which plane a body's orbital elements are measured against.</summary>
    public enum OrbitPlane
    {
        /// <summary>J2000 ecliptic (planets, the Moon, minor planets).</summary>
        Ecliptic,

        /// <summary>
        /// The parent's equator, or the moon's Laplace plane when its pole is set (regular satellites).
        /// The node is measured from the plane's ascending node on the ecliptic.
        /// </summary>
        ParentEquator,
    }

    public readonly struct Rgb
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>Static, physical description of one body. Everything here is real data unless noted.</summary>
    public sealed class BodyDefinition
    {
        public string Name;
        public BodyKind Kind;
        public string ParentName;
        public double RadiusKm;

        /// <summary>Gravitational parameter GM in km³/s².</summary>
        public double Gm;

        public OrbitalElements Elements;
        public OrbitPlane Plane = OrbitPlane.Ecliptic;

        /// <summary>IAU north pole, equatorial J2000. NaN = unknown (falls back to ecliptic north).</summary>
        public double PoleRaDeg = double.NaN;
        public double PoleDecDeg = double.NaN;

        /// <summary>Sidereal rotation period in hours; negative = retrograde about the IAU pole.</summary>
        public double RotationHours;

        public Rgb Color = new Rgb(0.7f, 0.7f, 0.7f);
        public string Texture;

        /// <summary>Fraction of the map's height at each pole with no data (stretched over from the last valid row).</summary>
        public float TexturePoleMargin;

        /// <summary>
        /// The colour map is a single-band (greyscale) mosaic, so it is multiplied by <see cref="Color"/>
        /// (the body's documented average colour). False for genuine colour maps.
        /// </summary>
        public bool TintTexture;

        /// <summary>Tangent-space relief map derived from real elevation data (Resources/Textures), or null.</summary>
        public string NormalMap;

        /// <summary>Drawn with the banded, wind-driven gas giant shader instead of the solid-surface one.</summary>
        public bool GasGiant;

        /// <summary>
        /// How much of the surface an opaque atmosphere hides in visible light (0 = none, 1 = all),
        /// drawn as a layer of <see cref="AtmosphereColor"/> over the map (Titan's haze, Venus's clouds).
        /// </summary>
        public float SurfaceHaze;

        // ---- Orientation (see BodyOrientation)

        /// <summary>IAU prime meridian angle W at J2000 (deg). NaN = unknown, the meridian starts at the equator's node.</summary>
        public double PrimeMeridianDeg = double.NaN;

        /// <summary>IAU dW/dt (deg/day) about the map's north pole. NaN = derived from <see cref="RotationHours"/>.</summary>
        public double PrimeMeridianRateDegPerDay = double.NaN;

        /// <summary>
        /// Synchronous rotator whose 0° meridian faces its parent (the IAU convention for the Moon and
        /// the regular satellites). The meridian is pointed at the parent each frame; libration is not modelled.
        /// </summary>
        public bool FacesParent;

        /// <summary>
        /// The map's north is the opposite of <see cref="Pole"/>. Pluto's maps use the IAU positive
        /// (right-hand) pole, and the Uranian satellites' maps use Uranus's IAU north, which is the
        /// opposite of the spin axis their catalogue pole follows.
        /// </summary>
        public bool MapNorthFlipped;
        public Rgb AtmosphereColor;
        public float AtmosphereStrength;

        /// <summary>Ring system radii (km). Zero = no rings.</summary>
        public double RingInnerKm;
        public double RingOuterKm;

        /// <summary>Pole of a moon's Laplace plane, equatorial J2000. NaN = the parent's equator.</summary>
        public double LaplacePoleRaDeg = double.NaN;
        public double LaplacePoleDecDeg = double.NaN;

        public string Summary;

        // Resolved by BodyCatalog.
        public int Index { get; internal set; } = -1;
        public int ParentIndex { get; internal set; } = -1;
        public DVec3 Pole { get; internal set; } = DVec3.UnitZ;

        /// <summary>
        /// Direction of the spin angular momentum: the IAU pole, flipped for retrograde rotators.
        /// Regular moons orbit in this sense (Uranus's and Pluto's moons go "backwards" about the IAU pole).
        /// </summary>
        public DVec3 SpinAxis => RotationHours < 0 ? -Pole : Pole;

        /// <summary>Sphere of influence radius (km), Laplace's a·(m/M)^0.4, floored at a few body radii.</summary>
        public double SoiKm { get; internal set; } = double.PositiveInfinity;

        /// <summary>Farthest apoapsis of the body's moons (km); zero when it has none.</summary>
        public double MoonReachKm { get; internal set; }

        public bool HasRings => RingOuterKm > 0;
        public bool IsStar => Kind == BodyKind.Star;

        public override string ToString() => Name;
    }
}
