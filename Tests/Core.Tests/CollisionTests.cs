using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.SpaceGod.Core.Tests
{
    /// <summary>Collisions and ramming (U.SPC-T60): volumes, momentum exchange, and damage by kinetic energy to the struck side.</summary>
    [TestFixture]
    public class CollisionTests
    {
        static CombatSpec Spec(ShipClass c) => CombatSpec.Of(c);

        static CollisionVolume HullAt(ShipClass c, DVec3 pos, DVec3 vel, DVec3? forward = null) =>
            CollisionVolume.Hull(Spec(c), pos, vel, forward ?? DVec3.UnitX);

        static DVec3 Momentum(in CollisionVolume a, in CollisionVolume b) => a.Vel * a.MassKt + b.Vel * b.MassKt;

        static double Kinetic(in CollisionVolume a, in CollisionVolume b) => 0.5 * a.MassKt * a.Vel.LengthSquared + 0.5 * b.MassKt * b.Vel.LengthSquared;

        /// <summary>Two destroyers nose to nose, overlapping slightly, closing at <paramref name="closing"/> km/s.</summary>
        static (CollisionVolume a, CollisionVolume b) HeadOn(ShipClass ca, ShipClass cb, double closing)
        {
            var a = HullAt(ca, DVec3.Zero, new DVec3(closing * 0.5, 0, 0));
            double reach = Spec(ca).LengthKm * 0.5 + Spec(cb).LengthKm * 0.5;
            var b = HullAt(cb, new DVec3(reach - 0.001, 0, 0), new DVec3(-closing * 0.5, 0, 0), -DVec3.UnitX);
            return (a, b);
        }

        // ---------------------------------------------------------------- volumes

        [Test]
        public void A_hull_is_a_capsule_as_long_as_the_ship_so_its_tail_can_be_struck_as_well_as_its_middle()
        {
            var spec = Spec(ShipClass.Cruiser);
            var hull = HullAt(ShipClass.Cruiser, DVec3.Zero, DVec3.Zero);
            Assert.That(hull.HalfLengthKm + hull.RadiusKm, Is.EqualTo(spec.LengthKm * 0.5).Within(1e-12));
            Assert.That(hull.RadiusKm * 2, Is.EqualTo(spec.LengthKm * spec.BeamFraction).Within(1e-12));

            // A rock just behind the stern touches; the same distance off the beam does not (the hull is slender).
            double d = spec.LengthKm * 0.5 + 0.01;
            var astern = CollisionVolume.Sphere(new DVec3(-d + 0.02, 0, 0), DVec3.Zero, 0.015, 0);
            var abeam = CollisionVolume.Sphere(new DVec3(0, d - 0.02, 0), DVec3.Zero, 0.015, 0);
            Assert.That(Collisions.Contact(hull, astern, out var n, out _, out _), Is.True);
            Assert.That(n.X, Is.LessThan(-0.99), "the normal points from the hull out through its stern");
            Assert.That(Collisions.Contact(hull, abeam, out _, out _, out _), Is.False);
        }

        [Test]
        public void Volumes_apart_do_not_touch_and_two_side_by_side_hulls_touch_along_their_flanks()
        {
            var a = HullAt(ShipClass.Destroyer, DVec3.Zero, DVec3.Zero);
            var far = HullAt(ShipClass.Destroyer, new DVec3(0, 1, 0), DVec3.Zero);
            Assert.That(Collisions.Contact(a, far, out _, out _, out _), Is.False);

            var flank = HullAt(ShipClass.Destroyer, new DVec3(0.1, a.RadiusKm * 2 - 0.005, 0), DVec3.Zero);
            Assert.That(Collisions.Contact(a, flank, out var n, out double depth, out _), Is.True);
            Assert.That(n.Y, Is.EqualTo(1).Within(1e-9));
            Assert.That(depth, Is.EqualTo(0.005).Within(1e-9));
        }

        [Test]
        public void A_fast_pass_that_would_jump_clean_over_a_piece_of_debris_between_frames_is_still_caught()
        {
            // 4 km/s for a sixtieth of a second: 67 m, past a 20 m chunk with no overlap at either end.
            var start = new DVec3(-0.03, 0, 0);
            var move = new DVec3(4.0 / 60, 0, 0);
            double reach = 0.02;
            Assert.That((start).Length, Is.GreaterThan(reach));
            Assert.That((start + move).Length, Is.GreaterThan(reach));
            double t = Collisions.TimeOfImpact(start, move, reach);
            Assert.That(t, Is.GreaterThan(0).And.LessThan(1));
            Assert.That((start + move * t).Length, Is.EqualTo(reach).Within(1e-9));
            Assert.That(Collisions.TimeOfImpact(new DVec3(-0.03, 0.05, 0), move, reach), Is.EqualTo(-1), "a pass wide of it misses");
            Assert.That(Collisions.TimeOfImpact(new DVec3(0.03, 0, 0), move, reach), Is.EqualTo(-1), "moving away misses");
        }

        // ---------------------------------------------------------------- momentum

        [Test]
        public void A_ram_between_two_hulls_conserves_their_momentum_and_stops_them_closing()
        {
            var (a, b) = HeadOn(ShipClass.Destroyer, ShipClass.Freighter, 0.2);
            var before = Momentum(a, b);
            var hit = Collisions.Resolve(ref a, ref b);
            Assert.That(hit.Hit, Is.True);
            Assert.That(hit.ClosingKmS, Is.EqualTo(0.2).Within(1e-12));
            var after = Momentum(a, b);
            Assert.That((after - before).Length, Is.LessThan(1e-9 * before.Length + 1e-12));
            // They part at the restitution share of the speed they closed at.
            double parting = DVec3.Dot(b.Vel - a.Vel, hit.Normal);
            Assert.That(parting, Is.EqualTo(Collisions.Restitution * 0.2).Within(1e-12));
        }

        [Test]
        public void A_glancing_scrape_conserves_momentum_too_and_friction_drags_on_the_slide()
        {
            var a = HullAt(ShipClass.Cruiser, DVec3.Zero, new DVec3(0.14, 0.03, 0));
            var b = HullAt(ShipClass.Destroyer, new DVec3(0, a.RadiusKm + CollisionVolume.Hull(Spec(ShipClass.Destroyer), DVec3.Zero, DVec3.Zero, DVec3.UnitX).RadiusKm - 0.002, 0), new DVec3(-0.05, -0.01, 0));
            var before = Momentum(a, b);
            double slideBefore = (b.Vel - a.Vel).X;
            var hit = Collisions.Resolve(ref a, ref b);
            Assert.That(hit.Hit, Is.True);
            Assert.That((Momentum(a, b) - before).Length, Is.LessThan(1e-9 * before.Length));
            double slideAfter = (b.Vel - a.Vel).X;
            Assert.That(Math.Abs(slideAfter), Is.LessThan(Math.Abs(slideBefore)), "the scrape slows the slide");
            Assert.That(Math.Sign(slideAfter), Is.EqualTo(Math.Sign(slideBefore)), "friction drags; it never reverses the slide");
            Assert.That(hit.EnergyTJ, Is.GreaterThan(0));
        }

        [Test]
        public void The_lighter_hull_takes_the_bigger_change_of_velocity_in_proportion_to_the_masses()
        {
            var (a, b) = HeadOn(ShipClass.Courier, ShipClass.Battleship, 0.3);
            var va = a.Vel;
            var vb = b.Vel;
            Collisions.Resolve(ref a, ref b);
            double dvA = (a.Vel - va).Length, dvB = (b.Vel - vb).Length;
            Assert.That(dvA / dvB, Is.EqualTo(b.MassKt / a.MassKt).Within(1e-6 * b.MassKt / a.MassKt));
            Assert.That(a.Vel.X, Is.LessThan(0), "the courier bounces back off the battleship");
        }

        [Test]
        public void A_hull_rebounds_off_a_station_which_does_not_move_at_all()
        {
            var hull = HullAt(ShipClass.Freighter, DVec3.Zero, new DVec3(0.1, 0.02, 0));
            var stationAt = new DVec3(hull.HalfLengthKm + hull.RadiusKm + 1.99, 0, 0);
            var station = CollisionVolume.Sphere(stationAt, DVec3.Zero, 2, 0);
            var hit = Collisions.Resolve(ref hull, ref station);
            Assert.That(hit.Hit, Is.True);
            Assert.That(station.Vel, Is.EqualTo(DVec3.Zero));
            Assert.That(station.Pos, Is.EqualTo(stationAt));
            Assert.That(hull.Vel.X, Is.EqualTo(-Collisions.Restitution * 0.1).Within(1e-12));
            Assert.That(Collisions.Contact(hull, station, out _, out _, out _), Is.False, "pushed clear of it");
            // Against something immovable, the energy lost is the hull's own kinetic energy lost.
            double keBefore = 0.5 * hull.MassKt * (0.1 * 0.1 + 0.02 * 0.02);
            double keAfter = 0.5 * hull.MassKt * hull.Vel.LengthSquared;
            Assert.That(hit.EnergyTJ, Is.EqualTo(keBefore - keAfter).Within(1e-9 * keBefore));
        }

        [Test]
        public void A_fender_bump_under_the_safe_speed_does_not_bounce_and_bodies_already_parting_exchange_nothing()
        {
            var (a, b) = HeadOn(ShipClass.Destroyer, ShipClass.Destroyer, Collisions.SafeImpactKmS * 0.5);
            var hit = Collisions.Resolve(ref a, ref b);
            Assert.That(DVec3.Dot(b.Vel - a.Vel, hit.Normal), Is.EqualTo(0).Within(1e-15), "they come to rest together");
            Assert.That(Collisions.DamageHp(Spec(ShipClass.Destroyer), hit.EnergyShareTJ), Is.EqualTo(0));

            var (c, d) = HeadOn(ShipClass.Destroyer, ShipClass.Destroyer, -0.1);
            var vc = c.Vel;
            var parting = Collisions.Resolve(ref c, ref d);
            Assert.That(parting.Hit, Is.True);
            Assert.That(parting.EnergyTJ, Is.EqualTo(0));
            Assert.That(c.Vel, Is.EqualTo(vc));
        }

        [Test]
        public void A_pinned_body_is_left_on_its_track_while_the_free_one_is_pushed_all_the_way_clear()
        {
            var hull = HullAt(ShipClass.Courier, DVec3.Zero, new DVec3(0.2, 0, 0));
            var piece = CollisionVolume.Sphere(new DVec3(hull.HalfLengthKm + hull.RadiusKm + 0.009, 0, 0), new DVec3(-0.01, 0, 0), 0.01, 50);
            piece.Pinned = true;
            var pieceBefore = piece;
            var hit = Collisions.Resolve(ref hull, ref piece);
            Assert.That(hit.Hit, Is.True);
            Assert.That(piece.Pos, Is.EqualTo(pieceBefore.Pos));
            Assert.That(piece.Vel, Is.EqualTo(pieceBefore.Vel));
            Assert.That(Collisions.Contact(hull, piece, out _, out _, out _), Is.False);
            // The impulse still reckons with the piece's real mass: much heavier than a courier, so it bounces back.
            Assert.That(hull.Vel.X, Is.LessThan(0));
        }

        // ---------------------------------------------------------------- damage

        [Test]
        public void Impact_energy_goes_as_the_square_of_the_closing_speed_and_damage_with_the_energy()
        {
            var spec = Spec(ShipClass.Destroyer);
            double Energy(double v)
            {
                var (a, b) = HeadOn(ShipClass.Destroyer, ShipClass.Destroyer, v);
                return Collisions.Resolve(ref a, ref b).EnergyTJ;
            }
            double e1 = Energy(0.05), e2 = Energy(0.1), e4 = Energy(0.2);
            Assert.That(e2 / e1, Is.EqualTo(4).Within(1e-9));
            Assert.That(e4 / e2, Is.EqualTo(4).Within(1e-9));
            // Head on, frictionless: the relative kinetic energy (reduced mass) less the bounce it gives back.
            double mu = spec.MassKt * spec.MassKt / (2 * spec.MassKt);
            Assert.That(e2, Is.EqualTo(0.5 * mu * 0.01 * (1 - Collisions.Restitution * Collisions.Restitution)).Within(1e-12));

            // Damage is linear in the energy share past what the hull shrugs off.
            double d1 = Collisions.DamageHp(spec, e1 * 0.5), d2 = Collisions.DamageHp(spec, e2 * 0.5), d4 = Collisions.DamageHp(spec, e4 * 0.5);
            Assert.That(d1, Is.GreaterThan(0));
            Assert.That((d4 - d2) / (d2 - d1), Is.EqualTo((e4 - e2) / (e2 - e1)).Within(1e-9));
            Assert.That(Collisions.DamageHp(spec, 2 * e4) - Collisions.DamageHp(spec, e4), Is.EqualTo(spec.Hull * e4 / (spec.MassKt * Collisions.ToughnessTJPerKt)).Within(1e-6));
        }

        [Test]
        public void Ramming_wrecks_a_light_hull_and_barely_marks_a_heavy_one_though_both_absorb_the_same_energy()
        {
            var (a, b) = HeadOn(ShipClass.Courier, ShipClass.Battleship, 0.45);
            var hit = Collisions.Resolve(ref a, ref b);
            double courierShare = Collisions.DamageHp(Spec(ShipClass.Courier), hit.EnergyShareTJ) / Spec(ShipClass.Courier).Hull;
            double battleshipShare = Collisions.DamageHp(Spec(ShipClass.Battleship), hit.EnergyShareTJ) / Spec(ShipClass.Battleship).Hull;
            Assert.That(courierShare, Is.GreaterThan(0.4));
            Assert.That(battleshipShare, Is.LessThan(0.001));
        }

        [Test]
        public void A_heavy_hull_scraping_a_station_at_speed_loses_a_real_share_of_its_hull_and_a_slow_nudge_loses_nothing()
        {
            double HullLost(double speed)
            {
                var hull = HullAt(ShipClass.Freighter, DVec3.Zero, new DVec3(speed * 0.94, speed * 0.34, 0));
                var station = CollisionVolume.Sphere(new DVec3(0, hull.RadiusKm + 15 - 0.01, 0), DVec3.Zero, 15, 0);
                var hit = Collisions.Resolve(ref hull, ref station);
                Assert.That(hit.Hit, Is.True);
                var combat = new ShipCombat(Spec(ShipClass.Freighter), ShipClass.Freighter);
                combat.ZeroAllShields();
                Collisions.ApplyDamage(combat, new DVec3(0, 0.1, 0), Collisions.DamageHp(combat.Spec, hit.EnergyShareTJ));
                return 1 - combat.HullFraction;
            }
            Assert.That(HullLost(0.18), Is.GreaterThan(0.005));
            Assert.That(HullLost(0.004), Is.EqualTo(0));
        }

        [Test]
        public void An_impact_damages_the_hardpoints_on_the_struck_side_and_leaves_the_far_side_alone()
        {
            var combat = new ShipCombat(Spec(ShipClass.Destroyer), ShipClass.Destroyer);
            combat.ZeroAllShields();
            Subsystem Find(SubsystemKind k, Func<SubsystemDef, bool> where) => combat.Subsystems.Find(s => s.Kind == k && where(s.Def));
            var starboardShield = Find(SubsystemKind.ShieldGenerator, d => d.X > 0.05);
            var portShield = Find(SubsystemKind.ShieldGenerator, d => d.X < -0.05);
            var railgun = Find(SubsystemKind.Railgun, _ => true);

            // Struck on the starboard flank, amidships.
            double hullBefore = combat.Hull;
            Collisions.ApplyDamage(combat, new DVec3(0.1, 0, -0.1), 400);
            Assert.That(starboardShield.Hp, Is.LessThan(starboardShield.Def.MaxHp));
            Assert.That(portShield.Hp, Is.EqualTo(portShield.Def.MaxHp), "the port side was not struck");
            Assert.That(railgun.Hp, Is.EqualTo(railgun.Def.MaxHp), "the bow is out of reach");
            Assert.That(combat.Hull, Is.LessThan(hullBefore));

            // Rammed from astern: the engines take it, the railgun up front does not.
            var engines = combat.Subsystems.FindAll(s => s.Kind == SubsystemKind.Engine);
            Collisions.ApplyDamage(combat, new DVec3(0, 0, -0.5), 600);
            Expect.All(engines, e => Assert.That(e.Hp, Is.LessThan(e.Def.MaxHp)));
            Assert.That(railgun.Hp, Is.EqualTo(railgun.Def.MaxHp));
        }

        [Test]
        public void A_shield_on_the_struck_side_soaks_the_impact_first_and_a_lost_reactor_takes_the_ship()
        {
            var combat = new ShipCombat(Spec(ShipClass.Destroyer), ShipClass.Destroyer);
            var bowShield = combat.GeneratorFor(0);
            double charge = bowShield.Shield;
            Assert.That(Collisions.ApplyDamage(combat, new DVec3(0, 0, 0.5), charge * 0.5), Is.EqualTo(0));
            Assert.That(bowShield.Shield, Is.EqualTo(charge * 0.5).Within(1e-9));
            Assert.That(combat.Hull, Is.EqualTo(combat.Spec.Hull));

            combat.ZeroAllShields();
            var lost = new List<Subsystem>();
            Collisions.ApplyDamage(combat, new DVec3(0, 0, -0.25), 1e5, destroyed: lost);
            Assert.That(lost.Exists(s => s.Kind == SubsystemKind.Reactor), Is.True);
            Assert.That(combat.Alive, Is.False);
        }

        [Test]
        public void An_impact_with_no_hardpoint_in_reach_goes_wholly_to_the_hull()
        {
            var combat = new ShipCombat(Spec(ShipClass.Destroyer), ShipClass.Destroyer);
            combat.ZeroAllShields();
            var weights = Collisions.StruckSideWeights(combat, new DVec3(0, -0.1, 0.45));
            Assert.That(Array.TrueForAll(weights, w => w == 0), Is.True);
            Collisions.ApplyDamage(combat, new DVec3(0, -0.1, 0.45), 300);
            Assert.That(combat.Hull, Is.EqualTo(combat.Spec.Hull - 300).Within(1e-9));
        }

        [Test]
        public void Coming_down_hard_on_a_surface_does_damage_and_settling_gently_does_none()
        {
            var spec = Spec(ShipClass.Destroyer);
            var s = PilotState.At(new DVec3(0, 0, 99.9), new DVec3(0.05, 0, -0.3), DVec3.UnitX);
            Assert.That(PilotFlight.KeepAboveSurface(ref s, DVec3.Zero, 100, out double sink), Is.True);
            Assert.That(sink, Is.EqualTo(0.3).Within(1e-12));
            Assert.That(s.Vel.X, Is.EqualTo(0.05), "the sideways speed is kept");
            Assert.That(Collisions.DamageHp(spec, Collisions.SurfaceImpactShareTJ(spec.MassKt, sink)), Is.GreaterThan(spec.Hull * 0.1));
            Assert.That(Collisions.DamageHp(spec, Collisions.SurfaceImpactShareTJ(spec.MassKt, 0.004)), Is.EqualTo(0));
        }

        [Test]
        public void A_collision_in_battle_kills_through_the_battle_like_a_shot()
        {
            var blue = new Faction("Blue", "BLU", new Rgb(0, 0, 1)) { IsMilitary = true };
            var ship = new Ship { Id = 0, Name = "Courier0", Spec = ShipClassSpec.Of(ShipClass.Courier), Faction = blue, Combat = new ShipCombat(CombatSpec.Of(ShipClass.Courier), ShipClass.Courier) };
            var b = new Battle(0, 3, "Test", new DVec3(50000, 0, 0), Astro.J2000, 1);
            int destroyed = 0;
            b.UnitDestroyed = (_, __) => destroyed++;
            var unit = b.AddShip(ship, 0, DVec3.Zero, DVec3.UnitX);
            unit.Combat.ZeroAllShields();
            var at = unit.Pos + unit.Forward * (unit.LengthKm * 0.5);
            b.ApplyImpact(unit, at, 100);
            Assert.That(unit.Dead, Is.False);
            Assert.That(b.Fx.Exists(f => f.Kind == FxKind.Impact && f.Unit == unit), Is.True);
            b.ApplyImpact(unit, at, unit.Combat.Spec.Hull * 10);
            Assert.That(unit.Dead, Is.True);
            Assert.That(destroyed, Is.EqualTo(1));
        }

        [Test]
        public void A_ship_broken_up_in_a_collision_outside_battle_is_lost_and_leaves_a_wreck_to_salvage_where_it_struck()
        {
            const double jd = 2461330.5;
            var eph = new Ephemeris(BodyCatalog.CreateSolSystem());
            eph.Update(jd);
            var world = World.CreateDefault(eph, jd);
            var ship = world.Fleet.Ships.First(s => !s.Destroyed && s.Battle == null && !s.IsEscort && !s.Derelict);
            int body = 3;
            var at = new DVec3(42000, 10, -5);
            Assert.That(world.War.LoseInCollision(ship, body, at, jd, "a station"), Is.True);
            Assert.That(ship.Destroyed, Is.True);
            var wreck = world.War.Wrecks.Of(ship);
            Assert.That(wreck, Is.Not.Null);
            Assert.That(wreck.Body, Is.EqualTo(body));
            Assert.That((world.WreckHelio(wreck, jd) - (eph.HelioAt(body, jd) + at)).Length, Is.LessThan(1e-6));
            Assert.That(world.War.LoseInCollision(ship, body, at, jd, "a station"), Is.False, "only once");
        }

        [Test]
        public void An_asteroid_is_sized_from_its_brightness()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string path = null;
            for (; dir != null && path == null; dir = dir.Parent)
            {
                var p = Path.Combine(dir.FullName, "Assets", "UnitySpace", "Resources", "Data", "asteroids.bytes");
                if (File.Exists(p)) path = p;
            }
            Assert.That(path, Is.Not.Null);
            var field = AsteroidField.Parse(File.ReadAllBytes(path));
            double d = field.DiameterKm(0);
            Assert.That(d, Is.EqualTo(1329 / Math.Sqrt(AsteroidField.TypicalAlbedo) * Math.Pow(10, -field.H[0] / 5.0)).Within(1e-9));
            Assert.That(field.DiameterKm(field.Count - 1), Is.LessThan(d), "sorted brightest (largest) first");
            Assert.That(field.DiameterKm(field.Count - 1), Is.GreaterThan(1).And.LessThan(10), "the faintest are a few km across");
        }
    }
}
