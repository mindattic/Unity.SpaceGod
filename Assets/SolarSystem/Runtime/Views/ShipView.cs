using System.Collections.Generic;
using SolarSystem.Core;
using SolarSystem.Rendering;
using UnityEngine;
using Subsystem = SolarSystem.Core.Subsystem;

namespace SolarSystem.Views
{
    /// <summary>
    /// The 3D model of one ship. Warships carry their hardpoints as separate parts: turrets traverse
    /// and elevate onto whatever they are shooting, a hardpoint that is shot away leaves a blackened
    /// stub and a fire, engines glow while they work, the hull shows more breaches as its hit points
    /// fall, and a destroyed ship breaks into bow and stern that tumble apart and burn out.
    /// </summary>
    public sealed class ShipView
    {
        public readonly Ship Ship;
        public readonly GameObject Root;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly MeshRenderer plume;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Transform stern, bow;
        readonly Part[] parts;
        readonly List<Glow> glows = new List<Glow>();
        readonly Glow[] breaches;
        bool visible = true;
        float wreckSpin;

        // Shield ring: one wedge per shield generator, built once (geometry is fixed) and recoloured
        // each frame from that generator's own charge. Built for every ship, not just warships, so a
        // freighter's shields show too.
        readonly List<Subsystem> shieldGenerators = new List<Subsystem>();
        MeshRenderer shieldRingRenderer;
        Mesh shieldRingMesh;
        int[] shieldWedgeStart, shieldWedgeCount;
        Color[] shieldVertexColors;

        static Mesh plumeMesh, quadMesh;
        static Material plumeMaterial, fireMaterial, engineMaterial, shieldRingMaterial;

        static readonly int EngineGlowId = Shader.PropertyToID("_EngineGlow");
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");
        static readonly int ColorId = Shader.PropertyToID("_Tint");

        /// <summary>Breaches open as the hull falls below these fractions.</summary>
        static readonly float[] BreachAt = { 0.85f, 0.7f, 0.55f, 0.4f, 0.28f, 0.16f };

        sealed class Part
        {
            public Subsystem Sub;
            public Transform Mount, Barrels;
            public Renderer[] Renderers;
            public GameObject Scar;
            public Glow Fire;
            public bool ShownDestroyed;
            public bool Turret;
            public Quaternion Rest;
        }

        sealed class Glow
        {
            public Transform T;
            public Renderer R;
            public float Size, Phase;
            public Part Owner;
            public bool Engine;
        }

        public ShipView(Ship ship, Transform parent)
        {
            Ship = ship;
            Root = new GameObject(ship.Name);
            Root.transform.SetParent(parent, false);
            ShipModels.Build(ship, Root.transform, renderers, out stern, out bow);
            EnsureStatics();

            var plumeGo = new GameObject("Plume");
            plumeGo.transform.SetParent(Root.transform, false);
            plumeGo.transform.localPosition = new Vector3(0, 0, -0.5f);
            plumeGo.transform.localScale = new Vector3(0.12f, 0.12f, 0.6f);
            plumeGo.AddComponent<MeshFilter>().sharedMesh = plumeMesh;
            plume = plumeGo.AddComponent<MeshRenderer>();
            plume.sharedMaterial = plumeMaterial;
            plume.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            BuildShieldRing();

            if (bow == null)
            {
                parts = new Part[0];
                breaches = new Glow[0];
                return;
            }

            var subs = ship.Combat.Subsystems;
            parts = new Part[subs.Count];
            for (int i = 0; i < subs.Count; i++) parts[i] = BuildPart(subs[i]);

            // Breach points along the upper hull, fixed per ship so the damage reads consistently.
            var rng = new System.Random(ship.Id * 7919 + 13);
            breaches = new Glow[BreachAt.Length];
            for (int i = 0; i < breaches.Length; i++)
            {
                float z = (float)(rng.NextDouble() * 0.8 - 0.42);
                float x = (float)(rng.NextDouble() - 0.5) * 0.09f;
                float y = (float)(rng.NextDouble() * 0.5 - 0.1) * 0.06f;
                breaches[i] = NewGlow(SectionFor(z), new Vector3(x, y, z), 0.035f, fireMaterial, null);
            }
        }

