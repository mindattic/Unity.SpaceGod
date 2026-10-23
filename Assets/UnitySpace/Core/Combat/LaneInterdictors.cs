using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// What throws a ship out of a hyperlane (U.SPC-T479): a field projector that disrupts the lane's magnetic field, carried
    /// by a hull built for it, the <see cref="ShipClass.Interdictor"/>. A group lying in wait beside a lane (see the lane
    /// ambushes in <see cref="War"/>) can pulse only while one of its hulls carries a working projector; the rest of the group
    /// fights what the pulse pulls out, in the battle beside the lane. A lane's route is predictable, so the projector is the
    /// pirate's and raider's favourite: every raider band keeps one for its lane ambushes. Navies fly one in a lane picket, to
    /// pull raiders and enemies off the lanes out of their systems. Another hull class is given a projector with
    /// <see cref="RegisterEmitter"/>; the tunables are in the settings menu's AI group.
    /// </summary>
    [SettingsSource]
    public static class LaneInterdictors
    {
        static readonly HashSet<ShipClass> emitters = new HashSet<ShipClass> { ShipClass.Interdictor };

        /// <summary>Fits every hull of <paramref name="cls"/> with a field projector (or, with <paramref name="emits"/> false, takes it away).</summary>
        public static void RegisterEmitter(ShipClass cls, bool emits = true)
        {
            if (emits) emitters.Add(cls);
            else emitters.Remove(cls);
        }

        /// <summary>Whether hulls of <paramref name="cls"/> carry a field projector.</summary>
        public static bool Emits(ShipClass cls) => emitters.Contains(cls);

        public static readonly RangeSetting RechargeDays = new RangeSetting("war.interdictorRechargeDays", SettingGroups.Ai,
            "Interdictor recharge", 1, 0.1, 5, 0.1,
            "How long (days) a lane interdictor's field projector takes to charge again after it has pulsed and thrown ships out of a hyperlane.",
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + " days", "interdictor interdiction pulse lane hyperlane recharge pirate ambush");

        public static readonly RangeSetting PicketChance = new RangeSetting("war.lanePicketChance", SettingGroups.Ai,
            "Navy lane pickets", 0.35, 0, 1, 0.05,
            "The chance a navy group that carries a lane interdictor, with nothing more pressing to do, lies in wait on the most " +
            "troubled hyperlane out of its system, to pull the raiders and enemies riding it out into a fight.",
            v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", "interdictor lane picket navy patrol ambush hyperlane");

        /// <summary>
        /// The hull in <paramref name="leader"/>'s group carrying a working field projector: the leader itself, else an escort
        /// flying with it (one of <paramref name="ships"/>), or null when none is left (lost, or never fitted).
        /// </summary>
        public static Ship EmitterOf(Ship leader, IEnumerable<Ship> ships)
        {
            if (leader == null || leader.Destroyed) return null;
            if (Working(leader)) return leader;
            if (ships != null)
                foreach (var s in ships)
                    if (s != null && s.Leader == leader && Working(s)) return s;
            return null;
        }

        /// <summary>Whether <paramref name="leader"/>'s group carries a working field projector.</summary>
        public static bool Fielded(Ship leader, IEnumerable<Ship> ships) => EmitterOf(leader, ships) != null;

        static bool Working(Ship s) => !s.Destroyed && !s.Derelict && s.Spec != null && Emits(s.Spec.Class);
    }
}
