using System;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod
{
    /// <summary>Something the player can select and focus: a body, a ship, a structure in orbit or a docking ring; or, for the
    /// camera only, one unit in a battle that is no ship of the fleet (a launched fighter, a defence platform: U.SPC-T422).</summary>
    public readonly struct Target : IEquatable<Target>
    {
        public enum TargetKind
        {
            None,
            Body,
            Ship,
            Structure,
            Ring,
            Unit,
        }

        public readonly TargetKind Kind;
        public readonly int Body;
        public readonly Ship Ship;
        public readonly OrbitalStructure Structure;
        public readonly DockingRing Ring;

        /// <summary>For <see cref="TargetKind.Unit"/>: the battle and the unit in it.</summary>
        public readonly Battle Battle;
        public readonly BattleUnit Unit;

        Target(TargetKind kind, int body, Ship ship, OrbitalStructure structure = null, DockingRing ring = null, Battle battle = null, BattleUnit unit = null)
        {
            Battle = battle;
            Unit = unit;
            Kind = kind;
            Body = body;
            Ship = ship;
            Structure = structure;
            Ring = ring;
        }

        public static readonly Target None = new Target(TargetKind.None, -1, null);
        public static Target OfBody(int index) => new Target(TargetKind.Body, index, null);
        public static Target OfShip(Ship ship) => ship == null ? None : new Target(TargetKind.Ship, -1, ship);
        public static Target OfStructure(OrbitalStructure s) => s == null ? None : new Target(TargetKind.Structure, s.Body, null, s);
        public static Target OfRing(DockingRing r) => r == null ? None : new Target(TargetKind.Ring, r.Body, null, null, r);

        /// <summary>A unit in a battle, for the camera to ride with (a launched fighter the player flies, a platform whose gun they lay).</summary>
        public static Target OfUnit(Battle battle, BattleUnit unit) => battle == null || unit == null ? None : new Target(TargetKind.Unit, battle.Body, null, null, null, battle, unit);

        public bool IsNone => Kind == TargetKind.None;
        public bool IsBody => Kind == TargetKind.Body;
        public bool IsShip => Kind == TargetKind.Ship;
        public bool IsStructure => Kind == TargetKind.Structure;
        public bool IsRing => Kind == TargetKind.Ring;
        public bool IsUnit => Kind == TargetKind.Unit;

        public bool Equals(Target other) =>
            Kind == other.Kind && Body == other.Body && ReferenceEquals(Ship, other.Ship) && ReferenceEquals(Structure, other.Structure) && ReferenceEquals(Ring, other.Ring) && ReferenceEquals(Unit, other.Unit);

        public override bool Equals(object obj) => obj is Target t && Equals(t);
        public override int GetHashCode() => HashCode.Combine((int)Kind, Body, Ship?.Id ?? -1, Structure?.Name, Ring?.Id ?? -1, Unit?.Id ?? -1);
        public static bool operator ==(Target a, Target b) => a.Equals(b);
        public static bool operator !=(Target a, Target b) => !a.Equals(b);
    }
}
