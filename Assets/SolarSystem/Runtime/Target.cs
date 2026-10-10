using System;
using SolarSystem.Core;

namespace SolarSystem
{
    /// <summary>Something the player can select and focus: a body or a ship.</summary>
    public readonly struct Target : IEquatable<Target>
    {
        public enum TargetKind
        {
            None,
            Body,
            Ship,
        }

        public readonly TargetKind Kind;
        public readonly int Body;
        public readonly Ship Ship;

        Target(TargetKind kind, int body, Ship ship)
        {
            Kind = kind;
            Body = body;
            Ship = ship;
        }

        public static readonly Target None = new Target(TargetKind.None, -1, null);
        public static Target OfBody(int index) => new Target(TargetKind.Body, index, null);
        public static Target OfShip(Ship ship) => ship == null ? None : new Target(TargetKind.Ship, -1, ship);

        public bool IsNone => Kind == TargetKind.None;
        public bool IsBody => Kind == TargetKind.Body;
        public bool IsShip => Kind == TargetKind.Ship;

        public bool Equals(Target other) => Kind == other.Kind && Body == other.Body && ReferenceEquals(Ship, other.Ship);
        public override bool Equals(object obj) => obj is Target t && Equals(t);
        public override int GetHashCode() => HashCode.Combine((int)Kind, Body, Ship?.Id ?? -1);
        public static bool operator ==(Target a, Target b) => a.Equals(b);
        public static bool operator !=(Target a, Target b) => !a.Equals(b);
    }
}
