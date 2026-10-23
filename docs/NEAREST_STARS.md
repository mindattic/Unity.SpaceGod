# Nearest stars to 100 ly (U.SPC-T454)

The owner's spec for U.SPC-T454, from the *Stargazer's Guide to the Solar Neighborhood (0–100 ly)* (22 Oct 2026, Ryan DeBraal). Every star's position, motion, type and magnitude is in [nearest_stars_2027.csv](nearest_stars_2027.csv); this page holds the rest: the frame, the rules, each system's planets and constellation, the state of the game and the plan for stellar motion.

## What the ticket asks

- Put the 123 notable star systems within 100 ly below into the game as real catalogue stars (`CatalogueStars` rows, or the system sources that replace them), at the positions in the CSV. The 85 marked `in_spacegod = no` are missing today; the rest are already there and take the CSV's position (see the Sirius fix below).
- **Void stations every 7 ly.** Wherever a stretch of 7 ly would otherwise have no destination, place a void station (`StarSystem.CreateVoidBase`), so no leg between charted destinations is longer than 7 ly.
- Canon (U.SPC-T455): no jump is possible inside a solar system's gravity wells; a ship gets beyond a system's outer rim to jump, and the jump gates stand at that edge too.

## Coordinate system

123 notable systems within 100 ly, grouped in 7-ly shells, each with a Sol-centric address: X, Y, Z in light-years, Sun at the origin, for **1 January 2027**. It is the frame SpaceGod uses for `StarEntry.PositionLy`, so the values drop straight in. About 14,000 systems lie within 100 ly; most are faint red dwarfs too dim to list one by one.

