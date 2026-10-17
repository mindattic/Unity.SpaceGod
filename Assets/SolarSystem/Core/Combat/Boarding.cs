using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum CompartmentKind
    {
        Bow,
        Hangar,
        Hold,
        Quarters,
        Battery,
        Midships,
        Bridge,
        Engineering,
        AssaultBay,
    }

    /// <summary>
    /// One space inside a hull. Who is in it (crew, defending marines, boarders), whether it is open
    /// to vacuum, and its place in the ship: ship lengths along the keel and its neighbours. This is
    /// everything a shipboard battle view will need to draw the fight deck by deck.
    /// </summary>
    public sealed class Compartment
    {
        public int Index;
        public string Name;
        public CompartmentKind Kind;

        /// <summary>Position along the keel (ship lengths, nose +0.5) and height, for mapping hits and pods to a space.</summary>
        public double Z, Y;

        public readonly List<int> Links = new List<int>();

        public double Crew;
        public double Marines;
        public double Boarders;

        /// <summary>People sheltering here who take no part in a fight: the crew left behind on a derelict,
        /// sealed in and waiting for a rescue (see <see cref="Interior.Abandon"/>).</summary>
        public double Survivors;

        /// <summary>True once boarders hold it (no defenders left in it).</summary>
        public bool Taken;

        /// <summary>Open to space: a pod or a breacher's drill holed it. Crew die until the bulkheads seal.</summary>
        public bool Vented;
        public double SealIn;

        public bool Contested => Boarders > 0.01 && (Crew + Marines) > 0.01;
    }

    /// <summary>A line in a ship's boarding log: what happened, where, and when (battle seconds).</summary>
    public readonly struct BoardingNote
    {
        public readonly double Seconds;
        public readonly int Compartment;
        public readonly string Text;

        public BoardingNote(double seconds, int compartment, string text)
        {
            Seconds = seconds;
            Compartment = compartment;
            Text = text;
        }
    }

    /// <summary>
    /// The inside of a hull and any fight going on in it. Defending marines sit in their barracks
    /// and react to a breach after a delay; the fight in each compartment trades casualties by
    /// numbers, training (<see cref="Faction.MarineQuality"/>) and a defender's advantage; boarders
    /// who clear a space push on toward the bridge and engineering by the shortest way. Holding both
    /// takes the ship; losing every boarder repels them. A breach vents its compartment: crew there
    /// die until the emergency bulkheads close.
    /// </summary>
    public sealed class Interior
    {
        public readonly List<Compartment> Compartments = new List<Compartment>();
        public readonly List<BoardingNote> Log = new List<BoardingNote>();

        /// <summary>Who is boarding (null when nobody is aboard who should not be).</summary>
        public Faction Boarder;
        public double BoarderQuality = 1;
        public double DefenderQuality = 1;

        /// <summary>Set when the boarders hold the bridge and engineering.</summary>
        public bool Captured;

        /// <summary>Marines that boarded in total, and how many of them fell.</summary>
        public double BoardersLanded, BoardersLost, CrewLost, MarinesLost;

        double reactIn;

        /// <summary>Casualties per second per unit of effective strength.</summary>
        const double Lethality = 0.012;

        /// <summary>Ship's crew fight, badly; marines are the defence.</summary>
        const double CrewFight = 0.15;

        const double DefenderAdvantage = 1.4;

        public double Crew
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Crew;
                return n;
            }
        }

        public double Marines
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Marines;
                return n;
            }
        }

        public double Boarders
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Boarders;
                return n;
            }
        }

        public bool UnderBoarding => Boarder != null && Boarders > 0.5 && !Captured;

        /// <summary>Non-combatants sheltering aboard (a derelict's left-behind crew).</summary>
        public double Survivors
        {
            get
            {
                double n = 0;
                foreach (var c in Compartments) n += c.Survivors;
                return n;
            }
        }

        public int TakenCount
        {
            get
            {
                int n = 0;
                foreach (var c in Compartments) if (c.Taken) n++;
                return n;
            }
        }

        public Compartment Of(CompartmentKind kind)
        {
            foreach (var c in Compartments)
                if (c.Kind == kind) return c;
            return null;
        }

        /// <summary>The compartment nearest a point on the hull (ship lengths along the keel, height).</summary>
        public Compartment Nearest(double z, double y)
        {
            Compartment best = null;
            double bestD = double.MaxValue;
            foreach (var c in Compartments)
            {
                double d = Math.Abs(c.Z - z) + Math.Abs(c.Y - y) * 0.5;
                if (d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- building

        public static Interior For(ShipClass cls, double crew, double marines)
        {
            var i = new Interior();
            switch (cls)
            {
                case ShipClass.Carrier:
                    i.Chain(("Bow", CompartmentKind.Bow, 0.4, 0), ("Hangar deck", CompartmentKind.Hangar, 0.15, 0), ("Crew quarters", CompartmentKind.Quarters, -0.02, 0),
                        ("Midships", CompartmentKind.Midships, -0.18, 0), ("Engineering", CompartmentKind.Engineering, -0.4, 0));
                    i.Branch("Island bridge", CompartmentKind.Bridge, -0.11, 0.14, CompartmentKind.Midships);
                    break;
                case ShipClass.Battleship:
                    i.Chain(("Bow", CompartmentKind.Bow, 0.38, 0), ("Forward battery", CompartmentKind.Battery, 0.16, 0), ("Crew quarters", CompartmentKind.Quarters, 0.0, 0),
                        ("Midships", CompartmentKind.Midships, -0.16, 0), ("Aft battery", CompartmentKind.Battery, -0.28, 0), ("Engineering", CompartmentKind.Engineering, -0.42, 0));
                    i.Branch("Bridge", CompartmentKind.Bridge, -0.11, 0.1, CompartmentKind.Midships);
                    break;
                case ShipClass.Cruiser:
                    i.Chain(("Bow", CompartmentKind.Bow, 0.3, 0), ("Crew quarters", CompartmentKind.Quarters, 0.05, 0), ("Midships", CompartmentKind.Midships, -0.12, 0),
                        ("Engineering", CompartmentKind.Engineering, -0.38, 0));
                    i.Branch("Bridge", CompartmentKind.Bridge, -0.17, 0.06, CompartmentKind.Midships);
                    break;
                case ShipClass.Destroyer:
                    i.Chain(("Bow", CompartmentKind.Bow, 0.25, 0), ("Midships", CompartmentKind.Midships, -0.05, 0), ("Engineering", CompartmentKind.Engineering, -0.36, 0));
                    i.Branch("Bridge", CompartmentKind.Bridge, -0.08, 0.05, CompartmentKind.Midships);
                    break;
                case ShipClass.Freighter:
                    i.Chain(("Cockpit", CompartmentKind.Bridge, 0.43, 0), ("Forward hold", CompartmentKind.Hold, 0.15, 0), ("Aft hold", CompartmentKind.Hold, -0.15, 0),
                        ("Engineering", CompartmentKind.Engineering, -0.42, 0));
                    break;
                case ShipClass.Miner:
                    i.Chain(("Cockpit", CompartmentKind.Bridge, 0.3, 0.05), ("Ore hold", CompartmentKind.Hold, 0, 0), ("Engineering", CompartmentKind.Engineering, -0.35, 0));
                    break;
                case ShipClass.Breacher:
                    i.Chain(("Assault bay", CompartmentKind.AssaultBay, 0.25, 0), ("Bridge", CompartmentKind.Bridge, 0, 0.03), ("Engineering", CompartmentKind.Engineering, -0.3, 0));
                    break;
                default:
                    i.Chain(("Cockpit", CompartmentKind.Bridge, 0.1, 0), ("Engineering", CompartmentKind.Engineering, -0.3, 0));
                    break;
            }
            i.Crew0(crew, marines);
            return i;
        }

        /// <summary>A defence platform: a gun deck around a control room and its reactor.</summary>
        public static Interior ForPlatform(double crew, double marines)
        {
            var i = new Interior();
            i.Chain(("Gun deck", CompartmentKind.Battery, 0.15, 0.15), ("Control", CompartmentKind.Bridge, 0, 0), ("Reactor", CompartmentKind.Engineering, -0.15, -0.15));
            i.Crew0(crew, marines);
            return i;
        }

        void Chain(params (string name, CompartmentKind kind, double z, double y)[] spaces)
        {
            foreach (var (name, kind, z, y) in spaces)
            {
                var c = new Compartment { Index = Compartments.Count, Name = name, Kind = kind, Z = z, Y = y };
                if (Compartments.Count > 0)
                {
                    var prev = Compartments[Compartments.Count - 1];
                    prev.Links.Add(c.Index);
                    c.Links.Add(prev.Index);
                }
                Compartments.Add(c);
            }
        }

        void Branch(string name, CompartmentKind kind, double z, double y, CompartmentKind from)
        {
            var parent = Of(from);
            var c = new Compartment { Index = Compartments.Count, Name = name, Kind = kind, Z = z, Y = y };
            c.Links.Add(parent.Index);
            parent.Links.Add(c.Index);
            Compartments.Add(c);
        }

        /// <summary>Crew spread through the ship; marines quartered amidships (or wherever there is room).</summary>
        void Crew0(double crew, double marines)
        {
            foreach (var c in Compartments) c.Crew = crew / Compartments.Count;
            var barracks = Of(CompartmentKind.Quarters) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.AssaultBay) ?? Compartments[Compartments.Count / 2];
            barracks.Marines = marines;
        }

        // ---------------------------------------------------------------- the fight

        /// <summary>Marines come aboard at a compartment (a pod, a breacher's drill, or an airlock after docking).</summary>
        public void Board(Faction boarder, double quality, double marines, Compartment at, bool breach, double seconds, Random rng)
        {
            if (Captured) return;
            if (Boarder != null && Boarder != boarder) return;
            // Defenders are roused once per boarding: more boarders landing later don't stand them down again.
            bool fresh = Boarder == null;
            Boarder = boarder;
            BoarderQuality = quality;
            at.Boarders += marines;
            BoardersLanded += marines;
            if (breach && !at.Vented)
            {
                at.Vented = true;
                // Emergency bulkheads: quick with the bridge giving the order, slower without.
                at.SealIn = 4 + rng.NextDouble() * 10;
            }
            double react = 15 + rng.NextDouble() * 30;
            if (fresh) reactIn = react;
            Note(seconds, at.Index, $"{marines:0} {boarder.Name} marines board at {at.Name}" + (breach ? ", hull breached" : ""));
        }

        /// <summary>Advances the fight aboard by <paramref name="dt"/> seconds. Returns true the moment the ship falls.</summary>
        public bool Step(double dt, double seconds, Random rng)
        {
            if (Captured || Boarder == null) return false;

            // Vacuum: crew caught in a holed compartment die until it is sealed.
            foreach (var c in Compartments)
            {
                if (!c.Vented || c.SealIn <= 0) continue;
                double killed = c.Crew * Math.Min(1, 0.06 * dt);
                c.Crew -= killed;
                CrewLost += killed;
                c.SealIn -= dt;
                if (c.SealIn <= 0) Note(seconds, c.Index, $"Emergency bulkheads seal {c.Name}");
            }

            // Defending marines move toward the fighting once they have been roused.
            reactIn -= dt;
            if (reactIn <= 0 && Boarders > 0.01) Reinforce(dt);

            foreach (var c in Compartments)
            {
                if (c.Boarders <= 0.01)
                {
                    // Defenders who have fought their way back in hold it again.
                    if (c.Taken && c.Crew + c.Marines > 0.01)
                    {
                        c.Taken = false;
                        Note(seconds, c.Index, $"The crew retake {c.Name}");
                    }
                    continue;
                }
                double defenders = c.Crew + c.Marines;
                if (defenders <= 0.01)
                {
                    if (!c.Taken)
                    {
                        c.Taken = true;
                        c.Crew = c.Marines = 0;
                        Note(seconds, c.Index, $"Boarders take {c.Name}");
                    }
                    continue;
                }
                double attack = c.Boarders * BoarderQuality;
                // The bridge and engineering are fortified: blast doors, prepared positions, a last stand.
                double fortified = c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering ? 1.6 : 1;
                double defence = (c.Marines * DefenderQuality + c.Crew * CrewFight) * DefenderAdvantage * fortified * (c.Vented ? 0.7 : 1);
                double defenderLosses = Math.Min(defenders, attack * Lethality * dt);
                double boarderLosses = Math.Min(c.Boarders, defence * Lethality * dt);
                // Marines stand in front; crew take what the marines cannot.
                double fromMarines = Math.Min(c.Marines, Math.Min(defenderLosses, defenderLosses * (c.Marines / defenders + 0.3)));
                double fromCrew = Math.Min(c.Crew, defenderLosses - fromMarines);
                c.Marines -= fromMarines;
                c.Crew -= fromCrew;
                MarinesLost += fromMarines;
                CrewLost += fromCrew;
                c.Boarders -= boarderLosses;
                BoardersLost += boarderLosses;
            }

            // Boarders who hold a space push on toward the bridge and engineering.
            Advance(dt);

            var bridge = Of(CompartmentKind.Bridge);
            var engineering = Of(CompartmentKind.Engineering);
            if (bridge.Taken && !bridge.Contested && engineering.Taken && !engineering.Contested)
            {
                Captured = true;
                Note(seconds, bridge.Index, $"The {Boarder.Name} hold the bridge and engineering: the ship is taken ({Crew:0} crew surrender)");
                return true;
            }
            if (Boarders <= 0.5)
            {
                Note(seconds, -1, $"The {Boarder.Name} boarders are wiped out; the crew hold the ship");
                foreach (var c in Compartments)
                {
                    c.Boarders = 0;
                    if (c.Taken && c.Crew + c.Marines <= 0.01) c.Taken = false;
                }
                Boarder = null;
                SealBreaches();
            }
            return false;
        }

        void Reinforce(double dt)
        {
            Compartment target = null;
            foreach (var c in Compartments)
                if (c.Boarders > 0.01 && (target == null || c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering)) target = c;
            if (target == null) return;
            foreach (var c in Compartments)
            {
                if (c == target || c.Marines <= 0.01 || c.Boarders > 0.01) continue;
                // A tenth of a compartment's marines move up each second.
                double move = c.Marines * Math.Min(1, 0.1 * dt);
                c.Marines -= move;
                target.Marines += move;
            }
        }

        void Advance(double dt)
        {
            // With nobody left fighting at the front, the rear guards have nothing to cover: they
            // gather and press on instead of holding what was taken for ever.
            bool front = false;
            foreach (var c in Compartments)
                if (c.Boarders > 0.01 && !c.Taken) front = true;
            // Every party's move is worked out from where they all stood at the start of the step, then made: a party
            // arriving in a held compartment does not move on again in the same step.
            var moves = new List<(Compartment from, Compartment to, double marines)>();
            foreach (var c in Compartments)
            {
                if (!c.Taken || c.Boarders <= (front ? 1 : 0.01)) continue;
                int next = NextToward(c.Index);
                if (next < 0) continue;
                // Most of the party moves on; a rear guard holds what was taken.
                double move = c.Boarders * Math.Min(1, 0.08 * dt);
                if (front && c.Boarders - move < 2) move = Math.Max(0, c.Boarders - 2);
                moves.Add((c, Compartments[next], move));
            }
            foreach (var (from, to, marines) in moves)
            {
                from.Boarders -= marines;
                to.Boarders += marines;
            }
        }

        /// <summary>The neighbour on the shortest way from a compartment to the nearest objective not yet held.</summary>
        int NextToward(int from)
        {
            var prev = new int[Compartments.Count];
            for (int k = 0; k < prev.Length; k++) prev[k] = -2;
            var queue = new Queue<int>();
            queue.Enqueue(from);
            prev[from] = -1;
            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                var c = Compartments[here];
                if (here != from && !c.Taken && (c.Kind == CompartmentKind.Bridge || c.Kind == CompartmentKind.Engineering))
                {
                    int step = here;
                    while (prev[step] != from) step = prev[step];
                    return step;
                }
                foreach (int n in c.Links)
                {
                    if (prev[n] != -2) continue;
                    prev[n] = here;
                    queue.Enqueue(n);
                }
            }
            return -1;
        }

        void Note(double seconds, int compartment, string text)
        {
            Log.Add(new BoardingNote(seconds, compartment, text));
            if (Log.Count > 60) Log.RemoveAt(0);
        }

        /// <summary>After a fight, survivors regroup: boarders aboard a taken ship become its prize crew.</summary>
        public void Settle()
        {
            foreach (var c in Compartments)
            {
                c.Vented = false;
                c.SealIn = 0;
            }
        }

        /// <summary>
        /// The ship got away (released, fled, or the battle ran out) with the fight aboard undecided: the
        /// boarders are gone with the battle, and what they held is the crew's again. Without this the
        /// boarding would stay "under way" for good, keeping the ship's marines tied up.
        /// </summary>
        public void BoardersGone()
        {
            if (Captured || Boarder == null) return;
            foreach (var c in Compartments)
            {
                c.Boarders = 0;
                c.Taken = false;
            }
            Boarder = null;
            SealBreaches();
        }

        /// <summary>The bulkheads round every breach still venting close (they take seconds, and nothing aboard stops
        /// them once the boarders are gone): its timer would otherwise stand frozen, as <see cref="Step"/> runs only
        /// while boarders are aboard, and go on killing crew at the start of the next boarding.</summary>
        void SealBreaches()
        {
            foreach (var c in Compartments)
                if (c.Vented && c.SealIn > 0) c.SealIn = 0;
        }

        /// <summary>A prize crew takes over: the boarders become the ship's people; the old crew are prisoners.</summary>
        public void HandOver(double prizeCrew)
        {
            foreach (var c in Compartments)
            {
                c.Crew = prizeCrew / Compartments.Count;
                c.Marines = c.Boarders;
                c.Boarders = 0;
                c.Taken = false;
            }
            Boarder = null;
            Captured = false;
        }

        /// <summary>
        /// The hull is left adrift: its own people are gone, apart from <paramref name="survivors"/> who
        /// sealed themselves in at the far end from the airlocks, and <paramref name="lurkers"/> raiders
        /// still aboard stripping it, who hold the holds (or wherever there is loot) as defenders of
        /// <paramref name="lurkerQuality"/>. Any boarding that took it is over.
        /// </summary>
        public void Abandon(double survivors, double lurkers, double lurkerQuality)
        {
            foreach (var c in Compartments)
            {
                c.Crew = c.Marines = c.Boarders = c.Survivors = 0;
                c.Taken = false;
                c.Vented = false;
                c.SealIn = 0;
            }
            Boarder = null;
            Captured = false;
            DefenderQuality = lurkerQuality;
            if (survivors > 0) (Of(CompartmentKind.Engineering) ?? Compartments[Compartments.Count - 1]).Survivors = survivors;
            if (lurkers > 0) (Of(CompartmentKind.Hold) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.Bridge) ?? Compartments[0]).Marines = lurkers;
        }

        /// <summary>
        /// Boarders still aboard go back the way they came: they are taken off and counted, and whatever
        /// they held is nobody's again (a derelict cleared, or a boarding party called back to its ship).
        /// </summary>
        public double Recall()
        {
            double n = 0;
            foreach (var c in Compartments)
            {
                n += c.Boarders;
                c.Boarders = 0;
                c.Taken = false;
            }
            Boarder = null;
            Captured = false;
            return n;
        }

        /// <summary>Takes up to <paramref name="n"/> crew off the ship, evenly from every compartment; returns how many went.</summary>
        public double TakeCrew(double n)
        {
            double have = Crew;
            if (have <= 0 || n <= 0) return 0;
            double share = Math.Min(1, n / have);
            foreach (var c in Compartments) c.Crew -= c.Crew * share;
            return have * share;
        }

        /// <summary>Takes up to <paramref name="n"/> marines off the ship (a boarding party leaving through the airlock).</summary>
        public double TakeMarines(double n)
        {
            double have = Marines;
            if (have <= 0 || n <= 0) return 0;
            double share = Math.Min(1, n / have);
            foreach (var c in Compartments) c.Marines -= c.Marines * share;
            return have * share;
        }

        /// <summary>People come aboard: crew spread through the ship, marines to their barracks.</summary>
        public void Embark(double crew, double marines)
        {
            if (crew > 0) foreach (var c in Compartments) c.Crew += crew / Compartments.Count;
            if (marines > 0) (Of(CompartmentKind.Quarters) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.AssaultBay) ?? Compartments[Compartments.Count / 2]).Marines += marines;
        }

        /// <summary>Back at a yard: crew and marines made up to strength.</summary>
        public void Muster(double crew, double marines)
        {
            foreach (var c in Compartments) c.Marines = 0;
            Crew0(crew, marines);
            foreach (var c in Compartments)
            {
                c.Boarders = 0;
                c.Taken = false;
                c.Vented = false;
            }
            Boarder = null;
            Captured = false;
        }
    }
}
