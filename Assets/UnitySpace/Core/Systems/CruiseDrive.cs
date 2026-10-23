using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A long-range cruise drive (U.SPC-T412): what lets a hull leave its system by its own power and cross the open
    /// space between the stars, rather than only by a jump gate. It charges (spools) at the edge of its system's
    /// gravity well, comes up to its cruise speed, holds it across the light-years, and comes down again to drop out
    /// at the edge of the far star's well, where the hull finishes the way in under its ordinary drive. Its speed is
    /// many times light's, but slow next to a gate: a gate is a shortcut, the drive is the way anywhere.
    ///
    /// A drive is data, not code: a new one is a new spec registered for the hull classes that carry it
    /// (<see cref="CruiseDrives.Register"/>).
    /// </summary>
    public sealed class CruiseDriveSpec
    {
        public string Name { get; }

        /// <summary>Top cruise speed, as a multiple of the speed of light (before the menu's <see cref="CruiseDrives.SpeedScale"/>).</summary>
        public double CruiseC { get; }

        /// <summary>How long it is rated to charge at the well's edge before it engages, days.</summary>
        public double RatedSpoolDays { get; }

        /// <summary>How long it charges before it engages, days: its rating times the menu's <see cref="FtlRules.SpoolScale"/>.</summary>
        public double SpoolDays => RatedSpoolDays * FtlRules.SpoolScale.Value;

        /// <summary>How long it takes to come up to cruise speed (and to come down from it), days.</summary>
        public double RampDays { get; }

        /// <summary>How long it is rated to cool after it drops out before it can charge again, days (U.SPC-T418).</summary>
        public double RatedCooldownDays { get; }

        /// <summary>How long it cools after it drops out, days: its rating times the menu's <see cref="FtlRules.CooldownScale"/>.</summary>
        public double CooldownDays => RatedCooldownDays * FtlRules.CooldownScale.Value;

        /// <summary>The share of a hull's tanks a charge burns, and the share each light-year flown burns (U.SPC-T418,
        /// before the menu's <see cref="FtlRules.FuelScale"/>; see <see cref="FtlRules.PropellantFor"/>).</summary>
        public double ChargePropellantShare { get; }
        public double PropellantSharePerLy { get; }

        public CruiseDriveSpec(string name, double cruiseC, double spoolDays, double rampDays, double cooldownDays = 0,
            double chargePropellantShare = 0, double propellantSharePerLy = 0)
        {
            if (!(cruiseC > 0)) throw new ArgumentOutOfRangeException(nameof(cruiseC), "A cruise drive must move: " + name);
            Name = name;
            CruiseC = cruiseC;
            RatedSpoolDays = Math.Max(0, spoolDays);
            RampDays = Math.Max(1e-6, rampDays);
            RatedCooldownDays = Math.Max(0, cooldownDays);
            ChargePropellantShare = Math.Max(0, chargePropellantShare);
            PropellantSharePerLy = Math.Max(0, propellantSharePerLy);
        }

        /// <summary>Light-years a day at cruise speed, at the menu's speed scale now.</summary>
        public double LyPerDay => CruiseC * CruiseDrives.SpeedScale.Value / (Astro.DaysPerCentury / 100);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A cruise's speed through one leg (U.SPC-T412): charging, coming up to speed, holding it, coming down. Worked out
    /// once when the leg is set and closed-form after that, so where a group is between the stars never depends on how
    /// often it is looked at.
    /// </summary>
    public readonly struct CruiseProfile
    {
        /// <summary>The leg's length, light-years; its speed at cruise, light-years a day; the ramp up and the charge, days.</summary>
        public readonly double Ly, LyPerDay, RampDays, SpoolDays;

        public CruiseProfile(double ly, double lyPerDay, double rampDays, double spoolDays)
        {
            Ly = Math.Max(0, ly);
            LyPerDay = Math.Max(1e-12, lyPerDay);
            RampDays = Math.Max(1e-9, rampDays);
            SpoolDays = Math.Max(0, spoolDays);
        }

        public static CruiseProfile For(CruiseDriveSpec drive, double ly) => new CruiseProfile(ly, drive.LyPerDay, drive.RampDays, drive.SpoolDays);

        double Accel => LyPerDay / RampDays;

        /// <summary>Whether the leg is too short to reach cruise speed (it comes straight down again from its peak).</summary>
        bool Short => Ly < LyPerDay * RampDays;

        /// <summary>Days under way once engaged (not counting the charge).</summary>
        public double FlightDays => Short ? 2 * Math.Sqrt(Ly / Accel) : RampDays + Ly / LyPerDay;

        /// <summary>Days from the start of the charge to dropping out.</summary>
        public double TotalDays => SpoolDays + FlightDays;

        /// <summary>Light-years covered <paramref name="days"/> after the charge began.</summary>
        public double LyAt(double days)
        {
            double t = days - SpoolDays;
            if (t <= 0) return 0;
            double total = FlightDays;
            if (t >= total) return Ly;
            double a = Accel;
            if (Short)
            {
                double half = total / 2;
                if (t <= half) return 0.5 * a * t * t;
                double r = total - t;
                return Ly - 0.5 * a * r * r;
            }
            if (t <= RampDays) return 0.5 * a * t * t;
            double coastEnd = total - RampDays;
            if (t <= coastEnd) return 0.5 * LyPerDay * RampDays + LyPerDay * (t - RampDays);
            double left = total - t;
            return Ly - 0.5 * a * left * left;
        }

        /// <summary>Speed <paramref name="days"/> after the charge began, light-years a day.</summary>
        public double SpeedAt(double days)
        {
            double t = days - SpoolDays;
            double total = FlightDays;
            if (t <= 0 || t >= total) return 0;
            double a = Accel;
            double peak = Short ? a * total / 2 : LyPerDay;
            return Math.Min(peak, Math.Min(a * t, a * (total - t)));
        }
    }

    /// <summary>
    /// Which hulls carry a cruise (FTL) drive, and the menu's FTL tuning (U.SPC-T412, U.SPC-T419). The capital classes
    /// (carrier, battleship, cruiser, destroyer) carry the heavy drive; the deep-space explorer, the hull the survey
    /// services send out past the gates, carries the lighter, faster survey drive. Everything else (fighters, couriers,
    /// breachers, freighters, haulers) cannot leave a system except by a gate, or docked aboard a capital ship that can
    /// (see <see cref="FtlCarriage"/>).
    /// </summary>
    [SettingsSource]
    public static class CruiseDrives
    {
        /// <summary>A capital ship's drive: a light-year a day, charging six hours and cooling twelve after, a charge burning
        /// 2% of the tanks and each light-year 0.2% (some 490 ly on full tanks).</summary>
        public static readonly CruiseDriveSpec Capital = new CruiseDriveSpec("Capital cruise drive", (Astro.DaysPerCentury / 100), 0.25, 0.1,
            cooldownDays: 0.5, chargePropellantShare: 0.02, propellantSharePerLy: 0.002);

        /// <summary>A survey hull's drive: two light-years a day, charging under three hours and cooling six after, a charge
        /// burning 1% of the tanks and each light-year 0.1% (some 990 ly on full tanks).</summary>
        public static readonly CruiseDriveSpec Survey = new CruiseDriveSpec("Survey cruise drive", 2 * (Astro.DaysPerCentury / 100), 0.1, 0.05,
            cooldownDays: 0.25, chargePropellantShare: 0.01, propellantSharePerLy: 0.001);

        static readonly Dictionary<ShipClass, CruiseDriveSpec> byClass = new Dictionary<ShipClass, CruiseDriveSpec>
        {
            [ShipClass.Carrier] = Capital,
            [ShipClass.Battleship] = Capital,
            [ShipClass.Cruiser] = Capital,
            [ShipClass.Destroyer] = Capital,
            // A destroyer-sized hull (U.SPC-T479): it crosses with its band, never left at the well's edge.
            [ShipClass.Interdictor] = Capital,
            // A battlecruiser fitted with a drive, like a battleship (U.SPC-T477).
            [ShipClass.CommandCruiser] = Capital,
            [ShipClass.Explorer] = Survey,
        };

        /// <summary>Gives every hull of <paramref name="cls"/> <paramref name="drive"/> (null takes it away): how a new drive or a new hull plugs in.</summary>
        public static void Register(ShipClass cls, CruiseDriveSpec drive)
        {
            if (drive == null) byClass.Remove(cls);
            else byClass[cls] = drive;
        }

        /// <summary>The drive hulls of <paramref name="cls"/> carry, or null.</summary>
        public static CruiseDriveSpec Of(ShipClass cls) => byClass.TryGetValue(cls, out var d) ? d : null;

        /// <summary>The drive <paramref name="ship"/> carries, or null if it cannot cross between the stars on its own.</summary>
        public static CruiseDriveSpec Of(Ship ship) => ship?.Spec == null ? null : Of(ship.Spec.Class);

        /// <summary>The settings menu's FTL tuning: every cruise drive's speed, as a multiple of its rated speed.</summary>
        public static readonly RangeSetting SpeedScale = new RangeSetting("ftl.cruiseSpeedScale", SettingGroups.Ftl,
            "Cruise drive top speed", 1, 0.25, 50, 0.25,
            "How fast every cruise drive crosses between the stars, as a multiple of its rated speed (a capital drive is rated at a light-year a day: " +
            "the galaxy's width in some 270 years; at the top of the range, in about five). Applies to trips set out on from now.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + "x", "ftl interstellar cruise speed drive");

        /// <summary>The settings menu's FTL tuning: how far out from a star a cruise drive can engage, and where it drops out.</summary>
        public static readonly RangeSetting WellLimit = new RangeSetting("ftl.wellLimitAu", SettingGroups.Ftl,
            "Gravity well limit", 2, 0.25, 20, 0.25,
            "How far from a star (AU) a cruise drive must be to engage, and where it drops out on arrival, the rest of the way flown under " +
            "the ordinary drive. Applies to trips set out on from now.",
            v => v.ToString("0.##", CultureInfo.InvariantCulture) + " AU", "ftl well drop out gravity");

        /// <summary>How far from <paramref name="system"/>'s root a cruise engages and drops out, km.</summary>
        public static double WellLimitKm(StarSystem system) =>
            Math.Max(WellLimit.Value * Astro.AuKm, system == null ? 0 : Galaxy.GateDistanceKm(system) * 4);
    }

    /// <summary>
    /// What a hull with an FTL drive can take with it docked aboard (U.SPC-T419): carriers and the other capital ships
    /// have hangar berths, and fighters and small craft that cannot cross between the stars on their own ride in them,
    /// so a fleet can strike or explore off the gate network with its wings. Data, not code: a hull's berths and a
    /// craft's size are registered (<see cref="RegisterBerths"/>, <see cref="RegisterCraft"/>), never threaded through
    /// the cruise.
    /// </summary>
    public static class FtlCarriage
    {
        /// <summary>Hangar berths each class of drive-carrying hull has for small craft.</summary>
        static readonly Dictionary<ShipClass, int> berths = new Dictionary<ShipClass, int>
        {
            [ShipClass.Carrier] = 12,
            [ShipClass.Battleship] = 4,
            [ShipClass.Cruiser] = 3,
            [ShipClass.Destroyer] = 2,
            [ShipClass.CommandCruiser] = 3,
        };

        /// <summary>How many berths a craft of each class takes up; a class not listed cannot be carried.</summary>
        static readonly Dictionary<ShipClass, int> sizes = new Dictionary<ShipClass, int>
        {
            [ShipClass.Fighter] = 1,
            [ShipClass.Interceptor] = 1,
            [ShipClass.Bomber] = 2,
            [ShipClass.Courier] = 2,
            [ShipClass.Breacher] = 2,
            [ShipClass.Gunship] = 2,
        };

        /// <summary>Gives hulls of <paramref name="cls"/> <paramref name="count"/> berths (0 takes them away).</summary>
        public static void RegisterBerths(ShipClass cls, int count)
        {
            if (count <= 0) berths.Remove(cls);
            else berths[cls] = count;
        }

        /// <summary>Lets craft of <paramref name="cls"/> be carried, taking <paramref name="size"/> berths (0 says it cannot be).</summary>
        public static void RegisterCraft(ShipClass cls, int size)
        {
            if (size <= 0) sizes.Remove(cls);
            else sizes[cls] = size;
        }

        /// <summary>Berths hulls of <paramref name="cls"/> have.</summary>
        public static int BerthsOf(ShipClass cls) => berths.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Berths <paramref name="ship"/> has to carry craft through FTL: none without a drive of its own to carry them on.</summary>
        public static int BerthsOf(Ship ship) =>
            ship?.Spec == null || CruiseDrives.Of(ship) == null ? 0 : BerthsOf(ship.Spec.Class);

        /// <summary>Berths a craft of <paramref name="cls"/> takes up, or 0 if it cannot be carried.</summary>
        public static int SizeOf(ShipClass cls) => sizes.TryGetValue(cls, out int n) ? n : 0;

        /// <summary>Whether <paramref name="craft"/> needs carrying (no drive of its own) and can be (it fits a berth).</summary>
        public static bool Carriable(Ship craft) =>
            craft?.Spec != null && !craft.Destroyed && !craft.Derelict && CruiseDrives.Of(craft) == null && SizeOf(craft.Spec.Class) > 0;

        /// <summary>
        /// Docks as many of <paramref name="craft"/> as fit aboard <paramref name="carriers"/> (the hulls going under
        /// their own drives), each in the first with berths to spare, in the order given: what goes aboard which. Craft
        /// that need no carrying, cannot be carried or find no room are left out (they stay behind).
        /// </summary>
        public static Dictionary<Ship, Ship> Stow(IEnumerable<Ship> carriers, IEnumerable<Ship> craft)
        {
            var free = new List<(Ship hull, int left)>();
            foreach (var c in carriers)
            {
                int n = BerthsOf(c);
                if (n > 0) free.Add((c, n));
            }
            var aboard = new Dictionary<Ship, Ship>();
            foreach (var s in craft)
            {
                if (!Carriable(s) || aboard.ContainsKey(s)) continue;
                int size = SizeOf(s.Spec.Class);
                for (int i = 0; i < free.Count; i++)
                {
                    if (free[i].left < size || free[i].hull == s) continue;
                    aboard[s] = free[i].hull;
                    free[i] = (free[i].hull, free[i].left - size);
                    break;
                }
            }
            return aboard;
        }
    }
}
