# Map and data sources

Every map here is real spacecraft or telescope data unless a row says otherwise. Downloads were
treated as data only: images were opened and resampled with Pillow, nothing in them was run.

## Layout and orientation

All maps are equirectangular (2:1) in one layout, so every body uses the same sphere mesh
(`Runtime/Rendering/MeshFactory.UvSphere`):

- **u** = planetocentric **east** longitude, from -180° at the left edge through 0° in the centre
  to +180° at the right. Sources that start at 0° or 180°, or that label longitudes west-positive,
  were rolled (never mirrored: a west-positive map is the same picture with different labels).
- **v** = latitude from the south pole (bottom) to the north pole (top).
- "North" is the pole the source map calls north. For Pluto that is the IAU positive (spin) pole;
  for Miranda, Titania and Oberon it is Uranus's IAU north, opposite their spin
  (`BodyDefinition.MapNorthFlipped`).

`Core/BodyOrientation.cs` turns each body so its 0° meridian faces the right way: IAU prime
meridians W = W0 + dW/dt·d (Archinal et al. 2018) for the planets, Ceres and Pluto, and the
parent-facing 0° meridian of synchronous moons. Orientation was checked feature by feature on
previews with markers at known coordinates (Moon: Mare Crisium, Tycho, Copernicus; Mercury:
Caloris; Mars: Olympus Mons, Hellas; Io: Pele, Loki, Prometheus; Europa: Pwyll; Ganymede:
Galileo Regio, Osiris; Callisto: Valhalla, Asgard; Titan: Xanadu, Shangri-La; Ceres: Occator;
Pluto: Sputnik Planitia, Cthulhu; Charon: Mordor Macula; Phobos: Stickney; Miranda: Arden,
Elsinore, Inverness coronae). A unit test also checks that Pluto's 0° meridian faces Charon.

**Greyscale maps** (single-band mosaics) are multiplied by the body's documented average colour
(`BodyDefinition.TintTexture`), so regional colour differences on those bodies are not shown.
**Gaps** with no data are filled with the mean grey of the imaged surface, feathered at the edge,
and listed per body below. Nothing invented is painted into them.

## Colour maps

