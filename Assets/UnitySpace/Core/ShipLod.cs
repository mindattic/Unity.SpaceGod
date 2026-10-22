using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// How a ship is drawn by its size on screen (U.SPC-T376): its full 3D model while it is big enough to read, and
    /// below that a flat class icon in its faction's colour with its name beside it, which the player can click for
    /// its full card. The swap point and which icons are named are settings (LOD &amp; render budget), so the trade
    /// between detail and frame rate is the player's to make; the 3D view, the HUD's icons and picking all ask here,
    /// so they always agree on which form a ship is in.
    /// </summary>
    [SettingsSource]
    public static class ShipLod
    {
        /// <summary>Below this many pixels long on screen a ship is drawn as its icon, not its model.</summary>
        public static readonly RangeSetting IconBelowPx = new RangeSetting("lod.shipIconPx", SettingGroups.RenderBudget,
            "Ship model to icon", 5, 2, 40, 1,
            "A ship shorter than this on screen is drawn as a flat faction icon with its name instead of its 3D model. Higher is faster in a crowded system.",
            v => v.ToString("0") + " px", "lod icon billboard model ship distance performance");

        public const int NamesOff = 0, NamesWarships = 1, NamesAll = 2;

        /// <summary>Which ships drawn as icons have their names beside them.</summary>
        public static readonly ChoiceSetting IconNames = new ChoiceSetting("lod.shipIconNames", SettingGroups.RenderBudget,
            "Ship icon names", new[] { "Off", "Warships", "All" }, NamesAll,
            "Which far-off ships drawn as icons are named beside their icon. A name that would overlap one already drawn is left off.",
            "label name icon ship tag");

        /// <summary>How many pixels long a ship of <paramref name="length"/> scene units looks at <paramref name="depth"/>, given the camera's pixels per unit at unit distance.</summary>
        public static double ApparentPx(double length, double depth, double pixelsPerUnit) =>
            length / Math.Max(depth, 1e-9) * pixelsPerUnit;

        /// <summary>Whether a ship <paramref name="px"/> pixels long on screen is drawn as its 3D model (else as its icon).</summary>
        public static bool DrawsModel(double px) => DrawsModel(px, IconBelowPx.Value);

        public static bool DrawsModel(double px, double iconBelowPx) => px >= iconBelowPx;

        /// <summary>Whether a ship drawn as an icon is named beside it, by <see cref="IconNames"/>. An escort is named only when it is all there is to see of its group: it is folded into its leader's icon otherwise.</summary>
        public static bool NamesIcon(bool military, bool escort) => NamesIcon(IconNames.Index, military, escort);

        public static bool NamesIcon(int mode, bool military, bool escort)
        {
            if (escort) return false;
            return mode == NamesAll || mode == NamesWarships && military;
        }

        /// <summary>
        /// A ship's weapons for its card: each kind with how many it mounts, the ones shot away counted as lost, e.g.
        /// "2 heavy laser, 4 point defence (1 lost)". "Unarmed" for a hull with none.
        /// </summary>
        public static string Armament(IReadOnlyList<Subsystem> subsystems)
        {
            var order = new List<SubsystemKind>();
            var mounted = new Dictionary<SubsystemKind, int>();
            var lost = new Dictionary<SubsystemKind, int>();
            if (subsystems != null)
                foreach (var s in subsystems)
                {
                    if (s == null || !s.Def.IsWeapon) continue;
                    if (!mounted.ContainsKey(s.Kind))
                    {
                        order.Add(s.Kind);
                        mounted[s.Kind] = 0;
                        lost[s.Kind] = 0;
                    }
                    mounted[s.Kind]++;
                    if (s.Destroyed) lost[s.Kind]++;
                }
            if (order.Count == 0) return "Unarmed";
            var sb = new StringBuilder();
            foreach (var k in order)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(mounted[k]).Append(' ').Append(SubsystemDef.Name(k).ToLowerInvariant());
                if (lost[k] > 0) sb.Append(" (").Append(lost[k]).Append(" lost)");
            }
            return sb.ToString();
        }
    }
}
