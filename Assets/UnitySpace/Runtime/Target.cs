using System;
using Unity.SpaceGod.Core;

namespace Unity.SpaceGod
{
    /// <summary>Something the player can select and focus: a body, a ship, a structure in orbit or a docking ring.</summary>
    public readonly struct Target : IEquatable<Target>
    {
        public enum TargetKind
        {
            None,
            Body,
            Ship,
            Structure,
            Ring,
        }

        public readonly TargetKind Kind;
        public readonly int Body;
        public readonly Ship Ship;
        public readonly OrbitalStructure Structure;
        public readonly DockingRing Ring;

        Target(TargetKind kind, int body, Ship ship, OrbitalStructure structure = null, DockingRing ring = null)
        {
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

        public bool IsNone => Kind == TargetKind.None;
        public bool IsBody => Kind == TargetKind.Body;
        public bool IsShip => Kind == TargetKind.Ship;
        public bool IsStructure => Kind == TargetKind.Structure;
        public bool IsRing => Kind == TargetKind.Ring;

        public bool Equals(Target other) =>
            Kind == other.Kind && Body == other.Body && ReferenceEquals(Ship, other.Ship) && ReferenceEquals(Structure, other.Structure) && ReferenceEquals(Ring, other.Ring);

        public override bool Equals(object obj) => obj is Target t && Equals(t);
        public override int GetHashCode() => HashCode.Combine((int)Kind, Body, Ship?.Id ?? -1, Structure?.Name, Ring?.Id ?? -1);
        public static bool operator ==(Target a, Target b) => a.Equals(b);
        public static bool operator !=(Target a, Target b) => !a.Equals(b);
    }
}
