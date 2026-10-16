# Sources: the nearby star systems, spacecraft and docking rings

What is real, where it comes from, and what is assumed or invented.

## How a star system is built

Every system outside Sol is a catalogue entry (`SystemSpec`, in `AlphaCentauri.cs` and `NearbySystems.cs`) turned into bodies by one builder (`StarSystemBuilder`):

- **Stars:** a single star is the system's root. A pair at the root circles an invisible barycentre (the root). Both members share their measured visual orbit (Campbell elements: P, a, e, i, Ω, ω, periastron date), scaled by −m_B/M and +m_A/M, so they stay on opposite sides of it.
- **Wide companions** (Proxima, the Epsilon Indi B pair) orbit the root through the place they are seen today: from their own sky position and distance, or from a projected separation across the line of sight. That orbit's plane and shape are **assumed** (stated below for each).
- **Planets** keep their catalogued period, semi-major axis and eccentricity exactly (the tests check every one). Mass is the catalogue's mass; for radial-velocity planets that is m sin i, a lower limit, and the HUD says "at least". Radii of planets never seen to transit are the NASA Exoplanet Archive's mass-radius estimates; for candidates the archive does not list, the Chen & Kipping 2017 relation (ApJ 834, 17) is used.
- **Planes and phases:** the inclination to the sky, ω and the time of periastron are used where the catalogue gives them. A node position angle has not been measured for any of these planets, so it is **assumed** (0°). Where the inclination is unknown, the system's reference plane is used: a resolved debris disc's tilt, a published adopted value, or else 60° (the median for randomly oriented orbits), all **assumed** for the planets. Where no phase is known, a fixed one is derived from the planet's name. Around a binary root, planets lie in the binary's plane unless stated otherwise.
- **Looks** (colour, gas-giant shading, tidal locking for planets on orbits under 40 days, rotation where unmeasured) are **assumed**. None of these planets has been seen as more than a point.
- **Habitable zone:** each planet is flagged against its own star's light, using Kopparapu et al. 2013 (ApJ 765, 131, with the corrected coefficients of the 2013 erratum).
  - Conservative zone: the runaway-greenhouse to maximum-greenhouse fluxes, as polynomials in the star's temperature (fitted for 2600–7200 K; cooler and hotter stars are clamped). Example: the Sun's zone is 0.98–1.69 AU.
  - Optimistic zone: recent-Venus and early-Mars fluxes. These are approximated by scaling the conservative edges by their solar ratios (1.7763/1.0385 and 0.3207/0.3507).
  - The flux is averaged over the orbit: L / (a²√(1−e²)). A companion star's light is ignored.
- **Distances** are 1000 / parallax (mas) parsecs, times 3.26156 ly/pc.
- **The game layer:** Sol and Alpha Centauri keep their settlements, fleets and jump gates. The other systems are visitable (Navigator, STAR SYSTEMS) but unsettled: no settlements, lanes or ships. Their two placeholder factions, "Survey Service" and "Unknown", are fiction, and so will be anything settled there later.

## Alpha Centauri A and B

| | A (Rigil Kentaurus) | B (Toliman) |
|---|---|---|
| Spectral type | G2V | K1V |
| Mass | 1.0788 M☉ | 0.9092 M☉ |
| Radius | 1.223 R☉ | 0.864 R☉ |
| Luminosity | 1.5059 L☉ | 0.4981 L☉ |
| Effective temperature | 5748 K | 5154 K |
| Rotation | 28.3 d | 36.7 d |

- **A–B relative orbit** (Akeson et al. 2021, AJ 162, 14, arXiv:2104.10086):
  - P = 79.762 yr
  - a = 17.493″ = 23.299 AU at 1.3319 pc
  - e = 0.51947
  - i = 79.243°, Ω = 205.073°, ω = 231.519°
  - periastron 1875.66

  The orbit is built in the sky frame at A's position (Thiele–Innes geometry) and turned into the J2000 ecliptic. The two stars share it, scaled by −m_B/M and +m_A/M about their barycentre. The model gives separations of 11.20–35.40 AU and the next periastron in 2035.
- **Position:** RA 14h39m36.494s, Dec −60°50′02.37″ (ICRS), 4.344 ly.
- **Not modelled:** the stars' spin poles (assumed along the A–B orbit normal) and the 2012 planet "Alpha Cen Bb" (refuted, so B has no planets here).

