using System;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.UI
{
    /// <summary>
    /// Procedurally drawn white icon sprites (tinted at draw time) that stand in for anything too
    /// small or too far away to see: each ship class and body kind has a distinct silhouette.
    /// </summary>
    public static class Icons
    {
        const int Size = 64;

        static Texture2D ring, dot, star, diamond, triangle, chevron, carrier, square, hexagon, white;

        public static Texture2D White => white ??= Solid();

        public static Texture2D ForBody(BodyDefinition body)
        {
            switch (body.Kind)
            {
                case BodyKind.Star: return star ??= Draw(StarSdf);
                case BodyKind.Moon: return dot ??= Draw(p => Ring(p, 0.42f, 0.16f));
                default: return ring ??= Draw(p => Ring(p, 0.62f, 0.14f));
            }
        }

        public static Texture2D ForShip(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier: return carrier ??= Draw(CarrierSdf);
                case ShipClass.Battleship: return diamond ??= Draw(p => Mathf.Abs(p.x) * 1.25f + Mathf.Abs(p.y) - 0.85f);
                case ShipClass.Cruiser: return triangle ??= Draw(p => Triangle(p, 0.8f));
                case ShipClass.Destroyer: return chevron ??= Draw(Chevron);
                case ShipClass.Miner: return hexagon ??= Draw(Hexagon);
                case ShipClass.Courier: return dot ??= Draw(p => Ring(p, 0.42f, 0.16f));
                default: return square ??= Draw(p => Outline(Box(p, new Vector2(0.62f, 0.62f)), 0.14f));
            }
        }

        /// <summary>On-screen icon size in pixels.</summary>
        public static float PixelSize(ShipClass c)
        {
            switch (c)
            {
                case ShipClass.Carrier: return 22;
                case ShipClass.Battleship: return 20;
                case ShipClass.Cruiser: return 16;
                case ShipClass.Destroyer: return 13;
                case ShipClass.Courier: return 9;
                default: return 12;
            }
        }

        public static float PixelSize(BodyDefinition body)
        {
            switch (body.Kind)
            {
                case BodyKind.Star: return 30;
                case BodyKind.Moon: return 10;
                case BodyKind.DwarfPlanet: return 13;
                default: return 17;
            }
        }

        static Texture2D Solid()
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            return t;
        }

        /// <summary>Rasterises a signed distance field (negative inside, in [-1,1] icon space) with 1.5 px anti-aliasing.</summary>
        static Texture2D Draw(Func<Vector2, float> sdf)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[Size * Size];
            float aa = 1.5f * 2f / Size;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size * 2 - 1, (y + 0.5f) / Size * 2 - 1);
                    float a = Mathf.Clamp01(0.5f - sdf(p) / aa);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static float Ring(Vector2 p, float radius, float thickness) => Mathf.Abs(p.magnitude - radius) - thickness * 0.5f;
        static float Outline(float d, float thickness) => Mathf.Abs(d) - thickness * 0.5f;

        static float Box(Vector2 p, Vector2 half)
        {
            var d = new Vector2(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y);
            return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0);
        }

        static float Triangle(Vector2 p, float r)
        {
            // Equilateral, pointing up.
            const float k = 1.7320508f;
            p.x = Mathf.Abs(p.x) - r;
            p.y = p.y + r / k;
            if (p.x + k * p.y > 0) p = new Vector2(p.x - k * p.y, -k * p.x - p.y) / 2;
            p.x -= Mathf.Clamp(p.x, -2 * r, 0);
            return -p.magnitude * Mathf.Sign(p.y);
        }

        static float Chevron(Vector2 p)
        {
            float outer = Triangle(p, 0.85f);
            float inner = Triangle(p + new Vector2(0, 0.45f), 0.6f);
            return Mathf.Max(outer, -inner);
        }

        static float Hexagon(Vector2 p)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float d = Mathf.Max(q.x * 0.866f + q.y * 0.5f, q.y) - 0.62f;
            return Outline(d, 0.16f);
        }

        static float StarSdf(Vector2 p)
        {
            float core = p.magnitude - 0.3f;
            float rays = Mathf.Min(Box(p, new Vector2(0.92f, 0.05f)), Box(p, new Vector2(0.05f, 0.92f)));
            return Mathf.Min(core, rays);
        }

        static float CarrierSdf(Vector2 p)
        {
            float body = Outline(Box(p, new Vector2(0.85f, 0.42f)), 0.14f);
            float deck = Box(p, new Vector2(0.6f, 0.05f));
            return Mathf.Min(body, deck);
        }
    }
}
