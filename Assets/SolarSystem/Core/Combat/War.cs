using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    public enum OrderKind
    {
        Patrol,

        /// <summary>Lurk beside a hyperlane and pull passing ships out of it (pirates).</summary>
        Ambush,
        Attack,
        Defend,
        Siege,
        Repair,
        Raid,
        Hunt,

        /// <summary>Storm a pirate haven: break its garrison, then board and take it or destroy it.</summary>
        Assault,
    }

    /// <summary>What a fleet group (a flagship and its escorts) is doing, and where.</summary>
    public sealed class GroupOrder
    {
        public OrderKind Kind;
        public int Body = -1;
        public double SinceJd;
    }

    /// <summary>
    /// The wars. Fleet groups take orders (patrol home, attack a weak enemy port, defend one under
    /// attack, besiege, refit at a yard), pirates raid busy ports with thin defences, and a battle
    /// starts whenever hostile forces meet at a body: groups, merchants or a settlement's defence
    /// platforms. Outcomes feed back: losses are permanent, yards rebuild from the markets (alloys,
    /// electronics, munitions), a port held without defenders for three days changes hands, and
    /// traders steer clear of where the fighting has been.
    /// </summary>
    public sealed class War
    {
        readonly World world;
        public readonly List<Battle> Battles = new List<Battle>();
        public readonly List<Battle> Ended = new List<Battle>();

        /// <summary>Lasting wrecks left where ships have died, cleared away once each has run its lifetime.</summary>
        public readonly Wrecks Wrecks = new Wrecks();
        public readonly Dictionary<Ship, GroupOrder> Orders = new Dictionary<Ship, GroupOrder>();
        readonly Dictionary<int, double> danger = new Dictionary<int, double>();
        readonly Dictionary<Settlement, double> siegeDays = new Dictionary<Settlement, double>();
        readonly Dictionary<Faction, int> losses = new Dictionary<Faction, int>();
        readonly Dictionary<int, int> battleCount = new Dictionary<int, int>();
        readonly Dictionary<Ship, double> retreating = new Dictionary<Ship, double>();
        readonly Dictionary<Ship, double> laneSeen = new Dictionary<Ship, double>();
        readonly Dictionary<Ship, double> pulseReady = new Dictionary<Ship, double>();
        readonly Dictionary<BattleUnit, Faction> captures = new Dictionary<BattleUnit, Faction>();
        readonly Dictionary<Settlement, double> assaultDays = new Dictionary<Settlement, double>();

        /// <summary>An interdiction pulse pulls out every ship within this distance along the lane.</summary>
        public const double PulseRadiusKm = 600000;
        readonly Dictionary<int, double> lastBattleEndJd = new Dictionary<int, double>();
        readonly Random rng;

        double lastJd, nextDailyJd;
        double warStartJd, ceasefireUntilJd = -1;
        int nextBattleId, builtWarships;

        /// <summary>Raised when a battle begins (the app may want to slow time or point the camera).</summary>
        public event Action<Battle> BattleStarted;

        public War(World world, double jd, int seed)
        {
            this.world = world;
            rng = new Random(seed);
            lastJd = jd;
            nextDailyJd = jd + 1;
            warStartJd = jd;
        }

        Fleet Fleet => world.Fleet;
        Economy Economy => world.Economy;
        Diplomacy Diplomacy => world.Diplomacy;
        BodyCatalog Catalog => world.Catalog;

        public double Danger(int body) => danger.TryGetValue(body, out var d) ? d : 0;

        public int Losses(Faction f) => losses.TryGetValue(f, out var n) ? n : 0;

        /// <summary>The battle being fought at <paramref name="body"/>, or null. An ambush out on a hyperlane
        /// is filed under the system root but fought out on the lane, so it is at no body.</summary>
        public Battle BattleAt(int body)
        {
            foreach (var b in Battles)
                if (b.Body == body && b.Site == null) return b;
            return null;
        }

        public GroupOrder OrderOf(Ship leader) => Orders.TryGetValue(leader, out var o) ? o : null;

        // ---------------------------------------------------------------- per frame

        public void Update(double jd)
        {
            double seconds = (jd - lastJd) * Astro.SecondsPerDay;
            lastJd = jd;
            for (int i = Battles.Count - 1; i >= 0; i--)
            {
                var b = Battles[i];
                if (b.Fresh)
                {
                    b.Fresh = false;
                    continue;
                }
                b.Advance(seconds);
                // The player who has left the fight (flown out of it, or let go for the cargo) is free of it
                // now, not when the rest have finished: free to cruise, take the gate or engage elsewhere.
                foreach (var u in b.Units)
                    if (u.Fled && u.PlayerControlled && u.Ship != null && u.Ship.Battle == b) u.Ship.Battle = null;
                if (b.Over) Resolve(b, jd);
            }
            Interdictions(jd);
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        // ---------------------------------------------------------------- lane ambushes

        /// <summary>
        /// Each lurking raider watches its lane. A ship that passes its station since the last look
        /// (so a fast pass between frames still counts) sets off the pulse: every ship near that
        /// point of the lane is thrown out of it, its drives and shields knocked out for a minute or
        /// two, and the raiders close in.
        /// </summary>
        void Interdictions(double jd)
        {
            var lanes = Fleet.Lanes;
            if (lanes == null) return;
            var seen = new Dictionary<Ship, double>();
            foreach (var v in Fleet.Ships)
            {
                // The player's ship is never an ambush's victim or lurker: its phase is a frozen leftover
                // from wherever the helm was taken, not what the player is doing.
                if (v.Destroyed || v.IsEscort || v.Phase != ShipPhase.Lane || v.Battle != null || v == world.PilotedShip) continue;
                double p = Fleet.LaneProgress(v, jd);
                seen[v] = v.LaneLeg.From == v.LaneLeg.Lane.A ? p : 1 - p;
            }
            foreach (var lurker in Fleet.Ships)
            {
                if (lurker.Destroyed || lurker.IsEscort || !lurker.Faction.IsPirate || lurker.Phase != ShipPhase.Lurk || lurker.Battle != null || lurker == world.PilotedShip) continue;
                if (pulseReady.TryGetValue(lurker, out double ready) && jd < ready) continue;
                var lane = lurker.LaneLeg.Lane;
                double station = lurker.LaneLeg.From == lane.A ? lurker.LaneHold : 1 - lurker.LaneHold;
                double window = PulseRadiusKm / Math.Max(lurker.LaneLengthKm, 1);
                var victims = new List<Ship>();
                foreach (var kv in seen)
                {
                    var v = kv.Key;
                    // Already caught by another lurker's pulse this pass: it is fighting there.
                    if (v.LaneLeg.Lane != lane || v.Faction.IsPirate || v.Derelict || v.Battle != null) continue;
                    double now = kv.Value;
                    double before = laneSeen.TryGetValue(v, out double b) ? b : now;
                    if ((before - station) * (now - station) <= 0 || Math.Abs(now - station) < window) victims.Add(v);
                }
                if (victims.Count > 0) Interdict(lurker, station, victims, jd);
            }
            laneSeen.Clear();
            foreach (var kv in seen) laneSeen[kv.Key] = kv.Value;
        }

        void Interdict(Ship lurker, double station, List<Ship> victims, double jd)
        {
            var lane = lurker.LaneLeg.Lane;
            foreach (var v in victims)
            {
                double at = v.LaneLeg.From == lane.A ? station : 1 - station;
                var side = lurker.LaneHoldSide * (0.8 + rng.NextDouble() * 0.4);
                Fleet.PullOut(v, jd, at, side);
            }
            var helio = Fleet.LanePoint(lurker, jd, lurker.LaneHold) + lurker.LaneHoldSide;
            var b = new Battle(nextBattleId++, 0, "Ambush on the " + lane.Name + " lane", helio, jd, rng.Next())
            {
                Site = new LaneSite
                {
                    Hop = lurker.LaneLeg, FromOffset = lurker.LaneFromOffset, ToOffset = lurker.LaneToOffset, Progress = lurker.LaneHold, Side = lurker.LaneHoldSide,
                },
            };
            Hook(b);
            var approach = DVec3.Cross(DVec3.UnitZ, lurker.LaneHoldSide);
            b.ApproachAxis = approach.LengthSquared > 0 ? approach.Normalized : DVec3.UnitX;
            Join(b, lurker, 0, false);
            foreach (var v in victims) Join(b, v, 1, false);
            BattleUnit source = b.UnitOf(lurker);
            foreach (var u in b.Units)
            {
                if (u.Side != 1) continue;
                u.Stunned = 60 + rng.NextDouble() * 60;
                u.Combat.ZeroAllShields();
            }
            if (source != null) b.Emit(FxKind.Pulse, source.Pos, source.Pos, 30, 0, source, -1);
            Battles.Add(b);
            pulseReady[lurker] = jd + 1;
            lanes0().Danger.TryGetValue(lane, out double danger);
            lanes0().SetDanger(lane, danger + 3);
            world.Log(jd, $"{world.Pirates.Name} raiders ({lurker.Name}) spring an ambush on the {lane.Name} lane: {victims.Count} ship{(victims.Count == 1 ? "" : "s")} pulled out", WorldEventKind.Battle, lane.A);
            BumpDanger(lane.A, 1);
            BumpDanger(lane.B, 1);
            BattleStarted?.Invoke(b);
        }

        Hyperlanes lanes0() => Fleet.Lanes;

        void Hook(Battle b)
        {
            b.UnitDestroyed = OnUnitDestroyed;
            b.Launch = FighterAI.Launch;
            b.FighterStep = FighterAI.Step;
            b.UnitCaptured = (battle, u, captor) =>
            {
                captures[u] = captor;
                // The player's ship taken: it is the captor's prize now, no longer the player's to fly
                // (the Runtime sees PilotedShip go and ends the session).
                if (u.Ship != null && u.Ship == world.PilotedShip)
                {
                    world.PilotedShip = null;
                    u.PlayerControlled = false;
                }
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: {captor.Name} marines take the {u.Ship.Name}", WorldEventKind.Capture, battle.Body);
            };
            b.UnitSurrendered += (battle, u) =>
            {
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: the {u.Ship.Name} strikes its colours and hails for terms", WorldEventKind.Battle, battle.Body);
            };
            b.SurrenderDecided += OnSurrenderDecided;
            b.CargoDemanded += (battle, u, pirates) =>
            {
                if (u.Ship != null) world.Log(battle.StartJd + battle.Seconds / Astro.SecondsPerDay,
                    $"{battle.Name}: {pirates.Name} demand the {u.Ship.Name}'s cargo", WorldEventKind.Battle, battle.Body);
            };
        }

        /// <summary>
        /// Books a captor's terms for a hull that struck its colours (or a player who paid off pirates):
        /// ransomed cargo goes to the captor, a released hull carries on; a ship to be taken waits for the
        /// boarders and is booked by the capture hook like any other prize.
        /// </summary>
        void OnSurrenderDecided(Battle b, BattleUnit u, Faction captor, SurrenderTerms terms)
        {
            var ship = u.Ship;
            if (ship == null) return;
            double jd = b.StartJd + b.Seconds / Astro.SecondsPerDay;
            string who = captor != null ? captor.Name : "its foes";
            switch (terms)
            {
                case SurrenderTerms.RansomCargo:
                    if (ship.HasCargo)
                    {
                        if (captor != null && captor.IsPirate) Loot(ship);
                        else
                        {
                            if (captor != null) captor.Treasury += ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice * 0.8;
                            Economy.Release(ship);
                            ship.HasCargo = false;
                            ship.CargoDestination = null;
                            ship.Cargo = null;
                        }
                    }
                    world.Log(jd, $"{b.Name}: the {ship.Name} hands its cargo to {who} and is let go", WorldEventKind.Capture, b.Body);
                    break;
                case SurrenderTerms.LetGo:
                    world.Log(jd, $"{b.Name}: {who} let the {ship.Name} go", WorldEventKind.Battle, b.Body);
                    break;
                case SurrenderTerms.TakeShip:
                    world.Log(jd, $"{b.Name}: {who} send boarders to take the {ship.Name}", WorldEventKind.Capture, b.Body);
                    break;
            }
        }

        /// <summary>The lane with the most merchant traffic, preferring those that start at the raiders' own system.</summary>
        Lane BusiestLane(int system)
        {
            var lanes = Fleet.Lanes;
            Lane best = null;
            double bestScore = 0;
            foreach (var lane in lanes.Lanes)
            {
                double traffic = 0;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || !world.IsTrader(s)) continue;
                    if (s.Phase == ShipPhase.Lane && s.LaneLeg.Lane == lane) traffic += 1;
                    if (s.Journey != null)
                        foreach (var hop in s.Journey) if (hop.Lane == lane) traffic += 0.5;
                }
                double score = traffic * (lane.A == system || lane.B == system ? 2 : 1) * (0.7 + rng.NextDouble() * 0.6);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lane;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- prizes

        /// <summary>
        /// What boarders do with a ship they have taken. Pirates keep warships they can crew, strip
        /// merchant holds and mostly leave the hull adrift (or sell it to the breakers at Blackwater,
        /// or scuttle it); navies take serviceable enemy hulls as prizes and scuttle pirates.
        /// </summary>
        void DisposeOfPrize(Battle b, BattleUnit u, Faction captor, double jd)
        {
            var ship = u.Ship;
            if (ship == null || ship.Destroyed)
            {
                if (u.Kind == UnitKind.Platform) u.Platform.Platforms.Remove(u.Combat);
                return;
            }
            string how;
            double hull = ship.Combat.HullFraction;
            bool warship = ship.Spec.IsMilitary;
            if (captor.IsPirate)
            {
                if (ship.HasCargo) Loot(ship);
                double roll = rng.NextDouble();
                if (warship && hull >= 0.3) how = Steal(ship, captor, b) ? $"pressed into {world.Pirates.Name} service" : "scuttled";
                else if (warship) { Scuttle(ship); how = "scuttled"; }
                else if (roll < 0.55) { LeaveAdrift(ship, jd); how = "stripped and left adrift"; }
                else if (roll < 0.85)
                {
                    world.Pirates.Treasury += Economy.HullPrice(ship.Spec.Class) * 0.25;
                    Detach(ship);
                    ship.Destroyed = true;
                    how = $"taken to {HavenName()} and sold to the breakers";
                }
                else { Scuttle(ship); how = "scuttled"; }
            }
            else if (ship.Faction.IsPirate || hull < 0.3)
            {
                // A raider's surviving crew, taken off before it is scuttled, give away where they sail from.
                if (ship.Faction.IsPirate && HomeHaven(ship.Controller) is Settlement home)
                    Discover(home, captor, jd, $"prisoners taken off the {ship.Name} give it away");
                Scuttle(ship);
                how = "scuttled";
            }
            else
            {
                if (ship.HasCargo)
                {
                    captor.Treasury += ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice * 0.8;
                    Economy.Release(ship);
                    ship.HasCargo = false;
                    ship.CargoDestination = null;
                    ship.Cargo = null;
                }
                how = Steal(ship, captor, b) ? "taken as a prize" : "scuttled";
            }
            world.Log(jd, $"The {ship.Name} is {how} by the {captor.Name}", WorldEventKind.Capture, b.Body);
        }

        void Loot(Ship ship)
        {
            Economy.Release(ship);
            DeliverLoot(ship.CargoGood, ship.CargoTonnes);
            world.Pirates.Treasury += ship.CargoTonnes * GoodInfo.Of(ship.CargoGood).BasePrice * 0.3;
            ship.HasCargo = false;
            ship.CargoDestination = null;
            ship.Cargo = null;
        }

        void Scuttle(Ship ship)
        {
            // Its group (if it led one) gets a new flagship, so the escorts are not left frozen on a wreck.
            Detach(ship);
            if (ship.HasCargo) Economy.Release(ship);
            ship.HasCargo = false;
            ship.Destroyed = true;
            losses.TryGetValue(ship.Faction, out int n);
            losses[ship.Faction] = n + 1;
        }

        void LeaveAdrift(Ship ship, double jd)
        {
            Detach(ship);
            ship.Derelict = true;
            ship.DerelictSinceJd = jd;
            ship.Cargo = "Derelict: boarded, stripped and abandoned";
        }

        /// <summary>The prize changes hands: a prize crew takes over and it joins the captor (a group of theirs in the fight, if there is room).</summary>
        bool Steal(Ship ship, Faction captor, Battle b)
        {
            Detach(ship);
            var old = ship.Faction;
            losses.TryGetValue(old, out int n);
            losses[old] = n + 1;
            ship.Faction = captor;
            if (captor.Prefix.Length > 0 && ship.Name.StartsWith(old.Prefix + " ")) ship.Name = captor.Prefix + ship.Name.Substring(old.Prefix.Length);
            ship.Combat.Interior.HandOver(ship.Combat.Spec.Crew * 0.3);
            Ship group = null;
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Dead || u.Ship == ship || u.Ship.Faction != captor) continue;
                var leader = u.Ship.Controller;
                if (!leader.Destroyed && leader.Faction == captor && !leader.IsEscort && leader != world.PilotedShip) group = leader;
            }
            if (ship.Spec.IsMilitary && group != null && ship.Phase == group.Phase && ship.Orbit?.Body == group.Orbit?.Body)
            {
                int index = 0;
                foreach (var e in Fleet.Ships) if (e.Leader == group) index++;
                if (index < 7)
                {
                    Fleet.AddEscort(group, ship, Fleet.SlotFor(index));
                    return true;
                }
            }
            if (ship.Spec.IsMilitary)
            {
                ship.Role = ShipRole.Patrol;
                ship.Route = group?.Route ?? (captor.IsPirate && HavenBody() >= 0 ? new[] { HavenBody() } : OwnPorts(captor, ship.Orbit.Body));
            }
            return true;
        }

        /// <summary>A faction's own settlement bodies (a navy's prize patrols those), or the given body if it has none.</summary>
        int[] OwnPorts(Faction f, int fallback)
        {
            var own = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Owner == f && !own.Contains(s.Body)) own.Add(s.Body);
            return own.Count > 0 ? own.ToArray() : new[] { fallback };
        }

        /// <summary>A ship leaves its group (taken, or abandoned): it keeps the group's motion as its own, and the group gets a new flagship if it was the old one.</summary>
        void Detach(Ship ship)
        {
            if (!ship.IsEscort)
            {
                Succession(ship);
                return;
            }
            var lead = ship.Leader;
            ship.Leader = null;
            ship.Phase = lead.Phase;
            ship.Orbit = OwnSpot(lead.Orbit, ship, lead);
            ship.DepartureOrbit = lead.DepartureOrbit;
            ship.DestinationBody = lead.DestinationBody;
            ship.OriginBody = lead.OriginBody;
            ship.PhaseStartJd = lead.PhaseStartJd;
            ship.PhaseEndJd = lead.PhaseEndJd;
            ship.FrameBody = lead.FrameBody;
            ship.Path = lead.Path;
            ship.PathCumulative = lead.PathCumulative;
            ship.PathLength = lead.PathLength;
            ship.AccelKmPerDay2 = lead.AccelKmPerDay2;
            ship.DepartureVelocity = lead.DepartureVelocity;
            ship.ArrivalVelocity = lead.ArrivalVelocity;
            ship.Journey = lead.Journey;
            ship.JourneyIndex = lead.JourneyIndex;
            ship.FinalBody = lead.FinalBody;
            ship.LaneLeg = lead.LaneLeg;
            ship.LaneFromOffset = lead.LaneFromOffset;
            ship.LaneToOffset = lead.LaneToOffset;
            ship.LaneLengthKm = lead.LaneLengthKm;
            ship.LaneStartProgress = lead.LaneStartProgress;
            ship.LaneEndProgress = lead.LaneEndProgress;
            ship.LaneHold = lead.LaneHold;
            ship.LaneHoldSide = lead.LaneHoldSide;
            CopyRest(lead, ship);
        }

        /// <summary>
        /// A ship leaving its group keeps the group's orbit, but a little way along it (where it was
        /// flying in formation), not on top of its old flagship.
        /// </summary>
        static ParkingOrbit OwnSpot(ParkingOrbit orbit, Ship ship, Ship lead)
        {
            if (orbit == null || orbit.Fixed || orbit.RadiusKm <= 0) return orbit;
            double gap = Fleet.SafeDistanceKm(ship, lead) * 1.5;
            return new ParkingOrbit
            {
                Body = orbit.Body, RadiusKm = orbit.RadiusKm, U = orbit.U, V = orbit.V, Center = orbit.Center,
                EpochJd = orbit.EpochJd, PhaseAtEpoch = orbit.PhaseAtEpoch - gap / orbit.RadiusKm, RateRadPerDay = orbit.RateRadPerDay,
            };
        }

        /// <summary>The rest of a ship's motion state that a group's escorts and heirs take over with it (lane shape, ring).</summary>
        static void CopyRest(Ship from, Ship to)
        {
            to.LaneStar = from.LaneStar;
            to.LaneShape = from.LaneShape;
            to.Ring = from.Ring;
            to.HoldPoint = from.HoldPoint;
        }

        // ---------------------------------------------------------------- meeting the enemy

        /// <summary>A ship has arrived at a body: fight if anyone there is hostile, or join a fight already on.</summary>
        public void OnArrived(Ship ship, double jd)
        {
            // On the ground (or on the way down or up) a ship is out of the fight.
            if (ship.Destroyed || ship.Battle != null || ship.Planetside) return;
            int body = ship.Orbit.Body;
            // Anyone who turns up at a hidden haven's rock sees it.
            if (!ship.Faction.IsPirate)
                foreach (var st in Economy.At(body))
                    if (st.Hidden) Discover(st, ship.Faction, jd, $"the {ship.Name} comes upon it");
            var existing = BattleAt(body);
            if (existing != null)
            {
                // Join the side it can fight beside: against one side's leader and not the other's.
                bool h0 = Diplomacy.Hostile(ship.Faction, existing.SideLeaders[0]);
                bool h1 = Diplomacy.Hostile(ship.Faction, existing.SideLeaders[1]);
                int side = h0 && !h1 ? 1 : h1 && !h0 ? 0 : -1;
                if (side >= 0) Join(existing, ship, side, true);
                return;
            }
            TryStartBattle(body, ship.Faction, jd);
        }

        /// <summary>A player flies a piloted ship (excluded from every other gathering of "ships present" at a
        /// body, see <see cref="PresentGroups"/>) into range of a hostile: starts a fight there if one isn't
        /// already on, or joins the one that is, then places the piloted ship into it at its own actual flown
        /// position -- not a formation slot -- since the Runtime pilot loop, not this battle, owns its pose
        /// from here on (see <see cref="BattleUnit.PlayerControlled"/>). <paramref name="body"/> is the body the
        /// pilot is actually flying beside (the frame <paramref name="offsetFromBodyKm"/> is measured from), not
        /// the ship's scheduled orbit, which for a hull taken over mid-transit is still its far-off destination.
        /// Returns null (no-op) if nothing hostile and willing to fight is actually present.</summary>
        public Battle TryStartPlayerBattle(Ship ship, int body, DVec3 offsetFromBodyKm, DVec3 forward, double jd)
        {
            if (ship.Destroyed || ship.Battle != null || ship.Planetside) return null;
            // Only in the body's own neighbourhood (the frame body never changes mid-flight, however far the
            // pilot goes), and only into a fight it has actually reached: one placed beyond the battle's
            // edge would be counted as escaped on the very next step. A fight it starts is fought where it is.
            if (offsetFromBodyKm.Length > ScaleModel.NeighbourhoodKm(Catalog[body])) return null;
            var b = BattleAt(body);
            if (b != null && (offsetFromBodyKm - b.Anchor).Length > Battle.EscapeKm * 0.8) return null;
            // Already left this one: not back in until it is over.
            if (b != null && b.UnitOf(ship) != null) return null;
            b ??= TryStartBattle(body, ship.Faction, jd, offsetFromBodyKm);
            if (b == null) return null;
            bool h0 = Diplomacy.Hostile(ship.Faction, b.SideLeaders[0]);
            bool h1 = Diplomacy.Hostile(ship.Faction, b.SideLeaders[1]);
            int side = h0 && !h1 ? 1 : h1 && !h0 ? 0 : -1;
            if (side < 0) return null;
            var unit = b.AddShip(ship, side, offsetFromBodyKm - b.Anchor, forward);
            unit.PlayerControlled = true;
            return b;
        }

        /// <summary>Starts a battle at <paramref name="body"/> if <paramref name="aggressor"/> has a quarrel with anyone there.</summary>
        public Battle TryStartBattle(int body, Faction aggressor, double jd, DVec3? anchor = null)
        {
            var present = PresentGroups(body);
            var hostile = new HashSet<Faction>();
            bool armed = aggressor.IsMilitary || aggressor.IsPirate;
            foreach (var s in present)
                if (Diplomacy.Hostile(aggressor, s.Faction))
                {
                    hostile.Add(s.Faction);
                    armed |= s.Spec.IsMilitary || s.Faction.IsPirate;
                }
            foreach (var st in Economy.At(body))
                if (st.Platforms.Count > 0 && Diplomacy.Hostile(aggressor, st.Owner))
                {
                    hostile.Add(st.Owner);
                    armed = true;
                }
            if (hostile.Count == 0 || !armed) return null;

            // A fight needs someone fit and willing to start it: a healthy armed warship or a platform.
            bool aggressorArmed = false, defenderArmed = false;
            foreach (var s in present)
            {
                bool military = (s.Spec.IsMilitary || s.Faction.IsPirate) && Willing(s.Controller, jd) && s.Combat.Armed;
                if (s.Faction == aggressor && military) aggressorArmed = true;
                if (hostile.Contains(s.Faction) && military) defenderArmed = true;
            }
            foreach (var st in Economy.At(body))
                if (st.Platforms.Count > 0 && hostile.Contains(st.Owner)) defenderArmed = true;
            if (!aggressorArmed && !defenderArmed) return null;

            var b = NewBattle(body, jd, present, anchor);
            foreach (var s in present)
            {
                int side = SideFor(s.Faction, aggressor, hostile);
                if (side >= 0) Join(b, s, side, false);
            }
            foreach (var st in Economy.At(body))
            {
                int side = SideFor(st.Owner, aggressor, hostile);
                if (side < 0) continue;
                for (int i = 0; i < st.Platforms.Count; i++)
                {
                    double a = Astro.TwoPi * i / Math.Max(1, st.Platforms.Count);
                    var pos = Spot(b, side) * 0.4 + new DVec3(Math.Cos(a), Math.Sin(a), 0.1) * 6;
                    b.AddPlatform(st, st.Platforms[i], side, pos);
                }
            }
            Battles.Add(b);
            world.Log(jd, $"{b.Name}: {Describe(b, 0)} engage {Describe(b, 1)}", WorldEventKind.Battle, body);
            BumpDanger(body, 1.5);
            BattleStarted?.Invoke(b);
            return b;
        }

        /// <summary>Fit to pick a fight: not withdrawing and not badly hurt.</summary>
        bool Willing(Ship leader, double jd) =>
            !(retreating.TryGetValue(leader, out double until) && jd < until) && leader.Combat.Damage < 0.45 && leader.Combat.Armed;

        int SideFor(Faction f, Faction aggressor, HashSet<Faction> hostile)
        {
            if (f == aggressor) return 0;
            if (hostile.Contains(f)) return 1;
            if (Diplomacy.Hostile(f, aggressor)) return 1;
            foreach (var h in hostile)
                if (Diplomacy.Hostile(f, h)) return 0;
            return -1;
        }

        /// <summary>Ships parked at a body and free to fight (escorts included), not already in a battle.</summary>
        List<Ship> PresentGroups(int body)
        {
            var list = new List<Ship>();
            foreach (var s in Fleet.Ships)
            {
                // A ship under direct player control isn't swept into a fight the AI decides to start.
                if (s.Destroyed || s.Battle != null || s == world.PilotedShip) continue;
                var c = s.Controller;
                // A stripped, abandoned hulk is not a fighting group.
                if (c.Destroyed || s.Derelict || c.Derelict || !Fleet.IsAt(c, body)) continue;
                list.Add(s);
            }
            return list;
        }

        Battle NewBattle(int body, double jd, List<Ship> present, DVec3? at = null)
        {
            var b0 = Catalog[body];
            DVec3 dir = DVec3.Zero;
            foreach (var s in present)
                if (s.Controller.Orbit != null) dir += s.Controller.Orbit.OffsetAt(jd).Normalized;
            if (dir.LengthSquared < 1e-6) dir = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, 0.2);
            var anchor = at ?? dir.Normalized * Battle.AnchorDistanceKm(b0);
            battleCount.TryGetValue(body, out int n);
            battleCount[body] = n + 1;
            string name = (n == 0 ? "Battle of " : Ordinal(n + 1) + " Battle of ") + b0.Name;
            var b = new Battle(nextBattleId++, body, name, anchor, jd, rng.Next());
            Hook(b);
            var approach = DVec3.Cross(anchor.Normalized, DVec3.UnitZ);
            if (approach.LengthSquared < 1e-6) approach = DVec3.UnitX;
            b.ApproachAxis = approach.Normalized;
            return b;
        }

        /// <summary>Where a side's line starts: the two sides open about 26 km apart.</summary>
        static DVec3 Spot(Battle b, int side) => b.ApproachAxis * (side == 0 ? 13 : -13);

        void Join(Battle b, Ship ship, int side, bool late)
        {
            // A ship under direct player control enters a fight only through TryStartPlayerBattle.
            if (ship.Battle != null || ship.Destroyed || ship == world.PilotedShip) return;
            var groupCentre = late ? b.ApproachAxis * (side == 0 ? 30 : -30) : Spot(b, side);
            var jitter = new DVec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, (rng.NextDouble() - 0.5) * 0.4) * 6;
            var face = -groupCentre;
            if (ship.IsEscort)
            {
                var leaderUnit = b.UnitOf(ship.Leader);
                if (leaderUnit == null && ship.Leader.Battle == null) Join(b, ship.Leader, side, late);
                if (ship.Battle != null) return;
                leaderUnit = b.UnitOf(ship.Leader);
                if (leaderUnit != null)
                {
                    var slot = ship.FormationSlot * (ship.Leader.LengthKm * 1.3 + 0.8);
                    leaderUnit.Basis(out var r, out var up);
                    var u = b.AddShip(ship, side, leaderUnit.Pos + r * slot.X + up * slot.Y + leaderUnit.Forward * slot.Z, leaderUnit.Forward);
                    u.Leader = leaderUnit;
                    u.Slot = slot;
                    return;
                }
            }
            b.AddShip(ship, side, groupCentre + jitter, face);
            foreach (var e in Fleet.Ships)
                if (e.Leader == ship && !e.Destroyed && e.Battle == null) Join(b, e, side, late);
        }

        void OnUnitDestroyed(Battle b, BattleUnit u)
        {
            if (u.Ship != null)
            {
                u.Ship.Destroyed = true;
                losses.TryGetValue(u.Faction, out int n);
                losses[u.Faction] = n + 1;
                Wrecks.Add(u, b.Body, b.Anchor, b.StartJd + b.Seconds / Astro.SecondsPerDay, site: b.Site);
            }
            BumpDanger(b.Body, u.Ship != null && !u.Ship.Spec.IsMilitary ? 2.0 : 0.6);
        }

        // ---------------------------------------------------------------- after the battle

        void Resolve(Battle b, double jd)
        {
            Battles.Remove(b);
            Ended.Add(b);
            if (Ended.Count > 30) Ended.RemoveAt(0);

            var lost = new int[2];
            var fled = new int[2];
            var lootTonnes = 0.0;
            var lootValue = 0.0;
            // Ships already let go of this battle (a pilot who left it) have moved on: whatever they are
            // doing now -- flying, another fight -- is not this battle's to reset.
            var left = new HashSet<BattleUnit>();
            foreach (var u in b.Units)
                if (u.Ship != null && u.Ship.Battle != b) left.Add(u);
            foreach (var u in b.Units)
            {
                if (u.Side < 0 || u.Side > 1) continue;
                if (u.Dead && u.Kind != UnitKind.Fighter) lost[u.Side]++;
                if (u.Fled) fled[u.Side]++;
                if (u.Kind == UnitKind.Platform && u.Dead) u.Platform.Platforms.Remove(u.Combat);
                // Boarders still fighting aboard anything that wasn't taken (a platform too) are gone with the battle.
                if (!u.Captured && !left.Contains(u)) u.Combat.Interior.BoardersGone();
                if (u.Ship == null || left.Contains(u)) continue;
                var ship = u.Ship;
                ship.Battle = null;
                if (u.Dead)
                {
                    if (ship.HasCargo)
                    {
                        Economy.Release(ship);
                        if (b.SideLeaders[1 - u.Side] != null && b.SideLeaders[1 - u.Side].IsPirate)
                        {
                            lootTonnes += ship.CargoTonnes * 0.6;
                            lootValue += ship.CargoTonnes * 0.6 * GoodInfo.Of(ship.CargoGood).BasePrice;
                            DeliverLoot(ship.CargoGood, ship.CargoTonnes * 0.6);
                        }
                        ship.HasCargo = false;
                    }
                    ship.CargoDestination = null;
                    ship.Cargo = null;
                }
            }
            foreach (var u in b.Units)
                if (u.Ship != null && u.Dead) Succession(u.Ship);
            foreach (var u in b.Units)
            {
                if (!captures.TryGetValue(u, out var captor)) continue;
                captures.Remove(u);
                if (!u.Captured || u.Dead) continue;
                DisposeOfPrize(b, u, captor, jd);
            }

            // Survivors go back to their own schedules; those that ran, or lost, withdraw to refit.
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Dead || u.Ship.IsEscort || u.Ship.Destroyed || u.Ship.Derelict) continue;
                // The player's ship stays the player's: its schedule is set again only on release.
                if (u.Ship == world.PilotedShip || left.Contains(u)) continue;
                bool beaten = u.Fled || (b.Winner >= 0 && u.Side != b.Winner);
                // Pulled out of a lane: back in and on their way. Raiders who won take the loot home;
                // beaten ones run.
                if (u.Ship.Phase == ShipPhase.Adrift)
                {
                    Fleet.ResumeLane(u.Ship, jd);
                    continue;
                }
                if (u.Ship.Phase == ShipPhase.Lurk)
                {
                    if (beaten || b.Winner == u.Side)
                    {
                        retreating[u.Ship] = jd + (beaten ? 6 : 4);
                        Fleet.ResumeLane(u.Ship, jd);
                    }
                    continue;
                }
                if (beaten) retreating[u.Ship] = jd + 6;
                // The beaten leave at once; everyone else keeps their plans unless the fight overran them.
                if (beaten || u.Ship.PhaseEndJd <= jd || !(u.Ship.Spec.IsMilitary || u.Ship.Faction.IsPirate)) Fleet.ScheduleNext(u.Ship, jd);
            }
            // A navy that beats raiders off may follow the survivors back to a hidden haven.
            var victor = b.Winner >= 0 ? b.SideLeaders[b.Winner] : null;
            if (victor != null && !victor.IsPirate)
                foreach (var u in b.Units)
                {
                    if (u.Ship == null || u.Dead || u.Ship.IsEscort || !u.Ship.Faction.IsPirate || u.Side == b.Winner) continue;
                    if (rng.NextDouble() < TrailChance) TrailHome(u.Ship, victor, jd);
                }
            lastBattleEndJd[b.Body] = jd;
            foreach (var u in b.Units)
                if (u.Kind == UnitKind.Fighter && u.Fighter?.Carrier?.Combat != null && u.Present)
                    u.Fighter.Carrier.Combat.RestoreFighters(1);

            string winner = b.Winner >= 0 ? Describe(b, b.Winner) + " hold the field" : "both sides break off";
            string text = $"{b.Name} ends: {winner}. Losses {Short(b, 0)} {lost[0]}, {Short(b, 1)} {lost[1]}";
            if (lootTonnes > 0) text += $"; pirates take {lootTonnes:N0} t of cargo";
            world.Log(jd, text, WorldEventKind.Battle, b.Body);
            if (lootValue > 0) world.Pirates.Treasury += lootValue * 0.3;
        }

        void DeliverLoot(Good g, double tonnes)
        {
            var haven = FirstLiveHaven();
            if (haven != null) haven.Stock[(int)g] += tonnes;
        }

        /// <summary>When a flagship is lost its strongest surviving escort takes the group.</summary>
        /// <summary>
        /// The player takes the helm of a group's flagship: its escorts carry on under the strongest of
        /// them (as when a flagship is lost) rather than holding station on a hull that no longer runs
        /// the group's schedule -- frozen at the spot piloting began, or left behind for good if it jumps
        /// through the gate.
        /// </summary>
        public void HandOverCommand(Ship leader)
        {
            if (leader == null || leader.IsEscort) return;
            Succession(leader);
        }

        void Succession(Ship dead)
        {
            if (dead.IsEscort) return;
            Ship heir = null;
            foreach (var s in Fleet.Ships)
                if (s.Leader == dead && !s.Destroyed && (heir == null || s.Combat.Spec.Hull > heir.Combat.Spec.Hull))
                    heir = s;
            if (heir == null)
            {
                Orders.Remove(dead);
                return;
            }
            heir.Leader = null;
            heir.Role = ShipRole.Patrol;
            heir.Route = dead.Route;
            heir.RouteIndex = dead.RouteIndex;
            heir.Phase = dead.Phase;
            // The old flagship is not always gone (a capture leaves it flying on under new colours): take
            // over a little way along its orbit rather than sharing its exact spot with it indefinitely.
            heir.Orbit = OwnSpot(dead.Orbit, heir, dead);
            heir.DepartureOrbit = dead.DepartureOrbit;
            heir.DestinationBody = dead.DestinationBody;
            heir.OriginBody = dead.OriginBody;
            heir.PhaseStartJd = dead.PhaseStartJd;
            heir.PhaseEndJd = dead.PhaseEndJd;
            heir.FrameBody = dead.FrameBody;
            heir.Path = dead.Path;
            heir.PathCumulative = dead.PathCumulative;
            heir.PathLength = dead.PathLength;
            heir.AccelKmPerDay2 = dead.AccelKmPerDay2;
            heir.DepartureVelocity = dead.DepartureVelocity;
            heir.ArrivalVelocity = dead.ArrivalVelocity;
            heir.Journey = dead.Journey;
            heir.JourneyIndex = dead.JourneyIndex;
            heir.FinalBody = dead.FinalBody;
            heir.LaneLeg = dead.LaneLeg;
            heir.LaneFromOffset = dead.LaneFromOffset;
            heir.LaneToOffset = dead.LaneToOffset;
            heir.LaneLengthKm = dead.LaneLengthKm;
            heir.LaneStartProgress = dead.LaneStartProgress;
            heir.LaneEndProgress = dead.LaneEndProgress;
            heir.LaneHold = dead.LaneHold;
            heir.LaneHoldSide = dead.LaneHoldSide;
            CopyRest(dead, heir);
            foreach (var s in Fleet.Ships)
                if (s.Leader == dead && s != heir) s.Leader = heir;
            if (Orders.TryGetValue(dead, out var o)) Orders[heir] = o;
            Orders.Remove(dead);
        }

        // ---------------------------------------------------------------- orders

        /// <summary>Fleet hook: where a warship or raider group goes next (its own body to hold), or -1.</summary>
        public int ChooseNext(Ship leader, int here, double jd)
        {
            if (!leader.Spec.IsMilitary && !leader.Faction.IsPirate) return -1;
            var order = Decide(leader, here, jd);
            if (!Orders.TryGetValue(leader, out var current) || current.Kind != order.Kind || current.Body != order.Body)
            {
                order.SinceJd = jd;
                Orders[leader] = order;
            }
            return order.Body;
        }

        public double Dwell(Ship ship, double normal)
        {
            if (retreating.TryGetValue(ship, out double until) && lastJd < until) return 0.05;
            if (!Orders.TryGetValue(ship, out var o)) return normal;
            switch (o.Kind)
            {
                case OrderKind.Repair:
                case OrderKind.Siege:
                case OrderKind.Assault:
                case OrderKind.Defend: return 1.0;
                case OrderKind.Raid: return 1.5 + rng.NextDouble();
                default: return normal;
            }
        }

        GroupOrder Decide(Ship leader, int here, double jd)
        {
            var f = leader.Faction;
            bool withdrawing = retreating.TryGetValue(leader, out double until) && jd < until;
            if (f.IsPirate) return DecidePirate(leader, here, jd, withdrawing);

            if (withdrawing || GroupDamage(leader) > 0.22 || MissilesLow(leader) || !leader.Combat.Armed || HangarsLow(leader) || MarinesLow(leader))
            {
                int yard = NearestYard(f, here, jd);
                if (yard >= 0) return new GroupOrder { Kind = OrderKind.Repair, Body = yard };
            }

            // Defend: an own port with enemies at it.
            int threatened = -1;
            double bestDays = double.MaxValue;
            foreach (var s in Economy.Settlements)
            {
                if (s.Owner != f) continue;
                if (!EnemiesAt(s.Body, f)) continue;
                double days = s.Body == here ? 0 : Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                if (days < bestDays && days < 40)
                {
                    bestDays = days;
                    threatened = s.Body;
                }
            }
            if (threatened >= 0) return new GroupOrder { Kind = OrderKind.Defend, Body = threatened };

            // At a haven it found, with the garrison broken: stay and finish the job.
            foreach (var s in Economy.At(here))
                if (Assailable(s, f) && s.Platforms.Count == 0)
                    return new GroupOrder { Kind = OrderKind.Assault, Body = here };

            // Besieging: stay until the port falls or defenders show up.
            foreach (var s in Economy.At(here))
                if (Diplomacy.Hostile(f, s.Owner) && !s.Owner.IsPirate && s.Platforms.Count == 0 && s.DaysSinceCapture > CaptureGraceDays && AtWar(f))
                    return new GroupOrder { Kind = OrderKind.Siege, Body = here };

            bool atWar = AtWar(f);
            if (atWar && !IsHomeGuard(leader))
            {
                double strength = GroupStrength(leader);
                int target = -1;
                double best = 0;
                foreach (var s in Economy.Settlements)
                {
                    if (!Diplomacy.Hostile(f, s.Owner) || s.Owner.IsPirate) continue;
                    double defence = DefenceAt(s.Body, f);
                    if (strength < defence * 1.25) continue;
                    double days = Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                    double value = (s.Population / 1e5 + 2) * (s.HasShipyard ? 2 : 1) / (defence + 200) / (days + 5);
                    if (value > best)
                    {
                        best = value;
                        target = s.Body;
                    }
                }
                if (target >= 0) return new GroupOrder { Kind = OrderKind.Attack, Body = target };
            }

            // Hunt pirates working an own port.
            foreach (var s in Economy.Settlements)
                if (s.Owner == f && s.Body != here && PiratesAt(s.Body))
                    return new GroupOrder { Kind = OrderKind.Hunt, Body = s.Body };

            // Strike a hidden haven this navy has found, if the group can take its garrison.
            if (!IsHomeGuard(leader) && AssaultTarget(leader, here, jd) is Settlement mark)
                return new GroupOrder { Kind = OrderKind.Assault, Body = mark.Body };

            return new GroupOrder { Kind = OrderKind.Patrol, Body = NextPatrol(leader, here) };
        }

        GroupOrder DecidePirate(Ship leader, int here, double jd, bool withdrawing)
        {
            // A fresh plan: an ambush planned earlier and not yet reached no longer stands.
            leader.LurkLane = null;
            var home = HomeHaven(leader);
            // A group whose haven has been taken or destroyed is out of the game: it lies low where it is,
            // no raids and no ambushes, until the haven is rebuilt.
            if (home != null && home.Fallen) return new GroupOrder { Kind = OrderKind.Patrol, Body = here };
            home ??= FirstLiveHaven();
            int haven = home?.Body ?? -1;
            if (withdrawing || GroupDamage(leader) > 0.3 || MissilesLow(leader) || !leader.Combat.Armed)
                return new GroupOrder { Kind = OrderKind.Repair, Body = haven >= 0 ? haven : here };

            // Home is under attack: back to defend it.
            if (haven >= 0 && EnemiesAt(haven, leader.Faction) && (haven == here || Fleet.EstimateTravelDays(leader, here, haven, jd) < 30))
                return new GroupOrder { Kind = OrderKind.Defend, Body = haven };

            // Lie in wait beside a busy hyperlane.
            var lanes = Fleet.Lanes;
            if (lanes != null && rng.NextDouble() < 0.55)
            {
                int system = lanes.SystemOf(here);
                var lane = BusiestLane(system);
                if (lane != null)
                {
                    if (lane.A == system || lane.B == system)
                    {
                        leader.LurkLane = lane;
                        leader.LurkAt = 0.3 + rng.NextDouble() * 0.4;
                        leader.LurkDays = 2.5 + rng.NextDouble() * 2;
                        return new GroupOrder { Kind = OrderKind.Ambush, Body = lane.Other(system) };
                    }
                    double toA = (world.Ephemeris.HelioAt(lane.A, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                    double toB = (world.Ephemeris.HelioAt(lane.B, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                    return new GroupOrder { Kind = OrderKind.Ambush, Body = toA < toB ? lane.A : lane.B };
                }
            }

            // Stay on a hunting ground a while if it has traffic and no warships.
            if (Orders.TryGetValue(leader, out var o) && o.Kind == OrderKind.Raid && o.Body == here && jd - o.SinceJd < 4 && DefenceAt(here, leader.Faction) < GroupStrength(leader))
                return new GroupOrder { Kind = OrderKind.Raid, Body = here };

            int target = -1;
            double best = 0;
            foreach (var s in Economy.Settlements)
            {
                if (s.Kind == SettlementKind.Haven || s.Body == here) continue;
                double traffic = 1;
                foreach (var t in Fleet.Ships)
                    if (!t.Destroyed && world.IsTrader(t) && (t.DestinationBody == s.Body || (t.Orbit != null && t.Orbit.Body == s.Body))) traffic++;
                double defence = DefenceAt(s.Body, leader.Faction);
                // Raiders take chances against a port's guns, but prefer the soft ones.
                if (defence > GroupStrength(leader) * 1.6) continue;
                double days = Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                double value = traffic / (defence + GroupStrength(leader) * 0.3 + 100) / (days + 3) * (0.7 + rng.NextDouble() * 0.6);
                if (value > best)
                {
                    best = value;
                    target = s.Body;
                }
            }
            if (target < 0) target = haven >= 0 && haven != here ? haven : NextPatrol(leader, here);
            return new GroupOrder { Kind = OrderKind.Raid, Body = target };
        }

        bool IsHomeGuard(Ship leader)
        {
            // Each navy keeps its oldest surviving group at home.
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction == leader.Faction && s.Spec.IsMilitary)
                    return s == leader;
            return false;
        }

        int NextPatrol(Ship leader, int here)
        {
            var own = new List<int>();
            foreach (var s in Economy.Settlements)
                if (s.Owner == leader.Faction && !own.Contains(s.Body)) own.Add(s.Body);
            if (own.Count == 0 && leader.Route != null) own.AddRange(leader.Route);
            own.Remove(here);
            if (own.Count == 0) return here;
            return own[rng.Next(own.Count)];
        }

        int NearestYard(Faction f, int here, double jd)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            foreach (var s in Economy.Settlements)
            {
                if (s.Owner != f || (!s.HasShipyard && s.Kind != SettlementKind.Haven)) continue;
                double d = s.Body == here ? 0 : (world.Ephemeris.HelioAt(s.Body, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s.Body;
                }
            }
            return best;
        }

        int HavenBody() => FirstLiveHaven()?.Body ?? -1;

        string HavenName() => FirstLiveHaven()?.Name ?? "a pirate haven";

        /// <summary>The first haven still standing (not taken or destroyed), or null.</summary>
        Settlement FirstLiveHaven()
        {
            foreach (var s in Economy.Settlements)
                if (s.Kind == SettlementKind.Haven && !s.Fallen) return s;
            return null;
        }

        /// <summary>The haven a raider group sails from (the first stop on its route, if a haven is there), or null.</summary>
        public Settlement HomeHaven(Ship ship)
        {
            var route = ship?.Controller.Route;
            if (route == null || route.Length == 0) return null;
            foreach (var s in Economy.At(route[0]))
                if (s.Kind == SettlementKind.Haven) return s;
            return null;
        }

        // ---------------------------------------------------------------- hidden havens

        /// <summary>How far a ship's scanners pick out a hidden haven: its reactors, its traffic, its guns.</summary>
        public const double ScanRangeKm = 2e6;

        /// <summary>Chance a navy that wins a fight follows each beaten raider group home.</summary>
        public const double TrailChance = 0.3;

        /// <summary>Days an assaulting group must hold a haven whose garrison is broken before it is boarded or levelled.</summary>
        public const double HavenAssaultDays = 1;

        /// <summary>Days the pirates take to rebuild a haven that was taken or destroyed.</summary>
        public const double HavenRebuildDays = 180;

        /// <summary>
        /// <paramref name="by"/> finds a hidden haven (if it had not already): it is logged, goes on the
        /// map, and that faction's navy may now strike it. Returns whether this was news to it.
        /// </summary>
        public bool Discover(Settlement st, Faction by, double jd, string how)
        {
            if (st == null || !st.Hidden || by == null || by.IsPirate || st.KnownTo.Contains(by)) return false;
            bool first = st.KnownTo.Count == 0;
            st.KnownTo.Add(by);
            string text = first
                ? $"The {by.Name} find a hidden {world.Pirates.Name} haven, {st.Name}, on {Catalog[st.Body].Name}: {how}"
                : $"The {by.Name} learn where {st.Name} ({Catalog[st.Body].Name}) lies: {how}";
            world.Log(jd, text, WorldEventKind.Info, st.Body);
            return true;
        }

        /// <summary>A ship of <paramref name="by"/> sweeps its scanners from a heliocentric position: every hidden
        /// haven within <see cref="ScanRangeKm"/> is found. Returns the havens found this time.</summary>
        public List<Settlement> ScanFrom(Faction by, DVec3 helioKm, double jd, string who = null)
        {
            var found = new List<Settlement>();
            if (by == null || by.IsPirate) return found;
            foreach (var st in Economy.Settlements)
            {
                if (!st.Hidden || st.KnownTo.Contains(by)) continue;
                if ((world.Ephemeris.HelioAt(st.Body, jd) - helioKm).Length > ScanRangeKm) continue;
                if (Discover(st, by, jd, (who != null ? "the " + who + "'s" : "a ship's") + " scanners pick it out")) found.Add(st);
            }
            return found;
        }

        /// <summary>A raider beaten by <paramref name="tracker"/> is followed back to its haven, if that haven is hidden.</summary>
        public bool TrailHome(Ship raider, Faction tracker, double jd)
        {
            var home = HomeHaven(raider);
            if (home == null || tracker == null || tracker.IsPirate) return false;
            return Discover(home, tracker, jd, $"{tracker.Name} ships follow the beaten {raider.Name} home");
        }

        /// <summary>Every navy's warships sweep their scanners once a day.</summary>
        void Scans(double jd)
        {
            bool anyHidden = false;
            foreach (var st in Economy.Settlements) anyHidden |= st.Hidden;
            if (!anyHidden) return;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || s.Derelict || s == world.PilotedShip || !s.Spec.IsMilitary || s.Faction.IsPirate) continue;
                ScanFrom(s.Faction, Fleet.StateOf(s, jd).HelioKm, jd, s.Name);
            }
        }

        /// <summary>A haven <paramref name="f"/>'s navy would strike on its own: a hidden one it has found, still
        /// standing. (Blackwater, the haven everyone knows of over Deimos, is an open secret the navies leave
        /// be; the player can still go in.)</summary>
        bool Assailable(Settlement s, Faction f) =>
            s.Kind == SettlementKind.Haven && !s.Fallen && s.Hidden && s.KnownTo.Contains(f) && Diplomacy.Hostile(f, s.Owner);

        /// <summary>The nearest found haven the group is strong enough to storm (half again its garrison and any raiders there), or null.</summary>
        Settlement AssaultTarget(Ship leader, int here, double jd)
        {
            var f = leader.Faction;
            double strength = GroupStrength(leader);
            Settlement best = null;
            double bestDays = 40;
            foreach (var s in Economy.Settlements)
            {
                if (!Assailable(s, f) || strength < DefenceAt(s.Body, f) * 1.5) continue;
                double days = s.Body == here ? 0 : Fleet.EstimateTravelDays(leader, here, s.Body, jd);
                if (days < bestDays)
                {
                    bestDays = days;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Whether the ship's group carries marines to board with.</summary>
        public bool HasMarines(Ship ship)
        {
            var leader = ship.Controller;
            if (!leader.Destroyed && leader.Combat.MarinesAvailable > 0) return true;
            foreach (var e in Fleet.Ships)
                if (e.Leader == leader && !e.Destroyed && e.Combat.MarinesAvailable > 0) return true;
            return false;
        }

        /// <summary>
        /// A haven is open to be boarded or destroyed by <paramref name="ship"/>: still standing, hostile,
        /// its garrison platforms all gone, no fight going on there and no raiders parked over it.
        /// </summary>
        public bool CanAssault(Settlement st, Ship ship) =>
            st != null && ship != null && !ship.Destroyed && st.Kind == SettlementKind.Haven && !st.Fallen && st.Platforms.Count == 0 &&
            BattleAt(st.Body) == null && !PiratesAt(st.Body) && Diplomacy.Hostile(ship.Faction, st.Owner);

        /// <summary>
        /// The player's (or anyone's) assault on a haven whose defences are broken: board and take it
        /// (needs marines in the group; the stolen goods are seized) or destroy it (needs guns or
        /// marines). Either way it is out of action, and its raiders lie low, until the pirates rebuild
        /// it. Returns false if it can't be done.
        /// </summary>
        public bool AssaultHaven(Settlement st, Ship ship, bool capture, double jd)
        {
            if (!CanAssault(st, ship)) return false;
            if (capture ? !HasMarines(ship) : !ship.Controller.Combat.Armed && !HasMarines(ship)) return false;
            FallHaven(st, ship.Faction, capture, jd);
            return true;
        }

        void FallHaven(Settlement st, Faction by, bool capture, double jd)
        {
            double value = 0;
            for (int i = 0; i < GoodInfo.Count; i++)
            {
                value += Math.Max(0, st.Stock[i]) * GoodInfo.Of((Good)i).BasePrice;
                st.Stock[i] = 0;
            }
            if (capture) by.Treasury += value * 0.6;
            st.Platforms.Clear();
            st.Population = 0;
            st.Fallen = true;
            st.RebuildJd = jd + HavenRebuildDays;
            if (st.Hidden) st.KnownTo.Add(by);
            assaultDays.Remove(st);
            string where = $"{st.Name} ({Catalog[st.Body].Name})";
            world.Log(jd, capture
                ? $"The {by.Name} board and take {where}, seizing {value / 1e6:N0} M of stolen goods; its raiders scatter"
                : $"The {by.Name} destroy {where}; its raiders scatter", WorldEventKind.Capture, st.Body);
            BumpDanger(st.Body, 1);
        }

        /// <summary>
        /// Navies storm the havens they have found: once the garrison's platforms are gone and no raiders
        /// hold the rock, an assaulting group boards it (if it has marines) or levels it after
        /// <see cref="HavenAssaultDays"/>. Fallen havens rise again after <see cref="HavenRebuildDays"/>.
        /// </summary>
        void HavenAssaults(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind != SettlementKind.Haven) continue;
                if (st.Fallen)
                {
                    if (jd >= st.RebuildJd) RebuildHaven(st, jd);
                    continue;
                }
                Ship assailant = null;
                if (st.Platforms.Count == 0 && BattleAt(st.Body) == null && !PiratesAt(st.Body))
                    foreach (var s in Fleet.Ships)
                    {
                        if (s.Destroyed || s.IsEscort || !s.Spec.IsMilitary || s.Faction.IsPirate || s == world.PilotedShip) continue;
                        var c = s.Controller;
                        if (c.Phase != ShipPhase.Parked || c.Orbit.Body != st.Body || !Diplomacy.Hostile(s.Faction, st.Owner)) continue;
                        if (!Orders.TryGetValue(s, out var o) || o.Kind != OrderKind.Assault) continue;
                        if (assailant == null || GroupStrength(s) > GroupStrength(assailant)) assailant = s;
                    }
                if (assailant == null)
                {
                    assaultDays.Remove(st);
                    continue;
                }
                assaultDays.TryGetValue(st, out double days);
                days += 1;
                assaultDays[st] = days;
                if (days >= HavenAssaultDays) FallHaven(st, assailant.Faction, HasMarines(assailant), jd);
            }
        }

        void RebuildHaven(Settlement st, double jd)
        {
            st.Fallen = false;
            st.Population = st.InitialPopulation;
            while (st.Platforms.Count < st.MaxPlatforms) st.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            world.Log(jd, $"The {world.Pirates.Name} have rebuilt {st.Name} ({Catalog[st.Body].Name})", WorldEventKind.Info, st.Body);
        }

        /// <summary>Days an assaulting group has held a broken haven (see <see cref="HavenAssaultDays"/>).</summary>
        public double AssaultDays(Settlement s) => assaultDays.TryGetValue(s, out var d) ? d : 0;

        public bool AtWar(Faction f)
        {
            foreach (var g in Fleet.Factions)
                if (g != f && !g.IsPirate && Diplomacy.Get(f, g) == Relation.War) return true;
            return false;
        }

        bool EnemiesAt(int body, Faction f)
        {
            if (BattleAt(body) is Battle b && (b.SideLeaders[0] == f || b.SideLeaders[1] == f)) return true;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || !(s.Spec.IsMilitary || s.Faction.IsPirate)) continue;
                if (!Diplomacy.Hostile(f, s.Faction)) continue;
                var c = s.Controller;
                if (c.Phase == ShipPhase.Parked && c.Orbit.Body == body) return true;
            }
            return false;
        }

        bool PiratesAt(int body)
        {
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction.IsPirate && s.Phase == ShipPhase.Parked && s.Orbit.Body == body) return true;
            return false;
        }

        /// <summary>Strength defending a body against <paramref name="attacker"/>: hostile platforms and parked warships.</summary>
        public double DefenceAt(int body, Faction attacker)
        {
            double total = 0;
            foreach (var st in Economy.At(body))
                if (Diplomacy.Hostile(attacker, st.Owner))
                    foreach (var p in st.Platforms) total += p.Strength;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || !(s.Spec.IsMilitary || s.Faction.IsPirate) || !Diplomacy.Hostile(attacker, s.Faction)) continue;
                var c = s.Controller;
                if (c.Phase == ShipPhase.Parked && c.Orbit.Body == body) total += s.Combat.Strength;
            }
            return total;
        }

        public double GroupStrength(Ship leader)
        {
            double total = leader.Combat.Strength;
            foreach (var s in Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed) total += s.Combat.Strength;
            return total;
        }

        double GroupDamage(Ship leader)
        {
            double sum = leader.Combat.Damage;
            int n = 1;
            foreach (var s in Fleet.Ships)
                if (s.Leader == leader && !s.Destroyed)
                {
                    sum += s.Combat.Damage;
                    n++;
                }
            return sum / n;
        }

        /// <summary>A carrier that has lost half its fighters goes back to base to replace them.</summary>
        bool HangarsLow(Ship leader)
        {
            int have = 0, full = 0;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || (s != leader && s.Leader != leader)) continue;
                have += s.Combat.Fighters;
                full += s.Combat.Spec.Squadrons * 6;
            }
            return full > 0 && have < full * 0.5;
        }

        /// <summary>A group that has spent most of its marines on boarding goes back for more.</summary>
        bool MarinesLow(Ship leader)
        {
            double have = 0, full = 0;
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || (s != leader && s.Leader != leader)) continue;
                have += s.Combat.Interior.Marines;
                full += s.Combat.Spec.Marines;
            }
            return full > 0 && have < full * 0.35;
        }

        bool MissilesLow(Ship leader)
        {
            int have = 0, full = 0;
            foreach (var sub in leader.Combat.Subsystems)
                if (sub.Def.Weapon != null && sub.Def.Weapon.Missile && !sub.Destroyed)
                {
                    have += sub.Ammo;
                    full += sub.Def.Weapon.Ammo;
                }
            return full > 0 && have < full * 0.3;
        }

        // ---------------------------------------------------------------- daily

        void Daily(double jd)
        {
            foreach (var key in new List<int>(danger.Keys)) danger[key] *= Math.Exp(-1.0 / 20);
            foreach (var s in Economy.Settlements) Array.Clear(s.ExtraDemand, 0, s.ExtraDemand.Length);

            foreach (var s in Fleet.Ships)
                if (s.Derelict && !s.Destroyed && jd - s.DerelictSinceJd > 25) s.Destroyed = true;
            Wrecks.AdvanceTo(jd);
            if (Fleet.Lanes != null)
                foreach (var lane in new List<Lane>(Fleet.Lanes.Danger.Keys)) Fleet.Lanes.SetDanger(lane, Fleet.Lanes.Danger[lane] * 0.85);
            MergeGroups();
            Refit(jd);
            Sieges(jd);
            Scans(jd);
            HavenAssaults(jd);
            RebuildPlatforms(jd);
            PirateRecruiting(jd);
            DiplomacyCycle(jd);

            // Groups that met at a body without a fight (a war resumed, say) fight now, if they are fit to.
            foreach (var s in Fleet.Ships)
            {
                // Not the player's ship: its fights start only where the player engages (TryStartPlayerBattle).
                if (s.Destroyed || s.IsEscort || s.Battle != null || s.Phase != ShipPhase.Parked || s == world.PilotedShip) continue;
                if (!(s.Spec.IsMilitary || s.Faction.IsPirate) || !Willing(s, jd)) continue;
                int body = s.Orbit.Body;
                if (lastBattleEndJd.TryGetValue(body, out double ended) && jd - ended < 0.5) continue;
                if (BattleAt(body) == null) TryStartBattle(body, s.Faction, jd);
            }
        }

        /// <summary>
        /// Navies concentrate: groups of one faction parked at the same body (and not fighting) merge
        /// into the strongest of them, up to eight hulls, so single new warships become battle groups
        /// instead of patrolling alone. The home guard is never absorbed into or swollen.
        /// </summary>
        void MergeGroups()
        {
            var groups = new List<Ship>();
            foreach (var s in Fleet.Ships)
                // The player's ship is flown by the player: never handed to an AI flagship, nor one itself.
                if (!s.Destroyed && !s.IsEscort && s.Battle == null && s != world.PilotedShip && (s.Spec.IsMilitary || s.Faction.IsPirate) && s.Phase == ShipPhase.Parked)
                    groups.Add(s);
            groups.Sort((a, b) => GroupStrength(b).CompareTo(GroupStrength(a)));
            for (int i = 0; i < groups.Count; i++)
            {
                var big = groups[i];
                if (big.Destroyed || big.IsEscort || IsHomeGuard(big)) continue;
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var small = groups[j];
                    if (small.IsEscort || small.Faction != big.Faction || small.Orbit.Body != big.Orbit.Body || IsHomeGuard(small)) continue;
                    int size = 1, joining = 1;
                    foreach (var e in Fleet.Ships)
                    {
                        if (e.Destroyed) continue;
                        if (e.Leader == big) size++;
                        if (e.Leader == small) joining++;
                    }
                    if (size + joining > 8) continue;
                    var members = new List<Ship> { small };
                    foreach (var e in Fleet.Ships)
                        if (e.Leader == small && !e.Destroyed) members.Add(e);
                    foreach (var m in members)
                    {
                        Fleet.AddEscort(big, m, Fleet.SlotFor(size - 1));
                        size++;
                    }
                    Orders.Remove(small);
                    retreating.Remove(small);
                }
            }
        }

        /// <summary>Warships at a friendly yard repair and rearm, buying alloys, electronics and munitions from its market.</summary>
        void Refit(double jd)
        {
            foreach (var s in Fleet.Ships)
            {
                if (s.Destroyed || s.Battle != null || !(s.Spec.IsMilitary || s.Faction.IsPirate)) continue;
                var c = s.Controller;
                if (c.Phase != ShipPhase.Parked) continue;
                Settlement yard = null;
                foreach (var st in Economy.At(c.Orbit.Body))
                    if (st.Owner == s.Faction && (st.HasShipyard || st.Kind == SettlementKind.Haven)) yard = st;
                if (yard == null) continue;

                double scale = s.LengthKm;
                double damage = s.Combat.Damage;
                if (damage > 0.001)
                {
                    double share = Math.Min(0.15, damage);
                    double alloys = 900 * scale * share / 0.15, electronics = 60 * scale * share / 0.15;
                    double have = Math.Min(1, Math.Min(yard.Stock[(int)Good.Alloys] / Math.Max(alloys, 1e-9), yard.Stock[(int)Good.Electronics] / Math.Max(electronics, 1e-9)));
                    yard.ExtraDemand[(int)Good.Alloys] += alloys;
                    yard.ExtraDemand[(int)Good.Electronics] += electronics;
                    if (yard.Kind == SettlementKind.Haven) have = 0.5;
                    yard.Stock[(int)Good.Alloys] -= Math.Min(yard.Stock[(int)Good.Alloys], alloys * have);
                    yard.Stock[(int)Good.Electronics] -= Math.Min(yard.Stock[(int)Good.Electronics], electronics * have);
                    s.Faction.Treasury -= (alloys * yard.Price[(int)Good.Alloys] + electronics * yard.Price[(int)Good.Electronics]) * have;
                    if (have > 0) s.Combat.Repair(share * have);
                }
                int missing = 0;
                foreach (var sub in s.Combat.Subsystems)
                    if (sub.Def.Weapon != null && sub.Def.Weapon.Missile) missing += sub.Def.Weapon.Ammo - sub.Ammo;
                if (missing > 0)
                {
                    double tonnes = missing * 1.5;
                    yard.ExtraDemand[(int)Good.Munitions] += tonnes;
                    if (yard.Stock[(int)Good.Munitions] >= tonnes || yard.Kind == SettlementKind.Haven)
                    {
                        yard.Stock[(int)Good.Munitions] = Math.Max(0, yard.Stock[(int)Good.Munitions] - tonnes);
                        s.Faction.Treasury -= tonnes * yard.Price[(int)Good.Munitions];
                        foreach (var sub in s.Combat.Subsystems)
                            if (sub.Def.Weapon != null && sub.Def.Weapon.Missile) sub.Ammo = sub.Def.Weapon.Ammo;
                    }
                }
                // Marines lost boarding are replaced; the wounded go to the yard's hospitals.
                double marinesShort = s.Combat.Spec.Marines - s.Combat.Interior.Marines;
                if (marinesShort > s.Combat.Spec.Marines * 0.1 && !s.Combat.Interior.UnderBoarding)
                {
                    double medical = marinesShort * 0.05;
                    yard.ExtraDemand[(int)Good.MedicalSupplies] += medical;
                    yard.Stock[(int)Good.MedicalSupplies] = Math.Max(0, yard.Stock[(int)Good.MedicalSupplies] - medical);
                    s.Faction.Treasury -= marinesShort * 40000;
                    s.Combat.Interior.Muster(s.Combat.Spec.Crew, s.Combat.Spec.Marines);
                }
                int fighters = s.Combat.Spec.Squadrons * 6 - s.Combat.Fighters;
                if (fighters > 0 && yard.Stock[(int)Good.Electronics] > 30)
                {
                    int add = Math.Min(fighters, 4);
                    yard.Stock[(int)Good.Electronics] -= 6 * add;
                    yard.Stock[(int)Good.Alloys] = Math.Max(0, yard.Stock[(int)Good.Alloys] - 40 * add);
                    s.Faction.Treasury -= add * 2.5e6;
                    s.Combat.RestoreFighters(add);
                }
            }
        }

        /// <summary>Days a captured port is garrisoned and cannot be taken again.</summary>
        public const double CaptureGraceDays = 90;

        /// <summary>Days of uncontested siege a port holds out: four, plus one per 200,000 people, up to twenty.</summary>
        public static double SiegeNeeded(Settlement s) => Math.Min(20, 4 + s.Population / 2e5);

        /// <summary>
        /// A port with no platforms left, blockaded by a besieging group with nobody defending it,
        /// changes hands after <see cref="SiegeNeeded"/> days. A fresh capture is garrisoned for
        /// <see cref="CaptureGraceDays"/>.
        /// </summary>
        void Sieges(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind == SettlementKind.Haven) continue;
                Faction besieger = null;
                bool defended = st.Platforms.Count > 0 || BattleAt(st.Body) != null || st.DaysSinceCapture < CaptureGraceDays;
                foreach (var s in Fleet.Ships)
                {
                    if (s.Destroyed || s.IsEscort || !s.Spec.IsMilitary || s.Faction.IsPirate) continue;
                    var c = s.Controller;
                    if (c.Phase != ShipPhase.Parked || c.Orbit.Body != st.Body) continue;
                    if (Diplomacy.Hostile(s.Faction, st.Owner) && Orders.TryGetValue(s, out var o) && o.Kind == OrderKind.Siege) besieger = s.Faction;
                    else if (s.Faction == st.Owner) defended = true;
                }
                if (besieger == null || defended)
                {
                    siegeDays.Remove(st);
                    continue;
                }
                siegeDays.TryGetValue(st, out double days);
                days += 1;
                siegeDays[st] = days;
                if (days >= SiegeNeeded(st))
                {
                    var old = st.Owner;
                    Economy.Capture(st, besieger);
                    siegeDays.Remove(st);
                    RevokeBookings();
                    world.Log(jd, $"{st.Name} ({Catalog[st.Body].Name}) falls to the {besieger.Name}, taken from the {old.Name}", WorldEventKind.Capture, st.Body);
                    BumpDanger(st.Body, 1);
                }
            }
        }

        /// <summary>Traders bound for a port that is now closed to them (it changed hands, or a war resumed) look elsewhere.</summary>
        void RevokeBookings()
        {
            foreach (var t in Fleet.Ships)
            {
                if (t.Destroyed || t.CargoDestination == null || Economy.CanTrade(t, t.CargoDestination)) continue;
                Economy.Release(t);
                t.CargoDestination = null;
                t.Cargo = Economy.CargoText(t);
            }
        }

        public double SiegeDays(Settlement s) => siegeDays.TryGetValue(s, out var d) ? d : 0;

        void RebuildPlatforms(double jd)
        {
            foreach (var st in Economy.Settlements)
            {
                if (st.Fallen) continue;
                // Not mid-fight: a daily refit would refill shields and ammunition (and end a boarding) in battle.
                if (BattleAt(st.Body) != null) continue;
                foreach (var p in st.Platforms)
                    if (p.Damage > 0) { p.Repair(0.05); p.FreeResupply(); }
                if (st.Platforms.Count >= st.MaxPlatforms) continue;
                // A new platform every couple of weeks while the port can afford the steel.
                if (rng.NextDouble() > 0.07) continue;
                const double alloys = 3000, electronics = 200;
                st.ExtraDemand[(int)Good.Alloys] += alloys / 14;
                st.ExtraDemand[(int)Good.Electronics] += electronics / 14;
                if (st.Stock[(int)Good.Alloys] < alloys || st.Stock[(int)Good.Electronics] < electronics) continue;
                st.Stock[(int)Good.Alloys] -= alloys;
                st.Stock[(int)Good.Electronics] -= electronics;
                st.Owner.Treasury -= 3e7;
                st.Platforms.Add(new ShipCombat(CombatSpec.Platform));
            }
        }

        static readonly string[] RaiderNames =
        {
            "Gallows Bird", "Red Meridian", "Widow's Due", "Cutthroat Margin", "No Quarter", "Bad Debt", "Scavenger", "Hollow Crown",
            "Ill Wind", "Knife Edge", "Grudge", "Salvage Rights", "Last Laugh", "Black Ledger",
        };

        int raidersRaised;

        public void RaisePirateGroup(double jd, int body)
        {
            var pirates = world.Pirates;
            var names = world.RaiderNames ?? RaiderNames;
            string Name()
            {
                int n = raidersRaised++;
                return names[n % names.Length] + (n >= names.Length ? " " + (n / names.Length + 1) : "");
            }
            var leader = Fleet.AddShip(Name(), raidersRaised % 3 == 0 ? ShipClass.Cruiser : ShipClass.Destroyer, pirates, ShipRole.Patrol);
            leader.Route = new[] { body };
            int escorts = 1 + rng.Next(2);
            for (int i = 0; i < escorts; i++)
            {
                var e = Fleet.AddShip(Name(), ShipClass.Destroyer, pirates, ShipRole.Escort);
                Fleet.AddEscort(leader, e, Fleet.SlotFor(i));
            }
            // Every raider band keeps a breacher for taking prizes.
            var breacher = Fleet.AddShip(Name(), ShipClass.Breacher, pirates, ShipRole.Escort);
            Fleet.AddEscort(leader, breacher, Fleet.SlotFor(escorts));
            Fleet.Station(leader, body, jd);
            world.NotifyAdded(leader);
        }

        void PirateRecruiting(double jd)
        {
            if (FirstLiveHaven() == null) return;
            int groups = 0;
            var based = new Dictionary<Settlement, int>();
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && !s.IsEscort && s.Faction.IsPirate)
                {
                    groups++;
                    if (HomeHaven(s) is Settlement h) based[h] = (based.TryGetValue(h, out int n) ? n : 0) + 1;
                }
            // Smuggling and protection rackets keep the Banner afloat between prizes.
            world.Pirates.Treasury += world.PirateIncomePerDay;
            if (groups >= world.PirateGroupCap || world.Pirates.Treasury < 5e7 || rng.NextDouble() > 0.08) return;
            // The new band sails from the standing haven with the fewest of its own.
            Settlement from = null;
            int fewest = int.MaxValue;
            foreach (var st in Economy.Settlements)
            {
                if (st.Kind != SettlementKind.Haven || st.Fallen) continue;
                int n = based.TryGetValue(st, out int k) ? k : 0;
                if (n < fewest)
                {
                    fewest = n;
                    from = st;
                }
            }
            world.Pirates.Treasury -= 4e7;
            RaisePirateGroup(jd, from.Body);
            world.Log(jd, $"A new {world.Pirates.Name} raider group sails from {(from.Discovered ? from.Name : "somewhere in the dark")}", WorldEventKind.Info, from.Discovered ? from.Body : -1);
        }

        /// <summary>Wars wind down to ceasefires when both sides have bled, and flare up again months later.</summary>
        void DiplomacyCycle(double jd)
        {
            var a = world.RivalA;
            var b = world.RivalB;
            if (a == null || b == null) return;
            var r = Diplomacy.Get(a, b);
            if (r == Relation.War)
            {
                if (jd - warStartJd > 240 && Losses(a) > 2 && Losses(b) > 2 && rng.NextDouble() < 0.012)
                {
                    Diplomacy.Set(a, b, Relation.Ceasefire);
                    ceasefireUntilJd = jd + 90 + rng.NextDouble() * 120;
                    world.Log(jd, $"Ceasefire between the {a.Name} and the {b.Name}", WorldEventKind.Diplomacy);
                }
            }
            else if (r == Relation.Ceasefire && jd > ceasefireUntilJd)
            {
                Diplomacy.Set(a, b, Relation.War);
                RevokeBookings();
                warStartJd = jd;
                losses[a] = 0;
                losses[b] = 0;
                world.Log(jd, $"The ceasefire collapses: the {a.Name} and the {b.Name} are at war again", WorldEventKind.Diplomacy);
            }
        }

        // ---------------------------------------------------------------- shipbuilding

        static readonly ShipClass[] WarshipCycle =
        {
            ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Destroyer, ShipClass.Battleship, ShipClass.Destroyer, ShipClass.Cruiser, ShipClass.Carrier,
        };

        /// <summary>What a navy's yard lays down next, or null to leave it to the merchant fleet.</summary>
        public ShipClass? ChooseWarship(Settlement yard)
        {
            var f = yard.Owner;
            if (!f.IsMilitary) return null;
            int count = 0;
            foreach (var s in Fleet.Ships)
                if (!s.Destroyed && s.Faction == f && s.Spec.IsMilitary) count++;
            int cap = AtWar(f) ? world.NavyCapAtWar : world.NavyCap;
            if (count >= cap) return null;
            return WarshipCycle[(builtWarships + yard.Id) % WarshipCycle.Length];
        }

        static readonly string[] HullNames =
        {
            "Valiant", "Bastion", "Sentinel", "Corvus", "Tenacity", "Ardent", "Harrier", "Rampart", "Vigil", "Onslaught",
            "Meridian", "Paladin", "Warden", "Sabre", "Gauntlet", "Thunderer", "Steadfast", "Lodestar", "Zenith", "Implacable",
        };

        public Ship LaunchWarship(Settlement yard, ShipClass cls, double jd)
        {
            builtWarships++;
            string name = HullNames[builtWarships % HullNames.Length] + (builtWarships >= HullNames.Length ? " " + (builtWarships / HullNames.Length + 1) : "");
            // Reinforce an under-strength group refitting here, or form a new one.
            Ship group = null;
            foreach (var s in Fleet.Ships)
            {
                // Never the player's ship: escorts would follow a schedule the player isn't flying.
                if (s.Destroyed || s.IsEscort || s.Faction != yard.Owner || !s.Spec.IsMilitary || s.Battle != null || s == world.PilotedShip) continue;
                if (s.Phase != ShipPhase.Parked || s.Orbit.Body != yard.Body) continue;
                int escorts = 0;
                foreach (var e in Fleet.Ships)
                    if (e.Leader == s && !e.Destroyed) escorts++;
                if (escorts < 5 && s.Combat.Spec.Hull >= CombatSpec.Of(cls).Hull) group = s;
            }
            Ship ship;
            if (group != null)
            {
                ship = Fleet.AddShip(name, cls, yard.Owner, ShipRole.Escort);
                int index = 0;
                foreach (var e in Fleet.Ships)
                    if (e.Leader == group) index++;
                Fleet.AddEscort(group, ship, Fleet.SlotFor(index));
            }
            else
            {
                ship = Fleet.AddShip(name, cls, yard.Owner, ShipRole.Patrol);
                var own = new List<int>();
                foreach (var s in Economy.Settlements)
                    if (s.Owner == yard.Owner && !own.Contains(s.Body)) own.Add(s.Body);
                ship.Route = own.ToArray();
                world.Launch(ship, yard, jd);
            }
            world.Log(jd, $"{yard.Name} commissions the {cls.ToString().ToLowerInvariant()} {ship.Name}" + (group != null ? $", joining {group.Name}" : ""), WorldEventKind.Launch, yard.Body);
            return ship;
        }

        // ---------------------------------------------------------------- helpers

        void BumpDanger(int body, double amount)
        {
            danger.TryGetValue(body, out double d);
            danger[body] = d + amount;
        }

        string Describe(Battle b, int side)
        {
            var f = b.SideLeaders[side];
            if (f == null) return "nobody";
            Ship flag = null;
            int count = 0;
            foreach (var u in b.Units)
            {
                if (u.Side != side || u.Kind == UnitKind.Fighter) continue;
                count++;
                if (u.Ship != null && (flag == null || u.Ship.Combat.Spec.Hull > flag.Combat.Spec.Hull)) flag = u.Ship;
            }
            var owner = flag?.Faction ?? f;
            string who = owner.IsPirate ? owner.Name + " raiders" : owner.Name;
            return flag != null ? $"{who} ({flag.Name}, {count} ships)" : $"{who} defences";
        }

        static string Short(Battle b, int side)
        {
            var f = b.SideLeaders[side];
            if (f != null && f.IsPirate) return "pirates";
            return f?.Prefix is string p && p.Length > 0 ? p : side == 0 ? "attackers" : "defenders";
        }

        /// <summary>The ordinal a repeat battle is named with: Second, Third, then 4th, ... 21st, 22nd, 23rd, 111th.</summary>
        public static string Ordinal(int n)
        {
            if (n == 2) return "Second";
            if (n == 3) return "Third";
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suffix;
        }
    }
}
