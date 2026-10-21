using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A navy's standing patrol wings (U.SPC-T371): besides its battle groups, every navy that holds ports in a system keeps
    /// a few small wings flying a beat of them, so the space between its worlds is never empty. A wing is a light wing
    /// (three fighters), a heavy-fighter wing (a Breacher leading two light fighters) or a picket (a destroyer leading a
    /// heavy and two light fighters), raised in that order, so a navy with a port or two flies only light fighters and the
    /// warship pickets are the big navies'; each on a beat of up to <see cref="BeatLength"/> of its navy's ports, the wings spread so
    /// that between them they cover every port.
    /// <para>A wing is commanded, not loosed on whatever it meets (see <see cref="War.Commanded"/>): it defends the ports on
    /// its beat when an enemy turns up there, hunts raiders seen working them, and puts in at a yard to repair, but it never
    /// strikes out at an enemy port, is never merged into a battle group, sent through a gate on a campaign, or counted
    /// against the yards' warship cap (<see cref="Ship.PatrolWing"/>).</para>
    /// </summary>
    public static class PatrolWings
    {
        /// <summary>The most wings one navy flies in one system.</summary>
        public const int MaxWingsPerFaction = 4;

        /// <summary>The most ports on one wing's beat.</summary>
        public const int BeatLength = 3;

        /// <summary>The three kinds of wing, in the order a navy raises them: light wing, heavy-fighter wing, picket.</summary>
        static readonly ShipClass[][] Compositions =
        {
            new[] { ShipClass.Fighter, ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Breacher, ShipClass.Fighter, ShipClass.Fighter },
            new[] { ShipClass.Destroyer, ShipClass.Breacher, ShipClass.Fighter, ShipClass.Fighter },
        };

        static readonly string[] CallSigns =
        {
            "Lamplighter", "Nighthawk", "Vigilant", "Redstart", "Picket", "Outrider", "Lookout", "Harfang", "Tripwire", "Nightwatch",
            "Gyrfalcon", "Warder", "Ranger", "Skirmisher", "Lanner", "Kingfisher", "Osprey", "Shrike", "Peregrine", "Hobby",
            "Goshawk", "Kite", "Sparrowhawk", "Falconet",
        };

        static readonly string[] Wingmen = { "Two", "Three", "Four", "Five" };

        /// <summary>How many wings a navy holding <paramref name="ports"/> ports here flies: one, plus one for every two
        /// ports, up to <see cref="MaxWingsPerFaction"/>; none with fewer than two, since a beat runs between ports and a
        /// navy's one port is its battle group's to hold.</summary>
        public static int WingsFor(int ports) => ports < 2 ? 0 : Math.Min(MaxWingsPerFaction, 1 + ports / 2);

        /// <summary>The classes of the <paramref name="index"/>th wing a navy raises, its leader first.</summary>
        public static ShipClass[] Composition(int index) => Compositions[index % Compositions.Length];

        /// <summary>The bodies of <paramref name="f"/>'s standing ports in <paramref name="w"/>, the most populous first.</summary>
        public static List<int> Ports(World w, Faction f)
        {
            var list = new List<Settlement>();
            foreach (var s in w.Economy.Settlements)
                if (s.Owner == f && !s.Fallen && s.Kind != SettlementKind.Haven) list.Add(s);
            list.Sort((a, b) => b.Population.CompareTo(a.Population));
            var bodies = new List<int>();
            foreach (var s in list)
                if (!bodies.Contains(s.Body)) bodies.Add(s.Body);
            return bodies;
        }

        /// <summary>The beat of the <paramref name="index"/>th of <paramref name="wings"/> wings over <paramref name="ports"/>:
        /// up to <see cref="BeatLength"/> ports in a row, each wing's beat starting further round, so the wings between them
        /// cover them all.</summary>
        public static int[] Beat(List<int> ports, int index, int wings)
        {
            int n = ports.Count;
            int length = Math.Min(BeatLength, n);
            int start = (int)((long)index * n / Math.Max(1, wings)) % n;
            var beat = new int[length];
            for (int j = 0; j < length; j++) beat[j] = ports[(start + j) % n];
            return beat;
        }

        /// <summary>
        /// Raises the standing patrol wings of every navy (not a pirate clan, nor a power without a navy) that holds ports in
        /// <paramref name="w"/>, each stationed at the first port of its beat. Returns the wings' leaders.
        /// </summary>
        public static List<Ship> Raise(World w, double jd)
        {
            var leaders = new List<Ship>();
            foreach (var f in new List<Faction>(w.Fleet.Factions))
            {
                if (!f.IsMilitary || f.IsPirate) continue;
                var ports = Ports(w, f);
                int wings = WingsFor(ports.Count);
                // Call signs run on across every system the navy holds, so no two of its wings share one.
                int sign = 0;
                foreach (var s in w.Polities.Navy(f))
                    if (s.PatrolWing && !s.IsEscort) sign++;
                for (int i = 0; i < wings; i++)
                {
                    var classes = Composition(i);
                    string call = CallSigns[sign % CallSigns.Length] + (sign >= CallSigns.Length ? " " + (sign / CallSigns.Length + 1) : "");
                    sign++;
                    var leader = w.Fleet.AddShip(call, classes[0], f, ShipRole.Patrol);
                    leader.PatrolWing = true;
                    leader.Route = Beat(ports, i, wings);
                    leader.RouteIndex = 0;
                    for (int k = 1; k < classes.Length; k++)
                    {
                        var e = w.Fleet.AddShip(call + " " + Wingmen[(k - 1) % Wingmen.Length], classes[k], f, ShipRole.Escort);
                        e.PatrolWing = true;
                        w.Fleet.AddEscort(leader, e, Fleet.SlotFor(k - 1));
                    }
                    w.Fleet.Station(leader, leader.Route[0], jd);
                    leaders.Add(leader);
                }
            }
            return leaders;
        }

        /// <summary>The next port on <paramref name="leader"/>'s beat after <paramref name="here"/> (its first if it is off
        /// its beat), or <paramref name="here"/> if it has none.</summary>
        public static int NextOnBeat(Ship leader, int here)
        {
            var beat = leader.Route;
            if (beat == null || beat.Length == 0) return here;
            int i = Array.IndexOf(beat, here);
            return i < 0 ? beat[0] : beat[(i + 1) % beat.Length];
        }

        /// <summary>Whether <paramref name="body"/> is on <paramref name="leader"/>'s beat (its patrol route).</summary>
        public static bool OnBeat(Ship leader, int body) => leader.Route != null && Array.IndexOf(leader.Route, body) >= 0;
    }
}
