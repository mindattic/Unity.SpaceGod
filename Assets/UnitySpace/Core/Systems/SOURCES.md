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
- **The game layer:** Sol and Alpha Centauri keep their settlements, fleets and jump gates. The twelve systems beyond are settled too (U.SPC-T90, `NearbySocieties.cs`), more thinly the farther out they are; see "The nearby societies" below. Every faction, settlement, station and ship there is fiction; the catalogue values above are untouched by it.

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
- **Candidates:** planets the archive does not list come from their discovery papers and are flagged **CANDIDATE** in the game. Every planet the archive lists as confirmed is confirmed here.

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
- **Planets** (archive; Feng et al. 2017), all confirmed: the archive lists exactly these three (query of 2026-10-16).

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

## The frontier stars (U.SPC-T97)

The stars of the interstellar view that nobody simulates until a faction builds the first gate to one (`FrontierSystems.cs`, see `GateConstruction`). Each is its primary star only (a companion is named in its summary but not simulated), with only the best-established planets. Positions are the interstellar view's (Wikipedia "List of nearest stars": RECONS and Gaia DR3). Star values are from each star's Wikipedia entry and the papers it cites; where marked *derived* the luminosity is worked out from the radius and temperature (L = R²(T/5772 K)⁴), and a rotation marked *assumed* has not been measured.

| Star | Type | Mass (M☉) | Radius (R☉) | Luminosity (L☉) | Teff (K) | Rotation (d) | Not simulated |
|---|---|---|---|---|---|---|---|
| WISE 0855−0714 | Y4 | ≈0.0048 (5 MJ; 3–10 MJ) | 0.10 | derived | 285 | 0.4 *assumed* | |
| Gliese 65 A (BL Ceti) | M5.5Ve | 0.1225 (Kervella et al. 2016) | 0.165 | derived | 2670 | 0.243 | UV Ceti (B) |
| Ross 154 | M3.5Ve | 0.177 | 0.240 | 0.0038 | 3248 | 2.85 | |
| Ross 248 | M5.5Ve | 0.136 | 0.160 | 0.0018 | 2799 | 4.2 | |
| Gliese 887 | M0.5V | 0.489 (Jeffers et al. 2020) | 0.471 | 0.0368 | 3688 | 200 | |
| 61 Cygni A | K5.0V | 0.70 | 0.665 | 0.153 | 4526 | 35.4 | 61 Cygni B |
| Procyon A | F5IV–V | 1.478 | 2.048 | 6.93 | 6530 | 23 | Procyon B |
| Struve 2398 A | M3.0V | 0.334 | 0.351 | derived | 3400 | 40 *assumed* | Struve 2398 B |
| Groombridge 34 A | M1.5V | 0.38 | 0.38 | derived | 3700 | 44 | Groombridge 34 B |
| DX Cancri | M6.5Ve | 0.087 | 0.11 | derived | 2700 | 1.4 *assumed* | |

- **Planets:** Gliese 887 b (9.262 d, 0.068 AU, e 0.09, 4.2 M⊕ min) and c (21.789 d, 0.120 AU, e 0.22, 7.6 M⊕ min), Jeffers et al. 2020 (Science 368, 1477); Groombridge 34 Ab (11.4433 d, 0.0717 AU, e 0.09, 3.03 M⊕ min), Howard et al. 2014 and Pinamonti et al. 2018. Radii from the Chen & Kipping 2017 relation; planes and phases assumed.
- **Left out:** EZ Aquarii, a close triple whose masses are not pinned down well enough, has no hand-built entry; it stays a plain star until a pilot comes near, when the catalogue source builds it as one star with values estimated from its temperature (see "The rest of the galaxy" below).
- **The gates themselves** are fiction: what one costs (tonnes of Alloys, Electronics and Machinery, scaling with its reach), how long it takes to build (two years and a fifth of a year per light-year, after a survey of 60 days and 15 per light-year), the farthest one reaches (8 ly) and how war damages them are game rules, in `GateConstruction.cs`.


## The rest of the galaxy (U.SPC-T413)

Past the hand-built systems every star comes from a registered source (`SystemSources.cs`), charted as a pilot comes near and built only when one comes nearer (`GalaxyStreaming.cs`).

**Catalogue stars** (`CatalogueStars.cs`): the interstellar view's 21 neighbours (above), twelve more of the nearest systems out to 20 ly, and four bright landmarks beyond. Values are from each star's Wikipedia article (Hipparcos, Gaia DR3 and the papers it cites). Known planets of these stars are not modelled yet; each is built as its star alone with the habitat orbit the frontier stars have. The list is far from complete (there are some sixty systems between 12 and 20 ly); the sphere is kept free of invented stars all the same.

