using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>What an FTL jump under a ship's own power can be pointed at (U.SPC-T417).</summary>
    public enum FtlTargetKind
    {
        /// <summary>Another star: a running system, or a plain star on the charts (built into a system when a pilot sets out for it).</summary>
        Star,

        /// <summary>A planet, moon, station or star of the system the ship is in.</summary>
        Body,

        /// <summary>A fixed point in the system the ship is in: a jump gate, or any beacon registered with <see cref="FtlTargets.Beacons"/>.</summary>
        Beacon,
    }

    /// <summary>
    /// A place an FTL jump can be pointed at (U.SPC-T417): a star, a planet (any body) or a beacon, where it is, and how
    /// far short of it the drive drops out so the ship comes out beside it rather than in it.
    /// </summary>
    public sealed class FtlTarget
    {
        public FtlTargetKind Kind { get; }
        public string Name { get; }

        /// <summary>A body's or beacon's system; for a star, the running system it is (null for a plain star on the charts).</summary>
        public StarSystem System { get; }

        /// <summary>For a star: where it is in the galaxy.</summary>
        public GalacticPosition StarOrigin { get; }

        /// <summary>For a body: its index in its system's catalogue.</summary>
        public int Body { get; } = -1;

        /// <summary>For a beacon: where it is, km from its system's root.</summary>
        public DVec3 BeaconKm { get; }

        /// <summary>How far short of it the drive drops out, km (a star's is the edge of its well, see <see cref="CruiseDrives.WellLimitKm"/>).</summary>
        public double ClearanceKm { get; }

        FtlTarget(FtlTargetKind kind, string name, StarSystem system, GalacticPosition origin, int body, DVec3 beaconKm, double clearanceKm)
        {
            Kind = kind;
            Name = name;
            System = system;
            StarOrigin = origin;
            Body = body;
            BeaconKm = beaconKm;
            ClearanceKm = Math.Max(0, clearanceKm);
        }

        /// <summary>A running system's star.</summary>
        public static FtlTarget OfStar(StarSystem system) =>
            new FtlTarget(FtlTargetKind.Star, system.Name, system, system.Origin, -1, DVec3.Zero, CruiseDrives.WellLimitKm(system));

        /// <summary>A plain star on the charts, one nobody simulates yet.</summary>
        public static FtlTarget OfStar(NearbyStar star) =>
            new FtlTarget(FtlTargetKind.Star, star.Name, null, star.Origin, -1, DVec3.Zero, CruiseDrives.WellLimitKm(null));

        /// <summary>A body of <paramref name="system"/>: the drive drops out <see cref="HelmFtl.BodyClearanceRadii"/> of its radii out.</summary>
        public static FtlTarget OfBody(StarSystem system, int body)
        {
            var b = system.Catalog[body];
            return new FtlTarget(FtlTargetKind.Body, b.Name, system, default, body, DVec3.Zero,
                Math.Max(b.RadiusKm * HelmFtl.BodyClearanceRadii, HelmFtl.MinBodyClearanceKm));
        }

        /// <summary>A beacon: a fixed point <paramref name="km"/> from <paramref name="system"/>'s root, dropped out at <paramref name="clearanceKm"/> short of.</summary>
        public static FtlTarget OfBeacon(StarSystem system, string name, DVec3 km, double clearanceKm) =>
            new FtlTarget(FtlTargetKind.Beacon, name, system, default, -1, km, clearanceKm);

        /// <summary>Whether a jump from <paramref name="here"/> to it leaves the system (a star other than its own).</summary>
        public bool Interstellar(StarSystem here) => Kind == FtlTargetKind.Star && System != here;

        /// <summary>Where it is at <paramref name="jd"/>, km from <paramref name="here"/>'s root (a body moves on its orbit).</summary>
        public DVec3 LocalKmAt(StarSystem here, double jd)
        {
            switch (Kind)
            {
                case FtlTargetKind.Body when System == here: return here.Ephemeris.HelioAt(Body, jd);
                case FtlTargetKind.Beacon when System == here: return BeaconKm;
                case FtlTargetKind.Star: return here.LocalKm(StarOrigin);
                default: return here.LocalKm(System.At(Kind == FtlTargetKind.Body ? System.Ephemeris.HelioAt(Body, jd) : BeaconKm));
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// What the helm can point its FTL drive at from a system (U.SPC-T417): the stars (running systems and the charts),
    /// the system's own bodies, its jump gates and any registered beacons. A new kind of target plugs in by adding a source
    /// to <see cref="Sources"/>, and a new beacon by adding it to <see cref="Beacons"/>, never by editing the helm.
    /// </summary>
    public static class FtlTargets
    {
        /// <summary>A source of targets: (the galaxy, the system the ship is in) to what it offers.</summary>
        public delegate IEnumerable<FtlTarget> Source(Galaxy galaxy, StarSystem here);

        /// <summary>A beacon somebody has set out: in which system (by name), what it is called, and where, km from the root.</summary>
        public readonly struct BeaconSpec
        {
            public readonly string System, Name;
            public readonly DVec3 Km;
            public readonly double ClearanceKm;

            public BeaconSpec(string system, string name, DVec3 km, double clearanceKm = HelmFtl.BeaconClearanceKm)
            {
                System = system;
                Name = name;
                Km = km;
                ClearanceKm = clearanceKm;
            }
        }

        /// <summary>Beacons set out by name (the extension point for navigation beacons, survey markers and the like).</summary>
        public static readonly List<BeaconSpec> Beacons = new List<BeaconSpec>();

        /// <summary>Where targets come from, in order.</summary>
        public static readonly List<Source> Sources = new List<Source> { Stars, Bodies, Gates, Registered };

        /// <summary>Every target offered from <paramref name="here"/>.</summary>
        public static List<FtlTarget> All(Galaxy galaxy, StarSystem here)
        {
            var list = new List<FtlTarget>();
            foreach (var s in Sources)
                foreach (var t in s(galaxy, here))
                    if (t != null) list.Add(t);
            return list;
        }

        /// <summary>The other running systems' stars, and the plain stars on the charts.</summary>
        public static IEnumerable<FtlTarget> Stars(Galaxy galaxy, StarSystem here)
        {
            if (galaxy == null) yield break;
            foreach (var s in galaxy.Systems)
                if (s != here) yield return FtlTarget.OfStar(s);
            foreach (var n in galaxy.NearbyStars)
                if (n.Name != null) yield return FtlTarget.OfStar(n);
        }

        /// <summary>Every body of the system that can be seen (not an empty barycentre).</summary>
        public static IEnumerable<FtlTarget> Bodies(Galaxy galaxy, StarSystem here)
        {
            for (int i = 0; i < here.Catalog.Count; i++)
                if (here.Catalog[i].IsVisible) yield return FtlTarget.OfBody(here, i);
        }

        /// <summary>The system's jump gates, as beacons: the drive drops out within reach of opening one.</summary>
        public static IEnumerable<FtlTarget> Gates(Galaxy galaxy, StarSystem here)
        {
            foreach (var g in here.Gates)
                yield return FtlTarget.OfBeacon(here, g.Name, g.PositionKm, JumpGate.ActivationRangeKm * 0.5);
        }

        /// <summary>The <see cref="Beacons"/> set out in this system.</summary>
        public static IEnumerable<FtlTarget> Registered(Galaxy galaxy, StarSystem here)
        {
            foreach (var b in Beacons)
                if (string.Equals(b.System, here.Name, StringComparison.OrdinalIgnoreCase))
                    yield return FtlTarget.OfBeacon(here, b.Name, b.Km, b.ClearanceKm);
        }
    }

    /// <summary>Where the helm's FTL drive stands.</summary>
    public enum FtlStage
    {
        Idle,

        /// <summary>Charging, nose held on the target.</summary>
        Spooling,

        /// <summary>Under way to a target in the same system (a jump to another star is handed to <see cref="DeepSpace"/>).</summary>
        Engaged,
    }

    /// <summary>What a step of the helm's FTL drive came to.</summary>
    public enum FtlEvent
    {
        None,

        /// <summary>The charge was lost (the nose swung off, a fight, or it was called off): see <see cref="HelmFtl.AbortReason"/>.</summary>
        Aborted,

        /// <summary>Charged: the drive engages now (<see cref="HelmFtl.EngageInSystem"/> or <see cref="HelmFtl.EngageInterstellar"/>).</summary>
        Charged,

        /// <summary>Dropped out beside the target.</summary>
        Arrived,
    }

    /// <summary>
    /// The helm's own FTL drive, I-War 2 style (U.SPC-T417): a capital ship or a survey hull (anything carrying a
    /// <see cref="CruiseDriveSpec"/>) is pointed at a star, a planet or a beacon, spools its drive while the nose is held
    /// on it, engages, and some while later drops out beside it. A jump within the system runs closed-form along the line
    /// to the target by the drive's own speed profile; a jump to another star hands the hull to <see cref="DeepSpace"/>,
    /// which flies it across open space as it does every other group under its own drive, and drops it out at the edge of
    /// that star's well. Small craft carry no drive and are refused: the jump gates and lanes are their way.
    ///
    /// Engine-free and driven only by the sim clock, so the Runtime helm just feeds it the hull's place and heading.
    /// </summary>
    public sealed class HelmFtl
    {
        /// <summary>How near the nose must be to a target to pick it and start the charge, degrees.</summary>
        public const double AlignDeg = 3;

        /// <summary>How far the nose may wander from the target while charging before the charge is lost, degrees.</summary>
        public const double HoldDeg = 8;

        /// <summary>Nearer than this (to where it would drop out) and the drive is not worth spooling, km: fly it.</summary>
        public const double MinJumpKm = 50000;

        /// <summary>A body's drop-out, in its radii: outside the reach of its pull on a hull (<see cref="PilotFlight.GravityReachRadii"/>).</summary>
        public const double BodyClearanceRadii = 8;

        /// <summary>The least a body's drop-out stands off, km (a small moon or an asteroid).</summary>
        public const double MinBodyClearanceKm = 2000;

        /// <summary>A beacon's drop-out, km, unless it says otherwise.</summary>
        public const double BeaconClearanceKm = 200;

        public FtlStage Stage { get; private set; }
        public FtlTarget Target { get; private set; }
        public CruiseDriveSpec Drive { get; private set; }

        /// <summary>When the charge began and when it is done (the drive's <see cref="CruiseDriveSpec.SpoolDays"/>).</summary>
        public double SpoolStartJd { get; private set; } = double.NaN;
        public double ChargedJd { get; private set; } = double.NaN;

        /// <summary>Why the last charge was lost or the jump refused.</summary>
        public string AbortReason { get; private set; }

        /// <summary>A jump within the system: from where, to where it drops out (km from the root), when it engaged, and its speed through it.</summary>
        public DVec3 StartKm { get; private set; }
        public DVec3 EndKm { get; private set; }
        public double LegJd { get; private set; } = double.NaN;
        public CruiseProfile Profile { get; private set; }

        /// <summary>When a jump within the system drops out.</summary>
        public double ArriveJd => Stage == FtlStage.Engaged ? LegJd + Profile.FlightDays : double.NaN;

        /// <summary>The way a jump within the system heads (unit).</summary>
        public DVec3 Direction
        {
            get
            {
                var d = EndKm - StartKm;
                return d.LengthSquared > 0 ? d.Normalized : DVec3.UnitX;
            }
        }

        /// <summary>0..1 through the charge.</summary>
        public double SpoolFraction(double jd) =>
            Stage != FtlStage.Spooling ? 0 : ChargedJd <= SpoolStartJd ? 1 : Math.Max(0, Math.Min(1, (jd - SpoolStartJd) / (ChargedJd - SpoolStartJd)));

        /// <summary>Why <paramref name="ship"/> cannot jump under its own power at all, or null if it can.</summary>
        public static string Refusal(Ship ship)
        {
            if (ship == null || ship.Spec == null) return "No ship at the helm";
            if (ship.Destroyed || ship.Derelict) return "The " + ship.Name + " is in no state to jump";
            if (CruiseDrives.Of(ship) == null)
                return "The " + ship.Spec.Class.ToString().ToLowerInvariant() + " is too small to carry an FTL drive: the jump gates and lanes are its way";
            return null;
        }

        /// <summary>The way to <paramref name="target"/> from <paramref name="fromKm"/> in <paramref name="here"/> (unit), and how far, km.</summary>
        public static DVec3 Bearing(FtlTarget target, StarSystem here, DVec3 fromKm, double jd, out double km)
        {
            var d = target.LocalKmAt(here, jd) - fromKm;
            km = d.Length;
            return km > 0 ? d / km : DVec3.UnitX;
        }

        /// <summary>The angle between <paramref name="forward"/> and the way to <paramref name="target"/>, degrees.</summary>
        public static double OffNoseDeg(FtlTarget target, StarSystem here, DVec3 fromKm, DVec3 forward, double jd)
        {
            var dir = Bearing(target, here, fromKm, jd, out double km);
            if (km <= 0 || forward.LengthSquared <= 0) return 180;
            double cos = Math.Max(-1, Math.Min(1, DVec3.Dot(dir, forward.Normalized)));
            return Math.Acos(cos) * Astro.Rad2Deg;
        }

        /// <summary>
        /// The target the nose is on: the one nearest dead ahead within <paramref name="toleranceDeg"/> (of two as near, the
        /// nearer to the ship), and not one the ship is already beside. Null if none.
        /// </summary>
        public static FtlTarget Pointed(IEnumerable<FtlTarget> targets, StarSystem here, DVec3 fromKm, DVec3 forward, double jd, double toleranceDeg = AlignDeg)
        {
            FtlTarget best = null;
            double bestDeg = double.MaxValue, bestKm = double.MaxValue;
            foreach (var t in targets)
            {
                Bearing(t, here, fromKm, jd, out double km);
                if (km - t.ClearanceKm < MinJumpKm) continue;
                double deg = OffNoseDeg(t, here, fromKm, forward, jd);
                if (deg > toleranceDeg) continue;
                // Within a tenth of a degree of each other: a planet over its own moons (from far off they are one point
                // of light), otherwise the nearer.
                bool tie = Math.Abs(deg - bestDeg) <= 0.1;
                bool better = deg < bestDeg - 0.1 || (tie && (MoonOf(best, t) || (!MoonOf(t, best) && km < bestKm)));
                if (!better) continue;
                best = t;
                bestDeg = deg;
                bestKm = km;
            }
            return best;
        }

        /// <summary>Whether <paramref name="moon"/> is a body circling <paramref name="planet"/> (directly or further down).</summary>
        static bool MoonOf(FtlTarget moon, FtlTarget planet)
        {
            if (moon == null || planet == null || moon.Kind != FtlTargetKind.Body || planet.Kind != FtlTargetKind.Body || moon.System != planet.System) return false;
            var catalog = moon.System.Catalog;
            for (int p = catalog[moon.Body].ParentIndex; p >= 0; p = catalog[p].ParentIndex)
                if (p == planet.Body) return true;
            return false;
        }

        /// <summary>
        /// Starts the charge for <paramref name="target"/> at <paramref name="jd"/>: the ship must carry a drive, be out of
        /// any fight, have its nose on the target and be far enough from it. False (with <see cref="AbortReason"/>) if not.
        /// </summary>
        public bool Spool(Ship ship, StarSystem here, FtlTarget target, DVec3 fromKm, DVec3 forward, bool inCombat, double jd)
        {
            AbortReason = Refusal(ship);
            if (AbortReason == null && target == null) AbortReason = "Nothing ahead to jump to: point the nose at a star, a planet or a beacon";
            if (AbortReason == null && inCombat) AbortReason = "The drive will not spool in a fight";
            if (AbortReason == null && here == null) AbortReason = "Nowhere to jump from";
            if (AbortReason != null) return false;
            Bearing(target, here, fromKm, jd, out double km);
            if (km - target.ClearanceKm < MinJumpKm) AbortReason = target.Name + " is too near to need the drive: fly it";
            else if (OffNoseDeg(target, here, fromKm, forward, jd) > HoldDeg) AbortReason = "The nose is off " + target.Name + ": line it up first";
            if (AbortReason != null) return false;
            Target = target;
            Drive = CruiseDrives.Of(ship);
            Stage = FtlStage.Spooling;
            SpoolStartJd = jd;
            ChargedJd = jd + Drive.SpoolDays;
            return true;
        }

        /// <summary>
        /// A step of the drive at <paramref name="jd"/>, the hull at <paramref name="fromKm"/> heading <paramref name="forward"/>:
        /// a charge is lost if the nose wanders or a fight starts, and is done when its time is up; a jump within the system drops out when due.
        /// </summary>
        public FtlEvent Step(StarSystem here, DVec3 fromKm, DVec3 forward, bool inCombat, double jd)
        {
            switch (Stage)
            {
                case FtlStage.Spooling:
                    if (inCombat) return Abort("Charge lost: the drive will not hold in a fight");
                    if (OffNoseDeg(Target, here, fromKm, forward, jd) > HoldDeg) return Abort("Charge lost: the nose swung off " + Target.Name);
                    return jd >= ChargedJd ? FtlEvent.Charged : FtlEvent.None;
                case FtlStage.Engaged:
                    return jd >= ArriveJd ? FtlEvent.Arrived : FtlEvent.None;
                default:
                    return FtlEvent.None;
            }
        }

        /// <summary>Calls the charge (or the drive) off; the reason is kept in <see cref="AbortReason"/>.</summary>
        public FtlEvent Abort(string why)
        {
            AbortReason = why;
            Reset();
            return FtlEvent.Aborted;
        }

        /// <summary>Back to idle, whatever it was doing.</summary>
        public void Reset()
        {
            Stage = FtlStage.Idle;
            Target = null;
            Drive = null;
            SpoolStartJd = ChargedJd = LegJd = double.NaN;
        }

        /// <summary>
        /// Charged, for a target in the same system: under way along the line from <paramref name="fromKm"/>, to drop out
        /// <see cref="FtlTarget.ClearanceKm"/> short of where the target will be when it gets there.
        /// </summary>
        public void EngageInSystem(StarSystem here, DVec3 fromKm, double jd)
        {
            if (Stage != FtlStage.Spooling || Target == null) throw new InvalidOperationException("The drive is not charged");
            StartKm = fromKm;
            LegJd = jd;
            // Aimed where a moving body will be: the trip's length depends on where that is, so settle it in a few passes.
            double at = jd;
            for (int i = 0; i < 4; i++)
            {
                var target = Target.LocalKmAt(here, at);
                var way = target - fromKm;
                double km = way.Length;
                var dir = km > 0 ? way / km : DVec3.UnitX;
                EndKm = fromKm + dir * Math.Max(0, km - Target.ClearanceKm);
                // Already charged: no second charge in the profile.
                Profile = new CruiseProfile((EndKm - fromKm).Length / Astro.LightYearKm, Drive.LyPerDay, Drive.RampDays, 0);
                at = jd + Profile.FlightDays;
            }
            Stage = FtlStage.Engaged;
        }

        /// <summary>Where a jump within the system has the hull at <paramref name="jd"/>, km from the root.</summary>
        public DVec3 PositionKmAt(double jd)
        {
            if (Stage != FtlStage.Engaged) return StartKm;
            return StartKm + Direction * (Profile.LyAt(jd - LegJd) * Astro.LightYearKm);
        }

        /// <summary>A jump within the system's speed at <paramref name="jd"/>, km/s.</summary>
        public double SpeedKmS(double jd) =>
            Stage != FtlStage.Engaged ? 0 : Profile.SpeedAt(jd - LegJd) * Astro.LightYearKm / Astro.SecondsPerDay;

        /// <summary>0..1 along a jump within the system.</summary>
        public double Progress(double jd) =>
            Stage != FtlStage.Engaged || Profile.Ly <= 0 ? 0 : Math.Max(0, Math.Min(1, Profile.LyAt(jd - LegJd) / Profile.Ly));

        /// <summary>A jump within the system has dropped out: back to idle, the drive ready again.</summary>
        public void Arrived() => Reset();

        /// <summary>
        /// Charged, for another star: <paramref name="ship"/> (at <paramref name="fromKm"/> in <paramref name="here"/>) goes out
        /// into open space under its own drive (<see cref="DeepSpace"/>), already charged, bound for the edge of that star's
        /// well. A plain star on the charts is built into a running system first, so there is somewhere to arrive. The hull
        /// itself goes, the same object (<see cref="Fleet.LetGo"/> leaves a marker in its slot here), never a copy; the cruise is marked
        /// <see cref="Cruise.Piloted"/>, so nobody else gives it orders when it drops out. Null (with
        /// <see cref="AbortReason"/>) if there is nowhere to go.
        /// </summary>
        public Cruise EngageInterstellar(Galaxy galaxy, StarSystem here, Ship ship, DVec3 fromKm, double jd)
        {
            if (Stage != FtlStage.Spooling || Target == null) throw new InvalidOperationException("The drive is not charged");
            var to = Target.System ?? galaxy.Realize(Target.Name, jd);
            if (to == null || to == here)
            {
                Abort("Nothing charted at " + Target.Name + " to drop out at");
                return null;
            }
            var world = here.World;
            world.War?.Forget(ship);
            world.Economy?.Release(ship);
            // The hull itself goes, not a copy: its place in this fleet keeps a marker so no other id moves.
            if (world.Fleet.LetGo(ship) == null)
            {
                Abort("The " + ship.Name + " is not in " + here.Name + "'s fleet to jump from");
                return null;
            }
            ship.Role = ShipRole.Free;
            var news = galaxy.Traffic?.News.Bundle(here, jd);
            var c = galaxy.DeepSpace.Launch(new[] { ship }, here, fromKm, to, Drive, jd, news, charged: true);
            c.Piloted = true;
            world.Log(jd, $"{ship.Name} of {ship.Faction.Name} engages its {Drive.Name.ToLowerInvariant()} for {to.Name}, " +
                          $"{c.Profile.Ly.ToString("0.00", CultureInfo.InvariantCulture)} ly under its own power", WorldEventKind.Info, here.Catalog.Root.Index);
            Reset();
            return c;
        }
    }
}
