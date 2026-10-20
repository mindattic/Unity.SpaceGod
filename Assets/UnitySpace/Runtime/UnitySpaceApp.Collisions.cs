using System;
using System.Collections.Generic;
using System.Globalization;
using UnitySpace.Core;
using UnityEngine;

namespace UnitySpace
{
    /// <summary>
    /// Collisions for the piloted hull (U.SPC-T60): every frame it flies, its volume is tested against the
    /// other hulls in its battle (or, out of one, every ship near it), the debris of wrecks, stations and
    /// structures in orbit, and the real asteroids of the belt, and a contact is resolved by
    /// <see cref="Collisions.Resolve"/>: momentum exchanged (a battle unit is knocked off its line; a ship on
    /// its schedule, a structure, a rock and a drifting debris field hold their tracks) and the energy the
    /// crumpling took done as damage to the struck side of each hull. A hull destroyed so leaves a wreck.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>Real seconds a contact's toast stays quiet for before the same thing can toast again.</summary>
        const float ContactToastIntervalS = 1.5f;

        readonly Dictionary<object, float> contactToastAt = new Dictionary<object, float>();

        /// <summary>The belt is scanned a slice a frame, over this many frames, for rocks near the hull.</summary>
        const int BeltScanFrames = 30;

        /// <summary>A belt asteroid this near the hull (km) is tested every frame until the next scan.</summary>
        const double BeltCandidateKm = 3000;

        List<int> beltCandidates = new List<int>(), beltScanning = new List<int>();
        float[] beltXyz;
        int beltCursor;

        /// <summary>Finite-difference step for velocities, days.</summary>
        const double VelocityStepDays = 1e-5;