        static void EnsureStatics()
        {
            plumeMesh ??= MeshFactory.Plume();
            quadMesh ??= MeshFactory.Quad();
            if (plumeMaterial != null) return;
            plumeMaterial = GlowMaterial("Plume", new Color(0.45f, 0.75f, 1f), 1.4f, 1.6f);
            fireMaterial = GlowMaterial("Fire", new Color(1f, 0.5f, 0.18f), 1.6f, 1.8f);
            engineMaterial = GlowMaterial("EngineGlow", new Color(0.5f, 0.8f, 1f), 1.5f, 2.2f);
            shieldRingMaterial = Materials.MarkShared(Materials.Create(Materials.Fx, "ShieldRing"));
        }

        static Material GlowMaterial(string name, Color c, float intensity, float falloff)
        {
            var m = Materials.MarkShared(Materials.Create(Materials.Glow, name));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Falloff", falloff);
            return m;
        }

        /// <summary>
        /// Builds the ring's geometry once: one flat annular wedge per shield generator, in the hull's
        /// own normalized (nose +Z, up +Y) space, just outside the hull. Every vertex shares one UV so
        /// the Fx shader's radial shape term comes out a constant multiplier everywhere — the wedge
        /// geometry itself is what draws the ring; the shader just makes it glow additively from the
        /// vertex colour, which is where each generator's own charge gets shown (recoloured every frame
        /// in <see cref="UpdateShieldRing"/>, never rebuilt).
        /// </summary>
        void BuildShieldRing()
        {
            foreach (var s in Ship.Combat.Subsystems)
                if (s.Kind == SubsystemKind.ShieldGenerator) shieldGenerators.Add(s);
            if (shieldGenerators.Count == 0) return;

            // Size the ring off the hull's own (local) bounds rather than a guessed constant, so it
            // clears whatever the actual mesh's proportions are — hull shapes vary a lot by design.
            var hullBounds = new Bounds();
            bool any = false;
            foreach (var r in renderers)
            {
                if (!any) { hullBounds = r.localBounds; any = true; }
                else hullBounds.Encapsulate(r.localBounds);
            }
            float hullRadius = any ? Mathf.Max(Mathf.Abs(hullBounds.extents.x), Mathf.Abs(hullBounds.extents.z)) : 0.5f;
            float innerR = hullRadius * 1.15f, outerR = hullRadius * 1.3f;
            const int segsPerWedge = 6;
            const float gapFraction = 0.92f; // a hair narrower than the mechanical arc: a visible seam between neighbours
            var verts = new List<Vector3>();
            var uvs = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            shieldWedgeStart = new int[shieldGenerators.Count];
            shieldWedgeCount = new int[shieldGenerators.Count];

            for (int gi = 0; gi < shieldGenerators.Count; gi++)
            {
                var def = shieldGenerators[gi].Def;
                shieldWedgeStart[gi] = verts.Count;
                float halfDeg = (float)(def.ArcWidthDeg * 0.5 * gapFraction);
                float centerDeg = (float)def.ArcCenterDeg;
                float a0 = (centerDeg - halfDeg) * Mathf.Deg2Rad;
                float a1 = (centerDeg + halfDeg) * Mathf.Deg2Rad;
                for (int k = 0; k <= segsPerWedge; k++)
                {
                    float t = Mathf.Lerp(a0, a1, (float)k / segsPerWedge);
                    float s = Mathf.Sin(t), c = Mathf.Cos(t);
                    int baseIdx = verts.Count;
                    verts.Add(new Vector3(s * innerR, 0, c * innerR));
                    verts.Add(new Vector3(s * outerR, 0, c * outerR));
                    uvs.Add(new Vector3(0.5f, 0.5f, 0f));
                    uvs.Add(new Vector3(0.5f, 0.5f, 0f));
                    cols.Add(Color.clear);
                    cols.Add(Color.clear);
                    if (k > 0)
                    {
                        int p = baseIdx - 2;
                        // Both windings, so the ring reads whichever side the camera is on.
                        tris.Add(p); tris.Add(p + 1); tris.Add(baseIdx + 1);
                        tris.Add(p); tris.Add(baseIdx + 1); tris.Add(baseIdx);
                        tris.Add(p); tris.Add(baseIdx + 1); tris.Add(p + 1);
                        tris.Add(p); tris.Add(baseIdx); tris.Add(baseIdx + 1);
                    }
                }
                shieldWedgeCount[gi] = verts.Count - shieldWedgeStart[gi];
            }

            shieldRingMesh = Materials.Own(new Mesh { name = "ShieldRing" });
            shieldRingMesh.SetVertices(verts);
            shieldRingMesh.SetUVs(0, uvs);
            shieldRingMesh.SetColors(cols);
            shieldRingMesh.SetTriangles(tris, 0);
            shieldRingMesh.RecalculateBounds();

            var go = new GameObject("ShieldRing");
            go.transform.SetParent(Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = shieldRingMesh;
            shieldRingRenderer = go.AddComponent<MeshRenderer>();
            shieldRingRenderer.sharedMaterial = shieldRingMaterial;
            shieldRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shieldRingRenderer.enabled = false;
            shieldVertexColors = cols.ToArray();
        }

        /// <summary>Recolours each wedge from its own generator's live charge; only shown while fighting.</summary>
        void UpdateShieldRing(bool fighting)
        {
            if (shieldRingRenderer == null) return;
            shieldRingRenderer.enabled = fighting && visible;
            if (!fighting || !visible) return;
            for (int gi = 0; gi < shieldGenerators.Count; gi++)
            {
                var gen = shieldGenerators[gi];
                float frac = !gen.Destroyed && gen.MaxShield > 0 ? (float)(gen.Shield / gen.MaxShield) : 0f;
                var c = gen.Destroyed
                    ? Color.clear
                    : Color.Lerp(new Color(0.1f, 0.25f, 0.5f, 0.22f), new Color(0.4f, 0.75f, 1f, 1f), frac);
                int start = shieldWedgeStart[gi], count = shieldWedgeCount[gi];
                for (int v = 0; v < count; v++) shieldVertexColors[start + v] = c;
            }
            shieldRingMesh.SetColors(shieldVertexColors);
        }

        Transform SectionFor(float z) => bow != null && z >= ShipMeshFactory.SplitZ ? bow : stern;

        Part BuildPart(Subsystem sub)
        {
            var d = sub.Def;
            var part = new Part { Sub = sub };
            var section = SectionFor((float)d.Z);
            var pos = new Vector3((float)d.X, (float)d.Y, (float)d.Z);
            // Mounts under the keel hang upside down.
            bool under = d.Y < -0.005;
            part.Rest = Quaternion.Euler(0, d.FacesAft ? 180 : 0, under ? 180 : 0);

            if (sub.Kind == SubsystemKind.Engine)
            {
                var g = NewGlow(section, pos + new Vector3(0, 0, -0.035f), 0.06f, engineMaterial, part);
                g.Engine = true;
                part.Renderers = new Renderer[0];
            }
            else if (sub.Kind == SubsystemKind.Reactor)
            {
                part.Renderers = new Renderer[0];
            }
            else
            {
                var mount = new GameObject(SubsystemDef.Name(sub.Kind)).transform;
                mount.SetParent(section, false);
                mount.localPosition = pos;
                mount.localRotation = part.Rest;
                part.Mount = mount;
                var list = new List<Renderer> { ShipModels.Part(mount, ShipMeshFactory.GetPart(sub.Kind, Ship.Faction, false), "Base") };
                part.Turret = sub.Kind == SubsystemKind.HeavyLaser || sub.Kind == SubsystemKind.LaserTurret || sub.Kind == SubsystemKind.PointDefense;
                if (part.Turret)
                {
                    var barrels = new GameObject("Barrels").transform;
                    barrels.SetParent(mount, false);
                    barrels.localPosition = new Vector3(0, sub.Kind == SubsystemKind.HeavyLaser ? 0.014f : sub.Kind == SubsystemKind.LaserTurret ? 0.009f : 0.008f, 0);
                    part.Barrels = barrels;
                    list.Add(ShipModels.Part(barrels, ShipMeshFactory.GetPart(sub.Kind, Ship.Faction, true), "Barrels"));
                }
                part.Renderers = list.ToArray();
            }

            var scar = ShipModels.Part(section, ShipMeshFactory.GetScar(), "Scar");
            scar.transform.localPosition = pos;
            scar.transform.localRotation = part.Rest;
            scar.enabled = false;
            part.Scar = scar.gameObject;
            part.Fire = NewGlow(section, pos + new Vector3(0, under ? -0.012f : 0.012f, 0), 0.05f, fireMaterial, part);
            part.Fire.R.enabled = false;
            return part;
        }

        Glow NewGlow(Transform parent, Vector3 localPos, float size, Material material, Part owner)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var g = new Glow { T = go.transform, R = mr, Size = size, Phase = Random.value * 10, Owner = owner };
            mr.enabled = false;
            glows.Add(g);
            return g;
        }

