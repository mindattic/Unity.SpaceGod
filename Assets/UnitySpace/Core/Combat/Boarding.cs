using System;
using System.Collections.Generic;

namespace Unity.SpaceGod.Core
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

        /// <summary>A marine habitat (U.SPC-T465): where the ship's marines live and drill, and muster from to repel boarders.</summary>
        Barracks,
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

        /// <summary>How far the marines aboard have drilled in their habitats (U.SPC-T465), 0 (raw) to 1 (fully trained):
        /// it raises their quality (<see cref="DrillFactor"/>) both repelling boarders and boarding from this hull.</summary>
        public double Training;

        /// <summary>What the marines' drill multiplies their quality by.</summary>
        public double DrillFactor => MarineHabitats.Factor(Training);

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

        /// <summary>The inside of a ship of the class as designed: its facilities' spaces (<see cref="FacilityLayouts.For"/>).</summary>
        public static Interior For(ShipClass cls, double crew, double marines) => For(FacilityLayouts.For(cls), crew, marines);

        /// <summary>A defence platform (or any station or satellite): a gun deck around a control room and its reactor.</summary>
        public static Interior ForPlatform(double crew, double marines) => For(FacilityLayouts.Platform, crew, marines);

        /// <summary>
        /// The inside of a hull built from its facilities (U.SPC-T470): each facility that is a space of its own
        /// (<see cref="FacilityKind.Compartment"/>) is a compartment, named and placed as its site says, joined on to the
        /// one before it along the keel or branching off the facility its site links to. A facility fitted later with no
        /// place of its own opens off midships (or the middle of the ship). Plate, racks and the like are no space.
        /// </summary>
        public static Interior For(IReadOnlyList<FacilitySite> facilities, double crew, double marines)
        {
            var i = new Interior();
            var made = new Compartment[facilities?.Count ?? 0];
            Compartment last = null;
            for (int f = 0; f < made.Length; f++)
            {
                var site = facilities[f];
                if (!(site.Def?.Compartment is CompartmentKind kind)) continue;
                var c = new Compartment { Index = i.Compartments.Count, Name = site.Name, Kind = kind, Z = site.Z, Y = site.Y };
                var parent = site.Link == FacilitySite.Keel ? last
                    : site.Link >= 0 && site.Link < made.Length ? made[site.Link]
                    : i.Of(CompartmentKind.Midships) ?? (i.Compartments.Count > 0 ? i.Compartments[i.Compartments.Count / 2] : null);
                if (site.Link == FacilitySite.Amidships && parent != null)
                {
                    c.Z = parent.Z;
                    c.Y = parent.Y - 0.05;
                }
                if (parent != null)
                {
                    parent.Links.Add(c.Index);
                    c.Links.Add(parent.Index);
                }
                i.Compartments.Add(c);
                made[f] = c;
                if (site.Link == FacilitySite.Keel) last = c;
            }
            if (i.Compartments.Count == 0)
                i.Compartments.Add(new Compartment { Index = 0, Name = "Hull", Kind = CompartmentKind.Midships });
            i.Crew0(crew, marines);
            return i;
        }

        /// <summary>The marine habitats' barracks aboard (U.SPC-T465).</summary>
        public IEnumerable<Compartment> Barracks
        {
            get
            {
                foreach (var c in Compartments) if (c.Kind == CompartmentKind.Barracks) yield return c;
            }
        }

        /// <summary>Whether the hull has a marine habitat at all; one without repels boarders with its crew alone.</summary>
        public bool HasBarracks => Of(CompartmentKind.Barracks) != null;

        /// <summary>Whether the ship's company works in a compartment: everywhere but the marines' barracks (unless the
        /// barracks are all there is).</summary>
        bool CrewSpace(Compartment c) => c.Kind != CompartmentKind.Barracks || !HasCrewSpace;

        bool HasCrewSpace
        {
            get
            {
                foreach (var c in Compartments) if (c.Kind != CompartmentKind.Barracks) return true;
                return false;
            }
        }

        int CrewSpaces
        {
            get
            {
                int n = 0;
                foreach (var c in Compartments) if (CrewSpace(c)) n++;
                return n;
            }
        }

        /// <summary>Spreads <paramref name="crew"/> evenly through the spaces the ship's company works in.</summary>
        void SpreadCrew(double crew, bool add)
        {
            int n = CrewSpaces;
            foreach (var c in Compartments)
            {
                if (!CrewSpace(c)) { if (!add) c.Crew = 0; continue; }
                c.Crew = (add ? c.Crew : 0) + crew / n;
            }
        }

        /// <summary>Quarters <paramref name="marines"/>: shared among the habitats' barracks, or, on a hull with none (a
        /// prize crew, a breacher's team in its assault bay), amidships or wherever there is room.</summary>
        void Quarter(double marines)
        {
            if (marines <= 0) return;
            int n = 0;
            foreach (var _ in Barracks) n++;
            if (n > 0)
            {
                foreach (var c in Barracks) c.Marines += marines / n;
                return;
            }
            (Of(CompartmentKind.Quarters) ?? Of(CompartmentKind.Midships) ?? Of(CompartmentKind.AssaultBay) ?? Compartments[Compartments.Count / 2]).Marines += marines;
        }

        /// <summary>Crew spread through the ship; marines in their habitats' barracks (or amidships, or wherever there is room).</summary>
        void Crew0(double crew, double marines)
        {
            SpreadCrew(crew, false);
            Quarter(marines);
        }

        /// <summary>
        /// A day's drill (U.SPC-T465): marines in their habitats' barracks train, the whole complement going from raw to
        /// fully trained in <see cref="MarineHabitats.DrillDays"/> days. Nothing is learned without a habitat, with
        /// nobody in it, or while the ship is being boarded.
        /// </summary>
        public void Drill(double days)
        {
            if (days <= 0 || UnderBoarding || Captured) return;
            double inBarracks = 0;
            foreach (var c in Barracks) inBarracks += c.Marines;
            if (inBarracks < 0.5) return;
            Training = Math.Min(1, Training + days / Math.Max(1e-9, MarineHabitats.DrillDays.Value));
        }

        // ---------------------------------------------------------------- the fight

        /// <summary>Marines come aboard at a compartment (a pod, a breacher's drill, or an airlock after docking).</summary>
        /// <summary>Returns false when nobody was landed (the ship is already taken, or another faction is boarding it),
        /// so the caller can take its marines back rather than lose them.</summary>
        public bool Board(Faction boarder, double quality, double marines, Compartment at, bool breach, double seconds, Random rng)
        {
            if (Captured) return false;
            if (Boarder != null && Boarder != boarder) return false;
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
            return true;
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
                double defence = (c.Marines * DefenderQuality * DrillFactor + c.Crew * CrewFight) * DefenderAdvantage * fortified * (c.Vented ? 0.7 : 1);
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
            SpreadCrew(prizeCrew, false);
            foreach (var c in Compartments)
            {
                c.Marines = c.Boarders;
                c.Boarders = 0;
                c.Taken = false;
            }
            Boarder = null;
            Captured = false;
            // The ship's own marines are gone; the boarders now aboard never drilled in its habitats.
            Training = 0;
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
            Training = 0;
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

        /// <summary>People come aboard: crew spread through the ship, marines to their barracks (the ship's own coming back
        /// from a boarding, so their drill is kept).</summary>
        public void Embark(double crew, double marines)
        {
            if (crew > 0) SpreadCrew(crew, true);
            Quarter(marines);
        }

        /// <summary>Back at a yard: crew and marines made up to strength. Raw recruits dilute the complement's drill.</summary>
        public void Muster(double crew, double marines)
        {
            double had = Marines;
            if (marines > had) Training *= had / marines;
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