        /// <summary>
        /// Tests and resolves the piloted hull's contacts this frame (it has just moved from
        /// <paramref name="posBefore"/>). Returns false if the hull was lost in one, the session already ended.
        /// </summary>
        bool UpdatePilotCollisions(Ship ship, DVec3 posBefore, float dt)
        {
            // Riding a hulk on the clamps is not flying into it.
            if (Salvage != null) return true;
            double jd = Clock.Jd;
            var frameHelio = Ephemeris.HelioAt(pilotFrameBody, jd);
            var frameVel = FrameVelocityKmS(pilotFrameBody, jd);
            var spec = ship.Combat.Spec;
            double pad = (pilot.Pos - posBefore).Length + 1;
            var me = CollisionVolume.Hull(spec, pilot.Pos, pilot.Vel, pilot.Forward);
            var battle = ship.Battle;
            var mine = battle?.UnitOf(ship);

            bool Near(DVec3 pos, double radius) => (pos - me.Pos).Length <= me.BoundingRadiusKm + radius + pad;

            if (battle != null)
            {
                foreach (var u in battle.Units)
                {
                    if (u == mine || !u.Present) continue;
                    var pos = battle.Anchor + u.Pos;
                    if (!Near(pos, u.LengthKm * 0.5)) continue;
                    var other = CollisionVolume.Hull(u.Combat.Spec, pos, u.Vel, u.Forward);
                    if (u.Kind == UnitKind.Platform) other.MassKt = 0;
                    var hit = Collide(ref me, ref other, posBefore, dt);
                    if (!hit.Hit) continue;
                    if (u.Kind != UnitKind.Platform)
                    {
                        u.Pos = other.Pos - battle.Anchor;
                        u.Vel = other.Vel;
                    }
                    if (hit.EnergyTJ <= 0) continue;
                    battle.ApplyImpact(u, hit.Point - battle.Anchor, Collisions.DamageHp(u.Combat.Spec, hit.EnergyShareTJ));
                    if (!Struck(ship, ref me, hit, u, u.Kind == UnitKind.Fighter ? "a fighter" : "the " + u.Name)) return false;
                }
            }
            else
            {
                foreach (var s in Fleet.Ships)
                {
                    // Not an escort: the fleet places it at its leader (its slot is only drawn), so it would be struck
                    // as a second hull stacked on its leader's centre rather than where it is seen.
                    if (s == ship || s.Destroyed || s.Battle != null || s.Planetside || s.Combat == null || s.IsEscort) continue;
                    bool derelict = s.Derelict && s.Orbit != null;
                    Func<double, DVec3> helioAt = derelict ? j => DerelictHelio(Ephemeris, s, j) : (Func<double, DVec3>)(j => Fleet.StateOf(s, j).HelioKm);
                    var pos = helioAt(jd) - frameHelio;
                    if (!Near(pos, s.LengthKm * 0.5)) continue;
                    var vel = VelocityKmS(helioAt, jd) - frameVel;
                    var facing = Fleet.StateOf(s, jd).Facing;
                    var other = CollisionVolume.Hull(s.Combat.Spec, pos, vel, facing);
                    other.Pinned = true; // on its schedule (or adrift where it was left): it holds its track
                    var hit = Collide(ref me, ref other, posBefore, dt);
                    if (!hit.Hit || hit.EnergyTJ <= 0) continue;
                    var local = Collisions.ToLocal(hit.Point, other.Pos, other.Axis, PilotState.LevelUp(other.Axis), s.LengthKm);
                    Collisions.ApplyDamage(s.Combat, local, Collisions.DamageHp(s.Combat.Spec, hit.EnergyShareTJ));
                    if (!s.Combat.Alive && World.War.LoseInCollision(s, pilotFrameBody, other.Pos, jd, "the " + ship.Name))
                        ShowToast("The " + s.Name + " breaks up under the impact");
                    if (!Struck(ship, ref me, hit, s, (derelict ? "the derelict " : "the ") + s.Name)) return false;
                }
            }

            // Wreckage: every piece of a wreck's drifting debris field.
            foreach (var w in World.War.Wrecks.Items)
            {
                var centre = World.WreckHelio(w, jd) - frameHelio;
                if (!Near(centre, w.DebrisRadiusKm(jd) + w.LengthKm)) continue;
                if (w.DebrisPieceRadiusKm(jd) <= 0) continue;
                var vel = VelocityKmS(j => World.WreckHelio(w, j), jd) - frameVel;
                for (int k = 0; k < w.DebrisPieces; k++)
                {
                    var piece = w.DebrisCollider(k, jd, centre, vel);
                    var hit = Collide(ref me, ref piece, posBefore, dt);
                    if (!hit.Hit || hit.EnergyTJ <= 0) continue;
                    if (!Struck(ship, ref me, hit, w, "debris from the wreck of " + w.Name)) return false;
                }
            }

            // Stations, yards and spacecraft in orbit: immovable, as far as a hull is concerned.
            foreach (var st in World.Infrastructure.Structures)
            {
                var pos = st.HelioAt(Ephemeris, jd) - frameHelio;
                double r = Math.Max(0.01, st.LengthKm * 0.5);
                if (!Near(pos, r)) continue;
                var other = CollisionVolume.Sphere(pos, VelocityKmS(j => st.HelioAt(Ephemeris, j), jd) - frameVel, r, 0);
                var hit = Collide(ref me, ref other, posBefore, dt);
                if (!hit.Hit || hit.EnergyTJ <= 0) continue;
                if (!Struck(ship, ref me, hit, st, st.Name)) return false;
            }

            // The belt's real rocks (Sol).
            var field = IsSol ? AsteroidBelt?.Field : null;
            ScanBelt(field, frameHelio + me.Pos, jd);
            if (field != null)
            {
                var sun = Ephemeris.HelioAt(0, jd);
                foreach (int i in beltCandidates)
                {
                    var pos = sun + field.PositionAu(i, jd) * Astro.AuKm - frameHelio;
                    double r = field.DiameterKm(i) * 0.5;
                    if (!Near(pos, r)) continue;
                    var vel = VelocityKmS(j => Ephemeris.HelioAt(0, j) + field.PositionAu(i, j) * Astro.AuKm, jd) - frameVel;
                    var rock = CollisionVolume.Sphere(pos, vel, r, 0);
                    var hit = Collide(ref me, ref rock, posBefore, dt);
                    if (!hit.Hit || hit.EnergyTJ <= 0) continue;
                    if (!Struck(ship, ref me, hit, field.Number[i], "the asteroid (" + field.Number[i] + ")")) return false;
                }
            }

            pilot.Pos = me.Pos;
            pilot.Vel = me.Vel;
            return true;
        }