## Proxima Centauri

- **Star:** M5.5Ve flare star; 0.1221 M☉, 0.1542 R☉, 0.001567 L☉, 2992 K, rotation 84.9 d.
- **Position:** RA 14h29m42.946s, Dec −62°40′46.16″, 4.2465 ly.
- **Orbit about A–B** (Kervella, Thévenin & Lovis 2017, A&A 598, L7, arXiv:1611.03495): P ≈ 550 kyr, e ≈ 0.50, near apastron now.
  - The model places Proxima where its sky position and distance put it today (≈12,050 AU from A–B), at mean anomaly 174°.
  - With e = 0.5 this gives a ≈ 8,000 AU and P ≈ 500 kyr.
  - The plane's orientation is **assumed**: the A–B plane, tipped to contain today's offset.
- **Flares** are drawn as random brightenings; their timing is not real data.

## Planets

| Body | Status | Data | Assumed |
|---|---|---|---|
| Proxima d | Confirmed (NASA Exoplanet Archive; Suárez Mascareño et al. 2025) | 0.02881 AU, 5.12338 d, e 0, ≥ 0.26 M⊕ | radius 0.692 R⊕ (archive estimate), plane, phase |
| Proxima b | Confirmed (NASA Exoplanet Archive; Suárez Mascareño et al. 2025; found by Anglada-Escudé et al. 2016) | 0.04848 AU, 11.18465 d, e 0, ≥ 1.055 M⊕ | radius 1.02 R⊕ (archive estimate), thin atmosphere, tidal lock, plane, phase |
| Proxima c | **Disputed** (Damasso et al. 2020) | 1.489 AU, 1928 d, ~7 M⊕, e 0.04 | radius 1.6 R⊕, plane, phase |
| Alpha Cen Ab ("S1") | **Candidate** (Beichman et al. 2025, ApJL; JWST/MIRI, August 2024) | 90–150 M⊕, ~1.0–1.1 R_J, a ~1–2 AU, e ~0.4, inclined | one allowed orbit: a 1.9 AU, e 0.4, tilted 50° from the A–B plane |

- Planets' orbital planes are assumed to be the A–B orbit plane, except where the table says otherwise.
- The habitable zones (A ~1.2 AU, B 0.5–0.9 AU) are where the invented stations sit.
- Other references: Wikipedia "Alpha Centauri" and "Proxima Centauri"; AAS Nova, "Potential discovery of a world next door" (11 August 2025).

## The Sun seen from Alpha Centauri

- The Sun shines at about magnitude 0.5 in Cassiopeia, at RA 2h39.6m, Dec +60°50′, which is the antipode of Alpha Centauri's position.
- The model reproduces this from the two systems' real positions.

## Nearby stars (interstellar view)

- **Source:** Wikipedia "List of nearest stars" (from RECONS and Gaia DR3).
- **Data used:** name, RA, Dec, distance (ly), spectral type and visual magnitude for the 21 systems out to GJ 1061 at 11.98 ly.
- The ten of those that are now simulated systems (below) are drawn from their own catalogue entries instead. The other eleven stay plain stars.
- **Colours:** from a temperature assigned to each spectral type (approximate).
- **Glow sizes:** from absolute magnitude.

## The nearest systems beyond Alpha Centauri

**Common sources:**

- **Stars:** position (ICRS J2000), parallax, spectral type, mass, radius, temperature, luminosity and rotation come from each star's Wikipedia article (Gaia DR3 and the papers it cites; fetched 2026-10-16). Where an article gives no luminosity, the NASA Exoplanet Archive's st_lum is used (marked "archive").
- **Planets:** the NASA Exoplanet Archive composite table `pscomppars` (TAP query of 2026-10-16) gives pl_orbper, pl_orbsmax, pl_orbeccen, pl_bmasse (m sin i where the provenance is Msini), pl_rade (estimated unless transiting), pl_orblper, pl_orbtper and pl_orbincl. The reference named is the archive's default parameter set.
- **Candidates:** planets the archive does not list come from their discovery papers. Tau Ceti's archive-listed planets come from a single team's detection that nobody has confirmed. Both kinds are flagged **CANDIDATE** in the game.

### Barnard's Star (5.96 ly)

