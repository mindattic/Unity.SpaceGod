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

        /// <summary>The cockpit of a launched fighter or bomber (U.SPC-T422).</summary>
        public static FighterSeat FighterOf(Battle battle, BattleUnit unit) => new FighterSeat(battle, unit);

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

        /// <summary>
        /// Every gun mount on any battle unit (a ship's battery, a defence platform's, a fort's), hosted by the ship's
        /// helm when it is a ship and <paramref name="world"/> is given.
        /// </summary>
        public static List<MountedTurret> TurretsOn(World world, Battle battle, BattleUnit unit) =>
            TurretsOn(battle, unit, unit?.Ship != null && world != null ? HelmOf(world, unit.Ship) : null);

        /// <summary>
        /// Every seat in <paramref name="battle"/> the player could take right now, in the battle's own order (U.SPC-T422):
        /// the helm of each ship still fighting, and the cockpit of each fighter and bomber aloft. Gun mounts, many to a
        /// hull, are reached from their hull (<see cref="TurretsOn(World, Battle, BattleUnit)"/>), not listed here.
        /// </summary>
        public static List<IEmbodiable> SeatsIn(World world, Battle battle)
        {
            var list = new List<IEmbodiable>();
            if (battle == null || battle.Over) return list;
            foreach (var u in battle.Units)
            {
                var seat = SeatOf(world, battle, u);
                if (seat != null && seat.CanEmbody) list.Add(seat);
            }
            return list;
        }

        /// <summary>
        /// The seat <paramref name="step"/> places on from <paramref name="current"/> in <paramref name="seats"/>, wrapping
        /// round (the first, or the last going back, when <paramref name="current"/> is not among them); null if there are none.
        /// </summary>
        public static IEmbodiable Cycle(IList<IEmbodiable> seats, IEmbodiable current, int step)
        {
            if (seats == null || seats.Count == 0) return null;
            int at = current == null ? -1 : seats.IndexOf(current);
            if (at < 0) return step >= 0 ? seats[0] : seats[seats.Count - 1];
            int n = seats.Count;
            return seats[((at + step) % n + n) % n];
        }

        /// <summary>The seat that flies <paramref name="unit"/> itself (a ship's helm, a fighter's cockpit), or null for a
        /// unit nobody flies (a defence platform, a fort: their guns are reached through <see cref="TurretsOn(World, Battle, BattleUnit)"/>).</summary>
        public static IEmbodiable SeatOf(World world, Battle battle, BattleUnit unit)
        {
            if (unit == null) return null;
            if (unit.Kind == UnitKind.Fighter) return FighterOf(battle, unit);
            if (unit.Ship != null && world != null) return HelmOf(world, unit.Ship);
            return null;
        }
    }
}