        /// <summary>
        /// A contact, caught even on a fast pass: the volumes are tested where they end the frame and, if they
        /// are apart there but their bounding spheres crossed on the way (<see cref="Collisions.TimeOfImpact"/>),
        /// at steps from first touch on, so a hull at cruise speed cannot slip through a rock between frames.
        /// </summary>
        static Impact Collide(ref CollisionVolume me, ref CollisionVolume other, DVec3 meBefore, double dt)
        {
            var hit = Collisions.Resolve(ref me, ref other);
            if (hit.Hit) return hit;
            var otherBefore = other.Pos - other.Vel * dt;
            var relStart = otherBefore - meBefore;
            var move = (other.Pos - me.Pos) - relStart;
            if (move.Length < Math.Min(me.RadiusKm, other.RadiusKm)) return hit;
            double t = Collisions.TimeOfImpact(relStart, move, me.BoundingRadiusKm + other.BoundingRadiusKm);
            if (t < 0) return hit;
            DVec3 meEnd = me.Pos, otherEnd = other.Pos;
            const int steps = 8;
            for (int k = 0; k <= steps; k++)
            {
                double s = t + (1 - t) * k / steps;
                me.Pos = DVec3.Lerp(meBefore, meEnd, s);
                other.Pos = DVec3.Lerp(otherBefore, otherEnd, s);
                if (Collisions.Contact(me, other, out _, out _, out _)) return Collisions.Resolve(ref me, ref other);
            }
            me.Pos = meEnd;
            other.Pos = otherEnd;
            return hit;
        }

        /// <summary>
        /// The piloted hull's share of an impact: the damage done to its struck side (through its battle if it
        /// is in one, so a kill there is the battle's), a toast, and the hull lost if that was the end of it.
        /// Returns false if it was.
        /// </summary>
        bool Struck(Ship ship, ref CollisionVolume me, in Impact hit, object what, string name)
        {
            pilot.Pos = me.Pos;
            pilot.Vel = me.Vel;
            var spec = ship.Combat.Spec;
            double hp = Collisions.DamageHp(spec, hit.EnergyShareTJ);
            double before = ship.Combat.HullFraction;
            var battle = ship.Battle;
            var unit = battle?.UnitOf(ship);
            if (unit != null)
            {
                unit.Pos = pilot.Pos - battle.Anchor;
                unit.Vel = pilot.Vel;
                unit.Forward = pilot.Forward;
                battle.ApplyImpact(unit, hit.Point - battle.Anchor, hp);
            }
            else
            {
                var local = Collisions.ToLocal(hit.Point, pilot.Pos, pilot.Forward, pilot.Up, ship.LengthKm);
                Collisions.ApplyDamage(ship.Combat, local, hp);
                if (!ship.Combat.Alive) World.War.LoseInCollision(ship, pilotFrameBody, pilot.Pos, Clock.Jd, name);
            }
            if (ship.Destroyed || !ship.Combat.Alive)
            {
                ExitPiloting();
                ShowToast("The " + ship.Name + " breaks up in a collision with " + name + " at " + Speed(hit.ClosingKmS));
                return false;
            }
            float now = Time.unscaledTime;
            if (!contactToastAt.TryGetValue(what, out float at) || now - at > ContactToastIntervalS)
            {
                contactToastAt[what] = now;
                double lost = before - ship.Combat.HullFraction;
                ShowToast("COLLISION with " + name + " at " + Speed(hit.ClosingKmS) +
                    (lost > 0.0005 ? ": hull " + Mathf.RoundToInt((float)(ship.Combat.HullFraction * 100)) + "%" : hp > 0 ? ": shields took it" : ""));
            }
            return true;
        }

