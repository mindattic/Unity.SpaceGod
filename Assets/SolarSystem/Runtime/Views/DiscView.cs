using System;
using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Views
{
    /// <summary>
    /// Draws a system's debris belts and discs (<see cref="StarSystem.Discs"/>: Epsilon Eridani's
    /// three belts, Tau Ceti's disc) as a band of faint concentric rings in the disc's real plane,
    /// from its inner to its outer edge, mapped through the scale model like everything else. Shown
    /// with the orbits.
    /// </summary>
    public sealed class DiscView
    {
        const int Rings = 6, Points = 161;
        static readonly Color DustColor = new Color(0.82f, 0.72f, 0.58f, 0.32f);

        readonly SolarSystemApp app;
        readonly List<(DebrisDisc disc, LineStrip[] rings)> discs = new List<(DebrisDisc, LineStrip[])>();
        readonly Vector3[] points = new Vector3[Points];
        double laidOutBlend = double.NaN;

        public DiscView(SolarSystemApp app, Transform parent)
        {
            this.app = app;
            foreach (var d in app.ActiveSystem.Discs)
            {
                var rings = new LineStrip[Rings];
                for (int i = 0; i < Rings; i++) rings[i] = new LineStrip(d.Name + " " + i, parent);
                discs.Add((d, rings));
            }
        }

        public void Update(bool visible)
        {
            bool relayout = laidOutBlend != app.Scale.Blend;
            foreach (var (disc, rings) in discs)
            {
                var host = app.Catalog[disc.HostIndex];
                var at = app.Relative(app.BodyScene(disc.HostIndex));
                var u = Perpendicular(disc.Normal);
                var v = DVec3.Cross(disc.Normal, u);
                for (int i = 0; i < Rings; i++)
                {
                    var ring = rings[i];
                    ring.Visible = visible;
                    if (!visible) continue;
                    if (relayout || ring.Capacity == 0)
                    {
                        double radius = disc.InnerKm + (disc.OuterKm - disc.InnerKm) * i / (Rings - 1);
                        for (int k = 0; k < Points; k++)
                        {
                            double a = Math.PI * 2 * k / (Points - 1);
                            var o = app.Scale.LocalOffset(host, (u * Math.Cos(a) + v * Math.Sin(a)) * radius);
                            points[k] = new Vector3((float)o.X, (float)o.Y, (float)o.Z);
                        }
                        // The edges read a little brighter than the dust between them.
                        bool edge = i == 0 || i == Rings - 1;
                        var c = new Color(DustColor.r, DustColor.g, DustColor.b, DustColor.a * (edge ? 1f : 0.55f));
                        ring.SetPoints(points, Points, c);
                    }
                    ring.SetTint(Color.white);
                    ring.Root.transform.localPosition = at;
                    ring.Root.transform.localRotation = Quaternion.identity;
                }
            }
            if (visible) laidOutBlend = app.Scale.Blend;
        }

        static DVec3 Perpendicular(DVec3 n)
        {
            var reference = Math.Abs(n.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            return DVec3.Cross(reference, n).Normalized;
        }
    }
}
