using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Views
{
    /// <summary>
    /// Draws every battle's weapons fire as one dynamic mesh of camera-facing quads: laser beams and
    /// tracers, point-defence streams, missiles with their trails, impacts, shield flashes and
    /// explosions. Effects are timed in real seconds (so they read at any time warp) and kept a few
    /// pixels wide at any distance, so a battle is visible from across a planetary system.
    ///
    /// All of the actual hit/miss/travel-time decisions live in Core (<see cref="Battle"/>) -- this
    /// class only draws what already happened: a beam sweeps onto its target and leaves a scorch, a
    /// kinetic round (<see cref="Battle.KineticRound"/>) is a travelling tracer like a missile rather
    /// than a single instant line, and every direct-fire shot gets a brief muzzle flash and recoil kick
    /// at the gun itself.
    /// </summary>
    public sealed class BattleFx
    {
        enum Shape
        {
            Radial = 0,
            Beam = 1,
            Ring = 2,
        }

        struct Effect
        {
            public Battle Battle;
            public FxKind Kind;
            public DVec3 A, B;
            public double Size;
            public Color Color;
            public float Born, Life;
            public BattleUnit Unit;
        }

        readonly SolarSystemApp app;
        readonly Mesh mesh;
        readonly List<Effect> effects = new List<Effect>();
        readonly Dictionary<Battle, int> seen = new Dictionary<Battle, int>();
        readonly List<Battle> staleSeen = new List<Battle>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector3> uvs = new List<Vector3>();
        readonly List<int> indices = new List<int>();

        const int MaxEffects = 4000;
        const int MaxNewPerBattlePerFrame = 160;

        public BattleFx(SolarSystemApp app, Transform parent)
        {
            this.app = app;
            var go = new GameObject("Battle FX");
            go.transform.SetParent(parent, false);
            mesh = Materials.Own(new Mesh { name = "Battle FX", indexFormat = IndexFormat.UInt32 });
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Materials.Create(Materials.Fx, "BattleFx");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            var war = app.World.War;
            foreach (var b in war.Battles) Collect(b, now);
            foreach (var b in war.Ended) Collect(b, now);
            PruneSeen(war);

            effects.RemoveAll(e => now - e.Born > e.Life);
            if (effects.Count > MaxEffects) effects.RemoveRange(0, effects.Count - MaxEffects);

            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();
            foreach (var e in effects) Draw(e, now);
            foreach (var b in war.Battles) DrawMissiles(b);
            foreach (var b in war.Battles) DrawRounds(b);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
        }

        void Collect(Battle b, float now)
        {
            seen.TryGetValue(b, out int last);
            if (b.FxSeq == last) return;
            // Only the newest events are worth drawing; at high warp thousands arrive per frame.
            int start = b.Fx.Count;
            int taken = 0;
            while (start > 0 && b.Fx[start - 1].Seq > last && taken < MaxNewPerBattlePerFrame)
            {
                start--;
                taken++;
            }
            for (int i = start; i < b.Fx.Count; i++) Spawn(b, b.Fx[i], now);
            seen[b] = b.FxSeq;
        }

        /// <summary>Drops a battle's seen-count once it is no longer live or kept in <see cref="War.Ended"/>,
        /// so the dictionary cannot grow without bound and never has to be wiped wholesale.</summary>
        void PruneSeen(War war)
        {
            if (seen.Count <= 64) return;
            staleSeen.Clear();
            foreach (var b in seen.Keys)
                if (!war.Battles.Contains(b) && !war.Ended.Contains(b)) staleSeen.Add(b);
            foreach (var b in staleSeen) seen.Remove(b);
        }

        void Spawn(Battle b, CombatFx fx, float now)
        {
            var side = fx.Side >= 0 && fx.Side < 2 ? b.SideLeaders[fx.Side] : null;
            var faction = side != null ? new Color(side.Color.R, side.Color.G, side.Color.B) : Color.white;
            var e = new Effect { Battle = b, Kind = fx.Kind, A = fx.A, B = fx.B, Size = fx.Size, Born = now, Unit = fx.Unit };
            switch (fx.Kind)
            {
                case FxKind.Beam:
                case FxKind.BeamMiss:
                    e.Color = Color.Lerp(faction, Color.white, 0.45f) * (fx.Size > 100 ? 1.6f : 1.1f);
                    e.Life = fx.Size > 100 ? 0.35f : 0.22f;
                    break;
                case FxKind.Railgun:
                case FxKind.RailgunMiss:
                    // A hypervelocity kinetic round, not an energy pulse: near-white, thinner, and
                    // gone faster than even a heavy laser beam.
                    e.Color = Color.Lerp(Color.white, new Color(0.75f, 0.85f, 1f), 0.3f) * 2.2f;
                    e.Life = 0.16f;
                    break;
                case FxKind.PointDefense:
                    e.Color = new Color(1f, 0.85f, 0.5f) * 0.8f;
                    e.Life = 0.08f;
                    break;
                case FxKind.Guns:
                    e.Color = Color.Lerp(faction, new Color(1f, 0.9f, 0.6f), 0.5f);
                    e.Life = 0.1f;
                    break;
                case FxKind.ShieldHit:
                    e.Color = new Color(0.35f, 0.7f, 1f) * 0.55f;
                    e.Life = 0.3f;
                    break;
                case FxKind.Impact:
                    e.Color = new Color(1f, 0.7f, 0.35f);
                    e.Life = 0.45f;
                    break;
                case FxKind.SubsystemDestroyed:
                    e.Color = new Color(1f, 0.6f, 0.25f) * 1.5f;
                    e.Life = 1.4f;
                    break;
                case FxKind.MissileKill:
                    e.Color = new Color(1f, 0.75f, 0.4f);
                    e.Life = 0.35f;
                    break;
                case FxKind.UnitDestroyed:
                    e.Color = new Color(1f, 0.8f, 0.5f) * 2f;
                    e.Life = 3.2f;
                    break;
                case FxKind.Breach:
                    e.Color = new Color(0.85f, 0.92f, 1f) * 1.2f;
                    e.Life = 2.2f;
                    break;
                case FxKind.PodBounce:
                    e.Color = new Color(0.4f, 0.75f, 1f);
                    e.Life = 0.5f;
                    break;
                case FxKind.Captured:
                    e.Color = Color.Lerp(faction, Color.white, 0.3f) * 1.4f;
                    e.Life = 2.5f;
                    break;
                case FxKind.Latch:
                    e.Color = new Color(1f, 0.9f, 0.6f);
                    e.Life = 0.8f;
                    break;
                case FxKind.Pulse:
                    e.Color = new Color(0.45f, 0.85f, 1f) * 1.3f;
                    e.Life = 3.5f;
                    break;
                case FxKind.Hail:
                    e.Color = new Color(1f, 1f, 0.8f) * 1.3f;
                    e.Life = 3f;
                    break;
                default:
                    return;
            }
            effects.Add(e);
        }

        void Draw(Effect e, float now)
        {
            float age = now - e.Born;
            float t = age / e.Life;
            float fade = 1 - t;
            switch (e.Kind)
            {
                case FxKind.Beam:
                case FxKind.BeamMiss:
                case FxKind.PointDefense:
                case FxKind.Guns:
                case FxKind.Railgun:
                case FxKind.RailgunMiss:
                {
                    var a = Point(e.Battle, e.A);
                    var bFull = Point(e.Battle, e.B);
                    // A beam sweeps onto its target across the first slice of its life instead of
                    // appearing full-length the instant it's fired; a kinetic round's own travelling
                    // Round entity (see DrawRounds) already reads as motion, so only the beam needs it.
                    bool isBeam = e.Kind == FxKind.Beam || e.Kind == FxKind.BeamMiss;
                    float sweepT = isBeam ? Mathf.Clamp01(age / Mathf.Max(e.Life * 0.3f, 1e-4f)) : 1f;
                    var b = Vector3.Lerp(a, bFull, sweepT);
                    float px = e.Kind == FxKind.PointDefense || e.Kind == FxKind.Guns ? 1.6f
                        : e.Kind == FxKind.Railgun || e.Kind == FxKind.RailgunMiss ? 1.8f
                        : e.Size > 100 ? 4f : 2.6f;
                    Ribbon(a, b, px, e.Color * fade);

                    // Muzzle flash and a brief recoil kick at the gun itself, for every discrete shot
                    // (not the continuous point-defence stream).
                    if (e.Kind != FxKind.PointDefense)
                    {
                        const float muzzleWindow = 0.08f;
                        if (age < muzzleWindow)
                        {
                            float k = 1 - age / muzzleWindow;
                            Sprite(a, PixelSize(a, 15f) * k, Color.white * k, Shape.Radial);
                            var kick = a - (bFull - a).normalized * PixelSize(a, 7f) * k;
                            Ribbon(kick, a, px * 1.6f, Color.white * k * 0.8f);
                        }
                    }

                    // A laser leaves a scorch where it actually hit; a miss has nothing to scorch.
                    if (e.Kind == FxKind.Beam)
                        Sprite(bFull, PixelSize(bFull, 6f) * (0.7f + 0.3f * t), new Color(0.3f, 0.17f, 0.1f) * fade, Shape.Radial);
                    break;
                }
                case FxKind.ShieldHit:
                {
                    var c = e.Unit != null && e.Unit.Present ? Point(e.Battle, e.Unit.Pos) : Point(e.Battle, e.B);
                    float r = (float)(e.Size * 0.75 * app.BattleKmScale);
                    Sprite(c, Mathf.Max(r, PixelSize(c, 10)), e.Color * fade, Shape.Ring);
                    break;
                }
                case FxKind.Breach:
                {
                    // Atmosphere venting from the hole: a bright puff and a jet streaming outward.
                    var c = Point(e.Battle, e.A);
                    var outward = Point(e.Battle, e.A + (e.A - e.B).Normalized * (0.3 + 1.2 * t)) - c;
                    float r = (float)(e.Size * app.BattleKmScale) * (0.3f + t);
                    Sprite(c, Mathf.Max(r, PixelSize(c, 10)), e.Color * fade * 0.8f, Shape.Radial);
                    Ribbon(c, c + outward, 3f, e.Color * fade);
                    break;
                }
                case FxKind.PodBounce:
                case FxKind.Captured:
                {
                    var c = e.Unit != null && e.Unit.Present ? Point(e.Battle, e.Unit.Pos) : Point(e.Battle, e.A);
                    float r = (float)(e.Size * 0.8 * app.BattleKmScale) * (e.Kind == FxKind.Captured ? 0.8f + t : 1f);
                    Sprite(c, Mathf.Max(r, PixelSize(c, e.Kind == FxKind.Captured ? 22 : 10)), e.Color * fade, Shape.Ring);
                    break;
                }
                case FxKind.Hail:
                {
                    // A hull hailing for terms: a slow-blinking signal ring around it, weapons cold.
                    var c = e.Unit != null && e.Unit.Present ? Point(e.Battle, e.Unit.Pos) : Point(e.Battle, e.A);
                    float r = (float)(e.Size * 1.1 * app.BattleKmScale);
                    float blink = Mathf.Sin(age * 6f) > 0 ? 1f : 0.35f;
                    Sprite(c, Mathf.Max(r, PixelSize(c, 26)), e.Color * fade * blink, Shape.Ring);
                    break;
                }
                case FxKind.Pulse:
                {
                    // The interdiction pulse: a shell racing outward far past the battle.
                    var c = Point(e.Battle, e.A);
                    float r = (float)(e.Size * app.BattleKmScale) * (0.1f + 3f * Mathf.Sqrt(t));
                    Sprite(c, Mathf.Max(r, PixelSize(c, 40 + 400 * t)), e.Color * fade, Shape.Ring);
                    if (age < 0.3f) Sprite(c, PixelSize(c, 50), Color.white * (1 - age / 0.3f), Shape.Radial);
                    break;
                }
                case FxKind.UnitDestroyed:
                {
                    var c = Point(e.Battle, e.A);
                    float r = (float)(e.Size * app.BattleKmScale) * (0.6f + 2.2f * Mathf.Sqrt(t));
                    Sprite(c, Mathf.Max(r, PixelSize(c, 24 * (0.5f + t))), e.Color * fade * fade, Shape.Radial);
                    if (age < 0.25f) Sprite(c, Mathf.Max(r * 3, PixelSize(c, 60)), Color.white * (1 - age / 0.25f), Shape.Radial);
                    break;
                }
                default:
                {
                    var c = Point(e.Battle, e.A);
                    float r = (float)(Mathf.Max(0.05f, (float)e.Size * (e.Kind == FxKind.Impact ? 0.002f : 1f)) * app.BattleKmScale) * (0.5f + t);
                    float minPx = e.Kind == FxKind.SubsystemDestroyed ? 14 : e.Kind == FxKind.Impact ? 5 : 6;
                    Sprite(c, Mathf.Max(r, PixelSize(c, minPx)), e.Color * fade, Shape.Radial);
                    break;
                }
            }
        }

        void DrawMissiles(Battle b)
        {
            foreach (var m in b.Missiles)
            {
                if (m.Gone) continue;
                var p = Point(b, m.Pos);
                var side = m.Side >= 0 && m.Side < 2 ? b.SideLeaders[m.Side] : null;
                var c = side != null ? Color.Lerp(new Color(side.Color.R, side.Color.G, side.Color.B), Color.white, 0.6f) : Color.white;
                bool pod = m.Kind == MissileKind.Pod;
                if (pod) c = new Color(1f, 0.75f, 0.35f);
                Sprite(p, PixelSize(p, pod ? 8 : m.Torpedo ? 7 : 5), c * 1.4f, Shape.Radial);
                var tail = Point(b, m.Pos - m.Vel * (pod ? 4.0 : m.Torpedo ? 3.0 : 2.0));
                Ribbon(tail, p, pod ? 2.2f : 1.4f, c * 0.5f);
            }
        }

        /// <summary>
        /// A railgun slug or autocannon round actually crossing the range: drawn as a moving tracer with
        /// a short trail, exactly like a missile, rather than the single appear-and-fade line a direct-fire
        /// hit/miss event still gets for its own instant muzzle flash (see <see cref="Draw"/>). A round that
        /// missed keeps flying and fading here same as a round that's still closing on a hit.
        /// </summary>
        void DrawRounds(Battle b)
        {
            foreach (var r in b.Rounds)
            {
                if (r.Gone) continue;
                var p = Point(b, r.Pos);
                var side = r.Side >= 0 && r.Side < 2 ? b.SideLeaders[r.Side] : null;
                var faction = side != null ? new Color(side.Color.R, side.Color.G, side.Color.B) : Color.white;
                var c = r.Railgun ? Color.Lerp(Color.white, new Color(0.75f, 0.85f, 1f), 0.3f) * 2.2f
                    : Color.Lerp(faction, new Color(1f, 0.9f, 0.6f), 0.5f);
                Sprite(p, PixelSize(p, r.Railgun ? 3f : 2f), c * 1.2f, Shape.Radial);
                var tail = Point(b, r.Pos - r.Vel * (r.Railgun ? 0.05 : 0.08));
                Ribbon(tail, p, r.Railgun ? 1.8f : 1.4f, c * 0.6f);
            }
        }

        Vector3 Point(Battle b, DVec3 local) => app.Relative(app.BattleScene(b, local));

        float PixelSize(Vector3 p, float pixels) => pixels * Mathf.Max(p.magnitude, 1e-6f) / (float)app.PixelsPerUnitAtUnitDistance();

        void Sprite(Vector3 c, float radius, Color color, Shape shape)
        {
            var cam = app.UnityCamera.transform;
            var right = cam.right * radius;
            var up = cam.up * radius;
            Quad(c - right - up, c + right - up, c + right + up, c - right + up, color, shape);
        }

        void Ribbon(Vector3 a, Vector3 b, float pixels, Color color)
        {
            var mid = (a + b) * 0.5f;
            var along = b - a;
            if (along.sqrMagnitude < 1e-12f) return;
            var side = Vector3.Cross(along, mid).normalized;
            float wa = PixelSize(a, pixels) * 0.5f, wb = PixelSize(b, pixels) * 0.5f;
            int i = vertices.Count;
            vertices.Add(a - side * wa);
            vertices.Add(b - side * wb);
            vertices.Add(b + side * wb);
            vertices.Add(a + side * wa);
            AddCorners(color, Shape.Beam);
            Indices(i);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Shape shape)
        {
            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            AddCorners(color, shape);
            Indices(i);
        }

        void AddCorners(Color color, Shape shape)
        {
            float z = (float)shape;
            for (int k = 0; k < 4; k++) colors.Add(color);
            uvs.Add(new Vector3(0, 0, z));
            uvs.Add(new Vector3(1, 0, z));
            uvs.Add(new Vector3(1, 1, z));
            uvs.Add(new Vector3(0, 1, z));
        }

        void Indices(int i)
        {
            indices.Add(i);
            indices.Add(i + 1);
            indices.Add(i + 2);
            indices.Add(i);
            indices.Add(i + 2);
            indices.Add(i + 3);
        }
    }
}