| Star | RA | Dec | ly | Type | V | Teff (K) | Mass (M☉) | Radius (R☉) | L (L☉) |
|---|---|---|---|---|---|---|---|---|---|
| Kapteyn's Star | 5h11m40.58s | −45°01′06.3″ | 12.83 | sdM1 | 8.85 | 3570 | 0.274 | 0.291 | 0.012 |
| Lacaille 8760 | 21h17m15.27s | −38°52′02.5″ | 12.95 | M0Ve | 6.67 | 3800 | 0.60 | 0.51 | 0.072 |
| Kruger 60 A | 22h27m59.47s | +57°41′45.1″ | 13.07 | M3V | 9.59 | 3180 | 0.271 | 0.35 | 0.010 |
| Van Maanen's Star | 0h49m09.90s | +5°23′19.0″ | 14.07 | DZ7 | 12.37 | 6220 | 0.68 | 0.011 | 0.00017 |
| Gliese 876 | 22h53m16.73s | −14°15′49.3″ | 15.24 | M4V | 10.19 | 3473 | 0.37 | 0.376 | 0.0122 |
| 40 Eridani A | 4h15m16.32s | −7°39′10.3″ | 16.26 | K0.5V | 4.43 | 5072 | 0.78 | 0.81 | 0.46 |
| 70 Ophiuchi A | 18h05m27.29s | +2°30′00.4″ | 16.71 | K0V | 4.03 | 5300 | 0.89 | 0.91 | 0.60 |
| Altair | 19h50m47.00s | +8°52′06.0″ | 16.73 | A7V | 0.76 | 7550 | 1.86 | 1.79 | 10.6 |
| Sigma Draconis | 19h32m21.59s | +69°39′40.2″ | 18.80 | G9V | 4.67 | 5297 | 0.87 | 0.78 | 0.43 |
| Eta Cassiopeiae A | 0h49m06.29s | +57°48′54.7″ | 19.33 | F9V | 3.44 | 5973 | 0.97 | 1.04 | 1.23 |
| 82 Eridani | 3h19m55.65s | −43°04′11.2″ | 19.71 | G8V | 4.25 | 5401 | 0.85 | 0.92 | 0.69 |
| Delta Pavonis | 20h08m43.61s | −66°10′55.4″ | 19.89 | G8IV | 3.56 | 5604 | 0.99 | 1.22 | 1.22 |
| Vega | 18h36m56.34s | +38°47′01.3″ | 25.04 | A0V | 0.03 | 9602 | 2.14 | 2.5 | 40.1 |
| Fomalhaut | 22h57m39.05s | −29°37′20.1″ | 25.13 | A3V | 1.16 | 8590 | 1.92 | 1.84 | 16.6 |
| Pollux | 7h45m18.95s | +28°01′34.3″ | 33.78 | K0III | 1.14 | 4586 | 1.91 | 9.06 | 32.7 |
| Arcturus | 14h15m39.67s | +19°10′56.7″ | 36.7 | K1.5III | −0.05 | 4286 | 1.08 | 25.4 | 170 |

- A neighbour with no hand-built entry (EZ Aquarii) has its mass read off the main-sequence temperature-mass table of Pecaut & Mamajek 2013 (ApJS 208, 9), its radius from M^0.8 (M^0.57 above a solar mass) and its luminosity from L = R²(T/5772 K)⁴: estimates, and its summary says so. Rotations are assumed.

**The procedural galaxy** (`ProceduralSystems.cs`) is fiction on a real shape:

- **Frame:** the galactic centre is Sagittarius A* (RA 17h45m40.04s, Dec −29°00′28.2″) at 8.178 kpc (26,673 ly; GRAVITY Collaboration 2019); the north galactic pole RA 12h51m26.28s, Dec +27°07′42.0″ (J2000); the Sun 20.8 pc (68 ly) above the plane (Bennett & Bovy 2019).
- **Density:** a thin exponential disc, scale length 2.6 kpc and scale height 300 pc (Bland-Hawthorn & Gerhard 2016), normalised to 0.0032 star systems a cubic light-year at the Sun (what the catalogued systems within 12 ly give), fading past 50,000 ly from the centre, plus an exponential bulge (0.1 /ly³ at the centre, scale 1,500 ly). Spiral arms, the bar and the halo are left out.
- **Stars:** the solar neighbourhood's mix (RECONS): 73% M, 12% K, 7% G, 3% F, 0.6% A, 0.13% B, 5% white dwarfs, the rest brown dwarfs. Masses uniform within a class, temperature rising with mass across it; radius M^0.8 (M^0.57 above a solar mass), 0.012 R☉ for a white dwarf, 0.1 R☉ for a brown dwarf; luminosity from radius and temperature.
- **Planets and belts:** invented. A few planets a star on average (fewer round white and brown dwarfs and round A and B stars), the innermost at 0.02–0.25 AU scaled by √L, each next 1.4–3 times farther out; 60% rocky (0.1–5 M⊕), 25% Neptune-like (5–50 M⊕), 15% giants (50–3000 M⊕); radii from the Chen & Kipping 2017 relation, periods from Kepler's third law. One system in seven has a belt past its planets.
- **Determinism:** space is cut into 10-ly cells; a cell's stars come from the galaxy's seed and the cell alone (SplitMix64), and a star's planets from its own seed, so the galaxy is the same however and in whatever order it is explored.

## Real spacecraft (Sol)

Orbits are circular, at the published altitude and inclination. Where each craft sits along its orbit is **illustrative**: there are no live orbital elements.

- **Crewed stations:**
  - ISS: ~420 km, 51.64°
  - Tiangong: ~385 km, 41.5°
- **Telescopes:**
  - Hubble: ~530 km, 28.5°
  - JWST: at Sun–Earth L2, 1.5 million km anti-sunward (its halo orbit is not drawn)
  - Euclid: at Sun–Earth L2 since 2023 (halo orbit not drawn)
- **At the Lagrange points** (each drawn at its point, not on its halo orbit):
  - SOHO (since 1996) and DSCOVR (since 2015): Sun–Earth L1
  - Queqiao: Chang'e 4's far-side relay, on a halo orbit about Earth–Moon L2 since 2018
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

## Lagrange points

- **Model:** the circular restricted three-body problem applied to each pair's instantaneous geometry (`Core/Lagrange.cs`). L1, L2 and L3 lie on the line through the pair at the roots of the collinear quintics for the mass ratio μ = m₂/(m₁+m₂) (Szebehely 1967, *Theory of Orbits*, §4.5); L4 and L5 are the third corners of the two equilateral triangles in the smaller body's instantaneous orbital plane, L4 ahead. A planet's points about the Sun use it and its moons together (Earth and the Moon as their barycentre, as JPL does); a moon's points use the moon alone.
- **This is how JPL Horizons defines its Lagrange-point ephemerides**, and the model is checked against them (LagrangeTests): SEMB-L1, L2, L4 and L5 (Horizons 31, 32, 34, 35; heliocentric) and EM-L1, L2, L4 and L5 (3011, 3012, 3014, 3015; geocentric) on 2026-10-17 and 2027-04-17. Horizons gives EM-L1 at 84.907% and EM-L2 at 116.783% of the Earth–Moon distance; the quintics give the same to five figures.
- **Simplified:** objects at L1, L2 and L3 really fly halo or Lissajous orbits about the point (they are unstable); they are drawn at the point itself.
- **Infrastructure placed there (game layer):** the Cislunar Depot and the L1 Gateway at Earth–Moon L1 (the low-energy waypoint between Earth and the Moon), the Farside Depot at Earth–Moon L2, the Sunward Relay at Sun–Earth L1 (where the solar wind passes about an hour before it reaches Earth), the Outbound Depot at Sun–Earth L2, the Greek Camp Depot at Sun–Jupiter L4 and the Trojan Camp Relay at Sun–Jupiter L5. Each moves with its pair, so the L4 and L5 structures stay co-orbital with Jupiter, 60° ahead and behind.

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

## Jupiter Trojans (mining grounds and a hidden haven)

Three real Trojans: Hektor and Eurybates in the Greek camp at Jupiter's L4, Patroclus in the Trojan camp at L5.

- **Orbits:** JPL Small-Body Database osculating elements (heliocentric ecliptic J2000), epoch JD 2461200.5, period from Kepler's third law; retrieved 2026-10-17 from the SBDB API (`ssd-api.jpl.nasa.gov/sbdb.api`). Checked against JPL Horizons heliocentric vectors on 2027-01-01 and 2027-12-31 (within 0.2°; LagrangeTests), and kept 25–100° ahead of (L4) or behind (L5) Jupiter across 2020–2035.