- **Star:** M4.0V. RA 17h57m48.498s, Dec +4°41′36.11″, parallax 546.976 mas. 0.162 M☉, 0.187 R☉, 3195 K, L 0.00355 L☉ (archive), rotation 114.06 d.
- **Planets** (archive; Basant et al. 2025; b found by González Hernández et al. 2024), all confirmed:

  | Planet | P (d) | a (AU) | e | m sin i (M⊕) | R est. (R⊕) | ω |
  |---|---|---|---|---|---|---|
  | d | 2.3402 | 0.0188 | 0.04 | 0.263 | 0.694 | −51.8° |
  | b | 3.1542 | 0.0229 | 0.03 | 0.299 | 0.72 | 3.8° |
  | c | 4.1244 | 0.0274 | 0.08 | 0.335 | 0.743 | 90.8° |
  | e | 6.7392 | 0.0381 | 0.04 | 0.193 | 0.637 | −27.5° |

- **Assumed:** the plane (i 60°, node 0°) and the phases.

### Luhman 16 (6.51 ly)

- **Position:** RA 10h49m18.771s, Dec −53°19′09.88″ (J2016), parallax 500.993 mas.
- **A:** L7.5, 35.4 M_J, 0.102 R☉, 1305 K, 2.2×10⁻⁵ L☉, rotation 6.94 h.
- **B:** T0.5, 29.4 M_J, 0.102 R☉, 1320 K, 2.1×10⁻⁵ L☉, rotation 5.28 h.
- **Orbit of B about A** (Bedin et al. 2024):
  - P = 26.55 yr
  - a = 1.764″ = 3.52 AU
  - e = 0.344
  - i = 79.92°, Ω = 130.02°, ω = 136.67°
  - periastron 2018.060

  The archive lists B as the planet "Luhman 16 b" (P 9697 d, a 3.52 AU, e 0.344, 9344 M⊕). Here it is a brown dwarf.
- **Assumed:** the spin poles (along the orbit normal).

### Wolf 359 (7.86 ly)

- **Star:** M6V flare star. RA 10h56m28.921s, Dec +7°00′53.00″, parallax 415.179 mas. 0.110 M☉, 0.144 R☉, 2749 K, 0.00106 L☉, rotation 2.704 d.
- **b, CANDIDATE** (Tuomi et al. 2019, arXiv:1906.04644; not in the archive): P 2938 d, a 1.845 AU, e 0.04, m sin i 43.9 M⊕. Radius 7.5 R⊕ (Chen & Kipping).
- **"c" (2.69 d) left out:** CARMENES data showed it was the star's rotation.
- **Assumed:** plane and phase.

### Lalande 21185 (8.30 ly)

- **Star:** M2V. RA 11h03m20.195s, Dec +35°58′11.58″, parallax 392.753 mas. 0.389 M☉, 0.392 R☉, 3547 K, L 0.0195 L☉ (archive), rotation 53.33 d.
- **Planets:**

  | Planet | Status (source) | P (d) | a (AU) | e | m sin i (M⊕) | R (R⊕) | Other |
  |---|---|---|---|---|---|---|---|
  | b | Confirmed (archive, as GJ 411 b; Hurt et al. 2022) | 12.9394 | 0.07879 | 0.063 | 2.69 | 1.45 est. | ω −30°, T_p JD 2456307.8 |
  | c | Confirmed (archive, as HD 95735 c; Hurt et al. 2022) | 2946 | 2.94 | 0.132 | 13.6 | 3.76 est. | ω 63°, T_p JD 2457250 |
  | d | **CANDIDATE** (Hurt et al. 2022) | 215.7 | 0.5142 | 0.18 | 3.89 | 1.80 (Chen & Kipping) | |

- **Assumed:** planes.

### Sirius (8.61 ly)

- **Position:** RA 06h45m08.917s, Dec −16°42′58.02″, parallax 378.9 mas.
- **A:** A0mA1 Va, 2.063 M☉, 1.7144 R☉, 24.74 L☉, 9845 K. Its rotation is not measured: 5.3 d is taken from v sin i = 16.3 km/s, assuming we see the equator.
- **B:** DA2 white dwarf, 1.018 M☉, 0.008098 R☉, 0.02448 L☉, 25,000 K. Rotation **assumed** (1 d).
- **Orbit** (Bond et al. 2017, ApJ 840, 70):
  - P = 50.1284 yr
  - a = 7.4957″ = 19.78 AU
  - e = 0.59142
  - i = 136.336°, Ω = 45.400°, ω = 149.161°
  - periastron 1994.5715

  This gives separations of 8.1–31.5 AU.
