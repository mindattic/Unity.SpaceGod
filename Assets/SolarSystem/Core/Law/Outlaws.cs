using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolarSystem.Core
{
    /// <summary>What became of an outlaw captain.</summary>
    public enum OutlawFate
    {
        /// <summary>Still out there, still raiding.</summary>
        AtLarge,

        /// <summary>Died with their flagship.</summary>
        Killed,

        /// <summary>Taken alive with their flagship (boarded, or it struck its colours to boarders).</summary>
        Captured,

        /// <summary>Gone without a fight anyone saw: the hull lost outside a battle (a collision, a scuttling).</summary>
        Vanished,
    }

    /// <summary>Where a wanted party was last seen: the body (or -1 when nobody can say which), when, by whom, in words.</summary>
    public sealed class Sighting
    {
        public int Body = -1;
        public double Jd;
        public string By;

        /// <summary>"at Mars", "bound for Ceres", "on the Earth-Mars lane", "somewhere in the Belt".</summary>
        public string Where;

        public double DaysAgo(double jd) => Math.Max(0, jd - Jd);
    }

    /// <summary>
    /// A named pirate captain the law has on its books for as long as they live: the flagship they command, the
    /// haven they sail from and a criminal record built from what their band actually did in the sim -- ships
    /// robbed, ships and platforms destroyed, cargoes ransomed -- on top of the crimes the law recorded
    /// (<see cref="LawRecord"/>). One who survives long enough at large becomes a feared name.
    /// </summary>
    public sealed class Outlaw
    {
        public readonly LawRecord Record;

        /// <summary>The flagship they command (the group leader the law's record is keyed by).</summary>
        public readonly Ship Ship;

        /// <summary>The haven they sail from, or null.</summary>
        public readonly Settlement Haven;

        /// <summary>When the law first had them on its books (their band sailed, or they took it over).</summary>
        public readonly double SinceJd;

        /// <summary>What the record is built from: lawful hulls robbed (ransomed, boarded and taken), hulls and
        /// platforms destroyed, cargoes ransomed, fights fought, and recorded crimes (all of them; the record's
        /// own list keeps only the latest).</summary>
        public int ShipsRobbed, Kills, Ransoms, Battles, CrimeCount;

        public OutlawFate Fate = OutlawFate.AtLarge;
        public double FateJd;

        /// <summary>Who ended their career ("the bounty hunter MV Nemesis", "the Sol Federation"), or null.</summary>
        public string FateBy;

        /// <summary>A feared name: survived <see cref="Outlaws.FearedDays"/> at large with a long record.</summary>
        public bool Feared;
        public double FearedJd;

        public Outlaw(LawRecord record, Ship ship, Settlement haven, double sinceJd)
        {
            Record = record;
            Ship = ship;
            Haven = haven;
            SinceJd = sinceJd;
        }

        public string Name => Record.Name;

        /// <summary>The name as the boards and the news give it: "the feared Captain ..." once it is.</summary>
        public string Title => Feared ? "the feared " + Name : Name;

        public bool AtLarge => Fate == OutlawFate.AtLarge;

        public double DaysAtLarge(double jd) => Math.Max(0, (AtLarge ? jd : FateJd) - SinceJd);

        /// <summary>The record in a line: "3 ships robbed, 2 destroyed, 1 cargo ransomed".</summary>
        public string Deeds()
        {
            var parts = new List<string>();
            if (ShipsRobbed > 0) parts.Add(ShipsRobbed + (ShipsRobbed == 1 ? " ship" : " ships") + " robbed");
            if (Kills > 0) parts.Add(Kills + " destroyed");
            if (Ransoms > 0) parts.Add(Ransoms + (Ransoms == 1 ? " cargo" : " cargoes") + " ransomed");
            if (parts.Count == 0) parts.Add(CrimeCount > 0 ? CrimeCount + (CrimeCount == 1 ? " crime" : " crimes") + " on record" : "no crimes yet on record");
            return string.Join(", ", parts);
        }

        public override string ToString() => Title;
    }

    /// <summary>A bounty hunter's group (a flagship and its escort) and what it has earned.</summary>
    public sealed class BountyHunter
    {
        public readonly Ship Ship;
        public readonly double SinceJd;

        /// <summary>The contract it has taken, or null.</summary>
        public BountyContract Contract;

        /// <summary>Bounties it has collected, credits, and how many.</summary>
        public double Collected;
        public int Takes;

        public BountyHunter(Ship ship, double sinceJd)
        {
            Ship = ship;
            Faction = ship.Faction;
            SinceJd = sinceJd;
        }

        public string Name => Ship.Name;

        /// <summary>Handed on to another flagship (its own lost, or taken over by the player).</summary>
        public bool Retired;

        /// <summary>Still hunting: not handed on, lost, cast adrift, gone through a gate or taken (flying other colours).</summary>
        public bool Alive => !Retired && !Ship.Destroyed && !Ship.Derelict && !Ship.GateDeparted && Ship.Faction == Faction;

        /// <summary>The colours it was commissioned under (the free traders').</summary>
        public readonly Faction Faction;
    }

    /// <summary>A contract a hunter has taken from the boards: the party it is after, and how long it keeps at it.</summary>
    public sealed class BountyContract
    {
        public LawRecord Target;
        public double TakenJd, UntilJd;
    }

    /// <summary>One line on a station's bounty board.</summary>
    public sealed class BountyPosting
    {
        public LawRecord Target;

        /// <summary>The outlaw it is on, or null (the player, or a lawful captain wanted for an assault).</summary>
        public Outlaw Outlaw;

        /// <summary>Who posts it here, and how much; and the price on the target's head across every board.</summary>
        public Faction PostedBy;
        public double Bounty, TotalBounty;

        /// <summary>Where the target was last seen, or null if nobody has.</summary>
        public Sighting LastSeen;

        /// <summary>The hunter that has taken a contract on the target, or null.</summary>
        public BountyHunter HuntedBy;

        public bool IsPlayer => Target.IsPlayer;
    }

    /// <summary>
    /// Named outlaws, the bounty boards and the hunters who work them (U.SPC-T62). Every raider band's captain is
    /// on the law's books from the day the band sails: a name, a flagship and a home haven, and a criminal record
    /// built from what the band actually does -- every lawful hull it robs, destroys or ransoms raises the price on
    /// the captain's head on top of the crime itself (<see cref="Law.Commit"/>). Each lawful port's bounty board
    /// lists everyone its owner has posted a bounty on, with where they were last seen (by a witness: a ship or
    /// station in sensor range; never a haven nobody has found). Bounty hunters -- independent warships the
    /// free traders commission while there is money on the boards -- take contracts on outlaws they can match
    /// (and on the player, when wanted), fly to the target's last-known position, fight it there and collect every
    /// bounty on it. A captain who survives <see cref="FearedDays"/> at large with a long record becomes a feared
    /// name: their bounties rise by half, and crews facing them strike their colours sooner.
    /// </summary>
    public sealed class Outlaws
    {
        readonly World world;
        readonly Random rng;

        /// <summary>Every outlaw ever on the books, oldest first (the dead and taken too: a persistent rogues' gallery).</summary>
        public readonly List<Outlaw> All = new List<Outlaw>();

        /// <summary>Every hunter group commissioned, oldest first.</summary>
        public readonly List<BountyHunter> Hunters = new List<BountyHunter>();

        /// <summary>Where each wanted party (the player included) was last seen.</summary>
        public readonly Dictionary<LawRecord, Sighting> LastSeen = new Dictionary<LawRecord, Sighting>();

        readonly Dictionary<Ship, Outlaw> byShip = new Dictionary<Ship, Outlaw>();
        readonly Dictionary<LawRecord, Outlaw> byRecord = new Dictionary<LawRecord, Outlaw>();
        readonly Dictionary<Ship, BountyHunter> byHunter = new Dictionary<Ship, BountyHunter>();

        // ---------------------------------------------------------------- tuning

        /// <summary>What each deed adds to the bounty a recorded crime posts, credits: a lawful hull or platform
        /// destroyed, a hull robbed (ransomed or taken), and a cargo ransomed on top of that.</summary>
        public const double KillBounty = 40000, RobbedBounty = 30000, RansomBounty = 20000;

        /// <summary>Days at large, and the record (recorded crimes, or kills), that make a feared name.</summary>
        public const double FearedDays = 120;
        public const int FearedCrimes = 4, FearedKills = 6;

        /// <summary>How a feared name's bounties grow (once when it becomes feared, and on every crime after).</summary>
        public const double FearedBountyFactor = 1.5;

        /// <summary>How much of their resolve crews facing a feared name keep (see <see cref="Battle.Dread"/>).</summary>
        public const double FearedResolve = 0.7;

        /// <summary>The smallest price on a head a hunter takes a contract for, credits.</summary>
        public const double MinContract = 100000;

        /// <summary>How long a hunter keeps at a contract, days, and how stale a sighting it will still follow.</summary>
        public const double ContractDays = 60, StaleSightingDays = 30;

        /// <summary>Of its target's strength a hunter group must match to take a contract on it (all of it for a feared name).</summary>
        public const double MatchShare = 0.7;

        /// <summary>How close a hunter comes to the player's wanted ship before it moves on it, km.</summary>
        public const double EngageKm = 250000;

        /// <summary>Chance a day the free traders commission a hunter while a bounty goes unhunted.</summary>
        public const double CommissionChance = 0.05;

        /// <summary>What the free traders keep in their treasury whatever the boards offer: a hunter is fitted out only from above it, credits.</summary>
        public const double GuildReserve = 3e8;

        /// <summary>A hunter this hurt goes to a yard first.</summary>
        public const double RepairDamage = 0.3;

        static readonly string[] HunterNames =
        {
            "Nemesis", "Writ of Seizure", "Long Arm", "Due Process", "Warrant", "Reckoning", "Bail Bond", "Quarry",
            "Manhunter", "Summons", "Last Word", "Price on Your Head",
        };

        double nextDailyJd = double.NaN;
        int hunterNames;

        /// <summary>The player's ship as last flown: where it is (for sightings and hunters), while it is.</summary>
        int pilotBody = -1;
        DVec3 pilotHelio;
        double pilotJd = double.NaN;

        public Outlaws(World world, int seed)
        {
            this.world = world;
            rng = new Random(seed);
        }

        Law Law => world.Law;

        /// <summary>Who pays and crews the hunters: the system's free traders.</summary>
        Faction Guild => world.Independents;

        // ---------------------------------------------------------------- lookups

        /// <summary>The outlaw commanding <paramref name="ship"/> (its group leader), or null.</summary>
        public Outlaw Of(Ship ship) => ship != null && byShip.TryGetValue(ship.Controller, out var o) ? o : null;

        /// <summary>The outlaw a record belongs to, or null.</summary>
        public Outlaw Of(LawRecord r) => r != null && byRecord.TryGetValue(r, out var o) ? o : null;

        /// <summary>The hunter group <paramref name="ship"/> flies in (it leads it or escorts its leader), or null.
        /// A hunter taken as a prize (pressed into pirate service, say) is a hunter no more.</summary>
        public BountyHunter HunterOf(Ship ship) =>
            ship != null && byHunter.TryGetValue(ship.Controller, out var h) && !h.Retired && h.Ship.Faction == Guild ? h : null;

        /// <summary>Whether <paramref name="ship"/> leads or flies in a hunter group.</summary>
        public bool IsHunter(Ship ship) => HunterOf(ship) != null;

        public Sighting SeenLast(LawRecord r) => r != null && LastSeen.TryGetValue(r, out var s) ? s : null;

        // ---------------------------------------------------------------- the clock

        /// <summary>Once a day: new captains on the books, fates settled, sightings, feared names, hunters' contracts and commissions.</summary>
        public void Update(double jd)
        {
            if (double.IsNaN(nextDailyJd))
            {
                Sync(jd);
                nextDailyJd = jd + 1;
                return;
            }
            while (jd >= nextDailyJd)
            {
                Daily(nextDailyJd);
                nextDailyJd += 1;
            }
        }

        void Daily(double jd)
        {
            Sync(jd);
            SettleFates(jd);
            Sightings(jd);
            Fear(jd);
            ReviewContracts(jd);
            Commission(jd);
            RepairHunters();
        }

        /// <summary>Every raider band's captain goes on the books: its flagship, its haven, its record.</summary>
        public void Sync(double jd)
        {
            if (world.War == null || Law == null) return;
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.IsEscort || s.Derelict || s.GateDeparted || s.Faction == null || !s.Faction.IsPirate || s == world.PilotedShip) continue;
                if (byShip.ContainsKey(s)) continue;
                var o = new Outlaw(Law.RecordOf(s), s, world.War.HomeHaven(s), jd);
                All.Add(o);
                byShip[s] = o;
                byRecord[o.Record] = o;
            }
        }

        /// <summary>Outlaws whose flagship is gone outside any battle (lost in a collision, scuttled) or flies under other colours.</summary>
        void SettleFates(double jd)
        {
            foreach (var o in All)
            {
                if (!o.AtLarge) continue;
                if (o.Ship.Faction == null || !o.Ship.Faction.IsPirate) End(o, OutlawFate.Captured, jd, o.Ship.Faction?.Name);
                else if (o.Ship.Destroyed || o.Ship.Derelict) End(o, OutlawFate.Vanished, jd, null);
            }
        }

        void End(Outlaw o, OutlawFate fate, double jd, string by)
        {
            if (!o.AtLarge) return;
            o.Fate = fate;
            o.FateJd = jd;
            o.FateBy = by;
            string what = fate == OutlawFate.Killed ? "is dead" : fate == OutlawFate.Captured ? "is taken alive" : "has vanished";
            world.Log(jd, $"{Cap(o.Title)} {what}" + (by != null ? " (" + by + ")" : "") + $" after {o.DaysAtLarge(jd):0} days at large: {o.Deeds()}",
                WorldEventKind.Law, -1);
        }

        // ---------------------------------------------------------------- sightings

        /// <summary>
        /// Every outlaw at large (and the player's wanted ship) that a lawful ship or station can see is sighted: the
        /// boards and the hunters learn where. Over a haven nobody has found, the sighting names no rock.
        /// </summary>
        void Sightings(double jd)
        {
            foreach (var o in All)
            {
                if (!o.AtLarge || o.Ship.Battle != null) continue;
                var pos = world.Fleet.StateOf(o.Ship, jd).HelioKm;
                string witness = Law.FindWitness(pos, jd);
                if (witness == null) continue;
                Sight(o.Record, Locate(o.Ship), jd, witness);
            }
            var p = Law.Player;
            if (p.Wanted && world.PilotedShip != null && pilotBody >= 0 && !double.IsNaN(pilotJd) && jd - pilotJd < 1)
            {
                string witness = Law.FindWitness(pilotHelio, jd);
                if (witness != null) Sight(p, (pilotBody, "near " + BodyName(pilotBody)), jd, witness);
            }
        }

        /// <summary>Where a ship is, as a board would put it: the body it is at or bound for, or the lane it rides.</summary>
        (int body, string where) Locate(Ship s)
        {
            int body;
            string where;
            switch (s.Phase)
            {
                case ShipPhase.Transit:
                    body = s.DestinationBody;
                    where = "bound for " + BodyName(body);
                    break;
                case ShipPhase.Lane:
                case ShipPhase.Lurk:
                case ShipPhase.Adrift:
                    body = s.LaneLeg.From;
                    where = (s.Phase == ShipPhase.Lane ? "riding the " : "lurking off the ") + s.LaneLeg.Lane.Name + " lane";
                    break;
                default:
                    body = s.Orbit?.Body ?? -1;
                    where = "at " + BodyName(body);
                    break;
            }
            return Hide(body, where);
        }

        /// <summary>A sighting at a haven nobody has found names no rock.</summary>
        (int body, string where) Hide(int body, string where)
        {
            if (body < 0 || body >= world.Catalog.Count) return (-1, where);
            foreach (var st in world.Economy.At(body))
                if (!st.Discovered) return (-1, world.Catalog[body].Kind == BodyKind.Asteroid ? "somewhere in the Belt" : "somewhere in the dark");
            return (body, where);
        }

        void Sight(LawRecord r, (int body, string where) at, double jd, string by)
        {
            if (!LastSeen.TryGetValue(r, out var s)) LastSeen[r] = s = new Sighting();
            s.Body = at.body;
            s.Where = at.where;
            s.Jd = jd;
            s.By = by;
        }

        string BodyName(int body) => body >= 0 && body < world.Catalog.Count ? world.Catalog[body].Name : "deep space";

        // ---------------------------------------------------------------- crimes and battles

        /// <summary>
        /// The law has booked a crime (<see cref="Law.Committed"/>): a recorded one goes on an outlaw's record, places
        /// the offender where it was done, and costs a feared name half again its bounty.
        /// </summary>
        public void OnCrime(Crime c)
        {
            if (c == null || !c.Recorded) return;
            var o = Of(c.Offender);
            if (o != null)
            {
                o.CrimeCount++;
                if (o.Feared) AddBounty(c.Offender, c, c.Bounty * (FearedBountyFactor - 1));
            }
            var (body, where) = Hide(c.Body, c.Body >= 0 ? "near " + BodyName(c.Body) : c.Detail != null ? "at the " + c.Detail : "somewhere unseen");
            Sight(c.Offender, (body, where), c.Jd, c.Witness);
        }

        static void AddBounty(LawRecord r, Crime c, double extra)
        {
            if (extra <= 0 || c.PostedBy == null) return;
            r.Bounty[c.PostedBy] = r.BountyBy(c.PostedBy) + extra;
            r.BountyUntil[c.PostedBy] = c.Jd + Law.BountyDays;
            c.Bounty += extra;
        }

        /// <summary>
        /// Before a battle starts in earnest: crews facing a feared name lose some of their nerve
        /// (<see cref="Battle.Dread"/>).
        /// </summary>
        public void OnBattleStarted(Battle b)
        {
            if (b == null) return;
            for (int side = 0; side < 2; side++)
            {
                foreach (var u in b.Units)
                {
                    if (u.Side != 1 - side || u.Ship == null) continue;
                    var o = Of(u.Ship);
                    if (o != null && o.AtLarge && o.Feared)
                    {
                        b.Dread[side] = FearedResolve;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A battle has ended and the law has judged it (<see cref="Law.OnBattleEnded"/>): each outlaw who fought in it
        /// adds what their band did to lawful ships to their record -- hulls robbed, destroyed, cargoes ransomed -- and,
        /// where the law recorded the crime, each deed raises the bounty it posted. An outlaw whose flagship died or
        /// was taken is finished. A fight on a lane places them by its near end.
        /// </summary>
        public void OnBattleEnded(Battle b, double jd)
        {
            if (b == null || b.Units.Count == 0) return;
            var seen = new HashSet<Outlaw>();
            foreach (var u in b.Units)
            {
                if (u.Ship == null || u.Side < 0 || u.Side > 1) continue;
                var o = Of(u.Ship);
                if (o == null || !o.AtLarge || !seen.Add(o)) continue;
                int side = u.Side;
                int kills = 0, robbed = 0, ransoms = 0;
                foreach (var v in b.Units)
                {
                    if (v.Side != 1 - side || v.Kind == UnitKind.Fighter || v.Faction == null || v.Faction.IsPirate) continue;
                    if (v.Dead) kills++;
                    else if (v.Captured || v.SurrenderOutcome == SurrenderTerms.TakeShip) robbed++;
                    else if (v.SurrenderOutcome == SurrenderTerms.RansomCargo)
                    {
                        robbed++;
                        ransoms++;
                    }
                }
                o.Battles++;
                o.Kills += kills;
                o.ShipsRobbed += robbed;
                o.Ransoms += ransoms;
                // The crime the law booked for this fight, if anyone saw it: the deeds go on its bounty.
                var rec = o.Record;
                Crime crime = rec.Crimes.Count > 0 ? rec.Crimes[rec.Crimes.Count - 1] : null;
                if (crime != null && crime.Jd == jd && crime.Detail == b.Name && crime.Recorded)
                {
                    double extra = kills * KillBounty + robbed * RobbedBounty + ransoms * RansomBounty;
                    if (o.Feared) extra *= FearedBountyFactor;
                    if (extra > 0)
                    {
                        AddBounty(rec, crime, extra);
                        world.Log(jd, $"{crime.PostedBy.Name} raises the price on {o.Title} to {Cr(rec.BountyBy(crime.PostedBy))} cr: {Deeds(kills, robbed, ransoms)} at {b.Name}",
                            WorldEventKind.Law, -1);
                    }
                    if (b.Site != null) Sight(rec, Hide(b.Site.Hop.From, "on the " + b.Site.Hop.Lane.Name + " lane"), jd, crime.Witness);
                }
            }
            // Outlaws whose flagship went down or was taken in it are finished.
            foreach (var u in b.Units)
            {
                if (u.Ship == null || !(u.Dead || u.Captured)) continue;
                if (!byShip.TryGetValue(u.Ship, out var o) || !o.AtLarge) continue;
                End(o, u.Dead ? OutlawFate.Killed : OutlawFate.Captured, jd, Victor(b, 1 - u.Side));
            }
        }

        static string Deeds(int kills, int robbed, int ransoms)
        {
            var parts = new List<string>();
            if (kills > 0) parts.Add(kills + (kills == 1 ? " ship destroyed" : " ships destroyed"));
            if (robbed > 0) parts.Add(robbed + (robbed == 1 ? " ship robbed" : " ships robbed"));
            if (ransoms > 0) parts.Add(ransoms + (ransoms == 1 ? " cargo ransomed" : " cargoes ransomed"));
            return string.Join(", ", parts);
        }

        /// <summary>Who won a fight on a side, as the news gives it: a hunter by name, else the side's faction.</summary>
        string Victor(Battle b, int side)
        {
            foreach (var u in b.Units)
                if (u.Side == side && u.Ship != null && IsHunter(u.Ship)) return "the bounty hunter " + HunterOf(u.Ship).Name;
            return side >= 0 && side < 2 ? b.SideLeaders[side]?.Name : null;
        }

        /// <summary>
        /// A wanted party (<paramref name="rec"/>, commanding <paramref name="u"/>) has been killed or taken in a battle:
        /// if bounty hunters fought on the other side, they collect every bounty on it, each poster paying its own
        /// out of its treasury. True if they did (the bounties are then cleared and the law has nothing more to do).
        /// </summary>
        public bool ClaimByHunter(Battle b, BattleUnit u, LawRecord rec, double jd)
        {
            if (b == null || u == null || rec == null || !rec.Wanted) return false;
            BountyHunter hunter = null;
            foreach (var o in b.Units)
                if (o.Side == 1 - u.Side && o.Ship != null && HunterOf(o.Ship) is BountyHunter h && (hunter == null || !o.Dead)) hunter = h;
            if (hunter == null) return false;
            double total = 0;
            foreach (var kv in new List<KeyValuePair<Faction, double>>(rec.Bounty))
            {
                kv.Key.Treasury -= kv.Value;
                total += kv.Value;
                Law.ClearBounty(rec, kv.Key);
            }
            hunter.Collected += total;
            hunter.Takes++;
            hunter.Ship.Credits += total;
            if (Guild != null) Guild.Treasury += total;
            if (hunter.Contract?.Target == rec) hunter.Contract = null;
            var outlaw = Of(rec);
            string name = outlaw != null ? outlaw.Title : rec.IsPlayer ? "the " + u.Ship?.Name : rec.Name;
            world.Log(jd, $"The bounty hunter {hunter.Name} collects {Cr(total)} cr on {name}, {(u.Dead ? "dead" : "taken")} at {b.Name}", WorldEventKind.Law, -1);
            if (rec.IsPlayer) Law.Notice($"The bounty hunter {hunter.Name} has collected the {Cr(total)} cr on your head");
            return true;
        }

        // ---------------------------------------------------------------- feared names

        void Fear(double jd)
        {
            foreach (var o in All)
            {
                if (!o.AtLarge || o.Feared || o.DaysAtLarge(jd) < FearedDays) continue;
                if (o.CrimeCount < FearedCrimes && o.Kills < FearedKills) continue;
                o.Feared = true;
                o.FearedJd = jd;
                o.Record.Notoriety += 25;
                foreach (var f in new List<Faction>(o.Record.Bounty.Keys)) o.Record.Bounty[f] *= FearedBountyFactor;
                world.Log(jd, $"{o.Name} has run free for {o.DaysAtLarge(jd):0} days ({o.Deeds()}): the name is feared on every lane, and the price on it rises to {Cr(o.Record.TotalBounty)} cr",
                    WorldEventKind.Law, -1);
            }
        }

        // ---------------------------------------------------------------- the boards

        /// <summary>
        /// The bounty board at <paramref name="port"/>: everyone its owner has a bounty posted on (outlaws still at
        /// large, the player, a lawful captain wanted for an assault), the biggest first, with where each was last
        /// seen and who is hunting them. A haven keeps no board.
        /// </summary>
        public List<BountyPosting> BoardAt(Settlement port)
        {
            var list = new List<BountyPosting>();
            if (port == null || port.Owner == null || port.Owner.IsPirate || port.Fallen || Law == null) return list;
            foreach (var p in Postings())
                if (p.Target.WantedBy(port.Owner))
                {
                    p.PostedBy = port.Owner;
                    p.Bounty = p.Target.BountyBy(port.Owner);
                    list.Add(p);
                }
            list.Sort((a, b) => b.Bounty.CompareTo(a.Bounty));
            return list;
        }

        /// <summary>Every bounty on the boards system-wide, one posting per target (its total price), the biggest first.</summary>
        public List<BountyPosting> Postings()
        {
            var list = new List<BountyPosting>();
            if (Law == null) return list;
            void Add(LawRecord r)
            {
                if (!r.Wanted) return;
                var o = Of(r);
                if (o != null && !o.AtLarge) return;
                if (!r.IsPlayer && (r.Captain == null || r.Captain.Destroyed)) return;
                list.Add(new BountyPosting { Target = r, Outlaw = o, Bounty = r.TotalBounty, TotalBounty = r.TotalBounty, LastSeen = SeenLast(r), HuntedBy = HunterOn(r) });
            }
            Add(Law.Player);
            foreach (var r in Law.Captains.Values) Add(r);
            list.Sort((a, b) => b.TotalBounty.CompareTo(a.TotalBounty));
            return list;
        }

        /// <summary>The hunter with a contract on <paramref name="r"/>, or null.</summary>
        public BountyHunter HunterOn(LawRecord r)
        {
            foreach (var h in Hunters)
                if (h.Alive && h.Contract?.Target == r) return h;
            return null;
        }

        /// <summary>A board line in words: who, how much, what they did, where last seen, and who is after them.</summary>
        public static string Describe(BountyPosting p, double jd)
        {
            var o = p.Outlaw;
            string who = o != null ? o.Title : p.IsPlayer ? "YOU" : p.Target.Name;
            string text = who + "  ·  " + Cr(p.Bounty) + " cr";
            if (o != null) text += "  ·  " + o.Deeds() + ", " + o.DaysAtLarge(jd).ToString("0", CultureInfo.InvariantCulture) + " d at large";
            else text += "  ·  " + p.Target.Crimes.Count + (p.Target.Crimes.Count == 1 ? " crime" : " crimes") + " on file";
            var s = p.LastSeen;
            text += s != null ? "  ·  last seen " + s.Where + ", " + Ago(s.DaysAgo(jd)) + (s.By != null ? " by " + s.By : "") : "  ·  not seen";
            if (p.HuntedBy != null) text += "  ·  hunted by the " + p.HuntedBy.Name;
            return text;
        }

        static string Ago(double days) =>
            days < 1 ? "today" : days < 2 ? "yesterday" : days.ToString("0", CultureInfo.InvariantCulture) + " days ago";

        // ---------------------------------------------------------------- hunters

        /// <summary>
        /// While a bounty worth hunting goes unhunted and there are fewer hunter groups than <see cref="World.HunterCap"/>,
        /// the free traders now and then commission one at the biggest lawful shipyard that will dock them: a cruiser
        /// and a destroyer, paid for out of their treasury above <see cref="GuildReserve"/> (their crews sign on in port).
        /// </summary>
        void Commission(double jd)
        {
            if (Guild == null || world.HunterCap <= 0) return;
            int alive = 0;
            foreach (var h in Hunters) if (h.Alive) alive++;
            if (alive >= world.HunterCap) return;
            bool unhunted = false;
            foreach (var p in Postings())
                if (p.TotalBounty >= MinContract && p.HuntedBy == null) unhunted = true;
            if (!unhunted) return;
            double cost = Economy.HullPrice(ShipClass.Cruiser) + Economy.HullPrice(ShipClass.Destroyer);
            if (Guild.Treasury < cost + GuildReserve || rng.NextDouble() > CommissionChance) return;
            Settlement yard = null;
            foreach (var st in world.Economy.Settlements)
            {
                if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                if (yard == null || st.Population > yard.Population) yard = st;
            }
            if (yard == null) return;
            Guild.Treasury -= cost;
            var hunter = Commission(yard, jd);
            world.Log(jd, $"{yard.Name} fits out the bounty hunter {hunter.Name} for the {Guild.Name}: there is money on the boards", WorldEventKind.Launch, yard.Body);
        }

        /// <summary>Puts a new hunter group into service out of <paramref name="yard"/> (a cruiser with a destroyer escort).</summary>
        public BountyHunter Commission(Settlement yard, double jd)
        {
            var fleet = world.Fleet;
            string Next()
            {
                int n = hunterNames++;
                return HunterNames[n % HunterNames.Length] + (n >= HunterNames.Length ? " " + (n / HunterNames.Length + 1) : "");
            }
            var lead = fleet.AddShip(Next(), ShipClass.Cruiser, Guild, ShipRole.Patrol);
            lead.Route = world.PortBodies();
            var escort = fleet.AddShip(Next(), ShipClass.Destroyer, Guild, ShipRole.Escort);
            fleet.AddEscort(lead, escort, Fleet.SlotFor(0));
            // Out into a parking orbit of its own (not the yard's slip, where a new merchant may be sitting).
            fleet.Station(lead, yard.Body, jd);
            var h = new BountyHunter(lead, jd);
            Hunters.Add(h);
            byHunter[lead] = h;
            world.NotifyAdded(lead);
            return h;
        }

        /// <summary>A hunter flagship lost: its escort carries on as the group (see <see cref="War"/>'s succession).</summary>
        public void OnSuccession(Ship dead, Ship heir)
        {
            if (dead == null || heir == null || !byHunter.TryGetValue(dead, out var h)) return;
            var next = new BountyHunter(heir, h.SinceJd) { Contract = h.Contract, Collected = h.Collected, Takes = h.Takes };
            byHunter.Remove(dead);
            Hunters.Add(next);
            byHunter[heir] = next;
            h.Contract = null;
            h.Retired = true;
        }

        /// <summary>Contracts lapse when the target is no longer wanted, taken, dead or long lost.</summary>
        void ReviewContracts(double jd)
        {
            foreach (var h in Hunters)
            {
                var c = h.Contract;
                if (c == null) continue;
                if (!h.Alive || !Huntable(c.Target) || jd > c.UntilJd) h.Contract = null;
            }
        }

        bool Huntable(LawRecord r)
        {
            if (r == null || !r.Wanted || r.TotalBounty < MinContract * 0.5) return false;
            if (r.IsPlayer) return world.PilotedShip != null && !world.PilotedShip.Destroyed;
            var o = Of(r);
            return o != null && o.AtLarge && !o.Ship.Destroyed;
        }

        /// <summary>Hunters idle at a lawful shipyard are patched up there, at the free traders' expense.</summary>
        void RepairHunters()
        {
            foreach (var s in world.Fleet.Ships)
            {
                if (s.Destroyed || s.Battle != null || !IsHunter(s) || s.Combat.Damage <= 0.001) continue;
                var c = s.Controller;
                if (c.Phase != ShipPhase.Parked) continue;
                foreach (var st in world.Economy.At(c.Orbit.Body))
                {
                    if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                    double share = Math.Min(0.2, s.Combat.Damage);
                    s.Combat.Repair(share);
                    double bill = Economy.HullPrice(s.Spec.Class) * share * 0.3;
                    Guild.Treasury -= bill;
                    st.Owner.Treasury += bill;
                    break;
                }
            }
        }

        /// <summary>
        /// Fleet hook (through <see cref="War.ChooseNext"/>): where a hunter group goes next. Hurt, to the nearest
        /// lawful yard. On a contract, to where its target was last seen (and it waits there for it). Without one,
        /// it takes the best it can match from the boards -- the most money for the fewest days away -- or, with
        /// nothing worth taking, makes the round of the ports.
        /// </summary>
        public int ChooseNext(Ship leader, int here, double jd)
        {
            var h = HunterOf(leader);
            if (h == null) return -1;
            if (leader.Combat.Damage > RepairDamage || !leader.Combat.Armed)
            {
                int yard = NearestYard(here, jd);
                if (yard >= 0) return yard;
            }
            if (h.Contract != null && (!Huntable(h.Contract.Target) || Outmatched(h, SeenLast(h.Contract.Target)))) h.Contract = null;
            h.Contract ??= TakeContract(h, here, jd);
            if (h.Contract != null)
            {
                var seen = SeenLast(h.Contract.Target);
                if (seen != null && seen.Body >= 0) return seen.Body;
            }
            var ports = leader.Route;
            if (ports == null || ports.Length == 0) return here;
            return ports[rng.Next(ports.Length)];
        }

        /// <summary>Whether what would fight the hunter where its target was seen (the target's band, a haven's guns,
        /// other raiders there) is now more than the hunter group can take on: it drops the contract.</summary>
        bool Outmatched(BountyHunter h, Sighting seen) =>
            seen != null && seen.Body >= 0 && world.War.DefenceAt(seen.Body, h.Ship.Faction) > world.War.GroupStrength(h.Ship) * 1.2;

        /// <summary>The contract a hunter takes from the boards, or null: a target worth <see cref="MinContract"/>, seen
        /// lately somewhere it can go, nobody else is after, and that it can match (<see cref="MatchShare"/>).</summary>
        public BountyContract TakeContract(BountyHunter h, int here, double jd)
        {
            BountyContract best = null;
            double bestValue = 0;
            double strength = world.War.GroupStrength(h.Ship);
            foreach (var p in Postings())
            {
                if (p.TotalBounty < MinContract || (p.HuntedBy != null && p.HuntedBy != h) || !Huntable(p.Target)) continue;
                var seen = p.LastSeen;
                if (seen == null || seen.Body < 0 || jd - seen.Jd > StaleSightingDays) continue;
                if (p.Outlaw != null)
                {
                    // Its band, and whatever else would fight it where it was seen (a haven's guns, other raiders).
                    double need = world.War.GroupStrength(p.Outlaw.Ship) * (p.Outlaw.Feared ? 1 : MatchShare);
                    need = Math.Max(need, world.War.DefenceAt(seen.Body, h.Ship.Faction) * (p.Outlaw.Feared ? 1 : MatchShare));
                    if (strength < need) continue;
                }
                double days = seen.Body == here ? 0 : world.Fleet.EstimateTravelDays(h.Ship, here, seen.Body, jd);
                double value = p.TotalBounty / (days + 3);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = new BountyContract { Target = p.Target, TakenJd = jd, UntilJd = jd + ContractDays };
                }
            }
            if (best != null)
            {
                var o = Of(best.Target);
                string who = o != null ? o.Title : best.Target.IsPlayer ? "the player's ship" : best.Target.Name;
                world.Log(jd, $"The bounty hunter {h.Name} takes the contract on {who} ({Cr(best.Target.TotalBounty)} cr)", WorldEventKind.Law, -1);
                if (best.Target.IsPlayer) Law.Notice($"The bounty hunter {h.Name} has taken the contract on you: {Cr(best.Target.TotalBounty)} cr on your head");
            }
            return best;
        }

        int NearestYard(int here, double jd)
        {
            int best = -1;
            double bestKm = double.MaxValue;
            foreach (var st in world.Economy.Settlements)
            {
                if (!st.HasShipyard || st.Fallen || st.Owner == null || st.Owner.IsPirate || !world.Diplomacy.CanDock(Guild, st.Owner)) continue;
                double km = st.Body == here ? 0 : (world.Ephemeris.HelioAt(st.Body, jd) - world.Ephemeris.HelioAt(here, jd)).Length;
                if (km < bestKm)
                {
                    bestKm = km;
                    best = st.Body;
                }
            }
            return best;
        }

        /// <summary>
        /// Advances the hunt around the player's ship, flown at <paramref name="offsetKm"/> from <paramref name="body"/>:
        /// where it is (for the next sighting), and a hunter with a contract on it that comes within
        /// <see cref="EngageKm"/> moves on it (<see cref="War.TryStartHunt"/>). Returns that battle, or null.
        /// </summary>
        public Battle UpdatePilot(Ship ship, int body, DVec3 offsetKm, DVec3 forward, double jd)
        {
            if (ship == null || ship.Destroyed || world.War == null) return null;
            pilotBody = body;
            pilotHelio = world.Ephemeris.HelioAt(body, jd) + offsetKm;
            pilotJd = jd;
            if (ship.Battle != null || !Law.Player.Wanted) return null;
            foreach (var h in Hunters)
            {
                if (!h.Alive || h.Contract?.Target != Law.Player || h.Ship.Battle != null || h.Ship == ship) continue;
                if ((world.Fleet.StateOf(h.Ship, jd).HelioKm - pilotHelio).Length > EngageKm) continue;
                var b = world.War.TryStartHunt(ship, h.Ship, body, offsetKm, forward, jd);
                if (b == null) continue;
                Law.Notice($"Bounty hunter {h.Name}: \"{ship.Name}, there's {Cr(Law.Player.TotalBounty)} on your head. Nothing personal.\"");
                return b;
            }
            return null;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Cr(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }
}