        /// <summary>A hull that comes down hard on a world (or a big station, or one of the belt's own worlds)
        /// takes the damage of the sink speed it lost there on its underside. Returns false if it was lost.</summary>
        bool SurfaceImpact(Ship ship, int body, DVec3 outward, double sinkKmS)
        {
            var spec = ship.Combat.Spec;
            double hp = Collisions.DamageHp(spec, Collisions.SurfaceImpactShareTJ(Collisions.MassOf(spec), sinkKmS));
            if (hp <= 0) return true;
            var down = Collisions.ToLocal(pilot.Pos - outward, pilot.Pos, pilot.Forward, pilot.Up, 1);
            var impact = new Impact { Hit = true, ClosingKmS = sinkKmS, Normal = -outward };
            var local = Collisions.SurfacePoint(spec, down);
            impact.Point = pilot.Pos + (Collisions.ToWorld(local, pilot.Forward, pilot.Up) * ship.LengthKm);
            // Its whole energy share is the hull's (the world takes the rest): reuse the struck-hull path.
            impact.EnergyTJ = Collisions.SurfaceImpactShareTJ(Collisions.MassOf(spec), sinkKmS) * 2;
            var me = CollisionVolume.Hull(spec, pilot.Pos, pilot.Vel, pilot.Forward);
            return Struck(ship, ref me, impact, Catalog[body], Catalog[body].Name);
        }

        /// <summary>Scans a slice of the belt this frame for rocks within <see cref="BeltCandidateKm"/> of the hull;
        /// a full pass replaces the list tested every frame.</summary>
        void ScanBelt(AsteroidField field, DVec3 hullHelio, double jd)
        {
            double au = hullHelio.Length / Astro.AuKm;
            if (field == null || au < 1.5 || au > 6)
            {
                beltCandidates.Clear();
                beltScanning.Clear();
                beltCursor = 0;
                return;
            }
            if (beltXyz == null || beltXyz.Length < field.Count * 3) beltXyz = new float[field.Count * 3];
            int per = (field.Count + BeltScanFrames - 1) / BeltScanFrames;
            int end = Math.Min(field.Count, beltCursor + per);
            field.PositionsAu(jd, beltCursor, per, beltXyz);
            var sun = Ephemeris.HelioAt(0, jd);
            var p = (hullHelio - sun) / Astro.AuKm;
            double limit = BeltCandidateKm / Astro.AuKm, limit2 = limit * limit;
            for (int i = beltCursor; i < end; i++)
            {
                double dx = beltXyz[i * 3] - p.X, dy = beltXyz[i * 3 + 1] - p.Y, dz = beltXyz[i * 3 + 2] - p.Z;
                if (dx * dx + dy * dy + dz * dz < limit2) beltScanning.Add(i);
            }
            beltCursor = end;
            if (beltCursor < field.Count) return;
            (beltCandidates, beltScanning) = (beltScanning, beltCandidates);
            beltScanning.Clear();
            beltCursor = 0;
        }

        /// <summary>A body's own velocity, heliocentric km/s.</summary>
        DVec3 FrameVelocityKmS(int body, double jd) => VelocityKmS(j => Ephemeris.HelioAt(body, j), jd);

        static DVec3 VelocityKmS(Func<double, DVec3> helioAt, double jd) =>
            (helioAt(jd + VelocityStepDays) - helioAt(jd - VelocityStepDays)) / (2 * VelocityStepDays * Astro.SecondsPerDay);

        static string Speed(double kmS) =>
            kmS >= 1 ? (kmS).ToString("0.0", CultureInfo.InvariantCulture) + " km/s" : (kmS * 1000).ToString("0", CultureInfo.InvariantCulture) + " m/s";
    }
}