- No planets are known.

### Epsilon Eridani (10.47 ly)

- **Star:** K2V. RA 03h32m55.845s, Dec −9°27′29.73″, parallax 311.37 mas. 0.82 M☉, 0.742 R☉, 0.326 L☉, 5085 K, rotation 11.60 d.
- **b (AEgir), confirmed** (archive; Thompson et al. 2025):
  - P 2680 d, a 3.53 AU, e 0.06
  - mass 317.83 M⊕ (a true mass, from astrometry)
  - i 40°, ω 268°, T_p JD 2458017.5
  - radius 13.8 R⊕ (archive estimate)
- **Debris belts:**
  - Inner belt, 1.5–2.0 AU (Su et al. 2017; the Spitzer model of Backman et al. 2009 puts it at 3 AU instead).
  - Outer belt, 8–20 AU (same sources).
  - Main belt, 65–75 AU (ALMA: MacGregor et al. 2015, Booth et al. 2017). It is inclined 33.7° to the sky (Booth et al. 2023).
- **Assumed:** the belts' node, and the inner belts' plane (taken to be the main belt's).

### Ross 128 (11.01 ly)

- **Star:** M4V. RA 11h47m44.397s, Dec +0°48′16.40″, parallax 296.305 mas. 0.176 M☉, 0.198 R☉, 3189 K, L 0.00362 L☉ (archive). Rotation is measured only as 101–223 d; the midpoint, 162 d, is used.
- **b, confirmed** (archive; Bonfils et al. 2018): P 9.8658 d, a 0.0496 AU, e 0.116, m sin i 1.40 M⊕, radius 1.11 R⊕ (estimate).
- **Assumed:** plane and phase.

### Epsilon Indi (11.87 ly)

- **A:** K5Ve. RA 22h03m21.654s, Dec −56°47′09.52″, parallax 274.843 mas. 0.782 M☉, 0.713 R☉, 0.21 L☉, 4649 K, rotation 35.732 d.
- **Ab, confirmed** (archive; Sanghi et al. 2026; imaged by JWST in 2024):
  - P 25,450 d, a 15.76 AU, e 0.25
  - mass 2065.88 M⊕ (6.5 M_J)
  - i 102.18°, ω −28.13°, T_p JD 2441420.76
  - radius 12.7 R⊕ (archive)
- **Ba:** T1, 66.92 M_J, 0.0805 R☉, 1312 K, 2.04×10⁻⁵ L☉.
- **Bb:** T6, 53.25 M_J, 0.0825 R☉, 972 K, 5.97×10⁻⁶ L☉.
- **Orbit of Bb about Ba** (Chen et al. 2022):
  - P = 11.0197 yr
  - a = 661.58 mas = 2.4058 AU
  - e = 0.54042
  - i = 77.082°, Ω = 147.959°, ω = 328.27°
- **The B pair:** about 1,500 AU (projected) from A (Scholz et al. 2003).
- **Assumed:** the brown dwarfs' rotation (2 h) and the pair's periastron date (2000.0); the B pair's direction (position angle 0°, across the line of sight) and its circular orbit about A; Ab's node.

### Tau Ceti (11.91 ly)

- **Star:** G8V. RA 01h44m04.083s, Dec −15°56′14.93″, parallax 273.810 mas. 0.800 M☉, 0.793 R☉, 0.488 L☉, 5320 K, rotation 46 d.
- **Planets: all CANDIDATES.** Feng et al. 2017 reported them. The archive lists them; Wikipedia, and these tables, mark them unconfirmed.

  | Planet | P (d) | a (AU) | e | m sin i (M⊕) | R est. (R⊕) | ω |
  |---|---|---|---|---|---|---|
  | g | 20.00 | 0.133 | 0.06 | 1.75 | 1.18 | 35.34° (archive 395.34°) |
  | h | 49.41 | 0.243 | 0.23 | 1.83 | 1.19 | 7.45° |
  | f | 636.13 | 1.334 | 0.16 | 3.93 | 1.81 | 119.75° |

  Feng's fourth signal, "e" (163 d), is no longer listed by the archive or Wikipedia and is left out.
- **Debris disc:** 6.2–52 AU, inclined 35° (MacGregor et al. 2016, ALMA).
- **Assumed:** the planets share the disc's plane; node assumed.

### GJ 1061 (11.98 ly)

- **Star:** M5.5V. RA 03h35m59.699s, Dec −44°30′45.73″, parallax 272.162 mas. 0.125 M☉, 0.152 R☉, 2977 K, L 0.00170 L☉ (archive), rotation ~125 d.
- **Planets** (archive; Dreizler et al. 2025; found 2020), all confirmed. Masses are the archive's m sin i:

  | Planet | P (d) | a (AU) | e | m sin i (M⊕) | R est. (R⊕) | ω |
  |---|---|---|---|---|---|---|
  | b | 3.2073 | 0.0210 | 0.05 | 1.37 | 1.04 | 145° |
  | c | 6.6821 | 0.0342 | 0.02 | 1.74 | 1.19 | 88° |
  | d | 13.066 | 0.054 | 0.04 | 1.64 | 1.16 | 157° |

- **Plane:** inclination ~77°, the value adopted for all three (Wikipedia). The node is assumed.

### Luyten's Star (12.35 ly)

- **Star:** GJ 273, M3.5V. RA 07h27m24.499s, Dec +5°13′32.84″, parallax 264.127 mas. 0.29 M☉, 0.293 R☉, 0.0088 L☉, 3382 K, rotation 115.6 d.
- **Planets:**

  | Planet | Status (source) | P (d) | a (AU) | e | m sin i (M⊕) | R (R⊕) |
  |---|---|---|---|---|---|---|
  | c (GJ 273 c) | Confirmed (archive; Astudillo-Defru et al. 2017) | 4.7234 | 0.036467 | 0.17 | 1.18 | 1.06 est. |
  | b (GJ 273 b) | Confirmed (archive; Astudillo-Defru et al. 2017) | 18.6498 | 0.091101 | 0.10 | 2.89 | 1.51 est. |
  | d | **CANDIDATE** (Tuomi et al. 2019) | 413.9 | 0.712 | 0.17 | 10.8 | 3.28 (Chen & Kipping) |
  | e | **CANDIDATE** (Tuomi et al. 2019) | 542 | 0.849 | 0.03 | 9.3 | 3.01 (Chen & Kipping) |

- **Assumed:** planes and phases.

### Teegarden's Star (12.50 ly)

- **Star:** M7.0V. RA 02h53m00.892s, Dec +16°52′52.63″, parallax 260.988 mas. 0.097 M☉, 0.120 R☉, 0.000722 L☉, 3034 K, rotation 98.05 d.
- **Planets** (archive; Dreizler et al. 2024), all confirmed:

  | Planet | Found | P (d) | a (AU) | e | m sin i (M⊕) | R est. (R⊕) | ω |
  |---|---|---|---|---|---|---|---|
  | b | 2019 | 4.90634 | 0.0259 | 0.03 | 1.16 | 1.05 | 338° |
  | c | 2019 | 11.416 | 0.0455 | 0.04 | 1.05 | 1.02 | 301° |
  | d | 2024 | 26.13 | 0.0791 | 0.07 | 0.82 | 0.954 | 345° |

- **Assumed:** plane.

## Real spacecraft (Sol)

Orbits are circular, at the published altitude and inclination. Where each craft sits along its orbit is **illustrative**: there are no live orbital elements.

- **Crewed stations:**
  - ISS: ~420 km, 51.64°
  - Tiangong: ~385 km, 41.5°
- **Telescopes:**
  - Hubble: ~530 km, 28.5°
  - JWST: at Sun–Earth L2, 1.5 million km anti-sunward (its halo orbit is not drawn)
- **Robotic orbiters:**
  - Mars Reconnaissance Orbiter: ~300 km polar
  - Mars Odyssey: ~400 km polar
  - MAVEN: 150 × 6,200 km at 75°, drawn circular
  - Mars Express: ~300 × 10,000 km polar, drawn circular
  - ExoMars TGO: ~400 km, 74°
  - Lunar Reconnaissance Orbiter: polar, drawn at 100 km
- **Constellations** (Walker-pattern shells):
  - Starlink: shells at 550 km/53°, 540 km/53.2°, 570 km/70°, 560 km/97.6° and 530 km/43°
  - GPS: 6 planes, 55°, a = 26,560 km
  - Galileo: 3 planes, 56°, a = 29,600 km
  - GLONASS: 3 planes, 64.8°, a = 25,510 km
  - BeiDou MEO: 3 planes, 55°, a = 27,900 km
  - Geostationary belt: 42,164 km

  Plane counts and altitudes are real. Satellite counts per plane are rounded.

## Docking rings (fiction on real geometry)

- **Earth:** one ring hovers directly over each of New York (40.7128 N, 74.0060 W), Paris (48.8566 N, 2.3522 E), Beijing (39.9042 N, 116.4074 E), Tokyo (35.6762 N, 139.6503 E), Sydney (33.8688 S, 151.2093 E), Johannesburg (26.2041 S, 28.0473 E) and Moscow (55.7558 N, 37.6173 E).
- **Ring height:** each ring sits at the planet's stationary-orbit radius (Earth 42,164 km, Mars ~20,430 km).
  - Away from the equator nothing can truly hold such a point, so the rings are powered stations keeping position by thrust. This is fiction.
  - Where the stationary orbit lies outside the planet's neighbourhood (Venus, Titan, tidally locked Proxima b), the ring sits three radii out.
- **Ring count:** ⌈log₁₀(population / 10⁵)⌉ rings, one to seven; Earth is fixed at seven.
- **Other sites:**
  - Mars: Jezero crater (18.38 N, 77.58 E) and Pavonis Mons (1.48 N, 112.96 W)
  - Venus: Aphrodite Terra
  - Titan: Kraken Mare
  - Proxima b: its terminator, at dawn and dusk longitudes
- **Entry interface heights:** Earth 120 km, Mars 125 km, Venus 250 km, Titan 600 km. These are typical values.

## Belt asteroids (Sol's hidden pirate havens)

Three real main-belt asteroids, each hiding a game-layer pirate haven (Hollow Iron, Lantern Rock, Dactyl's Rest).

- **Orbits:** JPL Small-Body Database osculating elements (heliocentric ecliptic J2000), epoch JD 2461200.5, period from Kepler's third law. Checked against JPL Horizons heliocentric vectors on 2027-01-01 and 2027-12-31 (within 0.5°; OrbitalMechanicsTests).

| Body | a (AU) | e | i | Ω | ω | M | Solution |
|---|---|---|---|---|---|---|---|
| 16 Psyche | 2.925720 | 0.134932 | 3.0987° | 149.9754° | 230.0327° | 79.7694° | JPL, 2025-02-06 |
| 21 Lutetia | 2.434431 | 0.164770 | 3.0645° | 80.8386° | 249.8803° | 298.7726° | JPL, 2026-06-05 |
| 243 Ida | 2.863348 | 0.046110 | 1.1304° | 323.5367° | 113.2572° | 49.6477° | JPL, 2026-06-05 |

- **Physical data** (SBDB physical parameters):
  - Psyche: diameter 222 km (Shepard et al. 2021, PSJ 2, 125); GM 1.601 km³/s² (Farnocchia et al. 2024, AJ 168, 21); rotation 4.196 h (LCDB).
  - Lutetia: diameter 98 km (Sierks et al. 2011, Science 334, 487); GM 0.1134 km³/s² (Pätzold et al. 2011, Science 334, 491); rotation 8.1655 h (LCDB).
  - Ida: diameter 32 km (Belton et al. 1995); GM 0.00275 km³/s² (Belton et al. 1996, Icarus 120, 185); rotation 4.634 h (LCDB).
- **Simplified:** each is drawn as a sphere of its mean diameter, with its spin pole along the ecliptic north (Lutetia's measured pole and Ida's moon Dactyl are not modelled). The osculating orbits drift from the truth over decades as Jupiter perturbs them.
- **Fiction:** the havens, their garrisons and the scanner range (2 million km) at which a ship picks one out.

## Factions, settlements, stations

- Everything below is game-layer fiction placed on real (or candidate) worlds:
  - the Centauri Accord, the Toliman Combine, the Kentaurus Free Traders and the Long Dark;
  - New Kentaurus, Ember Station, Far Reach, Rigil Gate, Aurora Skimmer, Toliman Forge and Blackhollow;
  - every orbital yard, factory and habitat;
  - in Sol, the Black Banner and its havens: Blackwater on Deimos and the hidden belt bases above.
- The ore mined at Toliman Forge is fiction: no asteroid belt is known around B.
