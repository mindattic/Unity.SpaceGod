namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// A ship's helm as an <see cref="IEmbodiable"/> (U.SPC-T18): the existing ship piloting behind the common
    /// interface, unchanged. Who may take it, where it starts and the take-charge/hand-back bookkeeping
    /// (<see cref="World.PilotedShip"/>, an escort cut loose, the war's command handed over, a fighting hull's
    /// <see cref="BattleUnit"/> passed between player and crew) live here; the hull's flight is still stepped
    /// by the Runtime pilot loop (<see cref="PilotFlight.Step"/>) until per-class flight models settle (U.SPC-T17).
    /// </summary>
    public sealed class ShipHelm : IEmbodiable
    {
        readonly World world;

        public Ship Ship { get; }

        public ShipHelm(World world, Ship ship)
        {
            this.world = world;
            Ship = ship;
        }

        public string Name => Ship.Name;
        public EmbodimentKind Kind => EmbodimentKind.Ship;
        public Faction Faction => Ship.Faction;
        public IEmbodiable Host => null;
        public string Blocker => BlockerOf(Ship);
        public bool CanEmbody => Blocker == null;
        public bool Embodied => world.PilotedShip == Ship && Ship != null;

        /// <summary>The hull's place in the fight it is in, if any.</summary>
        BattleUnit Unit => Ship.Battle?.UnitOf(Ship);

        /// <summary>
        /// Why <paramref name="ship"/>'s helm cannot be taken right now, or null if it can: only a wreck, a
        /// derelict, or a prize already taken in a fight still on (its captors' to dispose of when it ends).
        /// </summary>
        public static string BlockerOf(Ship ship)
        {
            if (ship == null) return "nothing there";
            if (ship.Destroyed) return "destroyed";
            if (ship.Derelict) return "derelict";
            if (ship.Battle?.UnitOf(ship)?.Captured ?? false) return "taken as a prize";
            return null;
        }

        /// <summary>
        /// The body whose frame best describes a ship's current position, and its offset there in true km --
        /// from wherever it actually is (parked, mid-lane, mid-burn, queued, landed), not only a parked orbit.
        /// </summary>
        public static (int body, DVec3 offsetKm) FrameOf(World world, Ship ship, double jd)
        {
            // An escort has no schedule of its own (no orbit, no phase): it is wherever its leader is.
            var c = ship.Controller;
            var st = world.Fleet.StateOf(c, jd);
            if (c.Phase == ShipPhase.Parked)
                return (c.Orbit.Body, c.Orbit.OffsetAt(jd));
            if ((c.Phase == ShipPhase.Queued || c.Planetside) && c.Ring != null)
                return (c.Ring.Body, st.HelioKm - world.Ephemeris.Helio(c.Ring.Body));
            return (c.FrameBody, st.HelioKm - world.Ephemeris.Helio(c.FrameBody));
        }

        public EmbodimentPose PoseAt(double jd)
        {
            var unit = Unit;
            if (unit != null)
            {
                unit.Basis(out _, out var up);
                return new EmbodimentPose(Ship.Battle.Body, unit.Pos + Ship.Battle.Anchor, unit.Forward, up);
            }
            var (body, offset) = FrameOf(world, Ship, jd);
            var f = world.Fleet.StateOf(Ship, jd).Facing;
            f = f.LengthSquared > 1e-18 ? f.Normalized : DVec3.UnitX;
            return new EmbodimentPose(body, offset, f, PilotState.LevelUp(f));
        }

        /// <summary>
        /// The player takes the helm: the fleet stops scheduling the hull, an escort is cut loose from its
        /// formation (the war's command handed over otherwise), and a hull already fighting has its own
        /// <see cref="BattleUnit"/> handed to the player rather than a new fight started.
        /// </summary>
        public void TakeCharge()
        {
            world.PilotedShip = Ship;
            if (Ship.IsEscort) Ship.Leader = null;
            else world.War?.HandOverCommand(Ship);
            var unit = Unit;
            if (unit != null)
            {
                unit.PlayerControlled = true;
                unit.PlayerFireHeld = false;
                unit.PlayerLockTarget = null;
            }
        }

        /// <summary>
        /// The trigger and the crosshair go to the hull's battle unit. Its flight is the Runtime pilot loop's
        /// (see the class summary).
        /// </summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            var unit = Unit;
            if (unit == null || !unit.PlayerControlled) return;
            unit.PlayerFireHeld = input.Primary;
            unit.PlayerSecondaryHeld = input.Secondary;
            unit.PlayerAim = input.Aim;
        }

        /// <summary>
        /// The crew takes the helm back: a fighting hull's own AI (broadsides, point defence, missiles) picks
        /// up where manual control left off, same facing and velocity. Pinning it where it was left and
        /// rescheduling it are the Runtime's, which knows the pilot's last spot.
        /// </summary>
        public void HandBack()
        {
            if (world.PilotedShip == Ship) world.PilotedShip = null;
            if (Ship.Destroyed) return;
            var unit = Unit;
            if (unit != null)
            {
                unit.PlayerControlled = false;
                unit.PlayerFireHeld = false;
                unit.PlayerLockTarget = null;
            }
        }

        public override bool Equals(object obj) => obj is ShipHelm h && h.Ship == Ship;
        public override int GetHashCode() => Ship?.GetHashCode() ?? 0;
    }
}