| Body | a (AU) | e | i | Ω | ω | M | Solution |
|---|---|---|---|---|---|---|---|
| 624 Hektor | 5.276986 | 0.024345 | 18.1472° | 342.8019° | 181.2889° | 24.9139° | JPL, 2026-06-05 |
| 617 Patroclus | 5.205975 | 0.139147 | 22.0636° | 44.3497° | 308.8377° | 58.6754° | JPL, 2026-09-09 |
| 3548 Eurybates | 5.217372 | 0.090599 | 8.0515° | 43.5587° | 28.6997° | 125.7480° | JPL, 2024-09-11 |

- **Physical data:**
  - Hektor: diameter 225 km (SBDB); GM 0.527 km³/s² from its mass, 7.9 × 10¹⁸ kg, measured by its moon Skamandrios (Marchis et al. 2014, ApJL 783, L37); rotation 6.924 h (LCDB).
  - Patroclus: diameter 140 km, the NEOWISE equivalent of the 113 and 104 km pair (Grav et al. 2012, ApJ 759, 49); GM 0.091 km³/s² from the pair's mass, 1.36 × 10¹⁸ kg (Marchis et al. 2006, Nature 439, 565); rotation 102.8 h, the pair's mutual period (LCDB).
  - Eurybates: diameter 64 km (NEOWISE, Grav et al. 2012); GM 0.0091 km³/s² **assumed** (a density of 1 g/cm³; its moon Queta has not yet given a mass); rotation 8.711 h (LCDB).
- **Simplified:** each is drawn as one sphere of its mean diameter (Hektor's lobes, Patroclus's twin Menoetius and the moons Skamandrios and Queta are not modelled). Trojans are strongly perturbed by Jupiter: an osculating orbit holds for years, not decades, and does not librate.
- **Fiction:** the mining outposts Skamandrios Diggings (Hektor) and Queta Camp (Eurybates), the Belt Consortium's lanes out to them, and the Black Banner's hidden haven Menoetius Hold on Patroclus. Trojans are dark, primitive D- and P-type bodies thought rich in ices and organics, hence the volatiles mined there.

## Factions, settlements, stations

- Everything below is game-layer fiction placed on real (or candidate) worlds:
  - the Centauri Accord, the Toliman Combine, the Kentaurus Free Traders and the Long Dark;
  - New Kentaurus, Ember Station, Far Reach, Rigil Gate, Aurora Skimmer, Toliman Forge and Blackhollow;
  - every orbital yard, factory and habitat;
  - in Sol, the Black Banner and its havens: Blackwater on Deimos, the hidden belt bases and Menoetius Hold above.
- The ore mined at Toliman Forge is fiction: no asteroid belt is known around B.

## The nearby societies (U.SPC-T90)

- **All fiction**, placed by `NearbySocieties.cs` on the real bodies of the twelve catalogue systems: every faction, settlement, population, mine, factory, yard, lane and ship name. The physical values of the stars and planets stay those sourced above; a society only adds invented stations (each summary begins "Fiction.") on circular orbits of its own choosing.
- **Where things go:** colonies only on habitable-zone worlds (Ross 128 b, GJ 1061 d, Luyten b, Teegarden's Star b); metal mines on the hot rocky worlds (Barnard e and c, Lalande 21185 b, Tau Ceti h, GJ 1061 b and c, Luyten c, Teegarden's Star c); skimmer aerostats on the giants and mini-Neptunes, candidates included (Wolf 359 b, Lalande 21185 c, Epsilon Eridani b, Epsilon Indi Ab, Luyten d); ice and rock stations in the known belts and discs (Epsilon Eridani's inner and outer belts, Tau Ceti's disc). Where no world will do, the hub is a station in the habitable zone, and ore or ice comes from a captured swarm of rock or comets that no survey has found (Barnard's Star, Luhman 16, Wolf 359, Sirius, Ross 128, Epsilon Indi): said so in each station's summary. Sirius's stations keep to orbits well inside the ~8 AU periastron of B (its habitable zone, past 5 AU, is not stable); Luhman 16's skim the brown dwarfs' iron and silicate cloud decks and their water, methane and ammonia.
- **How big:** each settlement's people are scaled by (4.344 ly / distance)², Alpha Centauri's distance over the system's, squared, so Barnard's Star is about half as built up as Centauri and the 12 ly frontier about an eighth. The fleet (freighters, ore haulers, couriers, a navy patrol, free ships) follows from the people. Raiders hold a haven only in the two systems big enough to keep a navy that can hold them off (Barnard's Star and Epsilon Eridani).
- **Sized to work:** every settlement grows most of its own food, water and air; the mines and factories are then sized so the system makes 15% more of every good than it uses, a facility whose consumables come from elsewhere counted at the 55% it runs on without them.