        // ---------------------------------------------------------------- per frame

        public void Apply(Vector3 relative, Quaternion rotation, float length, bool burning, bool highlighted)
        {
            var t = Root.transform;
            t.localPosition = relative;
            t.localRotation = rotation;
            t.localScale = Vector3.one * length;
            stern.localPosition = Vector3.zero;
            stern.localRotation = Quaternion.identity;
            if (bow != null)
            {
                bow.localPosition = Vector3.zero;
                bow.localRotation = Quaternion.identity;
            }

            block.SetFloat(EngineGlowId, burning ? 2.2f : 0.35f);
            block.SetFloat(HighlightId, highlighted ? 1f : 0f);
            block.SetColor(ColorId, Color.white);
            SetBlock();
            plume.enabled = visible && burning;
        }

        /// <summary>
        /// Shows the hull's combat state: turrets on their targets (or at rest), lost hardpoints as
        /// scars and fires, engines that still work glowing, breaches by hull damage.
        /// </summary>
        public void ApplyCombat(ShipCombat combat, bool fighting, bool thrusting, float now, float dt)
        {
            if (!visible) return;
            UpdateShieldRing(fighting);
            if (bow == null) return;
            var inverse = Quaternion.Inverse(Root.transform.rotation);
            foreach (var p in parts)
            {
                var sub = p.Sub;
                if (sub.Destroyed != p.ShownDestroyed)
                {
                    p.ShownDestroyed = sub.Destroyed;
                    foreach (var r in p.Renderers) r.enabled = !sub.Destroyed;
                    p.Scar.GetComponent<Renderer>().enabled = sub.Destroyed && p.Mount != null;
                    p.Fire.R.enabled = sub.Destroyed;
                }
                if (p.Turret && !sub.Destroyed) Aim(p, sub, fighting, inverse, dt);
            }
            float hull = (float)combat.HullFraction;
            for (int i = 0; i < breaches.Length; i++) breaches[i].R.enabled = hull < BreachAt[i];
            foreach (var g in glows)
            {
                if (!g.Engine) continue;
                bool alive = !g.Owner.Sub.Destroyed;
                g.R.enabled = alive;
                g.Size = thrusting ? 0.075f : 0.045f;
            }
            Billboard(now);
        }

