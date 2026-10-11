using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum Relation
    {
        Peace,
        Ceasefire,
        War,
    }

    /// <summary>Who is at war with whom. Pirates are hostile to everyone and everyone to them.</summary>
    public sealed class Diplomacy
    {
        readonly Dictionary<(Faction, Faction), Relation> relations = new Dictionary<(Faction, Faction), Relation>();

        static (Faction, Faction) Key(Faction a, Faction b) =>
            string.CompareOrdinal(a.Name, b.Name) <= 0 ? (a, b) : (b, a);

        public Relation Get(Faction a, Faction b)
        {
            if (a == b) return Relation.Peace;
            return relations.TryGetValue(Key(a, b), out var r) ? r : Relation.Peace;
        }

        public void Set(Faction a, Faction b, Relation r) => relations[Key(a, b)] = r;

        public bool Hostile(Faction a, Faction b)
        {
            if (a == null || b == null || a == b) return false;
            if (a.IsPirate || b.IsPirate) return true;
            return Get(a, b) == Relation.War;
        }

        /// <summary>A ship of <paramref name="ship"/> may trade at a port owned by <paramref name="port"/>.</summary>
        public bool CanDock(Faction ship, Faction port)
        {
            if (ship == port) return true;
            if (port.IsPirate) return ship.IsPirate;
            if (ship.IsPirate) return false;
            return Get(ship, port) != Relation.War;
        }
    }
}
