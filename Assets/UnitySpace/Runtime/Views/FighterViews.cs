using System.Collections.Generic;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.SpaceGod.Views
{
    /// <summary>
    /// Every fighter in every battle: its hull, an engine glow kept a few pixels across so a squadron
    /// reads as a cluster of sparks from afar, and a fading trail that traces its runs and break-offs. Craft down on a
    /// flight deck are drawn on it, and every hull's flight decks (U.SPC-T518) are drawn as lit frames round their stern
    /// and bow mouths, with the glide path astern while a craft is on it, once the hull is big enough on screen to show them.
    /// </summary>
    public sealed class FighterViews
    {
        sealed class View
        {
            public GameObject Hull;
            public Transform Glow;
            public LineStrip Trail;
            public readonly List<DVec3> History = new List<DVec3>();
            public float NextSample;
        }

        readonly UnitySpaceApp app;
        readonly Transform root;
        readonly Dictionary<BattleUnit, View> views = new Dictionary<BattleUnit, View>();

        /// <summary>A flight deck's lights: the frames round its stern and bow mouths and its glide path.</summary>
        sealed class DeckView
        {
            public LineStrip Stern, Bow, Path;
        }

        readonly Dictionary<FlightDeck, DeckView> deckViews = new Dictionary<FlightDeck, DeckView>();
        readonly Vector3[] framePoints = new Vector3[5];
        readonly Vector3[] trailPoints = new Vector3[TrailLength];
        static Material glowMaterial, shipMaterial;
        static Mesh quad;

        const int TrailLength = 24;
        const float SampleSeconds = 0.12f;

        public FighterViews(UnitySpaceApp app, Transform root)
        {
            this.app = app;
            this.root = root;
            quad ??= MeshFactory.Quad();
            if (glowMaterial == null)
            {
                glowMaterial = Materials.MarkShared(Materials.Create(Materials.Glow, "FighterGlow"));
                glowMaterial.SetColor("_Color", new Color(0.55f, 0.8f, 1f));
                glowMaterial.SetFloat("_Intensity", 1.6f);
                glowMaterial.SetFloat("_Falloff", 2f);
            }
            shipMaterial ??= ShipModels.ProceduralMaterial;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            var live = new HashSet<BattleUnit>();
            var liveDecks = new HashSet<FlightDeck>();
            var cam = app.UnityCamera.transform;
            double ppu = app.PixelsPerUnitAtUnitDistance();
            foreach (var b in app.World.War.Battles)
            {
                foreach (var u in b.Units)
                {
                    if (u.Kind == UnitKind.Ship && u.Present) DrawDecks(b, u, ppu, liveDecks);
                    // A craft down on a flight deck is drawn on it, inside its hull (U.SPC-T518).
                    bool onDeck = FlightDecks.OnDeck(u) && !u.Fighter.Deck.Deck.Carrier.Dead && u.Fighter.Deck.Deck.Carrier.Present;
                    if (u.Kind != UnitKind.Fighter || !(u.Present || onDeck)) continue;
                    live.Add(u);
                    if (!views.TryGetValue(u, out var v)) views[u] = v = Create(u);
                    var scene = app.BattleScene(b, u.Pos);
                    var rel = app.Relative(scene);
                    // Its model while big enough, then a glow impostor, then nothing below a pixel (U.SPC-T415): a battle
                    // seen from across the system is its ships' icons, not a cloud of sparks.
                    var tier = RenderLod.Tier(RenderLod.Fighter, RenderLod.ApparentPx(app.Scale.ShipLength(u.LengthKm), rel.magnitude, ppu));
                    bool model = RenderLod.DrawsModel(tier), shown = tier != LodTier.Culled;
                    if (v.Hull.activeSelf != model) v.Hull.SetActive(model);
                    if (v.Glow.gameObject.activeSelf != shown) v.Glow.gameObject.SetActive(shown);
                    if (!shown)
                    {
                        v.Trail.Visible = false;
                        continue;
                    }
                    var f = u.Forward.SwapYZ;
                    var fwd = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                    v.Hull.transform.localPosition = rel;
                    if (fwd.sqrMagnitude > 1e-12f)
                    {
                        // The unit's own roof (a manned fighter's roll), not world up (U.SPC-T563).
                        u.Basis(out _, out var roof);
                        var up = roof.SwapYZ;
                        v.Hull.transform.localRotation = Quaternion.LookRotation(fwd, new Vector3((float)up.X, (float)up.Y, (float)up.Z));
                    }
                    v.Hull.transform.localScale = Vector3.one * (float)app.Scale.ShipLength(u.LengthKm);

                    float px = 5f * Mathf.Max(rel.magnitude, 1e-6f) / (float)ppu;
                    v.Glow.localPosition = rel - fwd.normalized * (float)app.Scale.ShipLength(u.LengthKm) * 0.55f;
                    v.Glow.rotation = Quaternion.LookRotation(v.Glow.position - cam.position, cam.up);
                    v.Glow.localScale = Vector3.one * Mathf.Max(px, (float)app.Scale.ShipLength(u.LengthKm) * 0.8f);

                    if (now >= v.NextSample)
                    {
                        v.History.Add(u.Pos);
                        if (v.History.Count > TrailLength) v.History.RemoveAt(0);
                        v.NextSample = now + SampleSeconds;
                    }
                    int n = v.History.Count;
                    if (n >= 2)
                    {
                        for (int i = 0; i < n; i++) trailPoints[i] = app.Relative(app.BattleScene(b, v.History[i]));
                        trailPoints[n - 1] = rel;
                        var c = u.Faction.Color;
                        v.Trail.SetPoints(trailPoints, n, new Color(c.R, c.G, c.B, 0.55f), 0f, 0.8f);
                        v.Trail.SetTint(Color.white);
                        v.Trail.Root.transform.localPosition = Vector3.zero;
                        v.Trail.Visible = true;
                    }
                }
            }
            var gone = new List<BattleUnit>();
            foreach (var kv in views)
                if (!live.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var u in gone)
            {
                var v = views[u];
                Object.Destroy(v.Hull);
                Object.Destroy(v.Glow.gameObject);
                v.Trail.Dispose();
                views.Remove(u);
            }
            var closed = new List<FlightDeck>();
            foreach (var kv in deckViews)
                if (!liveDecks.Contains(kv.Key)) closed.Add(kv.Key);
            foreach (var d in closed)
            {
                var dv = deckViews[d];
                dv.Stern.Dispose();
                dv.Bow.Dispose();
                dv.Path.Dispose();
                deckViews.Remove(d);
            }
        }

        /// <summary>Half the width and height of a deck's mouth, in its hull's lengths, and at least so many km.</summary>
        const double MouthHalfWidth = 0.014, MouthHalfHeight = 0.007, MouthMinKm = 0.03;

        static readonly Color DeckOpen = new Color(0.55f, 0.95f, 0.6f, 0.9f), DeckClosed = new Color(1f, 0.35f, 0.3f, 0.6f);

        /// <summary>
        /// <paramref name="carrier"/>'s flight decks (U.SPC-T518): a lit frame round each mouth, green while the deck is open and
        /// red once its hangar is shot away, and the glide path from the approach gate to the stern mouth while a craft is on
        /// it. Drawn only while the hull is big enough on screen (<see cref="FlightDecks.Lod"/>).
        /// </summary>
        void DrawDecks(Battle b, BattleUnit carrier, double ppu, HashSet<FlightDeck> liveDecks)
        {
            var decks = FlightDecks.Of(carrier);
            if (decks.Count == 0) return;
            var rel = app.Relative(app.BattleScene(b, carrier.Pos));
            var tier = RenderLod.Tier(FlightDecks.Lod, RenderLod.ApparentPx(app.Scale.ShipLength(carrier.LengthKm), rel.magnitude, ppu));
            bool shown = RenderLod.DrawsModel(tier);
            double w = System.Math.Max(MouthMinKm, carrier.LengthKm * MouthHalfWidth), h = System.Math.Max(MouthMinKm * 0.5, carrier.LengthKm * MouthHalfHeight);
            foreach (var d in decks)
            {
                liveDecks.Add(d);
                if (!deckViews.TryGetValue(d, out var dv))
                    deckViews[d] = dv = new DeckView { Stern = new LineStrip("Deck stern", root), Bow = new LineStrip("Deck bow", root), Path = new LineStrip("Glide path", root) };
                dv.Stern.Visible = dv.Bow.Visible = shown;
                dv.Path.Visible = false;
                if (!shown) continue;
                var color = d.Open ? DeckOpen : DeckClosed;
                Frame(b, d, d.SternAlong, w, h, dv.Stern, color);
                Frame(b, d, d.BowAlong, w, h, dv.Bow, color);
                if (d.Open && d.Final != null && d.Final.Present)
                {
                    trailPoints[0] = app.Relative(app.BattleScene(b, d.Gate));
                    trailPoints[1] = app.Relative(app.BattleScene(b, d.Stern));
                    dv.Path.SetPoints(trailPoints, 2, color, 0.15f, 0.8f);
                    dv.Path.Visible = true;
                }
            }
        }

        /// <summary>The lit frame round a deck's mouth <paramref name="along"/> km down its axis.</summary>
        void Frame(Battle b, FlightDeck d, double along, double w, double h, LineStrip strip, Color color)
        {
            framePoints[0] = app.Relative(app.BattleScene(b, d.At(along, new DVec3(-w, -h, 0))));
            framePoints[1] = app.Relative(app.BattleScene(b, d.At(along, new DVec3(w, -h, 0))));
            framePoints[2] = app.Relative(app.BattleScene(b, d.At(along, new DVec3(w, h, 0))));
            framePoints[3] = app.Relative(app.BattleScene(b, d.At(along, new DVec3(-w, h, 0))));
            framePoints[4] = framePoints[0];
            strip.SetPoints(framePoints, 5, color);
            strip.Root.transform.localPosition = Vector3.zero;
        }

        View Create(BattleUnit u)
        {
            var v = new View();
            // Drawn as its own hull class (U.SPC-T484): interceptor, fighter, bomber.
            var cls = FighterCraft.Of(u.Fighter.Role).Class;
            v.Hull = new GameObject(cls.ToString());
            v.Hull.transform.SetParent(root, false);
            v.Hull.AddComponent<MeshFilter>().sharedMesh = ShipMeshFactory.GetFighter(u.Faction, cls);
            var mr = v.Hull.AddComponent<MeshRenderer>();
            mr.sharedMaterial = shipMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            var glow = new GameObject("Engine");
            glow.transform.SetParent(root, false);
            glow.AddComponent<MeshFilter>().sharedMesh = quad;
            var gr = glow.AddComponent<MeshRenderer>();
            gr.sharedMaterial = glowMaterial;
            gr.shadowCastingMode = ShadowCastingMode.Off;
            v.Glow = glow.transform;

            v.Trail = new LineStrip("Trail", root);
            v.Trail.Visible = false;
            return v;
        }
    }
}