        /// <summary>Traverses the mount about its own up axis and elevates the barrels, both rate-limited.</summary>
        void Aim(Part p, Subsystem sub, bool fighting, Quaternion inverseShip, float dt)
        {
            Quaternion goalMount = p.Rest, goalBarrels = Quaternion.identity;
            if (fighting && sub.Aim.LengthSquared > 1e-9)
            {
                var a = sub.Aim.SwapYZ;
                var world = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
                // Into the section's frame (sections stay aligned with the root unless wrecked).
                var local = inverseShip * world;
                var mountLocal = Quaternion.Inverse(Quaternion.Euler(0, 0, p.Rest.eulerAngles.z)) * local;
                var flat = new Vector3(mountLocal.x, 0, mountLocal.z);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    goalMount = Quaternion.Euler(0, 0, p.Rest.eulerAngles.z) * Quaternion.LookRotation(flat.normalized, Vector3.up);
                    float elevation = Mathf.Atan2(mountLocal.y, flat.magnitude) * Mathf.Rad2Deg;
                    goalBarrels = Quaternion.Euler(-Mathf.Clamp(elevation, -8f, 80f), 0, 0);
                }
            }
            float traverse = (sub.Kind == SubsystemKind.PointDefense ? 360f : sub.Kind == SubsystemKind.HeavyLaser ? 50f : 90f) * dt;
            p.Mount.localRotation = Quaternion.RotateTowards(p.Mount.localRotation, goalMount, traverse);
            p.Barrels.localRotation = Quaternion.RotateTowards(p.Barrels.localRotation, goalBarrels, traverse);
        }