| File | Source | License | Notes |
|---|---|---|---|
| `moon_color.jpg` (4096) | [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/), `lroc_color_16bit_srgb_4k.tif` (2025 version), by Ernie Wright | Public domain (NASA) | LRO LROC WAC Hapke-normalized mosaic: 643 nm as red, 566 nm green, 415 nm blue, white-balanced by SVS to approximate human vision. A real, subtle colour map, not a mineral-ratio false colour. Latitudes beyond ±70° are filled by SVS with the LOLA laser albedo (LDAM), which is greyscale. |
| `mercury_color.jpg` (4096) | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/mercury_messenger_mdis_global_color_mosaic_665m), `Mercury_MESSENGER_ClrMosaic_global_665m_v3.tif` | Public domain (NASA/USGS) | MESSENGER MDIS *Global Color Mosaic* (deliberately not the "Enhanced Color" product, which exaggerates mineral differences). Same product as before at 4× the resolution. Small black gaps are inpainted from their surroundings; the polar no-data caps are hidden by a pole margin. |
| `mars_color.jpg` (4096) | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/mars_viking_global_color_mosaic_925m), `Mars_Viking_ClrMosaic_global_925m.tif` | Public domain (NASA/USGS) | Viking Orbiter red/violet-filter global colour mosaic. Same product as before at 4× the resolution. |
| `jupiter_color.jpg` (4096) | [NASA SVS "Hubble Maps Jupiter in 4K"](https://svs.gsfc.nasa.gov/12021/), `Hubble_Jupiter_color_global_map_2015a.tif` | Public domain (NASA) | Hubble WFC3, OPAL programme, 2015 January 19 (Simon, Wong & Orton). Source is 3600×1800, resampled. The map runs 360°W to 0°W (System III) left to right; data ends near ±80°. The Great Red Spot is at 125.9°W, 22.6°S on it (measured). The shader turns the whole map so the spot sits at its current longitude (see below), so other cloud features are 2015 weather, not today's. |
| `io_color.jpg` | USGS Astrogeology, [`Io_GalileoSSI-Voyager_Global_Mosaic_ClrMerge_1km.tif`](https://planetarymaps.usgs.gov/mosaic/Io_GalileoSSI-Voyager_Global_Mosaic_ClrMerge_1km.tif) | Public domain (NASA/USGS) | Galileo SSI violet, green and 756 nm near-infrared, merged with Voyager 1 detail. USGS calls it false colour and says "the true colors that would be visible to the eye are similar but much more muted". **Saturation reduced by 50%** to approximate that. Approximate natural colour, not exact. |
| `europa_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/europa_voyager_galileo_ssi_global_mosaic_500m), `Europa_Voyager_GalileoSSI_global_mosaic_500m.tif` | Public domain (NASA/USGS) | Greyscale Voyager and Galileo mosaic, tinted. South polar no-data filled. |
| `ganymede_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/ganymede_voyager_galileo_ssi_color_global_mosaic_1_4km), `Ganymede_Voyager_GalileoSSI_Global_ClrMosaic_1435m.tif` | Public domain (NASA/USGS) | Voyager and Galileo colour mosaic. The metadata does not name the filters. It looks close to natural, with colour patchwork where Voyager and Galileo coverage meet. Corner no-data filled. |
| `callisto_color.jpg` | USGS Astrogeology, [`Callisto_Voyager_GalileoSSI_global_mosaic_1km.tif`](https://planetarymaps.usgs.gov/mosaic/Callisto_Voyager_GalileoSSI_global_mosaic_1km.tif) | Public domain (NASA/USGS) | Greyscale, tinted. Southern no-data filled. |
| `titan_surface.jpg` | USGS Astrogeology, [`Titan_ISS_P19658_Mosaic_Global_4km.tif`](https://planetarymaps.usgs.gov/mosaic/Titan_ISS_P19658_Mosaic_Global_4km.tif) | Public domain (NASA/USGS) | Cassini ISS at 938 nm, a methane window through the haze, so this is a greyscale *near-infrared* surface map. **In visible light Titan's haze hides the surface completely**, so the shader covers it with 82% opaque orange haze (`SurfaceHaze`). The faint surface showing through is a nod to Cassini's view, not what an eye would see. |
| `enceladus_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/enceladus_cassini_global_mosaic_110m), `Enceladus_Cassini_mosaic_global_110m.tif` | Public domain (NASA/USGS) | Cassini ISS greyscale, tinted. |
| `phobos_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/phobos_mars_express_src_global_mosaic_12m), `Phobos_ME_SRC_Mosaic_Global_16ppd.tif` | Public domain (ESA/DLR/FU Berlin data, USGS product) | Mars Express SRC greyscale, tinted. The map is wrapped on a sphere, though Phobos is irregular. |
| `deimos_color.jpg` | USGS planetary maps server, `wms_basemaps/Deimos/deimoscyl4.jpg` (asc-pds-services S3 bucket behind [planetarymaps.usgs.gov](https://planetarymaps.usgs.gov)) | Public domain (NASA/USGS) | Viking-based simple cylindrical map, 4 px/deg, greyscale, tinted. **The longitude origin is assumed (centre 0°) and not independently verified**: Voltaire and Swift fall near the expected place, but this map has no georeferencing tags. Wrapped on a sphere, though Deimos is irregular. |
| `ceres_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/ceres_dawn_fc_global_mosaic_400m), `Ceres_Dawn_FC_DLR_global_20ppd_Oct2015.tif` | Public domain (NASA/JPL/DLR data, USGS product) | Dawn Framing Camera clear filter, greyscale, tinted. Southern polar no-data filled. |
| `pluto_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/pluto_new_horizons_lorri_mvic_global_mosaic_300m), `Pluto_NewHorizons_Global_Mosaic_300m_Jul2017_8bit.tif` | Public domain (NASA/JHUAPL/SwRI, USGS product) | New Horizons LORRI and MVIC panchromatic mosaic, tinted with one average colour, so Pluto's real colour contrasts (white Sputnik Planitia, dark red Cthulhu) show only as brightness. **The southern third was in polar night in 2015 and was never imaged; it is filled with mean grey.** The hemisphere New Horizons passed at a distance is low resolution. |
| `charon_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/charon_new_horizons_lorri_mvic_global_mosaic_300m), `Charon_NewHorizons_Global_Mosaic_300m_Jul2017_8bit.tif` | Public domain (NASA/JHUAPL/SwRI, USGS product) | Panchromatic, tinted. Unimaged south filled with mean grey. |
| `triton_color.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/triton_voyager_2_global_color_mosaic_600m), `Triton_Voyager2_ClrMosaic_GlobalFill_600m.tif` | Public domain (NASA/JPL/USGS) | Voyager 2's colour was synthesised from orange, violet and **ultraviolet** frames shown as red, green and blue, so it is not natural colour. **Converted to brightness and tinted** with Triton's average colour. Voyager saw only the southern hemisphere; the north is filled with mean grey. |
| `miranda_color.jpg`, `titania_color.jpg`, `oberon_color.jpg` | [JPL Solar System Simulator texture maps](https://space.jpl.nasa.gov/tmaps/uranus.html), `ura5vuu2.tif`, `ura3vuu2.tif`, `ura4vuu2.tif` ("From the USGS, mosaicked with Voyager imagery") | NASA/JPL/Caltech/USGS; JPL image-use policy (free use with credit) | 1440×720 greyscale, tinted. **Voyager 2 saw only the southern hemispheres in 1986. The unimaged half of each map is filled with mean grey** (black in the source), so roughly half of each moon is a flat neutral colour. Orientation checked on Miranda against the Arden, Elsinore and Inverness coronae. |
| `saturn_color.jpg`, `uranus_color.jpg`, `neptune_color.jpg` | [Solar System Scope](https://www.solarsystemscope.com/textures/) (via Wikimedia Commons mirrors) | CC BY 4.0 (attribution required) | **Stylized, not raw photographic data.** No genuine cartographic global map was found: gas giants have no fixed surface to project, and Uranus and Neptune had a single brief 1980s flyby. Solar System Scope says gaps are "filled with fictional terrain that corresponds with the rest of the landscape." Attribute as: "Textures by Solar System Scope (solarsystemscope.com/textures), CC BY 4.0." |

## Relief (normal) maps

Tangent-space normals (x east, y north, z up) computed from real elevation grids, then resampled.
**Relief is exaggerated ×8 in the baked maps** so it survives 8-bit storage at planetary scale. The
shader applies 0.6 of that, so slopes are drawn about 5× steeper than real. Only shading uses
them; the silhouette stays a sphere.

| File | Source | License | Notes |
|---|---|---|---|
| `moon_normal.jpg` (4096) | [NASA SVS CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/), `ldem_16.tif` | Public domain (NASA) | LRO LOLA gridded topography, 16 px/deg, km relative to 1737.4 km. |
| `mars_normal.jpg` | [PDS Geosciences Node, MGS MOLA MEGDR](https://pds-geosciences.wustl.edu/mgs/mgs-m-mola-5-megdr-l3-v1/mgsl_300x/meg016/), `megt90n000eb.img` | Public domain (NASA) | MOLA topography, 16 px/deg, 16-bit big-endian metres; map starts at 0°E. |
| `mercury_normal.jpg` | [USGS Astropedia](https://astrogeology.usgs.gov/search/map/mercury_messenger_global_dem_665m), `Mercury_Messenger_USGS_DEM_Global_665m_v2_32bit_clon0.tif` | Public domain (NASA/USGS) | MESSENGER global DEM, metres. |

## Not mapped

- **Venus**: no visible-light camera has ever seen its surface; Magellan radar maps are false
  colour by construction. What an eye sees is the nearly featureless pale yellow cloud deck, so
  Venus is drawn as a **procedural** cloud deck (faint streaks at 5% contrast) that super-rotates
  once in about 4 days, retrograde (Mariner 10, Venus Express). The streak pattern is
  illustrative, not cloud data.
- **Eris**: no surface data exists; procedural.

## Gas giant weather (`Core/GasGiantWeather.cs`, `Shaders/GasGiant.shader`)

- **Zonal winds**, approximate digitisations (±15 m/s) of: Jupiter, Porco et al. 2003, *Science*
  299, 1541 (Cassini ISS; agrees with Limaye 1986); Saturn, Sánchez-Lavega et al. 2000, *Icarus*
  147, 405; Uranus, Sromovsky & Fry 2005 and Hammel et al. 2001; Neptune, Sromovsky et al. 1993.
  Speeds are relative to each planet's catalogue rotation (Jupiter System III).
- **Great Red Spot position**: BAA Jupiter Section 2025 report (S. Mizumoto), 2025 Sep 1:
  System II 77°, System III 292°. **Drift**: JUPOS via
  [projectpluto.com/grs_lon.txt](https://mail.projectpluto.com/grs_lon.txt), System II 410° on
  2024 Jan 1 to 439° on 2025 Dec 1 (+0.0415°/day), plus System III's 8.0°/30 days relative to
  System II (BAA): **+0.308°/day in System III west longitude**. The real drift oscillates (a
  ~90-day libration) and changes over years, so expect a few degrees of error by 2027.
- **Great Red Spot rotation**: counter-clockwise (an anticyclone in the southern hemisphere),
  about 6 days around the collar, with a quiet core (Voyager, Galileo and Juno cloud tracking).
- **Saturn, Uranus and Neptune** use the stylized maps above, so only their *motion* is data.

## Asteroids (`Resources/Data/asteroids.bytes`)

| Source | License | Notes |
|---|---|---|
| [JPL SBDB Query API](https://ssd-api.jpl.nasa.gov/doc/sbdb_query.html), queried 2026-10-11 with `full-prec=1` | Public domain (NASA/JPL) | Every numbered main-belt asteroid (`sb-class=MBA`) with H < 15.5 (101,092 after removing Ceres, which is a full body) and every Jupiter Trojan (`sb-class=TJN`) with H < 15 (10,175): **111,267 objects**. Fields: a, e, i, Ω, ω, M, epoch, H. All share the osculating epoch JD 2461200.5 (2026 Jun 9). Packed by a script into 36-byte records (format in `Core/AsteroidField.cs`). |

H 15.5 is roughly 2 to 5 km across, depending on albedo. The belt has hundreds of thousands
more fainter objects; the cutoff keeps the draw call under ~670k vertices. Kirkwood gaps and the
Trojan clouds come from the data, not from any model. Positions are two-body Kepler from the
epoch: tests against JPL Horizons put Vesta and Pallas within 0.02 AU and (624) Hektor within
0.03 AU over 2025 to 2028.

## Ship plating

- `ship_plating.png`: ambientCG **MetalPlates015A** (CC0 1.0, https://ambientcg.com/a/MetalPlates015A), 1K.
  - Packed for Ship.shader: R = the colour map as greyscale (normalised to mean 0.5), G/B = the OpenGL normal map's Y/X.
  - Applied triplanar to every hull, station and structure.
