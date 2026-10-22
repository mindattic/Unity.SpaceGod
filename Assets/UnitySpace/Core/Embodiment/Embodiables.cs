using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Where the things the player can embody are found (U.SPC-T18): a picker lists from here rather than
    /// building its own lists, so a new kind registers once and every picker sees it.
    /// </summary>
    public static class Embodiables
    {
        /// <summary>A ship's helm.</summary>
        public static ShipHelm HelmOf(World world, Ship ship) => new ShipHelm(world, ship);

        /// <summary>
        /// Every gun mount on <paramref name="unit"/> the player could take right now (intact, a direct-fire
        /// gun), each with <paramref name="host"/> as what it is mounted on.
        /// </summary>
        public static List<MountedTurret> TurretsOn(Battle battle, BattleUnit unit, IEmbodiable host = null)
        {
            var list = new List<MountedTurret>();
            if (battle == null || unit == null) return list;
            foreach (var s in unit.Combat.Subsystems)
            {
                var t = new MountedTurret(battle, unit, s, host);
                if (t.CanEmbody) list.Add(t);
            }
            return list;
        }

        /// <summary>Every gun mount on a ship in a fight, its helm as their host.</summary>
        public static List<MountedTurret> TurretsOn(World world, Ship ship) =>
            ship?.Battle == null ? new List<MountedTurret>() : TurretsOn(ship.Battle, ship.Battle.UnitOf(ship), HelmOf(world, ship));
    }
}