- **Origin**: the Sun (Sol's root in SpaceGod).
- **Axes**: J2000 ecliptic, fixed in space. +X toward the March equinox (ecliptic longitude 0°), +Y toward ecliptic longitude 90°, +Z toward the north ecliptic pole. Obliquity 23.4392911°, as `Astro.ObliquityDeg`.
- **R (ly)**: distance from the Sun, √(X² + Y² + Z²).
- **Epoch**: 1 January 2027. Each star was moved from its J2000.0 catalogue position along its own proper motion and radial velocity. The axes do not move; only the stars do.
- **Motion**: stars keep drifting after 2027. The fastest nearby star (Barnard's Star) moves about 10″ a year across the sky and closes on the Sun at 111 km/s. Proper motion and radial velocity for every star are in the CSV.
- **Exoplanets**: planet letters, confirmed unless marked. HZ = in the habitable zone.
- **Mag**: apparent visual magnitude from Earth (lower is brighter); blank = too faint in visible light.

Example: Sirius is at X = −1.61, Y = 6.43, Z = −5.48 ly, R = 8.60 ly: slightly toward ecliptic longitude 180°, mostly toward 90°, south of the ecliptic plane.

### CSV columns

`x_ly, y_ly, z_ly` are the Sol-centric J2000 ecliptic position at 1 January 2027, so `PositionLy = new DVec3(x_ly, y_ly, z_ly)`; equivalently `Astro.DirectionFromRaDec(ra_deg, dec_deg) * dist_ly` from the equatorial `ra_deg`/`dec_deg`. `pmra_mas_yr` (already multiplied by cos Dec), `pmdec_mas_yr` and `rv_km_s` give each star's motion for propagating to any other date; `rv_km_s = 0.0` means not measured. `in_spacegod` marks what the game has: `hand-built`, `yes`, or `no` (the 85 it still lacks).

## The systems, by 7-ly division

### Division 1: 0–7 ly (3 systems)

Three systems; two have planets. Alpha Centauri and Luhman 16 sit well south of the ecliptic (Z < −2.9 ly).

| System | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- |
| Alpha Centauri (incl. Proxima) | Closest system | Proxima b (HZ), d; Proxima c disputed; Alpha Cen A b candidate (JWST, 2025) | Centaurus |
| Barnard's Star | Fastest proper motion of any star | b, c, d, e | Ophiuchus |
| Luhman 16 | Infrared only | None known | Vela |

### Division 2: 7–14 ly (26 systems)

The richest shell for planets: 14 of 26 systems have confirmed or suspected worlds, six with a habitable-zone planet.

| System | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- |
| WISE 0855−0714 | Coldest known; JWST target | None known | Hydra |
| Wolf 359 | Flare star | b, c candidates (unconfirmed) | Leo |
| Lalande 21185 | | b, c; +1 candidate | Ursa Major |
| Sirius | Brightest star in the night sky | None known | Canis Major |
| Luyten 726-8 (UV/BL Ceti) | Prototype flare star UV Ceti | None known | Cetus |
| Ross 154 | Flare star | None known | Sagittarius |
| Ross 248 | Will be the nearest star in ~36,000 years | None known | Andromeda |
| Epsilon Eridani | Debris disk | b (Ægir) | Eridanus |
| Lacaille 9352 (Gliese 887) | | b, c, d (HZ), e; +2 candidates | Piscis Austrinus |
| Ross 128 | | b (HZ) | Virgo |
| EZ Aquarii | | None known | Aquarius |
| 61 Cygni | First star with a measured parallax (1838) | None known | Cygnus |
| Procyon | | None known | Canis Minor |
| Struve 2398 (Gliese 725) | | A b; B c (HZ) | Draco |
| Groombridge 34 | | A b, A c | Andromeda |
| DX Cancri | | None known | Cancer |
| Epsilon Indi | | A b (directly imaged) | Indus |
| Tau Ceti | Nearest single Sun-like star | e, f, g, h candidates (unconfirmed) | Cetus |
| GJ 1061 | | b, c (HZ), d (HZ) | Horologium |
| YZ Ceti | | b, c, d | Cetus |
| Luyten's Star | | b (HZ), c; +2 candidates | Canis Minor |
| Teegarden's Star | | b (HZ), c (HZ), d | Aries |
| Kapteyn's Star | Retrograde galactic orbit | b, c disputed (likely stellar activity) | Pictor |
| Lacaille 8760 (AX Mic) | Brightest red dwarf | None known | Microscopium |
| Kruger 60 | | None known | Cepheus |
| Ross 614 | | None known | Monoceros |

### Division 3: 14–21 ly (22 systems)

Eight systems carry planets, including Gliese 876's four-planet resonant chain and 82 Eridani's habitable-zone d.

| System | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- |
| Wolf 1061 (Gliese 628) | | b, c (HZ), d | Ophiuchus |
| Van Maanen's Star (Gliese 35) | Nearest solitary white dwarf | None known (planetary debris on its surface) | Pisces |
| Gliese 1 | | None known | Sculptor |
| Gliese 687 | | b, c | Draco |
| Gliese 674 | | b | Ara |
| Gliese 876 | | b, c, d, e | Aquarius |
| Gliese 412 (WX UMa) | | None known | Ursa Major |
| Gliese 832 | | b; c refuted | Grus |
| AD Leonis | Flare star | None (b refuted) | Leo |
| 40 Eridani (Omicron² Eri) | Easiest white dwarf to see; Vulcan's star | None (b refuted, 2023) | Eridanus |
| EV Lacertae | Superflare star | None known | Lacerta |
| 70 Ophiuchi | Fine double for small scopes | None known | Ophiuchus |
| Altair | Summer Triangle star | None known | Aquila |
| Gliese 229 | First confirmed brown-dwarf companion | A b, A c candidates (unconfirmed) | Lepus |
| Sigma Draconis | | None known | Draco |
| Gliese 570 | Quadruple system | None known | Libra |
| Eta Cassiopeiae | Gold-and-purple double | None known | Cassiopeia |
| 36 Ophiuchi | | None known | Ophiuchus |
| Gliese 783 (HR 7703) | | None known | Sagittarius |
| 82 Eridani | | b, c, d (HZ) | Eridanus |
| Delta Pavonis | Sun-like, older | None known | Pavo |
| Gliese 581 | | b, c, e (d, f, g refuted) | Libra |

### Division 4: 21–28 ly (10 systems)

Gliese 667 C and 61 Virginis host planets; Vega has an unconfirmed candidate. Beta Hydri sits farthest below the ecliptic (Z = −22.0 ly).

| System | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- |
| Xi Boötis | Yellow-orange double | None known | Boötes |
| Gliese 667 | | C b, C c (HZ); 5 doubtful candidates | Scorpius |
| Beta Hydri | Preview of our Sun's future | None known | Hydrus |
| 107 Piscium | | None known | Pisces |
| Vega | Debris disk; former pole star | Hot-Neptune candidate (unconfirmed) | Lyra |
| Fomalhaut | Famous dust ring | None (Fomalhaut b was a dust cloud) | Piscis Austrinus |
| Pi³ Orionis | | None known | Orion |
| Mu Herculis | | None known | Hercules |
| Chara (Beta CVn) | | None known | Canes Venatici |
| 61 Virginis | | b, c, d | Virgo |

### Division 5: 28–35 ly (14 systems)

Six red-dwarf planet systems: GJ 367, Gliese 357, Gliese 176, Wolf 1069, AU Microscopii and Gliese 436. Pollux is the nearest giant star, with one planet.

| System | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- |
| Zeta Tucanae | | None known | Tucana |
| Gliese 250 | | B b candidate | Monoceros |
| Delta Eridani | | None known | Eridanus |
| Beta Comae Berenices | | None known | Coma Berenices |
| Kappa¹ Ceti | Model of the young Sun | None known | Cetus |
| Gamma Pavonis | High-velocity star | None known | Pavo |
| GJ 367 | | b (Tahay), c, d | Vela |
| Gliese 357 | | b, c, d (HZ) | Hydra |
| Gliese 176 | | b; 1 disputed candidate | Taurus |
| Wolf 1069 | | b (HZ) | Cygnus |
| AU Microscopii | Edge-on debris disk | b, c; +2 candidates | Microscopium |
| Gliese 436 | | b (Awohali) | Leo |
| Pollux | Nearest giant star | b (Thestias) | Gemini |
| Zeta Herculis | Tight double | None known | Hercules |

### Divisions 6–7: 35–49 ly (18 systems)

Home of the famous planet systems: TRAPPIST-1's seven Earth-size worlds, 55 Cancri's lava planet e, and Upsilon Andromedae, the first multi-planet system found around a Sun-like star.

| System | Division | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- | --- |
| Gliese 86 | 6 | | A b (hot Jupiter) | Eridanus |
| Denebola | 6 | Tail of the Lion | None known | Leo |
| 54 Piscium | 6 | | b | Pisces |
| HD 85512 | 6 | Early habitable-zone candidate | b (HZ edge) | Vela |
| Arcturus | 6 | Brightest star north of the celestial equator | None known | Boötes |
| Porrima (Gamma Vir) | 6 | Fast-closing double | None known | Virgo |
| Deneb Algedi (Delta Cap) | 6 | | None known | Capricornus |
| Zeta Reticuli | 6 | Naked-eye Sun twins | None known | Reticulum |
| TRAPPIST-1 | 6 | | b, c, d, e (HZ), f (HZ), g (HZ), h | Aquarius |
| 55 Cancri | 6 | | b, c, d, e (Janssen), f | Cancer |
| HD 40307 | 7 | | b, c, d; e, f, g candidates | Pictor |
| Capella | 7 | Quadruple system | None known | Auriga |
| Upsilon Andromedae | 7 | First multi-planet system around a Sun-like star | b, c, d; e disputed | Andromeda |
| Errai (Gamma Cephei) | 7 | Future pole star | A b (Tadmor) | Cepheus |
| 47 Ursae Majoris | 7 | | b, c, d | Ursa Major |
| Gliese 1214 | 7 | | b | Ophiuchus |
| Rasalhague (Alpha Oph) | 7 | | None known | Ophiuchus |
| Alderamin (Alpha Cep) | 7 | | None known | Cepheus |

### Divisions 8–10: 49–70 ly (14 systems)

51 Pegasi b (1995) was the first planet found around a Sun-like star; Beta Pictoris b and c were photographed directly.

| System | Division | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- | --- |
| 51 Pegasi | 8 | First exoplanet around a Sun-like star (1995) | b (Dimidium) | Pegasus |
| Mu Arae | 8 | | b, c, d, e | Ara |
| Castor | 8 | Sextuple (3 binaries) | None known | Gemini |
| Tau Boötis | 8 | | A b (hot Jupiter) | Boötes |
| Gliese 777 (HD 190360) | 8 | | b, c | Cygnus |
| Caph (Beta Cas) | 8 | | None known | Cassiopeia |
| HD 10647 (q¹ Eridani) | 9 | Debris disk | b | Eridanus |
| Zosma (Delta Leo) | 9 | | None known | Leo |
| Menkent (Theta Cen) | 9 | | None known | Centaurus |
| Mothallah (Alpha Tri) | 10 | | None known | Triangulum |
| Beta Pictoris | 10 | Edge-on debris disk | b, c (directly imaged) | Pictor |
| HD 189733 | 10 | Near the Dumbbell Nebula (M27) | b | Vulpecula |
| Aldebaran | 10 | Eye of the Bull | b candidate (disputed) | Taurus |
| Hamal (Alpha Ari) | 10 | | b (reported 2011) | Aries |

### Divisions 11–14: 70–100 ly (16 systems)

Past 70 ly the list is bright naked-eye landmarks. Only Alioth has a planet candidate. The Big Dipper stars (Merak, Megrez, Alioth, Mizar, Phecda) form a tight group far above the ecliptic (Z = 56–69 ly).

| System | Division | Notes | Exoplanets | Constellation |
| --- | --- | --- | --- | --- |
| Unukalhai (Alpha Ser) | 11 | | None known | Serpens |
| Alphecca (Alpha CrB) | 11 | Jewel of the Northern Crown | None known | Corona Borealis |
| Zubenelgenubi (Alpha Lib) | 11 | | None known | Libra |
| Regulus | 12 | Egg-shaped from rapid spin | None known | Leo |
| Merak (Beta UMa) | 12 | Pointer star to Polaris | None known | Ursa Major |
| Megrez (Delta UMa) | 12 | | None known | Ursa Major |
| Delta Velorum | 12 | Part of the False Cross | None known | Vela |
| Alioth (Epsilon UMa) | 12 | Brightest Dipper star | Candidate (2004, unconfirmed) | Ursa Major |
| Mizar & Alcor | 12 | Classic double in the Dipper's handle | None known | Ursa Major |
| Phecda (Gamma UMa) | 12 | | None known | Ursa Major |
| Ankaa (Alpha Phe) | 13 | | None known | Phoenix |
| Gacrux (Gamma Cru) | 13 | Top of the Southern Cross; nearest M giant | None known | Crux |
| Algol (Beta Per) | 13 | Dims every 2.87 days | None known | Perseus |
| Eta Draconis | 14 | | None known | Draco |
| Diphda (Beta Cet) | 14 | Brightest star in Cetus | None known | Cetus |
| Ruchbah (Delta Cas) | 14 | Part of the Cassiopeia 'W' | None known | Cassiopeia |

## SpaceGod today

SpaceGod can hold every star here without strain, but its stars never move: positions are set once from J2000 RA/Dec and never tied to the sim clock.

| Area | Today | Where |
| --- | --- | --- |
| Real stars | 38 of these 123: Alpha Centauri hand-built, 21 neighbours, 12 catalogue stars to 20 ly, 4 landmarks (Vega, Fomalhaut, Pollux, Arcturus) | `Galaxy.Neighbours()`, `CatalogueStars.Beyond()` |
| Beyond the catalogue | Procedural stars from the galaxy seed, kept 3 ly clear of every real star | `ProceduralSystems`, `CatalogueStars.Claims` |
| Load | Only stars inside the chart radius (default 20 ly, setting 5–60) are charted; systems inside 3 ly are built | `GalaxyStreaming.ChartRadius`, `SystemRadius` |
| Frame | J2000 ecliptic axes, Sol at the origin; input is J2000 equatorial RA/Dec, converted by `Astro.DirectionFromRaDec` | `StarEntry.PositionLy` |
| Precision | Galactic sector (1e9 km cube, long index) plus a double km offset, good to ~0.1 mm anywhere in the galaxy | `GalacticPosition` |
| Star motion | None: no velocity, no epoch | `StarEntry` |
| Fastest warp | 1 simulated year per real second | `SimClock.Levels` |

- **Frame match.** Every X, Y, Z here is already in SpaceGod's frame (J2000 ecliptic, Sol at the origin, light-years), so it can be assigned straight to `StarEntry.PositionLy`. The values are for 1 January 2027; a star's J2000.0 catalogue position differs by at most 0.08° (Barnard's Star).
- **Data fix.** `Galaxy.cs` gives Sirius 8.7094 ly. Its parallax (379.21 mas) puts it at 8.60 ly, the value used here.

Would stars visibly drift? Only nearby fast movers, and only with a much faster warp. Time to drift ~45° across the sky:

| Star | Own motion | Simulated time for ~45° | Real time at today's top warp (1 yr/s) |
| --- | --- | --- | --- |
| Barnard's Star (fastest known) | 10.4″/yr | ~15,600 yr | ~4.3 hours |
| Kapteyn's Star | 8.7″/yr | ~19,000 yr | ~5.2 hours |
| 61 Cygni | 5.2″/yr | ~31,000 yr | ~8.6 hours |
| Typical bright star | ~0.1″/yr | ~1.5 million yr | ~17 days |

In the game's fixed J2000 frame, Earth's daily spin and the 25,800-year wobble of its axis do not move the stars, which is correct: only each star's own motion should.

## Plan: stellar motion

Give every star a 3D velocity and evaluate its position from the sim clock; the cost is one multiply-add per charted star per update.

1. **Velocity on StarEntry.** Add `DVec3 VelocityLyPerYear` and `DVec3 PositionAt(double jd) => PositionLy + VelocityLyPerYear * ((jd - Astro.J2000) / 365.25)`. `PositionLy` stays the J2000 position.
2. **Catalogue fields.** Each `CatalogueStars` row (and each neighbour) gains proper motion in RA (μα*, mas/yr), proper motion in Dec (μδ, mas/yr) and radial velocity (km/s), from Gaia DR3 or Hipparcos, recorded in `Systems/SOURCES.md`. Convert once to an ecliptic J2000 velocity in ly/yr. Radial velocity makes distances change too (Ross 248 reaches ~3 ly in ~36,000 years).
3. **Procedural velocities.** `ProceduralSystems` draws a seeded velocity from Milky Way disc dispersions (about 20–50 km/s, larger for older stars), so every playthrough of a seed drifts the same way.
4. **Streaming at the current date.** `GalaxyStreaming` charts and builds stars at `PositionAt(clock.Jd)`. `Claims` and `ClearLy` test positions at that date, so a procedural star never drifts into a real one. A built system's root moves with its star; everything inside stays in system-local km.
5. **Settings, registered once.** A "Stellar motion" on/off toggle and deep-time warp levels (100, 1,000 and 10,000 yr/s, galaxy map only), registered like `streaming.chartRadiusLy` and remembered per profile.
6. **Range of validity.** Straight-line motion holds for about 10 million years. Past that, stars follow curved galactic orbits; add a galactic potential then, still cheap at these star counts.
7. **More real stars.** Add the 85 systems the game lacks as `CatalogueStars` rows from the CSV, then move their known exoplanets in once planets are modelled.

## Sources and caveats

- Star positions, proper motions and radial velocities: HYG Database v3.8 (Hipparcos, Yale Bright Star and Gliese catalogues, J2000.0), propagated to 1 January 2027 with Astropy. Barnard's Star's proper motion is from Gaia DR3 (HYG caps it). Luhman 16, WISE 0855−0714, Teegarden's Star and TRAPPIST-1 are not in HYG; their positions and motions come from their discovery papers and Gaia, good to about 1′.
- Distances: modern published parallaxes, mostly Gaia DR3.
- Corrections are in the data itself: Gliese 357's position (about 0.15° off before) is fixed, and Zeta Herculis sits in Division 5 at 34.99 ly.
- Exoplanets within 32.6 ly: *List of nearest exoplanets* (Wikipedia, fetched 22 Oct 2026). **Planets beyond 32.6 ly are from general knowledge: confirm them in the NASA Exoplanet Archive before relying on them.**
- SpaceGod details: `Assets/UnitySpace/Core` (`Astro.cs`, `SimClock.cs`, `GalacticPosition.cs`, `Systems/CatalogueStars.cs`, `Systems/Galaxy.cs`, `Systems/GalaxyStreaming.cs`).