        void Billboard(float now)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var camPos = cam.transform.position;
            foreach (var g in glows)
            {
                if (!g.R.enabled) continue;
                var p = g.T.position;
                g.T.rotation = Quaternion.LookRotation(p - camPos, cam.transform.up);
                float flicker = g.Engine ? 1f : 0.75f + 0.25f * Mathf.Sin(now * 13f + g.Phase) * Mathf.Sin(now * 7.3f + g.Phase * 2f);
                g.T.localScale = Vector3.one * (g.Size * flicker);
            }
        }

        /// <summary>
        /// A destroyed ship: bow and stern drift apart and tumble, the hull blackens, fires burn at
        /// the break and on the wreck, then the pieces burn out and vanish.
        /// </summary>
        /// <returns>False once the wreck has burnt out.</returns>
        public bool Wreck(Vector3 relative, Quaternion rotation, float length, float age, float now)
        {
            const float Life = 28f;
            if (age > Life)
            {
                SetVisible(false);
                return false;
            }
            SetVisible(true);
            if (shieldRingRenderer != null) shieldRingRenderer.enabled = false;
            var t = Root.transform;
            t.localPosition = relative;
            t.localRotation = rotation;
            t.localScale = Vector3.one * length;
            if (wreckSpin == 0) wreckSpin = 6f + (Ship.Id % 5) * 3f;
            float drift = Mathf.Sqrt(age) * 0.07f;
            if (bow != null)
            {
                bow.localPosition = new Vector3(0.01f * age, 0.015f * age, drift);
                bow.localRotation = Quaternion.Euler(age * wreckSpin * 0.6f, age * wreckSpin * 0.3f, age * wreckSpin);
                stern.localPosition = new Vector3(-0.01f * age, -0.01f * age, -drift);
                stern.localRotation = Quaternion.Euler(-age * wreckSpin * 0.4f, 0, -age * wreckSpin * 0.7f);
                foreach (var p in parts)
                {
                    foreach (var r in p.Renderers) r.enabled = false;
                    p.Scar.GetComponent<Renderer>().enabled = p.Mount != null;
                    p.Fire.R.enabled = age < Life * 0.7f && (p.Sub.Index % 2 == 0);
                }
                foreach (var g in breaches) g.R.enabled = age < Life * 0.8f;
                foreach (var g in glows) if (g.Engine) g.R.enabled = false;
                Billboard(now);
            }
            else
            {
                stern.localRotation = Quaternion.Euler(age * 9f, age * 5f, age * 14f);
            }
            float burnt = Mathf.Clamp01(age / 1.5f);
            block.SetFloat(EngineGlowId, 0f);
            block.SetFloat(HighlightId, 0f);
            block.SetColor(ColorId, Color.Lerp(Color.white, new Color(0.22f, 0.2f, 0.19f), burnt));
            SetBlock();
            plume.enabled = false;
            return true;
        }

        /// <summary>A boarded hulk left adrift: dark, cold, slowly tumbling, never burning out.</summary>
        public void Derelict(Vector3 relative, Quaternion rotation, float length, float now)
        {
            SetVisible(true);
            if (shieldRingRenderer != null) shieldRingRenderer.enabled = false;
            var t = Root.transform;
            t.localPosition = relative;
            t.localRotation = rotation * Quaternion.Euler(now * 1.3f, now * 0.7f, now * 2.1f);
            t.localScale = Vector3.one * length;
            stern.localPosition = Vector3.zero;
            stern.localRotation = Quaternion.identity;
            if (bow != null)
            {
                bow.localPosition = Vector3.zero;
                bow.localRotation = Quaternion.identity;
            }
            foreach (var g in glows) g.R.enabled = false;
            block.SetFloat(EngineGlowId, 0f);
            block.SetFloat(HighlightId, 0f);
            block.SetColor(ColorId, new Color(0.35f, 0.33f, 0.32f));
            SetBlock();
            plume.enabled = false;
        }

        void SetBlock()
        {
            foreach (var r in renderers) r.SetPropertyBlock(block);
            foreach (var p in parts)
                foreach (var r in p.Renderers) r.SetPropertyBlock(block);
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            foreach (var r in renderers) r.enabled = value;
            foreach (var p in parts)
            {
                foreach (var r in p.Renderers) r.enabled = value && !p.Sub.Destroyed;
                p.Scar.GetComponent<Renderer>().enabled = value && p.Sub.Destroyed && p.Mount != null;
                p.Fire.R.enabled = value && p.Sub.Destroyed;
            }
            if (!value)
            {
                plume.enabled = false;
                foreach (var g in glows) g.R.enabled = false;
                if (shieldRingRenderer != null) shieldRingRenderer.enabled = false;
            }
            else
            {
                foreach (var p in parts) p.ShownDestroyed = !p.Sub.Destroyed;
            }
        }
    }
}
