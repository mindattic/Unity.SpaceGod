using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The real stars near Sol that no hand-built entry covers (U.SPC-T413), as a <see cref="SystemSource"/>: the nearest
    /// stars the interstellar view has always shown (<see cref="Galaxy.Neighbours"/>), a few more of the nearest systems
    /// out to <see cref="RadiusLy"/>, and a handful of bright, well-known stars beyond as landmarks. It claims the sphere
    /// of <see cref="RadiusLy"/> about Sol and <see cref="ClearLy"/> about each of its stars, so the procedural galaxy
    /// (<see cref="ProceduralSystems"/>) never puts an invented star where a real one is, or in the real neighbourhood.
    ///
    /// The list is far from every star in that sphere (there are some sixty systems between 12 and 20 light-years): it
    /// is the ones well enough known to place, and more can be added here as rows. Positions, distances, types,
    /// magnitudes, temperatures, masses, radii and luminosities are from each star's Wikipedia article (from Hipparcos,
    /// Gaia DR3 and the papers it cites), listed in Systems/SOURCES.md. A star without a mass in its row (one of the
    /// neighbours) has one estimated from its temperature as if it were on the main sequence, and says so. Known planets
    /// of these stars are not modelled yet: each is built as its star, with the habitat orbit settlers would use.
    /// </summary>
    public sealed class CatalogueStars : SystemSource
    {
        public const string SourceName = "Catalogue";

        /// <summary>The sphere about Sol the catalogue holds: no star is generated within it.</summary>
        public const double RadiusLy = 20;

        /// <summary>The clear space kept about each catalogue star, inside or outside <see cref="RadiusLy"/>.</summary>
        public const double ClearLy = 3;

        public override string Name => SourceName;
        public override int Order => 100;

        static List<StarEntry> stars;

        /// <summary>Every star the catalogue lists, nearest first.</summary>
        public static IReadOnlyList<StarEntry> Stars()
        {
            if (stars != null) return stars;
            var list = new List<StarEntry>();
            foreach (var n in Galaxy.Neighbours())
                list.Add(new StarEntry
                {
                    Name = n.Name, PositionLy = n.PositionLy, Spectral = n.SpectralType, ApparentMagnitude = n.ApparentMagnitude,
                    TeffK = n.TeffK, Source = SourceName, Tag = "neighbour", MassSolar = double.NaN, RadiusSolar = double.NaN,
                    LuminositySolar = double.NaN,
                });
            list.AddRange(Beyond());
            list.Sort((a, b) => a.PositionLy.Length.CompareTo(b.PositionLy.Length));
            return stars = list;
        }

        static StarEntry S(string name, double rh, double rm, double rs, double dd, double dm, double ds, bool south, double ly,
            string type, double mag, double teff, double mass, double radius, double luminosity, string tag = "catalogue") =>
            new StarEntry
            {
                Name = name, PositionLy = Astro.DirectionFromRaDec(Astro.Hms(rh, rm, rs), Astro.Dms(dd, dm, ds, south)) * ly,
                Spectral = type, ApparentMagnitude = mag, TeffK = teff, MassSolar = mass, RadiusSolar = radius, LuminositySolar = luminosity,
                Source = SourceName, Tag = tag,
            };

        /// <summary>The nearest systems past the neighbours, and bright landmarks farther out.</summary>
        static IEnumerable<StarEntry> Beyond()
        {
            yield return S("Kapteyn's Star", 5, 11, 40.58, 45, 1, 6.3, true, 12.83, "sdM1", 8.853, 3570, 0.274, 0.291, 0.012);
            yield return S("Lacaille 8760", 21, 17, 15.27, 38, 52, 2.5, true, 12.95, "M0Ve", 6.67, 3800, 0.60, 0.51, 0.072);
            yield return S("Kruger 60", 22, 27, 59.47, 57, 41, 45.1, false, 13.07, "M3V", 9.59, 3180, 0.271, 0.35, 0.010);
            yield return S("Van Maanen's Star", 0, 49, 9.90, 5, 23, 19.0, false, 14.07, "DZ7", 12.37, 6220, 0.68, 0.011, 0.00017);
            yield return S("Gliese 876", 22, 53, 16.73, 14, 15, 49.3, true, 15.24, "M4V", 10.19, 3473, 0.37, 0.376, 0.0122);
            yield return S("40 Eridani", 4, 15, 16.32, 7, 39, 10.3, true, 16.26, "K0.5V", 4.43, 5072, 0.78, 0.81, 0.46);
            yield return S("70 Ophiuchi", 18, 5, 27.29, 2, 30, 0.4, false, 16.71, "K0V", 4.03, 5300, 0.89, 0.91, 0.60);
            yield return S("Altair", 19, 50, 47.00, 8, 52, 6.0, false, 16.73, "A7V", 0.76, 7550, 1.86, 1.79, 10.6);
            yield return S("Sigma Draconis", 19, 32, 21.59, 69, 39, 40.2, false, 18.80, "G9V", 4.67, 5297, 0.87, 0.78, 0.43);
            yield return S("Eta Cassiopeiae", 0, 49, 6.29, 57, 48, 54.7, false, 19.33, "F9V", 3.44, 5973, 0.97, 1.04, 1.23);
            yield return S("82 Eridani", 3, 19, 55.65, 43, 4, 11.2, true, 19.71, "G8V", 4.25, 5401, 0.85, 0.92, 0.69);
            yield return S("Delta Pavonis", 20, 8, 43.61, 66, 10, 55.4, true, 19.89, "G8IV", 3.56, 5604, 0.99, 1.22, 1.22);
            // Landmarks past the catalogue's sphere: the procedural galaxy keeps clear of them.
            yield return S("Vega", 18, 36, 56.34, 38, 47, 1.3, false, 25.04, "A0V", 0.03, 9602, 2.14, 2.5, 40.1, "landmark");
            yield return S("Fomalhaut", 22, 57, 39.05, 29, 37, 20.1, true, 25.13, "A3V", 1.16, 8590, 1.92, 1.84, 16.6, "landmark");
            yield return S("Pollux", 7, 45, 18.95, 28, 1, 34.3, false, 33.78, "K0III", 1.14, 4586, 1.91, 9.06, 32.7, "landmark");
            yield return S("Arcturus", 14, 15, 39.67, 19, 10, 56.7, false, 36.7, "K1.5III", -0.05, 4286, 1.08, 25.4, 170, "landmark");
        }

        public override bool Claims(DVec3 ly)
        {
            if (ly.Length < RadiusLy) return true;
            foreach (var s in Stars())
                if ((s.PositionLy - ly).Length < ClearLy) return true;
            return false;
        }

        public override IEnumerable<StarEntry> StarsNear(DVec3 centreLy, double radiusLy, int seed)
        {
            foreach (var s in Stars())
                if ((s.PositionLy - centreLy).Length <= radiusLy) yield return Copy(s);
        }

        static StarEntry Copy(StarEntry e) => new StarEntry
        {
            Name = e.Name, PositionLy = e.PositionLy, Spectral = e.Spectral, MassSolar = e.MassSolar, RadiusSolar = e.RadiusSolar,
            LuminositySolar = e.LuminositySolar, TeffK = e.TeffK, ApparentMagnitude = e.ApparentMagnitude, Source = e.Source, Tag = e.Tag,
        };

        public override SystemSpec Define(StarEntry star, int seed)
        {
            StarEntry row = null;
            foreach (var s in Stars())
                if (string.Equals(s.Name, star.Name, StringComparison.OrdinalIgnoreCase)) row = s;
            if (row == null) return null;
            bool estimated = double.IsNaN(row.MassSolar);
            double mass = estimated ? SystemSources.MainSequenceMass(row.TeffK) : row.MassSolar;
            double radius = estimated ? SystemSources.MainSequenceRadius(mass) : row.RadiusSolar;
            double luminosity = estimated ? FrontierSystems.LuminosityOf(radius, row.TeffK) : row.LuminositySolar;
            var spec = new StarSpec
            {
                Name = row.Name, Spectral = row.Spectral, MassSolar = mass, RadiusSolar = radius, LuminositySolar = luminosity, TeffK = row.TeffK,
                RotationDays = row.TeffK > 7000 ? 1 : 30,
                Summary = $"{row.Name}, a real star {Ly(row)} light-years from the Sun ({row.Spectral})." +
                          (estimated ? " Mass, radius and luminosity estimated from its temperature as a main-sequence star." : "") +
                          " Rotation assumed; any known planets are not modelled yet.",
            };
            return SystemSources.LoneStar(row, spec, row.Tag == "landmark"
                ? $"{row.Name}, one of the bright stars of Earth's sky, {Ly(row)} light-years out."
                : $"A real star {Ly(row)} light-years from the Sun, empty until a gate or a ship reaches it.");
        }

        static string Ly(StarEntry e) => e.PositionLy.Length.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
